using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>Phase 23D: safe release-note formatting, the bundled notes file, and the show-once What's New rule.</summary>
public class ReleaseNotesAndWhatsNewTests
{
    [Fact]
    public void Markdown_SubsetBecomesHeadingsBulletsAndParagraphs()
    {
        var doc = ReleaseNotesDocument.Parse("## Highlights\n\n- One\n* Two\nA paragraph\ncontinues here.\n\n### Fixes");
        Assert.Equal(
            [(ReleaseNotesBlockKind.Heading, "Highlights"), (ReleaseNotesBlockKind.Bullet, "One"), (ReleaseNotesBlockKind.Bullet, "Two"),
             (ReleaseNotesBlockKind.Paragraph, "A paragraph continues here."), (ReleaseNotesBlockKind.Heading, "Fixes")],
            doc.Blocks.Select(b => (b.Kind, b.Text)));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>Hello", "alert(1)Hello")]
    [InlineData("<img src=x onerror=alert(1)>Text", "Text")]
    [InlineData("See [the guide](https://evil.example/install.exe)", "See the guide")]
    [InlineData("![logo](https://evil.example/a.png) after", "logo after")]
    [InlineData("**Bold** and `code`", "Bold and code")]
    [InlineData("Tab\there\u0007bell", "Tabherebell")]
    public void UnsafeOrRichContent_IsReducedToPlainText(string input, string expected) =>
        Assert.Equal(expected, ReleaseNotesDocument.Parse(input).Blocks.Single().Text);

    [Fact]
    public void HostileSizes_AreBounded()
    {
        var huge = ReleaseNotesDocument.Parse(string.Join("\n", Enumerable.Repeat("- item", 5000)));
        Assert.True(huge.IsTruncated);
        Assert.True(huge.Blocks.Count <= ReleaseNotesDocument.MaximumBlocks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n")]
    public void MissingNotes_AreEmpty(string? input) => Assert.True(ReleaseNotesDocument.Parse(input).IsEmpty);

    [Fact]
    public void BundledFile_ParsesHeaderAndBody()
    {
        var notes = BundledReleaseNotesParser.Parse("Version: 0.1.0-rc.8\nTitle: Updates built in\nDate: 2026-10-20\n---\n- Updates from Settings");
        Assert.NotNull(notes);
        Assert.Equal("0.1.0-rc.8", notes.Version);
        Assert.Equal("Updates built in", notes.Title);
        Assert.Equal(new DateOnly(2026, 10, 20), notes.PublishedOn);
        Assert.Equal("Updates from Settings", notes.Document.Blocks.Single().Text);
    }

    [Theory]
    [InlineData("Title: x\n---\nbody")]
    [InlineData("Version: banana\nTitle: x\n---\nbody")]
    [InlineData("Version: 0.1.0\nTitle: x\nbody without separator")]
    [InlineData("Version: 0.1.0\n---\nno title")]
    public void BundledFile_InvalidHeader_IsNull(string text) => Assert.Null(BundledReleaseNotesParser.Parse(text));

    private static UpdateInstallOutcome Outcome(UpdateInstallStatus status, string to = "0.1.0-rc.8") =>
        new(Guid.NewGuid(), "0.1.0-rc.7", to, status, null, DateTimeOffset.UtcNow, null);

    private static readonly SemanticVersion Running = SemanticVersion.Parse("0.1.0-rc.8");

    [Fact]
    public void WhatsNew_ShowsAfterConfirmedSuccessfulUpdate() =>
        Assert.True(WhatsNewPolicy.ShouldShowAfterUpdate(Outcome(UpdateInstallStatus.Succeeded), Running, "0.1.0-rc.7"));

    [Fact]
    public void WhatsNew_NotRepeatedOnceAcknowledged() =>
        Assert.False(WhatsNewPolicy.ShouldShowAfterUpdate(Outcome(UpdateInstallStatus.Succeeded), Running, "0.1.0-rc.8"));

    [Fact]
    public void WhatsNew_NextUpdateShowsItsOwnNotes() =>
        Assert.True(WhatsNewPolicy.ShouldShowAfterUpdate(Outcome(UpdateInstallStatus.Succeeded, "0.1.0-rc.9"), SemanticVersion.Parse("0.1.0-rc.9"), "0.1.0-rc.8"));

    [Fact]
    public void WhatsNew_CleanInstallNeverShowsAutomatically() =>
        Assert.False(WhatsNewPolicy.ShouldShowAfterUpdate(null, Running, null));

    [Theory]
    [InlineData(UpdateInstallStatus.RolledBack)]
    [InlineData(UpdateInstallStatus.NotInstalled)]
    [InlineData(UpdateInstallStatus.RecoveryRequired)]
    [InlineData(UpdateInstallStatus.AwaitingLaunchConfirmation)]
    [InlineData(UpdateInstallStatus.InProgress)]
    public void WhatsNew_FailedOrUnconfirmedUpdateNeverShows(UpdateInstallStatus status) =>
        Assert.False(WhatsNewPolicy.ShouldShowAfterUpdate(Outcome(status), Running, null));

    [Fact]
    public void WhatsNew_SuccessRecordForAnotherVersionDoesNotShow() =>
        Assert.False(WhatsNewPolicy.ShouldShowAfterUpdate(Outcome(UpdateInstallStatus.Succeeded, "0.1.0-rc.9"), Running, null));

    [Fact]
    public void ConfigurationParser_InvalidValuesDisableUpdates()
    {
        Assert.False(UpdateConfigurationParser.Parse("owner/repo", "ReleaseCandidate", []).IsConfigured);
        Assert.False(UpdateConfigurationParser.Parse("not a repo", "ReleaseCandidate", [("k", "v")]).IsConfigured);
        Assert.False(UpdateConfigurationParser.Parse("owner/repo/extra", "ReleaseCandidate", [("k", "v")]).IsConfigured);
        Assert.False(UpdateConfigurationParser.Parse("owner/repo", "Nightly", [("k", "v")]).IsConfigured);
        Assert.False(UpdateConfigurationParser.Parse("../repo", "Stable", [("k", "v")]).IsConfigured);

        var configured = UpdateConfigurationParser.Parse("KASHO7/Passdown", "Stable", [("k", "v"), (null, "x"), ("y", " ")]);
        Assert.True(configured.IsConfigured);
        Assert.Equal(UpdateChannel.Stable, configured.Channel);
        Assert.Single(configured.TrustedKeys);
    }
}
