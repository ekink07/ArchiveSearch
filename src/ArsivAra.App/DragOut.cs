using System.IO;
using System.Windows;

namespace ArsivAra.App;

/// <summary>
/// Sonuçları Explorer'a (ya da başka bir programa) sürüklemek. Dosyalar sürükleme başlarken değil, hedef ilk kez
/// dosya listesini istediğinde çıkarılır (gecikmeli veri): sürüklemeden vazgeçilirse hiçbir şey çıkarılmaz.
/// </summary>
sealed class LazyFileDrop(Func<string[]?> extract) : IDataObject
{
    const string PreferredDropEffect = "Preferred DropEffect";
    string[]? _files;
    bool _extracted;

    string[]? Files
    {
        get
        {
            if (!_extracted)
            {
                _extracted = true;
                _files = extract();
            }
            return _files;
        }
    }

    public object? GetData(string format) => GetData(format, true);

    public object? GetData(string format, bool autoConvert) =>
        format == DataFormats.FileDrop ? Files
        : format == PreferredDropEffect ? new MemoryStream(BitConverter.GetBytes(1)) // DROPEFFECT_COPY: taşıma değil kopyalama
        : null;

    public object? GetData(Type format) => format == typeof(string[]) ? Files : null;
    public bool GetDataPresent(string format) => format == DataFormats.FileDrop || format == PreferredDropEffect;
    public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);
    public bool GetDataPresent(Type format) => format == typeof(string[]);
    public string[] GetFormats() => [DataFormats.FileDrop, PreferredDropEffect];
    public string[] GetFormats(bool autoConvert) => GetFormats();

    // Hedef program sürükleme görseli gibi ek bilgiler yazmak isteyebilir; saklamaya gerek yok.
    public void SetData(object data) { }
    public void SetData(string format, object data) { }
    public void SetData(Type format, object data) { }
    public void SetData(string format, object data, bool autoConvert) { }
}
