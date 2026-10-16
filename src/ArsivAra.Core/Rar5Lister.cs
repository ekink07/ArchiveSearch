using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// Reads the file list of RAR5 archives using the "Quick Open" (QO) record.
///
/// In RAR each file's header sits in front of its own data; listing requires seeking forward
/// through the file for every entry. On a USB HDD that is ~5 ms per entry (a backup with 22k entries ≈ 2 min).
/// WinRAR writes exact copies of the headers in a single block at the end of the archive (QO). Here QO is fetched in one read,
/// and headers not in the cache are read from a 64 KB read window (consecutive small files fall into the same window).
///
/// Format: https://www.rarlab.com/technote.htm (RAR 5.0 archive format). In an unsupported/inconsistent case
/// it returns null and the caller falls back to SharpCompress.
/// </summary>
public static class Rar5Lister
{
    static ReadOnlySpan<byte> Signature => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    const int HeadMain = 1, HeadFile = 2, HeadService = 3, HeadCrypt = 4, HeadEnd = 5;
    const ulong HflExtra = 0x1, HflData = 0x2, HflSplitBefore = 0x8;
    const int WindowSize = 64 * 1024;

    /// <summary>Diagnostics: number of actual disk regions read and number of headers served from QO in the last listing.</summary>
    [ThreadStatic] public static int LastDiskReads, LastCacheHits;

    /// <summary>Null if not RAR5, or if the QO record is missing/unreadable.</summary>
    public static ArchiveEntry[]? TryList(Stream stream) => TryList(stream, out _);

    /// <param name="solid">Whether the archive is solid (reaching a file requires decompressing the ones before it).</param>
    public static ArchiveEntry[]? TryList(Stream stream, out bool solid)
    {
        solid = false;
        var src = new Source(stream);
        LastDiskReads = 0; LastCacheHits = 0;

        if (!src.TryGet(0, 8, out var sig) || !sig.SequenceEqual(Signature)) return null;
        long mainPos = 8;
        if (!ReadBlock(src, mainPos, out var main) || main.Type != HeadMain) return null;

        // Main header: archive flags, [volume number], extra (Locator).
        var p = new Reader(main.Body);
        ulong archiveFlags = p.VInt();
        if ((archiveFlags & 0x1) != 0) return null; // multi-volume: QO covers only this part, all parts are read with SharpCompress
        solid = (archiveFlags & 0x4) != 0;
        if ((archiveFlags & 0x2) != 0) p.VInt();
        long qoOffset = 0;
        foreach (var (type, rec) in ExtraRecords(main))
        {
            if (type != 1) continue; // Locator
            var r = new Reader(rec);
            ulong lflags = r.VInt();
            if ((lflags & 0x1) != 0) qoOffset = (long)r.VInt();
        }
        if (qoOffset <= 0) return null; // no QO: this path gains nothing, let SharpCompress read it

        long qoPos = mainPos + qoOffset;
        if (!LoadQuickOpen(src, qoPos)) return null;

        var result = new List<ArchiveEntry>();
        long pos = main.NextPos;
        while (true)
        {
            if (pos == qoPos) break; // all file headers come before QO
            if (!ReadBlock(src, pos, out var b)) return null;
            if (b.Type == HeadEnd) break;
            if (b.Type == HeadCrypt) return null; // headers are encrypted
            if (b.Type == HeadFile && (b.Flags & HflSplitBefore) == 0)
                result.Add(ParseFile(b));
            pos = b.NextPos;
        }
        LastDiskReads = src.DiskReads;
        LastCacheHits = src.CacheHits;
        return [.. result];
    }

    static ArchiveEntry ParseFile(in Block b)
    {
        var r = new Reader(b.Body);
        ulong fileFlags = r.VInt();
        ulong unpacked = r.VInt();
        r.VInt(); // attributes
        long mtimeTicks = 0;
        uint crc = 0;
        if ((fileFlags & 0x2) != 0) mtimeTicks = UnixToTicks(r.UInt32());
        if ((fileFlags & 0x4) != 0) crc = r.UInt32(); // CRC32 of the contents
        r.VInt(); // compression info
        r.VInt(); // host OS
        int nameLen = (int)r.VInt();
        string name = Encoding.UTF8.GetString(r.Bytes(nameLen));

        foreach (var (type, rec) in ExtraRecords(b))
        {
            if (type != 3) continue; // File time
            var t = new Reader(rec);
            ulong tflags = t.VInt();
            if ((tflags & 0x2) == 0) continue;
            mtimeTicks = (tflags & 0x1) != 0
                ? UnixToTicks(t.UInt32())
                : SafeFileTime((long)t.UInt64());
        }

        bool isDir = (fileFlags & 0x1) != 0;
        string path = ArchiveReader.NormalizePath(name);
        if (isDir && !path.EndsWith('/')) path += "/";
        long size = (fileFlags & 0x8) != 0 || unpacked > long.MaxValue ? 0 : (long)unpacked;
        return new ArchiveEntry(path, isDir ? 0 : size, mtimeTicks, isDir, isDir ? 0 : crc);
    }

    static long UnixToTicks(uint seconds) => DateTime.UnixEpoch.Ticks + seconds * TimeSpan.TicksPerSecond;

    static long SafeFileTime(long ft)
    {
        try { return DateTime.FromFileTimeUtc(ft).Ticks; } catch { return 0; }
    }

    // ---- QO ----

    static bool LoadQuickOpen(Source src, long qoPos)
    {
        if (!ReadBlock(src, qoPos, out var qo) || qo.Type != HeadService || (qo.Flags & HflData) == 0) return false;
        var r = new Reader(qo.Body);
        ulong ff = r.VInt(); // file flags
        r.VInt(); r.VInt();  // unpacked size, attributes
        if ((ff & 0x2) != 0) r.UInt32();
        if ((ff & 0x4) != 0) r.UInt32();
        r.VInt(); r.VInt();
        int nameLen = (int)r.VInt();
        if (!r.Bytes(nameLen).SequenceEqual("QO"u8)) return false;
        if (qo.DataSize <= 0 || qo.DataSize > 512L << 20) return false;

        var data = src.ReadDirect(qo.DataPos, (int)qo.DataSize);
        if (data == null) return false;

        var d = new Reader(data);
        while (d.Remaining > 4)
        {
            uint crc = d.UInt32();
            int start = d.Position;
            int structSize = (int)d.VInt();
            int bodyStart = d.Position;
            if (structSize <= 0 || bodyStart + structSize > data.Length) return false;
            if (Crc32.Compute(data.AsSpan(start, bodyStart - start + structSize)) != crc) return false;
            d.VInt(); // flags
            long offset = (long)d.VInt();
            int size = (int)d.VInt();
            if (d.Position + size > bodyStart + structSize) return false;
            src.AddCache(qoPos - offset, data.AsMemory(d.Position, size));
            d.Seek(bodyStart + structSize);
        }
        src.SealCache();
        return true;
    }

    // ---- Block reading ----

    readonly struct Block
    {
        public required int Type { get; init; }
        public required ulong Flags { get; init; }
        public required byte[] Body { get; init; }   // type-specific fields + extra area (after CRC/size/type/flags)
        public required int ExtraSize { get; init; }
        public required long DataPos { get; init; }
        public required long DataSize { get; init; }
        public long NextPos => DataPos + DataSize;
    }

    static bool ReadBlock(Source src, long pos, out Block block)
    {
        block = default;
        if (!src.TryGet(pos, 7, out var head, allowShort: true) || head.Length < 5) return false;
        var hr = new Reader(head[4..]);
        ulong headerSize = hr.VInt();
        int sizeLen = hr.Position;
        if (headerSize == 0 || headerSize > 2 << 20) return false;

        int total = 4 + sizeLen + (int)headerSize;
        if (!src.TryGet(pos, total, out var raw)) return false;
        if (Crc32.Compute(raw[4..]) != BitConverter.ToUInt32(raw[..4])) return false;

        var r = new Reader(raw[(4 + sizeLen)..].ToArray());
        int type = (int)r.VInt();
        ulong flags = r.VInt();
        int extraSize = (flags & HflExtra) != 0 ? (int)r.VInt() : 0;
        long dataSize = (flags & HflData) != 0 ? (long)r.VInt() : 0;
        if (dataSize < 0) return false;

        block = new Block
        {
            Type = type,
            Flags = flags,
            Body = r.RemainingBytes(),
            ExtraSize = extraSize,
            DataPos = pos + total,
            DataSize = dataSize,
        };
        return true;
    }

    static List<(ulong Type, byte[] Data)> ExtraRecords(Block b)
    {
        var list = new List<(ulong, byte[])>();
        if (b.ExtraSize <= 0 || b.ExtraSize > b.Body.Length) return list;
        var r = new Reader(b.Body.AsSpan(b.Body.Length - b.ExtraSize));
        while (r.Remaining > 0)
        {
            int size = (int)r.VInt();
            int start = r.Position;
            if (size <= 0 || start + size > b.ExtraSize) break;
            ulong type = r.VInt();
            list.Add((type, r.Bytes(size - (r.Position - start)).ToArray()));
            r.Seek(start + size);
        }
        return list;
    }

    // ---- Source: QO cache + 64 KB read window ----

    sealed class Source(Stream stream)
    {
        readonly List<(long Pos, ReadOnlyMemory<byte> Data)> _cache = [];
        long[] _cachePos = [];
        byte[] _window = [];
        long _windowPos = -1;
        public int DiskReads, CacheHits;

        public void AddCache(long pos, ReadOnlyMemory<byte> data) => _cache.Add((pos, data));

        /// <summary>Sorts the cache by position; lookups use binary search.</summary>
        public void SealCache()
        {
            _cache.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            _cachePos = [.. _cache.Select(c => c.Pos)];
        }

        public bool TryGet(long pos, int len, out ReadOnlySpan<byte> bytes, bool allowShort = false)
        {
            bytes = default;
            if (pos < 0) return false;
            int ci = Array.BinarySearch(_cachePos, pos);
            if (ci < 0) ci = ~ci - 1;
            if (ci >= 0)
            {
                var (cpos, cdata) = _cache[ci];
                if (pos + len <= cpos + cdata.Length)
                {
                    CacheHits++;
                    bytes = cdata.Span.Slice((int)(pos - cpos), len);
                    return true;
                }
            }
            if (_windowPos < 0 || pos < _windowPos || pos + len > _windowPos + _window.Length)
            {
                var buf = ReadDirect(pos, Math.Max(WindowSize, len), allowShort: true);
                if (buf == null) return false;
                _window = buf;
                _windowPos = pos;
            }
            int off = (int)(pos - _windowPos);
            int avail = _window.Length - off;
            if (avail < len)
            {
                if (!allowShort) return false;
                len = avail;
            }
            bytes = _window.AsSpan(off, len);
            return true;
        }

        public byte[]? ReadDirect(long pos, int len, bool allowShort = false)
        {
            if (pos >= stream.Length) return null;
            DiskReads++;
            stream.Position = pos;
            var buf = new byte[(int)Math.Min(len, stream.Length - pos)];
            int got = 0;
            while (got < buf.Length)
            {
                int n = stream.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < len && !allowShort) return null;
            return got == buf.Length ? buf : buf[..got];
        }
    }

    // ---- Small helpers ----

    ref struct Reader(ReadOnlySpan<byte> data)
    {
        readonly ReadOnlySpan<byte> _d = data;
        int _p;
        public readonly int Position => _p;
        public readonly int Remaining => _d.Length - _p;
        public void Seek(int p) => _p = p;

        public ulong VInt()
        {
            ulong v = 0;
            for (int shift = 0; shift < 70 && _p < _d.Length; shift += 7)
            {
                byte b = _d[_p++];
                if (shift < 64) v |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return v;
            }
            throw new InvalidDataException("Could not read vint");
        }

        public uint UInt32() { var v = BitConverter.ToUInt32(_d.Slice(_p, 4)); _p += 4; return v; }
        public ulong UInt64() { var v = BitConverter.ToUInt64(_d.Slice(_p, 8)); _p += 8; return v; }
        public ReadOnlySpan<byte> Bytes(int n) { var s = _d.Slice(_p, n); _p += n; return s; }
        public byte[] RemainingBytes() => _d[_p..].ToArray();
    }

    static class Crc32
    {
        static readonly uint[] Table = Build();

        static uint[] Build()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}

