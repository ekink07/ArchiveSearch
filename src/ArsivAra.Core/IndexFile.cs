using System.IO.Compression;

namespace ArsivAra.Core;

/// <summary>The index file is not a recognized version (a newer Arşiv Ara may have written it).</summary>
public sealed class IndexVersionException(int version) : IOException($"Index version {version} is not supported")
{
    public int Version { get; } = version;
}

/// <summary>
/// Saves/loads the index to/from disk (Brotli-compressed binary format).
/// Version 1: archive path, size, date, error, entries (path, size, date, is folder).
/// Version 2: + error kind, manually-added/solid flags, entry CRC, nested archives.
/// An older version is read and written back in the new version; an unrecognized file is never overwritten (see <see cref="LoadForUse"/>).
/// </summary>
public static class IndexFile
{
    const int Magic = 0x56535241; // "ARSV"
    public const int CurrentVersion = 2;

    public enum LoadStatus { Missing, Ok, Migrated, Unreadable }

    public static void Save(IReadOnlyCollection<ArchiveRecord> archives, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var br = new BrotliStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(new BufferedStream(br, 1 << 20)))
        {
            w.Write(Magic);
            w.Write(CurrentVersion);
            w.Write(archives.Count);
            foreach (var a in archives) WriteRecord(w, a, withNested: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    static void WriteRecord(BinaryWriter w, ArchiveRecord a, bool withNested)
    {
        w.Write(a.Path);
        w.Write(a.InnerPath ?? "");
        w.Write(a.FileSize);
        w.Write(a.FileMtimeUtcTicks);
        w.Write(a.Error ?? "");
        w.Write((byte)a.ErrorKind);
        w.Write((byte)((a.Manual ? 1 : 0) | (a.IsSolid ? 2 : 0)));
        w.Write(a.Entries.Length);
        foreach (var e in a.Entries)
        {
            w.Write(e.Path);
            w.Write(e.Size);
            w.Write(e.MtimeUtcTicks);
            w.Write(e.IsDirectory);
            w.Write(e.Crc);
        }
        if (!withNested) return;
        w.Write(a.Nested.Length);
        foreach (var n in a.Nested) WriteRecord(w, n, withNested: false);
    }

    public static List<ArchiveRecord> Load(string path) => LoadWithVersion(path).Archives;

    /// <summary>Reads the file; also returns the file's version (if 1, it has been converted).</summary>
    public static (List<ArchiveRecord> Archives, int Version) LoadWithVersion(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var br = new BrotliStream(fs, CompressionMode.Decompress);
        using var r = new BinaryReader(new BufferedStream(br, 1 << 16));
        if (r.ReadInt32() != Magic) throw new InvalidDataException("Index file not recognized");
        int version = r.ReadInt32();
        if (version is < 1 or > CurrentVersion) throw new IndexVersionException(version);

        int count = r.ReadInt32();
        var list = new List<ArchiveRecord>(count);
        for (int i = 0; i < count; i++) list.Add(version == 1 ? ReadV1(r) : ReadV2(r, withNested: true));
        return (list, version);
    }

    static ArchiveRecord ReadV2(BinaryReader r, bool withNested)
    {
        string path = r.ReadString();
        string inner = r.ReadString();
        long size = r.ReadInt64();
        long mtime = r.ReadInt64();
        string err = r.ReadString();
        var kind = (ArchiveError)r.ReadByte();
        byte flags = r.ReadByte();
        var entries = new ArchiveEntry[r.ReadInt32()];
        for (int j = 0; j < entries.Length; j++)
            entries[j] = new ArchiveEntry(r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean(), r.ReadUInt32());
        var nested = Array.Empty<ArchiveRecord>();
        if (withNested)
        {
            nested = new ArchiveRecord[r.ReadInt32()];
            for (int j = 0; j < nested.Length; j++) nested[j] = ReadV2(r, withNested: false);
        }
        return new ArchiveRecord
        {
            Path = path, InnerPath = inner.Length == 0 ? null : inner, FileSize = size, FileMtimeUtcTicks = mtime,
            Error = err.Length == 0 ? null : err, ErrorKind = kind,
            Manual = (flags & 1) != 0, IsSolid = (flags & 2) != 0,
            Entries = entries, Nested = nested,
        };
    }

    /// <summary>Version 1: the error kind is inferred from the text, folder paths get a trailing '/', and all archives were added manually.</summary>
    static ArchiveRecord ReadV1(BinaryReader r)
    {
        string p = r.ReadString();
        long size = r.ReadInt64();
        long mtime = r.ReadInt64();
        string err = r.ReadString();
        var entries = new ArchiveEntry[r.ReadInt32()];
        for (int j = 0; j < entries.Length; j++)
        {
            string path = r.ReadString();
            long esize = r.ReadInt64(), etime = r.ReadInt64();
            bool dir = r.ReadBoolean();
            if (dir && !path.EndsWith('/')) path += "/";
            entries[j] = new ArchiveEntry(path, esize, etime, dir);
        }
        return new ArchiveRecord
        {
            Path = p, FileSize = size, FileMtimeUtcTicks = mtime,
            Error = err.Length == 0 ? null : err, ErrorKind = err.Length == 0 ? ArchiveError.None : KindFromV1Text(err),
            Manual = true, Entries = entries,
        };
    }

    // Version-1 files stored only the (Turkish) error text, so the kind is recovered from it.
    // These literals match texts already written to users' files and must not be translated.
    static ArchiveError KindFromV1Text(string err) =>
        err.Contains("Şifreli", StringComparison.OrdinalIgnoreCase) ? ArchiveError.Encrypted
        : err.Contains("Erişim engellendi", StringComparison.OrdinalIgnoreCase) ? ArchiveError.AccessDenied
        : err.Contains("kullanılıyor", StringComparison.OrdinalIgnoreCase) ? ArchiveError.InUse
        : ArchiveError.Other;

    /// <summary>
    /// The safe load used by the application:
    /// an older-version file is read and, the first time, backed up as "index.bin.v1.bak" (the next save writes the new version);
    /// an unreadable or newer-version file is moved aside as "index.bin.okunamadi-....bak", so it is not overwritten.
    /// </summary>
    public static (List<ArchiveRecord> Archives, LoadStatus Status, string? Backup) LoadForUse(string path)
    {
        if (!File.Exists(path)) return ([], LoadStatus.Missing, null);
        try
        {
            var (list, version) = LoadWithVersion(path);
            if (version == CurrentVersion) return (list, LoadStatus.Ok, null);
            string backup = $"{path}.v{version}.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            Log.Info($"Index converted from version {version} to {CurrentVersion}, backup: {backup}");
            return (list, LoadStatus.Migrated, backup);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            string? backup = $"{path}.okunamadi-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            try { File.Move(path, backup); }
            catch (Exception) { backup = null; }
            Log.Error($"Could not read index; moved aside: {backup ?? "(could not be moved)"}", ex);
            return ([], LoadStatus.Unreadable, backup);
        }
    }

    /// <summary>For the command-line tool: an empty list if the file cannot be read.</summary>
    public static List<ArchiveRecord> TryLoad(string path)
    {
        try { return File.Exists(path) ? Load(path) : []; }
        catch (Exception) { return []; }
    }
}
