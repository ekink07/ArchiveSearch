using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArsivAra.Core;

/// <summary>
/// Bir birimin hangi fiziksel diskte olduğunu ve diskin "seek cezası" olup olmadığını (HDD mi SSD mi) bulur.
/// Yönetici yetkisi gerekmez (birim tutamacı erişim hakkı istemeden açılır).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static unsafe class DiskInfo
{
    const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    const int StorageDeviceSeekPenaltyProperty = 7;

    public readonly record struct VolumeDisk(int DiskNumber, bool IsRotational);

    static readonly ConcurrentDictionary<char, VolumeDisk> Cache = new();

    /// <summary>
    /// Yol hangi diskte? Bilinmiyorsa (ağ yolu, okunamayan aygıt) her birim ayrı bir "disk" sayılır
    /// ve döner disk (HDD) varsayılır: yanlışsa sadece biraz yavaş olur, tersi ise HDD'yi zorlar.
    /// </summary>
    public static VolumeDisk ForPath(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (root is not { Length: >= 2 } || root[1] != ':') return new VolumeDisk(-1000 - (root?.GetHashCode() & 0xFFFF ?? 0), true);
        char letter = char.ToUpperInvariant(root[0]);
        return Cache.GetOrAdd(letter, Query);
    }

    static VolumeDisk Query(char letter)
    {
        int disk = -100 - letter; // bilinmeyen: birim başına ayrı
        bool rotational = true;
        using var h = CreateFileW($@"\\.\{letter}:", 0, 3, 0, 3, 0, 0);
        if (h.IsInvalid) return new VolumeDisk(disk, rotational);

        // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (uint) + pad, sonra DISK_EXTENT { DiskNumber uint, pad, StartingOffset, ExtentLength }
        var ext = stackalloc byte[256];
        if (DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, ext, 256, out _, 0) && *(uint*)ext >= 1)
            disk = (int)*(uint*)(ext + 8);

        // STORAGE_PROPERTY_QUERY { PropertyId, QueryType=0 (standart), AdditionalParameters[1] }
        var query = stackalloc int[3];
        query[0] = StorageDeviceSeekPenaltyProperty;
        query[1] = 0;
        query[2] = 0;
        // DEVICE_SEEK_PENALTY_DESCRIPTOR { Version, Size, IncursSeekPenalty (BOOLEAN) }
        var desc = stackalloc byte[12];
        if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, query, 12, desc, 12, out int got, 0) && got >= 9)
            rotational = desc[8] != 0;

        return new VolumeDisk(disk, rotational);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, int inSize, void* outBuf, int outSize, out int returned, nint overlapped);
}
