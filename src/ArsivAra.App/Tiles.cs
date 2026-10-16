using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>Kareli görünümde tek bir dosya/klasör.</summary>
public sealed class TileVM(int position, ArchiveRecord archive, ArchiveEntry entry, ImageSource? icon, double iconSize) : INotifyPropertyChanged
{
    public int Position { get; } = position;
    public ArchiveRecord Archive { get; } = archive;
    public ArchiveEntry Entry { get; } = entry;
    public string Name { get; } = entry.Name;
    public double IconSize { get; } = iconSize;
    public double TileWidth => Math.Max(IconSize + 28, 96);
    public double TileHeight => IconSize + 58;
    public string ToolTipText => $"{Entry.Path}\n{Archive.DisplayName}";

    ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnChanged(); OnChanged(nameof(DisplayImage)); }
    }

    /// <summary>Resimse küçük resmi (yüklendiyse), değilse dosya türü simgesi.</summary>
    public ImageSource? DisplayImage => _thumbnail ?? icon;

    bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Kareli görünümün bir satırı (pencere genişliğine sığan kadar kare).</summary>
public sealed record TileRow(IReadOnlyList<TileVM> Items);

/// <summary>
/// Sonuçları satırlara bölen sanal liste: satırlar sadece ekranda göründükçe oluşturulur,
/// böylece milyonlarca sonuçta da kareli görünüm akıcı kalır.
/// </summary>
public sealed class GridRowList(int itemCount, int perRow, Func<int, TileVM> createTile) : IList
{
    public int PerRow { get; } = Math.Max(1, perRow);
    public int Count => (itemCount + PerRow - 1) / PerRow;

    public object? this[int row]
    {
        get
        {
            int start = row * PerRow, end = Math.Min(itemCount, start + PerRow);
            var items = new TileVM[end - start];
            for (int i = start; i < end; i++) items[i - start] = createTile(i);
            return new TileRow(items);
        }
        set => throw new NotSupportedException();
    }

    public int IndexOf(object? value) => value is TileRow r && r.Items.Count > 0 ? r.Items[0].Position / PerRow : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i); }
}
