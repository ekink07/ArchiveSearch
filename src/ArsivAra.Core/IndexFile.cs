using System.IO.Compression;

namespace ArsivAra.Core;

/// <summary>İndeksi diske kaydeder/yükler (Brotli ile sıkıştırılmış ikili format).</summary>
public static class IndexFile
{
    const int Magic = 0x56535241; // "ARSV"
    const int Version = 1;

    public static void Save(IReadOnlyCollection<ArchiveRecord> archives, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var br = new BrotliStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(new BufferedStream(br, 1 << 20)))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(archives.Count);
            foreach (var a in archives)
            {
                w.Write(a.Path);
                w.Write(a.FileSize);
                w.Write(a.FileMtimeUtcTicks);
                w.Write(a.Error ?? "");
                w.Write(a.Entries.Length);
                foreach (var e in a.Entries)
                {
                    w.Write(e.Path);
                    w.Write(e.Size);
                    w.Write(e.MtimeUtcTicks);
                    w.Write(e.IsDirectory);
                }
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static List<ArchiveRecord> Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var br = new BrotliStream(fs, CompressionMode.Decompress);
        using var r = new BinaryReader(new BufferedStream(br, 1 << 16));
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
            throw new InvalidDataException("İndeks dosyası tanınmadı");

        int count = r.ReadInt32();
        var list = new List<ArchiveRecord>(count);
        for (int i = 0; i < count; i++)
        {
            string p = r.ReadString();
            long size = r.ReadInt64();
            long mtime = r.ReadInt64();
            string err = r.ReadString();
            var entries = new ArchiveEntry[r.ReadInt32()];
            for (int j = 0; j < entries.Length; j++)
                entries[j] = new ArchiveEntry(r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadBoolean());
            list.Add(new ArchiveRecord
            {
                Path = p, FileSize = size, FileMtimeUtcTicks = mtime,
                Error = err.Length == 0 ? null : err, Entries = entries,
            });
        }
        return list;
    }

    public static List<ArchiveRecord> TryLoad(string path)
    {
        try { return File.Exists(path) ? Load(path) : []; }
        catch (Exception) { return []; }
    }
}
