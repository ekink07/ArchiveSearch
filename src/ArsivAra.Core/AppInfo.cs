using System.Reflection;

namespace ArsivAra.Core;

/// <summary>Ürün adı, sürüm, üretici, yapımcı imzası ve lisans (Hakkında ve Lisans pencereleri, komut satırı).</summary>
public static class AppInfo
{
    public const string Name = "Arşiv Ara";
    public const string Credit = "Created By MaledictusMaggot®";
    public const string LicenseName = "MIT";

    static readonly Assembly Core = typeof(AppInfo).Assembly;

    /// <summary>Çalışan programın sürümü ("1.2.3"); derleme meta verisi (+commit) atılır.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Üretici firma ("Verlora Studios"); Directory.Build.props'taki Company.</summary>
    public static string Company { get; } = Core.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Verlora Studios";

    /// <summary>"Copyright © 2026 MaledictusMaggot"; Directory.Build.props'taki Copyright.</summary>
    public static string Copyright { get; } = Core.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>LICENSE dosyasının tam metni (derlemeye gömülü).</summary>
    public static string LicenseText => Resource("LICENSE");

    /// <summary>Kullanılan açık kaynak bileşenler ve lisansları (THIRD-PARTY-NOTICES.txt, derlemeye gömülü).</summary>
    public static string ThirdPartyNotices => Resource("THIRD-PARTY-NOTICES.txt");

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Core;
        string v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString(3) ?? "";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    static string Resource(string name)
    {
        using var s = Core.GetManifestResourceStream(name);
        if (s == null) return "";
        using var reader = new StreamReader(s);
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
