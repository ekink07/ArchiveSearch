using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// The current UI language. In XAML, <c>{l:T Key}</c> binds to this object's indexer; when the language changes,
/// the "Item[]" notification updates every binding. From code: <c>Loc.T("Key")</c>, <c>Loc.F("Key", 3)</c>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public string Language { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    public string this[string key] => Strings.Get(Language, key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The language changed (text built in code should be regenerated).</summary>
    public event Action? Changed;

    public void Set(string language)
    {
        Language = language;
        Culture = Strings.CultureOf(language);
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        Thread.CurrentThread.CurrentCulture = Culture;
        Thread.CurrentThread.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        Changed?.Invoke();
    }

    public static string T(string key) => I[key];

    public static string F(string key, params object?[] args) => string.Format(I.Culture, I[key], args);
}

/// <summary>Translated text in XAML: <c>Text="{l:T SearchHint}"</c>. Updates automatically when the language changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
