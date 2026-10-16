using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ArsivAra.Core;

/// <summary>
/// Şifreli arşivlerin parolaları. "Hatırla" seçilmezse sadece program açıkken bellekte tutulur;
/// seçilirse Windows'un DPAPI'siyle bu Windows hesabına bağlı olarak şifrelenip veri klasörüne yazılır
/// (başka bir hesapta ya da bilgisayarda çözülemez, düz metin olarak hiçbir yerde durmaz).
/// Anahtar: arşivin <see cref="ArchiveRecord.DisplayPath"/>'i.
/// </summary>
public static class PasswordStore
{
    static readonly object Gate = new();
    static readonly Dictionary<string, string> Session = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string>? _saved;
    static readonly byte[] Entropy = "ArsivAra.parolalar.v1"u8.ToArray();

    /// <summary>Kayıt dosyası (testler değiştirebilir).</summary>
    public static string FilePath { get; set; } = Path.Combine(AppPaths.DataDir, "parolalar.bin");

    public static string? Get(string key)
    {
        lock (Gate)
        {
            if (Session.TryGetValue(key, out var p)) return p;
            return Saved.TryGetValue(key, out p) ? p : null;
        }
    }

    /// <summary>Arşiv kaydı için parola: önce kendisi, iç içe arşivde sonra dıştaki arşiv.</summary>
    public static string? For(ArchiveRecord rec) => Get(rec.DisplayPath) ?? (rec.IsNested ? Get(rec.Path) : null);

    public static void Set(string key, string password, bool remember)
    {
        lock (Gate)
        {
            Session[key] = password;
            if (remember)
            {
                Saved[key] = password;
                Persist();
            }
            else if (Saved.Remove(key)) Persist();
        }
    }

    public static void Remove(string key)
    {
        lock (Gate)
        {
            Session.Remove(key);
            if (Saved.Remove(key)) Persist();
        }
    }

    public static int SavedCount
    {
        get { lock (Gate) return Saved.Count; }
    }

    /// <summary>Kayıtlı tüm parolaları siler (oturumdakiler de unutulur).</summary>
    public static void ClearAll()
    {
        lock (Gate)
        {
            Session.Clear();
            Saved.Clear();
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { }
        }
    }

    /// <summary>Testler için: dosyadan yeniden okunsun.</summary>
    internal static void ResetCache()
    {
        lock (Gate)
        {
            Session.Clear();
            _saved = null;
        }
    }

    static Dictionary<string, string> Saved => _saved ??= LoadSaved();

    static Dictionary<string, string> LoadSaved()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = Unprotect(File.ReadAllBytes(FilePath));
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (d != null) return new Dictionary<string, string>(d, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { Log.Warn("Kayıtlı parolalar okunamadı (başka bir hesap ya da bilgisayar olabilir)", ex); }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    static void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (_saved is not { Count: > 0 })
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }
            string tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, Protect(JsonSerializer.SerializeToUtf8Bytes(_saved)));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { Log.Warn("Parolalar kaydedilemedi", ex); }
    }

    // ---- DPAPI (CryptProtectData / CryptUnprotectData) ----

    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr mem);

    internal static byte[] Protect(byte[] data) => Transform(data, protect: true);
    internal static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    static byte[] Transform(byte[] data, bool protect)
    {
        var input = Pin(data, out var hIn);
        var entropy = Pin(Entropy, out var hEnt);
        try
        {
            DataBlob output;
            bool ok = protect
                ? CryptProtectData(ref input, "Arşiv Ara", ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally
        {
            hIn.Free();
            hEnt.Free();
        }
    }

    static DataBlob Pin(byte[] data, out GCHandle handle)
    {
        handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        return new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
    }
}
