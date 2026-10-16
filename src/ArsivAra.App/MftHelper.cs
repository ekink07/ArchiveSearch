using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Fast disk scan: reading the MFT requires administrator rights. Running the whole program as administrator blocks
/// drag and drop from Explorer (Windows UIPI), so only the discovery step runs in a short-lived, elevated helper process:
/// <c>ArsivAra.exe --mft-bul istek.json sonuc.json</c>. Reading the archives stays in the normal process.
/// </summary>
static class MftHelper
{
    public const string Flag = "--mft-bul";

    public sealed record Request(List<string> Roots, List<string> Excludes);
    public sealed record Response(List<string> Archives, List<string> NotScanned);

    /// <summary>Helper process: scans the roots via the MFT and writes out the archive paths it finds.</summary>
    public static int Run(string requestFile, string responseFile)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile))
                          ?? throw new InvalidDataException("Could not read the request");
            var (archives, notScanned) = Scanner.DiscoverMft(request.Roots, request.Excludes);
            File.WriteAllText(responseFile, JsonSerializer.Serialize(new Response(archives, notScanned)));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("MFT helper", ex);
            return 1;
        }
    }

    /// <summary>From the normal process: starts the helper as administrator and waits for it to finish. Null if permission is denied.</summary>
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
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // the user declined elevation
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}
