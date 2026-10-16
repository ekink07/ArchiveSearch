namespace ArsivAra.Core;

/// <summary>
/// Arşivin içindeki tek bir dosya/klasör kaydı. Path her zaman '/' ayraçlıdır, klasörlerde '/' ile biter.
/// Crc: biliniyorsa içeriğin CRC32'si (ZIP, RAR, 7z), bilinmiyorsa 0; kopya bulmada kullanılır.
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

/// <summary>Arşiv neden okunamadı. Metni arayüz kendi dilinde gösterir; ayrıntı <see cref="ArchiveRecord.Error"/>'dadır.</summary>
public enum ArchiveError : byte
{
    None = 0,
    Other = 1,
    /// <summary>Dosya listesi şifreli: parola gerekir (ya da verilen parola yanlış).</summary>
    Encrypted = 2,
    AccessDenied = 3,
    InUse = 4,
}

/// <summary>Diskteki bir arşiv dosyası (ya da bir arşivin içindeki arşiv) ve içeriğinin listesi.</summary>
public sealed class ArchiveRecord
{
    /// <summary>Diskteki arşiv dosyası. İç içe arşivde en dıştaki arşivin yolu.</summary>
    public required string Path { get; init; }

    /// <summary>İç içe arşivde dış arşivin içindeki yolu ("Yedekler/eski.zip"); diskteki arşivde null.</summary>
    public string? InnerPath { get; init; }

    public long FileSize { get; init; }
    public long FileMtimeUtcTicks { get; init; }

    /// <summary>Okunamadıysa (şifreli başlık, bozuk dosya...) ayrıntılı hata mesajı.</summary>
    public string? Error { get; init; }
    public ArchiveError ErrorKind { get; init; }

    public ArchiveEntry[] Entries { get; init; } = [];

    /// <summary>Bu arşivin içinde bulunup okunan arşivler (tek seviye).</summary>
    public ArchiveRecord[] Nested { get; init; } = [];

    /// <summary>Solid arşiv: tek bir dosyaya ulaşmak öncekileri de açmayı gerektirir (önizleme ve küçük resim yavaş).</summary>
    public bool IsSolid { get; init; }

    /// <summary>Kullanıcı ekledi (true) ya da disk taramasıyla bulundu (false).</summary>
    public bool Manual { get; set; } = true;

    public bool IsNested => InnerPath != null;
    public bool NeedsPassword => ErrorKind == ArchiveError.Encrypted;

    /// <summary>Kullanıcıya gösterilen ad: "yedek.zip" ya da "yedek.zip › eski.zip".</summary>
    public string DisplayName => InnerPath == null
        ? System.IO.Path.GetFileName(Path)
        : System.IO.Path.GetFileName(Path) + " › " + LastSegment(InnerPath);

    /// <summary>Tam yol: "D:\yedek.zip" ya da "D:\yedek.zip › Yedekler/eski.zip".</summary>
    public string DisplayPath => InnerPath == null ? Path : Path + " › " + InnerPath;

    /// <summary>Kendi girdileri ve iç içe arşivlerinin girdileri.</summary>
    public int TotalEntryCount
    {
        get
        {
            int n = Entries.Length;
            foreach (var r in Nested) n += r.Entries.Length;
            return n;
        }
    }

    /// <summary>Dosya değişmemişse önceki indeks kaydı yeniden kullanılabilir.</summary>
    public bool IsSameFile(long size, long mtimeUtcTicks) => FileSize == size && FileMtimeUtcTicks == mtimeUtcTicks;

    static string LastSegment(string p)
    {
        var s = p.AsSpan().TrimEnd('/');
        int i = s.LastIndexOf('/');
        return (i < 0 ? s : s[(i + 1)..]).ToString();
    }
}
