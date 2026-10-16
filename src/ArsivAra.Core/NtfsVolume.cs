using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ArsivAra.Core;

/// <summary>Bir USN kaydından ihtiyaç duyduğumuz alanlar.</summary>
public readonly record struct UsnEntry(ulong FileRef, ulong ParentRef, string Name, uint Attributes, uint Reason, long Usn)
{
    public bool IsDirectory => (Attributes & 0x10) != 0;
}

public readonly record struct UsnJournalInfo(ulong JournalId, long NextUsn);

/// <summary>
/// MFT taramasında her kayıt için string ayırmamak adına ad bir span olarak verilir;
/// çağıran sadece ihtiyaç duyduğu adları (klasörler, arşivler) string'e çevirir.
/// </summary>
public delegate void MftRecordHandler(ulong fileRef, ulong parentRef, uint attributes, ReadOnlySpan<char> name);

/// <summary>
/// NTFS biriminin MFT'sini ve USN Journal'ını doğrudan okur (Everything'in yöntemi).
/// Birim tutamacı açmak yönetici yetkisi gerektirir.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed unsafe class NtfsVolume : IDisposable
{
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ_WRITE = 0x3;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
    const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;
    const int ERROR_HANDLE_EOF = 38;

    public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
    public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
    public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

    readonly SafeFileHandle _volume;
    SafeFileHandle? _rootDir;

    /// <summary>"C:\" biçiminde birim kökü.</summary>
    public string Root { get; }

    NtfsVolume(string root, SafeFileHandle volume)
    {
        Root = root;
        _volume = volume;
    }

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Birim NTFS ise ve erişim varsa açar; yoksa null (klasör gezmeye geri düşülür).</summary>
    public static NtfsVolume? TryOpen(string anyPath)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(anyPath));
            if (root is null || root.Length != 3 || root[1] != ':') return null; // ağ yolu, UNC vb.
            var drive = new DriveInfo(root);
            if (!drive.IsReady || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return null;

            var h = CreateFileW($@"\\.\{root[0]}:", GENERIC_READ, FILE_SHARE_READ_WRITE, 0, OPEN_EXISTING, 0, 0);
            if (h.IsInvalid) { h.Dispose(); return null; }
            return new NtfsVolume(root.ToUpperInvariant(), h);
        }
        catch (Exception) { return null; }
    }

    public UsnJournalInfo? QueryJournal()
    {
        var buf = stackalloc byte[80];
        if (!DeviceIoControl(_volume, FSCTL_QUERY_USN_JOURNAL, null, 0, buf, 80, out _, 0)) return null;
        return new UsnJournalInfo(*(ulong*)buf, *(long*)(buf + 16));
    }

    /// <summary>
    /// MFT'deki tüm kayıtları dolaşır (FSCTL_ENUM_USN_DATA). Disk taramasından katlarca hızlıdır,
    /// çünkü klasörler tek tek açılmaz; dosya tablosu büyük bloklar hâlinde okunur.
    /// </summary>
    /// <summary>Son MFT taramasında kullanılan yöntem (teşhis için).</summary>
    public string? LastEnumerationMethod { get; private set; }

    /// <summary>
    /// MFT'deki tüm kayıtları dolaşır. Önce ham $MFT okuması (tüm hard link adlarıyla), o olmazsa
    /// FSCTL_ENUM_USN_DATA (dosya başına tek ad). İkisi de klasörleri tek tek açmaz.
    /// </summary>
    public void EnumerateMft(MftRecordHandler onRecord, CancellationToken ct = default)
    {
        try
        {
            MftReader.Enumerate(_volume, onRecord, ct);
            LastEnumerationMethod = "ham MFT";
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* aşağıdaki API'ye düş */ }
        EnumerateUsnData(onRecord, ct);
        LastEnumerationMethod = "FSCTL_ENUM_USN_DATA";
    }

    /// <summary>Teşhis: ham MFT ile FSCTL_ENUM_USN_DATA'yı ayrı ayrı çalıştırmak için.</summary>
    public void EnumerateRawMft(MftRecordHandler onRecord, CancellationToken ct = default) => MftReader.Enumerate(_volume, onRecord, ct);
    public void EnumerateUsnDataOnly(MftRecordHandler onRecord, CancellationToken ct = default) => EnumerateUsnData(onRecord, ct);

    void EnumerateUsnData(MftRecordHandler onRecord, CancellationToken ct)
    {
        const int BufSize = 1 << 20;
        byte[] buffer = new byte[BufSize];
        // MFT_ENUM_DATA_V0: StartFileReferenceNumber, LowUsn, HighUsn
        long* input = stackalloc long[3];
        input[0] = 0;
        input[1] = 0;
        input[2] = long.MaxValue;

        fixed (byte* buf = buffer)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!DeviceIoControl(_volume, FSCTL_ENUM_USN_DATA, input, 24, buf, BufSize, out int returned, 0))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_HANDLE_EOF) break;
                    throw new Win32Exception(err);
                }
                if (returned <= 8) break;
                input[0] = *(long*)buf; // bir sonraki başlangıç kaydı
                ParseRaw(buf + 8, returned - 8, onRecord);
            }
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// startUsn'den itibaren journal'daki yeni kayıtları okur ve bir sonraki USN'i döndürür.
    /// Beklemez; yeni kayıt yoksa hemen döner (çağıran periyodik yoklar).
    /// </summary>
    public long ReadJournal(ulong journalId, long startUsn, Action<UsnEntry> onEntry, byte[] buffer)
    {
        // READ_USN_JOURNAL_DATA_V0: StartUsn, ReasonMask, ReturnOnlyOnClose, Timeout, BytesToWaitFor, UsnJournalID
        byte* input = stackalloc byte[40];
        *(long*)input = startUsn;
        *(uint*)(input + 8) = 0xFFFFFFFF;
        *(uint*)(input + 12) = 0;
        *(ulong*)(input + 16) = 0;
        *(ulong*)(input + 24) = 0;
        *(ulong*)(input + 32) = journalId;

        long usn = startUsn;
        fixed (byte* buf = buffer)
        {
            while (true)
            {
                *(long*)input = usn;
                if (!DeviceIoControl(_volume, FSCTL_READ_USN_JOURNAL, input, 40, buf, buffer.Length, out int returned, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                long next = *(long*)buf;
                if (returned > 8) ParseRecords(buf + 8, returned - 8, onEntry);
                if (returned <= 8 || next == usn) return next;
                usn = next;
            }
        }
    }

    static void ParseRaw(byte* p, int length, MftRecordHandler onRecord)
    {
        byte* end = p + length;
        while (p + 60 <= end)
        {
            uint recLen = *(uint*)p;
            if (recLen == 0 || p + recLen > end) break;
            ushort major = *(ushort*)(p + 4);
            if (major == 2)
                onRecord(*(ulong*)(p + 8), *(ulong*)(p + 16), *(uint*)(p + 52),
                    new ReadOnlySpan<char>(p + *(ushort*)(p + 58), *(ushort*)(p + 56) / 2));
            else if (major == 3)
                onRecord(*(ulong*)(p + 8), *(ulong*)(p + 24), *(uint*)(p + 68),
                    new ReadOnlySpan<char>(p + *(ushort*)(p + 74), *(ushort*)(p + 72) / 2));
            p += recLen;
        }
    }

    static void ParseRecords(byte* p, int length, Action<UsnEntry> onEntry)
    {
        byte* end = p + length;
        while (p + 60 <= end)
        {
            uint recLen = *(uint*)p;
            if (recLen == 0 || p + recLen > end) break;
            ushort major = *(ushort*)(p + 4);

            if (major == 2)
            {
                var name = new string((char*)(p + *(ushort*)(p + 58)), 0, *(ushort*)(p + 56) / 2);
                onEntry(new UsnEntry(*(ulong*)(p + 8), *(ulong*)(p + 16), name, *(uint*)(p + 52), *(uint*)(p + 40), *(long*)(p + 24)));
            }
            else if (major == 3)
            {
                // 128 bit kimlikler; NTFS'te üst 64 bit sıfırdır.
                var name = new string((char*)(p + *(ushort*)(p + 74)), 0, *(ushort*)(p + 72) / 2);
                onEntry(new UsnEntry(*(ulong*)(p + 8), *(ulong*)(p + 24), name, *(uint*)(p + 68), *(uint*)(p + 56), *(long*)(p + 40)));
            }
            p += recLen;
        }
    }

    /// <summary>Dosya kimliğinden (FRN) güncel tam yolu bulur. Silinmişse null.</summary>
    public string? PathFromId(ulong fileRef)
    {
        _rootDir ??= CreateFileW(Root, 0, FILE_SHARE_READ_WRITE | 0x4, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (_rootDir.IsInvalid) return null;

        // FILE_ID_DESCRIPTOR: dwSize, Type (0 = FileIdType), FileId (16 baytlık birlik)
        byte* desc = stackalloc byte[24];
        *(uint*)desc = 24;
        *(int*)(desc + 4) = 0;
        *(ulong*)(desc + 8) = fileRef;
        *(ulong*)(desc + 16) = 0;

        using var h = OpenFileById(_rootDir, desc, 0, FILE_SHARE_READ_WRITE | 0x4, 0, FILE_FLAG_BACKUP_SEMANTICS);
        if (h.IsInvalid) return null;

        char* path = stackalloc char[1024];
        uint len = GetFinalPathNameByHandleW(h, path, 1024, 0);
        if (len == 0 || len >= 1024) return null;
        var s = new string(path, 0, (int)len);
        return s.StartsWith(@"\\?\", StringComparison.Ordinal) ? s[4..] : s;
    }

    public void Dispose()
    {
        _rootDir?.Dispose();
        _volume.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, int inSize, void* outBuf, int outSize, out int returned, nint overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, void* fileId, uint access, uint share, nint security, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, char* path, uint len, uint flags);
}




