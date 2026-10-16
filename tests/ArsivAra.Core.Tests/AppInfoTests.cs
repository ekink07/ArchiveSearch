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
    public void Notices_cover_every_shipped_package()
    {
        // Yeni bir NuGet paketi eklenirse lisansı THIRD-PARTY-NOTICES.txt'e de yazılmalı.
        string core = File.ReadAllText(Path.Combine(Fx.RepoRoot, "src", "ArsivAra.Core", "ArsivAra.Core.csproj"));
        string app = File.ReadAllText(Path.Combine(Fx.RepoRoot, "src", "ArsivAra.App", "ArsivAra.App.csproj"));
        var packages = System.Text.RegularExpressions.Regex.Matches(core + app, @"PackageReference Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.Contains(p, AppInfo.ThirdPartyNotices));
    }
}
