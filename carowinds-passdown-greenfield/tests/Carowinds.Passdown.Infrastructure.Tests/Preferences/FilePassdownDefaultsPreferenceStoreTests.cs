using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Preferences;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>
/// Phase 31D: the local Passdown defaults file — built-in defaults when missing or unreadable, each value read
/// independently (missing never means "off"), round-trips across restarts, and lives outside any sync folder.
/// </summary>
public class FilePassdownDefaultsPreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-defaults-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor() => Path.Combine(_directory, "passdown-defaults.json");

    private string WriteRaw(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(), content);
        return PathFor();
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsBuiltIn_AndCreatesNothing()
    {
        Assert.Equal(PassdownDefaultSelections.BuiltIn, await new FilePassdownDefaultsPreferenceStore(PathFor()).LoadAsync());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTripsEveryValue_AcrossNewInstances()
    {
        var defaults = new PassdownDefaultSelections(false, true, false, true, true, false, true);

        await new FilePassdownDefaultsPreferenceStore(PathFor()).SaveAsync(defaults);

        Assert.Equal(defaults, await new FilePassdownDefaultsPreferenceStore(PathFor()).LoadAsync()); // a restart reads it back
    }

    [Fact]
    public async Task Load_MissingValue_UsesThatValuesBuiltInDefault_ButKeepsExplicitFalse()
    {
        WriteRaw("""{ "DailyAdmission": false, "Shows": true }""");

        var loaded = await new FilePassdownDefaultsPreferenceStore(PathFor()).LoadAsync();

        Assert.False(loaded.DailyAdmission); // explicit false kept
        Assert.True(loaded.Shows);
        Assert.True(loaded.OperationalNotes); // missing -> built-in (on), never "off"
        Assert.True(loaded.FastLane);
        Assert.False(loaded.SeasonPasses);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "OperationalNotes": "yes" }""")]
    [InlineData("null")]
    public async Task Load_UnreadableFile_ReturnsBuiltIn_AndLeavesTheFileAlone(string content)
    {
        var path = WriteRaw(content);

        Assert.Equal(PassdownDefaultSelections.BuiltIn, await new FilePassdownDefaultsPreferenceStore(path).LoadAsync());
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public async Task Save_WritesOnlyTheDefaultsFile_WithStableNames()
    {
        await new FilePassdownDefaultsPreferenceStore(PathFor()).SaveAsync(PassdownDefaultSelections.BuiltIn);

        Assert.Equal(["passdown-defaults.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        using var document = JsonDocument.Parse(File.ReadAllText(PathFor()));
        Assert.Equal(
            ["DailyAdmission", "FastLane", "Promotions", "SeasonPasses", "AddOns", "OperationalNotes", "Shows"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void DefaultPath_IsInLocalAppData_BesideTheOtherDevicePreferences()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "passdown-defaults.json"), FilePassdownDefaultsPreferenceStore.DefaultPath);
        Assert.Equal(Path.GetDirectoryName(FileAppearancePreferenceStore.DefaultPath), Path.GetDirectoryName(FilePassdownDefaultsPreferenceStore.DefaultPath));
    }
}

/// <summary>Phase 31D: section selections saved with a draft survive a real SQLite close/reopen (an app restart or update).</summary>
public class Phase31DSavedSelectionSqliteTests : IDisposable
{
    private readonly SqliteRepositoryTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationalNotesAndShowsSelection_SurviveReopen(bool includeOperationalNotes)
    {
        var date = new DateOnly(2026, 10, 31);
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            SectionSelection = PassdownSectionSelection.Default with { IncludeOperationalNotes = includeOperationalNotes },
            Shows = new Application.Shows.AssociateShowsSelection(!includeOperationalNotes, []),
        };
        await new SqliteAssociateDraftRepository(_fixture.ConnectionFactory).SaveAsync(
            new AssociateDraft(date, AssociateDraftPayload.CurrentSchemaVersion, JsonSerializer.Serialize(draft), DateTimeOffset.UtcNow));

        var stored = await new SqliteAssociateDraftRepository(_fixture.ReopenConnectionFactory()).GetAsync(date);
        var loaded = AssociateDraftPayloadSerializer.Deserialize(stored!.PayloadJson);

        Assert.Equal(includeOperationalNotes, loaded.SectionSelection.IncludeOperationalNotes);
        Assert.Equal(!includeOperationalNotes, loaded.Shows?.IncludeInPassdown ?? false);
    }
}
