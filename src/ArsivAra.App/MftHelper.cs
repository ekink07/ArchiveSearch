using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Hızlı disk taraması: MFT'yi okumak yönetici izni ister. Tüm programı yönetici olarak çalıştırmak Explorer'dan
/// sürükle-bırakı engellediği için (Windows UIPI) sadece bulma işi kısa ömürlü, yönetici izinli bir yardımcı süreçte yapılır:
/// <c>ArsivAra.exe --mft-bul istek.json sonuc.json</c>. Arşivleri okumak normal süreçte kalır.
/// </summary>
static class MftHelper
{
    public const string Flag = "--mft-bul";

    public sealed record Request(List<string> Roots, List<string> Excludes);
    public sealed record Response(List<string> Archives, List<string> NotScanned);

    /// <summary>Yardımcı süreç: kökleri MFT ile tarar, bulunan arşiv yollarını yazar.</summary>
    public static int Run(string requestFile, string responseFile)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile))
                          ?? throw new InvalidDataException("İstek okunamadı");
            var (archives, notScanned) = Scanner.DiscoverMft(request.Roots, request.Excludes);
            File.WriteAllText(responseFile, JsonSerializer.Serialize(new Response(archives, notScanned)));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("MFT yardımcısı", ex);
            return 1;
        }
    }

    /// <summary>Normal süreçten: yardımcıyı yönetici olarak başlatır ve bitmesini bekler. İzin verilmezse null.</summary>
    public static async Task<Response?> RunElevatedAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> excludes, CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ArsivAra-mft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string request = Path.Combine(dir, "istek.json"), response = Path.Combine(dir, "sonuc.json");
        try
        {
            File.WriteAllText(request, JsonSerializer.Serialize(new Request([.. roots], [.. excludes])));
            var psi = new ProcessStartInfo(Environment.ProcessPath!, $"{Flag} \"{request}\" \"{response}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0 && File.Exists(response)
                ? JsonSerializer.Deserialize<Response>(await File.ReadAllTextAsync(response, ct))
                : null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // kullanıcı izin vermedi
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}
