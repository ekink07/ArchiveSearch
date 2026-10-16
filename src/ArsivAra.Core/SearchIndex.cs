using System.Diagnostics;
using System.Text;

namespace ArsivAra.Core;

public sealed record SearchResult(int[] Hits, TimeSpan Elapsed);

/// <summary>
/// Değişmez (immutable) arama anlık görüntüsü.
/// Tüm arşiv içi yollar katlanmış hâlde tek bir UTF-8 tampona '\n' ile ayrılarak yazılır;
/// arama bu tamponda vektörize IndexOf ile paralel taranır (Everything'in RAM'de tarama mantığı).
///
/// Sorgu sözdizimi:
///   kelime1 kelime2   → hepsi yolda geçmeli (VE)
///   ext:pdf;docx      → uzantı filtresi
///   arsiv:yedek       → arşiv dosyasının yolunda geçmeli
///   klasor: / dosya:  → sadece klasörler / sadece dosyalar
/// </summary>
public sealed class SearchIndex
{
    public IReadOnlyList<ArchiveRecord> Archives { get; }
    readonly string[] _foldedArchivePaths;
    readonly byte[] _text;
    readonly int[] _offsets;   // girdi i'nin tampondaki başlangıcı; _offsets[Count] = son
    readonly int[] _archiveOf;
    readonly int[] _entryOf;

    public int EntryCount => _archiveOf.Length;
    public static SearchIndex Empty { get; } = Build([]);

    SearchIndex(IReadOnlyList<ArchiveRecord> archives, string[] foldedArchivePaths, byte[] text, int[] offsets, int[] archiveOf, int[] entryOf)
    {
        Archives = archives;
        _foldedArchivePaths = foldedArchivePaths;
        _text = text;
        _offsets = offsets;
        _archiveOf = archiveOf;
        _entryOf = entryOf;
    }

    public static SearchIndex Build(IReadOnlyList<ArchiveRecord> archives)
    {
        int count = 0;
        long chars = 0;
        foreach (var a in archives)
        {
            count += a.Entries.Length;
            foreach (var e in a.Entries) chars += e.Path.Length + 1;
        }

        var archiveOf = new int[count];
        var entryOf = new int[count];
        var offsets = new int[count + 1];
        var text = new byte[Math.Max(16, (int)Math.Min(Array.MaxLength, chars + chars / 8))];
        var folded = Array.Empty<char>();
        int pos = 0, idx = 0;

        for (int a = 0; a < archives.Count; a++)
        {
            var entries = archives[a].Entries;
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

        var foldedArchives = archives.Select(a => Folding.Fold(a.Path)).ToArray();
        return new SearchIndex(archives, foldedArchives, text, offsets, archiveOf, entryOf);
    }

    /// <summary>Sonucun ait olduğu arşivin <see cref="Archives"/> içindeki sırası.</summary>
    public int ArchiveIndexOf(int hit) => _archiveOf[hit];

    public (ArchiveRecord Archive, ArchiveEntry Entry) Get(int hit)
    {
        var a = Archives[_archiveOf[hit]];
        return (a, a.Entries[_entryOf[hit]]);
    }

    sealed class Query
    {
        public required byte[]? Primary;
        public required byte[][] Others;
        public required byte[][]? Extensions;
        public required bool[]? ArchiveAllowed;
        public bool? DirectoriesOnly;
    }

    Query Parse(string query)
    {
        var terms = new List<byte[]>();
        var archiveTerms = new List<string>();
        byte[][]? exts = null;
        bool? dirsOnly = null;

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string f = Folding.Fold(token);
            if (f.StartsWith("arsiv:") || f.StartsWith("archive:"))
            {
                string v = f[(f.IndexOf(':') + 1)..];
                if (v.Length > 0) archiveTerms.Add(v);
            }
            else if (f.StartsWith("ext:"))
            {
                exts = f[4..].Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Encoding.UTF8.GetBytes("." + x.TrimStart('.')))
                    .ToArray();
                if (exts.Length == 0) exts = null;
            }
            else if (f is "klasor:" or "folder:") dirsOnly = true;
            else if (f is "dosya:" or "file:") dirsOnly = false;
            else terms.Add(Encoding.UTF8.GetBytes(f));
        }

        // En uzun kelime genelde en seçicidir: tampon onunla taranır, diğerleri sadece adaylarda kontrol edilir.
        terms.Sort((a, b) => b.Length.CompareTo(a.Length));

        bool[]? allowed = null;
        if (archiveTerms.Count > 0)
        {
            allowed = new bool[Archives.Count];
            for (int i = 0; i < allowed.Length; i++)
                allowed[i] = archiveTerms.All(t => _foldedArchivePaths[i].Contains(t, StringComparison.Ordinal));
        }

        return new Query
        {
            Primary = terms.Count > 0 ? terms[0] : null,
            Others = terms.Skip(1).ToArray(),
            Extensions = exts,
            ArchiveAllowed = allowed,
            DirectoriesOnly = dirsOnly,
        };
    }

    public SearchResult Search(string query, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var q = Parse(query);
        int n = EntryCount;
        if (n == 0) return new SearchResult([], sw.Elapsed);

        int chunks = Math.Clamp(n / 50_000, 1, Environment.ProcessorCount * 4);
        var parts = new List<int>[chunks];

        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = ct }, c =>
        {
            int first = (int)((long)n * c / chunks);
            int last = (int)((long)n * (c + 1) / chunks);
            var res = new List<int>();
            var text = _text.AsSpan();

            if (q.Primary == null)
            {
                for (int e = first; e < last; e++)
                    if (Matches(e, q)) res.Add(e);
            }
            else
            {
                int pos = _offsets[first], end = _offsets[last];
                while (pos < end)
                {
                    int i = text[pos..end].IndexOf(q.Primary);
                    if (i < 0) break;
                    int e = EntryAt(pos + i);
                    if (Matches(e, q)) res.Add(e);
                    pos = _offsets[e + 1]; // aynı girdide tekrar aramaya gerek yok
                    if (ct.IsCancellationRequested) break;
                }
            }
            parts[c] = res;
        });
        ct.ThrowIfCancellationRequested();

        var hits = new int[parts.Sum(p => p.Count)];
        int k = 0;
        foreach (var p in parts) { p.CopyTo(hits, k); k += p.Count; }
        return new SearchResult(hits, sw.Elapsed);
    }

    int EntryAt(int bytePos)
    {
        int i = Array.BinarySearch(_offsets, bytePos);
        return i >= 0 ? i : ~i - 1;
    }

    bool Matches(int e, Query q)
    {
        if (q.ArchiveAllowed != null && !q.ArchiveAllowed[_archiveOf[e]]) return false;

        var slice = _text.AsSpan(_offsets[e], _offsets[e + 1] - _offsets[e] - 1);
        if (q.DirectoriesOnly is bool d)
        {
            bool isDir = Archives[_archiveOf[e]].Entries[_entryOf[e]].IsDirectory;
            if (isDir != d) return false;
        }
        if (q.Extensions != null)
        {
            bool any = false;
            foreach (var x in q.Extensions)
                if (slice.EndsWith(x)) { any = true; break; }
            if (!any) return false;
        }
        foreach (var t in q.Others)
            if (slice.IndexOf(t) < 0) return false;
        return true;
    }
}
