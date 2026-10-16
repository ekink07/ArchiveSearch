using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArsivAra.Core;

/// <summary>
/// $MFT'yi diskten ham olarak okur (Everything'in yöntemi). FSCTL_ENUM_USN_DATA her dosya için tek bir ad
/// döndürür; burada her kayıttaki tüm $FILE_NAME öznitelikleri işlendiği için hard link'lerin
/// (ör. pnpm, yedekleme araçları) bütün adları bulunur. Ayrıca büyük sıralı okumalarla daha hızlıdır.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class MftReader
{
    const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
    const uint AttrStandardInformation = 0x10;
    const uint AttrFileName = 0x30;
    const uint AttrData = 0x80;
    const uint AttrEnd = 0xFFFFFFFF;
    const uint FileNameIsDirectory = 0x10000000; // $FILE_NAME bayraklarında "dizin indeksi var"
    const byte NamespaceDos = 2;

    public static void Enumerate(SafeFileHandle volume, MftRecordHandler onRecord, CancellationToken ct)
    {
        var vd = stackalloc byte[128];
        if (!DeviceIoControl(volume, FSCTL_GET_NTFS_VOLUME_DATA, null, 0, vd, 128, out _, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        uint bytesPerCluster = *(uint*)(vd + 44);
        int recordSize = (int)*(uint*)(vd + 48);
        long mftValidLength = *(long*)(vd + 56);
        long mftStartLcn = *(long*)(vd + 64);
        if (recordSize <= 0 || recordSize > 65536 || bytesPerCluster == 0)
            throw new InvalidDataException("Beklenmeyen NTFS geometrisi");

        // Kayıt 0 = $MFT'nin kendisi; $DATA özniteliğinin run listesi MFT'nin diskteki parçalarını verir.
        int firstRead = (int)Math.Max(recordSize, bytesPerCluster);
        var first = new byte[firstRead];
        ReadExactly(volume, first, mftStartLcn * bytesPerCluster);
        var runs = ParseMftDataRuns(first.AsSpan(0, recordSize), recordSize)
                   ?? throw new InvalidDataException("$MFT run listesi okunamadı");

        const int ChunkTarget = 8 << 20;
        int chunkBytes = Math.Max(recordSize, ChunkTarget / recordSize * recordSize);
        chunkBytes = (int)(chunkBytes / bytesPerCluster * bytesPerCluster);
        var buffer = new byte[chunkBytes];

        long mftOffset = 0; // MFT içindeki bayt konumu → kayıt numarası
        foreach (var (lcn, clusters) in runs)
        {
            long runBytes = clusters * bytesPerCluster;
            long done = 0;
            while (done < runBytes && mftOffset < mftValidLength)
            {
                ct.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(chunkBytes, runBytes - done);
                if (lcn < 0)
                {
                    // Seyrek (sparse) bölüm: kayıt yok.
                    mftOffset += toRead; done += toRead; continue;
                }
                ReadExactly(volume, buffer.AsSpan(0, toRead), lcn * bytesPerCluster + done);

                fixed (byte* buf = buffer)
                {
                    int usable = (int)Math.Min(toRead, mftValidLength - mftOffset);
                    for (int off = 0; off + recordSize <= usable; off += recordSize)
                    {
                        long recordNumber = (mftOffset + off) / recordSize;
                        ParseRecord(buf + off, recordSize, recordNumber, onRecord);
                    }
                }
                mftOffset += toRead;
                done += toRead;
            }
        }
    }

    static void ParseRecord(byte* rec, int recordSize, long recordNumber, MftRecordHandler onRecord)
    {
        if (*(uint*)rec != 0x454C4946) return; // "FILE"
        ushort flags = *(ushort*)(rec + 22);
        if ((flags & 0x1) == 0) return; // kullanılmayan kayıt
        if (!ApplyFixups(rec, recordSize)) return;

        ushort seq = *(ushort*)(rec + 16);
        ulong baseRef = *(ulong*)(rec + 32);
        // Uzantı kaydındaki adlar ana kayda aittir.
        ulong frn = baseRef != 0 ? baseRef : ((ulong)seq << 48) | (ulong)recordNumber;

        uint stdAttrs = 0;
        int pos = *(ushort*)(rec + 20);
        uint used = Math.Min(*(uint*)(rec + 24), (uint)recordSize);
        while (pos + 16 <= used)
        {
            byte* a = rec + pos;
            uint type = *(uint*)a;
            if (type == AttrEnd) break;
            uint len = *(uint*)(a + 4);
            if (len < 16 || pos + len > used) break;

            if (a[8] == 0) // yerleşik (resident)
            {
                uint valueLen = *(uint*)(a + 16);
                byte* v = a + *(ushort*)(a + 20);
                if (v + valueLen > rec + used) break;

                if (type == AttrStandardInformation && valueLen >= 36)
                    stdAttrs = *(uint*)(v + 32);
                else if (type == AttrFileName && valueLen >= 66)
                {
                    byte nameLen = v[64];
                    byte ns = v[65];
                    if (ns != NamespaceDos && 66 + nameLen * 2 <= valueLen)
                    {
                        ulong parent = *(ulong*)v;
                        uint fnFlags = *(uint*)(v + 56);
                        // Diskteki ham öznitelikler Win32'nin gizlediği iç bitler taşır (ör. WindowsApps'teki sıkıştırılmış
                        // dosyalarda 0x40000, Win32'de "RecallOnOpen" anlamına gelir). Sadece standart alt 16 bit kullanılır;
                        // bulut yer tutucu kontrolü okuma aşamasında FileInfo ile (doğru Win32 öznitelikleriyle) yapılır.
                        uint attrs = (stdAttrs != 0 ? stdAttrs : fnFlags) & 0xFFFF & ~(uint)FileAttributes.Directory;
                        if ((fnFlags & FileNameIsDirectory) != 0 || ((flags & 0x2) != 0 && baseRef == 0))
                            attrs |= (uint)FileAttributes.Directory;
                        onRecord(frn, parent, attrs, new ReadOnlySpan<char>(v + 66, nameLen));
                    }
                }
            }
            pos += (int)len;
        }
    }

    /// <summary>Sektör sonlarındaki "update sequence" baytlarını geri yükler; tutarsızsa kayıt bozuktur.</summary>
    static bool ApplyFixups(byte* rec, int recordSize)
    {
        ushort usaOffset = *(ushort*)(rec + 4);
        ushort usaCount = *(ushort*)(rec + 6);
        if (usaCount < 2 || usaOffset + usaCount * 2 > recordSize) return false;
        int stride = recordSize / (usaCount - 1);
        ushort usn = *(ushort*)(rec + usaOffset);
        for (int i = 1; i < usaCount; i++)
        {
            ushort* end = (ushort*)(rec + i * stride - 2);
            if (*end != usn) return false;
            *end = *(ushort*)(rec + usaOffset + i * 2);
        }
        return true;
    }

    /// <summary>Kayıt 0'daki yerleşik olmayan $DATA özniteliğinin run listesini çözer: (LCN, küme sayısı); LCN &lt; 0 = seyrek.</summary>
    static List<(long Lcn, long Clusters)>? ParseMftDataRuns(Span<byte> record, int recordSize)
    {
        fixed (byte* rec = record)
        {
            if (*(uint*)rec != 0x454C4946 || !ApplyFixups(rec, recordSize)) return null;
            int pos = *(ushort*)(rec + 20);
            uint used = Math.Min(*(uint*)(rec + 24), (uint)recordSize);
            while (pos + 16 <= used)
            {
                byte* a = rec + pos;
                uint type = *(uint*)a;
                if (type == AttrEnd) break;
                uint len = *(uint*)(a + 4);
                if (len < 16 || pos + len > used) break;

                if (type == AttrData && a[8] == 1 && a[9] == 0) // isimsiz, yerleşik olmayan $DATA
                {
                    var runs = new List<(long, long)>();
                    byte* r = a + *(ushort*)(a + 32);
                    byte* end = a + len;
                    long lcn = 0;
                    while (r < end && *r != 0)
                    {
                        int lenBytes = *r & 0xF, offBytes = *r >> 4;
                        r++;
                        if (lenBytes == 0 || r + lenBytes + offBytes > end) return null;
                        long count = ReadLittle(r, lenBytes, signed: false);
                        r += lenBytes;
                        if (offBytes == 0)
                        {
                            runs.Add((-1, count)); // seyrek
                        }
                        else
                        {
                            lcn += ReadLittle(r, offBytes, signed: true);
                            runs.Add((lcn, count));
                        }
                        r += offBytes;
                    }
                    return runs.Count > 0 ? runs : null;
                }
                pos += (int)len;
            }
        }
        return null; // $DATA bir $ATTRIBUTE_LIST içinde (aşırı parçalı MFT): çağıran eski yönteme düşer
    }

    static long ReadLittle(byte* p, int n, bool signed)
    {
        long v = 0;
        for (int i = 0; i < n; i++) v |= (long)p[i] << (8 * i);
        if (signed && n < 8 && (p[n - 1] & 0x80) != 0) v |= -1L << (8 * n);
        return v;
    }

    static void ReadExactly(SafeFileHandle h, Span<byte> buffer, long offset)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = RandomAccess.Read(h, buffer[total..], offset + total);
            if (n <= 0) throw new EndOfStreamException("Birim okunamadı");
            total += n;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, int inSize, void* outBuf, int outSize, out int returned, nint overlapped);
}
