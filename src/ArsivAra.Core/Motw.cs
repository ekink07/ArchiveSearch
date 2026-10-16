namespace ArsivAra.Core;

/// <summary>
/// "Mark of the Web": internetten indirilen dosyalara Windows'un eklediği Zone.Identifier akışı (NTFS alternatif veri akışı).
/// Arşivden çıkarılan dosyalara aktarılınca SmartScreen ve Office Korumalı Görünüm, arşivden açılan dosyalarda da çalışır.
/// </summary>
public static class Motw
{
    const string Stream = ":Zone.Identifier";

    /// <summary>Dosyanın Zone.Identifier içeriği; yoksa (ya da okunamıyorsa) null.</summary>
    public static string? Read(string file)
    {
        try { return File.ReadAllText(file + Stream); }
        catch (Exception) { return null; }
    }

    /// <summary>ZoneId (3 = İnternet, 4 = Kısıtlı); işaret yoksa null.</summary>
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

    /// <summary>Kaynak dosyada işaret varsa aynısını hedefe yazar (Explorer'ın zip açarken yaptığı gibi).</summary>
    public static void Propagate(string source, string destination)
    {
        string? zone = Read(source);
        if (zone == null) return;
        try { File.WriteAllText(destination + Stream, zone); }
        catch (Exception) { /* FAT32/exFAT gibi alternatif akış desteklemeyen birimler */ }
    }
}

/// <summary>Çift tıklayınca kod çalıştıran dosya türleri: açmadan önce kullanıcı uyarılır.</summary>
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
