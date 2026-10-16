namespace ArsivAra.Core.Tests;

[Collection("Parolalar")] // the thumbnail reader consults PasswordStore; must not run at the same time as the password test
public class ExtractorTests
{
    static string CopyWithZone(string fixture, int zone)
    {
        string dir = Fx.TempDir();
        string copy = Path.Combine(dir, fixture);
        File.Copy(Fx.File(fixture), copy);
        File.WriteAllText(copy + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl=https://ornek.com/{fixture}\r\n");
        return copy;
    }

    static ArchiveRecord ReadAt(string path)
    {
        var fi = new FileInfo(path);
        return ArchiveReader.Read(path, fi.Length, fi.LastWriteTimeUtc.Ticks);
    }

    [Fact]
    public void Mark_of_the_web_is_copied_to_opened_and_extracted_files()
    {
        string archive = CopyWithZone("zip-utf8.zip", 3);
        Assert.Equal(3, Motw.ZoneId(archive));
        var rec = ReadAt(archive);
        var entry = rec.Entries.Single(e => e.Path == "okubeni.txt");

        string opened = ArchiveExtractor.ExtractToTemp(rec, entry);
        Assert.Equal(Fx.Files["okubeni.txt"], File.ReadAllBytes(opened));
        Assert.Equal(3, Motw.ZoneId(opened));

        string dest = Fx.TempDir();
        int n = ArchiveExtractor.ExtractGroup(rec, new Dictionary<string, string> { ["Belgeler/Sözleşme.txt"] = "Belgeler/Sözleşme.txt" }, dest, null);
        Assert.Equal(1, n);
        Assert.Equal(3, Motw.ZoneId(Path.Combine(dest, "Belgeler", "Sözleşme.txt")));
    }

    [Fact]
    public void Files_without_mark_stay_unmarked()
    {
        var rec = Fx.Read("zip-utf8.zip");
        string opened = ArchiveExtractor.ExtractToTemp(rec, rec.Entries.Single(e => e.Path == "okubeni.txt"));
        Assert.Null(Motw.ZoneId(opened));
    }

    [Fact]
    public void Opening_again_reuses_the_file_even_while_it_is_locked()
    {
        var rec = Fx.Read("7z-solid.7z");
        var entry = rec.Entries.Single(e => e.Path == "Belgeler/Şirket Raporu 2023.txt");
        string first = ArchiveExtractor.ExtractToTemp(rec, entry);
        using (new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None)) // as if it were open in another program
        {
            string second = ArchiveExtractor.ExtractToTemp(rec, entry);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void Extract_group_keeps_folders_and_renames_collisions()
    {
        var rec = Fx.Read("kaynak.tar.xz");
        string dest = Fx.TempDir();
        var map = Fx.Files.Keys.ToDictionary(k => k, k => k);
        Assert.Equal(4, ArchiveExtractor.ExtractGroup(rec, map, dest, null));
        Assert.Equal(4, ArchiveExtractor.ExtractGroup(rec, map, dest, null)); // second time: "name (2)"
        Assert.Equal(Fx.Files["Veri/rastgele.bin"], File.ReadAllBytes(Path.Combine(dest, "Veri", "rastgele.bin")));
        Assert.True(File.Exists(Path.Combine(dest, "Veri", "rastgele (2).bin")));
        Assert.Empty(Directory.GetFiles(dest, "*.part", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../dışarı.txt", "dışarı.txt")]
    [InlineData("a/../../b/c.txt", @"a\b\c.txt")]
    [InlineData(@"C:\Windows\x.dll", @"C\Windows\x.dll")]
    [InlineData("a:b.txt", "ab.txt")]
    [InlineData("klasör/NUL", @"klasör\_NUL")]
    public void Safe_combine_never_leaves_the_target(string relative, string expected)
    {
        string root = Fx.TempDir();
        Assert.Equal(Path.Combine(root, expected), ArchiveExtractor.SafeCombine(root, relative));
    }

    [Fact]
    public void Nested_entry_can_be_opened()
    {
        var rec = Fx.Read("ic-ice.zip");
        var inner = rec.Nested.Single(n => n.InnerPath == "arsivler/ic.tar.gz");
        var entry = inner.Entries.Single(e => e.Path == "Belgeler/Sözleşme.txt");
        Assert.Equal(Fx.Files["Belgeler/Sözleşme.txt"], File.ReadAllBytes(ArchiveExtractor.ExtractToTemp(inner, entry)));
    }

    [Fact]
    public void Thumbnail_reader_reads_zip_iso_and_nested_but_not_solid()
    {
        using var reader = new ArchiveEntryReader();
        Assert.NotNull(reader.Read(Fx.Read("zip-utf8.zip"), Fx.Png, 1 << 20));
        Assert.NotNull(reader.Read(Fx.Read("joliet.iso"), Fx.Png, 1 << 20));
        Assert.NotNull(reader.Read(Fx.Read("udf.iso"), Fx.Png, 1 << 20));
        Assert.NotNull(reader.Read(Fx.Read("rar5-qo.rar"), Fx.Png, 1 << 20));
        var nested = Fx.Read("ic-ice.zip").Nested.Single(n => n.InnerPath == "arsivler/ic.zip");
        Assert.NotNull(reader.Read(nested, Fx.Png, 1 << 20));
        Assert.Null(reader.Read(Fx.Read("7z-solid.7z"), Fx.Png, 1 << 20));   // solid: everything would have to be decompressed first
        Assert.Null(reader.Read(Fx.Read("zip-utf8.zip"), "Veri/rastgele.bin", 1024)); // larger than the limit
    }

    [Fact]
    public void Risky_file_types()
    {
        Assert.True(RiskyFiles.IsRisky("fatura.pdf.exe"));
        Assert.True(RiskyFiles.IsRisky("x.JS"));
        Assert.True(RiskyFiles.IsRisky("kisayol.lnk"));
        Assert.False(RiskyFiles.IsRisky("rapor.pdf"));
        Assert.False(RiskyFiles.IsRisky("foto.jpg"));
    }
}
