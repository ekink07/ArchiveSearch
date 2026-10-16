using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArsivAra.Core;
using Microsoft.Win32;

namespace ArsivAra.App;

/// <summary>Kenar çubuğundaki bir arşiv satırı.</summary>
public sealed record ArchiveItem(string Path, string Name, string Detail, ImageSource? Icon);

public partial class MainWindow : Window
{
    // Ctrl+tekerlek basamakları (px). 16–32: liste, 48 ve üstü: kareli görünüm (Everything gibi).
    static readonly int[] ZoomSteps = [16, 20, 24, 32, 48, 64, 96, 128, 160, 192, 256];
    const int FirstGridSize = 48;

    readonly Library _library = new();
    readonly ThumbnailService _thumbnails = new();
    readonly Settings _settings = Settings.Load();
    readonly Progress<Library.AddProgress> _progress;
    CancellationTokenSource? _searchCts;
    string? _selectedArchive;
    bool _busy;

    // Ekrandaki sonuçlar ve seçim (iki görünüm aynı seçimi paylaşır).
    SearchIndex _shownIndex = SearchIndex.Empty;
    int[] _shownHits = [];
    int _iconSize;
    int _lastListSize = 16, _lastGridSize = 96;
    int _gridSelected = -1;
    int _gridPerRow;
    readonly Dictionary<int, TileVM> _tiles = new();

    bool IsGrid => _iconSize >= FirstGridSize;

    public MainWindow()
    {
        InitializeComponent();
        _progress = new Progress<Library.AddProgress>(ShowProgress);
        _library.Changed += () => Dispatcher.InvokeAsync(Refresh);
        _iconSize = ZoomSteps.Contains(_settings.IconSize) ? _settings.IconSize : 16;
        if (IsGrid) _lastGridSize = _iconSize; else _lastListSize = _iconSize;
        UpdateViewButtons();

        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            Refresh();
            await _library.LoadAsync(_progress);
            HideProgress();
            UpdateLibraryStatus();

            // Program simgesinin üzerine bırakılan ya da "Birlikte aç" ile verilen dosyalar.
            var args = Environment.GetCommandLineArgs().Skip(1).Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
            if (args.Count > 0) await Add(args);
        };
        Closed += (_, _) => _thumbnails.Dispose();
    }

    // ---- Kütüphane görünümü ----

    void Refresh()
    {
        var index = _library.Current;
        var items = index.Archives.Select(ToItem).ToList();
        ArchiveList.SelectionChanged -= ArchiveList_SelectionChanged;
        ArchiveList.ItemsSource = items;
        ArchiveList.SelectedItem = items.FirstOrDefault(i => i.Path == _selectedArchive);
        if (ArchiveList.SelectedItem == null) _selectedArchive = null;
        ArchiveList.SelectionChanged += ArchiveList_SelectionChanged;

        ArchivesHeader.Text = items.Count == 0 ? "Arşivler" : $"Arşivler ({items.Count:N0})";
        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Sidebar.Visibility = ResultsPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (!_busy) UpdateLibraryStatus();
        RunSearch();
    }

    static ArchiveItem ToItem(ArchiveRecord a)
    {
        string name = Path.GetFileName(a.Path);
        string detail;
        if (a.Error != null) detail = "⚠ " + a.Error;
        else if (!File.Exists(a.Path)) detail = "⚠ Dosya bulunamadı (taşınmış/silinmiş)";
        else detail = $"{a.Entries.Length:N0} dosya · {FormatSize(a.FileSize)}";
        return new ArchiveItem(a.Path, name, detail, IconProvider.For(name, false, 32));
    }

    void UpdateLibraryStatus()
    {
        var index = _library.Current;
        LibraryStatus.Text = index.Archives.Count == 0
            ? "Henüz arşiv eklenmedi"
            : $"{index.Archives.Count:N0} arşiv · {index.EntryCount:N0} dosya aranabilir";
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
            AddProgressText.Text = p.Total < 0 ? "Aranıyor…" : "";
            return;
        }
        double value = p.Total == 0 ? 1 : (double)p.Done / p.Total;
        AddProgressBar.Value = value;
        Taskbar.ProgressValue = value;
        AddProgressText.Text = $"{p.Done:N0} / {p.Total:N0}";
    }

    void HideProgress()
    {
        AddProgressPanel.Visibility = Visibility.Collapsed;
        AddProgressBar.IsIndeterminate = false;
        Taskbar.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
    }

    // ---- Arama ----

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RunSearch();
    }

    async void RunSearch()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var index = _library.Current;
        string query = SearchBox.Text;
        string? onlyArchive = _selectedArchive;

        try
        {
            var (hits, ms) = await Task.Run(() =>
            {
                var r = index.Search(query, cts.Token);
                var h = r.Hits;
                if (onlyArchive != null)
                {
                    int ai = -1;
                    for (int i = 0; i < index.Archives.Count; i++)
                        if (index.Archives[i].Path == onlyArchive) { ai = i; break; }
                    h = h.Where(x => index.ArchiveIndexOf(x) == ai).ToArray();
                }
                return (h, r.Elapsed.TotalMilliseconds);
            }, cts.Token);
            if (cts.IsCancellationRequested) return;

            _shownIndex = index;
            _shownHits = hits;
            _gridSelected = -1;
            ShowResults(scrollToTop: true);

            string scope = onlyArchive != null ? $"  ·  sadece {Path.GetFileName(onlyArchive)}" : "";
            ResultStatus.Text = index.EntryCount == 0 ? "" : $"{hits.Length:N0} sonuç ({ms:0.0} ms){scope}";
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
            Dispatcher.InvokeAsync(() => ScrollToGridItem(keepVisible), System.Windows.Threading.DispatcherPriority.Loaded);
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

    // ---- Görünüm ve yakınlaştırma ----

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
        ResultStatus.Text = $"Simge boyutu: {size} px  ·  {_shownHits.Length:N0} sonuç";

        _settings.IconSize = size;
        try { _settings.Save(); } catch (Exception) { }
    }

    void UpdateViewButtons()
    {
        var accent = TryFindResource("AccentButtonStyle") as Style;
        ListViewButton.Style = IsGrid ? null : accent;
        GridViewButton.Style = IsGrid ? accent : null;
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

    void ArchiveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Bir arşive tıklamak sonuçları o arşivle sınırlar (boş aramada arşivin tüm içeriği görünür).
        _selectedArchive = (ArchiveList.SelectedItem as ArchiveItem)?.Path;
        RunSearch();
    }

    void ArchiveList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ArchiveList.SelectedItem is ArchiveItem item) _library.Remove([item.Path]);
        else if (e.Key == Key.Escape) ArchiveList.SelectedItem = null;
    }

    void RemoveArchive_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path) _library.Remove([path]);
    }

    // ---- Ekleme: düğmeler ve sürükle-bırak ----

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Arşiv ekle",
            Multiselect = true,
            Filter = "Arşivler|*.rar;*.zip;*.7z;*.tar;*.jar;*.cbz;*.cbr;*.cb7;*.zipx;*.apk;*.nupkg|Tüm dosyalar|*.*",
        };
        if (dlg.ShowDialog(this) == true) _ = Add(dlg.FileNames);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "İçindeki arşivler eklenecek klasörü seçin", Multiselect = true };
        if (dlg.ShowDialog(this) == true) _ = Add(dlg.FolderNames);
    }

    void Window_DragEnter(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
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
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            _ = Add(paths);
        e.Handled = true;
    }

    async Task Add(IEnumerable<string> paths)
    {
        _busy = true;
        try
        {
            var r = await _library.AddAsync(paths, _progress);
            var parts = new List<string>();
            if (r.Added > 0) parts.Add($"{r.Added:N0} arşiv eklendi");
            if (r.Updated > 0) parts.Add($"{r.Updated:N0} arşiv güncellendi");
            if (r.Failed > 0) parts.Add($"{r.Failed:N0} arşiv okunamadı");
            if (r.Skipped > 0) parts.Add($"{r.Skipped:N0} dosya arşiv değil, atlandı");
            if (parts.Count == 0) parts.Add("Eklenecek arşiv bulunamadı");
            _busy = false;
            UpdateLibraryStatus();
            LibraryStatus.Text += "  ·  " + string.Join(", ", parts);
        }
        catch (Exception ex)
        {
            LibraryStatus.Text = "Eklenemedi: " + ex.Message;
        }
        finally
        {
            _busy = false;
            HideProgress();
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

        ResultStatus.Text = $"Açılıyor: {entry.Name}…";
        try
        {
            string file = await Task.Run(() => ArchiveReader.ExtractToTemp(archive.Path, entry.Path));
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            ResultStatus.Text = $"Açıldı: {entry.Name}";
        }
        catch (Exception ex)
        {
            ResultStatus.Text = "";
            MessageBox.Show(this, $"Dosya açılamadı:\n{ex.Message}", "Arşiv Ara", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        string text = string.Join(Environment.NewLine,
            SelectedEntries().Select(s => $"{s.Archive.Path}\\{s.Entry.Path.Replace('/', '\\')}"));
        if (text.Length > 0) Clipboard.SetText(text);
    }

    // ---- Hakkında ----

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    static string FormatSize(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:N0} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):N1} MB",
        _ => $"{b / (1024.0 * 1024 * 1024):N2} GB",
    };
}
