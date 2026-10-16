namespace ArsivAra.Core;

/// <summary>
/// Temporary files extracted for opening, previewing and dragging (%TEMP%\ArsivAra). Old ones are deleted at program startup;
/// files still open in another program are skipped.
/// </summary>
public static class TempFiles
{
    public static string Root => AppPaths.ExtractDir;

    /// <summary>Total size of the temporary folder (bytes).</summary>
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
    /// Deletes folders and files not used for at least <paramref name="olderThan"/> (zero: all of them).
    /// Returns the number of bytes deleted.
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
        catch (Exception ex) { Log.Warn("Could not clean up temporary files", ex); }
        return freed;
    }

    /// <summary>The latest change in the folder: the folder itself or the newest file inside it.</summary>
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
        catch (Exception) { /* file is open or access denied: leave it */ }
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
