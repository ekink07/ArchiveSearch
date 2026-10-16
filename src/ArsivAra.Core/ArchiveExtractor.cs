using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Readers;

namespace ArsivAra.Core;

/// <summary>
/// Extracts files from archives. In ZIP, ISO and non-solid archives the requested entry is accessed directly;
/// in solid archives (7z, compressed tar) the archive is read once from start to end and the requested entries are taken along the way.
/// For a nested archive the inner archive is first extracted to a temporary folder. The archive's
/// "downloaded from the internet" mark (Mark-of-the-Web) is copied to extracted files, so SmartScreen and Office Protected View stay in effect.
/// </summary>
public static class ArchiveExtractor
{
    public readonly record struct Item(string Path, long Size);

    static readonly object InnerLock = new();

    /// <summary>Finds the requested entries and calls <paramref name="onEntry"/> once for each (the stream is valid only during the call).</summary>
    public static void ForEach(ArchiveRecord rec, IReadOnlySet<string> paths, string? password, Action<Item, Stream> onEntry, CancellationToken ct = default)
    {
        if (!rec.IsNested)
        {
            ForEach(rec.Path, paths, password, onEntry, ct);
            return;
        }
        string inner = InnerArchiveFile(rec, password, ct);
        ForEachInFile(inner, ArchiveReader.FormatOf(rec.InnerPath), paths, password, onEntry, ct);
    }

    public static void ForEach(string archivePath, IReadOnlySet<string> paths, string? password, Action<Item, Stream> onEntry, CancellationToken ct = default) =>
        ForEachInFile(archivePath, ArchiveReader.FormatOf(archivePath), paths, password, onEntry, ct);

    static void ForEachInFile(string file, ArchiveFormat format, IReadOnlySet<string> wanted, string? password,
        Action<Item, Stream> onEntry, CancellationToken ct)
    {
        if (wanted.Count == 0) return;
        var done = new HashSet<string>(StringComparer.Ordinal);
        switch (format)
        {
            case ArchiveFormat.Zip:
                if (FromZip(file, wanted, done, onEntry, ct)) return;
                break; // not a ZIP, or encrypted/unsupported compression: the rest via SharpCompress
            case ArchiveFormat.Iso:
                using (var fs = ArchiveReader.OpenShared(file)) FromIso(fs, wanted, done, onEntry, ct);
                return;
            case ArchiveFormat.CompressedTar:
                using (var fs = ArchiveReader.OpenShared(file))
                using (var reader = ReaderFactory.OpenReader(fs, ArchiveReader.CreateReaderOptions(password, leaveOpen: true)))
                    Drain(reader, wanted, done, onEntry, ct);
                return;
        }
        FromSharp(format == ArchiveFormat.Rar ? ArchiveReader.RarParts(file) : [file], wanted, done, password, onEntry, ct);
    }

    /// <returns>true: the requested entries were taken from the ZIP; false: the rest should be tried with SharpCompress.</returns>
    static bool FromZip(string file, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        var fs = ArchiveReader.OpenShared(file);
        ZipArchive zip;
        try { zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, ArchiveReader.LegacyNameEncoding); }
        catch (InvalidDataException) { fs.Dispose(); return false; }

        using (zip)
        {
            foreach (var e in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string key = ArchiveReader.NormalizePath(e.FullName);
                if (key.EndsWith('/') || !wanted.Contains(key) || done.Contains(key)) continue;
                if (e.IsEncrypted) return false; // .NET cannot open encrypted ZIPs; SharpCompress opens them with the password
                Stream s;
                try { s = e.Open(); }
                catch (InvalidDataException) { return false; } // unsupported compression method (LZMA, BZip2, PPMd...)
                using (s) onEntry(new Item(key, e.Length), s);
                done.Add(key);
                if (done.Count == wanted.Count) break;
            }
        }
        return true;
    }

    static void FromSharp(IReadOnlyList<string> parts, IReadOnlySet<string> wanted, HashSet<string> done, string? password,
        Action<Item, Stream> onEntry, CancellationToken ct)
    {
        using var archive = ArchiveReader.OpenSharp(parts, password);
        // Entries first: for a RAR5 with encrypted headers, querying IsSolid before the entries returns an empty list (SharpCompress).
        var entries = archive.Entries.ToList();
        if (!archive.IsSolid)
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.IsDirectory) continue;
                string key = ArchiveReader.NormalizePath(e.Key ?? "");
                if (!wanted.Contains(key) || !done.Add(key)) continue;
                using (var s = e.OpenEntryStream()) onEntry(new Item(key, e.Size), s);
                if (done.Count == wanted.Count) return;
            }
            return;
        }
        using var reader = archive.ExtractAllEntries();
        Drain(reader, wanted, done, onEntry, ct);
    }

    /// <summary>Solid archive or stream: entries are walked in order and the requested ones are read along the way.</summary>
    static void Drain(IReader reader, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var e = reader.Entry;
            if (e.IsDirectory) continue;
            string key = ArchiveReader.NormalizePath(e.Key ?? "");
            if (!wanted.Contains(key) || !done.Add(key)) continue;
            using (var s = reader.OpenEntryStream()) onEntry(new Item(key, e.Size), s);
            if (done.Count == wanted.Count) return;
        }
    }

    static void FromIso(Stream image, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        foreach (var f in IsoReader.List(image))
        {
            ct.ThrowIfCancellationRequested();
            if (f.IsDirectory || !wanted.Contains(f.Path) || !done.Add(f.Path)) continue;
            using (var s = IsoReader.Open(image, f)) onEntry(new Item(f.Path, f.Size), s);
            if (done.Count == wanted.Count) return;
        }
    }

    // ---- Extracting to a temporary folder ----

    /// <summary>Extracts a nested archive to a temporary folder once; later calls reuse the same file.</summary>
    public static string InnerArchiveFile(ArchiveRecord rec, string? password, CancellationToken ct = default)
    {
        if (!rec.IsNested) return rec.Path;
        string file = Path.Combine(AppPaths.ExtractDir, "ic",
            Id(rec.Path, rec.InnerPath!, rec.FileSize.ToString(), rec.FileMtimeUtcTicks.ToString()) + ArchiveReader.ExtensionOf(rec.InnerPath!));
        lock (InnerLock)
        {
            if (File.Exists(file) && new FileInfo(file).Length == rec.FileSize) return file;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            bool found = false;
            try
            {
                ForEach(rec.Path, Single(rec.InnerPath!), password, (_, s) =>
                {
                    using var d = File.Create(tmp);
                    s.CopyTo(d);
                    found = true;
                }, ct);
                if (!found) throw new FileNotFoundException("Inner archive not found: " + rec.InnerPath);
                File.Move(tmp, file, overwrite: true);
            }
            finally { TryDelete(tmp); }
            Motw.Propagate(rec.Path, file);
            return file;
        }
    }

    /// <summary>
    /// Extracts a single file to a temporary folder and returns its path. If the same file was extracted before (e.g. it is still open in a program)
    /// it is not rewritten; if it is open but different, a numbered folder is created next to it.
    /// </summary>
    public static string ExtractToTemp(ArchiveRecord rec, ArchiveEntry entry, string? password = null, CancellationToken ct = default)
    {
        string dir = Path.Combine(AppPaths.ExtractDir, Id(rec.DisplayPath, entry.Path, entry.Size.ToString(), entry.MtimeUtcTicks.ToString()));
        string dest = Path.Combine(dir, ArchiveReader.SafeFileName(entry.Path));
        if (File.Exists(dest) && new FileInfo(dest).Length == entry.Size)
        {
            Touch(dir);
            return dest;
        }

        Directory.CreateDirectory(dir);
        string tmp = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        bool found = false;
        try
        {
            ForEach(rec, Single(entry.Path), password, (_, s) =>
            {
                using var d = File.Create(tmp);
                s.CopyTo(d);
                found = true;
            }, ct);
            if (!found) throw new FileNotFoundException("Not found in archive: " + entry.Path);
            dest = MoveIntoPlace(tmp, dest);
        }
        finally { TryDelete(tmp); }
        Motw.Propagate(rec.Path, dest);
        return dest;
    }

    /// <summary>
    /// Writes the selected files from an archive to a folder. <paramref name="relative"/>: entry path → path relative to the destination folder.
    /// If a file with the same name exists, it is written as "name (2).ext". <paramref name="onFile"/> is called for each file written.
    /// </summary>
    public static int ExtractGroup(ArchiveRecord rec, IReadOnlyDictionary<string, string> relative, string destDir, string? password,
        Action<string>? onFile = null, CancellationToken ct = default)
    {
        int count = 0;
        string root = Path.GetFullPath(destDir);
        ForEach(rec, relative.Keys.ToHashSet(StringComparer.Ordinal), password, (item, s) =>
        {
            string target = UniqueName(SafeCombine(root, relative[item.Path]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string tmp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            try
            {
                using (var d = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write)) s.CopyTo(d);
                File.Move(tmp, target);
            }
            finally { TryDelete(tmp); }
            Motw.Propagate(rec.Path, target);
            count++;
            onFile?.Invoke(target);
        }, ct);
        return count;
    }

    /// <summary>Safely places a relative path under the destination folder: "..", drive names and invalid characters are removed.</summary>
    public static string SafeCombine(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not "." and not "..")
            .Select(ArchiveReader.SafeSegment)
            .ToArray();
        if (parts.Length == 0) throw new IOException("Invalid path: " + relative);
        string full = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid path: " + relative);
        return full;
    }

    static string UniqueName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    /// <summary>If the destination is open in another program and different (the move fails), a numbered folder is created next to it.</summary>
    static string MoveIntoPlace(string tmp, string dest)
    {
        try
        {
            File.Move(tmp, dest, overwrite: true);
            return dest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        string dir = Path.GetDirectoryName(dest)!, name = Path.GetFileName(dest);
        for (int i = 2; i < 100; i++)
        {
            string alt = Path.Combine(dir + "-" + i, name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(alt)!);
                File.Move(tmp, alt, overwrite: true);
                return alt;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        throw new IOException("Could not write temporary file: " + dest);
    }

    static HashSet<string> Single(string path) => new(StringComparer.Ordinal) { path };

    static string Id(params string[] parts) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))))[..16];

    static void Touch(string dir)
    {
        try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch (Exception) { }
    }

    static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
    }
}
