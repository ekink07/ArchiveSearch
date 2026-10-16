using System.Diagnostics;
using System.Text;
using ArsivAra.Core;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

string banner = $"{AppInfo.Name} CLI {AppInfo.Version} · {AppInfo.Company} · {AppInfo.Credit}";
string indexPath = AppPaths.IndexFile;
bool useMft = true;
string? password = null;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--index" && i + 1 < args.Length) indexPath = args[++i];
    else if (args[i] == "--no-mft") useMft = false;
    else if (args[i] is "--password" or "--parola" && i + 1 < args.Length) password = args[++i];
    else if (args[i] == "--hdd-readers" && i + 1 < args.Length) Scanner.HddReaders = int.Parse(args[++i]);
    else if (args[i] == "--ssd-readers" && i + 1 < args.Length) Scanner.SsdReaders = int.Parse(args[++i]);
    else rest.Add(args[i]);
}

string command = rest.Count > 0 ? rest[0] : "";
switch (command)
{
    case "index":
        Index(rest.Skip(1).ToList());
        break;
    case "search":
        RunSearch(LoadIndex(), string.Join(' ', rest.Skip(1)), 30);
        break;
    case "extract" when rest.Count == 3:
        Extract(Path.GetFullPath(rest[1]), rest[2]);
        break;
    case "mftdebug" when rest.Count >= 3:
        MftDebug(rest[1], rest.Skip(2).ToArray());
        break;
    case "profile" when rest.Count >= 2:
        Profile(rest[1], rest.Count > 2 ? rest[2] : null, rest.Count > 3 ? int.Parse(rest[3]) : int.MaxValue);
        break;
    case "verifyrar":
        VerifyRar(rest.Count > 1 ? rest[1] : "");
        break;
    case "list":
        foreach (var a in IndexFile.TryLoad(indexPath).Where(a => rest.Count < 2 || a.Path.StartsWith(rest[1], StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine(a.Path);
        break;
    case "watch":
        Watch(rest.Skip(1).ToList());
        break;
    case "--version":
    case "version":
        Console.WriteLine(banner);
        Console.WriteLine($"{AppInfo.Copyright} · {AppInfo.LicenseName} license (details: ArsivAra.Cli license)");
        break;
    case "lisans":
    case "license":
        Console.WriteLine(AppInfo.LicenseText);
        Console.WriteLine();
        Console.WriteLine(AppInfo.ThirdPartyNotices);
        break;
    case "":
    case "interactive":
        Interactive();
        break;
    default:
        Console.WriteLine(banner);
        Console.WriteLine("""
            Usage:
              ArsivAra.Cli index <folder1> [folder2 ...]   Scan and index archives (all fixed disks if no folder is given)
              ArsivAra.Cli search <query>                  Search the index
              ArsivAra.Cli                                 Interactive search
              ArsivAra.Cli watch <folder> [seconds]        Print live archive changes from the USN Journal (administrator)
              ArsivAra.Cli extract <archive> <entry path>  Extract a single file to a temp folder and print its path
              ArsivAra.Cli --version                       Version, manufacturer and creator info
              ArsivAra.Cli license                         License text and third-party components (alias: lisans)
              --no-mft                                     Scan by walking folders instead of the MFT (for comparison)
              --index <file>                               Index file (default: %LOCALAPPDATA%\ArsivAra\index.bin)
              --password <password>                        For encrypted archives (alias: --parola)

            Query: word1 word2 (AND)  a|b (OR)  !word (NOT)  "exact phrase"  *.pdf  report?.doc
                   ext:pdf;docx  archive:backup  folder:  file:  name:report  name:="Full Name.pdf"
                   size:>10mb  size:1mb..1gb  date:2024  date:>=2024-06-01  regex:^invoice_\d+  dupe:
            """);
        break;
}

void Index(List<string> roots)
{
    var settings = Settings.Load();
    if (roots.Count == 0) roots = settings.EffectiveRoots.ToList();
    roots = roots.Select(Path.GetFullPath).ToList();

    var previous = IndexFile.TryLoad(indexPath).ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
    Console.WriteLine($"Scanning: {string.Join(", ", roots)}");

    var sw = Stopwatch.StartNew();
    var result = Scanner.Scan(roots, settings.Excludes, previous, p =>
    {
        Console.Write(p.Processed == 0 && p.Current != null
            ? $"\r  {p.Current}   "
            : $"\r  {p.Processed:N0} archives processed ({p.Reused:N0} from cache, {p.Failed:N0} unreadable)   ");
    }, useMft: useMft);
    Console.WriteLine();
    // On a partial scan (e.g. only C:\) keep the other roots' records in the index.
    var archives = result.Archives
        .Concat(previous.Values.Where(a => !roots.Any(r => a.Path.StartsWith(r.EndsWith('\\') ? r : r + "\\", StringComparison.OrdinalIgnoreCase))))
        .OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    foreach (var j in result.Journals) Console.WriteLine($"  Scanned via MFT: {j.Root}");
    foreach (var w in result.WalkedRoots) Console.WriteLine($"  Scanned by walking folders: {w}");
    if (useMft && result.Journals.Count == 0 && !NtfsVolume.IsElevated)
        Console.WriteLine("  (run as administrator to use the MFT)");

    long entries = result.Archives.Sum(a => (long)a.Entries.Length);
    Console.WriteLine($"Done: {result.Archives.Count:N0} archives, {entries:N0} entries — discovery {result.DiscoveryTime.TotalSeconds:0.00} s, total {sw.Elapsed.TotalSeconds:0.00} s");

    if (result.SlowArchives.Count > 0)
    {
        Console.WriteLine($"  Slowest archives to read ({result.SlowArchives.Count:N0} took longer than 2 s, {result.SlowArchives.Sum(s => s.Seconds):N0} s in total):");
        foreach (var (p, s) in result.SlowArchives.Take(15))
            Console.WriteLine($"    {s,7:0.0} s  {new FileInfo(p).Length / (1024.0 * 1024),9:N1} MB  {p}");
    }

    foreach (var bad in result.Archives.Where(a => a.Error != null).Take(20))
        Console.WriteLine($"  ! {bad.Path}: {bad.Error}");

    sw.Restart();
    IndexFile.Save(archives, indexPath);
    Console.WriteLine($"Index saved: {indexPath} ({new FileInfo(indexPath).Length / 1024.0:N0} KB, {sw.ElapsedMilliseconds} ms)");
}

void Extract(string archivePath, string entryPath)
{
    var fi = new FileInfo(archivePath);
    var rec = ArchiveReader.Read(archivePath, fi.Length, fi.LastWriteTimeUtc.Ticks, new ReadOptions { Password = password, ReadNested = false });
    if (rec.Error != null)
    {
        Console.WriteLine($"Could not read archive ({rec.ErrorKind}): {rec.Error}");
        return;
    }
    var entry = rec.Entries.FirstOrDefault(e => e.Path == ArchiveReader.NormalizePath(entryPath));
    if (entry.Path == null)
    {
        Console.WriteLine("Not found in archive: " + entryPath);
        return;
    }
    Console.WriteLine(ArchiveExtractor.ExtractToTemp(rec, entry, password));
}

void Watch(List<string> a)
{
    string root = Path.GetFullPath(a.Count > 0 ? a[0] : "C:\\");
    int seconds = a.Count > 1 ? int.Parse(a[1]) : 30;
    using var vol = NtfsVolume.TryOpen(root);
    if (vol?.QueryJournal() is not { } j)
    {
        Console.WriteLine("Could not open the USN Journal (run as administrator; the volume must be NTFS).");
        return;
    }
    using var watcher = UsnWatcher.TryStart(new VolumeJournal(vol.Root, j.JournalId, j.NextUsn))!;
    watcher.PathChanged += p => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] archive changed: {p} (exists: {File.Exists(p)})");
    watcher.DirectoryChanged += p => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] folder changed: {p} (exists: {Directory.Exists(p)})");
    watcher.NameRemoved += n => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] parent folder deleted: {n}");
    watcher.ResetRequired += r => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] RESET: {r}");
    watcher.Start();
    Console.WriteLine($"Watching {vol.Root} ({seconds} s)…");
    Thread.Sleep(seconds * 1000);
}

void MftDebug(string drive, string[] names)
{
    using var vol = NtfsVolume.TryOpen(drive) ?? throw new InvalidOperationException("Could not open the volume");
    foreach (var (label, run) in new (string, Action<MftRecordHandler>)[] { ("RAW", h => vol.EnumerateRawMft(h)), ("USN", h => vol.EnumerateUsnDataOnly(h)) })
    {
        long total = 0, dirs = 0;
        run((ulong frn, ulong parent, uint attrs, ReadOnlySpan<char> name) =>
        {
            total++;
            if ((attrs & 0x10) != 0) dirs++;
            foreach (var n in names)
                if (name.Equals(n, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  {label} frn={frn:X16} parent={parent:X16} attrs={attrs:X8} {name}");
        });
        Console.WriteLine($"{label}: {total:N0} names, {dirs:N0} folders");
    }
}

// Reads archives one at a time on a single thread; for each one the time and the increase in the process's I/O counters
// (including extra files opened by SharpCompress) are measured. A measure independent of the disk cache: read calls and bytes.
void Profile(string prefix, string? ext, int max)
{
    var targets = IndexFile.TryLoad(indexPath)
        .Where(a => a.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Where(a => ext == null || a.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        .Take(max).ToList();
    Console.WriteLine($"Profiling {targets.Count:N0} archives…");
    var rows = new List<(string Path, double Ms, ulong Ops, ulong Bytes, int Entries, long Size, string? Err)>();
    var total = Stopwatch.StartNew();
    foreach (var a in targets)
    {
        var fi = new FileInfo(a.Path);
        if (!fi.Exists) continue;
        IoCounters.Get(out var before);
        long t0 = Stopwatch.GetTimestamp();
        var rec = ArchiveReader.Read(a.Path, fi.Length, fi.LastWriteTimeUtc.Ticks);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        IoCounters.Get(out var after);
        rows.Add((a.Path, ms, after.ReadOperationCount - before.ReadOperationCount, after.ReadTransferCount - before.ReadTransferCount, rec.Entries.Length, fi.Length, rec.Error));
        if (rows.Count % 25 == 0) Console.Write($"\r  {rows.Count:N0}/{targets.Count:N0}  {total.Elapsed.TotalSeconds:0}s   ");
    }
    Console.WriteLine($"\rTotal {total.Elapsed.TotalSeconds:0.0} s                ");

    Console.WriteLine("\nBy extension:");
    foreach (var g in rows.GroupBy(r => Path.GetExtension(r.Path).ToLowerInvariant()).OrderByDescending(g => g.Sum(r => r.Ms)))
        Console.WriteLine($"  {g.Key,-7} {g.Count(),5} archives  {g.Sum(r => r.Ms) / 1000,8:0.0} s  {g.Sum(r => (double)r.Ops),12:N0} reads  {g.Sum(r => (double)r.Bytes) / (1 << 20),10:N1} MB read  {g.Sum(r => r.Entries),10:N0} entries");

    Console.WriteLine("\nSlowest 20:");
    foreach (var r in rows.OrderByDescending(r => r.Ms).Take(20))
        Console.WriteLine($"  {r.Ms / 1000,7:0.00} s {r.Ops,9:N0} reads {r.Bytes / (1024.0 * 1024),9:N1} MB  {r.Entries,7:N0} entries  {r.Size / (1024.0 * 1024 * 1024),6:0.00} GB  {r.Path}{(r.Err != null ? "  ! " + r.Err : "")}");

    Console.WriteLine("\nTop 10 by read calls:");
    foreach (var r in rows.OrderByDescending(r => r.Ops).Take(10))
        Console.WriteLine($"  {r.Ops,9:N0} reads {r.Bytes / (1024.0 * 1024),9:N1} MB  {r.Ms / 1000,7:0.00} s  {r.Entries,7:N0} entries  {r.Path}");
}

// Compares the Quick Open lister with SharpCompress entry by entry.
void VerifyRar(string prefix)
{
    var rars = IndexFile.TryLoad(indexPath)
        .Where(a => a.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    (a.Path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) || a.Path.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase)))
        .Select(a => a.Path).Where(File.Exists).ToList();
    int same = 0, fallback = 0, diff = 0;
    double fastMs = 0;
    long reads = 0, hits = 0;
    foreach (var path in rars)
    {
        long t0 = Stopwatch.GetTimestamp();
        var fast = ArchiveReader.TryReadRar5QuickOpen(path);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (fast == null) { fallback++; continue; }
        fastMs += ms; reads += Rar5Lister.LastDiskReads; hits += Rar5Lister.LastCacheHits;

        ArchiveEntry[] slow;
        try { slow = ArchiveReader.ReadWithSharpCompress(path); }
        catch (Exception ex) { Console.WriteLine($"  SharpCompress could not read it ({ex.GetType().Name}), fast path {fast.Length:N0} entries: {path}"); diff++; continue; }

        // SharpCompress may not end folder names with '/'; the comparison ignores a trailing '/' and compares dates to the second.
        static string Key(ArchiveEntry e) => $"{e.Path.TrimEnd('/')}|{e.IsDirectory}|{(e.IsDirectory ? 0 : e.Size)}|{e.MtimeUtcTicks / TimeSpan.TicksPerSecond}";
        var a = fast.Select(Key).Order(StringComparer.Ordinal).ToList();
        var b = slow.Select(Key).Order(StringComparer.Ordinal).ToList();
        if (a.SequenceEqual(b)) { same++; continue; }
        diff++;
        Console.WriteLine($"  DIFF: {path}  fast={fast.Length:N0} SharpCompress={slow.Length:N0}");
        foreach (var x in a.Except(b).Take(3)) Console.WriteLine($"     only fast: {x}");
        foreach (var x in b.Except(a).Take(3)) Console.WriteLine($"     only SC:   {x}");
    }
    Console.WriteLine($"{rars.Count:N0} RAR: {same:N0} identical, {diff:N0} different, {fallback:N0} not eligible for the fast path (RAR4/no QO → SharpCompress)");
    Console.WriteLine($"Fast path: {fastMs / 1000:0.0} s total, {reads:N0} disk reads, {hits:N0} headers from the QO cache");
}

SearchIndex LoadIndex()
{
    var sw = Stopwatch.StartNew();
    var archives = IndexFile.TryLoad(indexPath);
    long loadMs = sw.ElapsedMilliseconds;
    sw.Restart();
    var index = SearchIndex.Build(archives);
    Console.WriteLine($"Index: {archives.Count:N0} archives, {index.EntryCount:N0} entries (load {loadMs} ms, prepare {sw.ElapsedMilliseconds} ms)");
    return index;
}

void RunSearch(SearchIndex index, string query, int max)
{
    var r = index.Search(query);
    foreach (int hit in r.Hits.Take(max))
    {
        var (a, e) = index.Get(hit);
        Console.WriteLine($"  {e.Path,-60} {FormatSize(e.Size),10}   ← {a.Path}");
    }
    if (r.Hits.Length > max) Console.WriteLine($"  ... and {r.Hits.Length - max:N0} more results");
    Console.WriteLine($"{r.Hits.Length:N0} results, {r.Elapsed.TotalMilliseconds:0.00} ms");
}

void Interactive()
{
    Console.WriteLine(banner);
    var index = LoadIndex();
    if (index.EntryCount == 0)
    {
        Console.WriteLine("The index is empty. First run: ArsivAra.Cli index <folder>");
        return;
    }
    while (true)
    {
        Console.Write("\nsearch> ");
        string? line = Console.ReadLine();
        if (line == null || line.Trim() is "q" or "çık" or "exit") break;
        RunSearch(index, line, 20);
    }
}

static string FormatSize(long bytes) => bytes switch
{
    < 1024 => $"{bytes} B",
    < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
    < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
    _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
};





static class IoCounters
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool GetProcessIoCounters(nint process, out IO_COUNTERS counters);

    public static void Get(out IO_COUNTERS c) => GetProcessIoCounters(Process.GetCurrentProcess().Handle, out c);
}


