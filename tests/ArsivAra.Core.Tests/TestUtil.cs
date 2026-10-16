using System.Text;

namespace ArsivAra.Core.Tests;

static class TestSetup
{
    /// <summary>Testler kullanıcının gerçek veri klasörüne (günlük, parolalar) yazmasın: AppPaths ilk kullanılmadan önce ayarlanır.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("ARSIVARA_DATA", Path.Combine(Path.GetTempPath(), "arsivara-test-veri"));
}

/// <summary>tools/make_test_fixtures.ps1 ile üretilen örnek arşivler ve kaynak ağacın beklenen içeriği.</summary>
static class Fx
{
    public const string Password = "Parola123";

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static string File(string name) => Path.Combine(Dir, name);

    /// <summary>Depo kökü (ArsivAra.sln'in bulunduğu klasör); kaynak dosyalarını denetleyen testler için.</summary>
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !System.IO.File.Exists(Path.Combine(d.FullName, "ArsivAra.sln"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("ArsivAra.sln bulunamadı");
        }
    }

    /// <summary>Kaynak ağaçtaki dosyalar (kule.png hariç; o System.Drawing ile üretildiği için sadece varlığına bakılır).</summary>
    public static readonly Dictionary<string, byte[]> Files = new()
    {
        ["okubeni.txt"] = Encoding.UTF8.GetBytes("Arşiv Ara test dosyası.\r\n"),
        ["Belgeler/Şirket Raporu 2023.txt"] = Encoding.UTF8.GetBytes("Şirket raporu 2023. Gizli kelime: zümrüt.\r\n"),
        ["Belgeler/Sözleşme.txt"] = Encoding.UTF8.GetBytes("Sözleşme metni, İSTANBUL şubesi.\r\n"),
        ["Veri/rastgele.bin"] = RandomBytes(),
    };

    public const string Png = "Fotoğraflar/İstanbul/kule.png";

    static byte[] RandomBytes()
    {
        var b = new byte[48 * 1024];
        new Random(1234).NextBytes(b); // betikteki [Random]::new(1234) ile aynı dizi
        return b;
    }

    public static ArchiveRecord Read(string name, string? password = null, bool nested = true)
    {
        string p = File(name);
        var fi = new FileInfo(p);
        return ArchiveReader.Read(p, fi.Length, fi.LastWriteTimeUtc.Ticks, new ReadOptions { Password = password, ReadNested = nested });
    }

    public static byte[] Extract(ArchiveRecord rec, string entryPath, string? password = null)
    {
        byte[]? result = null;
        ArchiveExtractor.ForEach(rec, new HashSet<string>(StringComparer.Ordinal) { entryPath }, password, (_, s) =>
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            result = ms.ToArray();
        });
        return result ?? throw new FileNotFoundException(entryPath);
    }

    public static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "arsivara-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }

    public static ArchiveEntry File(string path, long size, DateTime local, uint crc = 0) =>
        new(path, size, local.ToUniversalTime().Ticks, false, crc);

    public static ArchiveEntry Folder(string path) => new(path, 0, 0, true);
}
