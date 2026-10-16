namespace ArsivAra.Core;

/// <summary>
/// Disk taramasından sonra arşivlerdeki değişiklikleri canlı izler: MFT ile taranan birimlerde USN Journal
/// (taşma yok, yönetici gerekir), diğer köklerde FileSystemWatcher. Olaylar ham yol olarak bildirilir;
/// arka arkaya gelen olayları birleştirmek (debounce) çağıranın işidir. Olaylar arka plan iş parçacığından gelir.
/// </summary>
public sealed class LiveWatcher(IReadOnlyCollection<string> excludes) : IDisposable
{
    readonly List<UsnWatcher> _usn = [];
    readonly List<FileSystemWatcher> _fsw = [];

    /// <summary>Arşiv dosyası eklendi, değişti, silindi ya da adı değişti (eski ve yeni yol ayrı ayrı bildirilir).</summary>
    public event Action<string>? ArchiveChanged;

    /// <summary>Klasör eklendi, silindi, taşındı ya da adı değişti: altındaki arşivler yeniden denetlenmeli.</summary>
    public event Action<string>? DirectoryChanged;

    /// <summary>İzleme koptu (journal sıfırlandı, FileSystemWatcher tamponu taştı): tam yeniden tarama gerekir.</summary>
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
            w.NameRemoved += _ => DirectoryChanged?.Invoke(j.Root); // üst klasörü de silinmiş: birimdeki kayıtlar denetlensin
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
        catch (Exception ex) { Log.Warn($"{root} canlı izlenemiyor", ex); }
    }

    // Changed olayı diskteki her yazmada gelir: sadece arşiv adlarına bakılır (ucuz).
    void OnArchive(string path)
    {
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path)) && !Scanner.IsExcluded(path, excludes)) ArchiveChanged?.Invoke(path);
    }

    void OnCreatedOrRenamedTo(string path)
    {
        if (Scanner.IsExcluded(path, excludes)) return;
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path))) ArchiveChanged?.Invoke(path);
        else if (Directory.Exists(path)) DirectoryChanged?.Invoke(path); // içinde arşiv olan bir klasör taşınmış olabilir
    }

    void OnRemoved(string path)
    {
        if (Scanner.IsExcluded(path, excludes)) return;
        if (ArchiveReader.IsArchiveName(Path.GetFileName(path))) ArchiveChanged?.Invoke(path);
        // Silinen şeyin klasör olup olmadığı artık sorulamaz; diskteki her geçici dosya silinmesi olay üretmesin diye
        // sadece uzantısız adlar klasör sayılır.
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
