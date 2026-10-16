using System.Text.Json;

namespace ArsivAra.Core;

public static class AppPaths
{
    public const string PortableFolderName = "ArsivAra-veri";

    /// <summary>İndeks, ayarlar, parolalar ve günlüğün klasörü (bkz. <see cref="ResolveDataDir"/>).</summary>
    public static string DataDir { get; } = ResolveDataDir(
        Environment.GetEnvironmentVariable("ARSIVARA_DATA"),
        Path.GetDirectoryName(Environment.ProcessPath),
        IsRemovable,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArsivAra"),
        CanWrite);

    /// <summary>
    /// Veri klasörünü seçer:
    /// 1. ARSIVARA_DATA ortam değişkeni (test için);
    /// 2. exe'nin yanında "ArsivAra-veri" klasörü zaten varsa orası (taşınabilir kullanım sürer);
    /// 3. exe'nin yanında "portable" ya da "portable.txt" dosyası varsa ya da exe çıkarılabilir bir diskteyse (USB bellek)
    ///    exe'nin yanında açılır;
    /// 4. aksi halde %LOCALAPPDATA%\ArsivAra. Böylece İndirilenler ya da Masaüstü'nde exe'nin yanına klasör açılmaz.
    /// Taşınabilir klasöre yazılamıyorsa (ör. salt okunur USB) 4. seçeneğe düşülür.
    /// </summary>
    internal static string ResolveDataDir(string? env, string? exeDir, Func<string, bool> isRemovable, string localDir, Func<string, bool> canWrite)
    {
        if (env is { Length: > 0 }) return env;
        if (exeDir is { Length: > 0 })
        {
            string portable = Path.Combine(exeDir, PortableFolderName);
            bool wanted = Directory.Exists(portable)
                          || File.Exists(Path.Combine(exeDir, "portable"))
                          || File.Exists(Path.Combine(exeDir, "portable.txt"))
                          || isRemovable(exeDir);
            if (wanted && canWrite(portable)) return portable;
        }
        return localDir;
    }

    static bool IsRemovable(string dir)
    {
        try
        {
            string? root = Path.GetPathRoot(dir);
            return root is { Length: > 0 } && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch (Exception) { return false; }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".yazma-testi");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool IsPortable => DataDir.EndsWith(PortableFolderName, StringComparison.OrdinalIgnoreCase);

    public static string IndexFile => Path.Combine(DataDir, "index.bin");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ExtractDir => Path.Combine(Path.GetTempPath(), "ArsivAra");
}

/// <summary>Kaydedilmiş arama: kenar çubuğunda adıyla görünür, tıklayınca sorgu uygulanır.</summary>
public sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
}

public sealed class Settings
{
    /// <summary>Disk taramasında taranacak kök klasörler. Boşsa tüm sabit diskler taranır.</summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>Taranmayacak klasörler (alt klasörleriyle) ve listeden kaldırılan arşiv dosyaları.</summary>
    public List<string> Excludes { get; set; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        AppPaths.ExtractDir.TrimEnd('\\'),
    ];

    /// <summary>Simge/küçük resim boyutu (px). 16–32: liste, 48 ve üstü: kareli görünüm. Ctrl+tekerlek ile değişir.</summary>
    public int IconSize { get; set; } = 16;

    /// <summary>Arayüz dili: "" (Windows'un dili), "tr", "en", "de", "es".</summary>
    public string Language { get; set; } = "";

    /// <summary>Bilgisayardaki tüm arşivleri bul ve değişiklikleri canlı izle.</summary>
    public bool ScanDisks { get; set; }

    /// <summary>Disk taramasında MFT kullan: yönetici izni ister, klasör gezmekten kat kat hızlı.</summary>
    public bool FastScan { get; set; }

    /// <summary>Arşivlerin içindeki arşivleri (zip içinde zip) de listele.</summary>
    public bool ReadNested { get; set; } = true;

    /// <summary>Açmak için çıkarılan geçici dosyalar bu kadar gün sonra silinir.</summary>
    public int TempRetentionDays { get; set; } = 2;

    public bool ShowPreview { get; set; }
    public double PreviewWidth { get; set; } = 360;

    /// <summary>Sonuç sıralaması (<see cref="SortKey"/> adı) ve yönü.</summary>
    public string SortKey { get; set; } = "";
    public bool SortDescending { get; set; }

    public List<SavedSearch> SavedSearches { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EffectiveRoots =>
        Roots.Count > 0
            ? Roots
            : DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();

    public ReadOptions ReadOptions(string? password = null) => new() { ReadNested = ReadNested, Password = password };

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load() => Load(AppPaths.SettingsFile);

    public static Settings Load(string file)
    {
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new Settings();
        }
        catch (Exception ex) { Log.Warn("Ayarlar okunamadı, varsayılanlar kullanılıyor", ex); }
        return new Settings();
    }

    public void Save() => Save(AppPaths.SettingsFile);

    /// <summary>Önce geçici dosyaya yazılır: yazma yarıda kalırsa eski ayarlar bozulmaz.</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, file, overwrite: true);
    }
}
