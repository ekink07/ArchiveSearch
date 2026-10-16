namespace ArsivAra.Core.Tests;

public class ArchiveReaderTests
{
    public static TheoryData<string> Formats => new()
    {
        "rar5-qo.rar", "rar5-solid.rar", "rar5-cok-parca.part1.rar", "rar4-cok-parca.rar",
        "7z-solid.7z", "zip-aes.zip", "zip-utf8.zip", "zip-cp857.zip",
        "kaynak.tar", "kaynak.tar.gz", "kaynak.tgz", "kaynak.tar.bz2", "kaynak.tar.xz",
        "joliet.iso", "udf.iso",
    };

    static string? PasswordFor(string fixture) => fixture == "zip-aes.zip" ? Fx.Password : null;

    [Theory, MemberData(nameof(Formats))]
    public void Lists_every_file_with_its_size(string fixture)
    {
        var rec = Fx.Read(fixture);
        Assert.Null(rec.Error);
        var files = rec.Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Path, e => e.Size);
        foreach (var (path, content) in Fx.Files)
        {
            Assert.True(files.ContainsKey(path), $"{fixture}: '{path}' yok. Bulunanlar: {string.Join(" | ", files.Keys)}");
            Assert.Equal(content.Length, files[path]);
        }
        Assert.Contains(Fx.Png, files.Keys);
        Assert.Equal(5, files.Count);
        Assert.Contains(rec.Entries, e => e.IsDirectory && e.Path == "Boş Klasör/");
        Assert.All(rec.Entries.Where(e => e.IsDirectory), e => Assert.EndsWith("/", e.Path));
    }

    [Theory, MemberData(nameof(Formats))]
    public void Extracts_exact_contents(string fixture)
    {
        var rec = Fx.Read(fixture);
        foreach (var (path, content) in Fx.Files)
            Assert.Equal(content, Fx.Extract(rec, path, PasswordFor(fixture)));
    }

    [Theory]
    [InlineData("zip-utf8.zip")]
    [InlineData("rar5-qo.rar")]
    [InlineData("7z-solid.7z")]
    [InlineData("rar5-cok-parca.part1.rar")]
    public void Records_crc32_of_contents(string fixture)
    {
        var rec = Fx.Read(fixture);
        foreach (var (path, content) in Fx.Files)
            Assert.Equal(Fx.Crc32(content), rec.Entries.Single(e => e.Path == path).Crc);
    }

    [Fact]
    public void Solid_archives_are_marked()
    {
        Assert.True(Fx.Read("7z-solid.7z").IsSolid);
        Assert.True(Fx.Read("rar5-solid.rar").IsSolid);
        Assert.True(Fx.Read("kaynak.tar.gz").IsSolid);
        Assert.False(Fx.Read("rar5-qo.rar").IsSolid);
        Assert.False(Fx.Read("zip-utf8.zip").IsSolid);
        Assert.False(Fx.Read("joliet.iso").IsSolid);
    }

    [Fact]
    public void Old_dos_zip_names_are_decoded_as_turkish()
    {
        ArchiveReader.LegacyNameEncoding = NameEncoding.ForCodePage(857); // test makinesinin dilinden bağımsız
        var rec = Fx.Read("zip-cp857.zip");
        Assert.Contains(rec.Entries, e => e.Path == "Belgeler/Şirket Raporu 2023.txt");
        Assert.Contains(rec.Entries, e => e.Path == "Fotoğraflar/İstanbul/kule.png");
    }

    [Theory]
    [InlineData("x.zip", true)]
    [InlineData("x.ZIP", true)]
    [InlineData("x.rar", true)]
    [InlineData("x.part1.rar", true)]
    [InlineData("x.part01.rar", true)]
    [InlineData("x.part2.rar", false)]
    [InlineData("x.part10.rar", false)]
    [InlineData("x.r00", false)]
    [InlineData("x.7z", true)]
    [InlineData("x.tar", true)]
    [InlineData("x.tar.gz", true)]
    [InlineData("x.tgz", true)]
    [InlineData("x.tar.bz2", true)]
    [InlineData("x.tar.xz", true)]
    [InlineData("x.tar.zst", true)]
    [InlineData("x.gz", false)]
    [InlineData("rapor.pdf.gz", false)]
    [InlineData("x.iso", true)]
    [InlineData("x.docx", false)]
    [InlineData("x", false)]
    public void Recognizes_archive_names(string name, bool expected) => Assert.Equal(expected, ArchiveReader.IsArchiveName(name));

    [Fact]
    public void Compressed_tar_keeps_its_double_extension() => Assert.Equal(".tar.gz", ArchiveReader.ExtensionOf("yedek.tar.gz"));

    [Fact]
    public void Multi_volume_rar_reads_all_parts()
    {
        foreach (var fixture in new[] { "rar5-cok-parca.part1.rar", "rar4-cok-parca.rar" })
        {
            var rec = Fx.Read(fixture);
            Assert.Null(rec.Error);
            Assert.Equal(Fx.Files["Veri/rastgele.bin"], Fx.Extract(rec, "Veri/rastgele.bin")); // parçalara bölünmüş dosya
        }
    }

    [Theory]
    [InlineData("rar5-sifreli-baslik.rar")]
    [InlineData("7z-sifreli.7z")]
    public void Encrypted_file_list_needs_password(string fixture)
    {
        var locked = Fx.Read(fixture);
        Assert.Equal(ArchiveError.Encrypted, locked.ErrorKind);
        Assert.True(locked.NeedsPassword);
        Assert.Empty(locked.Entries);

        var wrong = Fx.Read(fixture, password: "yanlis-parola");
        Assert.Equal(ArchiveError.Encrypted, wrong.ErrorKind);

        var open = Fx.Read(fixture, password: Fx.Password);
        Assert.Null(open.Error);
        Assert.Equal(5, open.Entries.Count(e => !e.IsDirectory));
        Assert.Equal(Fx.Files["okubeni.txt"], Fx.Extract(open, "okubeni.txt", Fx.Password));
    }

    [Theory]
    [InlineData("rar5-sifreli-icerik.rar")]
    [InlineData("zip-aes.zip")]
    public void Encrypted_contents_are_listed_but_need_password_to_extract(string fixture)
    {
        var rec = Fx.Read(fixture);
        Assert.Null(rec.Error);
        Assert.Equal(5, rec.Entries.Count(e => !e.IsDirectory));
        var ex = Record.Exception(() => Fx.Extract(rec, "okubeni.txt"));
        Assert.NotNull(ex);
        Assert.Equal(ArchiveError.Encrypted, ArchiveReader.Describe(ex).Kind);
        Assert.Equal(Fx.Files["okubeni.txt"], Fx.Extract(rec, "okubeni.txt", Fx.Password));
    }

    [Fact]
    public void Nested_archives_are_listed_and_extractable()
    {
        var rec = Fx.Read("ic-ice.zip");
        Assert.Null(rec.Error);
        Assert.Equal(["arsivler/ic.7z", "arsivler/ic.tar.gz", "arsivler/ic.zip"], rec.Nested.Select(n => n.InnerPath!).ToArray());
        foreach (var inner in rec.Nested)
        {
            Assert.Null(inner.Error);
            Assert.Equal(rec.Path, inner.Path);
            Assert.True(inner.IsNested);
            Assert.Equal(5, inner.Entries.Count(e => !e.IsDirectory));
            Assert.Equal(Fx.Files["Belgeler/Şirket Raporu 2023.txt"], Fx.Extract(inner, "Belgeler/Şirket Raporu 2023.txt"));
        }
        Assert.Equal("ic-ice.zip › ic.zip", rec.Nested[2].DisplayName);
        Assert.Equal(rec.Entries.Length + rec.Nested.Sum(n => n.Entries.Length), rec.TotalEntryCount);

        var flat = Fx.Read("ic-ice.zip", nested: false);
        Assert.Empty(flat.Nested);
    }

    [Fact]
    public void Rar5_quick_open_matches_sharpcompress()
    {
        string path = Fx.File("rar5-qo.rar");
        var fast = ArchiveReader.TryReadRar5QuickOpen(path);
        Assert.NotNull(fast);
        Assert.True(Rar5Lister.LastCacheHits > 0, "Quick Open kaydı kullanılmadı");

        // Aynı arşivi SharpCompress başlıktan başlığa gezerek okusun; sonuçlar birebir aynı olmalı.
        using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(path, new SharpCompress.Readers.ReaderOptions());
        var slow = archive.Entries.Select(e =>
        {
            string p = ArchiveReader.NormalizePath(e.Key!);
            if (e.IsDirectory && !p.EndsWith('/')) p += "/";
            return $"{p}|{e.IsDirectory}|{(e.IsDirectory ? 0 : e.Size)}|{(e.IsDirectory ? 0 : (uint)e.Crc)}";
        });
        static string Key(ArchiveEntry e) => $"{e.Path}|{e.IsDirectory}|{e.Size}|{e.Crc}";
        Assert.Equal(slow.Order(), fast!.Select(Key).Order());

        // Çok parçalı ve başlığı şifreli RAR5'te hızlı yol kullanılmaz.
        Assert.Null(ArchiveReader.TryReadRar5QuickOpen(Fx.File("rar5-cok-parca.part1.rar")));
        Assert.Null(ArchiveReader.TryReadRar5QuickOpen(Fx.File("rar5-sifreli-baslik.rar")));
    }

    [Fact]
    public void Corrupt_file_reports_error_instead_of_throwing()
    {
        string dir = Fx.TempDir();
        string bad = Path.Combine(dir, "bozuk.zip");
        File.WriteAllBytes(bad, [1, 2, 3, 4, 5]);
        var rec = ArchiveReader.Read(bad, 5, 0);
        Assert.NotNull(rec.Error);
        Assert.Equal(ArchiveError.Other, rec.ErrorKind);
    }

    [Theory]
    [InlineData("../../Windows/evil.exe", "evil.exe")]
    [InlineData("klasor/CON.txt", "_CON.txt")]
    [InlineData("x/a*b?c.txt", "abc.txt")]
    [InlineData("ad. ", "ad")]
    public void Safe_file_names(string entryPath, string expected) => Assert.Equal(expected, ArchiveReader.SafeFileName(entryPath));
}
