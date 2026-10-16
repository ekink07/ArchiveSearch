namespace ArsivAra.Core;

/// <summary>
/// Watches archives for changes live after a disk scan: USN Journal on volumes scanned via the MFT
/// (no overflow, requires administrator), FileSystemWatcher on other roots. Events are reported as raw paths;
/// merging back-to-back events (debouncing) is the caller's job. Events arrive on a background thread.
/// </summary>
public sealed class LiveWatcher(IReadOnlyCollection<string> excludes) : IDisposable
{
    readonly List<UsnWatcher> _usn = [];
    readonly List<FileSystemWatcher> _fsw = [];

    /// <summary>An archive file was added, changed, deleted or renamed (old and new paths are reported separately).</summary>
    public event Action<string>? ArchiveChanged;

    /// <summary>A folder was added, deleted, moved or renamed: the archives under it must be rechecked.</summary>
    public event Action<string>? DirectoryChanged;

    /// <summary>Watching broke off (journal reset, FileSystemWatcher buffer overflow): a full rescan is required.</summary>
    public event Action<string>? RescanRequired;

    public int WatchCount => _usn.Count + _fsw.Count;

    public void Start(IEnumerable<VolumeJournal> journals, IEnumerable<string> walkedRoots)
    {
        foreach (var j in journals)
        {
            var w = UsnWatcher.TryStart(j);
            if (w == null)
            {
                WatchFolder(j.Root);
                continue;
            }
            w.PathChanged += p => { if (!Scanner.IsExcluded(p, excludes)) ArchiveChanged?.Invoke(p); };
            w.DirectoryChanged += p => { if (!Scanner.IsExcluded(p, excludes)) DirectoryChanged?.Invoke(p); };
            w.NameRemoved += _ => DirectoryChanged?.Invoke(j.Root); // its parent folder was deleted too: recheck the volume's records
            w.ResetRequired += r => RescanRequired?.Invoke(r);
            w.Start();
            _usn.Add(w);
        }
        foreach (var root in walkedRoots) WatchFolder(root);
    }

    void WatchFolder(string root)
    {
        try
        {
            var w = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Created += (_, e) => OnCreatedOrRenamedTo(e.FullPath);
            w.Changed += (_, e) => OnArchive(e.FullPath);
            w.Deleted += (_, e) => OnRemoved(e.FullPath);
            w.Renamed += (_, e) =>
            {
                OnRemoved(e.OldFullPath);
                OnCreatedOrRenamedTo(e.FullPath);
            };
            w.Error += (_, e) => RescanRequired?.Invoke($"{root}: {e.GetException().Message}");
            w.EnableRaisingEvents = true;
            _fsw.Add(w);
        }
        catch (Exception ex) { Log.Warn($"Cannot watch {root} live", ex); }
    }

    // The Changed event fires on every write to disk: only archive names are checked (cheap).
    void OnArchive(string path)
    {
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path)) && !Scanner.IsExcluded(path, excludes)) ArchiveChanged?.Invoke(path);
    }

    void OnCreatedOrRenamedTo(string path)
    {
        if (Scanner.IsExcluded(path, excludes)) return;
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path))) ArchiveChanged?.Invoke(path);
        else if (Directory.Exists(path)) DirectoryChanged?.Invoke(path); // a folder containing archives may have been moved
    }

    void OnRemoved(string path)
    {
        if (Scanner.IsExcluded(path, excludes)) return;
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path))) ArchiveChanged?.Invoke(path);
        // Whether the deleted item was a folder can no longer be queried; so that every temp file deletion on disk does not
        // raise an event, only names without an extension are treated as folders.
        else if (!Path.HasExtension(path)) DirectoryChanged?.Invoke(path);
    }

    public void Dispose()
    {
        foreach (var w in _usn) w.Dispose();
        foreach (var w in _fsw) w.Dispose();
        _usn.Clear();
        _fsw.Clear();
    }
}
