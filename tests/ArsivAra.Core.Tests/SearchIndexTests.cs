namespace ArsivAra.Core.Tests;

/// <summary>
/// Synthetic index (index order):
///   Yedek_2025.rar      0 Belgeler/Şirket Raporu 2023.pdf (1.2 MB, 2023)   1 Belgeler/Şirket Sözleşmesi.docx (84 KB, 2024)
///                       2 Masaüstü/Toplantı Notları.txt (3 KB, 2025)       3 Masaüstü/Kısayollar/ (folder)
///                       4 İSTANBUL/Galata Kulesi.jpg (4.2 MB, 2025)         5 Belgeler Arşivi/okubeni.txt (500 B, 2024)
///   Muhasebe_2024.7z    6 Faturalar/2024/Ocak/fatura_00123.pdf (2024)      7 Faturalar/2023/Aralık/fatura_00987.pdf (2023)
///                       8 Banka/Hesap Özeti Ağustos 2024.pdf (2024)
///     › Eski/arsiv.zip  9 eski/Şirket Raporu 2023.pdf (1.2 MB, 2025; duplicate of 0: same name, size, CRC)
/// </summary>
public class SearchIndexTests
{
    static readonly DateTime D2023 = new(2023, 5, 1, 12, 0, 0, DateTimeKind.Local);
    static readonly DateTime D2024 = new(2024, 1, 10, 12, 0, 0, DateTimeKind.Local);
    static readonly DateTime D2025 = new(2025, 11, 3, 12, 0, 0, DateTimeKind.Local);

    static readonly ArchiveRecord Yedek = new()
    {
        Path = @"D:\Yedek_2025.rar",
        Entries =
        [
            Fx.File("Belgeler/Şirket Raporu 2023.pdf", 1_200_000, D2023, crc: 0xAABBCCDD),
            Fx.File("Belgeler/Şirket Sözleşmesi.docx", 84_000, D2024),
            Fx.File("Masaüstü/Toplantı Notları.txt", 3_000, D2025),
            Fx.Folder("Masaüstü/Kısayollar/"),
            Fx.File("İSTANBUL/Galata Kulesi.jpg", 4_200_000, D2025),
            Fx.File("Belgeler Arşivi/okubeni.txt", 500, D2024),
        ],
    };

    static readonly ArchiveRecord Muhasebe = new()
    {
        Path = @"E:\Muhasebe_2024.7z",
        Entries =
        [
            Fx.File("Faturalar/2024/Ocak/fatura_00123.pdf", 96_000, D2024),
            Fx.File("Faturalar/2023/Aralık/fatura_00987.pdf", 92_000, D2023),
            Fx.File("Banka/Hesap Özeti Ağustos 2024.pdf", 140_000, D2024),
        ],
        Nested =
        [
            new ArchiveRecord
            {
                Path = @"E:\Muhasebe_2024.7z", InnerPath = "Eski/arsiv.zip",
                Entries = [Fx.File("eski/Şirket Raporu 2023.pdf", 1_200_000, D2025, crc: 0xAABBCCDD)],
            },
        ],
    };

    static readonly SearchIndex Index = SearchIndex.Build([Yedek, Muhasebe]);

    static string[] Names(string query, (int, int)? scope = null)
    {
        var r = Index.Search(query, scope: scope);
        Assert.Equal(QueryErrorKind.None, r.Error);
        return r.Hits.Select(h => Index.Get(h).Entry.Path).ToArray();
    }

    [Fact]
    public void Nested_archives_follow_their_parent()
    {
        Assert.Equal(2, Index.TopLevel.Count);
        Assert.Equal(3, Index.Archives.Count);
        Assert.Equal(10, Index.EntryCount);
        Assert.Same(Muhasebe, Index.Get(8).Archive);
        Assert.Same(Muhasebe.Nested[0], Index.Get(9).Archive);
    }

    [Fact]
    public void Turkish_case_and_accent_insensitive()
    {
        Assert.Equal(2, Names("sirket rapor").Length);
        Assert.Single(Names("istanbul"));
        Assert.Single(Names("ISTANBUL"));
        Assert.Single(Names("toplanti"));
        Assert.Equal(Names("ŞİRKET"), Names("sirket"));
    }

    [Fact]
    public void And_or_not_phrase()
    {
        Assert.Equal(["Belgeler/Şirket Raporu 2023.pdf", "Belgeler/Şirket Sözleşmesi.docx", "eski/Şirket Raporu 2023.pdf"], Names("sirket"));
        Assert.Equal(4, Names("rapor|fatura").Length);
        Assert.Equal(Names("rapor|fatura"), Names("rapor | fatura"));
        Assert.Equal(["Belgeler/Şirket Sözleşmesi.docx"], Names("sirket !pdf"));
        Assert.Equal(["Belgeler/Şirket Raporu 2023.pdf", "eski/Şirket Raporu 2023.pdf"], Names("\"sirket raporu\""));
        Assert.Empty(Names("\"raporu sirket\""));
        Assert.Equal(["Belgeler/Şirket Sözleşmesi.docx"], Names("belgeler sozlesme|kulesi"));
    }

    [Fact]
    public void Wildcards_match_whole_name()
    {
        Assert.Equal(5, Names("*.pdf").Length);
        Assert.Equal(["Faturalar/2024/Ocak/fatura_00123.pdf", "Faturalar/2023/Aralık/fatura_00987.pdf"], Names("fatura_00???.pdf"));
        Assert.Empty(Names("fatura_00??.pdf"));
        Assert.Equal(["Faturalar/2024/Ocak/fatura_00123.pdf"], Names("faturalar/2024/*"));
        Assert.Equal(["Masaüstü/Kısayollar/"], Names("kisa*"));
    }

    [Fact]
    public void Filters()
    {
        Assert.Equal(6, Names("ext:pdf;docx").Length);
        Assert.Equal(["Belgeler/Şirket Sözleşmesi.docx"], Names("ext:.docx"));
        Assert.Equal(4, Names("arsiv:muhasebe").Length);          // including the nested archive
        Assert.Equal(["eski/Şirket Raporu 2023.pdf"], Names("arsiv:arsiv.zip"));
        Assert.Equal(["Masaüstü/Kısayollar/"], Names("klasor:"));
        Assert.Equal(9, Names("dosya:").Length);
        Assert.Equal(["Belgeler Arşivi/okubeni.txt"], Names("okubeni ad:okubeni"));
        Assert.Empty(Names("ad:belgeler"));                         // appears in the folder, not the name
        Assert.Equal(3, Names("belgeler").Length);                  // appears in the path
        Assert.Equal(["Belgeler/Şirket Raporu 2023.pdf", "eski/Şirket Raporu 2023.pdf"], Names("ad:=\"ŞİRKET RAPORU 2023.PDF\""));
        Assert.Empty(Names("ad:=\"Şirket Raporu\""));
        Assert.Equal(["Masaüstü/Kısayollar/"], Names("klasor:kisa"));
        Assert.Equal(["Masaüstü/Toplantı Notları.txt"], Names("!klasor: masaustu"));
    }

    [Fact]
    public void Size_filters()
    {
        Assert.Equal(3, Names("boyut:>1mb").Length);
        Assert.Equal(["Masaüstü/Toplantı Notları.txt", "Belgeler Arşivi/okubeni.txt"], Names("boyut:<10kb"));
        Assert.Equal(["Belgeler/Şirket Sözleşmesi.docx"], Names("size:80kb..85kb"));
        Assert.Equal(["Belgeler Arşivi/okubeni.txt"], Names("boyut:=500"));
        Assert.Equal(3, Names("boyut:1,1mb").Length);               // decimal comma; a single value = minimum
        var bad = Index.Search("boyut:abc");
        Assert.Equal(QueryErrorKind.Size, bad.Error);
        Assert.Equal("abc", bad.ErrorDetail);
    }

    [Fact]
    public void Date_filters()
    {
        Assert.Equal(4, Names("tarih:2024").Length);
        Assert.Equal(3, Names("tarih:>=2025-01-01").Length);
        Assert.Equal(2, Names("tarih:<2024").Length);
        Assert.Equal(6, Names("tarih:2023..2024").Length);
        Assert.Equal(["Belgeler/Şirket Sözleşmesi.docx", "Faturalar/2024/Ocak/fatura_00123.pdf"], Names("tarih:10.01.2024 fatura|sozlesme"));
        Assert.Equal(4, Names("date:2024-01").Length);
        Assert.Equal(QueryErrorKind.Date, Index.Search("tarih:2024-13-45").Error);
        Assert.Equal(QueryErrorKind.Date, Index.Search("tarih:dunden").Error);
    }

    [Fact]
    public void Regex_filter()
    {
        Assert.Equal(2, Names(@"regex:fatura_\d+\.pdf$").Length);
        Assert.Equal(2, Names("regex:Ocak|Aralık").Length);         // '|' is part of the regular expression
        Assert.Equal(QueryErrorKind.Regex, Index.Search("regex:[").Error);
    }

    [Fact]
    public void Duplicates_and_versions()
    {
        Assert.Equal(["Belgeler/Şirket Raporu 2023.pdf", "eski/Şirket Raporu 2023.pdf"], Names("kopya:"));
        int hit = Index.Search("\"sirket raporu\" arsiv:yedek").Hits.Single();
        var versions = Index.SameName(hit).Select(h => Index.Get(h).Archive.DisplayName).ToArray();
        Assert.Equal(["Muhasebe_2024.7z › arsiv.zip", "Yedek_2025.rar"], versions); // newest first
    }

    [Fact]
    public void Different_crc_is_not_a_duplicate()
    {
        var a = new ArchiveRecord { Path = "a.zip", Entries = [Fx.File("x.txt", 10, D2024, crc: 1), Fx.File("y.txt", 10, D2024)] };
        var b = new ArchiveRecord { Path = "b.zip", Entries = [Fx.File("x.txt", 10, D2024, crc: 2), Fx.File("y.txt", 10, D2024)] };
        var idx = SearchIndex.Build([a, b]);
        Assert.Equal(["y.txt", "y.txt"], idx.Search("kopya:").Hits.Select(h => idx.Get(h).Entry.Path).ToArray());
    }

    [Fact]
    public void Content_terms_are_returned_for_later()
    {
        var r = Index.Search("icerik:\"Gizli Kelime\" ext:pdf");
        Assert.Equal(["gizli kelime"], r.ContentTerms!);
        Assert.Equal(5, r.Hits.Length);
    }

    [Fact]
    public void Archive_scope_searches_only_that_range()
    {
        var scope = Index.ScopeOf(@"E:\Muhasebe_2024.7z");
        Assert.Equal(4, Names("", scope).Length);                    // including the nested archive
        Assert.Equal(["eski/Şirket Raporu 2023.pdf"], Names("sirket", scope));
        Assert.Equal((0, 0), Index.ScopeOf(@"X:\yok.zip"));
    }

    [Fact]
    public void Sorting()
    {
        var all = Index.Search("dosya:").Hits;
        string[] Sorted(SortKey k, bool desc = false) => Index.Sort(all, k, desc).Select(h => Index.Get(h).Entry.Name).ToArray();

        Assert.Equal("fatura_00123.pdf", Sorted(SortKey.Name)[0]);
        Assert.Equal("Toplantı Notları.txt", Sorted(SortKey.Name)[^1]);
        Assert.Equal(Sorted(SortKey.Name).Reverse(), Sorted(SortKey.Name, desc: true));
        Assert.Equal("okubeni.txt", Sorted(SortKey.Size)[0]);
        Assert.Equal("Galata Kulesi.jpg", Sorted(SortKey.Size, desc: true)[0]);
        Assert.Equal("Şirket Raporu 2023.pdf", Sorted(SortKey.Date)[0]); // of the two 2023 files, the one that comes first in the index
        Assert.Equal("fatura_00987.pdf", Sorted(SortKey.Date)[1]);
        Assert.Equal(["docx", "jpg", "pdf", "pdf", "pdf", "pdf", "pdf", "txt", "txt"], Sorted(SortKey.Type).Select(n => n[(n.LastIndexOf('.') + 1)..]).ToArray());
        Assert.Equal("Banka", Index.Get(Index.Sort(all, SortKey.Folder, false)[0]).Entry.Folder);
        Assert.Equal(all, Index.Sort(all, SortKey.Archive, false));
        Assert.Equal(Sorted(SortKey.Name), Sorted(SortKey.Name));     // repeatable
    }

    [Fact]
    public void Glob_matcher_handles_multibyte_characters()
    {
        static bool G(string s, string p) => SearchIndex.GlobMatch(System.Text.Encoding.UTF8.GetBytes(s), System.Text.Encoding.UTF8.GetBytes(p));
        Assert.True(G("çanta.pdf", "?anta.pdf"));
        Assert.True(G("çanta.pdf", "*.pdf"));
        Assert.True(G("ab", "a*b*"));
        Assert.False(G("abc", "a?"));
        Assert.True(G("", "*"));
        Assert.False(G("a", ""));
    }

    [Fact]
    public void Parser_size_and_date_helpers()
    {
        Assert.True(QueryParser.TryParseBytes("1,5mb", out long b));
        Assert.Equal(1_572_864, b);
        Assert.True(QueryParser.TryParseBytes("2GB", out b));
        Assert.Equal(2L << 30, b);
        Assert.False(QueryParser.TryParseBytes("mb", out _));
        var now = new DateTime(2026, 10, 16, 15, 0, 0);
        Assert.True(QueryParser.TryParseDate("bugun", now, out long min, out long max));
        Assert.Equal(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Local).ToUniversalTime().Ticks, min);
        Assert.Equal(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Local).ToUniversalTime().Ticks - 1, max);
        Assert.True(QueryParser.TryParseDate("Dün", now, out _, out _));
        Assert.True(QueryParser.TryParseDate("buay", now, out _, out _));
    }

    [Fact]
    public void Empty_index_and_empty_query()
    {
        Assert.Empty(SearchIndex.Empty.Search("x").Hits);
        Assert.Equal(10, Index.Search("   ").Hits.Length);
        Assert.Equal(10, Index.Search("!").Hits.Length);
    }
}
