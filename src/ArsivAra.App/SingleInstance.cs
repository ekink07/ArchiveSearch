using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Aynı veri klasörüyle tek bir Arşiv Ara çalışır. Program açıkken exe'ye dosya bırakılırsa (ya da "Birlikte aç" kullanılırsa)
/// yeni süreç dosyaları açık olana iletip kapanır. Böylece iki süreç aynı index.bin'i birbirinin üzerine yazmaz.
/// Farklı veri klasörleri (ör. USB'deki taşınabilir kopya) ayrı ayrı çalışabilir.
/// </summary>
static class SingleInstance
{
    /// <summary>Önceki örnek kapanırken yeniden başlatma: kilit bırakılana kadar beklenir.</summary>
    public const string RestartFlag = "--yeniden-baslat";

    static Mutex? _mutex;
    static bool _owned;

    static string Id { get; } = "ArsivAra-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDir.ToLowerInvariant())))[..16];

    public static bool TryAcquire(bool waitForPrevious)
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + Id, out bool created);
        if (created) return _owned = true;
        if (!waitForPrevious) return false;
        try { return _owned = _mutex.WaitOne(TimeSpan.FromSeconds(20)); }
        catch (AbandonedMutexException) { return _owned = true; }
    }

    public static void Release()
    {
        try { if (_owned) _mutex?.ReleaseMutex(); }
        catch (Exception) { }
        _mutex?.Dispose();
        _owned = false;
    }

    /// <summary>Dosyaları açık olan örneğe iletir; ulaşılamazsa false.</summary>
    public static bool SendToRunning(string[] files)
    {
        try
        {
            AllowSetForegroundWindow(-1); // açık olan pencere öne gelebilsin
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            client.Write(JsonSerializer.SerializeToUtf8Bytes(files));
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Açık olan Arşiv Ara'ya ulaşılamadı", ex);
            return false;
        }
    }

    /// <summary>Diğer süreçlerden gelen dosya listelerini dinler (arka planda).</summary>
    public static void Listen(Action<string[]> onReceived, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct);
                    using var ms = new MemoryStream();
                    await server.CopyToAsync(ms, ct);
                    onReceived(JsonSerializer.Deserialize<string[]>(ms.ToArray()) ?? []);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warn("Tek örnek kanalı", ex);
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }, ct);
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
