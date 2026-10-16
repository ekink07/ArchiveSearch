using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArsivAra.Core;
using Microsoft.Win32;

namespace ArsivAra.App;

/// <summary>
/// Ayarlar. Dil seçimi anında uygulanır; diğerleri pencere kapanınca ayarlara yazılır, ana pencere değişenlere göre davranır
/// (disk taramasını başlatır/durdurur, iç içe arşiv ayarı değişince arşivleri yeniden okur).
/// </summary>
public partial class SettingsWindow : Window
{
    static readonly int[] DayOptions = [1, 2, 7, 30];

    readonly Settings _settings;
    readonly ObservableCollection<string> _roots, _excludes;
    readonly string _originalLanguage;
    bool _loading = true;

    /// <summary>"Şimdi tara" seçildi.</summary>
    public bool ScanRequested { get; private set; }

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _originalLanguage = settings.Language;

        LanguageBox.Items.Add(new ComboBoxItem { Content = Loc.T("LangSystem"), Tag = "" });
        foreach (var lang in Strings.Languages)
            LanguageBox.Items.Add(new ComboBoxItem { Content = Strings.NativeNames[lang], Tag = lang });
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.Language)
                                   ?? LanguageBox.Items[0];

        ScanDisksBox.IsChecked = settings.ScanDisks;
        FastScanBox.IsChecked = settings.FastScan;
        RootsList.ItemsSource = _roots = new ObservableCollection<string>(settings.Roots);
        ExcludesList.ItemsSource = _excludes = new ObservableCollection<string>(settings.Excludes);
        NestedBox.IsChecked = settings.ReadNested;

        foreach (int d in DayOptions) TempDaysBox.Items.Add(new ComboBoxItem { Content = d == 1 ? Loc.T("Day1") : Loc.F("Days", d), Tag = d });
        TempDaysBox.SelectedItem = TempDaysBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == settings.TempRetentionDays)
                                   ?? TempDaysBox.Items[1];

        DataDirText.Text = Loc.F("SetDataDir", AppPaths.DataDir) + (AppPaths.IsPortable ? $" ({Loc.T("SetPortable")})" : "");
        UpdateTempSize();
        UpdatePasswords();
        _loading = false;
    }

    void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        string chosen = (string)item.Tag;
        _settings.Language = chosen;
        Loc.I.Set(Strings.Resolve(chosen)); // tüm pencereler hemen yeni dile geçer
        ((ComboBoxItem)LanguageBox.Items[0]).Content = Loc.T("LangSystem");
        DataDirText.Text = Loc.F("SetDataDir", AppPaths.DataDir) + (AppPaths.IsPortable ? $" ({Loc.T("SetPortable")})" : "");
        UpdateTempSize();
        UpdatePasswords();
    }

    void UpdateTempSize()
    {
        long size = TempFiles.Size();
        TempSizeText.Text = Loc.F("SetTempSize", Format.Size(size));
    }

    void UpdatePasswords()
    {
        int count = PasswordStore.SavedCount;
        PasswordsText.Text = Loc.F("SetPasswords", count);
        ClearPasswordsButton.IsEnabled = count > 0;
    }

    void AddRoot_Click(object sender, RoutedEventArgs e) => AddFolder(_roots, Loc.T("DialogRootTitle"));
    void AddExclude_Click(object sender, RoutedEventArgs e) => AddFolder(_excludes, Loc.T("DialogExcludeTitle"));

    void AddFolder(ObservableCollection<string> list, string title)
    {
        var dlg = new OpenFolderDialog { Title = title, Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FolderNames)
            if (!list.Contains(f, StringComparer.OrdinalIgnoreCase)) list.Add(f);
    }

    void RemoveRoot_Click(object sender, RoutedEventArgs e) => RemoveSelected(RootsList, _roots);
    void RemoveExclude_Click(object sender, RoutedEventArgs e) => RemoveSelected(ExcludesList, _excludes);

    static void RemoveSelected(ListBox box, ObservableCollection<string> list)
    {
        if (box.SelectedItem is string s) list.Remove(s);
    }

    void ScanNow_Click(object sender, RoutedEventArgs e)
    {
        ScanRequested = true;
        Close();
    }

    void CleanTemp_Click(object sender, RoutedEventArgs e)
    {
        TempFiles.Clean(TimeSpan.Zero);
        UpdateTempSize();
    }

    void ClearPasswords_Click(object sender, RoutedEventArgs e)
    {
        PasswordStore.ClearAll();
        UpdatePasswords();
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = true });
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>Pencere nasıl kapanırsa kapansın (düğme, Esc, X) seçimler ayarlara yazılır.</summary>
    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.ScanDisks = ScanDisksBox.IsChecked == true;
        _settings.FastScan = FastScanBox.IsChecked == true;
        _settings.Roots = [.. _roots];
        _settings.Excludes = [.. _excludes];
        _settings.ReadNested = NestedBox.IsChecked == true;
        if (TempDaysBox.SelectedItem is ComboBoxItem days) _settings.TempRetentionDays = (int)days.Tag;
        try { _settings.Save(); }
        catch (Exception ex) { Log.Warn("Ayarlar kaydedilemedi", ex); }
    }

    public bool LanguageChanged => _settings.Language != _originalLanguage;
}
