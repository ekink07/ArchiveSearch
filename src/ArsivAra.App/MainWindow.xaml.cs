using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ArsivAra.Core;
using Microsoft.Win32;

namespace ArsivAra.App;

/// <summary>Kenar çubuğundaki bir arşiv satırı.</summary>
public sealed record ArchiveItem(string Path, string Name, string Detail, string ToolTipText, ImageSource? Icon,
    bool NeedsPassword, string RemoveLabel);

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand TogglePreviewCommand = new("Preview", nameof(TogglePreviewCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ExtractCommand = new("Extract", nameof(ExtractCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveSearchCommand = new("SaveSearch", nameof(SaveSearchCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SettingsCommand = new("Settings", nameof(SettingsCommand), typeof(MainWindow));

    // Ctrl+tekerlek basamakları (px). 16–32: liste, 48 ve üstü: kareli görünüm (Everything gibi).
    static readonly int[] ZoomSteps = [16, 20, 24, 32, 48, 64, 96, 128, 160, 192, 256];
    const int FirstGridSize = 48;

    readonly Settings _settings;
    readonly Library _library;
    readonly ThumbnailService _thumbnails = new();
    readonly Progress<Library.AddProgress> _progress;
    readonly string[] _initialFiles;
    readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    CancellationTokenSource? _searchCts;
    string? _selectedArchive;
    int _busy;            // süren ekleme/tarama/çıkarma sayısı
    int _refreshPending;
    string? _notice;      // son bilgi (canlı değişiklik, tarama sonucu); kütüphane durumunun yanında görünür
    bool _settingQuery;
    string? _versionsOf;  // "tüm sürümleri" araması sürüyor
    bool _dragOutActive;

    // Ekrandaki sonuçlar ve seçim (iki görünüm aynı seçimi paylaşır).
    SearchIndex _shownIndex = SearchIndex.Empty;
    int[] _shownHits = [];
    int _iconSize;
    int _lastListSize = 16, _lastGridSize = 96;
    int _gridSelected = -1;
    int _gridPerRow;
    readonly Dictionary<int, TileVM> _tiles = new();
    SortKey _sortKey;
    bool _sortDescending;

    // Explorer'a sürükleme
    Point _dragStart;
    bool _dragCandidate;
    ResultRow? _pendingSingleSelect;

    bool IsGrid => _iconSize >= FirstGridSize;
    static System.Globalization.CultureInfo C => Loc.I.Culture;

    public MainWindow(Settings settings, string[] initialFiles)
    {
        _settings = settings;
        _initialFiles = initialFiles;
        _library = new Library(settings);
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(Loc.I.Culture.IetfLanguageTag);

        _progress = new Progress<Library.AddProgress>(ShowProgress);
        _library.Changed += ScheduleRefresh;
        _library.Notice += message => Dispatcher.InvokeAsync(() => ShowNotice(message));
        _library.RescanNeeded += () => Dispatcher.InvokeAsync(StartScan);
        Loc.I.Changed += OnLanguageChanged;

        _iconSize = ZoomSteps.Contains(settings.IconSize) ? settings.IconSize : 16;
        if (IsGrid) _lastGridSize = _iconSize; else _lastListSize = _iconSize;
        _sortKey = Enum.TryParse<SortKey>(settings.SortKey, out var key) ? key : SortKey.None;
        _sortDescending = settings.SortDescending;
        UpdateViewButtons();
        UpdateSortHeaders();
        SetPreviewVisible(settings.ShowPreview, save: false);

        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };
        Preview.OpenRequested += OpenSelected;
        Preview.ExtractRequested += () => ExtractSelected_Click(this, new RoutedEventArgs());

        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            Refresh();
            var (status, backup) = await _library.LoadAsync(_progress);
            HideProgress();
            if (status == IndexFile.LoadStatus.Migrated) ShowNotice(Loc.F("StatusIndexMigrated", backup));
            else if (status == IndexFile.LoadStatus.Unreadable) ShowNotice(Loc.F("StatusIndexUnreadable", backup ?? "—"));
            UpdateLibraryStatus();

            // Program simgesinin üzerine bırakılan ya da "Birlikte aç" ile verilen dosyalar.
            var files = _initialFiles.Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
            if (files.Count > 0) await Add(files);
            if (_settings.ScanDisks) StartScan();
        };
        Closing += (_, _) => SaveSettings();
        Closed += (_, _) =>
        {
            Loc.I.Changed -= OnLanguageChanged;
            _searchCts?.Cancel();
            _thumbnails.Dispose();
            Preview.DisposeReader();
            _library.Dispose();
        };
    }

    void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log.Warn("Ayarlar kaydedilemedi", ex); }
    }

    /// <summary>Program açıkken exe'ye bırakılan dosyalar (ikinci örnekten gelir).</summary>
    public async void ReceiveFromOtherInstance(string[] files)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        var existing = files.Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
        if (existing.Count > 0) await Add(existing);
    }

    void OnLanguageChanged()
    {
        Language = XmlLanguage.GetLanguage(Loc.I.Culture.IetfLanguageTag);
        UpdateSortHeaders();
        Refresh();
        UpdatePreview();
    }

    // ---- Kütüphane görünümü ----

    void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
        Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            Refresh(fromLibrary: true);
        }, DispatcherPriority.Background);
    }

    void Refresh(bool fromLibrary = false)
    {
        var index = _library.Current;
        var items = index.TopLevel.Select(ToItem).ToList();
        ArchiveList.SelectionChanged -= ArchiveList_SelectionChanged;
        ArchiveList.ItemsSource = items;
        ArchiveList.SelectedItem = items.FirstOrDefault(i => i.Path == _selectedArchive);
        if (ArchiveList.SelectedItem == null) _selectedArchive = null;
        ArchiveList.SelectionChanged += ArchiveList_SelectionChanged;

        ArchivesHeader.Text = items.Count == 0 ? Loc.T("Archives") : Loc.F("ArchivesCount", items.Count.ToString("N0", C));
        bool empty = items.Count == 0 && !_library.IsScanning;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Sidebar.Visibility = ResultsPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ApplyPreviewLayout(empty);
        RefreshSavedSearches();
        if (_busy == 0) UpdateLibraryStatus();
        RunSearch(keepPosition: fromLibrary);
    }

    ArchiveItem ToItem(ArchiveRecord a)
    {
        string name = Path.GetFileName(a.Path);
        string detail;
        if (a.NeedsPassword) detail = "🔒 " + Loc.T("ArchiveEncrypted");
        else if (a.Error != null)
            detail = "⚠ " + a.ErrorKind switch
            {
                ArchiveError.AccessDenied => Loc.T("ArchiveAccessDenied"),
                ArchiveError.InUse => Loc.T("ArchiveInUse"),
                _ => Loc.F("ArchiveUnreadable", a.Error),
            };
        else if (a.Manual && !File.Exists(a.Path)) detail = "⚠ " + Loc.T("ArchiveMissing");
        else
        {
            detail = Loc.F("ArchiveFiles", a.TotalEntryCount.ToString("N0", C), Format.Size(a.FileSize));
            if (a.Nested.Length > 0) detail += " · " + Loc.F("ArchiveNested", a.Nested.Length);
            if (!a.Manual) detail += " · " + Loc.T("ArchiveScanned");
        }
        string tip = a.Error != null ? $"{a.Path}\n{a.Error}" : a.Path;
        return new ArchiveItem(a.Path, name, detail, tip, IconProvider.For(name, false, 32), a.NeedsPassword, Loc.F("RemoveNamed", name));
    }

    void UpdateLibraryStatus()
    {
        var index = _library.Current;
        string text = index.TopLevel.Count == 0
            ? Loc.T("StatusNoArchives")
            : Loc.F("StatusLibrary", index.TopLevel.Count.ToString("N0", C), index.EntryCount.ToString("N0", C));
        if (_notice != null) text += "  ·  " + _notice;
        LibraryStatus.Text = text;
    }

    void ShowNotice(string message)
    {
        _notice = message;
        if (_busy == 0) UpdateLibraryStatus();
        else LibraryStatus.Text = message;
    }

    // ---- İlerleme ----

    void ShowProgress(Library.AddProgress p)
    {
        AddProgressPanel.Visibility = Visibility.Visible;
        LibraryStatus.Text = p.Message;

        // Kaç arşiv olduğu bilinmiyorsa ya da tek bir (belki çok büyük) arşiv okunuyorsa süre tahmin edilemez.
        bool unknown = p.Total < 0 || (p.Total == 1 && p.Done == 0);
        AddProgressBar.IsIndeterminate = unknown;
        Taskbar.ProgressState = unknown ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate
                                        : System.Windows.Shell.TaskbarItemProgressState.Normal;
        if (unknown)
        {
            AddProgressText.Text = p.Done > 0 ? p.Done.ToString("N0", C) : "";
            return;
        }
        double value = p.Total == 0 ? 1 : (double)p.Done / p.Total;
        AddProgressBar.Value = value;
        Taskbar.ProgressValue = value;
        AddProgressText.Text = $"{p.Done.ToString("N0", C)} / {p.Total.ToString("N0", C)}";
    }

    void HideProgress()
    {
        if (_busy > 0) return;
        AddProgressPanel.Visibility = Visibility.Collapsed;
        AddProgressBar.IsIndeterminate = false;
        Taskbar.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
    }

    async Task RunBusy(Func<Task> work)
    {
        Interlocked.Increment(ref _busy);
        try { await work(); }
        finally
        {
            Interlocked.Decrement(ref _busy);
            HideProgress();
            UpdateLibraryStatus();
        }
    }

    // ---- Arama ----

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_settingQuery) _versionsOf = null;
        UpdateSaveSearchGlyph();
        RunSearch();
    }

    void SetQuery(string query, string? versionsOf = null)
    {
        _settingQuery = true;
        _versionsOf = versionsOf;
        SearchBox.Text = query;
        SearchBox.CaretIndex = query.Length;
        _settingQuery = false;
        RunSearch();
    }

    async void RunSearch(bool keepPosition = false)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;
        var index = _library.Current;
        string query = SearchBox.Text;
        string? onlyArchive = _selectedArchive;
        var sortKey = _sortKey;
        bool descending = _sortDescending;

        try
        {
            var r = await Task.Run(() => index.Search(query, ct, onlyArchive != null ? index.ScopeOf(onlyArchive) : null), ct);
            if (ct.IsCancellationRequested) return;
            if (r.Error != QueryErrorKind.None)
            {
                // Yazılmakta olan sorgu ("boyut:>") henüz geçersiz: önceki sonuçlar kalsın, hata durum satırında görünsün.
                ResultStatus.Text = Loc.F(r.Error switch
                {
                    QueryErrorKind.Size => "QueryErrorSize",
                    QueryErrorKind.Date => "QueryErrorDate",
                    _ => "QueryErrorRegex",
                }, r.ErrorDetail);
                return;
            }

            int[] hits = r.Hits;
            string? status = null;
            if (r.ContentTerms is { Length: > 0 } terms)
            {
                await Task.Delay(350, ct); // yazmaya devam ediliyorsa pahalı içerik aramasına başlama
                var sw = Stopwatch.StartNew();
                ResultStatus.Text = Loc.F("StatusContent", 0, hits.Length.ToString("N0", C));
                var progress = new Progress<(int Done, int Total)>(p =>
                {
                    if (!ct.IsCancellationRequested) ResultStatus.Text = Loc.F("StatusContent", p.Done.ToString("N0", C), p.Total.ToString("N0", C));
                });
                var found = await Task.Run(() => ContentSearch.Filter(index, hits, terms, PasswordStore.For, progress, ct), ct);
                hits = found.Hits;
                status = Loc.F("StatusContentDone", hits.Length.ToString("N0", C), found.Searched.ToString("N0", C),
                    (sw.Elapsed + r.Elapsed).TotalMilliseconds.ToString("0", C));
                if (found.Skipped > 0) status += " · " + Loc.F("StatusContentSkipped", found.Skipped.ToString("N0", C));
            }
            if (sortKey != SortKey.None || descending) hits = await Task.Run(() => index.Sort(hits, sortKey, descending), ct);
            if (ct.IsCancellationRequested) return;

            _shownIndex = index;
            _shownHits = hits;
            if (!keepPosition) _gridSelected = -1;
            ShowResults(scrollToTop: !keepPosition);

            status ??= Loc.F("StatusResults", hits.Length.ToString("N0", C), r.Elapsed.TotalMilliseconds.ToString("0.0", C));
            if (_versionsOf != null && hits.Length > 0)
            {
                var (archive, newest) = index.Get(hits[0]);
                status = Loc.F("StatusVersions", hits.Length, $"{archive.DisplayName} ({Format.Date(newest.MtimeUtcTicks)})");
            }
            if (onlyArchive != null) status += "  ·  " + Loc.F("StatusScope", Path.GetFileName(onlyArchive));
            ResultStatus.Text = index.EntryCount == 0 ? "" : status;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Mevcut sonuçları seçili görünümde (liste ya da kareler) gösterir.</summary>
    void ShowResults(bool scrollToTop)
    {
        _thumbnails.CancelPending();
        if (IsGrid)
        {
            ListHeader.Visibility = Visibility.Collapsed;
            Results.Visibility = Visibility.Collapsed;
            Results.ItemsSource = null;
            ResultsGrid.Visibility = Visibility.Visible;
            BuildGrid(scrollToTop ? 0 : Math.Max(_gridSelected, 0));
        }
        else
        {
            ResultsGrid.Visibility = Visibility.Collapsed;
            ResultsGrid.ItemsSource = null;
            _tiles.Clear();
            ListHeader.Visibility = Visibility.Visible;
            Results.Visibility = Visibility.Visible;
            Results.ItemsSource = new ResultList(_shownIndex, _shownHits, _iconSize);
            int target = scrollToTop ? 0 : _gridSelected;
            if (target >= 0 && target < _shownHits.Length)
            {
                if (!scrollToTop) Results.SelectedIndex = target;
                Results.ScrollIntoView(Results.Items[target]);
            }
        }
        SchedulePreview();
    }

    // ---- Sıralama ----

    void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        var key = Enum.Parse<SortKey>((string)((FrameworkElement)sender).Tag);
        if (_sortKey == key) _sortDescending = !_sortDescending;
        else
        {
            _sortKey = key;
            _sortDescending = key is SortKey.Size or SortKey.Date; // büyük ve yeni olanlar önce daha kullanışlı
        }
        ApplySort();
    }

    void SortMenu_Click(object sender, RoutedEventArgs e)
    {
        string tag = (string)((FrameworkElement)sender).Tag;
        if (tag == "Descending") _sortDescending = !_sortDescending;
        else _sortKey = Enum.Parse<SortKey>(tag);
        ApplySort();
    }

    void ApplySort()
    {
        _settings.SortKey = _sortKey.ToString();
        _settings.SortDescending = _sortDescending;
        SaveSettings();
        UpdateSortHeaders();
        RunSearch();
    }

    void UpdateSortHeaders()
    {
        (Button Button, SortKey Key, string Text)[] headers =
        [
            (SortName, SortKey.Name, "ColName"), (SortFolder, SortKey.Folder, "ColFolder"), (SortSize, SortKey.Size, "ColSize"),
            (SortDate, SortKey.Date, "ColModified"), (SortArchive, SortKey.Archive, "ColArchive"),
        ];
        foreach (var (button, key, text) in headers)
        {
            button.Content = Loc.T(text) + (_sortKey == key ? (_sortDescending ? "  ▼" : "  ▲") : "");
            button.ToolTip = Loc.T("SortTip");
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.T(text));
        }
    }

    void ResultsMenu_Opened(object sender, RoutedEventArgs e)
    {
        var single = SelectedEntries().Take(2).ToList();
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "Versions") item.IsEnabled = single.Count == 1 && !single[0].Entry.IsDirectory;
            foreach (var sub in item.Items.OfType<MenuItem>())
                sub.IsChecked = sub.Tag as string == "Descending" ? _sortDescending : sub.Tag as string == _sortKey.ToString();
        }
    }

    // ---- Kareli görünüm ----

    int TileOuterWidth => (int)Math.Max(_iconSize + 28, 96) + 4; // kare + kenar boşluğu

    int ComputePerRow()
    {
        double width = ResultsGrid.ActualWidth > 0 ? ResultsGrid.ActualWidth : ResultsPanel.ActualWidth;
        return Math.Max(1, (int)((width - SystemParameters.VerticalScrollBarWidth - 4) / TileOuterWidth));
    }

    void BuildGrid(int keepVisible)
    {
        _tiles.Clear();
        _gridPerRow = ComputePerRow();
        var list = new GridRowList(_shownHits.Length, _gridPerRow, CreateTile);
        ResultsGrid.ItemsSource = list;
        // Yeniden düzenlemeden (yakınlaştırma, pencere boyutu) sonra aynı yerde kal.
        if (keepVisible > 0 && list.Count > 0)
            Dispatcher.InvokeAsync(() => ScrollToGridItem(keepVisible), DispatcherPriority.Loaded);
    }

    TileVM CreateTile(int position)
    {
        if (_tiles.TryGetValue(position, out var existing)) return existing;
        var (archive, entry) = _shownIndex.Get(_shownHits[position]);
        var tile = new TileVM(position, archive, entry, IconProvider.For(entry.Name, entry.IsDirectory, _iconSize), _iconSize)
        {
            IsSelected = position == _gridSelected,
        };
        // Bellekte sınırsız birikmesin: çok fazlaysa (uzun kaydırmadan sonra) temizle, seçili kalsın.
        if (_tiles.Count > 4000)
        {
            _tiles.Clear();
            if (_gridSelected >= 0 && _gridSelected != position) _tiles.Remove(_gridSelected);
        }
        _tiles[position] = tile;

        if (!entry.IsDirectory && ThumbnailService.IsImage(entry.Name)) _thumbnails.Request(tile);
        return tile;
    }

    void ResultsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsGrid || !e.WidthChanged) return;
        if (ComputePerRow() != _gridPerRow) BuildGrid(Math.Max(_gridSelected, FirstVisibleGridItem()));
    }

    int FirstVisibleGridItem()
    {
        var sv = FindChild<ScrollViewer>(ResultsGrid);
        return sv == null ? 0 : (int)sv.VerticalOffset * Math.Max(1, _gridPerRow);
    }

    void SelectGridItem(int position, bool scroll)
    {
        if (position < 0 || position >= _shownHits.Length) return;
        if (_tiles.TryGetValue(_gridSelected, out var old)) old.IsSelected = false;
        _gridSelected = position;
        if (_tiles.TryGetValue(position, out var t)) t.IsSelected = true;
        if (scroll) ScrollToGridItem(position);
        SchedulePreview();
    }

    void ScrollToGridItem(int position)
    {
        var sv = FindChild<ScrollViewer>(ResultsGrid);
        if (sv == null || _gridPerRow <= 0) return;
        int row = position / _gridPerRow;
        // ScrollUnit=Item: dikey ofset satır numarasıdır.
        if (row < sv.VerticalOffset) sv.ScrollToVerticalOffset(row);
        else if (row >= sv.VerticalOffset + Math.Max(1, sv.ViewportHeight) - 1) sv.ScrollToVerticalOffset(row - Math.Max(1, sv.ViewportHeight) + 2);
    }

    void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TileVM tile) return;
        SelectGridItem(tile.Position, scroll: false);
        ResultsGrid.Focus();
        if (e.ClickCount == 2) OpenSelected();
        else
        {
            _dragStart = e.GetPosition(this);
            _dragCandidate = true;
        }
        e.Handled = true;
    }

    void Tile_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TileVM tile) SelectGridItem(tile.Position, scroll: false);
    }

    void ResultsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shownHits.Length == 0) return;
        int cur = Math.Max(_gridSelected, 0), per = Math.Max(1, _gridPerRow);
        int next = e.Key switch
        {
            Key.Right => cur + 1,
            Key.Left => cur - 1,
            Key.Down => cur + per,
            Key.Up => cur - per,
            Key.Home => 0,
            Key.End => _shownHits.Length - 1,
            Key.PageDown => cur + per * 4,
            Key.PageUp => cur - per * 4,
            _ => -2,
        };
        if (next != -2)
        {
            SelectGridItem(Math.Clamp(next, 0, _shownHits.Length - 1), scroll: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopyPath_Click(sender, e); e.Handled = true; }
    }

    void ResultsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed || !MovedEnough(e)) return;
        _dragCandidate = false;
        StartDragOut(ResultsGrid);
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } found) return found;
        }
        return null;
    }

    // ---- Görünüm, yakınlaştırma ve önizleme ----

    void ListView_Click(object sender, RoutedEventArgs e) => SetIconSize(_lastListSize);
    void GridView_Click(object sender, RoutedEventArgs e) => SetIconSize(_lastGridSize);

    void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        Zoom(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key is Key.OemPlus or Key.Add) { Zoom(+1); e.Handled = true; }
        else if (e.Key is Key.OemMinus or Key.Subtract) { Zoom(-1); e.Handled = true; }
        else if (e.Key is Key.D0 or Key.NumPad0) { SetIconSize(16); e.Handled = true; }
    }

    void Zoom(int direction)
    {
        int i = Array.IndexOf(ZoomSteps, _iconSize);
        if (i < 0) i = 0;
        SetIconSize(ZoomSteps[Math.Clamp(i + direction, 0, ZoomSteps.Length - 1)]);
    }

    void SetIconSize(int size)
    {
        if (size == _iconSize) return;
        // Görünümler arası geçişte seçimi taşı.
        if (!IsGrid && Results.SelectedIndex >= 0) _gridSelected = Results.SelectedIndex;
        _iconSize = size;
        if (IsGrid) _lastGridSize = size; else _lastListSize = size;
        UpdateViewButtons();
        ShowResults(scrollToTop: false);
        ResultStatus.Text = Loc.F("StatusIconSize", size, _shownHits.Length.ToString("N0", C));
        _settings.IconSize = size;
        SaveSettings();
    }

    void UpdateViewButtons()
    {
        var accent = TryFindResource("AccentButtonStyle") as Style;
        ListViewButton.Style = IsGrid ? null : accent;
        GridViewButton.Style = IsGrid ? accent : null;
        PreviewButton.Style = _settings.ShowPreview ? accent : null;
    }

    void TogglePreview_Click(object sender, RoutedEventArgs e) => SetPreviewVisible(!_settings.ShowPreview, save: true);

    void SetPreviewVisible(bool visible, bool save)
    {
        _settings.ShowPreview = visible;
        ApplyPreviewLayout(EmptyState.Visibility == Visibility.Visible);
        UpdateViewButtons();
        if (visible) UpdatePreview();
        if (save) SaveSettings();
    }

    /// <summary>Önizleme sütunu: açıksa ve kütüphane boş değilse görünür.</summary>
    void ApplyPreviewLayout(bool emptyLibrary)
    {
        bool show = _settings.ShowPreview && !emptyLibrary;
        PreviewColumn.Width = show ? new GridLength(Math.Clamp(_settings.PreviewWidth, 240, 900)) : new GridLength(0);
        SplitterColumn.Width = show ? new GridLength(6) : new GridLength(0);
        Preview.Visibility = PreviewSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void PreviewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.PreviewWidth = PreviewColumn.ActualWidth;
        SaveSettings();
    }

    void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => SchedulePreview();

    void SchedulePreview()
    {
        if (Preview.Visibility != Visibility.Visible) return;
        _previewTimer.Stop();
        _previewTimer.Start(); // ok tuşuyla hızlı gezerken her satırı yüklememek için kısa bekleme
    }

    void UpdatePreview()
    {
        if (Preview.Visibility != Visibility.Visible) return;
        int hit = SelectedHit();
        if (hit < 0) Preview.Clear();
        else Preview.Show(_shownIndex, hit);
    }

    int SelectedHit()
    {
        if (IsGrid) return _gridSelected >= 0 && _gridSelected < _shownHits.Length ? _shownHits[_gridSelected] : -1;
        return Results.SelectedItem is ResultRow r && r.Position < _shownHits.Length ? _shownHits[r.Position] : -1;
    }

    void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && _shownHits.Length > 0)
        {
            if (IsGrid)
            {
                SelectGridItem(0, scroll: true);
                ResultsGrid.Focus();
            }
            else
            {
                Results.SelectedIndex = 0;
                Results.UpdateLayout();
                (Results.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else ArchiveList.SelectedItem = null;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _shownHits.Length > 0)
        {
            if (IsGrid) SelectGridItem(0, scroll: true); else Results.SelectedIndex = 0;
            OpenSelected();
            e.Handled = true;
        }
    }

    void FocusSearch(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    void Help_Click(object sender, RoutedEventArgs e) => HelpPopup.IsOpen = !HelpPopup.IsOpen;

    // ---- Kayıtlı aramalar ----

    void RefreshSavedSearches()
    {
        SavedSearchList.SelectionChanged -= SavedSearchList_SelectionChanged;
        SavedSearchList.ItemsSource = _settings.SavedSearches.ToList();
        SavedSearchList.SelectionChanged += SavedSearchList_SelectionChanged;
        SavedSearchesPanel.Visibility = _settings.SavedSearches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSaveSearchGlyph();
    }

    void UpdateSaveSearchGlyph()
    {
        string q = SearchBox.Text.Trim();
        SaveSearchGlyph.Text = q.Length > 0 && _settings.SavedSearches.Any(s => s.Query == q) ? "" : "";
    }

    void SaveSearch_Click(object sender, RoutedEventArgs e)
    {
        string q = SearchBox.Text.Trim();
        if (q.Length == 0) return;
        var existing = _settings.SavedSearches.FirstOrDefault(s => s.Query == q);
        if (existing != null)
        {
            _settings.SavedSearches.Remove(existing); // dolu yıldız: kaydı kaldır
        }
        else
        {
            var dlg = new InputWindow(Loc.T("SaveSearchTitle"), Loc.T("SaveSearchPrompt"), q) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _settings.SavedSearches.Add(new SavedSearch { Name = dlg.Value, Query = q });
            ShowNotice(Loc.F("StatusSearchSaved", dlg.Value));
        }
        SaveSettings();
        RefreshSavedSearches();
    }

    void SavedSearchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedSearchList.SelectedItem is not SavedSearch s) return;
        SetQuery(s.Query);
        SearchBox.Focus();
        Dispatcher.InvokeAsync(() => SavedSearchList.SelectedItem = null, DispatcherPriority.Background);
    }

    void DeleteSavedSearch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedSearch s) return;
        _settings.SavedSearches.RemoveAll(x => x.Name == s.Name && x.Query == s.Query);
        SaveSettings();
        RefreshSavedSearches();
    }

    // ---- Kenar çubuğu: arşivler ----

    void ArchiveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Bir arşive tıklamak sonuçları o arşivle (ve iç içe arşivleriyle) sınırlar; boş aramada arşivin tüm içeriği görünür.
        _selectedArchive = (ArchiveList.SelectedItem as ArchiveItem)?.Path;
        RunSearch();
    }

    void ArchiveList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ArchiveList.SelectedItem is ArchiveItem item) _library.Remove([item.Path]);
        else if (e.Key == Key.Escape) ArchiveList.SelectedItem = null;
        else if (e.Key == Key.Enter && ArchiveList.SelectedItem is ArchiveItem { NeedsPassword: true } locked) _ = EnterPasswordFor(locked.Path);
    }

    void ArchiveList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveItem { NeedsPassword: true } item) _ = EnterPasswordFor(item.Path);
    }

    void ArchiveMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool encrypted = ArchiveList.SelectedItem is ArchiveItem { NeedsPassword: true };
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>())
            if (item.Tag as string == "Password") item.IsEnabled = encrypted;
    }

    void EnterPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveItem item) _ = EnterPasswordFor(item.Path);
    }

    async Task EnterPasswordFor(string path)
    {
        var rec = _library.Find(path);
        if (rec == null) return;
        bool wrong = false;
        while (true)
        {
            var dlg = new PasswordWindow(rec.DisplayName, wrong) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            PasswordStore.Set(rec.Path, dlg.Password, dlg.Remember);
            await RunBusy(() => _library.AddAsync([path], _progress, manual: null));
            rec = _library.Find(path);
            if (rec is not { NeedsPassword: true }) return;
            PasswordStore.Remove(path); // yanlış parola saklanmasın
            wrong = true;
        }
    }

    async void Reread_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveItem item) await RunBusy(() => _library.AddAsync([item.Path], _progress, manual: null));
    }

    void ShowSelectedArchive_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveItem item) ShowInExplorer(item.Path);
    }

    void RemoveSelectedArchive_Click(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is ArchiveItem item) _library.Remove([item.Path]);
    }

    void RemoveArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path) _library.Remove([path]);
    }

    // ---- Ekleme: düğmeler, sürükle-bırak, disk taraması ----

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        string patterns = string.Join(";", ArchiveReader.FilePatterns);
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("DialogAddTitle"),
            Multiselect = true,
            Filter = $"{Loc.T("DialogFilterArchives")}|{patterns}|{Loc.T("DialogFilterAll")}|*.*",
        };
        if (dlg.ShowDialog(this) == true) _ = Add(dlg.FileNames);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("DialogFolderTitle"), Multiselect = true };
        if (dlg.ShowDialog(this) == true) _ = Add(dlg.FolderNames);
    }

    void EmptyScanDisks_Click(object sender, RoutedEventArgs e)
    {
        _settings.ScanDisks = true;
        SaveSettings();
        StartScan();
    }

    async void StartScan()
    {
        if (_library.IsScanning) return;
        await RunBusy(() => _library.ScanAsync(_progress));
        Refresh();
    }

    void Window_DragEnter(object sender, DragEventArgs e)
    {
        bool files = !_dragOutActive && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_dragOutActive && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_DragLeave(object sender, DragEventArgs e)
    {
        // İmleç bir alt öğeye geçerken de DragLeave gelir; gerçekten pencereden çıkıldı mı bak.
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (_dragOutActive) return; // kendi sürüklediğimiz dosyalar pencereye geri bırakıldı
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            _ = Add(paths);
    }

    async Task Add(IEnumerable<string> paths)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            var r = await _library.AddAsync(paths, _progress);
            var parts = new List<string>();
            if (r.Added > 0) parts.Add(Loc.F("StatusAdded", r.Added.ToString("N0", C)));
            if (r.Updated > 0) parts.Add(Loc.F("StatusUpdated", r.Updated.ToString("N0", C)));
            if (r.Failed > 0) parts.Add(Loc.F("StatusFailed", r.Failed.ToString("N0", C)));
            if (r.Skipped > 0) parts.Add(Loc.F("StatusSkipped", r.Skipped.ToString("N0", C)));
            if (parts.Count == 0) parts.Add(Loc.T("StatusNothingAdded"));
            _notice = string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            Log.Warn("Arşiv eklenemedi", ex);
            _notice = Loc.F("StatusAddFailed", ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            HideProgress();
            UpdateLibraryStatus();
        }
    }

    // ---- Sonuç işlemleri (iki görünümde de seçili öğe üzerinden) ----

    IEnumerable<(ArchiveRecord Archive, ArchiveEntry Entry)> SelectedEntries()
    {
        if (IsGrid)
        {
            if (_gridSelected >= 0 && _gridSelected < _shownHits.Length) yield return _shownIndex.Get(_shownHits[_gridSelected]);
            yield break;
        }
        foreach (var r in Results.SelectedItems.OfType<ResultRow>()) yield return (r.ArchiveRecord, r.Entry);
    }

    void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    void Results_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopyPath_Click(sender, e); e.Handled = true; }
    }

    void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    async void OpenSelected()
    {
        var sel = SelectedEntries().FirstOrDefault();
        if (sel.Archive == null) return;
        var (archive, entry) = sel;
        if (entry.IsDirectory)
        {
            ShowInExplorer(archive.Path);
            return;
        }
        if (RiskyFiles.IsRisky(entry.Name)
            && MessageBox.Show(this, Loc.F("RiskyText", entry.Name), Loc.T("RiskyTitle"), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        ResultStatus.Text = Loc.F("StatusOpening", entry.Name);
        try
        {
            string? file = await WithPassword(archive, password => ArchiveExtractor.ExtractToTemp(archive, entry, password));
            if (file == null)
            {
                ResultStatus.Text = "";
                return;
            }
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            ResultStatus.Text = Loc.F("StatusOpened", entry.Name);
        }
        catch (Exception ex)
        {
            ResultStatus.Text = "";
            Log.Warn($"Açılamadı: {archive.DisplayPath} › {entry.Path}", ex);
            MessageBox.Show(this, Loc.F("OpenFailed", ex.Message), "Arşiv Ara", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>İşlemi dener; içerik şifreliyse parola sorar ve yeniden dener. Vazgeçilirse null.</summary>
    async Task<T?> WithPassword<T>(ArchiveRecord rec, Func<string?, T> action) where T : class
    {
        string? password = PasswordStore.For(rec);
        while (true)
        {
            try { return await Task.Run(() => action(password)); }
            catch (Exception ex) when (ArchiveReader.Describe(ex, password != null).Kind == ArchiveError.Encrypted)
            {
                var dlg = new PasswordWindow(rec.DisplayName, wrong: password != null) { Owner = this };
                if (dlg.ShowDialog() != true) return null;
                password = dlg.Password;
                PasswordStore.Set(rec.DisplayPath, password, dlg.Remember);
            }
        }
    }

    void ShowArchive_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedEntries().FirstOrDefault();
        if (sel.Archive != null) ShowInExplorer(sel.Archive.Path);
    }

    static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        string text = string.Join(Environment.NewLine, SelectedEntries().Select(s =>
        {
            string inner = s.Archive.IsNested ? "\\" + s.Archive.InnerPath!.Replace('/', '\\') : "";
            return $"{s.Archive.Path}{inner}\\{s.Entry.Path.Replace('/', '\\')}";
        }));
        if (text.Length > 0) Clipboard.SetText(text);
    }

    void Versions_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedEntries().FirstOrDefault();
        if (sel.Archive == null || sel.Entry.IsDirectory) return;
        // Aynı adlı tüm dosyalar, en yeni önce: "bu dosyanın en yeni sürümü hangi yedekte".
        ArchiveList.SelectedItem = null;
        _sortKey = SortKey.Date;
        _sortDescending = true;
        UpdateSortHeaders();
        SetQuery($"ad:=\"{sel.Entry.Name.Replace("\"", "")}\" dosya:", versionsOf: sel.Entry.Name);
    }

    /// <summary>
    /// Seçimi arşivlere göre gruplar. Dosya: sadece adıyla; klasör: kendisi ve altındaki her şey, klasörün adıyla başlayan yollarla.
    /// </summary>
    static List<(ArchiveRecord Rec, Dictionary<string, string> Map, List<string> Dirs)> Plan(
        IEnumerable<(ArchiveRecord Archive, ArchiveEntry Entry)> selection)
    {
        var groups = new Dictionary<ArchiveRecord, (Dictionary<string, string> Map, List<string> Dirs)>(ReferenceEqualityComparer.Instance);
        foreach (var (rec, entry) in selection)
        {
            if (!groups.TryGetValue(rec, out var g)) groups[rec] = g = (new Dictionary<string, string>(StringComparer.Ordinal), []);
            if (!entry.IsDirectory)
            {
                g.Map.TryAdd(entry.Path, entry.Name);
                continue;
            }
            int cut = entry.Folder.Length == 0 ? 0 : entry.Folder.Length + 1;
            g.Dirs.Add(entry.Path[cut..]);
            foreach (var e in rec.Entries)
            {
                if (e.Path.Length <= entry.Path.Length || !e.Path.StartsWith(entry.Path, StringComparison.Ordinal)) continue;
                if (e.IsDirectory) g.Dirs.Add(e.Path[cut..]);
                else g.Map.TryAdd(e.Path, e.Path[cut..]);
            }
        }
        return groups.Select(kv => (kv.Key, kv.Value.Map, kv.Value.Dirs)).ToList();
    }

    async void ExtractSelected_Click(object sender, RoutedEventArgs e)
    {
        var selection = SelectedEntries().ToList();
        if (selection.Count == 0) return;
        var dlg = new OpenFolderDialog { Title = Loc.T("DialogExtractTitle") };
        if (dlg.ShowDialog(this) != true) return;
        string dest = dlg.FolderName;

        var plan = Plan(selection);
        int total = plan.Sum(p => p.Map.Count), done = 0, written = 0;
        var errors = new List<string>();
        Interlocked.Increment(ref _busy);
        try
        {
            ShowProgress(new Library.AddProgress(0, total, Loc.F("StatusExtracting", 0, total)));
            foreach (var (rec, map, dirs) in plan)
            {
                try
                {
                    foreach (var d in dirs) Directory.CreateDirectory(ArchiveExtractor.SafeCombine(dest, d));
                    if (map.Count == 0) continue;
                    var count = await WithPassword(rec, password => (object)ArchiveExtractor.ExtractGroup(rec, map, dest, password, _ =>
                        Dispatcher.InvokeAsync(() =>
                        {
                            done++;
                            ShowProgress(new Library.AddProgress(done, total, Loc.F("StatusExtracting", done, total)));
                        })));
                    if (count is int n) written += n;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Çıkarılamadı: {rec.DisplayPath}", ex);
                    errors.Add($"{rec.DisplayName}: {ex.Message}");
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            HideProgress();
        }
        _notice = errors.Count == 0 ? Loc.F("StatusExtracted", written, dest) : Loc.F("StatusExtractFailed", string.Join("; ", errors));
        UpdateLibraryStatus();
        if (written > 0) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dest}\"") { UseShellExecute = true });
    }

    // ---- Explorer'a sürükleme ----

    bool MovedEnough(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return Math.Abs(p.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance
               || Math.Abs(p.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    void Results_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = false;
        _pendingSingleSelect = null;
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        _dragStart = e.GetPosition(this);
        _dragCandidate = true;
        // Çoklu seçimdeki bir öğeye basılınca seçim hemen daralmasın (yoksa birden çok dosya sürüklenemez);
        // sürükleme olmazsa bırakınca daraltılır (Explorer gibi).
        if (item.IsSelected && Keyboard.Modifiers == ModifierKeys.None && Results.SelectedItems.Count > 1 && e.ClickCount == 1)
        {
            _pendingSingleSelect = item.DataContext as ResultRow;
            item.Focus();
            e.Handled = true;
        }
    }

    void Results_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSingleSelect != null)
        {
            Results.SelectedItems.Clear();
            Results.SelectedItem = _pendingSingleSelect;
            _pendingSingleSelect = null;
        }
        _dragCandidate = false;
    }

    void Results_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed || !MovedEnough(e)) return;
        _dragCandidate = false;
        _pendingSingleSelect = null;
        StartDragOut(Results);
    }

    void StartDragOut(DependencyObject source)
    {
        var selection = SelectedEntries().ToList();
        if (selection.Count == 0) return;
        _dragOutActive = true;
        try { DragDrop.DoDragDrop(source, new LazyFileDrop(() => PrepareDrag(selection)), DragDropEffects.Copy); }
        catch (Exception ex) { Log.Warn("Sürükleme", ex); }
        finally
        {
            _dragOutActive = false;
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Hedef dosyaları istediğinde çağrılır: seçilenler geçici bir klasöre çıkarılır.</summary>
    string[]? PrepareDrag(List<(ArchiveRecord Archive, ArchiveEntry Entry)> selection)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        LibraryStatus.Text = Loc.T("StatusPreparingDrag");
        try
        {
            string root = Path.Combine(AppPaths.ExtractDir, "surukle", Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(root);
            foreach (var (rec, map, dirs) in Plan(selection))
            {
                foreach (var d in dirs) Directory.CreateDirectory(ArchiveExtractor.SafeCombine(root, d));
                if (map.Count > 0) ArchiveExtractor.ExtractGroup(rec, map, root, PasswordStore.For(rec));
            }
            return Directory.EnumerateFileSystemEntries(root).ToArray();
        }
        catch (Exception ex)
        {
            Log.Warn("Sürükleme için çıkarılamadı", ex);
            _notice = Loc.F("StatusExtractFailed", ex.Message);
            return null;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            UpdateLibraryStatus();
        }
    }

    // ---- Ayarlar ve Hakkında ----

    async void Settings_Click(object sender, RoutedEventArgs e)
    {
        bool wasScan = _settings.ScanDisks, wasNested = _settings.ReadNested;
        var roots = _settings.Roots.ToList();
        var excludes = _settings.Excludes.ToList();
        var dlg = new SettingsWindow(_settings) { Owner = this };
        dlg.ShowDialog();

        bool scopeChanged = !roots.SequenceEqual(_settings.Roots) || !excludes.SequenceEqual(_settings.Excludes);
        if (wasScan && !_settings.ScanDisks) _library.RemoveScanned();
        else if (_settings.ScanDisks && (!wasScan || scopeChanged || dlg.ScanRequested)) StartScan();
        if (wasNested != _settings.ReadNested) await RunBusy(() => _library.ReloadAllAsync(_progress));
        Refresh();
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    void Brand_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => About_Click(sender, e);
}
