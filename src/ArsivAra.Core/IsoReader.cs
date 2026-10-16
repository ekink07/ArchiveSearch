using System.Buffers.Binary;
using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// ISO disk görüntülerinin dosya listesini okur ve tek tek dosyalarını açar:
/// UDF (Windows kurulum ISO'ları), ISO 9660 + Joliet (Unicode adlar) ve Rock Ridge (Linux ISO'larındaki uzun adlar).
/// Dosya verisi görüntüde bitişik bloklar hâlinde durur; bir dosyayı açmak o blokları okumaktır, açma (decompress) yoktur.
/// Biçimler: ECMA-119 (ISO 9660), Joliet belirtimi, IEEE P1282 (Rock Ridge), ECMA-167 / OSTA UDF 1.02–2.60.
/// </summary>
public static class IsoReader
{
    const int Sector = 2048;
    const int MaxDepth = 64;
    const int MaxEntries = 2_000_000;

    /// <summary>Görüntüdeki bir dosya ya da klasör.</summary>
    public sealed class IsoFile
    {
        public required string Path { get; init; }
        public long Size { get; set; }
        public long MtimeUtcTicks { get; init; }
        public bool IsDirectory { get; init; }

        /// <summary>Verinin görüntüdeki parçaları (bayt konumu, uzunluk). Konum &lt; 0: sıfırlarla dolu bölge.</summary>
        public List<(long Offset, long Length)> Extents { get; init; } = [];

        /// <summary>UDF'de dosya kaydının içine gömülü küçük dosyaların verisi.</summary>
        public byte[]? Inline { get; init; }
    }

    /// <summary>Görüntüdeki tüm dosya ve klasörler. UDF varsa o, yoksa Joliet, o da yoksa ISO 9660 (+ Rock Ridge) okunur.</summary>
    public static List<IsoFile> List(Stream image)
    {
        if (HasUdf(image))
        {
            try
            {
                var udf = new UdfVolume(image).List();
                if (udf.Count > 0) return udf;
            }
            catch (InvalidDataException) { /* bozuk ya da desteklenmeyen UDF: ISO 9660 kısmına düş */ }
        }
        return Iso9660.List(image);
    }

    /// <summary>Dosyanın içeriğini okuyan akış (görüntü akışı açık kaldığı sürece geçerli).</summary>
    public static Stream Open(Stream image, IsoFile file) =>
        file.Inline != null ? new MemoryStream(file.Inline, writable: false) : new ExtentStream(image, file.Extents, file.Size);

    // ---- Ortak yardımcılar ----

    static byte[] ReadAt(Stream s, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > s.Length) throw new InvalidDataException("ISO görüntüsünün dışına okuma");
        var buf = new byte[count];
        s.Position = offset;
        s.ReadExactly(buf);
        return buf;
    }

    static ushort LE16(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d[o..]);
    static uint LE32(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d[o..]);
    static ulong LE64(ReadOnlySpan<byte> d, int o) => BinaryPrimitives.ReadUInt64LittleEndian(d[o..]);

    /// <summary>Birim tanıma dizisinde (16. sektörden itibaren) "NSR02"/"NSR03" varsa görüntüde UDF vardır.</summary>
    static bool HasUdf(Stream s)
    {
        for (int i = 16; i < 16 + 64; i++)
        {
            long off = (long)i * Sector;
            if (off + Sector > s.Length) return false;
            var d = ReadAt(s, off, 6);
            var id = Encoding.ASCII.GetString(d, 1, 5);
            if (id is "NSR02" or "NSR03") return true;
            if (id is "TEA01") return false;
            if (id is not ("CD001" or "BEA01" or "BOOT2" or "CDW02")) return false;
        }
        return false;
    }

    static long ValidTicks(int year, int month, int day, int hour, int minute, int second, int offsetMinutes)
    {
        if (year is < 1601 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59) return 0;
        long ticks = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).Ticks
                     - offsetMinutes * TimeSpan.TicksPerMinute;
        return ticks is > 0 and < 3155378975999999999 ? ticks : 0;
    }

    // ---- ISO 9660 / Joliet / Rock Ridge ----

    static class Iso9660
    {
        public static List<IsoFile> List(Stream s)
        {
            byte[]? primary = null, joliet = null;
            for (int i = 16; i < 16 + 64; i++)
            {
                long off = (long)i * Sector;
                if (off + Sector > s.Length) break;
                var d = ReadAt(s, off, Sector);
                if (Encoding.ASCII.GetString(d, 1, 5) != "CD001") break;
                if (d[0] == 255) break; // sonlandırıcı
                if (d[0] == 1) primary ??= d;
                else if (d[0] == 2 && d[88] == 0x25 && d[89] == 0x2F && d[90] is 0x40 or 0x43 or 0x45) joliet ??= d;
            }
            var vd = joliet ?? primary ?? throw new InvalidDataException("ISO 9660 birim tanımlayıcısı bulunamadı");
            int blockSize = LE16(vd, 128);
            if (blockSize is not (512 or 1024 or 2048)) blockSize = Sector;

            var result = new List<IsoFile>();
            var root = vd.AsSpan(156, 34);
            ReadDir(s, LE32(root, 2), LE32(root, 10), "", joliet != null, blockSize, result, [], 0);
            return result;
        }

        static void ReadDir(Stream s, uint lba, uint size, string prefix, bool joliet, int blockSize,
            List<IsoFile> result, HashSet<uint> visited, int depth)
        {
            if (depth > MaxDepth || !visited.Add(lba) || result.Count > MaxEntries) return;
            if (size > 64u << 20) throw new InvalidDataException("ISO klasör kaydı çok büyük");
            var data = ReadAt(s, (long)lba * blockSize, (int)size);

            IsoFile? pending = null; // çok parçalı (multi-extent) dosya: aynı adla ardışık kayıtlar
            int pos = 0;
            while (pos < data.Length)
            {
                int len = data[pos];
                if (len == 0)
                {
                    // Kayıtlar sektör sınırını aşmaz; sektörün kalanı boştur.
                    pos = (pos / Sector + 1) * Sector;
                    continue;
                }
                if (len < 34 || pos + len > data.Length) break;
                var r = data.AsSpan(pos, len);
                pos += len;

                int nameLen = r[32];
                if (33 + nameLen > len) continue;
                var nameBytes = r.Slice(33, nameLen);
                if (nameLen == 1 && nameBytes[0] <= 1) continue; // "." ve ".."

                string name;
                int suStart = 33 + nameLen + ((nameLen & 1) == 0 ? 1 : 0);
                if (!joliet && RockRidgeName(r, suStart) is { Length: > 0 } rr) name = rr;
                else
                {
                    name = joliet ? Encoding.BigEndianUnicode.GetString(nameBytes) : Encoding.Latin1.GetString(nameBytes);
                    int semi = name.IndexOf(';');
                    if (semi >= 0) name = name[..semi];
                    if (!joliet && name.EndsWith('.')) name = name[..^1];
                }
                name = name.Replace('/', '_').Replace('\\', '_');
                if (name.Length == 0) continue;

                byte flags = r[25];
                uint extent = LE32(r, 2), dataLen = LE32(r, 10);
                long mtime = IsoDate(r.Slice(18, 7));
                if ((flags & 0x02) != 0)
                {
                    string dirPath = prefix + name + "/";
                    result.Add(new IsoFile { Path = dirPath, IsDirectory = true, MtimeUtcTicks = mtime });
                    ReadDir(s, extent, dataLen, dirPath, joliet, blockSize, result, visited, depth + 1);
                    continue;
                }

                string path = prefix + name;
                if (pending != null && pending.Path != path) { result.Add(pending); pending = null; }
                pending ??= new IsoFile { Path = path, MtimeUtcTicks = mtime };
                pending.Extents.Add(((long)extent * blockSize, dataLen));
                pending.Size += dataLen;
                if ((flags & 0x80) == 0) { result.Add(pending); pending = null; }
            }
            if (pending != null) result.Add(pending);
        }

        /// <summary>Rock Ridge "NM" (alternatif ad) kayıtlarını birleştirir; yoksa null.</summary>
        static string? RockRidgeName(ReadOnlySpan<byte> rec, int p)
        {
            StringBuilder? sb = null;
            while (p + 4 <= rec.Length)
            {
                int l = rec[p + 2];
                if (l < 4 || p + l > rec.Length) break;
                if (rec[p] == 'N' && rec[p + 1] == 'M' && l >= 5)
                {
                    byte fl = rec[p + 4];
                    if ((fl & 0x06) == 0) (sb ??= new StringBuilder()).Append(Encoding.UTF8.GetString(rec.Slice(p + 5, l - 5)));
                }
                else if (rec[p] == 'S' && rec[p + 1] == 'T') break; // SUSP sonlandırıcı
                p += l;
            }
            return sb?.ToString();
        }

        static long IsoDate(ReadOnlySpan<byte> d) =>
            ValidTicks(1900 + d[0], d[1], d[2], d[3], d[4], d[5], (sbyte)d[6] * 15);
    }

    // ---- UDF ----

    readonly record struct LbAddr(uint Lbn, ushort Partition);

    sealed class UdfVolume(Stream s)
    {
        sealed class PartitionMap
        {
            public uint Start;                                 // fiziksel bölümün başlangıç sektörü
            public List<(uint Lbn, long Length)>? Metadata;    // UDF 2.50 metadata bölümü: metadata dosyasının parçaları
        }

        readonly List<PartitionMap> _maps = [];
        readonly HashSet<(uint, ushort)> _visited = [];
        readonly List<IsoFile> _result = [];

        public List<IsoFile> List()
        {
            var avdp = FindAnchor() ?? throw new InvalidDataException("UDF bağlantı tanımlayıcısı (AVDP) yok");
            uint vdsLen = LE32(avdp, 16), vdsLoc = LE32(avdp, 20);

            var partitionStarts = new Dictionary<ushort, uint>();
            byte[]? lvd = null;
            for (uint i = 0; i < Math.Min(vdsLen / Sector, 256u); i++)
            {
                var d = ReadAt(s, (long)(vdsLoc + i) * Sector, Sector);
                if (!ValidTag(d, out ushort tag)) continue;
                if (tag == 5) partitionStarts.TryAdd(LE16(d, 22), LE32(d, 188));
                else if (tag == 6) lvd ??= d;
                else if (tag == 8) break; // sonlandırıcı
            }
            if (lvd == null || partitionStarts.Count == 0) throw new InvalidDataException("UDF birim tanımlayıcıları eksik");
            if (LE32(lvd, 212) != Sector) throw new InvalidDataException("UDF blok boyutu desteklenmiyor");

            ParseMaps(lvd, partitionStarts);

            var fsd = ReadBlock(new LbAddr(LE32(lvd, 252), LE16(lvd, 256)));
            if (!ValidTag(fsd, out ushort fsdTag) || fsdTag != 256) throw new InvalidDataException("UDF dosya kümesi tanımlayıcısı yok");
            var root = new LbAddr(LE32(fsd, 404), LE16(fsd, 408));
            ReadDirectory(root, "", 0);
            return _result;
        }

        byte[]? FindAnchor()
        {
            long sectors = s.Length / Sector;
            foreach (long at in new[] { 256L, sectors - 257, sectors - 1, 512L })
            {
                if (at <= 0 || at >= sectors) continue;
                var d = ReadAt(s, at * Sector, Sector);
                if (ValidTag(d, out ushort tag) && tag == 2) return d;
            }
            return null;
        }

        /// <summary>ECMA-167 tanımlayıcı etiketi: 16 bayt, 4. bayt diğer 15 baytın toplamı (mod 256).</summary>
        static bool ValidTag(byte[] d, out ushort tag, int o = 0)
        {
            tag = 0;
            if (d.Length < o + 16) return false;
            tag = LE16(d, o);
            int sum = 0;
            for (int i = 0; i < 16; i++) if (i != 4) sum += d[o + i];
            return (byte)sum == d[o + 4] && tag is >= 1 and <= 266;
        }

        void ParseMaps(byte[] lvd, Dictionary<ushort, uint> starts)
        {
            int count = (int)LE32(lvd, 268), p = 440;
            for (int i = 0; i < count && p + 2 <= lvd.Length; i++)
            {
                int type = lvd[p], len = lvd[p + 1];
                if (len < 6 || p + len > lvd.Length) break;
                if (type == 1)
                {
                    _maps.Add(new PartitionMap { Start = Start(starts, LE16(lvd, p + 4)) });
                }
                else if (type == 2)
                {
                    string kind = Encoding.ASCII.GetString(lvd, p + 5, 23).TrimEnd('\0', ' ');
                    var map = new PartitionMap { Start = Start(starts, LE16(lvd, p + 38)) };
                    if (kind.StartsWith("*UDF Metadata Partition", StringComparison.Ordinal))
                    {
                        // Metadata dosyası fiziksel bölümdeki bir dosya kaydıdır; parçaları metadata bloklarını eşler.
                        var tmp = new PartitionMap { Start = map.Start };
                        _maps.Add(tmp);
                        var fe = ReadFileEntry(new LbAddr(LE32(lvd, p + 40), (ushort)(_maps.Count - 1)));
                        _maps.RemoveAt(_maps.Count - 1);
                        map.Metadata = fe.Extents.Select(e => ((uint)((e.Offset / Sector) - map.Start), e.Length)).ToList();
                    }
                    else if (kind.StartsWith("*UDF Virtual Partition", StringComparison.Ordinal))
                        throw new InvalidDataException("UDF sanal bölümü (VAT) desteklenmiyor");
                    _maps.Add(map); // sparable bölümler fiziksel gibi okunur
                }
                p += len;
            }
            if (_maps.Count == 0) throw new InvalidDataException("UDF bölüm haritası yok");
        }

        static uint Start(Dictionary<ushort, uint> starts, ushort number) =>
            starts.TryGetValue(number, out var st) ? st : throw new InvalidDataException("UDF bölümü bulunamadı");

        long Physical(LbAddr a)
        {
            if (a.Partition >= _maps.Count) throw new InvalidDataException("UDF bölüm başvurusu geçersiz");
            var m = _maps[a.Partition];
            if (m.Metadata == null) return ((long)m.Start + a.Lbn) * Sector;
            long off = (long)a.Lbn * Sector;
            foreach (var (lbn, length) in m.Metadata)
            {
                if (off < length) return ((long)m.Start + lbn) * Sector + off;
                off -= length;
            }
            throw new InvalidDataException("UDF metadata bloğu eşlenemedi");
        }

        byte[] ReadBlock(LbAddr a) => ReadAt(s, Physical(a), Sector);

        sealed record Fe(int FileType, long Size, long MtimeUtcTicks, List<(long Offset, long Length)> Extents, byte[]? Inline);

        Fe ReadFileEntry(LbAddr a)
        {
            var d = ReadBlock(a);
            if (!ValidTag(d, out ushort tag) || (tag != 261 && tag != 266)) throw new InvalidDataException("UDF dosya kaydı bekleniyordu");
            bool efe = tag == 266;
            int fileType = d[27];
            int adType = LE16(d, 34) & 7;
            long size = (long)LE64(d, 56);
            long mtime = UdfTime(d.AsSpan(efe ? 92 : 84, 12));
            int lEa = (int)LE32(d, efe ? 208 : 168), lAd = (int)LE32(d, efe ? 212 : 172);
            int adStart = (efe ? 216 : 176) + lEa;
            if (lEa < 0 || lAd < 0 || adStart + lAd > d.Length) throw new InvalidDataException("UDF dosya kaydı bozuk");

            if (adType == 3) return new Fe(fileType, size, mtime, [], d.AsSpan(adStart, (int)Math.Min(lAd, size)).ToArray());

            var extents = new List<(long, long)>();
            ParseAllocation(d, adStart, lAd, adType, a.Partition, extents, 0);
            return new Fe(fileType, size, mtime, extents, null);
        }

        void ParseAllocation(byte[] d, int start, int length, int adType, ushort partition, List<(long, long)> extents, int depth)
        {
            if (depth > 32) throw new InvalidDataException("UDF ayırma zinciri çok uzun");
            int step = adType switch { 0 => 8, 1 => 16, _ => throw new InvalidDataException("UDF ayırma türü desteklenmiyor") };
            for (int p = start; p + step <= start + length; p += step)
            {
                uint raw = LE32(d, p);
                long len = raw & 0x3FFFFFFF;
                int kind = (int)(raw >> 30);
                if (len == 0) break;
                var at = adType == 0 ? new LbAddr(LE32(d, p + 4), partition) : new LbAddr(LE32(d, p + 4), LE16(d, p + 8));
                if (kind == 3)
                {
                    // Ayırma tanımlayıcılarının devamı başka bir blokta (Allocation Extent Descriptor, etiket 258).
                    var aed = ReadBlock(at);
                    if (!ValidTag(aed, out ushort t) || t != 258) throw new InvalidDataException("UDF ayırma devamı bozuk");
                    ParseAllocation(aed, 24, (int)Math.Min(LE32(aed, 20), Sector - 24), adType, partition, extents, depth + 1);
                    return;
                }
                extents.Add((kind == 0 ? Physical(at) : -1, len)); // 1/2: yazılmamış ya da ayrılmamış, sıfır okunur
            }
        }

        void ReadDirectory(LbAddr icb, string prefix, int depth)
        {
            if (depth > MaxDepth || !_visited.Add((icb.Lbn, icb.Partition)) || _result.Count > MaxEntries) return;
            var fe = ReadFileEntry(icb);
            if (fe.Size > 64L << 20) throw new InvalidDataException("UDF klasörü çok büyük");
            byte[] data = fe.Inline ?? ReadExtents(fe.Extents, fe.Size);

            int p = 0;
            while (p + 38 <= data.Length)
            {
                if (!ValidTag(data, out ushort tag, p) || tag != 257) break; // File Identifier Descriptor
                byte characteristics = data[p + 18];
                int lfi = data[p + 19];
                var child = new LbAddr(LE32(data, p + 24), LE16(data, p + 28));
                int liu = LE16(data, p + 36);
                int nameAt = p + 38 + liu;
                if (nameAt + lfi > data.Length) break;
                string name = Osta(data.AsSpan(nameAt, lfi));
                p += (38 + liu + lfi + 3) & ~3;

                bool parent = (characteristics & 0x08) != 0, deleted = (characteristics & 0x04) != 0;
                if (parent || deleted || name.Length == 0) continue;
                name = name.Replace('/', '_').Replace('\\', '_');

                Fe info;
                try { info = ReadFileEntry(child); }
                catch (InvalidDataException) { continue; } // tek bozuk kayıt tüm listeyi düşürmesin

                if ((characteristics & 0x02) != 0 || info.FileType == 4)
                {
                    string dirPath = prefix + name + "/";
                    _result.Add(new IsoFile { Path = dirPath, IsDirectory = true, MtimeUtcTicks = info.MtimeUtcTicks });
                    ReadDirectory(child, dirPath, depth + 1);
                }
                else if (info.FileType is 5 or 0)
                {
                    _result.Add(new IsoFile
                    {
                        Path = prefix + name, Size = info.Size, MtimeUtcTicks = info.MtimeUtcTicks,
                        Extents = info.Extents, Inline = info.Inline,
                    });
                }
            }
        }

        byte[] ReadExtents(List<(long Offset, long Length)> extents, long size)
        {
            using var ms = new MemoryStream();
            new ExtentStream(s, extents, size).CopyTo(ms);
            return ms.ToArray();
        }

        /// <summary>OSTA sıkıştırılmış Unicode: ilk bayt 8 → 8 bitlik karakterler, 16 → UTF-16 BE.</summary>
        static string Osta(ReadOnlySpan<byte> b)
        {
            if (b.Length < 1) return "";
            return b[0] switch
            {
                8 or 254 => Encoding.Latin1.GetString(b[1..]),
                16 or 255 => Encoding.BigEndianUnicode.GetString(b[1..(1 + ((b.Length - 1) & ~1))]),
                _ => "",
            };
        }

        static long UdfTime(ReadOnlySpan<byte> t)
        {
            int typeTz = LE16(t, 0);
            int tz = typeTz & 0xFFF;
            if (tz > 2047) tz -= 4096;
            if (tz == -2047) tz = 0; // belirtilmemiş
            return ValidTicks((short)LE16(t, 2), t[4], t[5], t[6], t[7], t[8], tz);
        }
    }

    /// <summary>Görüntüdeki parçaları tek bir dosya gibi okutan akış.</summary>
    sealed class ExtentStream(Stream image, List<(long Offset, long Length)> extents, long size) : Stream
    {
        long _pos;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_pos >= size || buffer.Length == 0) return 0;
            int want = (int)Math.Min(buffer.Length, size - _pos), done = 0;
            long skip = _pos;
            foreach (var (off, len) in extents)
            {
                if (skip >= len) { skip -= len; continue; }
                int n = (int)Math.Min(want - done, len - skip);
                var dst = buffer.Slice(done, n);
                if (off < 0) dst.Clear();
                else
                {
                    image.Position = off + skip;
                    image.ReadExactly(dst);
                }
                done += n;
                skip = 0;
                if (done == want) break;
            }
            _pos += done;
            return done;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => size;
        public override long Position { get => _pos; set => _pos = Math.Clamp(value, 0, size); }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _pos + offset,
            _ => size + offset,
        };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
