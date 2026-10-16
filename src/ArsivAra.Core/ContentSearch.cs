using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace ArsivAra.Core;

/// <summary>
/// The "icerik:" search: looks inside the files of the results already narrowed down by the other conditions.
/// Plain-text types and Office/OpenDocument documents (docx, xlsx, pptx, odt, ods, odp) are read; PDFs and binary files are skipped.
/// Each archive is opened once (a single pass for a solid archive). It is a slow operation; progress is reported and it can be cancelled.
/// </summary>
public static class ContentSearch
{
    static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".log", ".ini", ".cfg", ".conf", ".config", ".json", ".xml", ".yml", ".yaml",
        ".toml", ".properties", ".html", ".htm", ".css", ".scss", ".less", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".vue", ".svelte",
        ".cs", ".vb", ".fs", ".java", ".kt", ".swift", ".py", ".rb", ".php", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc",
        ".m", ".r", ".lua", ".pl", ".sh", ".bat", ".cmd", ".ps1", ".sql", ".tex", ".srt", ".vtt", ".rtf", ".gradle", ".csproj",
        ".sln", ".xaml", ".svg", ".nfo", ".diz",
    };

    static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".odt", ".ods", ".odp",
    };

    public const long MaxTextBytes = 4L << 20;
    public const long MaxOfficeBytes = 32L << 20;
    public const int MaxCandidates = 20_000;

    public static bool IsTextName(string name) => TextExtensions.Contains(Path.GetExtension(name));
    public static bool IsOfficeName(string name) => OfficeExtensions.Contains(Path.GetExtension(name));

    /// <summary>Whether this file's contents can be searched (type and size).</summary>
    public static bool CanSearch(in ArchiveEntry e)
    {
        if (e.IsDirectory) return false;
        string ext = Path.GetExtension(e.Name);
        return (TextExtensions.Contains(ext) && e.Size <= MaxTextBytes) || (OfficeExtensions.Contains(ext) && e.Size <= MaxOfficeBytes);
    }

    public sealed record Result(int[] Hits, int Searched, int Skipped, int Failed);

    /// <param name="hits">Results that match the other conditions.</param>
    /// <param name="foldedTerms">All must appear in the file (folded: lowercase, accent-free).</param>
    /// <param name="passwordFor">Password for encrypted archives (null if none).</param>
    /// <param name="progress">Number of files checked / total.</param>
    public static Result Filter(SearchIndex index, int[] hits, IReadOnlyList<string> foldedTerms, Func<ArchiveRecord, string?> passwordFor,
        IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var byArchive = new Dictionary<int, Dictionary<string, List<int>>>();
        int candidates = 0, skipped = 0;
        foreach (int hit in hits)
        {
            var (_, entry) = index.Get(hit);
            if (!CanSearch(entry) || candidates >= MaxCandidates) { skipped++; continue; }
            int a = index.ArchiveIndexOf(hit);
            if (!byArchive.TryGetValue(a, out var paths)) byArchive[a] = paths = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (!paths.TryGetValue(entry.Path, out var list)) paths[entry.Path] = list = [];
            list.Add(hit);
            candidates++;
        }

        var matched = new ConcurrentBag<int>();
        int done = 0, failed = 0;
        progress?.Report((0, candidates));
        // Many parallel reads on the same disk (especially an HDD) slow things down: only a few archives at a time.
        Parallel.ForEach(byArchive, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, kv =>
        {
            var rec = index.Archives[kv.Key];
            var paths = kv.Value;
            try
            {
                ArchiveExtractor.ForEach(rec, paths.Keys.ToHashSet(StringComparer.Ordinal), passwordFor(rec), (item, s) =>
                {
                    string text;
                    try { text = ExtractText(item.Path, s); }
                    catch (Exception) { Interlocked.Increment(ref failed); text = ""; }
                    if (text.Length > 0 && ContainsAll(Folding.Fold(text), foldedTerms))
                        foreach (int h in paths[item.Path]) matched.Add(h);
                    progress?.Report((Interlocked.Increment(ref done), candidates));
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Add(ref failed, paths.Count);
                Log.Warn($"Content search: could not read {rec.DisplayPath}", ex);
            }
        });

        var result = matched.ToArray();
        Array.Sort(result);
        return new Result(result, candidates, skipped, failed);
    }

    static bool ContainsAll(string folded, IReadOnlyList<string> terms)
    {
        foreach (var t in terms)
            if (!folded.Contains(t, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>Plain text of the file: from the XML for Office documents, otherwise by guessing the encoding.</summary>
    public static string ExtractText(string name, Stream content)
    {
        string ext = Path.GetExtension(name);
        if (OfficeExtensions.Contains(ext)) return OfficeText(content, ext);
        return DecodeText(ReadUpTo(content, MaxTextBytes));
    }

    static byte[] ReadUpTo(Stream s, long max)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while (ms.Length < max && (n = s.Read(buf, 0, (int)Math.Min(buf.Length, max - ms.Length))) > 0) ms.Write(buf, 0, n);
        return ms.ToArray();
    }

    static string OfficeText(Stream content, string ext)
    {
        using var buffered = ArchiveReader.TempBuffer(content, -1);
        using var zip = new ZipArchive(buffered, ZipArchiveMode.Read);
        var sb = new StringBuilder();
        ext = ext.ToLowerInvariant();
        foreach (var e in zip.Entries)
        {
            string n = e.FullName;
            bool take = ext switch
            {
                ".docx" or ".docm" => n == "word/document.xml" || n.StartsWith("word/header") || n.StartsWith("word/footer")
                                      || n == "word/footnotes.xml" || n == "word/endnotes.xml" || n == "word/comments.xml",
                ".xlsx" or ".xlsm" => n == "xl/sharedStrings.xml" || n.StartsWith("xl/worksheets/sheet"),
                ".pptx" or ".pptm" => n.StartsWith("ppt/slides/slide") || n.StartsWith("ppt/notesSlides/"),
                _ => n == "content.xml", // OpenDocument
            };
            if (!take || e.Length > 64L << 20) continue;
            using var es = e.Open();
            using var reader = new StreamReader(es, Encoding.UTF8);
            sb.Append(XmlText(reader.ReadToEnd())).Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Text from XML: tags are stripped. Because Word can split a word into several pieces ("Hel" + "lo"), the pieces are
    /// joined without spaces; only paragraph, line, tab and cell ends become spaces.
    /// </summary>
    internal static string XmlText(string xml)
    {
        var sb = new StringBuilder(xml.Length / 2);
        int i = 0;
        while (i < xml.Length)
        {
            int lt = xml.IndexOf('<', i);
            if (lt < 0) { sb.Append(xml, i, xml.Length - i); break; }
            sb.Append(xml, i, lt - i);
            int gt = xml.IndexOf('>', lt);
            if (gt < 0) break;
            var tag = xml.AsSpan(lt + 1, gt - lt - 1).Trim('/');
            int space = tag.IndexOfAny(' ', '\t');
            if (space >= 0) tag = tag[..space];
            int colon = tag.IndexOf(':');
            var local = colon >= 0 ? tag[(colon + 1)..] : tag;
            if (local is "p" or "br" or "tab" or "cr" or "si" or "c" or "row" or "h" or "line-break" or "s" or "tab-stop")
                sb.Append(' ');
            i = gt + 1;
        }
        return WebUtility.HtmlDecode(sb.ToString());
    }

    /// <summary>By the BOM if there is one; otherwise UTF-8 if valid UTF-8, else the system's legacy code page (Windows-1254 for Turkish).</summary>
    public static string DecodeText(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (System.Text.Unicode.Utf8.IsValid(b)) return Encoding.UTF8.GetString(b);
        return LegacyText.GetString(b);
    }

    static readonly Encoding LegacyText = CreateLegacy();

    static Encoding CreateLegacy()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            int cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return Encoding.GetEncoding(cp > 0 ? cp : 1252);
        }
        catch (Exception) { return Encoding.Latin1; }
    }
}
