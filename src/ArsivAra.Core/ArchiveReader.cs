using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ArsivAra.Core;

public enum ArchiveFormat { Unknown, Zip, Rar, SevenZip, Tar, CompressedTar, Iso }

/// <summary>Arşiv okuma seçenekleri.</summary>
public sealed record ReadOptions
{
    /// <summary>Şifreli arşivin parolası (dosya listesi ya da içerik şifreliyse).</summary>
    public string? Password { get; init; }

    /// <summary>Arşivin içindeki arşivler (zip içinde zip) de listelensin mi.</summary>
    public bool ReadNested { get; init; } = true;

    /// <summary>İç içe arşiv en fazla bu kadar büyükse okunur (listelemek için açılması gerekir).</summary>
    public long NestedMaxBytes { get; init; } = 256L << 20;

    public int NestedMaxCount { get; init; } = 64;

    /// <summary>Solid dış arşivde (7z, sıkıştırılmış tar) içteki arşive ulaşmak öncekileri açmayı gerektirir; bundan büyükse bakılmaz.</summary>
    public long NestedSolidMaxBytes { get; init; } = 512L << 20;

    public static ReadOptions Default { get; } = new();
}

/// <summary>
/// Arşivlerin sadece dosya listesini okur; içerik açılmaz.
/// ZIP: dosya sonundaki merkezi dizin. RAR5: Quick Open kaydı (<see cref="Rar5Lister"/>), çok parçalı ve şifreli RAR'lar SharpCompress ile.
/// 7z/TAR: SharpCompress. tar.gz/bz2/xz/zst: baştan sona akış olarak. ISO: <see cref="IsoReader"/> (UDF, Joliet, Rock Ridge).
/// </summary>
public static partial class ArchiveReader
{
    static readonly HashSet<string> ZipExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".zipx", ".jar", ".cbz", ".apk", ".nupkg" };
    static readonly HashSet<string> RarExtensions = new(StringComparer.OrdinalIgnoreCase) { ".rar", ".cbr" };
    static readonly HashSet<string> SevenZipExtensions = new(StringComparer.OrdinalIgnoreCase) { ".7z", ".cb7" };
    static readonly HashSet<string> TarExtensions = new(StringComparer.OrdinalIgnoreCase) { ".tar", ".cbt" };
    static readonly HashSet<string> CompressedTarExtensions = new(StringComparer.OrdinalIgnoreCase) { ".tgz", ".tbz", ".tbz2", ".txz", ".tzst", ".tlz" };
    static readonly HashSet<string> IsoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".iso" };

    /// <summary>Sadece "x.tar" + bu sıkıştırma uzantısı arşiv sayılır (tek başına .gz dosyaları değil).</summary>
    static readonly HashSet<string> TarCompressionExtensions = new(StringComparer.OrdinalIgnoreCase) { ".gz", ".bz2", ".xz", ".zst", ".lz" };

    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ZipLookup = ZipExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> RarLookup = RarExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> SevenZipLookup = SevenZipExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> TarLookup = TarExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> CompressedTarLookup = CompressedTarExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> IsoLookup = IsoExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> TarCompressionLookup = TarCompressionExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Dosya seçme penceresi için desenler ("*.zip", "*.tar.gz"...).</summary>
    public static IReadOnlyList<string> FilePatterns { get; } =
    [
        .. new[] { ZipExtensions, RarExtensions, SevenZipExtensions, TarExtensions, CompressedTarExtensions, IsoExtensions }
            .SelectMany(s => s).Select(e => "*" + e),
        .. TarCompressionExtensions.Select(e => "*.tar" + e),
    ];

    /// <summary>UTF-8 bayrağı olmayan ZIP/RAR adları için çözücü (bkz. <see cref="NameEncoding"/>).</summary>
    public static Encoding LegacyNameEncoding { get; set; } = NameEncoding.CreateDefault();

    [GeneratedRegex(@"\.part(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPartRegex();

    public static ArchiveFormat FormatOf(ReadOnlySpan<char> fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.IsEmpty) return ArchiveFormat.Unknown;
        if (ZipLookup.Contains(ext)) return ArchiveFormat.Zip;
        if (RarLookup.Contains(ext)) return ArchiveFormat.Rar;
        if (SevenZipLookup.Contains(ext)) return ArchiveFormat.SevenZip;
        if (TarLookup.Contains(ext)) return ArchiveFormat.Tar;
        if (CompressedTarLookup.Contains(ext)) return ArchiveFormat.CompressedTar;
        if (IsoLookup.Contains(ext)) return ArchiveFormat.Iso;
        if (TarCompressionLookup.Contains(ext) && Path.GetExtension(fileName[..^ext.Length]).Equals(".tar", StringComparison.OrdinalIgnoreCase))
            return ArchiveFormat.CompressedTar;
        return ArchiveFormat.Unknown;
    }

    /// <summary>Uzantı (".zip", sıkıştırılmış tar'da ".tar.gz"): geçici dosya adlarında biçim korunsun diye.</summary>
    public static string ExtensionOf(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (TarCompressionExtensions.Contains(ext) && Path.GetExtension(fileName[..^ext.Length]).Equals(".tar", StringComparison.OrdinalIgnoreCase))
            return ".tar" + ext;
        return ext;
    }

    public static bool IsArchiveName(ReadOnlySpan<char> fileName)
    {
        var format = FormatOf(fileName);
        if (format == ArchiveFormat.Unknown) return false;

        // Çok parçalı RAR'da sadece ilk parçayı indeksle (x.part2.rar, x.part3.rar... atlanır; eski adlandırmada .r00 zaten arşiv sayılmaz).
        if (format == ArchiveFormat.Rar)
        {
            foreach (var m in RarPartRegex().EnumerateMatches(fileName))
            {
                var digits = fileName.Slice(m.Index + 5, m.Length - 9);
                return int.TryParse(digits, out int part) && part <= 1;
            }
        }
        return true;
    }

    // 64 KB tampon: büyük ZIP'lerin merkezi dizini sıralı okunurken binlerce küçük okuma çağrısı olmasın.
    internal static FileStream OpenShared(string path, int bufferSize = 64 * 1024) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.RandomAccess);

    public static ArchiveRecord Read(string path, long size, long mtimeUtcTicks, ReadOptions? options = null)
    {
        options ??= ReadOptions.Default;
        try
        {
            var listing = ListFile(path, options.Password);
            var nested = options.ReadNested ? ReadNested(path, size, listing, options) : [];
            return new ArchiveRecord
            {
                Path = path, FileSize = size, FileMtimeUtcTicks = mtimeUtcTicks,
                Entries = listing.Entries, IsSolid = listing.Solid, Nested = nested,
            };
        }
        catch (Exception ex)
        {
            var (kind, message) = Describe(ex, options.Password != null);
            return new ArchiveRecord { Path = path, FileSize = size, FileMtimeUtcTicks = mtimeUtcTicks, Error = message, ErrorKind = kind };
        }
    }

    internal readonly record struct Listing(ArchiveEntry[] Entries, bool Solid);

    static Listing ListFile(string path, string? password)
    {
        switch (FormatOf(path))
        {
            case ArchiveFormat.Zip:
                try
                {
                    using var fs = OpenShared(path);
                    return new Listing(ReadZip(fs), false);
                }
                catch (InvalidDataException) { } // uzantısı .zip ama başka biçim
                return ListSharp([path], password);

            case ArchiveFormat.Rar:
                if (password == null && TryReadRar5QuickOpen(path, out bool solid) is { } fast) return new Listing(fast, solid);
                return ListSharp(RarParts(path), password);

            case ArchiveFormat.CompressedTar:
                using (var fs = OpenShared(path)) return ListForwardOnly(fs, password);

            case ArchiveFormat.Iso:
                using (var fs = OpenShared(path)) return new Listing(ListIso(fs), false);

            default:
                return ListSharp([path], password);
        }
    }

    /// <summary>Arşiv bir akışta (iç içe arşiv): biçim ada göre seçilir.</summary>
    internal static Listing ListStream(Stream s, string nameHint, string? password)
    {
        switch (FormatOf(nameHint))
        {
            case ArchiveFormat.Zip:
                try { return new Listing(ReadZip(s), false); }
                catch (InvalidDataException) { s.Position = 0; }
                return ListSharp(s, password);

            case ArchiveFormat.Rar:
                if (password == null)
                {
                    s.Position = 0;
                    if (Rar5Lister.TryList(s, out bool solid) is { } fast) return new Listing(fast, solid);
                    s.Position = 0;
                }
                return ListSharp(s, password);

            case ArchiveFormat.CompressedTar:
                return ListForwardOnly(s, password);

            case ArchiveFormat.Iso:
                return new Listing(ListIso(s), false);

            default:
                return ListSharp(s, password);
        }
    }

    /// <summary>RAR5 + Quick Open varsa hızlı yol; aksi halde (çok parçalı, şifreli, eski RAR ya da herhangi bir hatada) null.</summary>
    public static ArchiveEntry[]? TryReadRar5QuickOpen(string path) => TryReadRar5QuickOpen(path, out _);

    static ArchiveEntry[]? TryReadRar5QuickOpen(string path, out bool solid)
    {
        solid = false;
        try
        {
            using var fs = OpenShared(path, bufferSize: 0); // Rar5Lister kendi 64 KB penceresini tutar
            return Rar5Lister.TryList(fs, out solid);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Çok parçalı RAR'ın tüm parçaları (tek parçalıysa sadece kendisi).</summary>
    internal static IReadOnlyList<string> RarParts(string path)
    {
        try
        {
            var parts = ArchiveFactory.GetFileParts(path).ToList();
            return parts.Count > 1 ? parts : [path];
        }
        catch (Exception) { return [path]; }
    }

    static ArchiveEntry[] ReadZip(Stream s)
    {
        using var zip = new ZipArchive(s, ZipArchiveMode.Read, leaveOpen: true, LegacyNameEncoding);
        var entries = zip.Entries;
        var result = new ArchiveEntry[entries.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var e = entries[i];
            string name = NormalizePath(e.FullName);
            bool dir = name.EndsWith('/');
            result[i] = new ArchiveEntry(name, dir ? 0 : e.Length, SafeUtcTicks(e.LastWriteTime), dir, dir ? 0 : e.Crc32);
        }
        return result;
    }

    internal static ReaderOptions CreateReaderOptions(string? password, bool leaveOpen = false) => new()
    {
        ArchiveEncoding = new ArchiveEncoding { Default = LegacyNameEncoding },
        Password = password,
        LeaveStreamOpen = leaveOpen,
    };

    internal static IArchive OpenSharp(IReadOnlyList<string> parts, string? password) => parts.Count > 1
        ? ArchiveFactory.OpenArchive(parts.Select(p => new FileInfo(p)).ToList(), CreateReaderOptions(password))
        : ArchiveFactory.OpenArchive(parts[0], CreateReaderOptions(password));

    /// <summary>SharpCompress ile okur (Quick Open'ı doğrulamak için; çok parçalı RAR'da tüm parçalar).</summary>
    public static ArchiveEntry[] ReadWithSharpCompress(string path, string? password = null) =>
        ListSharp(FormatOf(path) == ArchiveFormat.Rar ? RarParts(path) : [path], password).Entries;

    // Önce girdiler, sonra IsSolid: başlığı şifreli RAR5'te IsSolid önce sorulursa liste boş döner (SharpCompress).
    static Listing ListSharp(IReadOnlyList<string> parts, string? password)
    {
        using var archive = OpenSharp(parts, password);
        var entries = ToEntries(archive.Entries);
        return new Listing(entries, archive.IsSolid);
    }

    static Listing ListSharp(Stream s, string? password)
    {
        s.Position = 0;
        using var archive = ArchiveFactory.OpenArchive(s, CreateReaderOptions(password, leaveOpen: true));
        var entries = ToEntries(archive.Entries);
        return new Listing(entries, archive.IsSolid);
    }

    /// <summary>Sıkıştırılmış tar (ve tek dosyalık .gz): merkezi dizin yok, baştan sona okunur.</summary>
    static Listing ListForwardOnly(Stream s, string? password)
    {
        using var reader = ReaderFactory.OpenReader(s, CreateReaderOptions(password, leaveOpen: true));
        var list = new List<IEntry>();
        while (reader.MoveToNextEntry()) list.Add(reader.Entry);
        return new Listing(ToEntries(list), true);
    }

    static ArchiveEntry[] ListIso(Stream s)
    {
        var files = IsoReader.List(s);
        var result = new ArchiveEntry[files.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var f = files[i];
            result[i] = new ArchiveEntry(f.Path, f.IsDirectory ? 0 : f.Size, f.MtimeUtcTicks, f.IsDirectory);
        }
        return result;
    }

    /// <summary>SharpCompress girdileri: klasörler '/' ile biter; çok parçalı arşivde aynı dosya birden fazla görünürse bir kez alınır.</summary>
    static ArchiveEntry[] ToEntries(IEnumerable<IEntry> entries)
    {
        var result = new List<ArchiveEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            string name = NormalizePath(e.Key ?? "");
            if (name.Length == 0) continue;
            bool dir = e.IsDirectory || name.EndsWith('/');
            if (dir && !name.EndsWith('/')) name += "/";
            if (!seen.Add(name)) continue;
            long ticks = e.LastModifiedTime is { } dt ? SafeUtcTicks(dt) : 0;
            result.Add(new ArchiveEntry(name, dir ? 0 : Math.Max(0, e.Size), ticks, dir, dir ? 0 : unchecked((uint)e.Crc)));
        }
        return [.. result];
    }

    // ---- İç içe arşivler ----

    static ArchiveRecord[] ReadNested(string path, long outerSize, Listing listing, ReadOptions options)
    {
        var candidates = new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in listing.Entries)
        {
            if (e.IsDirectory || e.Size <= 0 || e.Size > options.NestedMaxBytes || !IsArchiveName(e.Name)) continue;
            candidates[e.Path] = e;
            if (candidates.Count >= options.NestedMaxCount) break;
        }
        if (candidates.Count == 0 || (listing.Solid && outerSize > options.NestedSolidMaxBytes)) return [];

        var result = new List<ArchiveRecord>();
        try
        {
            ArchiveExtractor.ForEach(path, candidates.Keys.ToHashSet(StringComparer.Ordinal), options.Password,
                (item, stream) => result.Add(ReadInner(path, candidates[item.Path], stream, options.Password)));
        }
        catch (Exception) { /* içerik şifreli ya da okunamıyor: dış arşivin listesi yine de geçerli */ }
        result.Sort((a, b) => string.CompareOrdinal(a.InnerPath, b.InnerPath));
        return [.. result];
    }

    static ArchiveRecord ReadInner(string outerPath, ArchiveEntry e, Stream content, string? password)
    {
        try
        {
            using var buffered = TempBuffer(content, e.Size);
            var inner = ListStream(buffered, e.Name, password);
            return new ArchiveRecord
            {
                Path = outerPath, InnerPath = e.Path, FileSize = e.Size, FileMtimeUtcTicks = e.MtimeUtcTicks,
                Entries = inner.Entries, IsSolid = inner.Solid,
            };
        }
        catch (Exception ex)
        {
            var (kind, message) = Describe(ex, password != null);
            return new ArchiveRecord
            {
                Path = outerPath, InnerPath = e.Path, FileSize = e.Size, FileMtimeUtcTicks = e.MtimeUtcTicks,
                Error = message, ErrorKind = kind,
            };
        }
    }

    /// <summary>Akışı aranabilir hâle getirir: küçükse bellekte, büyükse kapanınca silinen geçici dosyada.</summary>
    internal static Stream TempBuffer(Stream src, long size)
    {
        Stream dst = size is >= 0 and <= 32L << 20
            ? new MemoryStream((int)size)
            : new FileStream(Path.Combine(Path.GetTempPath(), "arsivara-" + Guid.NewGuid().ToString("N") + ".tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        src.CopyTo(dst);
        dst.Position = 0;
        return dst;
    }

    public static string NormalizePath(string p) => p.Replace('\\', '/').TrimStart('/');

    internal static long SafeUtcTicks(DateTimeOffset d)
    {
        try { return d.UtcTicks; } catch { return 0; }
    }

    internal static long SafeUtcTicks(DateTime d)
    {
        try { return d.Kind == DateTimeKind.Utc ? d.Ticks : d.ToUniversalTime().Ticks; } catch { return 0; }
    }

    /// <summary>Hatanın türü (arayüz kendi dilinde gösterir) ve ayrıntısı.</summary>
    public static (ArchiveError Kind, string Message) Describe(Exception ex, bool passwordGiven = false)
    {
        if (ex is AggregateException { InnerException: { } inner }) ex = inner;
        if (ex is SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException
            || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
            return (ArchiveError.Encrypted, ex.Message);
        if (ex is UnauthorizedAccessException) return (ArchiveError.AccessDenied, ex.Message);
        if (ex is IOException io && io.HResult == unchecked((int)0x80070020)) return (ArchiveError.InUse, ex.Message);
        // Parola verilmişken bozuk görünen veri (7z'de "Data Error", RAR'da bozuk başlık) çoğunlukla yanlış paroladır.
        if (passwordGiven && ex is not (FileNotFoundException or DirectoryNotFoundException or OperationCanceledException or OutOfMemoryException))
            return (ArchiveError.Encrypted, ex.Message);
        return (ArchiveError.Other, $"{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>Arşivdeki yolun son parçasından güvenli bir dosya adı: "../../" gibi yollar dışarı yazamaz.</summary>
    public static string SafeFileName(string entryPath)
    {
        var p = entryPath.Replace('\\', '/').TrimEnd('/');
        return SafeSegment(p[(p.LastIndexOf('/') + 1)..]);
    }

    /// <summary>
    /// Tek bir yol parçası: geçersiz karakterler (: * ? " &lt; &gt; | ve kontrol karakterleri) atılır, sondaki nokta ve boşluk
    /// silinir (Windows siler), Windows'un ayrılmış adlarının (CON, NUL, COM1...) başına "_" eklenir.
    /// </summary>
    public static string SafeSegment(string segment)
    {
        string name = string.Concat(segment.Split(Path.GetInvalidFileNameChars())).TrimEnd('.', ' ');
        if (name.Length == 0) name = "dosya";
        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = "_" + name;
        return name;
    }
}
