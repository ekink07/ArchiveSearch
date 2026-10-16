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
    /// <summary>Full path of the archive (for a nested archive "D:\backup.zip › Backups/old.zip"); shown in the tooltip.</summary>
    public string Archive => ArchiveRecord.DisplayPath;
    public string ArchiveName => ArchiveRecord.DisplayName;
    public string SizeText => Entry.IsDirectory ? "" : Format.Size(Entry.Size);
    public string ModifiedText => Format.Date(Entry.MtimeUtcTicks);
    /// <summary>Windows file type icon (folder, Word, Photos…), sized for the list zoom.</summary>
    public System.Windows.Media.ImageSource? Icon => IconProvider.For(Entry.Name, Entry.IsDirectory, Owner.IconSize);
    public double IconSize => Owner.IconSize;

    // In the virtual list the same row is recreated on every request; equality by position for selection tracking.
    public override bool Equals(object? obj) => obj is ResultRow r && r.Owner == Owner && r.Position == Position;
    public override int GetHashCode() => HashCode.Combine(Owner, Position);
}

/// <summary>
/// Virtual list that hands millions of results to WPF without filling memory with row objects:
/// row objects are created on demand, only for the rows visible on screen.
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


