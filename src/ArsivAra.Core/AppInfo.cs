using System.Reflection;

namespace ArsivAra.Core;

/// <summary>Ürün adı, sürüm ve yapımcı imzası (Hakkında penceresi, komut satırı başlığı).</summary>
public static class AppInfo
{
    public const string Name = "Arşiv Ara";
    public const string Credit = "Created By MaledictusMaggot®";

    /// <summary>Çalışan programın sürümü ("1.2.3"); derleme meta verisi (+commit) atılır.</summary>
    public static string Version { get; } = ReadVersion();

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        string v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? asm.GetName().Version?.ToString(3) ?? "";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
