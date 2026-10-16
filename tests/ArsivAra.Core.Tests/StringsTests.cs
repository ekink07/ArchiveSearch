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
            Assert.True(placeholders.Count == 1, $"{key}: placeholders differ between languages: {string.Join(" | ", placeholders)}");
            foreach (var t in texts) _ = string.Format(t, "a", "b", "c"); // must not throw a format error
        }
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        string app = Path.Combine(Fx.RepoRoot, "src", "ArsivAra.App");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".xaml") || file.EndsWith(".cs")) || file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
            if (Path.GetFileName(file) == "Loc.cs") continue; // documentation examples ("Key")
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\{l:T (\w+)\}")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Loc\.[TF]\(""(\w+)""")) used.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"""(Col(?:Name|Folder|Size|Modified|Archive|Type)|QueryError\w+|Status\w+|Day\w*)"""))
                used.Add(m.Groups[1].Value);
        }
        Assert.True(used.Count > 80, $"Only {used.Count} keys found; the scan may be broken");
        var missing = used.Where(k => !Strings.Keys.Contains(k)).Order().ToList();
        Assert.True(missing.Count == 0, "Keys without translations: " + string.Join(", ", missing));
    }

    [Fact]
    public void Language_resolution_and_cultures()
    {
        Assert.Equal("de", Strings.Resolve("de"));
        Assert.Contains(Strings.Resolve(""), Strings.Languages);
        Assert.Contains(Strings.Resolve("fr"), Strings.Languages);
        Assert.Equal("en", Strings.Resolve(""));
        Assert.Equal("en", Strings.Resolve(null));
        Assert.Contains(Strings.Resolve(Strings.SystemLanguage), Strings.Languages);
        Assert.Equal("tr-TR", Strings.CultureOf("tr").Name);
        Assert.Equal("es-ES", Strings.CultureOf("es").Name);
        Assert.Equal("Örnek", Strings.Get("tr", "Örnek")); // an unknown key returns itself
        Assert.Equal("Arama dili", Strings.Get("tr", "SearchHelp"));
        Assert.Equal("Suchsyntax", Strings.Get("de", "SearchHelp"));
    }
}
