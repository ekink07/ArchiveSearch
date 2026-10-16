using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace ArsivAra.Core;

/// <summary>
/// Arşivden tek tek dosyaları belleğe okur (küçük resimler için). Büyük bir arşivi her dosyada yeniden açmamak için
/// son kullanılan birkaç arşiv açık tutulur. Aynı arşive aynı anda tek iş parçacığı erişir.
/// Solid RAR/7z'de bir dosyaya ulaşmak öncekilerin hepsini açmayı gerektirebilir; orada null döner.
/// </summary>
public sealed class ArchiveEntryReader : IDisposable
{
    const int MaxOpen = 4;

    sealed class Handle : IDisposable
    {
        public required string Path { get; init; }
        public ZipArchive? Zip { get; init; }
        public Dictionary<string, ZipArchiveEntry>? ZipEntries { get; init; }
        public IArchive? Archive { get; init; }
        public Dictionary<string, IArchiveEntry>? Entries { get; init; }
        public bool Solid { get; init; }
        public readonly object Gate = new();
        public void Dispose() { Zip?.Dispose(); Archive?.Dispose(); }
    }

    readonly object _lock = new();
    readonly LinkedList<Handle> _open = new(); // en son kullanılan başta

    /// <summary>Girdinin içeriği; çok büyükse, bulunamazsa ya da solid arşivdeyse null.</summary>
    public byte[]? Read(string archivePath, string entryPath, long maxBytes)
    {
        Handle? h;
        try { h = Get(archivePath); }
        catch (Exception) { return null; }
        if (h == null || h.Solid) return null;

        lock (h.Gate)
        {
            try
            {
                Stream src;
                if (h.ZipEntries != null)
                {
                    if (!h.ZipEntries.TryGetValue(entryPath, out var ze) || ze.Length > maxBytes) return null;
                    src = ze.Open();
                }
                else
                {
                    if (!h.Entries!.TryGetValue(entryPath, out var e) || e.Size > maxBytes || e.IsEncrypted) return null;
                    src = e.OpenEntryStream();
                }
                using (src)
                {
                    var ms = new MemoryStream();
                    src.CopyTo(ms);
                    return ms.Length > maxBytes ? null : ms.ToArray();
                }
            }
            catch (Exception) { return null; }
        }
    }

    Handle? Get(string path)
    {
        lock (_lock)
        {
            for (var n = _open.First; n != null; n = n.Next)
            {
                if (!string.Equals(n.Value.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                _open.Remove(n);
                _open.AddFirst(n);
                return n.Value;
            }
        }

        var h = Open(path);
        lock (_lock)
        {
            _open.AddFirst(h);
            while (_open.Count > MaxOpen)
            {
                var old = _open.Last!.Value;
                _open.RemoveLast();
                lock (old.Gate) old.Dispose();
            }
        }
        return h;
    }

    static Handle Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        try
        {
            if (!path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".cb7", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, ArchiveReader.LegacyNameEncoding);
                    var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                    foreach (var e in zip.Entries) map.TryAdd(ArchiveReader.NormalizePath(e.FullName), e);
                    return new Handle { Path = path, Zip = zip, ZipEntries = map };
                }
                catch (InvalidDataException) { fs.Position = 0; }
            }

            var archive = ArchiveFactory.OpenArchive(fs, new ReaderOptions
            {
                ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Default = ArchiveReader.LegacyNameEncoding },
            });
            var entries = new Dictionary<string, IArchiveEntry>(StringComparer.Ordinal);
            bool solid = archive.IsSolid;
            if (!solid)
                foreach (var e in archive.Entries)
                    if (!e.IsDirectory) entries.TryAdd(ArchiveReader.NormalizePath(e.Key ?? ""), e);
            return new Handle { Path = path, Archive = archive, Entries = entries, Solid = solid };
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var h in _open) lock (h.Gate) h.Dispose();
            _open.Clear();
        }
    }
}
