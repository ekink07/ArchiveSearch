using System.Windows;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>Logo, sürüm, yapımcı, üretici ve lisans bilgisi. F1, logo ya da durum satırındaki imzayla açılır.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = Loc.F("AboutVersion", AppInfo.Version);
        ManufacturerText.Text = Loc.F("AboutManufacturer", AppInfo.Company);
        LicenseKindText.Text = AppInfo.LicenseName;
        CopyrightText.Text = AppInfo.Copyright;
    }

    void License_Click(object sender, RoutedEventArgs e) => new LicenseWindow { Owner = this }.ShowDialog();
}
