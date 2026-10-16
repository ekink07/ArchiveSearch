using System.Windows;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>The program's license text (LICENSE) and the licenses of the open-source components it uses. Opened from the About window.</summary>
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
