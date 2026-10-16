using System.Collections.Concurrent;
using System.IO;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Kullanıcının programa eklediği arşivler. Sadece eklenen arşivler okunur (disk taranmaz).
/// Liste ve içerikleri index.bin'de saklanır; açılışta boyutu/tarihi değişen arşivler yeniden okunur.
/// </summary>
public sealed class Library
{
    readonly object _lock = new();
    readonly object _publishLock = new();
    readonly Dictionary<string, ArchiveRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public SearchIndex Current { get; private set; } = SearchIndex.Empty;

    /// <summary>Arşiv listesi ya da içerikleri değişti (arka plan iş parçacığından tetiklenebilir).</summary>
    public event Action? Changed;

    public bool Contains(string path)
    {
        lock (_lock) return _records.ContainsKey(path);
    }

    /// <summary>Ekleme ilerlemesi. Total &lt; 0: henüz kaç arşiv olduğu bilinmiyor (klasör taranıyor).</summary>
    public readonly record struct AddProgress(int Done, int Total, string Message);

    public async Task LoadAsync(IProgress<AddProgress>? progress = null)
    {
        var loaded = await Task.Run(() => IndexFile.TryLoad(AppPaths.IndexFile));
        lock (_lock)
            foreach (var r in loaded) _records[r.Path] = r;
        Publish();

        // Eklenmiş arşivlerden değişenleri (üzerine yazılmış yedekler vb.) tazele.
        var changed = await Task.Run(() => loaded.Where(r =>
        {
            var fi = new FileInfo(r.Path);
            return fi.Exists && !r.IsSameFile(fi.Length, fi.LastWriteTimeUtc.Ticks);
        }).Select(r => r.Path).ToList());
        if (changed.Count > 0) await AddAsync(changed, progress);
    }

    public sealed record AddResult(int Added, int Updated, int Failed, int Skipped);

    /// <summary>
    /// Dosya ve klasörleri ekler. Klasörlerin içindeki arşivler bulunur; arşiv olmayan dosyalar atlanır.
    /// </summary>
    public async Task<AddResult> AddAsync(IEnumerable<string> paths, IProgress<AddProgress>? progress = null, CancellationToken ct = default)
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
                    progress?.Report(new AddProgress(0, -1, $"Klasörde arşivler aranıyor: {p}"));
                    files.AddRange(Scanner.EnumerateArchives(p, []).Select(f => f.Path));
                }
            }
            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            int added = 0, updated = 0, failed = 0, done = 0;
            long lastPublish = Environment.TickCount64;

            // Harici HDD'de aynı anda tek dosya okumak çok daha hızlı (kafa dosyalar arasında gidip gelmez).
            bool anyRotational = files.Any(f => DiskInfo.ForPath(f).IsRotational);
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = anyRotational ? 1 : 4 };

            Parallel.ForEach(files, options, path =>
            {
                progress?.Report(new AddProgress(Volatile.Read(ref done), files.Count, $"Okunuyor: {Path.GetFileName(path)}"));
                var fi = new FileInfo(path);
                if (!fi.Exists) { Interlocked.Increment(ref done); return; }

                var rec = ArchiveReader.Read(path, fi.Length, fi.LastWriteTimeUtc.Ticks);
                bool existed;
                lock (_lock)
                {
                    existed = _records.ContainsKey(path);
                    _records[path] = rec;
                }
                if (rec.Error != null) Interlocked.Increment(ref failed);
                else if (existed) Interlocked.Increment(ref updated);
                else Interlocked.Increment(ref added);
                int completed = Interlocked.Increment(ref done);
                progress?.Report(new AddProgress(completed, files.Count, $"Okundu: {Path.GetFileName(path)}"));

                // Büyük eklemelerde sonuçlar okuma bitmeden aranabilsin.
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

    public void Remove(IEnumerable<string> archivePaths)
    {
        lock (_lock)
            foreach (var p in archivePaths) _records.Remove(p);
        Publish();
        Task.Run(Save);
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
        Publish();
        Task.Run(Save);
    }

    void Publish()
    {
        // Ara yayınlar ile son yayın üst üste binmesin: eski bir anlık görüntü yenisini ezmesin.
        lock (_publishLock)
        {
            List<ArchiveRecord> list;
            lock (_lock) list = [.. _records.Values];
            list.Sort((a, b) => string.Compare(Path.GetFileName(a.Path), Path.GetFileName(b.Path), StringComparison.CurrentCultureIgnoreCase));
            Current = SearchIndex.Build(list);
        }
        Changed?.Invoke();
    }

    readonly object _saveLock = new();

    void Save()
    {
        try
        {
            lock (_saveLock) // aynı anda iki kayıt aynı geçici dosyaya yazmasın
            {
                List<ArchiveRecord> list;
                lock (_lock) list = [.. _records.Values];
                IndexFile.Save(list, AppPaths.IndexFile);
            }
        }
        catch (Exception) { /* bir sonraki değişiklikte tekrar denenir */ }
    }
}

