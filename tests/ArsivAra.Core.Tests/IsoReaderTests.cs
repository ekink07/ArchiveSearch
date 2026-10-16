using System.Text;

namespace ArsivAra.Core.Tests;

public class IsoReaderTests
{
    [Theory]
    [InlineData("joliet.iso")]
    [InlineData("udf.iso")]
    public void Lists_unicode_names_dates_and_contents(string fixture)
    {
        using var fs = File.OpenRead(Fx.File(fixture));
        var files = IsoReader.List(fs);
        var byPath = files.ToDictionary(f => f.Path);
        foreach (var (path, content) in Fx.Files)
        {
            var f = byPath[path];
            Assert.Equal(content.Length, f.Size);
            using var s = IsoReader.Open(fs, f);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(content, ms.ToArray());
            var local = new DateTime(f.MtimeUtcTicks, DateTimeKind.Utc).ToLocalTime();
            Assert.Equal(new DateTime(2024, 6, 15, 10, 30, 0), local, TimeSpan.FromSeconds(2));
        }
        Assert.True(byPath["Boş Klasör/"].IsDirectory);
        Assert.True(byPath["Fotoğraflar/İstanbul/"].IsDirectory);
    }

    [Fact]
    public void Plain_iso9660_lists_all_files()
    {
        using var fs = File.OpenRead(Fx.File("iso9660.iso"));
        var files = IsoReader.List(fs).Where(f => !f.IsDirectory).ToList();
        Assert.Equal(5, files.Count);
        var readme = files.Single(f => f.Path.Equals("OKUBENI.TXT", StringComparison.OrdinalIgnoreCase));
        using var s = IsoReader.Open(fs, readme);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(Fx.Files["okubeni.txt"], ms.ToArray());
    }

    /// <summary>A minimal hand-built ISO 9660 image: a long name via a Rock Ridge "NM" entry.</summary>
    [Fact]
    public void Rock_ridge_alternate_names()
    {
        const int S = 2048;
        var img = new byte[S * 20];
        // Primary volume descriptor (sector 16)
        var pvd = img.AsSpan(16 * S, S);
        pvd[0] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd[1..]);
        pvd[6] = 1;
        BitConverter.GetBytes((ushort)S).CopyTo(pvd[128..]);
        WriteRecord(pvd[156..], extent: 18, size: S, flags: 2, name: [0], nm: null); // root
        img[17 * S] = 255;
        Encoding.ASCII.GetBytes("CD001").CopyTo(img.AsSpan(17 * S + 1));

        // Root folder (sector 18): ".", "..", one file
        var dir = img.AsSpan(18 * S, S);
        int p = WriteRecord(dir, 18, S, 2, [0], null);
        p += WriteRecord(dir[p..], 18, S, 2, [1], null);
        var content = Encoding.UTF8.GetBytes("Rock Ridge içerik");
        content.CopyTo(img.AsSpan(19 * S));
        WriteRecord(dir[p..], 19, (uint)content.Length, 0, Encoding.ASCII.GetBytes("UZUNAD.TXT;1"), "Çok Uzun Dosya Adı.txt");

        using var ms = new MemoryStream(img);
        var file = Assert.Single(IsoReader.List(ms));
        Assert.Equal("Çok Uzun Dosya Adı.txt", file.Path);
        using var s = IsoReader.Open(ms, file);
        using var outp = new MemoryStream();
        s.CopyTo(outp);
        Assert.Equal(content, outp.ToArray());
    }

    static int WriteRecord(Span<byte> d, uint extent, uint size, byte flags, byte[] name, string? nm)
    {
        byte[] nmBytes = nm == null ? [] : Encoding.UTF8.GetBytes(nm);
        int nameLen = name.Length;
        int su = 33 + nameLen + ((nameLen & 1) == 0 ? 1 : 0);
        int len = su + (nm == null ? 0 : 5 + nmBytes.Length);
        if ((len & 1) == 1) len++;
        d[0] = (byte)len;
        BitConverter.GetBytes(extent).CopyTo(d[2..]);
        BitConverter.GetBytes(size).CopyTo(d[10..]);
        d[18] = 124; d[19] = 6; d[20] = 15; d[21] = 10; d[22] = 30; // 2024-06-15 10:30
        d[25] = flags;
        d[32] = (byte)nameLen;
        name.CopyTo(d[33..]);
        if (nm != null)
        {
            d[su] = (byte)'N';
            d[su + 1] = (byte)'M';
            d[su + 2] = (byte)(5 + nmBytes.Length);
            d[su + 3] = 1;
            d[su + 4] = 0;
            nmBytes.CopyTo(d[(su + 5)..]);
        }
        return len;
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        using var ms = new MemoryStream(new byte[100_000]);
        Assert.Throws<InvalidDataException>(() => IsoReader.List(ms));
    }
}
