using System.Windows;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>Programın lisans metni (LICENSE) ve kullanılan açık kaynak bileşenlerin lisansları. Hakkında penceresinden açılır.</summary>
public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
        HeadText.Text = $"{AppInfo.Name} {AppInfo.Version}";
        SubText.Text = $"{Loc.F("LicenseKind", AppInfo.LicenseName)} · {Loc.F("AboutManufacturer", AppInfo.Company)}";
        LicenseBox.Text = AppInfo.LicenseText;
        NoticesBox.Text = AppInfo.ThirdPartyNotices;
    }
}
