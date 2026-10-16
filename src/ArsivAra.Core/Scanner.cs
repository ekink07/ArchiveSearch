using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;

namespace ArsivAra.Core;

/// <summary>Size &lt; 0 means size/date have not been read yet (MFT path; filled in parallel during the read phase).</summary>
public readonly record struct FoundArchive(string Path, long Size, long MtimeUtcTicks);

public readonly record struct ScanProgress(int Found, int Processed, int Reused, int Failed, string? Current);

/// <summary>A volume scanned via the MFT and the USN to start watching from (taken before the scan starts, so no change in between is missed).</summary>
public sealed record VolumeJournal(string Root, ulong JournalId, long StartUsn);

public sealed class ScanResult
{
    public required List<ArchiveRecord> Archives { get; init; }
    public required List<VolumeJournal> Journals { get; init; }
    /// <summary>Roots scanned by walking folders because the MFT could not be used.</summary>
    public required List<string> WalkedRoots { get; init; }
    public TimeSpan DiscoveryTime { get; init; }
    /// <summary>Archives that took longer than 2 seconds to read (for diagnostics).</summary>
    public required List<(string Path, double Seconds)> SlowArchives { get; init; }
    public TimeSpan TotalTime { get; init; }
}

/// <summary>Finds archives on disk and re-reads only the ones that changed.</summary>
public static class Scanner
{
    // Cloud (OneDrive etc.) placeholder files: reading them triggers a download, so we skip them.
    const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    const FileAttributes CloudOnly = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    static readonly string[] AlwaysExcludedDirs = ["$Recycle.Bin", "System Volume Information"];

    /// <summary>Number of archives read at once on a rotating disk (HDD). More makes the head jump between files.</summary>
    public static int HddReaders { get; set; } = 1;

    /// <summary>Number of archives read at once on an SSD.</summary>
    public static int SsdReaders { get; set; } = Math.Clamp(Environment.ProcessorCount, 2, 8);

    // ---- Discovery by walking folders (when there is no MFT) ----

    public static IEnumerable<FoundArchive> EnumerateArchives(string root, IReadOnlyCollection<string> excludes)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MaxRecursionDepth = 64, // safeguard against symbolic link loops
        };

        var excludeList = excludes.Select(e => e.TrimEnd('\\')).Where(e => e.Length > 0).ToArray();
        using var enumerator = new TolerantEnumerator(root, options, excludeList);
        while (enumerator.MoveNext()) yield return enumerator.Current;
    }

    /// <summary>
    /// IgnoreInaccessible only swallows "access denied"; so that locked (sharing violation) or just-deleted
    /// folders do not bring down the whole scan, any error skips that folder and continues.
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

        /// <summary>Is it a junction/symlink? (Cloud folders such as OneDrive are reparse points too, but have no target.)</summary>
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
            // A folder (with its subfolders) or a single archive file removed from the list.
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

    // ---- Discovery via the MFT ----

    /// <summary>Can this root be scanned via the MFT (NTFS, administrator, not a reparse point)?</summary>
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
    /// Reads the volume's MFT and finds the archives under the given roots.
    /// The folder hierarchy is built from an FRN → (parent FRN, name) table; full paths are cached.
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
            if ((++count & 0x3FFFF) == 0) phase?.Invoke($"Reading MFT {vol.Root}  {count:N0} records");
        }, ct);
        phase?.Invoke($"MFT read {vol.Root} ({vol.LastEnumerationMethod})  {count:N0} records, {files.Count:N0} archive candidates");

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
                // At the root (record 5) the parent ID points to itself; anything missing from the table is attached to the root.
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
    /// Scans the roots: on NTFS volumes (when running as administrator) the MFT is read, elsewhere folders are walked.
    /// Archives whose size and date match the previous index are not read again.
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
            // If a root is a reparse point (junction/symlink), the MFT path points to another volume: fall back to walking.
            bool anyReparse = groupRoots.Any(r => r.Length > 3 && (File.GetAttributes(r) & FileAttributes.ReparsePoint) != 0);
            NtfsVolume? vol = useMft && !anyReparse ? NtfsVolume.TryOpen(group.Key) : null;
            if (vol != null)
            {
                using (vol)
                {
                    try
                    {
                        // Take the journal position BEFORE reading the MFT: changes made during the read are tracked too.
                        var j = vol.QueryJournal();
                        discovered.Add(FindArchivesMft(vol, groupRoots, excludes, phase, ct));
                        if (j is { } info) journals.Add(new VolumeJournal(vol.Root, info.JournalId, info.NextUsn));
                        else walked.AddRange(groupRoots); // no journal: live watching uses FileSystemWatcher
                        continue;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* MFT could not be read: fall back to walking */ }
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
    /// MFT-only discovery (for the helper process running with administrator rights): archive paths and the roots that could not be read via the MFT.
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
    /// Reads the discovered archives. Each physical disk is read from its own queue: few readers on an HDD (so the head
    /// does not jump back and forth between files), many on an SSD; disks do not wait for each other. Discovery (walking) and reading also run concurrently.
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
                onRead?.Invoke(rec); // lets the UI show results before the scan finishes
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
