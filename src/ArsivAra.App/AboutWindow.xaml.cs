using System.Windows;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>Logo, sürüm ve yapımcı bilgisi. F1 ya da durum satırındaki imzayla açılır.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Sürüm {AppInfo.Version}";
    }
}
