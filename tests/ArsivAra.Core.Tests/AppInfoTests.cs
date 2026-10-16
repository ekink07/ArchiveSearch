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
        Assert.DoesNotContain("\r\r", AppInfo.LicenseText); // satır sonları tek tip
    }

    [Fact]
    public void Notices_cover_every_shipped_component()
    {
        // Yeni bir NuGet paketi ya da src/ThirdParty altına kaynak kod eklenirse lisansı THIRD-PARTY-NOTICES.txt'e de yazılmalı.
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
            Assert.True(File.Exists(Path.Combine(dir, "LICENSE.txt")), $"{dir}: LICENSE.txt yok");
            Assert.Contains($"src/ThirdParty/{Path.GetFileName(dir)}", notices);
        }
    }

    [Fact]
    public void UnRar_license_paragraph_is_kept()
    {
        // unRAR lisansı bu paragrafın hem lisans belgesinde hem de kaynak kodun yorumlarında bulunmasını şart koşar.
        const string paragraph = "UnRAR source code may be used in any software to handle";
        Assert.Contains(paragraph, AppInfo.ThirdPartyNotices);
        string comment = File.ReadAllText(Path.Combine(Fx.RepoRoot, "src", "ThirdParty", "SharpCompress", "Compressors", "Rar", "UnRarLicense.cs"));
        Assert.Contains(paragraph, comment);
        Assert.Contains("in source code comments of resulting package.", comment);
        foreach (var license in new[] { "Microsoft Public License", "Apache License", "BSD License", "Oleg Stepanischev", "Tobias K" })
            Assert.Contains(license, AppInfo.ThirdPartyNotices);
    }
}
