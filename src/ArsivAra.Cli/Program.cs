using System.Diagnostics;
using System.Text;
using ArsivAra.Core;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

string banner = $"{AppInfo.Name} CLI {AppInfo.Version} · {AppInfo.Credit}";
string indexPath = AppPaths.IndexFile;
bool useMft = true;
string? password = null;
var rest = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--index" && i + 1 < args.Length) indexPath = args[++i];
    else if (args[i] == "--no-mft") useMft = false;
    else if (args[i] == "--parola" && i + 1 < args.Length) password = args[++i];
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
        break;
    case "":
    case "interactive":
        Interactive();
        break;
    default:
        Console.WriteLine(banner);
        Console.WriteLine("""
            Kullanım:
              ArsivAra.Cli index <klasör1> [klasör2 ...]   Arşivleri tara ve indeksle (klasör yoksa tüm sabit diskler)
              ArsivAra.Cli search <sorgu>                  İndekste ara
              ArsivAra.Cli                                 Etkileşimli arama
              ArsivAra.Cli watch <klasör> [saniye]         USN Journal'dan canlı arşiv değişikliklerini yazdır (yönetici)
              ArsivAra.Cli extract <arşiv> <iç yol>        Tek dosyayı geçici klasöre çıkar ve yolunu yazdır
              ArsivAra.Cli --version                       Sürüm ve yapımcı bilgisi
              --no-mft                                     MFT yerine klasör gezerek tara (karşılaştırma için)
              --index <dosya>                              İndeks dosyası (varsayılan: %LOCALAPPDATA%\ArsivAra\index.bin)
              --parola <parola>                            Şifreli arşivler için

            Sorgu: kelime1 kelime2 (VE)  a|b (VEYA)  !kelime (DEĞİL)  "tam ifade"  *.pdf  rapor?.doc
                   ext:pdf;docx  arsiv:yedek  klasor:  dosya:  ad:rapor  ad:="Tam Ad.pdf"
                   boyut:>10mb  boyut:1mb..1gb  tarih:2024  tarih:>=2024-06-01  regex:^fatura_\d+  kopya:
            """);
        break;
}

void Index(List<string> roots)
{
    var settings = Settings.Load();
    if (roots.Count == 0) roots = settings.EffectiveRoots.ToList();
    roots = roots.Select(Path.GetFullPath).ToList();

    var previous = IndexFile.TryLoad(indexPath).ToDictionary(a => a.Path, StringComparer.OrdinalIgnoreCase);
    Console.WriteLine($"Taranıyor: {string.Join(", ", roots)}");

    var sw = Stopwatch.StartNew();
    var result = Scanner.Scan(roots, settings.Excludes, previous, p =>
    {
        Console.Write(p.Processed == 0 && p.Current != null
            ? $"\r  {p.Current}   "
            : $"\r  {p.Processed:N0} arşiv işlendi ({p.Reused:N0} önbellekten, {p.Failed:N0} okunamadı)   ");
    }, useMft: useMft);
    Console.WriteLine();
    // Kısmi taramada (ör. sadece C:\) diğer köklerin kayıtları indekste kalsın.
    var archives = result.Archives
        .Concat(previous.Values.Where(a => !roots.Any(r => a.Path.StartsWith(r.EndsWith('\\') ? r : r + "\\", StringComparison.OrdinalIgnoreCase))))
        .OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    foreach (var j in result.Journals) Console.WriteLine($"  MFT ile tarandı: {j.Root}");
    foreach (var w in result.WalkedRoots) Console.WriteLine($"  Klasör gezilerek tarandı: {w}");
    if (useMft && result.Journals.Count == 0 && !NtfsVolume.IsElevated)
        Console.WriteLine("  (MFT için yönetici olarak çalıştırın)");

    long entries = result.Archives.Sum(a => (long)a.Entries.Length);
    Console.WriteLine($"Bitti: {result.Archives.Count:N0} arşiv, {entries:N0} girdi — bulma {result.DiscoveryTime.TotalSeconds:0.00} sn, toplam {sw.Elapsed.TotalSeconds:0.00} sn");

    if (result.SlowArchives.Count > 0)
    {
        Console.WriteLine($"  En yavaş okunan arşivler ({result.SlowArchives.Count:N0} tanesi 2 sn'den uzun, toplam {result.SlowArchives.Sum(s => s.Seconds):N0} sn):");
        foreach (var (p, s) in result.SlowArchives.Take(15))
            Console.WriteLine($"    {s,7:0.0} sn  {new FileInfo(p).Length / (1024.0 * 1024),9:N1} MB  {p}");
    }

    foreach (var bad in result.Archives.Where(a => a.Error != null).Take(20))
        Console.WriteLine($"  ! {bad.Path}: {bad.Error}");

    sw.Restart();
    IndexFile.Save(archives, indexPath);
    Console.WriteLine($"İndeks kaydedildi: {indexPath} ({new FileInfo(indexPath).Length / 1024.0:N0} KB, {sw.ElapsedMilliseconds} ms)");
}

void Extract(string archivePath, string entryPath)
{
    var fi = new FileInfo(archivePath);
    var rec = ArchiveReader.Read(archivePath, fi.Length, fi.LastWriteTimeUtc.Ticks, new ReadOptions { Password = password, ReadNested = false });
    if (rec.Error != null)
    {
        Console.WriteLine($"Arşiv okunamadı ({rec.ErrorKind}): {rec.Error}");
        return;
    }
    var entry = rec.Entries.FirstOrDefault(e => e.Path == ArchiveReader.NormalizePath(entryPath));
    if (entry.Path == null)
    {
        Console.WriteLine("Arşivde bulunamadı: " + entryPath);
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
        Console.WriteLine("USN Journal açılamadı (yönetici olarak çalıştırın, birim NTFS olmalı).");
        return;
    }
    using var watcher = UsnWatcher.TryStart(new VolumeJournal(vol.Root, j.JournalId, j.NextUsn))!;
    watcher.PathChanged += p => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] arşiv değişti: {p} (var: {File.Exists(p)})");
    watcher.DirectoryChanged += p => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] klasör değişti: {p} (var: {Directory.Exists(p)})");
    watcher.NameRemoved += n => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] üst klasörü silinmiş: {n}");
    watcher.ResetRequired += r => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] SIFIRLAMA: {r}");
    watcher.Start();
    Console.WriteLine($"{vol.Root} izleniyor ({seconds} sn)…");
    Thread.Sleep(seconds * 1000);
}

void MftDebug(string drive, string[] names)
{
    using var vol = NtfsVolume.TryOpen(drive) ?? throw new InvalidOperationException("Birim açılamadı");
    foreach (var (label, run) in new (string, Action<MftRecordHandler>)[] { ("HAM", h => vol.EnumerateRawMft(h)), ("USN", h => vol.EnumerateUsnDataOnly(h)) })
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
        Console.WriteLine($"{label}: {total:N0} ad, {dirs:N0} klasör");
    }
}

// Arşivleri tek iş parçacığında sırayla okur; her biri için süre ve işlemin G/Ç sayaçlarındaki artışı
// (SharpCompress'in açtığı ek dosyalar dahil) ölçülür. Disk önbelleğinden bağımsız ölçüt: okuma çağrısı ve bayt.
void Profile(string prefix, string? ext, int max)
{
    var targets = IndexFile.TryLoad(indexPath)
        .Where(a => a.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Where(a => ext == null || a.Path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        .Take(max).ToList();
    Console.WriteLine($"{targets.Count:N0} arşiv profilleniyor…");
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
    Console.WriteLine($"\rToplam {total.Elapsed.TotalSeconds:0.0} sn                ");

    Console.WriteLine("\nUzantıya göre:");
    foreach (var g in rows.GroupBy(r => Path.GetExtension(r.Path).ToLowerInvariant()).OrderByDescending(g => g.Sum(r => r.Ms)))
        Console.WriteLine($"  {g.Key,-7} {g.Count(),5} arşiv  {g.Sum(r => r.Ms) / 1000,8:0.0} sn  {g.Sum(r => (double)r.Ops),12:N0} okuma  {g.Sum(r => (double)r.Bytes) / (1 << 20),10:N1} MB okundu  {g.Sum(r => r.Entries),10:N0} girdi");

    Console.WriteLine("\nEn yavaş 20:");
    foreach (var r in rows.OrderByDescending(r => r.Ms).Take(20))
        Console.WriteLine($"  {r.Ms / 1000,7:0.00} sn {r.Ops,9:N0} okuma {r.Bytes / (1024.0 * 1024),9:N1} MB  {r.Entries,7:N0} girdi  {r.Size / (1024.0 * 1024 * 1024),6:0.00} GB  {r.Path}{(r.Err != null ? "  ! " + r.Err : "")}");

    Console.WriteLine("\nEn çok okuma çağrısı yapan 10:");
    foreach (var r in rows.OrderByDescending(r => r.Ops).Take(10))
        Console.WriteLine($"  {r.Ops,9:N0} okuma {r.Bytes / (1024.0 * 1024),9:N1} MB  {r.Ms / 1000,7:0.00} sn  {r.Entries,7:N0} girdi  {r.Path}");
}

// Quick Open listeleyicisini SharpCompress ile girdi girdi karşılaştırır.
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
        catch (Exception ex) { Console.WriteLine($"  SharpCompress okuyamadı ({ex.GetType().Name}), hızlı yol {fast.Length:N0} girdi: {path}"); diff++; continue; }

        // SharpCompress klasörleri '/' ile bitirmeyebilir; karşılaştırmada yol sonu '/' ve tarih saniye hassasiyetinde.
        static string Key(ArchiveEntry e) => $"{e.Path.TrimEnd('/')}|{e.IsDirectory}|{(e.IsDirectory ? 0 : e.Size)}|{e.MtimeUtcTicks / TimeSpan.TicksPerSecond}";
        var a = fast.Select(Key).Order(StringComparer.Ordinal).ToList();
        var b = slow.Select(Key).Order(StringComparer.Ordinal).ToList();
        if (a.SequenceEqual(b)) { same++; continue; }
        diff++;
        Console.WriteLine($"  FARK: {path}  hızlı={fast.Length:N0} SharpCompress={slow.Length:N0}");
        foreach (var x in a.Except(b).Take(3)) Console.WriteLine($"     sadece hızlı: {x}");
        foreach (var x in b.Except(a).Take(3)) Console.WriteLine($"     sadece SC:    {x}");
    }
    Console.WriteLine($"{rars.Count:N0} RAR: {same:N0} birebir aynı, {diff:N0} farklı, {fallback:N0} hızlı yol uygun değil (RAR4/QO yok → SharpCompress)");
    Console.WriteLine($"Hızlı yol: toplam {fastMs / 1000:0.0} sn, {reads:N0} disk okuması, {hits:N0} başlık QO önbelleğinden");
}

SearchIndex LoadIndex()
{
    var sw = Stopwatch.StartNew();
    var archives = IndexFile.TryLoad(indexPath);
    long loadMs = sw.ElapsedMilliseconds;
    sw.Restart();
    var index = SearchIndex.Build(archives);
    Console.WriteLine($"İndeks: {archives.Count:N0} arşiv, {index.EntryCount:N0} girdi (yükleme {loadMs} ms, hazırlama {sw.ElapsedMilliseconds} ms)");
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
    if (r.Hits.Length > max) Console.WriteLine($"  ... ve {r.Hits.Length - max:N0} sonuç daha");
    Console.WriteLine($"{r.Hits.Length:N0} sonuç, {r.Elapsed.TotalMilliseconds:0.00} ms");
}

void Interactive()
{
    Console.WriteLine(banner);
    var index = LoadIndex();
    if (index.EntryCount == 0)
    {
        Console.WriteLine("İndeks boş. Önce: ArsivAra.Cli index <klasör>");
        return;
    }
    while (true)
    {
        Console.Write("\nara> ");
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


