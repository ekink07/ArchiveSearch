using System.Text.Json;

namespace ArsivAra.Core;

public static class AppPaths
{
    /// <summary>
    /// İndeks ve ayarların klasörü. Taşınabilir kullanım için önce exe'nin yanındaki "ArsivAra-veri" klasörü denenir
    /// (USB bellekle birlikte taşınır); oraya yazılamıyorsa (ör. Program Files) %LOCALAPPDATA%\ArsivAra kullanılır.
    /// ARSIVARA_DATA ortam değişkeni her ikisini de geçersiz kılar (test için).
    /// </summary>
    public static string DataDir { get; } = ResolveDataDir();

    static string ResolveDataDir()
    {
        if (Environment.GetEnvironmentVariable("ARSIVARA_DATA") is { Length: > 0 } custom) return custom;

        if (Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } exeDir)
        {
            string portable = Path.Combine(exeDir, "ArsivAra-veri");
            if (CanWrite(portable)) return portable;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArsivAra");
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

    public static string IndexFile => Path.Combine(DataDir, "index.bin");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ExtractDir => Path.Combine(Path.GetTempPath(), "ArsivAra");
}

public sealed class Settings
{
    /// <summary>Taranacak kök klasörler. Boşsa tüm sabit diskler taranır.</summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>Taranmayacak klasörler (alt klasörleriyle birlikte).</summary>
    public List<string> Excludes { get; set; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        AppPaths.ExtractDir.TrimEnd('\\'),
    ];

    /// <summary>Simge/küçük resim boyutu (px). 16–32: liste, 48 ve üstü: kareli görünüm. Ctrl+tekerlek ile değişir.</summary>
    public int IconSize { get; set; } = 16;

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> EffectiveRoots =>
        Roots.Count > 0
            ? Roots
            : DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch (Exception) { }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
