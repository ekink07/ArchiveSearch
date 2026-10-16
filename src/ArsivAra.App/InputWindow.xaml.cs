using System.Windows;

namespace ArsivAra.App;

/// <summary>Tek satırlık metin girişi (ör. kaydedilen aramanın adı).</summary>
public partial class InputWindow : Window
{
    public string Value => Input.Text.Trim();

    public InputWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length > 0) DialogResult = true;
    }
}
