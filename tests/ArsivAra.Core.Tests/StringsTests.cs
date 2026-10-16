using System.Text.RegularExpressions;

namespace ArsivAra.Core.Tests;

public class StringsTests
{
    [Fact]
    public void Every_text_has_four_languages_with_the_same_placeholders()
    {
        Assert.True(Strings.Keys.Count() > 100);
        foreach (var key in Strings.Keys)
        {
            var texts = Strings.Languages.Select(l => Strings.Get(l, key)).ToArray();
            Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t), key));
            var placeholders = texts
                .Select(t => string.Join(",", Regex.Matches(t, @"\{\d+\}").Select(m => m.Value).Distinct().Order()))
                .Distinct().ToList();
            Assert.True(placeholders.Count == 1, $"{key}: yer tutucular dillerde farklı: {string.Join(" | ", placeholders)}");
            foreach (var t in texts) _ = string.Format(t, "a", "b", "c"); // biçim hatası olmamalı
        }
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        string app = Path.Combine(RepoRoot(), "src", "ArsivAra.App");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".xaml") || file.EndsWith(".cs")) || file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
            if (Path.GetFileName(file) == "Loc.cs") continue; // belge örnekleri ("Anahtar")
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{l:T (\w+)\}")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Loc\.[TF]\(""(\w+)""")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"""(Col(?:Name|Folder|Size|Modified|Archive|Type)|QueryError\w+|Status\w+|Day\w*)"""))
                used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 80, $"Sadece {used.Count} anahtar bulundu; tarama bozuk olabilir");
        var missing = used.Where(k => !Strings.Keys.Contains(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Çevirisi olmayan anahtarlar: " + string.Join(", ", missing));
    }

    [Fact]
    public void Language_resolution_and_cultures()
    {
        Assert.Equal("de", Strings.Resolve("de"));
        Assert.Contains(Strings.Resolve(""), Strings.Languages);
        Assert.Contains(Strings.Resolve("fr"), Strings.Languages);
        Assert.Equal("tr-TR", Strings.CultureOf("tr").Name);
        Assert.Equal("es-ES", Strings.CultureOf("es").Name);
        Assert.Equal("Örnek", Strings.Get("tr", "Örnek")); // bilinmeyen anahtar kendisi döner
        Assert.Equal("Arama dili", Strings.Get("tr", "SearchHelp"));
        Assert.Equal("Suchsyntax", Strings.Get("de", "SearchHelp"));
    }

    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ArsivAra.sln"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("ArsivAra.sln bulunamadı");
    }
}
