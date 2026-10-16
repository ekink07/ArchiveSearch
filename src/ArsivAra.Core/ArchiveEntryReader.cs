using System.IO.Compression;
using SharpCompress.Archives;

namespace ArsivAra.Core;

/// <summary>
/// Arşivden tek tek dosyaları belleğe okur (küçük resim ve önizleme için). Büyük bir arşivi her dosyada yeniden açmamak için
/// son kullanılan birkaç arşiv açık tutulur. Aynı arşive aynı anda tek iş parçacığı erişir.
/// Solid RAR/7z ve sıkıştırılmış tar'da bir dosyaya ulaşmak öncekilerin hepsini açmayı gerektirebilir; orada null döner.
/// İç içe arşivler küçükse (64 MB'a kadar) geçici klasöre çıkarılıp okunur.
/// </summary>
public sealed class ArchiveEntryReader : IDisposable
{
    const int MaxOpen = 4;
    const long MaxNestedMaterialize = 64L << 20;

    sealed class Handle : IDisposable
    {
        public required string Path { get; init; }
        public ZipArchive? Zip { get; init; }
        public Dictionary<string, ZipArchiveEntry>? ZipEntries { get; init; }
        public IArchive? Archive { get; init; }
        public Dictionary<string, IArchiveEntry>? Entries { get; init; }
        public Stream? Image { get; init; }
        public Dictionary<string, IsoReader.IsoFile>? IsoFiles { get; init; }
        public bool Solid { get; init; }
        public readonly object Gate = new();

        public void Dispose()
        {
            Zip?.Dispose();
            Archive?.Dispose();
            Image?.Dispose();
        }
    }

    readonly object _lock = new();
    readonly LinkedList<Handle> _open = new(); // en son kullanılan başta

    /// <summary>Girdinin içeriği; çok büyükse, bulunamazsa, şifreliyse ya da solid arşivdeyse null.</summary>
    public byte[]? Read(ArchiveRecord rec, string entryPath, long maxBytes)
    {
        string? password = PasswordStore.For(rec);
        string path;
        ArchiveFormat format;
        if (rec.IsNested)
        {
            if (rec.FileSize > MaxNestedMaterialize) return null;
            try { path = ArchiveExtractor.InnerArchiveFile(rec, password); }
            catch (Exception) { return null; }
            format = ArchiveReader.FormatOf(rec.InnerPath);
        }
        else
        {
            path = rec.Path;
            format = ArchiveReader.FormatOf(path);
        }
        return Read(path, format, entryPath, maxBytes, password);
    }

    /// <summary>Diskteki bir arşivden okur (komut satırı ve testler için).</summary>
    public byte[]? Read(string archivePath, string entryPath, long maxBytes) =>
        Read(archivePath, ArchiveReader.FormatOf(archivePath), entryPath, maxBytes, null);

    byte[]? Read(string path, ArchiveFormat format, string entryPath, long maxBytes, string? password)
    {
        Handle? h;
        try { h = Get(path, format, password); }
        catch (Exception) { return null; }
        if (h == null || h.Solid) return null;

        lock (h.Gate)
        {
            try
            {
                Stream src;
                if (h.ZipEntries != null)
                {
                    if (!h.ZipEntries.TryGetValue(entryPath, out var ze) || ze.Length > maxBytes || ze.IsEncrypted) return null;
                    src = ze.Open();
                }
                else if (h.IsoFiles != null)
                {
                    if (!h.IsoFiles.TryGetValue(entryPath, out var f) || f.Size > maxBytes) return null;
                    src = IsoReader.Open(h.Image!, f);
                }
                else
                {
                    if (!h.Entries!.TryGetValue(entryPath, out var e) || e.Size > maxBytes || (e.IsEncrypted && password == null)) return null;
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

    Handle? Get(string path, ArchiveFormat format, string? password)
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

        var h = Open(path, format, password);
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

    static Handle Open(string path, ArchiveFormat format, string? password)
    {
        if (format == ArchiveFormat.CompressedTar) return new Handle { Path = path, Solid = true };

        var fs = ArchiveReader.OpenShared(path);
        try
        {
            if (format == ArchiveFormat.Iso)
            {
                var files = new Dictionary<string, IsoReader.IsoFile>(StringComparer.Ordinal);
                foreach (var f in IsoReader.List(fs)) if (!f.IsDirectory) files.TryAdd(f.Path, f);
                return new Handle { Path = path, Image = fs, IsoFiles = files };
            }

            if (format == ArchiveFormat.Zip)
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

            fs.Dispose();
            var archive = ArchiveReader.OpenSharp(format == ArchiveFormat.Rar ? ArchiveReader.RarParts(path) : [path], password);
            // Önce girdiler: başlığı şifreli RAR5'te IsSolid girdilerden önce sorulursa liste boş döner (SharpCompress).
            var all = archive.Entries.ToList();
            bool solid = archive.IsSolid;
            var entries = new Dictionary<string, IArchiveEntry>(StringComparer.Ordinal);
            if (!solid)
                foreach (var e in all)
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
