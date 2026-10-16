using System.Windows;

namespace ArsivAra.App;

/// <summary>Asks for the password of an encrypted archive; if "remember" is checked, the password is stored with DPAPI (see PasswordStore).</summary>
public partial class PasswordWindow : Window
{
    public string Password => Box.Password;
    public bool Remember => RememberBox.IsChecked == true;

    /// <param name="wrong">The previous attempt failed: a warning is shown.</param>
    public PasswordWindow(string archiveName, bool wrong)
    {
        InitializeComponent();
        PromptText.Text = Loc.F("PasswordPrompt", archiveName);
        WrongText.Visibility = wrong ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Box.Focus();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Box.Password.Length > 0) DialogResult = true;
    }
}
