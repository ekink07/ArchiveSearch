using System.IO.Compression;

namespace ArsivAra.Core.Tests;

public class IndexFileTests
{
    /// <summary>1.0.0'ın yazdığı biçim (sürüm 1).</summary>
    static void WriteV1(string path, string archivePath, string error, params (string Path, long Size, bool Dir)[] entries)
    {
        using var fs = File.Create(path);
        using var br = new BrotliStream(fs, CompressionLevel.Fastest);
        using var w = new BinaryWriter(br);
        w.Write(0x56535241);
        w.Write(1);
        w.Write(1);
        w.Write(archivePath);
        w.Write(123L);
        w.Write(456L);
        w.Write(error);
        w.Write(entries.Length);
        foreach (var e in entries)
        {
            w.Write(e.Path);
            w.Write(e.Size);
            w.Write(789L);
            w.Write(e.Dir);
        }
    }

    [Fact]
    public void Version1_is_migrated_and_backed_up()
    {
        string dir = Fx.TempDir();
        string path = Path.Combine(dir, "index.bin");
        WriteV1(path, @"D:\yedek.zip", "Şifreli arşiv (dosya listesi şifre olmadan okunamıyor)",
            ("Belgeler", 0, true), ("Belgeler/rapor.pdf", 10, false));

        var (list, status, backup) = IndexFile.LoadForUse(path);
        Assert.Equal(IndexFile.LoadStatus.Migrated, status);
        Assert.True(File.Exists(backup));
        var rec = Assert.Single(list);
        Assert.Equal(ArchiveError.Encrypted, rec.ErrorKind);
        Assert.True(rec.Manual);
        Assert.Equal("Belgeler/", rec.Entries[0].Path); // klasör yolu '/' ile biter

        IndexFile.Save(list, path);
        var (again, status2, _) = IndexFile.LoadForUse(path);
        Assert.Equal(IndexFile.LoadStatus.Ok, status2);
        Assert.Equal("Belgeler/rapor.pdf", again[0].Entries[1].Path);
    }

    [Fact]
    public void Version2_roundtrip_keeps_everything()
    {
        string path = Path.Combine(Fx.TempDir(), "index.bin");
        var rec = new ArchiveRecord
        {
            Path = @"D:\a.zip", FileSize = 1, FileMtimeUtcTicks = 2, IsSolid = true, Manual = false,
            Error = "x", ErrorKind = ArchiveError.InUse,
            Entries = [new ArchiveEntry("a/b.txt", 3, 4, false, 0xDEADBEEF), new ArchiveEntry("a/", 0, 0, true)],
            Nested = [new ArchiveRecord { Path = @"D:\a.zip", InnerPath = "i.zip", FileSize = 5, Entries = [new ArchiveEntry("x", 6, 7, false, 8)] }],
        };
        IndexFile.Save([rec], path);
        var back = Assert.Single(IndexFile.Load(path));
        Assert.Equal(rec.Path, back.Path);
        Assert.True(back.IsSolid);
        Assert.False(back.Manual);
        Assert.Equal(ArchiveError.InUse, back.ErrorKind);
        Assert.Equal(rec.Entries, back.Entries);
        var nested = Assert.Single(back.Nested);
        Assert.Equal("i.zip", nested.InnerPath);
        Assert.Equal(8u, nested.Entries[0].Crc);
    }

    [Fact]
    public void Unknown_version_is_moved_aside_never_overwritten()
    {
        string path = Path.Combine(Fx.TempDir(), "index.bin");
        using (var fs = File.Create(path))
        using (var br = new BrotliStream(fs, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(br))
        {
            w.Write(0x56535241);
            w.Write(99); // gelecekteki bir sürüm
        }
        Assert.Throws<IndexVersionException>(() => IndexFile.Load(path));

        var (list, status, backup) = IndexFile.LoadForUse(path);
        Assert.Empty(list);
        Assert.Equal(IndexFile.LoadStatus.Unreadable, status);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void Garbage_file_is_moved_aside()
    {
        string path = Path.Combine(Fx.TempDir(), "index.bin");
        File.WriteAllText(path, "bu bir indeks değil");
        var (list, status, backup) = IndexFile.LoadForUse(path);
        Assert.Empty(list);
        Assert.Equal(IndexFile.LoadStatus.Unreadable, status);
        Assert.Equal("bu bir indeks değil", File.ReadAllText(backup!));
    }

    [Fact]
    public void Missing_file_is_empty()
    {
        var (list, status, _) = IndexFile.LoadForUse(Path.Combine(Fx.TempDir(), "yok.bin"));
        Assert.Empty(list);
        Assert.Equal(IndexFile.LoadStatus.Missing, status);
    }
}
