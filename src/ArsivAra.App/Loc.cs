using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Arayüzün o anki dili. XAML'de <c>{l:T Anahtar}</c> bu nesnenin dizinleyicisine bağlanır; dil değişince
/// "Item[]" bildirimi tüm bağlamaları günceller. Koddan: <c>Loc.T("Anahtar")</c>, <c>Loc.F("Anahtar", 3)</c>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public string Language { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

    public string this[string key] => Strings.Get(Language, key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Dil değişti (koddan üretilen metinler yeniden yazılsın).</summary>
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

/// <summary>XAML'de çevrilmiş metin: <c>Text="{l:T SearchHint}"</c>. Dil değişince kendiliğinden güncellenir.</summary>
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
