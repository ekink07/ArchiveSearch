using System.Text.Json;

namespace ArsivAra.Core.Tests;

public class AppPathsTests
{
    static readonly Func<string, bool> NotRemovable = _ => false, Removable = _ => true;
    static bool CanWrite(string dir) { Directory.CreateDirectory(dir); return true; }

    [Fact]
    public void Environment_variable_wins()
    {
        Assert.Equal(@"X:\veri", AppPaths.ResolveDataDir(@"X:\veri", Fx.TempDir(), Removable, @"C:\local", CanWrite));
    }

    [Fact]
    public void Plain_folder_uses_local_app_data_and_creates_nothing()
    {
        string exeDir = Fx.TempDir(); // İndirilenler gibi
        Assert.Equal(@"C:\local", AppPaths.ResolveDataDir(null, exeDir, NotRemovable, @"C:\local", CanWrite));
        Assert.Empty(Directory.GetFileSystemEntries(exeDir));
    }

    [Fact]
    public void Existing_portable_folder_is_kept()
    {
        string exeDir = Fx.TempDir();
        Directory.CreateDirectory(Path.Combine(exeDir, "ArsivAra-veri"));
        Assert.Equal(Path.Combine(exeDir, "ArsivAra-veri"), AppPaths.ResolveDataDir(null, exeDir, NotRemovable, @"C:\local", CanWrite));
    }

    [Theory]
    [InlineData("portable")]
    [InlineData("portable.txt")]
    public void Marker_file_enables_portable_mode(string marker)
    {
        string exeDir = Fx.TempDir();
        File.WriteAllText(Path.Combine(exeDir, marker), "");
        Assert.Equal(Path.Combine(exeDir, "ArsivAra-veri"), AppPaths.ResolveDataDir(null, exeDir, NotRemovable, @"C:\local", CanWrite));
    }

    [Fact]
    public void Usb_stick_is_portable_unless_read_only()
    {
        string exeDir = Fx.TempDir();
        Assert.Equal(Path.Combine(exeDir, "ArsivAra-veri"), AppPaths.ResolveDataDir(null, exeDir, Removable, @"C:\local", CanWrite));
        Assert.Equal(@"C:\local", AppPaths.ResolveDataDir(null, Fx.TempDir(), Removable, @"C:\local", _ => false));
    }
}

public class TempFilesTests
{
    [Fact]
    public void Removes_old_folders_keeps_recent_and_locked_files()
    {
        string root = Fx.TempDir();
        string old = Directory.CreateDirectory(Path.Combine(root, "eski")).FullName;
        string recent = Directory.CreateDirectory(Path.Combine(root, "yeni")).FullName;
        string locked = Directory.CreateDirectory(Path.Combine(root, "acik")).FullName;
        File.WriteAllBytes(Path.Combine(old, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(recent, "b.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(locked, "c.bin"), new byte[10]);
        var past = DateTime.UtcNow.AddDays(-5);
        foreach (var d in new[] { old, locked })
        {
            foreach (var f in Directory.GetFiles(d)) File.SetLastWriteTimeUtc(f, past);
            Directory.SetLastWriteTimeUtc(d, past);
        }

        Assert.Equal(1020, TempFiles.Size(root));
        long freed;
        using (new FileStream(Path.Combine(locked, "c.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
            freed = TempFiles.Clean(TimeSpan.FromDays(2), root);

        Assert.Equal(1000, freed);
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));
        Assert.True(File.Exists(Path.Combine(locked, "c.bin")));
        Assert.Equal(0, TempFiles.Clean(TimeSpan.FromDays(2), Path.Combine(root, "yok")));
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrip_and_old_files_get_defaults()
    {
        string file = Path.Combine(Fx.TempDir(), "settings.json");
        var s = new Settings { Language = "de", ScanDisks = true, IconSize = 64, SavedSearches = [new SavedSearch { Name = "Faturalar", Query = "fatura ext:pdf" }] };
        s.Save(file);
        var back = Settings.Load(file);
        Assert.Equal("de", back.Language);
        Assert.True(back.ScanDisks);
        Assert.Equal("fatura ext:pdf", back.SavedSearches.Single().Query);

        File.WriteAllText(file, """{ "Roots": [], "Excludes": [ "C:\\WINDOWS" ], "IconSize": 32 }"""); // 1.0.0 ayar dosyası
        var old = Settings.Load(file);
        Assert.Equal(32, old.IconSize);
        Assert.True(old.ReadNested);
        Assert.Equal("", old.Language);
        Assert.Empty(old.SavedSearches);

        File.WriteAllText(file, "{ bozuk json");
        Assert.Equal(16, Settings.Load(file).IconSize);
    }
}

[Collection("Parolalar")] // statik durum: başka testlerle paralel çalışmasın
public class PasswordStoreTests
{
    [Fact]
    public void Remembered_passwords_are_encrypted_on_disk()
    {
        string old = PasswordStore.FilePath;
        try
        {
            PasswordStore.FilePath = Path.Combine(Fx.TempDir(), "parolalar.bin");
            PasswordStore.ResetCache();

            PasswordStore.Set(@"D:\a.rar", "Gizli-Parola-123", remember: true);
            PasswordStore.Set(@"D:\b.7z", "SadeceOturum", remember: false);
            Assert.True(File.Exists(PasswordStore.FilePath));
            Assert.DoesNotContain("Gizli-Parola-123", File.ReadAllText(PasswordStore.FilePath));
            Assert.DoesNotContain("SadeceOturum", File.ReadAllText(PasswordStore.FilePath));

            PasswordStore.ResetCache(); // program yeniden açıldı
            Assert.Equal("Gizli-Parola-123", PasswordStore.Get(@"d:\A.RAR"));
            Assert.Null(PasswordStore.Get(@"D:\b.7z"));
            Assert.Equal(1, PasswordStore.SavedCount);

            var nested = new ArchiveRecord { Path = @"D:\a.rar", InnerPath = "ic.zip" };
            Assert.Equal("Gizli-Parola-123", PasswordStore.For(nested)); // iç arşiv dıştakinin parolasını dener

            PasswordStore.Remove(@"D:\a.rar");
            Assert.Null(PasswordStore.Get(@"D:\a.rar"));
            Assert.False(File.Exists(PasswordStore.FilePath));

            PasswordStore.Set(@"D:\c.zip", "x", remember: true);
            PasswordStore.ClearAll();
            Assert.Equal(0, PasswordStore.SavedCount);
        }
        finally
        {
            PasswordStore.FilePath = old;
            PasswordStore.ResetCache();
        }
    }

    [Fact]
    public void Dpapi_roundtrip()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new { a = "ğüşiöç" });
        var enc = PasswordStore.Protect(data);
        Assert.NotEqual(data, enc);
        Assert.Equal(data, PasswordStore.Unprotect(enc));
    }
}

public class FoldingTests
{
    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("ıstanbul", "istanbul")]
    [InlineData("Şükrü Çağlar Öğüt", "sukru caglar ogut")]
    [InlineData(@"Belgeler\Rapor", "belgeler/rapor")]
    [InlineData("Ærø", "ærø")]
    public void Folds(string input, string expected) => Assert.Equal(expected, Folding.Fold(input));

    [Fact]
    public void Length_is_preserved() => Assert.Equal("İĞÜŞÖÇ".Length, Folding.Fold("İĞÜŞÖÇ").Length);
}
