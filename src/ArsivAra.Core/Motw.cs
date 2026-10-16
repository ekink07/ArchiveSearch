namespace ArsivAra.Core;

/// <summary>
/// "Mark of the Web": the Zone.Identifier stream (an NTFS alternate data stream) that Windows adds to files downloaded from the internet.
/// When it is carried over to files extracted from an archive, SmartScreen and Office Protected View also work for files opened from the archive.
/// </summary>
public static class Motw
{
    const string Stream = ":Zone.Identifier";

    /// <summary>The file's Zone.Identifier content; null if there is none (or it cannot be read).</summary>
    public static string? Read(string file)
    {
        try { return File.ReadAllText(file + Stream); }
        catch (Exception) { return null; }
    }

    /// <summary>ZoneId (3 = Internet, 4 = Restricted); null if there is no mark.</summary>
    public static int? ZoneId(string file)
    {
        string? text = Read(file);
        if (text == null) return null;
        foreach (var line in text.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(l.AsSpan(7), out int zone)) return zone;
        }
        return null;
    }

    /// <summary>If the source file has a mark, writes the same mark to the destination (as Explorer does when extracting a zip).</summary>
    public static void Propagate(string source, string destination)
    {
        string? zone = Read(source);
        if (zone == null) return;
        try { File.WriteAllText(destination + Stream, zone); }
        catch (Exception) { /* volumes without alternate stream support, such as FAT32/exFAT */ }
    }
}

/// <summary>File types that run code when double-clicked: the user is warned before opening them.</summary>
public static class RiskyFiles
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".msi", ".msp", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appref-ms", ".application",
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".cpl", ".msc",
        ".lnk", ".url", ".reg", ".inf", ".scf", ".jar", ".gadget", ".chm", ".xll", ".iqy", ".slk", ".settingcontent-ms",
        ".library-ms", ".search-ms", ".sct", ".dll", ".sys", ".drv", ".ocx",
    };

    public static bool IsRisky(string fileName) => Extensions.Contains(Path.GetExtension(fileName));
}
