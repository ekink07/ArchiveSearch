using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Seçili dosyanın önizlemesi: resimler, metin dosyaları ve Office belgelerinin metni; bilgiler ve aynı adlı dosyanın
/// tüm sürümleri (hangi yedekte en yenisi). Solid arşivlerde içerik ancak istenirse (arşiv baştan açılarak) yüklenir.
/// </summary>
public partial class PreviewPane : UserControl
{
    const long MaxImageBytes = 40L << 20;
    const int MaxTextBytes = 256 * 1024;

    enum Kind { None, Image, Text, Office }

    public sealed record VersionLine(string Text, string ToolTip, FontWeight Weight, double Opacity);

    readonly ArchiveEntryReader _reader = new();
    CancellationTokenSource? _cts;
    ArchiveRecord? _rec;
    ArchiveEntry _entry;

    public event Action? OpenRequested;
    public event Action? ExtractRequested;

    public PreviewPane()
    {
        InitializeComponent();
        Clear();
        Unloaded += (_, _) => _cts?.Cancel();
    }

    public void Clear()
    {
        _cts?.Cancel();
        _rec = null;
        Header.Visibility = VersionsPanel.Visibility = Visibility.Collapsed;
        ImageView.Visibility = TextView.Visibility = LoadAnywayButton.Visibility = Visibility.Collapsed;
        ImageView.Source = null;
        TextView.Text = "";
        ShowMessage(Loc.T("PreviewSelect"));
    }

    public async void Show(SearchIndex index, int hit)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;
        var (rec, entry) = index.Get(hit);
        _rec = rec;
        _entry = entry;

        Header.Visibility = Visibility.Visible;
        IconImage.Source = IconProvider.For(entry.Name, entry.IsDirectory, 32);
        TitleText.Text = entry.Name;
        SizeText.Text = entry.IsDirectory ? "—" : $"{Format.Size(entry.Size)} ({entry.Size.ToString("N0", Loc.I.Culture)} B)";
        ModifiedText.Text = entry.MtimeUtcTicks > 0 ? Format.Date(entry.MtimeUtcTicks) : "—";
        FolderText.Text = entry.Folder.Length > 0 ? entry.Folder : "—";
        ArchiveText.Text = rec.DisplayPath;
        OpenButton.IsEnabled = !entry.IsDirectory;
        VersionsPanel.Visibility = Visibility.Collapsed;

        try
        {
            if (!entry.IsDirectory)
            {
                var versions = await Task.Run(() => index.SameName(hit), ct);
                if (ct.IsCancellationRequested) return;
                ShowVersions(index, hit, versions);
            }
            await LoadContent(rec, entry, allowSolid: false, ct);
        }
        catch (OperationCanceledException) { }
    }

    void ShowVersions(SearchIndex index, int hit, int[] versions)
    {
        var lines = versions.Take(8).Select((v, i) =>
        {
            var (a, e) = index.Get(v);
            string text = $"{(v == hit ? "▶ " : "")}{a.DisplayName} · {Format.Date(e.MtimeUtcTicks)} · {Format.Size(e.Size)}";
            return new VersionLine(text, a.DisplayPath + " › " + e.Path, i == 0 ? FontWeights.SemiBold : FontWeights.Normal, v == hit || i == 0 ? 1 : 0.75);
        }).ToList();
        if (versions.Length <= 1) lines = [new VersionLine(Loc.T("PreviewNoOtherVersions"), "", FontWeights.Normal, 0.65)];
        VersionsList.ItemsSource = lines;
        VersionsPanel.Visibility = Visibility.Visible;
    }

    static Kind KindOf(string name) =>
        ThumbnailService.IsImage(name) ? Kind.Image
        : ContentSearch.IsOfficeName(name) ? Kind.Office
        : ContentSearch.IsTextName(name) ? Kind.Text
        : Kind.None;

    async Task LoadContent(ArchiveRecord rec, ArchiveEntry entry, bool allowSolid, CancellationToken ct)
    {
        ImageView.Visibility = TextView.Visibility = Visibility.Collapsed;
        LoadAnywayButton.Visibility = Visibility.Collapsed;
        ImageView.Source = null;
        TextView.Text = "";

        if (entry.IsDirectory)
        {
            int count = rec.Entries.Count(e => e.Path.Length > entry.Path.Length && e.Path.StartsWith(entry.Path, StringComparison.Ordinal));
            ShowMessage(Loc.F("PreviewFolder", count));
            return;
        }
        var kind = KindOf(entry.Name);
        if (kind == Kind.None)
        {
            ShowMessage(Loc.T("PreviewNone"));
            return;
        }
        long limit = kind switch { Kind.Image => MaxImageBytes, Kind.Office => ContentSearch.MaxOfficeBytes, _ => ContentSearch.MaxTextBytes };
        if (entry.Size > limit)
        {
            ShowMessage(Loc.F("PreviewTooLarge", Format.Size(entry.Size)));
            return;
        }

        ShowMessage(Loc.T("PreviewLoading"));
        byte[]? data;
        try
        {
            data = await Task.Run(() =>
            {
                var d = _reader.Read(rec, entry.Path, limit);
                if (d == null && allowSolid)
                    d = File.ReadAllBytes(ArchiveExtractor.ExtractToTemp(rec, entry, PasswordStore.For(rec), ct));
                return d;
            }, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            ShowMessage(ArchiveReader.Describe(ex).Kind == ArchiveError.Encrypted ? Loc.T("PreviewEncrypted") : Loc.F("PreviewFailed", ex.Message));
            return;
        }
        if (ct.IsCancellationRequested) return;
        if (data == null)
        {
            // Solid arşiv (ya da şifreli/büyük iç arşiv): istenirse arşiv baştan açılarak yüklenir.
            ShowMessage(rec.IsSolid ? Loc.T("PreviewSolid") : Loc.T("PreviewEncrypted"));
            LoadAnywayButton.Visibility = Visibility.Visible;
            return;
        }

        switch (kind)
        {
            case Kind.Image:
                var image = await Task.Run(() => Decode(data), ct);
                if (ct.IsCancellationRequested) return;
                if (image == null) { ShowMessage(Loc.F("PreviewFailed", entry.Name)); return; }
                ImageView.Source = image;
                ImageView.Visibility = Visibility.Visible;
                MessagePanel.Visibility = Visibility.Collapsed;
                break;
            case Kind.Office:
                string office = await Task.Run(() => ContentSearch.ExtractText(entry.Name, new MemoryStream(data)), ct);
                ShowText(office);
                break;
            default:
                ShowText(ContentSearch.DecodeText(data.Length > MaxTextBytes ? data[..MaxTextBytes] : data));
                break;
        }
    }

    void ShowText(string text)
    {
        TextView.Text = text.Length > 200_000 ? text[..200_000] + " …" : text;
        TextView.Visibility = Visibility.Visible;
        MessagePanel.Visibility = Visibility.Collapsed;
    }

    void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessagePanel.Visibility = Visibility.Visible;
    }

    static ImageSource? Decode(byte[] data)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = 1600;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    async void LoadAnyway_Click(object sender, RoutedEventArgs e)
    {
        if (_rec == null) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { await LoadContent(_rec, _entry, allowSolid: true, cts.Token); }
        catch (OperationCanceledException) { }
    }

    void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
    void Extract_Click(object sender, RoutedEventArgs e) => ExtractRequested?.Invoke();

    public void DisposeReader() => _reader.Dispose();
}
