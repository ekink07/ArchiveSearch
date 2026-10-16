using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace ArsivAra.Core;

/// <param name="Hits">Eşleşen girdiler (dizindeki sırayla).</param>
/// <param name="Error">Sorguda geçersiz bir değer varsa türü; <paramref name="ErrorDetail"/> kullanıcının yazdığı.</param>
/// <param name="ContentTerms">icerik: terimleri (katlanmış): dosyaların içine bakılarak sonradan uygulanır (<see cref="ContentSearch"/>).</param>
public sealed record SearchResult(int[] Hits, TimeSpan Elapsed, QueryErrorKind Error = QueryErrorKind.None,
    string? ErrorDetail = null, string[]? ContentTerms = null);

public enum SortKey { None, Name, Folder, Size, Date, Archive, Type }

/// <summary>
/// Değişmez (immutable) arama anlık görüntüsü.
/// Tüm arşiv içi yollar katlanmış hâlde tek bir UTF-8 tampona '\n' ile ayrılarak yazılır;
/// arama bu tamponda vektörize IndexOf ile paralel taranır (Everything'in RAM'de tarama mantığı).
/// Diskteki her arşivin girdilerinden hemen sonra onun iç içe arşivlerinin girdileri gelir, böylece bir arşivle
/// sınırlı arama tamponun tek bir aralığını tarar. Sorgu dili: <see cref="QueryParser"/>.
/// </summary>
public sealed class SearchIndex
{
    /// <summary>Diskteki (en dıştaki) arşivler, kenar çubuğundaki sırayla.</summary>
    public IReadOnlyList<ArchiveRecord> TopLevel { get; }

    /// <summary>Tampondaki sırayla tüm arşivler: her diskteki arşivden hemen sonra onun iç içe arşivleri.</summary>
    public IReadOnlyList<ArchiveRecord> Archives { get; }

    readonly string[] _foldedArchivePaths;
    readonly byte[] _text;
    readonly int[] _offsets;   // girdi i'nin tampondaki başlangıcı; _offsets[Count] = son
    readonly int[] _archiveOf;
    readonly int[] _entryOf;
    readonly (int Start, int End)[] _topSpans;
    readonly Lazy<bool[]> _duplicates;
    readonly Lazy<int[]> _nameRank, _folderRank, _typeRank;

    public int EntryCount => _archiveOf.Length;
    public static SearchIndex Empty { get; } = Build([]);

    SearchIndex(IReadOnlyList<ArchiveRecord> topLevel, IReadOnlyList<ArchiveRecord> archives, (int, int)[] topSpans,
        string[] foldedArchivePaths, byte[] text, int[] offsets, int[] archiveOf, int[] entryOf)
    {
        TopLevel = topLevel;
        Archives = archives;
        _topSpans = topSpans;
        _foldedArchivePaths = foldedArchivePaths;
        _text = text;
        _offsets = offsets;
        _archiveOf = archiveOf;
        _entryOf = entryOf;
        _duplicates = new Lazy<bool[]>(ComputeDuplicates);
        _nameRank = new Lazy<int[]>(() => ComputeRank(e => NameOf(Slice(e)), tieBreak: null));
        _folderRank = new Lazy<int[]>(() => ComputeRank(e => FolderOf(Slice(e)), tieBreak: _nameRank.Value));
        _typeRank = new Lazy<int[]>(() => ComputeRank(e => TypeOf(e), tieBreak: _nameRank.Value));
    }

    public static SearchIndex Build(IReadOnlyList<ArchiveRecord> topLevel)
    {
        var flat = new List<ArchiveRecord>(topLevel.Count);
        var spans = new (int, int)[topLevel.Count];
        int count = 0;
        long chars = 0;
        for (int t = 0; t < topLevel.Count; t++)
        {
            int start = count;
            foreach (var r in topLevel[t].Nested.Prepend(topLevel[t]))
            {
                flat.Add(r);
                count += r.Entries.Length;
                foreach (var e in r.Entries) chars += e.Path.Length + 1;
            }
            spans[t] = (start, count);
        }

        var archiveOf = new int[count];
        var entryOf = new int[count];
        var offsets = new int[count + 1];
        var text = new byte[Math.Max(16, (int)Math.Min(Array.MaxLength, chars + chars / 8))];
        var folded = Array.Empty<char>();
        int pos = 0, idx = 0;

        for (int a = 0; a < flat.Count; a++)
        {
            var entries = flat[a].Entries;
            for (int j = 0; j < entries.Length; j++)
            {
                string p = entries[j].Path;
                if (folded.Length < p.Length) folded = new char[Math.Max(p.Length, 256)];
                Folding.Fold(p, folded);

                int need = p.Length * 3 + 1;
                if (text.Length - pos < need)
                    Array.Resize(ref text, (int)Math.Min(Array.MaxLength, Math.Max((long)text.Length * 2, (long)pos + need)));

                offsets[idx] = pos;
                pos += Encoding.UTF8.GetBytes(folded.AsSpan(0, p.Length), text.AsSpan(pos));
                text[pos++] = (byte)'\n';
                archiveOf[idx] = a;
                entryOf[idx] = j;
                idx++;
            }
        }
        offsets[count] = pos;

        var foldedArchives = flat.Select(a => Folding.Fold(a.DisplayPath)).ToArray();
        return new SearchIndex(topLevel, flat, spans, foldedArchives, text, offsets, archiveOf, entryOf);
    }

    // ---- Girdilere erişim ----

    /// <summary>Sonucun ait olduğu arşivin <see cref="Archives"/> içindeki sırası.</summary>
    public int ArchiveIndexOf(int hit) => _archiveOf[hit];

    public (ArchiveRecord Archive, ArchiveEntry Entry) Get(int hit)
    {
        var a = Archives[_archiveOf[hit]];
        return (a, a.Entries[_entryOf[hit]]);
    }

    internal ArchiveEntry EntryAt(int e) => Archives[_archiveOf[e]].Entries[_entryOf[e]];
    internal bool IsDirectoryAt(int e) => EntryAt(e).IsDirectory;
    internal long SizeAt(int e) => EntryAt(e).Size;
    internal long MtimeAt(int e) => EntryAt(e).MtimeUtcTicks;

    /// <summary>Diskteki arşivin (ve iç içe arşivlerinin) girdi aralığı; arşiv bu dizinde yoksa boş aralık.</summary>
    public (int Start, int End) ScopeOf(string topLevelPath)
    {
        for (int i = 0; i < TopLevel.Count; i++)
            if (string.Equals(TopLevel[i].Path, topLevelPath, StringComparison.OrdinalIgnoreCase)) return _topSpans[i];
        return (0, 0);
    }

    ReadOnlySpan<byte> Slice(int e) => _text.AsSpan(_offsets[e], _offsets[e + 1] - _offsets[e] - 1);

    /// <summary>Katlanmış yolun ad kısmı (klasörde sondaki '/' hariç).</summary>
    internal static ReadOnlySpan<byte> NameOf(ReadOnlySpan<byte> path)
    {
        if (path.Length > 0 && path[^1] == '/') path = path[..^1];
        int i = path.LastIndexOf((byte)'/');
        return i < 0 ? path : path[(i + 1)..];
    }

    static ReadOnlySpan<byte> FolderOf(ReadOnlySpan<byte> path)
    {
        if (path.Length > 0 && path[^1] == '/') path = path[..^1];
        int i = path.LastIndexOf((byte)'/');
        return i < 0 ? [] : path[..i];
    }

    /// <summary>Tür: klasörler en başta (boş), sonra uzantıya göre.</summary>
    ReadOnlySpan<byte> TypeOf(int e)
    {
        if (IsDirectoryAt(e)) return [];
        var name = NameOf(Slice(e));
        int dot = name.LastIndexOf((byte)'.');
        return dot <= 0 ? "\u0001"u8 : name[dot..];
    }

    // ---- Arama ----

    /// <param name="scope">Sadece bu girdi aralığında ara (kenar çubuğunda seçili arşiv: <see cref="ScopeOf"/>).</param>
    public SearchResult Search(string query, CancellationToken ct = default, (int Start, int End)? scope = null)
    {
        var sw = Stopwatch.StartNew();
        var q = QueryParser.Parse(query);
        if (q.Error != QueryErrorKind.None) return new SearchResult([], sw.Elapsed, q.Error, q.ErrorDetail);
        foreach (var node in q.ArchiveNodes)
            node.Allowed = _foldedArchivePaths.Select(p => p.Contains(node.Term, StringComparison.Ordinal)).ToArray();

        int first = Math.Clamp(scope?.Start ?? 0, 0, EntryCount), last = Math.Clamp(scope?.End ?? EntryCount, first, EntryCount);
        string[]? content = q.ContentTerms.Count > 0 ? [.. q.ContentTerms] : null;
        int n = last - first;
        if (n == 0) return new SearchResult([], sw.Elapsed, ContentTerms: content);

        int chunks = Math.Clamp(n / 50_000, 1, Environment.ProcessorCount * 4);
        var parts = new List<int>[chunks];
        var root = q.Root;
        var scan = q.ScanTerm;

        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = ct }, c =>
        {
            int a = first + (int)((long)n * c / chunks);
            int b = first + (int)((long)n * (c + 1) / chunks);
            var res = new List<int>();
            if (scan == null)
            {
                for (int e = a; e < b; e++)
                {
                    if ((e & 0x3FFF) == 0 && ct.IsCancellationRequested) break;
                    if (root.Match(new EntryView(this, e, Slice(e)))) res.Add(e);
                }
            }
            else
            {
                var text = _text.AsSpan();
                int pos = _offsets[a], end = _offsets[b];
                while (pos < end)
                {
                    int i = text[pos..end].IndexOf(scan);
                    if (i < 0) break;
                    int e = EntryAtByte(pos + i);
                    if (root.Match(new EntryView(this, e, Slice(e)))) res.Add(e);
                    pos = _offsets[e + 1]; // aynı girdide tekrar aramaya gerek yok
                    if (ct.IsCancellationRequested) break;
                }
            }
            parts[c] = res;
        });
        ct.ThrowIfCancellationRequested();

        var hits = new int[parts.Sum(p => p.Count)];
        int k = 0;
        foreach (var p in parts)
        {
            p.CopyTo(hits, k);
            k += p.Count;
        }
        return new SearchResult(hits, sw.Elapsed, ContentTerms: content);
    }

    int EntryAtByte(int bytePos)
    {
        int i = Array.BinarySearch(_offsets, bytePos);
        return i >= 0 ? i : ~i - 1;
    }

    /// <summary>Joker eşleştirme (UTF-8): '*' herhangi bir dizi, '?' tek bir karakter (kod noktası); metnin tamamına uymalı.</summary>
    internal static bool GlobMatch(ReadOnlySpan<byte> s, ReadOnlySpan<byte> p)
    {
        int si = 0, pi = 0, starP = -1, starS = 0;
        while (si < s.Length)
        {
            if (pi < p.Length && p[pi] == '?')
            {
                si = Math.Min(s.Length, si + CodePointLength(s[si]));
                pi++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                starP = pi++;
                starS = si;
            }
            else if (pi < p.Length && p[pi] == s[si])
            {
                si++;
                pi++;
            }
            else if (starP >= 0)
            {
                pi = starP + 1;
                starS = Math.Min(s.Length, starS + CodePointLength(s[starS]));
                si = starS;
            }
            else return false;
        }
        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }

    static int CodePointLength(byte b) => b < 0xC0 ? 1 : b < 0xE0 ? 2 : b < 0xF0 ? 3 : 4;

    // ---- Sıralama ----

    /// <summary>Sonuçları sıralar. Ad/klasör/tür sıraları dizin başına bir kez hesaplanır, sonraki sıralamalar tamsayı karşılaştırmasıdır.</summary>
    public int[] Sort(int[] hits, SortKey key, bool descending)
    {
        var result = (int[])hits.Clone();
        switch (key)
        {
            case SortKey.Size: SortBy(result, h => SizeAt(h)); break;
            case SortKey.Date: SortBy(result, h => MtimeAt(h)); break;
            case SortKey.Name: SortBy(result, h => _nameRank.Value[h]); break;
            case SortKey.Folder: SortBy(result, h => _folderRank.Value[h]); break;
            case SortKey.Type: SortBy(result, h => _typeRank.Value[h]); break;
            default: Array.Sort(result); break; // dizin sırası = arşiv adına göre, arşivin içinde kendi sırası
        }
        if (descending) Array.Reverse(result);
        return result;
    }

    /// <summary>Anahtara göre sıralar; eşit anahtarlarda dizin sırası korunur (kararlı ve tekrarlanabilir).</summary>
    static void SortBy(int[] hits, Func<int, long> keyOf)
    {
        var keys = new long[hits.Length];
        for (int i = 0; i < hits.Length; i++) keys[i] = keyOf(hits[i]);
        Array.Sort(keys, hits);
        for (int i = 0; i < hits.Length;)
        {
            int j = i + 1;
            while (j < hits.Length && keys[j] == keys[i]) j++;
            if (j - i > 1) Array.Sort(hits, i, j - i);
            i = j;
        }
    }

    delegate ReadOnlySpan<byte> KeyOf(int entry);

    /// <summary>
    /// Her girdinin anahtara göre sırası. Önce ilk 8 bayta göre (tamsayı olarak, hızlı) sıralanır,
    /// sonra aynı ön eki paylaşan gruplar tam karşılaştırmayla; eşitlikte <paramref name="tieBreak"/>, o da eşitse dizin sırası.
    /// </summary>
    int[] ComputeRank(KeyOf keyOf, int[]? tieBreak)
    {
        int n = EntryCount;
        var order = new int[n];
        var prefix = new ulong[n];
        Span<byte> buf = stackalloc byte[8];
        for (int e = 0; e < n; e++)
        {
            var k = keyOf(e);
            buf.Clear();
            k[..Math.Min(8, k.Length)].CopyTo(buf);
            prefix[e] = BinaryPrimitives.ReadUInt64BigEndian(buf);
            order[e] = e;
        }
        Array.Sort(prefix, order);

        for (int i = 0; i < n;)
        {
            int j = i + 1;
            while (j < n && prefix[j] == prefix[i]) j++;
            if (j - i > 1)
            {
                Array.Sort(order, i, j - i, Comparer<int>.Create((x, y) =>
                {
                    int c = keyOf(x).SequenceCompareTo(keyOf(y));
                    if (c == 0 && tieBreak != null) c = tieBreak[x].CompareTo(tieBreak[y]);
                    return c != 0 ? c : x.CompareTo(y);
                }));
            }
            i = j;
        }

        var rank = new int[n];
        for (int i = 0; i < n; i++) rank[order[i]] = i;
        return rank;
    }

    // ---- Kopyalar ve sürümler ----

    /// <summary>Girdinin bir kopyası var mı: aynı ad ve boyut, CRC'ler biliniyorsa aynı CRC (bilinmeyen CRC her şeyle eşleşir).</summary>
    public bool IsDuplicate(int entry) => _duplicates.Value[entry];

    bool[] ComputeDuplicates()
    {
        int n = EntryCount;
        var keys = new List<(ulong Name, long Size, uint Crc, int Entry)>();
        for (int e = 0; e < n; e++)
        {
            var en = EntryAt(e);
            if (en.IsDirectory || en.Size <= 0) continue;
            keys.Add((Fnv1a(NameOf(Slice(e))), en.Size, en.Crc, e));
        }
        keys.Sort((a, b) => a.Name != b.Name ? a.Name.CompareTo(b.Name) : a.Size.CompareTo(b.Size));

        var dup = new bool[n];
        var crcCount = new Dictionary<uint, int>();
        for (int i = 0; i < keys.Count;)
        {
            int j = i + 1;
            while (j < keys.Count && keys[j].Name == keys[i].Name && keys[j].Size == keys[i].Size) j++;
            if (j - i > 1)
            {
                crcCount.Clear();
                int unknown = 0;
                for (int k = i; k < j; k++)
                {
                    if (keys[k].Crc == 0) unknown++;
                    else crcCount[keys[k].Crc] = crcCount.GetValueOrDefault(keys[k].Crc) + 1;
                }
                for (int k = i; k < j; k++)
                    dup[keys[k].Entry] = keys[k].Crc == 0 || unknown > 0 || crcCount[keys[k].Crc] > 1;
            }
            i = j;
        }
        return dup;
    }

    static ulong Fnv1a(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037;
        foreach (byte b in data) h = (h ^ b) * 1099511628211;
        return h;
    }

    /// <summary>
    /// Aynı adlı (büyük/küçük harf ve aksan duyarsız) tüm dosyalar, en yeniden eskiye: "bu dosyanın en yeni sürümü hangi yedekte".
    /// </summary>
    public int[] SameName(int hit)
    {
        if (IsDirectoryAt(hit)) return [hit];
        byte[] name = NameOf(Slice(hit)).ToArray();
        var list = new List<int>();
        for (int e = 0; e < EntryCount; e++)
            if (!IsDirectoryAt(e) && NameOf(Slice(e)).SequenceEqual(name)) list.Add(e);
        list.Sort((a, b) =>
        {
            int c = MtimeAt(b).CompareTo(MtimeAt(a));
            return c != 0 ? c : a.CompareTo(b);
        });
        return [.. list];
    }
}
