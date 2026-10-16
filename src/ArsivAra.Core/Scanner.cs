using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;

namespace ArsivAra.Core;

/// <summary>Size &lt; 0 ise boyut/tarih henüz okunmadı demektir (MFT yolu; okuma aşamasında paralel doldurulur).</summary>
public readonly record struct FoundArchive(string Path, long Size, long MtimeUtcTicks);

public readonly record struct ScanProgress(int Found, int Processed, int Reused, int Failed, string? Current);

/// <summary>MFT ile taranan bir birim ve izlemeye başlanacak USN noktası (tarama başlamadan önce alınır, böylece arada değişiklik kaçmaz).</summary>
public sealed record VolumeJournal(string Root, ulong JournalId, long StartUsn);

public sealed class ScanResult
{
    public required List<ArchiveRecord> Archives { get; init; }
    public required List<VolumeJournal> Journals { get; init; }
    /// <summary>MFT kullanılamadığı için klasör gezilerek taranan kökler.</summary>
    public required List<string> WalkedRoots { get; init; }
    public TimeSpan DiscoveryTime { get; init; }
    /// <summary>Okunması 2 saniyeden uzun süren arşivler (teşhis için).</summary>
    public required List<(string Path, double Seconds)> SlowArchives { get; init; }
    public TimeSpan TotalTime { get; init; }
}

/// <summary>Diskte arşivleri bulur ve sadece değişmiş olanları yeniden okur.</summary>
public static class Scanner
{
    // Bulut (OneDrive vb.) yer tutucu dosyaları: okumak indirmeyi tetikler, atlıyoruz.
    const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    const FileAttributes CloudOnly = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    static readonly string[] AlwaysExcludedDirs = ["$Recycle.Bin", "System Volume Information"];

    /// <summary>Döner diskte (HDD) aynı anda okunan arşiv sayısı. Fazlası kafayı dosyalar arasında gezdirir.</summary>
    public static int HddReaders { get; set; } = 1;

    /// <summary>SSD'de aynı anda okunan arşiv sayısı.</summary>
    public static int SsdReaders { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 8);

    // ---- Klasör gezerek bulma (MFT yoksa) ----

    public static IEnumerable<FoundArchive> EnumerateArchives(string root, IReadOnlyCollection<string> excludes)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MaxRecursionDepth = 64, // sembolik bağlantı döngülerine karşı emniyet
        };

        var excludeList = excludes.Select(e => e.TrimEnd('\\')).Where(e => e.Length > 0).ToArray();
        using var enumerator = new TolerantEnumerator(root, options, excludeList);
        while (enumerator.MoveNext()) yield return enumerator.Current;
    }

    /// <summary>
    /// IgnoreInaccessible sadece "erişim engellendi"yi yutar; kilitli (paylaşım ihlali) ya da o an silinmiş
    /// klasörler tüm taramayı düşürmesin diye her hatada o klasörü atlayıp devam eder.
    /// </summary>
    sealed class TolerantEnumerator(string root, EnumerationOptions options, string[] excludes)
        : FileSystemEnumerator<FoundArchive>(root, options)
    {
        protected override bool ContinueOnError(int error) => true;

        protected override bool ShouldIncludeEntry(ref FileSystemEntry e) =>
            !e.IsDirectory && (e.Attributes & CloudOnly) == 0 && ArchiveReader.IsArchiveName(e.FileName);

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry e)
        {
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && IsLink(e.ToFullPath())) return false;
            foreach (var d in AlwaysExcludedDirs)
                if (e.FileName.Equals(d, StringComparison.OrdinalIgnoreCase)) return false;
            if (excludes.Length == 0) return true;
            string full = e.ToFullPath();
            foreach (var ex in excludes)
                if (full.Equals(ex, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>Junction/symlink mi? (OneDrive gibi bulut klasörleri de reparse point'tir ama hedefleri yoktur.)</summary>
        static bool IsLink(string path)
        {
            try { return new DirectoryInfo(path).LinkTarget != null; }
            catch (Exception) { return true; }
        }

        protected override FoundArchive TransformEntry(ref FileSystemEntry e) =>
            new(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcTicks);
    }

    public static bool IsExcluded(string path, IReadOnlyCollection<string> excludes)
    {
        foreach (var d in AlwaysExcludedDirs)
            if (path.Contains("\\" + d + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var ex in excludes)
        {
            var e = ex.TrimEnd('\\');
            if (e.Length == 0) continue;
            // Klasör (alt klasörleriyle) ya da listeden kaldırılmış tek bir arşiv dosyası.
            if (path.StartsWith(e + "\\", StringComparison.OrdinalIgnoreCase) || path.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static bool IsUnderAnyRoot(string path, List<string> roots)
    {
        foreach (var r in roots)
        {
            string root = r.EndsWith('\\') ? r : r + "\\";
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ---- MFT ile bulma ----

    /// <summary>Bu kök MFT ile taranabilir mi (NTFS, yönetici, bağlantı noktası değil)?</summary>
    public static bool CanUseMft(string root)
    {
        try
        {
            string full = Path.GetFullPath(root);
            if (full.Length > 3 && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return false;
            using var vol = NtfsVolume.TryOpen(full);
            return vol != null;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Birimin MFT'sini okuyup verilen köklerin altındaki arşivleri bulur.
    /// Klasör hiyerarşisi FRN → (üst FRN, ad) tablosundan kurulur; tam yollar önbelleklenir.
    /// </summary>
    public static List<FoundArchive> FindArchivesMft(
        NtfsVolume vol, List<string> roots, IReadOnlyCollection<string> excludes,
        Action<string>? phase = null, CancellationToken ct = default)
    {
        var dirs = new Dictionary<ulong, (ulong Parent, string Name)>(1 << 16);
        var files = new List<(ulong Parent, string Name)>();
        long count = 0;

        vol.EnumerateMft((ulong frn, ulong parent, uint attrs, ReadOnlySpan<char> name) =>
        {
            if ((attrs & (uint)FileAttributes.Directory) != 0) dirs[frn] = (parent, name.ToString());
            else if ((attrs & (uint)CloudOnly) == 0 && ArchiveReader.IsArchiveName(name)) files.Add((parent, name.ToString()));
            if ((++count & 0x3FFFF) == 0) phase?.Invoke($"MFT okunuyor {vol.Root}  {count:N0} kayıt");
        }, ct);
        phase?.Invoke($"MFT okundu {vol.Root} ({vol.LastEnumerationMethod})  {count:N0} kayıt, {files.Count:N0} arşiv adayı");

        var cache = new Dictionary<ulong, string>();
        var chain = new List<ulong>();

        string DirPath(ulong frn)
        {
            if (cache.TryGetValue(frn, out var cached)) return cached;
            chain.Clear();
            string basePath = vol.Root;
            ulong cur = frn;
            for (int depth = 0; depth < 512; depth++)
            {
                if (cache.TryGetValue(cur, out var c)) { basePath = c; break; }
                // Kökte (kayıt 5) üst kimlik kendisini gösterir; tabloda olmayan da köke bağlanır.
                if (!dirs.TryGetValue(cur, out var d) || d.Parent == cur) break;
                chain.Add(cur);
                cur = d.Parent;
            }
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                basePath = basePath + dirs[chain[i]].Name + "\\";
                cache[chain[i]] = basePath;
            }
            return basePath;
        }

        var result = new List<FoundArchive>(files.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (parent, name) in files)
        {
            string path = DirPath(parent) + name;
            if (!IsUnderAnyRoot(path, roots) || IsExcluded(path, excludes) || !seen.Add(path)) continue;
            result.Add(new FoundArchive(path, -1, 0));
        }
        return result;
    }

    /// <summary>
    /// Kökleri tarar: NTFS birimlerde (yönetici olarak çalışıyorsa) MFT okunur, diğerlerinde klasörler gezilir.
    /// Önceki indekste boyutu ve tarihi aynı olan arşivler tekrar okunmaz.
    /// </summary>
    public static ScanResult Scan(
        IEnumerable<string> roots,
        IReadOnlyCollection<string> excludes,
        IReadOnlyDictionary<string, ArchiveRecord> previous,
        Action<ScanProgress>? progress = null,
        CancellationToken ct = default,
        bool useMft = true,
        Action<ArchiveRecord>? onRead = null,
        ReadOptions? readOptions = null,
        Func<string, string?>? passwordFor = null)
    {
        var total = Stopwatch.StartNew();
        var journals = new List<VolumeJournal>();
        var walked = new List<string>();
        var discovered = new List<IEnumerable<FoundArchive>>();
        var phase = (string s) => progress?.Invoke(new ScanProgress(0, 0, 0, 0, s));

        var byVolume = roots.Where(Directory.Exists)
            .Select(r => Path.GetFullPath(r))
            .GroupBy(r => Path.GetPathRoot(r)!.ToUpperInvariant());

        foreach (var group in byVolume)
        {
            var groupRoots = group.ToList();
            // Kök bir bağlantı noktasıysa (junction/symlink) MFT yolu başka birimi gösterir: gezmeye düş.
            bool anyReparse = groupRoots.Any(r => r.Length > 3 && (File.GetAttributes(r) & FileAttributes.ReparsePoint) != 0);
            NtfsVolume? vol = useMft && !anyReparse ? NtfsVolume.TryOpen(group.Key) : null;
            if (vol != null)
            {
                using (vol)
                {
                    try
                    {
                        // Journal noktasını MFT okumasından ÖNCE al: okuma sırasında olan değişiklikler de izlenir.
                        var j = vol.QueryJournal();
                        discovered.Add(FindArchivesMft(vol, groupRoots, excludes, phase, ct));
                        if (j is { } info) journals.Add(new VolumeJournal(vol.Root, info.JournalId, info.NextUsn));
                        else walked.AddRange(groupRoots); // journal yoksa canlı izleme FileSystemWatcher ile
                        continue;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* MFT okunamadı: gezmeye düş */ }
                }
            }
            walked.AddRange(groupRoots);
            discovered.AddRange(groupRoots.Select(r => EnumerateArchives(r, excludes)));
        }
        var discoveryTime = total.Elapsed;

        var (archives, slow) = ReadAll(discovered.SelectMany(x => x), previous, progress, ct, onRead, readOptions, passwordFor);
        return new ScanResult
        {
            Archives = archives,
            Journals = journals,
            WalkedRoots = walked,
            DiscoveryTime = discoveryTime,
            SlowArchives = slow,
            TotalTime = total.Elapsed,
        };
    }

    /// <summary>
    /// Sadece MFT ile bulma (yönetici izniyle çalışan yardımcı süreç için): arşiv yolları ve MFT ile okunamayan kökler.
    /// </summary>
    public static (List<string> Archives, List<string> NotScanned) DiscoverMft(IEnumerable<string> roots,
        IReadOnlyCollection<string> excludes, CancellationToken ct = default)
    {
        var found = new List<string>();
        var notScanned = new List<string>();
        var byVolume = roots.Where(Directory.Exists)
            .Select(r => Path.GetFullPath(r))
            .GroupBy(r => Path.GetPathRoot(r)!.ToUpperInvariant());
        foreach (var group in byVolume)
        {
            var groupRoots = group.ToList();
            bool anyReparse = groupRoots.Any(r => r.Length > 3 && (File.GetAttributes(r) & FileAttributes.ReparsePoint) != 0);
            using var vol = anyReparse ? null : NtfsVolume.TryOpen(group.Key);
            if (vol == null)
            {
                notScanned.AddRange(groupRoots);
                continue;
            }
            try { found.AddRange(FindArchivesMft(vol, groupRoots, excludes, null, ct).Select(f => f.Path)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { notScanned.AddRange(groupRoots); }
        }
        return (found, notScanned);
    }

    /// <summary>
    /// Bulunan arşivleri okur. Her fiziksel disk kendi kuyruğundan okunur: HDD'de az okuyucu (kafa dosyalar arasında
    /// gidip gelmesin), SSD'de çok okuyucu; diskler birbirini beklemez. Bulma (gezme) ile okuma da eşzamanlı ilerler.
    /// </summary>
    public static (List<ArchiveRecord> Archives, List<(string Path, double Seconds)> Slow) ReadAll(
        IEnumerable<FoundArchive> discovered,
        IReadOnlyDictionary<string, ArchiveRecord> previous,
        Action<ScanProgress>? progress = null,
        CancellationToken ct = default,
        Action<ArchiveRecord>? onRead = null,
        ReadOptions? readOptions = null,
        Func<string, string?>? passwordFor = null)
    {
        var results = new ConcurrentBag<ArchiveRecord>();
        var slow = new ConcurrentBag<(string, double)>();
        int found = 0, processed = 0, reused = 0, failed = 0;
        long lastReport = 0;
        var baseOptions = readOptions ?? ReadOptions.Default;

        var queues = new Dictionary<int, BlockingCollection<FoundArchive>>();
        var workers = new List<Task>();
        Exception? workerError = null;

        BlockingCollection<FoundArchive> QueueFor(string path)
        {
            var disk = DiskInfo.ForPath(path);
            if (queues.TryGetValue(disk.DiskNumber, out var q)) return q;
            q = new BlockingCollection<FoundArchive>(boundedCapacity: 10_000);
            queues[disk.DiskNumber] = q;
            int readers = disk.IsRotational ? HddReaders : SsdReaders;
            for (int i = 0; i < readers; i++)
                workers.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {
                        foreach (var f in q.GetConsumingEnumerable(ct)) Process(f);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { workerError ??= ex; }
                }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            return q;
        }

        try
        {
            foreach (var f in discovered)
            {
                ct.ThrowIfCancellationRequested();
                QueueFor(f.Path).Add(f, ct);
            }
        }
        finally
        {
            foreach (var q in queues.Values) q.CompleteAdding();
            Task.WaitAll([.. workers]);
        }
        ct.ThrowIfCancellationRequested();
        if (workerError != null) throw new AggregateException(workerError);

        void Process(FoundArchive f)
        {
            if (f.Size < 0)
            {
                var fi = new FileInfo(f.Path);
                if (!fi.Exists || (fi.Attributes & CloudOnly) != 0) return;
                f = new FoundArchive(f.Path, fi.Length, fi.LastWriteTimeUtc.Ticks);
            }
            Interlocked.Increment(ref found);
            ArchiveRecord rec;
            if (previous.TryGetValue(f.Path, out var old) && old.IsSameFile(f.Size, f.MtimeUtcTicks) && !old.NeedsPassword)
            {
                rec = old;
                Interlocked.Increment(ref reused);
            }
            else
            {
                long t0 = Stopwatch.GetTimestamp();
                var options = passwordFor?.Invoke(f.Path) is { } pw ? baseOptions with { Password = pw } : baseOptions;
                rec = ArchiveReader.Read(f.Path, f.Size, f.MtimeUtcTicks, options);
                double secs = Stopwatch.GetElapsedTime(t0).TotalSeconds;
                if (secs > 2) slow.Add((f.Path, secs));
                if (rec.Error != null) Interlocked.Increment(ref failed);
                onRead?.Invoke(rec); // arayüz tarama bitmeden sonuç gösterebilsin
            }
            results.Add(rec);
            int done = Interlocked.Increment(ref processed);

            long now = Environment.TickCount64;
            if (progress != null && now - Interlocked.Read(ref lastReport) > 100)
            {
                Interlocked.Exchange(ref lastReport, now);
                progress(new ScanProgress(found, done, reused, failed, f.Path));
            }
        }

        progress?.Invoke(new ScanProgress(found, processed, reused, failed, null));
        var list = results.ToList();
        list.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return (list, [.. slow.OrderByDescending(s => s.Item2)]);
    }
}
