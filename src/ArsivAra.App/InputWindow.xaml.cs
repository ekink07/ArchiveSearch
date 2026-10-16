using System.Windows;

namespace ArsivAra.App;

/// <summary>Single-line text input (e.g. the name of a saved search).</summary>
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
