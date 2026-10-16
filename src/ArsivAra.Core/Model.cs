namespace ArsivAra.Core;

/// <summary>Arşivin içindeki tek bir dosya/klasör kaydı. Path her zaman '/' ayraçlıdır.</summary>
public readonly record struct ArchiveEntry(string Path, long Size, long MtimeUtcTicks, bool IsDirectory)
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

/// <summary>Diskteki bir arşiv dosyası ve içeriğinin listesi.</summary>
public sealed class ArchiveRecord
{
    public required string Path { get; init; }
    public long FileSize { get; init; }
    public long FileMtimeUtcTicks { get; init; }

    /// <summary>Okunamadıysa (şifreli başlık, bozuk dosya...) hata mesajı.</summary>
    public string? Error { get; init; }

    public ArchiveEntry[] Entries { get; init; } = [];

    /// <summary>Dosya değişmemişse önceki indeks kaydı yeniden kullanılabilir.</summary>
    public bool IsSameFile(long size, long mtimeUtcTicks) => FileSize == size && FileMtimeUtcTicks == mtimeUtcTicks;
}
