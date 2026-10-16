using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArsivAra.Core;

namespace ArsivAra.App;

/// <summary>
/// Arşiv içindeki resimlerin küçük resimlerini arka planda üretir. Sadece ekranda görünen kareler istenir;
/// en son istenen önce yapılır ve hızlı kaydırırken eski istekler atılır. Üretilen küçük resimler bellekte tutulur.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico" };

    const long MaxImageBytes = 40L << 20; // daha büyük resimlerde simge yeter
    const int DecodeWidth = 256;          // en büyük kare boyutu
    const int MaxPending = 96;
    const int MaxCached = 3000;
    const int Workers = 2;

    readonly ArchiveEntryReader _reader = new();
    readonly object _lock = new();
    readonly LinkedList<TileVM> _pending = new();               // baş: en yeni istek
    readonly Dictionary<(string, string), ImageSource?> _cache = new();
    readonly Queue<(string, string)> _cacheOrder = new();
    int _running;

    public static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name));

    public void Request(TileVM tile)
    {
        var key = (tile.Archive.DisplayPath, tile.Entry.Path);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached != null) tile.Thumbnail = cached;
                return;
            }
            _pending.AddFirst(tile);
            while (_pending.Count > MaxPending) _pending.RemoveLast();
            if (_running >= Workers) return;
            _running++;
        }
        Task.Run(Work);
    }

    /// <summary>Yeni arama/görünümde ekranda olmayan eski istekleri at.</summary>
    public void CancelPending()
    {
        lock (_lock) _pending.Clear();
    }

    void Work()
    {
        while (true)
        {
            TileVM tile;
            lock (_lock)
            {
                if (_pending.Count == 0) { _running--; return; }
                tile = _pending.First!.Value;
                _pending.RemoveFirst();
            }

            var key = (tile.Archive.DisplayPath, tile.Entry.Path);
            ImageSource? img;
            lock (_lock) _cache.TryGetValue(key, out img);
            if (img == null)
            {
                img = Decode(_reader.Read(tile.Archive, tile.Entry.Path, MaxImageBytes));
                lock (_lock)
                {
                    _cache[key] = img;
                    _cacheOrder.Enqueue(key);
                    while (_cacheOrder.Count > MaxCached) _cache.Remove(_cacheOrder.Dequeue());
                }
            }
            if (img != null) tile.Thumbnail = img; // WPF bağlama motoru başka iş parçacığından gelen bildirimi aktarır
        }
    }

    static ImageSource? Decode(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; } // bozuk ya da Windows'un çözemediği biçim: simge kalır
    }

    public void Dispose() => _reader.Dispose();
}
