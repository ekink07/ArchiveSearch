using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Only one Arşiv Ara runs per data folder. If files are dropped on the exe while the program is open (or "Open with" is used),
/// the new process forwards them to the running one and exits, so two processes never overwrite each other's index.bin.
/// Different data folders (e.g. a portable copy on USB) can run side by side.
/// </summary>
static class SingleInstance
{
    /// <summary>Restart while the previous instance is closing: waits until the lock is released.</summary>
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

    /// <summary>Forwards the files to the running instance; false if it cannot be reached.</summary>
    public static bool SendToRunning(string[] files)
    {
        try
        {
            AllowSetForegroundWindow(-1); // let the running window come to the foreground
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            client.Write(JsonSerializer.SerializeToUtf8Bytes(files));
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not reach the running Arşiv Ara instance", ex);
            return false;
        }
    }

    /// <summary>Listens for file lists sent by other processes (in the background).</summary>
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
                    Log.Warn("Single-instance pipe", ex);
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }, ct);
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
