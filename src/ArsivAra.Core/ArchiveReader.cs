using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ArsivAra.Core;

/// <summary>
/// Arşivlerin sadece dosya listesini okur; içerik açılmaz.
/// ZIP: dosya sonundaki Central Directory okunur (en hızlısı).
/// RAR/7z/TAR: SharpCompress başlıktan başlığa atlayarak listeler.
/// </summary>
public static partial class ArchiveReader
{
    static readonly HashSet<string> ZipExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".zipx", ".jar", ".cbz", ".apk", ".nupkg" };

    static readonly HashSet<string> OtherExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".rar", ".cbr", ".7z", ".cb7", ".tar" };

    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ZipLookup = ZipExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
    static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> OtherLookup = OtherExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

    public static IReadOnlyCollection<string> SupportedExtensions => [.. ZipExtensions, .. OtherExtensions];

    /// <summary>UTF-8 bayrağı olmayan ZIP/RAR adları için çözücü (bkz. <see cref="NameEncoding"/>).</summary>
    public static Encoding LegacyNameEncoding { get; set; } = NameEncoding.CreateDefault();

    [GeneratedRegex(@"\.part(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPartRegex();

    public static bool IsArchiveName(ReadOnlySpan<char> fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.IsEmpty) return false;
        if (ZipLookup.Contains(ext)) return true;
        if (!OtherLookup.Contains(ext)) return false;

        // Çok parçalı RAR'da sadece ilk parçayı indeksle (x.part2.rar, x.part3.rar... atlanır).
        if (ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var m in RarPartRegex().EnumerateMatches(fileName))
            {
                var digits = fileName.Slice(m.Index + 5, m.Length - 9);
                return int.TryParse(digits, out int part) && part <= 1;
            }
        }
        return true;
    }

    static bool IsZip(string path) => ZipLookup.Contains(Path.GetExtension(path.AsSpan()));

    public static ArchiveRecord Read(string path, long size, long mtimeUtcTicks)
    {
        try
        {
            ArchiveEntry[] entries;
            if (IsZip(path))
            {
                try { entries = ReadZip(path); }
                catch (InvalidDataException) { entries = ReadOther(path); } // uzantısı .zip ama başka format olabilir
            }
            else
            {
                entries = ReadOther(path);
            }
            return new ArchiveRecord { Path = path, FileSize = size, FileMtimeUtcTicks = mtimeUtcTicks, Entries = entries };
        }
        catch (Exception ex)
        {
            return new ArchiveRecord { Path = path, FileSize = size, FileMtimeUtcTicks = mtimeUtcTicks, Error = Describe(ex) };
        }
    }

    // 64 KB tampon: büyük ZIP'lerin merkezi dizini sıralı okunurken binlerce küçük okuma çağrısı olmasın.
    static FileStream OpenShared(string path, int bufferSize = 64 * 1024) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.RandomAccess);

    static bool IsRar(string path)
    {
        var ext = Path.GetExtension(path.AsSpan());
        return ext.Equals(".rar", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cbr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>RAR5 + Quick Open varsa hızlı yol; aksi halde (ya da herhangi bir hatada) null.</summary>
    public static ArchiveEntry[]? TryReadRar5QuickOpen(string path)
    {
        try
        {
            using var fs = OpenShared(path, bufferSize: 0); // Rar5Lister kendi 64 KB penceresini tutar
            return Rar5Lister.TryList(fs);
        }
        catch (Exception) { return null; }
    }

    static ArchiveEntry[] ReadZip(string path)
    {
        using var zip = new ZipArchive(OpenShared(path), ZipArchiveMode.Read, leaveOpen: false, LegacyNameEncoding);
        var entries = zip.Entries;
        var result = new ArchiveEntry[entries.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var e = entries[i];
            string name = NormalizePath(e.FullName);
            result[i] = new ArchiveEntry(name, e.Length, SafeUtcTicks(e.LastWriteTime), name.EndsWith('/'));
        }
        return result;
    }

    static ReaderOptions CreateReaderOptions() =>
        new() { ArchiveEncoding = new ArchiveEncoding { Default = LegacyNameEncoding } };

    static ArchiveEntry[] ReadOther(string path)
    {
        if (IsRar(path) && TryReadRar5QuickOpen(path) is { } fast) return fast;
        return ReadWithSharpCompress(path);
    }

    /// <summary>SharpCompress ile okur (doğrulama ve geri dönüş yolu).</summary>
    public static ArchiveEntry[] ReadWithSharpCompress(string path)
    {
        using var archive = ArchiveFactory.OpenArchive(path, CreateReaderOptions());
        var result = new List<ArchiveEntry>();
        foreach (var e in archive.Entries)
        {
            string name = NormalizePath(e.Key ?? "");
            long ticks = e.LastModifiedTime is { } dt ? SafeUtcTicks(dt) : 0;
            result.Add(new ArchiveEntry(name, e.Size, ticks, e.IsDirectory || name.EndsWith('/')));
        }
        return [.. result];
    }

    /// <summary>Tek bir girdiyi geçici klasöre çıkarır ve dosya yolunu döndürür.</summary>
    public static string ExtractToTemp(string archivePath, string entryPath)
    {
        string id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(archivePath + "|" + entryPath)))[..16];
        string dir = Path.Combine(AppPaths.ExtractDir, id);
        Directory.CreateDirectory(dir);

        // Sadece dosya adını kullan: arşivdeki "../../" gibi yollar dışarı yazamasın.
        string fileName = string.Concat(Path.GetFileName(entryPath.TrimEnd('/')).Split(Path.GetInvalidFileNameChars()));
        if (fileName.Length == 0) fileName = "dosya";
        string dest = Path.Combine(dir, fileName);

        if (IsZip(archivePath))
        {
            try
            {
                using var zip = new ZipArchive(OpenShared(archivePath), ZipArchiveMode.Read, false, LegacyNameEncoding);
                var entry = zip.Entries.FirstOrDefault(e => NormalizePath(e.FullName) == entryPath)
                            ?? throw new FileNotFoundException("Arşivde bulunamadı: " + entryPath);
                using var src = entry.Open();
                using var dst = File.Create(dest);
                src.CopyTo(dst);
                return dest;
            }
            catch (InvalidDataException) { }
        }

        using (var archive = ArchiveFactory.OpenArchive(archivePath, CreateReaderOptions()))
        {
            var entry = archive.Entries.FirstOrDefault(e => NormalizePath(e.Key ?? "") == entryPath)
                        ?? throw new FileNotFoundException("Arşivde bulunamadı: " + entryPath);
            using var src = entry.OpenEntryStream();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
        return dest;
    }

    public static string NormalizePath(string p) => p.Replace('\\', '/').TrimStart('/');

    static long SafeUtcTicks(DateTimeOffset d)
    {
        try { return d.UtcTicks; } catch { return 0; }
    }

    static long SafeUtcTicks(DateTime d)
    {
        try { return d.Kind == DateTimeKind.Utc ? d.Ticks : d.ToUniversalTime().Ticks; } catch { return 0; }
    }

    static string Describe(Exception ex) => ex switch
    {
        SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException => "Şifreli arşiv (dosya listesi şifre olmadan okunamıyor)",
        UnauthorizedAccessException => "Erişim engellendi",
        IOException io when io.HResult == unchecked((int)0x80070020) => "Dosya başka bir işlem tarafından kullanılıyor",
        _ when ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) => "Şifreli arşiv (dosya listesi şifre olmadan okunamıyor)",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}

