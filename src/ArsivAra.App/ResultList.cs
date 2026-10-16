using System.Collections;
using ArsivAra.Core;

namespace ArsivAra.App;

public sealed class ResultRow(ResultList owner, int position, ArchiveRecord archive, ArchiveEntry entry)
{
    internal ResultList Owner { get; } = owner;
    internal int Position { get; } = position;
    public ArchiveRecord ArchiveRecord { get; } = archive;
    public ArchiveEntry Entry { get; } = entry;

    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    /// <summary>Arşivin tam yolu (iç içe arşivde "D:\yedek.zip › Yedekler/eski.zip"); ipucunda görünür.</summary>
    public string Archive => ArchiveRecord.DisplayPath;
    public string ArchiveName => ArchiveRecord.DisplayName;
    public string SizeText => Entry.IsDirectory ? "" : Format.Size(Entry.Size);
    public string ModifiedText => Format.Date(Entry.MtimeUtcTicks);
    /// <summary>Windows dosya türü simgesi (klasör, Word, Fotoğraflar…), liste yakınlaştırmasına uygun boyutta.</summary>
    public System.Windows.Media.ImageSource? Icon => IconProvider.For(Entry.Name, Entry.IsDirectory, Owner.IconSize);
    public double IconSize => Owner.IconSize;

    // Sanal listede aynı satır her istekte yeniden oluşturulur; seçim takibi için konuma göre eşitlik.
    public override bool Equals(object? obj) => obj is ResultRow r && r.Owner == Owner && r.Position == Position;
    public override int GetHashCode() => HashCode.Combine(Owner, Position);
}

/// <summary>
/// Milyonlarca sonucu belleğe satır nesnesi olarak doldurmadan WPF'e veren sanal liste:
/// satır nesneleri sadece ekranda görünenler için, istendiği anda oluşturulur.
/// </summary>
public sealed class ResultList(SearchIndex index, int[] hits, double iconSize = 16) : IList
{
    public int Count => hits.Length;
    public double IconSize { get; } = iconSize;

    public object? this[int i]
    {
        get
        {
            var (a, e) = index.Get(hits[i]);
            return new ResultRow(this, i, a, e);
        }
        set => throw new NotSupportedException();
    }

    public int IndexOf(object? value) => value is ResultRow r && r.Owner == this ? r.Position : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;

    public IEnumerator GetEnumerator()
    {
        for (int i = 0; i < hits.Length; i++) yield return this[i];
    }

    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { for (int i = 0; i < hits.Length; i++) array.SetValue(this[i], index + i); }
}


