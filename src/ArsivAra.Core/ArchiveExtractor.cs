using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Readers;

namespace ArsivAra.Core;

/// <summary>
/// Arşivlerden dosya çıkarır. ZIP, ISO ve solid olmayan arşivlerde istenen girdiye doğrudan gidilir;
/// solid arşivlerde (7z, sıkıştırılmış tar) arşiv bir kez baştan sona okunur ve istenenler yol üstünde alınır.
/// İç içe arşivde önce içteki arşiv geçici klasöre çıkarılır. Çıkarılan dosyalara arşivin
/// "internetten indirildi" işareti (Mark-of-the-Web) aktarılır, böylece SmartScreen ve Office Korumalı Görünüm devrede kalır.
/// </summary>
public static class ArchiveExtractor
{
    public readonly record struct Item(string Path, long Size);

    static readonly object InnerLock = new();

    /// <summary>İstenen girdileri bulur ve her biri için bir kez <paramref name="onEntry"/> çağırır (akış sadece çağrı süresince geçerli).</summary>
    public static void ForEach(ArchiveRecord rec, IReadOnlySet<string> paths, string? password, Action<Item, Stream> onEntry, CancellationToken ct = default)
    {
        if (!rec.IsNested)
        {
            ForEach(rec.Path, paths, password, onEntry, ct);
            return;
        }
        string inner = InnerArchiveFile(rec, password, ct);
        ForEachInFile(inner, ArchiveReader.FormatOf(rec.InnerPath), paths, password, onEntry, ct);
    }

    public static void ForEach(string archivePath, IReadOnlySet<string> paths, string? password, Action<Item, Stream> onEntry, CancellationToken ct = default) =>
        ForEachInFile(archivePath, ArchiveReader.FormatOf(archivePath), paths, password, onEntry, ct);

    static void ForEachInFile(string file, ArchiveFormat format, IReadOnlySet<string> wanted, string? password,
        Action<Item, Stream> onEntry, CancellationToken ct)
    {
        if (wanted.Count == 0) return;
        var done = new HashSet<string>(StringComparer.Ordinal);
        switch (format)
        {
            case ArchiveFormat.Zip:
                if (FromZip(file, wanted, done, onEntry, ct)) return;
                break; // ZIP değil ya da şifreli/desteklenmeyen sıkıştırma: kalanlar SharpCompress ile
            case ArchiveFormat.Iso:
                using (var fs = ArchiveReader.OpenShared(file)) FromIso(fs, wanted, done, onEntry, ct);
                return;
            case ArchiveFormat.CompressedTar:
                using (var fs = ArchiveReader.OpenShared(file))
                using (var reader = ReaderFactory.OpenReader(fs, ArchiveReader.CreateReaderOptions(password, leaveOpen: true)))
                    Drain(reader, wanted, done, onEntry, ct);
                return;
        }
        FromSharp(format == ArchiveFormat.Rar ? ArchiveReader.RarParts(file) : [file], wanted, done, password, onEntry, ct);
    }

    /// <returns>true: istenenler ZIP'ten alındı; false: kalanlar SharpCompress ile denenmeli.</returns>
    static bool FromZip(string file, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        var fs = ArchiveReader.OpenShared(file);
        ZipArchive zip;
        try { zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, ArchiveReader.LegacyNameEncoding); }
        catch (InvalidDataException) { fs.Dispose(); return false; }

        using (zip)
        {
            foreach (var e in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string key = ArchiveReader.NormalizePath(e.FullName);
                if (key.EndsWith('/') || !wanted.Contains(key) || done.Contains(key)) continue;
                if (e.IsEncrypted) return false; // .NET şifreli ZIP açamaz; SharpCompress parolayla açar
                Stream s;
                try { s = e.Open(); }
                catch (InvalidDataException) { return false; } // desteklenmeyen sıkıştırma yöntemi (LZMA, BZip2, PPMd...)
                using (s) onEntry(new Item(key, e.Length), s);
                done.Add(key);
                if (done.Count == wanted.Count) break;
            }
        }
        return true;
    }

    static void FromSharp(IReadOnlyList<string> parts, IReadOnlySet<string> wanted, HashSet<string> done, string? password,
        Action<Item, Stream> onEntry, CancellationToken ct)
    {
        using var archive = ArchiveReader.OpenSharp(parts, password);
        // Önce girdiler: başlığı şifreli RAR5'te IsSolid girdilerden önce sorulursa liste boş döner (SharpCompress).
        var entries = archive.Entries.ToList();
        if (!archive.IsSolid)
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.IsDirectory) continue;
                string key = ArchiveReader.NormalizePath(e.Key ?? "");
                if (!wanted.Contains(key) || !done.Add(key)) continue;
                using (var s = e.OpenEntryStream()) onEntry(new Item(key, e.Size), s);
                if (done.Count == wanted.Count) return;
            }
            return;
        }
        using var reader = archive.ExtractAllEntries();
        Drain(reader, wanted, done, onEntry, ct);
    }

    /// <summary>Solid arşiv ya da akış: girdiler sırayla geçilir, istenenler yol üstünde okunur.</summary>
    static void Drain(IReader reader, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var e = reader.Entry;
            if (e.IsDirectory) continue;
            string key = ArchiveReader.NormalizePath(e.Key ?? "");
            if (!wanted.Contains(key) || !done.Add(key)) continue;
            using (var s = reader.OpenEntryStream()) onEntry(new Item(key, e.Size), s);
            if (done.Count == wanted.Count) return;
        }
    }

    static void FromIso(Stream image, IReadOnlySet<string> wanted, HashSet<string> done, Action<Item, Stream> onEntry, CancellationToken ct)
    {
        foreach (var f in IsoReader.List(image))
        {
            ct.ThrowIfCancellationRequested();
            if (f.IsDirectory || !wanted.Contains(f.Path) || !done.Add(f.Path)) continue;
            using (var s = IsoReader.Open(image, f)) onEntry(new Item(f.Path, f.Size), s);
            if (done.Count == wanted.Count) return;
        }
    }

    // ---- Geçici klasöre çıkarma ----

    /// <summary>İç içe arşivi geçici klasöre bir kez çıkarır; sonraki çağrılar aynı dosyayı kullanır.</summary>
    public static string InnerArchiveFile(ArchiveRecord rec, string? password, CancellationToken ct = default)
    {
        if (!rec.IsNested) return rec.Path;
        string file = Path.Combine(AppPaths.ExtractDir, "ic",
            Id(rec.Path, rec.InnerPath!, rec.FileSize.ToString(), rec.FileMtimeUtcTicks.ToString()) + ArchiveReader.ExtensionOf(rec.InnerPath!));
        lock (InnerLock)
        {
            if (File.Exists(file) && new FileInfo(file).Length == rec.FileSize) return file;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            bool found = false;
            try
            {
                ForEach(rec.Path, Single(rec.InnerPath!), password, (_, s) =>
                {
                    using var d = File.Create(tmp);
                    s.CopyTo(d);
                    found = true;
                }, ct);
                if (!found) throw new FileNotFoundException("İç arşiv bulunamadı: " + rec.InnerPath);
                File.Move(tmp, file, overwrite: true);
            }
            finally { TryDelete(tmp); }
            Motw.Propagate(rec.Path, file);
            return file;
        }
    }

    /// <summary>
    /// Tek bir dosyayı geçici klasöre çıkarır ve yolunu döndürür. Aynı dosya daha önce çıkarıldıysa (ör. hâlâ bir programda açıksa)
    /// yeniden yazılmaz; açık ama farklıysa yanına numaralı bir klasör açılır.
    /// </summary>
    public static string ExtractToTemp(ArchiveRecord rec, ArchiveEntry entry, string? password = null, CancellationToken ct = default)
    {
        string dir = Path.Combine(AppPaths.ExtractDir, Id(rec.DisplayPath, entry.Path, entry.Size.ToString(), entry.MtimeUtcTicks.ToString()));
        string dest = Path.Combine(dir, ArchiveReader.SafeFileName(entry.Path));
        if (File.Exists(dest) && new FileInfo(dest).Length == entry.Size)
        {
            Touch(dir);
            return dest;
        }

        Directory.CreateDirectory(dir);
        string tmp = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        bool found = false;
        try
        {
            ForEach(rec, Single(entry.Path), password, (_, s) =>
            {
                using var d = File.Create(tmp);
                s.CopyTo(d);
                found = true;
            }, ct);
            if (!found) throw new FileNotFoundException("Arşivde bulunamadı: " + entry.Path);
            dest = MoveIntoPlace(tmp, dest);
        }
        finally { TryDelete(tmp); }
        Motw.Propagate(rec.Path, dest);
        return dest;
    }

    /// <summary>
    /// Bir arşivden seçilen dosyaları klasöre yazar. <paramref name="relative"/>: girdi yolu → hedef klasöre göre yolu.
    /// Aynı adda dosya varsa "ad (2).uzantı" olarak yazılır. Yazılan her dosya için <paramref name="onFile"/> çağrılır.
    /// </summary>
    public static int ExtractGroup(ArchiveRecord rec, IReadOnlyDictionary<string, string> relative, string destDir, string? password,
        Action<string>? onFile = null, CancellationToken ct = default)
    {
        int count = 0;
        string root = Path.GetFullPath(destDir);
        ForEach(rec, relative.Keys.ToHashSet(StringComparer.Ordinal), password, (item, s) =>
        {
            string target = UniqueName(SafeCombine(root, relative[item.Path]));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string tmp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            try
            {
                using (var d = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write)) s.CopyTo(d);
                File.Move(tmp, target);
            }
            finally { TryDelete(tmp); }
            Motw.Propagate(rec.Path, target);
            count++;
            onFile?.Invoke(target);
        }, ct);
        return count;
    }

    /// <summary>Göreli yolu hedef klasörün altına güvenle yerleştirir: "..", sürücü adı ve geçersiz karakterler temizlenir.</summary>
    public static string SafeCombine(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not "." and not "..")
            .Select(ArchiveReader.SafeSegment)
            .ToArray();
        if (parts.Length == 0) throw new IOException("Geçersiz yol: " + relative);
        string full = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Geçersiz yol: " + relative);
        return full;
    }

    static string UniqueName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    /// <summary>Hedef başka bir programda açık ve farklıysa (taşıma başarısız) yanına numaralı klasör açılır.</summary>
    static string MoveIntoPlace(string tmp, string dest)
    {
        try
        {
            File.Move(tmp, dest, overwrite: true);
            return dest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        string dir = Path.GetDirectoryName(dest)!, name = Path.GetFileName(dest);
        for (int i = 2; i < 100; i++)
        {
            string alt = Path.Combine(dir + "-" + i, name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(alt)!);
                File.Move(tmp, alt, overwrite: true);
                return alt;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        throw new IOException("Geçici dosya yazılamadı: " + dest);
    }

    static HashSet<string> Single(string path) => new(StringComparer.Ordinal) { path };

    static string Id(params string[] parts) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))))[..16];

    static void Touch(string dir)
    {
        try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch (Exception) { }
    }

    static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
    }
}
