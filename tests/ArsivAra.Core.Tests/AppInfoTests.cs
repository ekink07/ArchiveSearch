namespace ArsivAra.Core.Tests;

public class AppInfoTests
{
    [Fact]
    public void Publisher_credit_and_copyright()
    {
        Assert.Equal("Verlora Studios", AppInfo.Company);
        Assert.Equal("Created By MaledictusMaggot®", AppInfo.Credit);
        Assert.StartsWith("Copyright © 20", AppInfo.Copyright);
        Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
    }

    [Fact]
    public void License_texts_are_embedded()
    {
        Assert.StartsWith("MIT License", AppInfo.LicenseText);
        Assert.Contains("Permission is hereby granted, free of charge", AppInfo.LicenseText);
        Assert.Contains("Verlora Studios", AppInfo.ThirdPartyNotices);
        Assert.Contains("SharpCompress", AppInfo.ThirdPartyNotices);
        Assert.Contains(".NET Foundation", AppInfo.ThirdPartyNotices);
        Assert.DoesNotContain("\r\r", AppInfo.LicenseText); // consistent line endings
    }

    [Fact]
    public void Notices_cover_every_shipped_component()
    {
        // If a new NuGet package or source code under src/ThirdParty is added, its license must also be added to THIRD-PARTY-NOTICES.txt.
        string notices = AppInfo.ThirdPartyNotices;
        string src = Path.Combine(Fx.RepoRoot, "src");
        foreach (var csproj in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(csproj), @"PackageReference Include=""([^""]+)"""))
                Assert.Contains(m.Groups[1].Value, notices);

        var vendored = Directory.GetDirectories(Path.Combine(src, "ThirdParty"));
        Assert.NotEmpty(vendored);
        foreach (var dir in vendored)
        {
            Assert.True(File.Exists(Path.Combine(dir, "LICENSE.txt")), $"{dir}: LICENSE.txt missing");
            Assert.Contains($"src/ThirdParty/{Path.GetFileName(dir)}", notices);
        }
    }

    [Fact]
    public void UnRar_license_paragraph_is_kept()
    {
        // The unRAR license requires this paragraph to appear both in the license document and in the source code comments.
        const string paragraph = "UnRAR source code may be used in any software to handle";
        Assert.Contains(paragraph, AppInfo.ThirdPartyNotices);
        string comment = File.ReadAllText(Path.Combine(Fx.RepoRoot, "src", "ThirdParty", "SharpCompress", "Compressors", "Rar", "UnRarLicense.cs"));
        Assert.Contains(paragraph, comment);
        Assert.Contains("in source code comments of resulting package.", comment);
        foreach (var license in new[] { "Microsoft Public License", "Apache License", "BSD License", "Oleg Stepanischev", "Tobias K" })
            Assert.Contains(license, AppInfo.ThirdPartyNotices);
    }
}
