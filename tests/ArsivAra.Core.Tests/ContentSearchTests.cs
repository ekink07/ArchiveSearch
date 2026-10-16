using System.IO.Compression;
using System.Text;

namespace ArsivAra.Core.Tests;

public class ContentSearchTests
{
    [Fact]
    public void Finds_text_inside_plain_files_across_formats()
    {
        var archives = new[] { "zip-utf8.zip", "7z-solid.7z", "kaynak.tar.gz", "joliet.iso", "rar5-qo.rar" }.Select(f => Fx.Read(f)).ToList();
        var index = SearchIndex.Build(archives);
        var r = index.Search("icerik:ZÜMRÜT ext:txt");
        Assert.Equal(["zumrut"], r.ContentTerms!);

        int reported = 0;
        var result = ContentSearch.Filter(index, r.Hits, r.ContentTerms!, _ => null,
            new Progress<(int Done, int Total)>(_ => Interlocked.Increment(ref reported)), CancellationToken.None);

        Assert.Equal(5, result.Hits.Length); // her arşivde "Şirket Raporu 2023.txt"
        Assert.All(result.Hits, h => Assert.Equal("Şirket Raporu 2023.txt", index.Get(h).Entry.Name));
        Assert.Equal(15, result.Searched);   // arşiv başına 3 .txt
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void Encrypted_contents_use_the_password()
    {
        var index = SearchIndex.Build([Fx.Read("rar5-sifreli-icerik.rar")]);
        var r = index.Search("icerik:istanbul");
        Assert.Empty(ContentSearch.Filter(index, r.Hits, r.ContentTerms!, _ => null, null, CancellationToken.None).Hits);
        var withPassword = ContentSearch.Filter(index, r.Hits, r.ContentTerms!, _ => Fx.Password, null, CancellationToken.None);
        Assert.Equal("Sözleşme.txt", index.Get(Assert.Single(withPassword.Hits)).Entry.Name);
    }

    [Fact]
    public void Reads_office_documents_with_split_runs()
    {
        // Word bir kelimeyi birkaç "run"a bölebilir: "Mer" + "haba"
        string docx = Path.Combine(Fx.TempDir(), "belge.docx");
        using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8))
            w.Write("""<w:document><w:body><w:p><w:r><w:t>Mer</w:t></w:r><w:r><w:t>haba &amp; Dünya</w:t></w:r></w:p><w:p><w:r><w:t>İkinci</w:t></w:r></w:p></w:body></w:document>""");

        string text = ContentSearch.ExtractText("belge.docx", File.OpenRead(docx));
        Assert.Contains("Merhaba & Dünya", text);
        Assert.Contains("İkinci", text);
        Assert.DoesNotContain("DünyaİkinciX", text + "X"); // paragraflar arasında boşluk var
    }

    [Fact]
    public void Decodes_bom_utf8_and_legacy_text()
    {
        Assert.Equal("şık", ContentSearch.DecodeText([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("şık")]));
        Assert.Equal("şık", ContentSearch.DecodeText(Encoding.UTF8.GetBytes("şık")));
        Assert.Equal("şık", ContentSearch.DecodeText([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("şık")]));
    }

    [Fact]
    public void Only_text_and_office_types_within_limits()
    {
        Assert.True(ContentSearch.CanSearch(new ArchiveEntry("a/b.txt", 10, 0, false)));
        Assert.True(ContentSearch.CanSearch(new ArchiveEntry("a/b.DOCX", 10, 0, false)));
        Assert.False(ContentSearch.CanSearch(new ArchiveEntry("a/b.pdf", 10, 0, false)));
        Assert.False(ContentSearch.CanSearch(new ArchiveEntry("a/b.txt", ContentSearch.MaxTextBytes + 1, 0, false)));
        Assert.False(ContentSearch.CanSearch(new ArchiveEntry("a/", 0, 0, true)));
    }
}
