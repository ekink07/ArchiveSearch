using System.Text;

namespace ArsivAra.Core.Tests;

static class TestSetup
{
    /// <summary>Keeps tests from writing to the user's real data folder (log, passwords): set before AppPaths is first used.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("ARSIVARA_DATA", Path.Combine(Path.GetTempPath(), "arsivara-test-veri"));
}

/// <summary>Sample archives produced by tools/make_test_fixtures.ps1 and the expected contents of the source tree.</summary>
static class Fx
{
    public const string Password = "Parola123";

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static string File(string name) => Path.Combine(Dir, name);

    /// <summary>Repository root (the folder containing ArsivAra.sln); for tests that inspect source files.</summary>
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !System.IO.File.Exists(Path.Combine(d.FullName, "ArsivAra.sln"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("ArsivAra.sln not found");
        }
    }

    /// <summary>Files in the source tree (except kule.png; it is generated with System.Drawing, so only its presence is checked).</summary>
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
        new Random(1234).NextBytes(b); // same sequence as [Random]::new(1234) in the script
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
