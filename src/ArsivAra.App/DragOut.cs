using System.IO;
using System.Windows;

namespace ArsivAra.App;

/// <summary>
/// Dragging results to Explorer (or another program). Files are extracted not when the drag starts but when the target
/// first asks for the file list (delayed rendering): if the drag is cancelled, nothing is extracted.
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
        : format == PreferredDropEffect ? new MemoryStream(BitConverter.GetBytes(1)) // DROPEFFECT_COPY: copy, not move
        : null;

    public object? GetData(Type format) => format == typeof(string[]) ? Files : null;
    public bool GetDataPresent(string format) => format == DataFormats.FileDrop || format == PreferredDropEffect;
    public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);
    public bool GetDataPresent(Type format) => format == typeof(string[]);
    public string[] GetFormats() => [DataFormats.FileDrop, PreferredDropEffect];
    public string[] GetFormats(bool autoConvert) => GetFormats();

    // The target program may want to write extra data such as the drag image; no need to keep it.
    public void SetData(object data) { }
    public void SetData(string format, object data) { }
    public void SetData(Type format, object data) { }
    public void SetData(string format, object data, bool autoConvert) { }
}
