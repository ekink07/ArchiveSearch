namespace ArsivAra.Core;

/// <summary>
/// Açmak, önizlemek ve sürüklemek için çıkarılan geçici dosyalar (%TEMP%\ArsivAra). Program açılışında eskileri silinir;
/// başka bir programda hâlâ açık olan dosyalar atlanır.
/// </summary>
public static class TempFiles
{
    public static string Root => AppPaths.ExtractDir;

    /// <summary>Geçici klasörün toplam boyutu (bayt).</summary>
    public static long Size(string? root = null)
    {
        root ??= Root;
        try
        {
            return Directory.Exists(root)
                ? new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// <paramref name="olderThan"/> süresinden beri kullanılmamış klasör ve dosyaları siler (sıfır: hepsi).
    /// Silinen bayt sayısını döndürür.
    /// </summary>
    public static long Clean(TimeSpan olderThan, string? root = null)
    {
        root ??= Root;
        if (!Directory.Exists(root)) return 0;
        var limit = DateTime.UtcNow - olderThan;
        long freed = 0;
        try
        {
            foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (LastUse(dir) > limit) continue;
                freed += DeleteTree(dir);
            }
            foreach (var file in new DirectoryInfo(root).EnumerateFiles())
                if (file.LastWriteTimeUtc <= limit) freed += TryDelete(file);
        }
        catch (Exception ex) { Log.Warn("Geçici dosyalar temizlenemedi", ex); }
        return freed;
    }

    /// <summary>Klasördeki en son değişiklik: klasörün kendisi ya da içindeki en yeni dosya.</summary>
    static DateTime LastUse(DirectoryInfo dir)
    {
        var last = dir.LastWriteTimeUtc;
        try
        {
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                if (f.LastWriteTimeUtc > last) last = f.LastWriteTimeUtc;
        }
        catch (Exception) { }
        return last;
    }

    static long DeleteTree(DirectoryInfo dir)
    {
        long freed = 0;
        try
        {
            foreach (var sub in dir.EnumerateDirectories()) freed += DeleteTree(sub);
            foreach (var f in dir.EnumerateFiles()) freed += TryDelete(f);
            if (!dir.EnumerateFileSystemInfos().Any()) dir.Delete();
        }
        catch (Exception) { /* açık dosya ya da erişim yok: kalsın */ }
        return freed;
    }

    static long TryDelete(FileInfo f)
    {
        try
        {
            long len = f.Length;
            f.Delete();
            return len;
        }
        catch (Exception) { return 0; }
    }
}
