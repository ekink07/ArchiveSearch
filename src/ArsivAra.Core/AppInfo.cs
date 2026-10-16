using System.Reflection;

namespace ArsivAra.Core;

/// <summary>Product name, version, manufacturer, author credit and license (About and License windows, command line).</summary>
public static class AppInfo
{
    public const string Name = "Arşiv Ara";
    public const string Credit = "Created By MaledictusMaggot®";
    public const string LicenseName = "MIT";

    static readonly Assembly Core = typeof(AppInfo).Assembly;

    /// <summary>Version of the running program ("1.2.3"); build metadata (+commit) is stripped.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Manufacturer ("Verlora Studios"); the Company in Directory.Build.props.</summary>
    public static string Company { get; } = Core.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Verlora Studios";

    /// <summary>"Copyright © 2026 MaledictusMaggot"; the Copyright in Directory.Build.props.</summary>
    public static string Copyright { get; } = Core.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>Full text of the LICENSE file (embedded in the assembly).</summary>
    public static string LicenseText => Resource("LICENSE");

    /// <summary>Open-source components used and their licenses (THIRD-PARTY-NOTICES.txt, embedded in the assembly).</summary>
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
