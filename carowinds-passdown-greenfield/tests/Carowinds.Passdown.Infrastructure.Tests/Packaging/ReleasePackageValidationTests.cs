using System.IO.Compression;
using System.Text;

namespace Carowinds.Passdown.Infrastructure.Tests.Packaging;

/// <summary>
/// Phase 18B PKG-002 / PKG-001 regression: the release content rules (shared with the Windows
/// packaging script) accept a clean release and reject every forbidden category; our own Release
/// assemblies carry no build-machine path. Validating a real ZIP is opt-in
/// (<c>CAROWINDS_RELEASE_ZIP=&lt;path&gt;</c>) so the normal build never depends on a packaged artifact.
/// </summary>
public class ReleasePackageValidationTests
{
    /// <summary>Stand-in content: notice files get complete-looking license text (PKG-003 length/placeholder checks), everything else a short non-empty body.</summary>
    internal static byte[] SyntheticContent(string path) =>
        ReleasePackageValidator.Load().IsNoticeFile(path)
            ? Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Permission is hereby granted, free of charge, to any person obtaining a copy. ", 4)))
            : Encoding.UTF8.GetBytes("ok");

    private static Dictionary<string, Func<byte[]>> CleanRelease()
    {
        var validator = ReleasePackageValidator.Load();
        var files = validator.RequiredFiles.ToDictionary(path => path, path => (Func<byte[]>)(() => SyntheticContent(path)), StringComparer.OrdinalIgnoreCase);
        files["System.Text.Json.dll"] = () => [0x4D, 0x5A];
        files["Avalonia.Base.dll"] = () => [0x4D, 0x5A];
        return files;
    }

    private static IReadOnlyList<string> ValidateWith(string path, string content = "x", IReadOnlyList<string>? extraTokens = null)
    {
        var files = CleanRelease();
        files[path] = () => Encoding.UTF8.GetBytes(content);
        return ReleasePackageValidator.Load(extraTokens).Validate(files);
    }

    [Fact]
    public void RulesFile_IsTheOneTheWindowsPackagingScriptUses()
    {
        var script = File.ReadAllText(Path.Combine(ReleasePackageValidator.RepositoryRoot, "packaging", "windows", "Test-ReleasePackage.ps1"));
        var build = File.ReadAllText(Path.Combine(ReleasePackageValidator.RepositoryRoot, "packaging", "windows", "Build-PortableRelease.ps1"));

        Assert.Contains("release-content-rules.json", script);
        Assert.Contains("Test-ReleasePackage.ps1", build);
        Assert.True(File.Exists(ReleasePackageValidator.RulesPath));
    }

    [Fact]
    public void CleanRelease_HasNoViolations()
    {
        Assert.Empty(ReleasePackageValidator.Load().Validate(CleanRelease()));
    }

    [Theory]
    [InlineData("Carowinds.Passdown.Desktop.exe")]
    [InlineData("e_sqlite3.dll")]
    [InlineData("Enable Automatic Refresh.cmd")]
    [InlineData("tools/Install-ScheduledRefresh.ps1")]
    [InlineData("LatoFont/OFL.txt")]
    [InlineData("THIRD-PARTY-NOTICES.txt")]
    [InlineData("licenses/dotnet-THIRD-PARTY-NOTICES.txt")]
    [InlineData("licenses/Inter-OFL.txt")]
    [InlineData("licenses/QuestPDF-LICENSE.txt")]
    [InlineData("licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt")]
    public void MissingRequiredFile_IsAViolation(string required)
    {
        var files = CleanRelease();
        files.Remove(required);

        var violations = ReleasePackageValidator.Load().Validate(files);

        Assert.Contains(violations, v => v.Contains("missing required file", StringComparison.Ordinal) && v.Contains(required, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Carowinds.Passdown.Desktop.pdb")]
    [InlineData("Program.cs")]
    [InlineData("Carowinds.Passdown.Desktop.csproj")]
    [InlineData("Carowinds.Passdown.Infrastructure.Tests.dll")]
    [InlineData("xunit.core.dll")]
    [InlineData("testhost.dll")]
    [InlineData("carowinds-passdown.db")]
    [InlineData("carowinds-passdown.db-wal")]
    [InlineData("carowinds-passdown.db-shm")]
    [InlineData("app-20261010.log")]
    [InlineData("scheduled-refresh-20261010.log")]
    [InlineData("Carowinds-Operations-Diagnostics-20261010-1200.zip")]
    [InlineData("Carowinds-Operations-2026-09-19.pdf")]
    [InlineData("preview.pdf")]
    [InlineData("gsdll64.dll")]
    [InlineData("gswin64c.exe")]
    [InlineData(".DS_Store")]
    [InlineData("._Carowinds.Passdown.Desktop.dll")]
    [InlineData("libSkiaSharp.dylib")]
    [InlineData("libe_sqlite3.so")]
    [InlineData("sync-config.json")]
    [InlineData("device-id.txt")]
    [InlineData("manifest.json")]
    [InlineData("2026-09-19.json.tmp-0123456789abcdef0123456789abcdef")]
    [InlineData(".gitignore")]
    [InlineData("CLAUDE.md")]
    [InlineData("signing.pfx")]
    [InlineData("appsettings.Development.json")]
    [InlineData(".env")]
    public void ForbiddenFile_IsAViolation(string path)
    {
        Assert.Contains(ValidateWith(path), v => v.StartsWith("forbidden file", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("logs/readme.txt")]
    [InlineData("fixtures/park-hours.json")]
    [InlineData("TestData/x.json")]
    [InlineData("Carowinds Operations Sync/drafts/a.txt")]
    [InlineData(".git/HEAD")]
    [InlineData("obj/project.assets.json")]
    [InlineData("__MACOSX/x")]
    public void ForbiddenDirectory_IsAViolation(string path)
    {
        Assert.Contains(ValidateWith(path), v => v.StartsWith("forbidden directory", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("runtimes/osx-arm64/native/libSkiaSharp.bin")]
    [InlineData("runtimes/linux-x64/native/libe_sqlite3.bin")]
    [InlineData("runtimes/win-arm64/native/e_sqlite3.dll")]
    [InlineData("runtimes/maccatalyst-x64/native/x.bin")]
    public void NonWindowsX64NativeRuntime_IsAViolation(string path)
    {
        Assert.Contains(ValidateWith(path), v => v.StartsWith("non-win-x64 native runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void WinX64Runtime_IsAllowed()
    {
        Assert.Empty(ValidateWith("runtimes/win-x64/native/extra.dll"));
    }

    [Theory]
    [InlineData("Carowinds.Passdown.Application.dll", "/Users/jane/Documents/Passdown/src/x.pdb")]
    [InlineData("Carowinds.Passdown.Desktop.dll", "/home/dev/repo/obj/x.pdb")]
    [InlineData("README.txt", @"Extract to C:\Users\jane\Downloads")]
    [InlineData("Carowinds.Passdown.Desktop.deps.json", "\"path\": \"/Users/dev/.nuget\"")]
    public void DeveloperPathInOwnBinaryOrTextFile_IsAViolation(string path, string content)
    {
        Assert.Contains(ValidateWith(path, content), v => v.StartsWith("absolute developer path token", StringComparison.Ordinal));
    }

    [Fact]
    public void Utf16PathInOwnBinary_IsDetected()
    {
        var files = CleanRelease();
        files["Carowinds.Passdown.Domain.dll"] = () => [0x4D, 0x5A, .. Encoding.Unicode.GetBytes(@"C:\USERS\jane\x")];

        Assert.Contains(ReleasePackageValidator.Load().Validate(files), v => v.Contains("Carowinds.Passdown.Domain.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void ThirdPartyBinaryWithItsVendorsBuildPath_IsNotAFalsePositive_ButTheCheckoutPathAlwaysIs()
    {
        // SQLitePCLRaw and MicroCom genuinely ship their vendors' /Users/… and /home/… build paths.
        Assert.Empty(ValidateWith("SQLitePCLRaw.core.dll", "/Users/ericsink/dev/x.pdb"));

        var checkout = ReleasePackageValidator.RepositoryRoot;
        Assert.Contains(
            ValidateWith("SQLitePCLRaw.core.dll", checkout + "/src/x.pdb", extraTokens: [checkout]),
            v => v.StartsWith("absolute developer path token", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_ReadsZipsWithTheTopLevelReleaseFolderStripped()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"carowinds-release-validation-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var path in CleanRelease().Keys.Append("carowinds-passdown.db"))
                {
                    using var stream = archive.CreateEntry($"Carowinds-Operations-0.1.0-dev-win-x64/{path}").Open();
                    stream.Write(SyntheticContent(path));
                }
            }

            var violations = ReleasePackageValidator.Load().ValidateZip(zipPath);

            Assert.Equal(["forbidden file (*.db): carowinds-passdown.db"], violations);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

#if !DEBUG
    /// <summary>PKG-001 regression across every one of our own Release assemblies (not just one), via string scan of the binary.</summary>
    [Fact]
    public void OwnReleaseAssemblies_ContainNoDeveloperOrCheckoutPath()
    {
        var outputDirectory = AppContext.BaseDirectory;
        var ownBinaries = Directory.GetFiles(outputDirectory, "Carowinds.Passdown.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(ownBinaries, path => path.EndsWith("Carowinds.Passdown.Infrastructure.dll", StringComparison.Ordinal));

        var files = ownBinaries.ToDictionary(path => Path.GetFileName(path), path => (Func<byte[]>)(() => File.ReadAllBytes(path)));
        foreach (var required in ReleasePackageValidator.Load().RequiredFiles.Where(r => !files.ContainsKey(r)))
        {
            files[required] = () => SyntheticContent(required);
        }

        var violations = ReleasePackageValidator.Load([ReleasePackageValidator.RepositoryRoot, Environment.UserName.Length >= 3 ? "/" + Environment.UserName + "/" : ""])
            .Validate(files);

        Assert.Empty(violations);
    }
#endif

    [Theory]
    [InlineData("Carowinds.Passdown.Desktop.exe")]
    [InlineData("hostfxr.dll")]
    [InlineData("THIRD-PARTY-NOTICES.txt")]
    public void EmptyRequiredFile_IsAViolation(string required)
    {
        var files = CleanRelease();
        files[required] = () => [];

        Assert.Contains(ReleasePackageValidator.Load().Validate(files), v => v == $"empty required file: {required}");
    }

    [Theory]
    [InlineData("THIRD-PARTY-NOTICES.txt", "TODO")]
    [InlineData("THIRD-PARTY-NOTICES.txt", "TBD")]
    [InlineData("licenses/Inter-OFL.txt", "{{")]
    [InlineData("licenses/Apache-2.0.txt", "<insert")]
    [InlineData("LatoFont/OFL.txt", "<copyright>")]
    public void NoticeFileWithUnresolvedPlaceholder_IsAViolation(string path, string placeholder)
    {
        var files = CleanRelease();
        var body = Encoding.UTF8.GetString(SyntheticContent(path)) + $"\nCopyright {placeholder} holder\n";
        files[path] = () => Encoding.UTF8.GetBytes(body);

        Assert.Contains(ReleasePackageValidator.Load().Validate(files), v => v.StartsWith($"unresolved placeholder '{placeholder}'", StringComparison.Ordinal) && v.EndsWith(path, StringComparison.Ordinal));
    }

    [Fact]
    public void TruncatedNoticeFile_IsAViolation()
    {
        var files = CleanRelease();
        files["licenses/ANGLE-LICENSE.txt"] = () => "BSD"u8.ToArray();

        Assert.Contains(ReleasePackageValidator.Load().Validate(files), v => v.StartsWith("notice file too short", StringComparison.Ordinal));
    }

    [Fact]
    public void PlaceholderWordInANonNoticeFile_IsNotFlagged()
    {
        Assert.Empty(ValidateWith("README.txt", "TODO: nothing to do, this is the user README"));
    }

    [Fact]
    public void ZipWithDuplicateEntries_IsAViolation()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"carowinds-release-validation-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var path in CleanRelease().Keys.Append("Carowinds.Passdown.Desktop.dll"))
                {
                    using var stream = archive.CreateEntry($"Carowinds-Operations-0.1.0-dev-win-x64/{path}").Open();
                    stream.Write(SyntheticContent(path));
                }
            }

            Assert.Equal(["duplicate ZIP entry: Carowinds.Passdown.Desktop.dll"], ReleasePackageValidator.Load().ValidateZip(zipPath));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>
    /// PKG-003: the notice sources Build-PortableRelease.ps1 copies into the release
    /// (packaging/windows/THIRD-PARTY-NOTICES.txt and licenses/) are exactly the required set, each
    /// passes the notice-content rules, and every licenses\ file the index points to exists. Runs
    /// offline on every host — no packaged artifact needed.
    /// </summary>
    [Fact]
    public void RepositoryNoticeSources_MatchTheRequiredSet_AndAreComplete()
    {
        var validator = ReleasePackageValidator.Load();
        var packagingRoot = Path.Combine(ReleasePackageValidator.RepositoryRoot, "packaging", "windows");
        var licensesRoot = Path.Combine(packagingRoot, "licenses");

        var shipped = Directory.EnumerateFiles(licensesRoot, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != ".DS_Store")
            .Select(file => "licenses/" + Path.GetRelativePath(licensesRoot, file).Replace('\\', '/'))
            .Append("THIRD-PARTY-NOTICES.txt")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = validator.RequiredFiles
            .Where(path => path.StartsWith("licenses/", StringComparison.OrdinalIgnoreCase) || path == "THIRD-PARTY-NOTICES.txt")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(shipped.SetEquals(required), $"licenses/ and requiredFiles differ: {string.Join(", ", shipped.Except(required).Concat(required.Except(shipped)))}");

        foreach (var path in shipped)
        {
            Assert.True(validator.IsNoticeFile(path), path);
            Assert.Empty(validator.ValidateNoticeContent(path, File.ReadAllBytes(Path.Combine(packagingRoot, path))));
        }

        var index = File.ReadAllText(Path.Combine(packagingRoot, "THIRD-PARTY-NOTICES.txt"));
        var referenced = System.Text.RegularExpressions.Regex.Matches(index, @"licenses\\[A-Za-z0-9.\-]+(?:\\[A-Za-z0-9.\-]*)?")
            .Select(match => match.Value.Replace('\\', '/').TrimEnd('/'))
            .Distinct()
            .ToList();
        Assert.NotEmpty(referenced);
        foreach (var reference in referenced)
        {
            Assert.True(File.Exists(Path.Combine(packagingRoot, reference)) || Directory.Exists(Path.Combine(packagingRoot, reference)), $"THIRD-PARTY-NOTICES.txt points to missing {reference}");
        }

        Assert.Contains("LatoFont\\OFL.txt", index, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScript_ShipsTheThirdPartyNotices()
    {
        var build = File.ReadAllText(Path.Combine(ReleasePackageValidator.RepositoryRoot, "packaging", "windows", "Build-PortableRelease.ps1"));

        Assert.Contains("'THIRD-PARTY-NOTICES.txt'", build, StringComparison.Ordinal);
        Assert.Contains("'licenses'", build, StringComparison.Ordinal);
    }

    [Fact]
    public void RealReleaseZip_WhenSupplied_PassesEveryRule()
    {
        if (Environment.GetEnvironmentVariable("CAROWINDS_RELEASE_ZIP") is not { Length: > 0 } zipPath)
        {
            return; // Opt-in: run after Build-PortableRelease.ps1 with CAROWINDS_RELEASE_ZIP=<zip>.
        }

        Assert.Empty(ReleasePackageValidator.Load([ReleasePackageValidator.RepositoryRoot]).ValidateZip(zipPath));
    }
}
