namespace ArsivAra.Core;

/// <summary>
/// A single file/folder record inside an archive. Path always uses '/' as the separator and ends with '/' for folders.
/// Crc: the CRC32 of the content if known (ZIP, RAR, 7z), otherwise 0; used for finding duplicates.
/// </summary>
public readonly record struct ArchiveEntry(string Path, long Size, long MtimeUtcTicks, bool IsDirectory, uint Crc = 0)
{
    public string Name
    {
        get
        {
            var p = Path.AsSpan().TrimEnd('/');
            int i = p.LastIndexOf('/');
            return (i < 0 ? p : p[(i + 1)..]).ToString();
        }
    }

    public string Folder
    {
        get
        {
            var p = Path.AsSpan().TrimEnd('/');
            int i = p.LastIndexOf('/');
            return i < 0 ? "" : p[..i].ToString();
        }
    }
}

/// <summary>Why the archive could not be read. The UI shows the text in its own language; details are in <see cref="ArchiveRecord.Error"/>.</summary>
public enum ArchiveError : byte
{
    None = 0,
    Other = 1,
    /// <summary>The file list is encrypted: a password is required (or the given password is wrong).</summary>
    Encrypted = 2,
    AccessDenied = 3,
    InUse = 4,
}

/// <summary>An archive file on disk (or an archive inside an archive) and the list of its contents.</summary>
public sealed class ArchiveRecord
{
    /// <summary>The archive file on disk. For a nested archive, the path of the outermost archive.</summary>
    public required string Path { get; init; }

    /// <summary>For a nested archive, its path inside the outer archive ("Backups/old.zip"); null for an archive on disk.</summary>
    public string? InnerPath { get; init; }

    public long FileSize { get; init; }
    public long FileMtimeUtcTicks { get; init; }

    /// <summary>Detailed error message if the archive could not be read (encrypted header, corrupt file...).</summary>
    public string? Error { get; init; }
    public ArchiveError ErrorKind { get; init; }

    public ArchiveEntry[] Entries { get; init; } = [];

    /// <summary>Archives found and read inside this archive (one level).</summary>
    public ArchiveRecord[] Nested { get; init; } = [];

    /// <summary>Solid archive: reaching a single file requires unpacking the ones before it too (preview and thumbnails are slow).</summary>
    public bool IsSolid { get; init; }

    /// <summary>Added by the user (true) or found by a disk scan (false).</summary>
    public bool Manual { get; set; } = true;

    public bool IsNested => InnerPath != null;
    public bool NeedsPassword => ErrorKind == ArchiveError.Encrypted;

    /// <summary>Name shown to the user: "backup.zip" or "backup.zip › old.zip".</summary>
    public string DisplayName => InnerPath == null
        ? System.IO.Path.GetFileName(Path)
        : System.IO.Path.GetFileName(Path) + " › " + LastSegment(InnerPath);

    /// <summary>Full path: "D:\backup.zip" or "D:\backup.zip › Backups/old.zip".</summary>
    public string DisplayPath => InnerPath == null ? Path : Path + " › " + InnerPath;

    /// <summary>Its own entries plus the entries of its nested archives.</summary>
    public int TotalEntryCount
    {
        get
        {
            int n = Entries.Length;
            foreach (var r in Nested) n += r.Entries.Length;
            return n;
        }
    }

    /// <summary>If the file has not changed, the previous index record can be reused.</summary>
    public bool IsSameFile(long size, long mtimeUtcTicks) => FileSize == size && FileMtimeUtcTicks == mtimeUtcTicks;

    static string LastSegment(string p)
    {
        var s = p.AsSpan().TrimEnd('/');
        int i = s.LastIndexOf('/');
        return (i < 0 ? s : s[(i + 1)..]).ToString();
    }
}
