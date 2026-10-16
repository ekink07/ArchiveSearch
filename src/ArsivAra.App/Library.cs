using System.Diagnostics;
using System.IO;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// The archives in the program: those the user added and (if enabled) those found by the disk scan.
/// The list and its contents are stored in index.bin; archives that changed are re-read at startup. If disk scanning
/// is enabled, archives on the computer are found, then changes are watched live (USN Journal or FileSystemWatcher).
/// </summary>
public sealed class Library : IDisposable
{
    readonly object _lock = new();
    readonly object _publishLock = new();
    readonly object _saveLock = new();
    readonly object _pendingLock = new();
    readonly Dictionary<string, ArchiveRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _pendingDirs = new(StringComparer.OrdinalIgnoreCase);
    readonly Settings _settings;
    readonly Timer _debounce;
    LiveWatcher? _watcher;
    CancellationTokenSource? _scanCts;
    volatile bool _rescanRequested;

    public Library(Settings settings)
    {
        _settings = settings;
        _debounce = new Timer(_ => ProcessPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public SearchIndex Current { get; private set; } = SearchIndex.Empty;

    /// <summary>The archive list or its contents changed (may be raised from a background thread).</summary>
    public event Action? Changed;

    /// <summary>Short message for the status bar (live change, scan result); may be raised from the background.</summary>
    public event Action<string>? Notice;

    /// <summary>Live watching broke: a full rescan is needed (the UI starts it with its own progress bar).</summary>
    public event Action? RescanNeeded;

    public bool IsScanning => _scanCts != null;
    public bool IsWatching => _watcher != null;

    /// <summary>Add progress. Total &lt; 0: the number of archives is not known yet.</summary>
    public readonly record struct AddProgress(int Done, int Total, string Message);

    public sealed record AddResult(int Added, int Updated, int Failed, int Skipped);

    ReadOptions OptionsFor(string path) => _settings.ReadOptions(PasswordStore.Get(path));

    public ArchiveRecord? Find(string path)
    {
        lock (_lock) return _records.GetValueOrDefault(path);
    }

    public async Task<(IndexFile.LoadStatus Status, string? Backup)> LoadAsync(IProgress<AddProgress>? progress = null)
    {
        var (loaded, status, backup) = await Task.Run(() => IndexFile.LoadForUse(AppPaths.IndexFile));
        lock (_lock)
            foreach (var r in loaded) _records[r.Path] = r;
        Publish();
        if (status == IndexFile.LoadStatus.Migrated) await Task.Run(Save);

        // Refresh manually added archives that changed (overwritten backups) and encrypted ones whose password is saved;
        // the disk scan refreshes the ones it found.
        var changed = await Task.Run(() => loaded.Where(r => r.Manual && NeedsRefresh(r)).Select(r => r.Path).ToList());
        if (changed.Count > 0) await AddAsync(changed, progress, manual: null);
        return (status, backup);
    }

    static bool NeedsRefresh(ArchiveRecord r)
    {
        var fi = new FileInfo(r.Path);
        return fi.Exists && (!r.IsSameFile(fi.Length, fi.LastWriteTimeUtc.Ticks) || (r.NeedsPassword && PasswordStore.Get(r.Path) != null));
    }

    /// <summary>
    /// Adds files and folders. Archives inside the folders are found; files that are not archives are skipped.
    /// <paramref name="manual"/>: true = added manually, false = disk scan, null = keep the previous state (re-read).
    /// </summary>
    public async Task<AddResult> AddAsync(IEnumerable<string> paths, IProgress<AddProgress>? progress = null, bool? manual = true,
        CancellationToken ct = default)
    {
        var input = paths.ToList();
        return await Task.Run(() =>
        {
            int skipped = 0;
            var files = new List<string>();
            foreach (var p in input)
            {
                if (File.Exists(p))
                {
                    if (ArchiveReader.IsArchiveName(Path.GetFileName(p))) files.Add(Path.GetFullPath(p));
                    else skipped++;
                }
                else if (Directory.Exists(p))
                {
                    progress?.Report(new AddProgress(0, -1, Loc.F("StatusSearchingFolder", p)));
                    files.AddRange(Scanner.EnumerateArchives(p, []).Select(f => f.Path));
                }
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (manual == true) UnExclude(files); // an archive removed from the list and added again should come back in scans too

            int added = 0, updated = 0, failed = 0, done = 0;
            long lastPublish = Environment.TickCount64;

            // On an external HDD, reading one file at a time is much faster (the head doesn't seek back and forth between files).
            bool anyRotational = files.Any(f => DiskInfo.ForPath(f).IsRotational);
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = anyRotational ? 1 : 4 };

            Parallel.ForEach(files, options, path =>
            {
                progress?.Report(new AddProgress(Volatile.Read(ref done), files.Count, Loc.F("StatusReading", Path.GetFileName(path))));
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    Interlocked.Increment(ref done);
                    return;
                }

                var rec = ArchiveReader.Read(path, fi.Length, fi.LastWriteTimeUtc.Ticks, OptionsFor(path));
                bool existed;
                lock (_lock)
                {
                    existed = _records.TryGetValue(path, out var old);
                    rec.Manual = manual switch
                    {
                        true => true,
                        false => old?.Manual == true, // a manually added archive doesn't become "found" because of a scan
                        null => old?.Manual ?? true,
                    };
                    _records[path] = rec;
                }
                if (rec.Error != null) Interlocked.Increment(ref failed);
                else if (existed) Interlocked.Increment(ref updated);
                else Interlocked.Increment(ref added);
                int completed = Interlocked.Increment(ref done);
                progress?.Report(new AddProgress(completed, files.Count, Loc.F("StatusRead", Path.GetFileName(path))));

                // For large additions, make results searchable before reading finishes.
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastPublish) > 1500)
                {
                    Interlocked.Exchange(ref lastPublish, now);
                    Publish();
                }
            });

            Publish();
            Save();
            return new AddResult(added, updated, failed, skipped);
        }, ct);
    }

    /// <summary>Re-reads all archives (e.g. when the nested archive setting changes).</summary>
    public Task<AddResult> ReloadAllAsync(IProgress<AddProgress>? progress = null)
    {
        List<string> all;
        lock (_lock) all = [.. _records.Keys];
        return AddAsync(all, progress, manual: null);
    }

    /// <summary>
    /// Removes archives from the list. Those found by the disk scan are added to the exclusions so they don't come back in a scan.
    /// </summary>
    public void Remove(IEnumerable<string> archivePaths)
    {
        bool excluded = false;
        lock (_lock)
        {
            foreach (var p in archivePaths)
            {
                if (_records.Remove(p, out var rec) && !rec.Manual
                    && !_settings.Excludes.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    _settings.Excludes.Add(p);
                    excluded = true;
                }
            }
        }
        if (excluded) TrySaveSettings();
        Publish();
        Task.Run(Save);
    }

    /// <summary>Disk scanning was turned off: archives found by the scan leave the list and watching stops.</summary>
    public void RemoveScanned()
    {
        StopWatching();
        lock (_lock)
            foreach (var p in _records.Values.Where(r => !r.Manual).Select(r => r.Path).ToList()) _records.Remove(p);
        Publish();
        Task.Run(Save);
    }

    void UnExclude(List<string> files)
    {
        int removed = _settings.Excludes.RemoveAll(e => files.Contains(e, StringComparer.OrdinalIgnoreCase));
        if (removed > 0) TrySaveSettings();
    }

    void TrySaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log.Warn("Could not save settings", ex); }
    }

    // ---- Disk scan and live watching ----

    /// <summary>Finds the archives on the computer (the roots in the settings; all fixed disks if empty), then watches for changes.</summary>
    public async Task ScanAsync(IProgress<AddProgress> progress)
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        var ct = cts.Token;
        var sw = Stopwatch.StartNew();
        try
        {
            var roots = _settings.EffectiveRoots.ToList();
            var excludes = _settings.Excludes.ToList();
            Dictionary<string, ArchiveRecord> previous;
            lock (_lock) previous = new Dictionary<string, ArchiveRecord>(_records, StringComparer.OrdinalIgnoreCase);
            var readOptions = _settings.ReadOptions();
            long lastPublish = 0;

            void OnRead(ArchiveRecord rec)
            {
                lock (_lock)
                {
                    rec.Manual = _records.TryGetValue(rec.Path, out var old) && old.Manual;
                    _records[rec.Path] = rec;
                }
                long now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastPublish) > 2000)
                {
                    Interlocked.Exchange(ref lastPublish, now);
                    Publish();
                }
            }

            void OnProgress(ScanProgress p) => progress.Report(new AddProgress(p.Processed, -1,
                p.Processed == 0 && p.Current != null ? Loc.F("StatusScanning", p.Current) : Loc.F("StatusScanProgress", p.Found, p.Processed)));

            ScanResult? result = null;
            if (_settings.FastScan && !NtfsVolume.IsElevated)
            {
                progress.Report(new AddProgress(0, -1, Loc.T("StatusMft")));
                var mft = await MftHelper.RunElevatedAsync(roots, excludes, ct);
                if (mft == null) Notice?.Invoke(Loc.T("StatusElevationDeclined"));
                else
                {
                    var found = mft.Archives.Select(p => new FoundArchive(p, -1, 0))
                        .Concat(mft.NotScanned.SelectMany(r => Scanner.EnumerateArchives(r, excludes)));
                    var (archives, slow) = await Task.Run(() =>
                        Scanner.ReadAll(found, previous, OnProgress, ct, OnRead, readOptions, PasswordStore.Get), ct);
                    result = new ScanResult
                    {
                        Archives = archives, Journals = [], WalkedRoots = roots, SlowArchives = slow, TotalTime = sw.Elapsed,
                    };
                }
            }
            result ??= await Task.Run(() => Scanner.Scan(roots, excludes, previous, OnProgress, ct,
                useMft: NtfsVolume.IsElevated, onRead: OnRead, readOptions, PasswordStore.Get), ct);

            // Manually added ones stay; those found by the scan are refreshed; scanned ones no longer found are dropped.
            var foundPaths = new HashSet<string>(result.Archives.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                foreach (var stale in _records.Values.Where(r => !r.Manual && !foundPaths.Contains(r.Path)).Select(r => r.Path).ToList())
                    _records.Remove(stale);
                foreach (var a in result.Archives)
                {
                    a.Manual = _records.TryGetValue(a.Path, out var old) && old.Manual;
                    _records[a.Path] = a;
                }
            }
            Publish();
            Save();
            StartWatching(result, excludes);
            Log.Info($"Disk scan: {result.Archives.Count} archives, {sw.Elapsed.TotalSeconds:0.0} s");
            Notice?.Invoke(Loc.F("StatusScanDone", result.Archives.Count, Math.Round(sw.Elapsed.TotalSeconds)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error("Disk scan", ex);
            Notice?.Invoke(Loc.F("StatusScanFailed", ex.Message));
        }
        finally
        {
            if (_scanCts == cts) _scanCts = null;
            cts.Dispose();
        }
    }

    void StartWatching(ScanResult result, IReadOnlyCollection<string> excludes)
    {
        _watcher?.Dispose();
        var w = new LiveWatcher(excludes);
        w.ArchiveChanged += p => Enqueue(p, directory: false);
        w.DirectoryChanged += p => Enqueue(p, directory: true);
        w.RescanRequired += reason =>
        {
            Log.Warn("Live watching broke: " + reason);
            _rescanRequested = true;
            _debounce.Change(5000, Timeout.Infinite);
        };
        w.Start(result.Journals, result.WalkedRoots);
        _watcher = w;
    }

    public void StopWatching()
    {
        _scanCts?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
    }

    void Enqueue(string path, bool directory)
    {
        lock (_pendingLock) (directory ? _pendingDirs : _pendingFiles).Add(path);
        _debounce.Change(1500, Timeout.Infinite); // coalesce events that arrive in quick succession
    }

    /// <summary>Accumulated changes: changed archives are re-read, deleted scanned archives are dropped, moved folders are checked.</summary>
    void ProcessPending()
    {
        if (_rescanRequested && !IsScanning)
        {
            _rescanRequested = false;
            RescanNeeded?.Invoke();
            return;
        }

        string[] files, dirs;
        lock (_pendingLock)
        {
            files = [.. _pendingFiles];
            dirs = [.. _pendingDirs];
            _pendingFiles.Clear();
            _pendingDirs.Clear();
        }
        if (files.Length == 0 && dirs.Length == 0) return;

        try
        {
            var excludes = _settings.Excludes.ToList();
            var check = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            if (dirs.Length > 0)
            {
                lock (_lock)
                {
                    foreach (var d in dirs)
                    {
                        string prefix = d.TrimEnd('\\') + "\\";
                        foreach (var key in _records.Keys)
                            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) check.Add(key);
                    }
                }
                foreach (var d in dirs.Where(Directory.Exists))
                    foreach (var f in Scanner.EnumerateArchives(d, excludes)) check.Add(f.Path);
            }

            var changedNames = new List<string>();
            foreach (var path in check)
            {
                if (Scanner.IsExcluded(path, excludes)) continue;
                ArchiveRecord? old;
                lock (_lock) _records.TryGetValue(path, out old);
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    if (old is { Manual: false })
                    {
                        lock (_lock) _records.Remove(path);
                        changedNames.Add(Path.GetFileName(path));
                    }
                    continue;
                }
                if (old != null && old.IsSameFile(fi.Length, fi.LastWriteTimeUtc.Ticks)) continue;
                var rec = ArchiveReader.Read(path, fi.Length, fi.LastWriteTimeUtc.Ticks, OptionsFor(path));
                rec.Manual = old?.Manual ?? false;
                lock (_lock) _records[path] = rec;
                changedNames.Add(Path.GetFileName(path));
            }

            if (changedNames.Count > 0)
            {
                Publish();
                Save();
                Notice?.Invoke(Loc.F("StatusLiveChange", changedNames.Count == 1 ? changedNames[0] : changedNames.Count.ToString(Loc.I.Culture)));
            }
        }
        catch (Exception ex) { Log.Warn("Live update", ex); }
    }

    // ---- Publishing and saving ----

    void Publish()
    {
        // Intermediate publishes and the final one must not overlap: an old snapshot must not overwrite a newer one.
        lock (_publishLock)
        {
            List<ArchiveRecord> list;
            lock (_lock) list = [.. _records.Values];
            list.Sort((a, b) => string.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path), StringComparison.CurrentCultureIgnoreCase));
            Current = SearchIndex.Build(list);
        }
        Changed?.Invoke();
    }

    void Save()
    {
        try
        {
            lock (_saveLock) // two saves must not write to the same temp file at once
            {
                List<ArchiveRecord> list;
                lock (_lock) list = [.. _records.Values];
                IndexFile.Save(list, AppPaths.IndexFile);
            }
        }
        catch (Exception ex) { Log.Warn("Could not save the index", ex); }
    }

    public void Dispose()
    {
        StopWatching();
        _debounce.Dispose();
    }
}
