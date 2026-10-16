using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArsivAra.Core;

/// <summary>Sorgudaki hatalı değer türü (arayüz kendi dilinde gösterir).</summary>
public enum QueryErrorKind { None, Size, Date, Regex }

/// <summary>Sorgu düğümünün baktığı girdi: katlanmış (küçük harf, aksansız) UTF-8 yol ve dizindeki sırası.</summary>
internal readonly ref struct EntryView(SearchIndex index, int entry, ReadOnlySpan<byte> path)
{
    public readonly SearchIndex Index = index;
    public readonly int Entry = entry;

    /// <summary>Katlanmış yol; klasörlerde sondaki '/' dahil.</summary>
    public readonly ReadOnlySpan<byte> Path = path;

    public ReadOnlySpan<byte> Name => SearchIndex.NameOf(Path);
    public ReadOnlySpan<byte> PathNoSlash => Path.Length > 0 && Path[^1] == '/' ? Path[..^1] : Path;
}

internal abstract class QueryNode
{
    public abstract bool Match(in EntryView v);
}

sealed class AllNode : QueryNode
{
    public static readonly AllNode Instance = new();
    public override bool Match(in EntryView v) => true;
}

sealed class AndNode(QueryNode[] items) : QueryNode
{
    public readonly QueryNode[] Items = items;
    public override bool Match(in EntryView v)
    {
        foreach (var i in Items) if (!i.Match(v)) return false;
        return true;
    }
}

sealed class OrNode(QueryNode[] items) : QueryNode
{
    public readonly QueryNode[] Items = items;
    public override bool Match(in EntryView v)
    {
        foreach (var i in Items) if (i.Match(v)) return true;
        return false;
    }
}

sealed class NotNode(QueryNode item) : QueryNode
{
    public override bool Match(in EntryView v) => !item.Match(v);
}

/// <summary>Kelime ya da "tam ifade": yolda (ad: ile sadece adda) geçmeli.</summary>
sealed class TermNode(byte[] term, bool nameOnly) : QueryNode
{
    public readonly byte[] Term = term;
    public override bool Match(in EntryView v) => (nameOnly ? v.Name : v.Path).IndexOf(Term) >= 0;
}

/// <summary>ad:="Tam Ad.pdf": adın tamamı eşit olmalı (büyük/küçük harf ve aksan duyarsız).</summary>
sealed class ExactNameNode(byte[] name) : QueryNode
{
    public readonly byte[] Name = name;
    public override bool Match(in EntryView v) => v.Name.SequenceEqual(Name);
}

/// <summary>Joker: * (herhangi bir dizi) ve ? (tek karakter); adın (desende '/' varsa yolun) tamamına uymalı.</summary>
sealed class GlobNode(byte[] pattern, bool fullPath) : QueryNode
{
    public readonly byte[] Pattern = pattern;
    public override bool Match(in EntryView v) => SearchIndex.GlobMatch(fullPath ? v.PathNoSlash : v.Name, Pattern);
}

sealed class ExtNode(byte[][] extensions) : QueryNode
{
    public override bool Match(in EntryView v)
    {
        if (v.Index.IsDirectoryAt(v.Entry)) return false;
        foreach (var x in extensions) if (v.Path.EndsWith(x)) return true;
        return false;
    }
}

sealed class KindNode(bool directories) : QueryNode
{
    public override bool Match(in EntryView v) => v.Index.IsDirectoryAt(v.Entry) == directories;
}

sealed class SizeNode(long min, long max) : QueryNode
{
    public override bool Match(in EntryView v)
    {
        if (v.Index.IsDirectoryAt(v.Entry)) return false;
        long s = v.Index.SizeAt(v.Entry);
        return s >= min && s <= max;
    }
}

sealed class DateNode(long minUtcTicks, long maxUtcTicks) : QueryNode
{
    public override bool Match(in EntryView v)
    {
        long t = v.Index.MtimeAt(v.Entry);
        return t > 0 && t >= minUtcTicks && t <= maxUtcTicks;
    }
}

sealed class RegexNode(Regex regex) : QueryNode
{
    public override bool Match(in EntryView v)
    {
        try { return regex.IsMatch(v.Index.EntryAt(v.Entry).Path); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}

/// <summary>arsiv:yedek — arşivin yolunda geçmeli. İzin verilen arşivler aramadan önce dizine göre hesaplanır.</summary>
sealed class ArchiveNode(string foldedTerm) : QueryNode
{
    public readonly string Term = foldedTerm;
    public bool[] Allowed = [];
    public override bool Match(in EntryView v) => Allowed[v.Index.ArchiveIndexOf(v.Entry)];
}

/// <summary>kopya: — aynı ad ve boyutta (CRC biliniyorsa aynı CRC'de) başka bir dosya da var.</summary>
sealed class DupNode : QueryNode
{
    public override bool Match(in EntryView v) => v.Index.IsDuplicate(v.Entry);
}

internal sealed class ParsedQuery
{
    public required QueryNode Root { get; init; }

    /// <summary>Tampon bununla taranır (en uzun zorunlu kelime); null ise her girdiye bakılır.</summary>
    public byte[]? ScanTerm { get; init; }

    public List<string> ContentTerms { get; init; } = [];
    public List<ArchiveNode> ArchiveNodes { get; init; } = [];
    public QueryErrorKind Error { get; init; }
    public string? ErrorDetail { get; init; }
}

/// <summary>
/// Arama sorgusu ayrıştırıcısı. Sözdizimi (Everything'e benzer; Türkçe ve İngilizce anahtar adları):
/// <code>
///   kelime1 kelime2        VE: hepsi yolda geçmeli
///   a|b  ya da  a | b      VEYA (boşluktan önce bağlanır: "x a|b" = x VE (a VEYA b))
///   !kelime                DEĞİL
///   "tam ifade"            boşluklu ifade olduğu gibi
///   *.pdf  rapor?.docx     joker: adın tamamına uymalı (desende '/' varsa yolun)
///   ext:pdf;docx           uzantı                     arsiv:yedek     arşivin yolunda geçmeli
///   klasor: / dosya:       sadece klasörler/dosyalar   ad:rapor        sadece adda   ad:="Tam Ad.pdf"
///   boyut:>10mb  boyut:1mb..1gb  boyut:10mb (en az)    tarih:2024  tarih:>=2024-06-01  tarih:2023..2024  tarih:bugun
///   regex:^fatura_\d+      düzenli ifade (yolda)       kopya:          aynı ad ve boyutta başka dosya var
///   icerik:"metin"         dosyaların içinde (metin, Office belgeleri; yavaş, sonradan uygulanır)
/// </code>
/// </summary>
internal static class QueryParser
{
    sealed class Ctx(DateTime now)
    {
        public readonly DateTime Now = now;
        public readonly List<string> ContentTerms = [];
        public readonly List<ArchiveNode> ArchiveNodes = [];
        public QueryErrorKind Error;
        public string? ErrorDetail;

        public void Fail(QueryErrorKind kind, string detail)
        {
            if (Error != QueryErrorKind.None) return;
            Error = kind;
            ErrorDetail = detail;
        }
    }

    public static ParsedQuery Parse(string text, DateTime? now = null)
    {
        var ctx = new Ctx(now ?? DateTime.Now);
        var groups = new List<List<QueryNode>>();
        bool joinNext = false;

        foreach (string token in Tokenize(text ?? ""))
        {
            if (token == "|")
            {
                joinNext = groups.Count > 0;
                continue;
            }
            // Düzenli ifadede '|' ifadenin parçasıdır.
            var alternatives = IsRegexToken(token) ? [token] : SplitOutsideQuotes(token, '|');
            bool leadingPipe = alternatives.Count > 1 && alternatives[0].Length == 0;
            bool trailingPipe = alternatives.Count > 1 && alternatives[^1].Length == 0;

            var nodes = new List<QueryNode>();
            foreach (var alt in alternatives)
                if (alt.Length > 0 && ParseUnit(alt, ctx) is { } node) nodes.Add(node);

            if (nodes.Count > 0)
            {
                if ((joinNext || leadingPipe) && groups.Count > 0) groups[^1].AddRange(nodes);
                else groups.Add(nodes);
            }
            joinNext = trailingPipe || (nodes.Count == 0 && joinNext);
        }

        if (ctx.Error != QueryErrorKind.None)
            return new ParsedQuery { Root = AllNode.Instance, Error = ctx.Error, ErrorDetail = ctx.ErrorDetail };

        // VE'nin elemanları: tek seçenekli gruplar düzleştirilir (klasor:x gibi iç VE'ler dahil).
        var items = new List<QueryNode>();
        foreach (var g in groups)
        {
            if (g.Count > 1) items.Add(new OrNode([.. g]));
            else if (g[0] is AndNode inner) items.AddRange(inner.Items);
            else items.Add(g[0]);
        }

        QueryNode root = items.Count switch
        {
            0 => AllNode.Instance,
            1 => items[0],
            _ => new AndNode([.. items]),
        };
        return new ParsedQuery
        {
            Root = root,
            ScanTerm = PickScanTerm(items),
            ContentTerms = ctx.ContentTerms,
            ArchiveNodes = ctx.ArchiveNodes,
        };
    }

    /// <summary>VE'nin zorunlu elemanlarından en uzun düz metin: tampon onunla taranır, diğer koşullar sadece adaylarda denenir.</summary>
    static byte[]? PickScanTerm(List<QueryNode> items)
    {
        byte[]? best = null;
        foreach (var n in items)
        {
            byte[]? candidate = n switch
            {
                TermNode t => t.Term,
                ExactNameNode e => e.Name,
                GlobNode g => LongestLiteral(g.Pattern),
                _ => null,
            };
            if (candidate is { Length: > 0 } && (best == null || candidate.Length > best.Length)) best = candidate;
        }
        return best;
    }

    static byte[]? LongestLiteral(byte[] pattern)
    {
        int bestStart = 0, bestLen = 0, start = 0;
        for (int i = 0; i <= pattern.Length; i++)
        {
            if (i < pattern.Length && pattern[i] is not ((byte)'*' or (byte)'?')) continue;
            if (i - start > bestLen) { bestStart = start; bestLen = i - start; }
            start = i + 1;
        }
        return bestLen >= 2 ? pattern.AsSpan(bestStart, bestLen).ToArray() : null;
    }

    static QueryNode? ParseUnit(string s, Ctx ctx)
    {
        bool negate = false;
        while (s.Length > 0 && s[0] == '!')
        {
            negate = !negate;
            s = s[1..];
        }
        if (s.Length == 0) return null;
        var node = ParseAtom(s, ctx);
        return node == null ? null : negate ? new NotNode(node) : node;
    }

    static QueryNode? ParseAtom(string s, Ctx ctx)
    {
        int colon = IndexOfOutsideQuotes(s, ':');
        if (colon > 0)
        {
            string key = Folding.Fold(s[..colon]);
            string raw = s[(colon + 1)..];
            string value = Unquote(raw).Trim();
            switch (key)
            {
                case "ext" or "uzanti":
                {
                    var exts = value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => Encoding.UTF8.GetBytes(Folding.Fold("." + x.TrimStart('.'))))
                        .ToArray();
                    return exts.Length == 0 ? null : new ExtNode(exts);
                }
                case "arsiv" or "archive":
                {
                    if (value.Length == 0) return null;
                    var node = new ArchiveNode(Folding.Fold(value));
                    ctx.ArchiveNodes.Add(node);
                    return node;
                }
                case "klasor" or "folder":
                    return value.Length == 0 ? new KindNode(true) : new AndNode([new KindNode(true), NameNode(value)]);
                case "dosya" or "file":
                    return value.Length == 0 ? new KindNode(false) : new AndNode([new KindNode(false), NameNode(value)]);
                case "ad" or "name":
                    return value.Length == 0 ? null : NameNode(value);
                case "yol" or "path":
                    return value.Length == 0 ? null : WordNode(value, quoted: raw.StartsWith('"'), fullPath: true);
                case "boyut" or "size":
                    if (!TryParseSize(value, out long minSize, out long maxSize)) { ctx.Fail(QueryErrorKind.Size, value); return null; }
                    return new SizeNode(minSize, maxSize);
                case "tarih" or "date" or "dm":
                    if (!TryParseDate(value, ctx.Now, out long minDate, out long maxDate)) { ctx.Fail(QueryErrorKind.Date, value); return null; }
                    return new DateNode(minDate, maxDate);
                case "regex" or "rx":
                {
                    string pattern = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;
                    if (pattern.Length == 0) return null;
                    try { return new RegexNode(BuildRegex(pattern)); }
                    catch (ArgumentException) { ctx.Fail(QueryErrorKind.Regex, pattern); return null; }
                }
                case "kopya" or "dupe" or "dupes":
                    return new DupNode();
                case "icerik" or "content":
                    if (value.Length > 0) ctx.ContentTerms.Add(Folding.Fold(value));
                    return null;
            }
        }
        return WordNode(Unquote(s), quoted: s.StartsWith('"'), fullPath: false);
    }

    static QueryNode NameNode(string value)
    {
        if (value.StartsWith('='))
            return new ExactNameNode(Encoding.UTF8.GetBytes(Folding.Fold(value[1..].Trim())));
        if (value.Contains('*') || value.Contains('?'))
            return new GlobNode(Encoding.UTF8.GetBytes(Folding.Fold(value)), fullPath: false);
        return new TermNode(Encoding.UTF8.GetBytes(Folding.Fold(value)), nameOnly: true);
    }

    static QueryNode WordNode(string word, bool quoted, bool fullPath)
    {
        if (!quoted && (word.Contains('*') || word.Contains('?')))
            return new GlobNode(Encoding.UTF8.GetBytes(Folding.Fold(word)), fullPath || word.Contains('/') || word.Contains('\\'));
        return new TermNode(Encoding.UTF8.GetBytes(Folding.Fold(word)), nameOnly: false);
    }

    static Regex BuildRegex(string pattern)
    {
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        try { return new Regex(pattern, options | RegexOptions.NonBacktracking); }
        catch (NotSupportedException) { return new Regex(pattern, options, TimeSpan.FromMilliseconds(200)); }
    }

    static bool IsRegexToken(string token)
    {
        var t = token.TrimStart('!');
        return t.StartsWith("regex:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("rx:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Boşluklarla ayırır; tırnak içindeki boşluklar ayırmaz (tırnaklar parçada kalır).</summary>
    internal static List<string> Tokenize(string q)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char c in q)
        {
            if (c == '"') inQuote = !inQuote;
            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    static List<string> SplitOutsideQuotes(string s, char separator)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        foreach (char c in s)
        {
            if (c == '"') inQuote = !inQuote;
            if (c == separator && !inQuote)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    static int IndexOfOutsideQuotes(string s, char c)
    {
        bool inQuote = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inQuote = !inQuote;
            else if (s[i] == c && !inQuote) return i;
        }
        return -1;
    }

    static string Unquote(string s) => s.Replace("\"", "");

    // ---- Boyut: >10mb, <=1,5gb, 1mb..1gb, =0, 10mb (en az) ----

    internal static bool TryParseSize(string v, out long min, out long max)
    {
        min = 0;
        max = long.MaxValue;
        v = v.Replace(" ", "");
        if (v.Length == 0) return false;

        int range = v.IndexOf("..", StringComparison.Ordinal);
        if (range >= 0)
        {
            string lo = v[..range], hi = v[(range + 2)..];
            if (lo.Length == 0 && hi.Length == 0) return false;
            if (lo.Length > 0 && !TryParseBytes(lo, out min)) return false;
            if (hi.Length > 0 && !TryParseBytes(hi, out max)) return false;
            return min <= max;
        }

        long x;
        if (v.StartsWith(">=")) return TryParseBytes(v[2..], out min);
        if (v.StartsWith("<=")) return TryParseBytes(v[2..], out max);
        if (v.StartsWith('>'))
        {
            if (!TryParseBytes(v[1..], out x)) return false;
            min = x + 1;
            return true;
        }
        if (v.StartsWith('<'))
        {
            if (!TryParseBytes(v[1..], out x) || x == 0) return false;
            max = x - 1;
            return true;
        }
        if (v.StartsWith('='))
        {
            if (!TryParseBytes(v[1..], out x)) return false;
            min = max = x;
            return true;
        }
        return TryParseBytes(v, out min);
    }

    internal static bool TryParseBytes(string s, out long bytes)
    {
        bytes = 0;
        s = s.Trim().ToLowerInvariant().Replace(',', '.');
        int i = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.')) i++;
        if (i == 0 || !double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return false;
        double multiplier = s[i..] switch
        {
            "" or "b" or "bayt" or "byte" or "bytes" => 1,
            "k" or "kb" or "kib" => 1024,
            "m" or "mb" or "mib" => 1 << 20,
            "g" or "gb" or "gib" => 1 << 30,
            "t" or "tb" or "tib" => 1099511627776d,
            _ => -1,
        };
        if (multiplier < 0) return false;
        double b = number * multiplier;
        if (b > long.MaxValue / 2.0) return false;
        bytes = (long)Math.Round(b);
        return true;
    }

    // ---- Tarih: 2024, 2024-06, 2024-06-15, 15.06.2024, bugun, buay..., >=, <, a..b ----

    internal static bool TryParseDate(string v, DateTime now, out long minUtc, out long maxUtc)
    {
        minUtc = 0;
        maxUtc = long.MaxValue;
        v = Folding.Fold(v).Replace(" ", "");
        if (v.Length == 0) return false;

        int range = v.IndexOf("..", StringComparison.Ordinal);
        if (range >= 0)
        {
            string lo = v[..range], hi = v[(range + 2)..];
            if (lo.Length == 0 && hi.Length == 0) return false;
            if (lo.Length > 0)
            {
                if (!TryPeriod(lo, now, out var s, out _)) return false;
                minUtc = Utc(s);
            }
            if (hi.Length > 0)
            {
                if (!TryPeriod(hi, now, out _, out var e)) return false;
                maxUtc = Utc(e) - 1;
            }
            return minUtc <= maxUtc;
        }

        string op = v.StartsWith(">=") || v.StartsWith("<=") ? v[..2] : v[0] is '>' or '<' or '=' ? v[..1] : "";
        if (!TryPeriod(v[op.Length..], now, out var start, out var end)) return false;
        switch (op)
        {
            case ">": minUtc = Utc(end); break;
            case ">=": minUtc = Utc(start); break;
            case "<": maxUtc = Utc(start) - 1; break;
            case "<=": maxUtc = Utc(end) - 1; break;
            default: minUtc = Utc(start); maxUtc = Utc(end) - 1; break;
        }
        return true;
    }

    static long Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime().Ticks;

    static readonly Regex Ymd = new(@"^(\d{4})[-./](\d{1,2})[-./](\d{1,2})$", RegexOptions.CultureInvariant);
    static readonly Regex Dmy = new(@"^(\d{1,2})[-./](\d{1,2})[-./](\d{4})$", RegexOptions.CultureInvariant);
    static readonly Regex Ym = new(@"^(\d{4})[-./](\d{1,2})$", RegexOptions.CultureInvariant);
    static readonly Regex My = new(@"^(\d{1,2})[-./](\d{4})$", RegexOptions.CultureInvariant);
    static readonly Regex Y = new(@"^(\d{4})$", RegexOptions.CultureInvariant);

    /// <summary>Yerel saatle dönem: [start, end).</summary>
    static bool TryPeriod(string s, DateTime now, out DateTime start, out DateTime end)
    {
        var today = now.Date;
        start = end = default;
        switch (s)
        {
            case "bugun" or "today": start = today; end = today.AddDays(1); return true;
            case "dun" or "yesterday": start = today.AddDays(-1); end = today; return true;
            case "buhafta" or "thisweek": start = WeekStart(today); end = start.AddDays(7); return true;
            case "gecenhafta" or "lastweek": start = WeekStart(today).AddDays(-7); end = start.AddDays(7); return true;
            case "buay" or "thismonth": start = new DateTime(today.Year, today.Month, 1); end = start.AddMonths(1); return true;
            case "gecenay" or "lastmonth": start = new DateTime(today.Year, today.Month, 1).AddMonths(-1); end = start.AddMonths(1); return true;
            case "buyil" or "thisyear": start = new DateTime(today.Year, 1, 1); end = start.AddYears(1); return true;
            case "gecenyil" or "lastyear": start = new DateTime(today.Year - 1, 1, 1); end = start.AddYears(1); return true;
        }

        Match m;
        try
        {
            if ((m = Ymd.Match(s)).Success) start = new DateTime(Int(m, 1), Int(m, 2), Int(m, 3));
            else if ((m = Dmy.Match(s)).Success) start = new DateTime(Int(m, 3), Int(m, 2), Int(m, 1));
            else if ((m = Ym.Match(s)).Success) { start = new DateTime(Int(m, 1), Int(m, 2), 1); end = start.AddMonths(1); return true; }
            else if ((m = My.Match(s)).Success) { start = new DateTime(Int(m, 2), Int(m, 1), 1); end = start.AddMonths(1); return true; }
            else if ((m = Y.Match(s)).Success) { start = new DateTime(Int(m, 1), 1, 1); end = start.AddYears(1); return true; }
            else return false;
        }
        catch (ArgumentOutOfRangeException) { return false; } // 2024-13-45 gibi
        end = start.AddDays(1);
        return true;
    }

    static int Int(Match m, int group) => int.Parse(m.Groups[group].ValueSpan, CultureInfo.InvariantCulture);

    static DateTime WeekStart(DateTime day)
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int diff = (7 + (day.DayOfWeek - first)) % 7;
        return day.AddDays(-diff);
    }
}
