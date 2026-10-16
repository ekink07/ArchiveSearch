using System.Text.Json;

namespace ArsivAra.Core;

public static class AppPaths
{
    public const string PortableFolderName = "ArsivAra-veri";

    /// <summary>Folder for the index, settings, passwords and log (see <see cref="ResolveDataDir"/>).</summary>
    public static string DataDir { get; } = ResolveDataDir(
        Environment.GetEnvironmentVariable("ARSIVARA_DATA"),
        Path.GetDirectoryName(Environment.ProcessPath),
        IsRemovable,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArsivAra"),
        CanWrite);

    /// <summary>
    /// Chooses the data folder:
    /// 1. the ARSIVARA_DATA environment variable (for testing);
    /// 2. the "ArsivAra-veri" folder next to the exe, if it already exists (portable use continues);
    /// 3. if there is a "portable" or "portable.txt" file next to the exe, or the exe is on a removable drive (USB stick),
    ///    it is created next to the exe;
    /// 4. otherwise %LOCALAPPDATA%\ArsivAra. This way no folder is created next to the exe in Downloads or on the Desktop.
    /// If the portable folder is not writable (e.g. a read-only USB stick), it falls back to option 4.
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

/// <summary>Saved search: appears by name in the sidebar; clicking it applies the query.</summary>
public sealed class SavedSearch
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
}

public sealed class Settings
{
    /// <summary>Root folders to scan in the disk scan. If empty, all fixed drives are scanned.</summary>
    public List<string> Roots { get; set; } = [];

    /// <summary>Folders not to scan (including their subfolders) and archive files removed from the list.</summary>
    public List<string> Excludes { get; set; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        AppPaths.ExtractDir.TrimEnd('\\'),
    ];

    /// <summary>Icon/thumbnail size (px). 16–32: list, 48 and above: grid view. Changed with Ctrl+mouse wheel.</summary>
    public int IconSize { get; set; } = 16;

    /// <summary>UI language: "" (the Windows language), "tr", "en", "de", "es".</summary>
    public string Language { get; set; } = "";

    /// <summary>Find all archives on the computer and watch for changes live.</summary>
    public bool ScanDisks { get; set; }

    /// <summary>Use the MFT for the disk scan: requires administrator rights, many times faster than walking folders.</summary>
    public bool FastScan { get; set; }

    /// <summary>Also list archives inside archives (zip inside zip).</summary>
    public bool ReadNested { get; set; } = true;

    /// <summary>Temporary files extracted for opening are deleted after this many days.</summary>
    public int TempRetentionDays { get; set; } = 2;

    public bool ShowPreview { get; set; }
    public double PreviewWidth { get; set; } = 360;

    /// <summary>Result sort order (a <see cref="SortKey"/> name) and direction.</summary>
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
        catch (Exception ex) { Log.Warn("Could not read settings, using defaults", ex); }
        return new Settings();
    }

    public void Save() => Save(AppPaths.SettingsFile);

    /// <summary>Written to a temporary file first: if writing is interrupted, the old settings are not corrupted.</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, file, overwrite: true);
    }
}
