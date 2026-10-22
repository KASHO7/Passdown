using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 29: the local morning-message preference and per-date content files — safe defaults, round trips, never synced.</summary>
public sealed class FileMorningMessageStoresTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-morning-message-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    private string WriteRaw(string fileName, string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathFor(fileName), content);
        return PathFor(fileName);
    }

    [Fact]
    public void DefaultPaths_AreLocalAppData_NotTheSyncFolder()
    {
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "morning-message.json"), FileMorningMessagePreferenceStore.DefaultPath);
        Assert.Equal(Path.Combine(AppPaths.LocalApplicationDataDirectory, "morning-message-content.json"), FileMorningMessageContentStore.DefaultPath);
    }

    [Fact]
    public void Preferences_MissingFile_IsDefault_AndCreatesNothing()
    {
        Assert.Equal(MorningMessagePreferences.Default, new FileMorningMessagePreferenceStore(PathFor("morning-message.json")).Load());
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task Preferences_RoundTrip_GreetingOrderVisibilityModeAndPrices()
    {
        var preferences = MorningMessagePreferences.Default with
        {
            Greeting = "Gooooood Morning!",
            Layout = MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.BreakPlan).WithEnabled(MorningMessageSectionId.DapPlacement, false),
            DefaultCopyMode = TeamsCopyMode.Formatted,
            PriceSections = [ProductDisplaySection.DailyAdmission, ProductDisplaySection.SeasonPasses],
            RemindersAsBullets = true,
        };

        await new FileMorningMessagePreferenceStore(PathFor("morning-message.json")).SaveAsync(preferences);
        var loaded = new FileMorningMessagePreferenceStore(PathFor("morning-message.json")).Load();

        Assert.Equal(preferences, loaded);
        var json = File.ReadAllText(PathFor("morning-message.json"));
        Assert.Contains("\"Formatted\"", json, StringComparison.Ordinal); // stored by name
        Assert.Contains("\"BreakPlan\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{\"DefaultCopyMode\": 7}")]
    [InlineData("{\"DefaultCopyMode\": \"Sparkly\"}")]
    public void Preferences_MalformedFile_IsDefault(string content)
    {
        Assert.Equal(MorningMessagePreferences.Default, new FileMorningMessagePreferenceStore(WriteRaw("morning-message.json", content)).Load());
    }

    [Fact]
    public void Preferences_PartialFile_FillsMissingSectionsAndKeepsKnownValues()
    {
        var path = WriteRaw("morning-message.json", "{\"Greeting\": \"Hey team!\", \"Sections\": [{\"Id\": \"DailyFeature\", \"Enabled\": true}]}");

        var loaded = new FileMorningMessagePreferenceStore(path).Load();

        Assert.Equal("Hey team!", loaded.Greeting);
        // Missing sections are inserted at their default positions, so this partial file reads as the default order.
        Assert.Equal(MorningMessageLayout.Default, loaded.Layout);
        Assert.Equal(MorningMessagePreferences.DefaultPriceSections, loaded.PriceSections);
    }

    [Fact]
    public async Task Content_RoundTripsPerDate_AndDatesAreIndependent()
    {
        var store = new FileMorningMessageContentStore(PathFor("content.json"));
        var monday = new DateOnly(2026, 10, 19);
        var tuesday = new DateOnly(2026, 10, 20);
        var content = MorningMessageContent.Empty with { Reminders = "Hydrate\nSmile 🎢", BreakPlan = "Avery   10:30", OdNotes = "Ride 4 down", DapImageReminder = true };

        await store.SaveAsync(monday, content);

        Assert.Equal(content, await new FileMorningMessageContentStore(PathFor("content.json")).LoadAsync(monday));
        Assert.Equal(MorningMessageContent.Empty, await store.LoadAsync(tuesday));
    }

    [Fact]
    public async Task Content_ClearingADate_KeepsAnEmptyEntry_SoSyncCanTellClearedFromAgedOut()
    {
        // Phase 30 (was Phase 29's "saving empty removes the date"): a cleared date stays as an empty entry.
        var store = new FileMorningMessageContentStore(PathFor("content.json"));
        var date = new DateOnly(2026, 10, 19);
        await store.SaveAsync(date, MorningMessageContent.Empty with { Reminders = "x" });

        await store.SaveAsync(date, MorningMessageContent.Empty);

        Assert.Contains("2026-10-19", File.ReadAllText(PathFor("content.json")), StringComparison.Ordinal);
        Assert.Equal(MorningMessageContent.Empty, await store.LoadAsync(date));
        Assert.Equal(MorningMessageContent.Empty, (await store.LoadAllAsync())[date]);
        Assert.Null(await store.FindPreviousRemindersAsync(new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public async Task Content_SavingEmptyForANeverWrittenDate_WritesNothing()
    {
        var store = new FileMorningMessageContentStore(PathFor("content.json"));

        await store.SaveAsync(new DateOnly(2026, 10, 19), MorningMessageContent.Empty);

        Assert.False(File.Exists(PathFor("content.json")));
    }

    [Fact]
    public async Task Content_KeepsOnlyTheMostRecentDates()
    {
        var store = new FileMorningMessageContentStore(PathFor("content.json"));
        var start = new DateOnly(2026, 1, 1);
        for (var i = 0; i < FileMorningMessageContentStore.RetainedDates + 5; i++)
        {
            await store.SaveAsync(start.AddDays(i), MorningMessageContent.Empty with { Reminders = $"Day {i}" });
        }

        Assert.Equal(MorningMessageContent.Empty, await store.LoadAsync(start));
        Assert.Equal("Day 64", (await store.LoadAsync(start.AddDays(64))).Reminders);
    }

    [Fact]
    public async Task Content_FindPreviousReminders_IsTheMostRecentEarlierNonBlankDate()
    {
        var store = new FileMorningMessageContentStore(PathFor("content.json"));
        await store.SaveAsync(new DateOnly(2026, 10, 15), MorningMessageContent.Empty with { Reminders = "Old" });
        await store.SaveAsync(new DateOnly(2026, 10, 18), MorningMessageContent.Empty with { Reminders = "Newer" });
        await store.SaveAsync(new DateOnly(2026, 10, 19), MorningMessageContent.Empty with { BreakPlan = "no reminders" });
        await store.SaveAsync(new DateOnly(2026, 10, 25), MorningMessageContent.Empty with { Reminders = "Future" });

        var found = await store.FindPreviousRemindersAsync(new DateOnly(2026, 10, 22));

        Assert.Equal((new DateOnly(2026, 10, 18), "Newer"), found!.Value);
        Assert.Null(await store.FindPreviousRemindersAsync(new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public async Task Content_MalformedFile_ReadsEmpty_AndIsReplacedByTheNextSave()
    {
        var path = WriteRaw("content.json", "{ broken");
        var store = new FileMorningMessageContentStore(path);
        var date = new DateOnly(2026, 10, 22);

        Assert.Equal(MorningMessageContent.Empty, await store.LoadAsync(date));

        await store.SaveAsync(date, MorningMessageContent.Empty with { OdNotes = "Fixed" });
        Assert.Equal("Fixed", (await store.LoadAsync(date)).OdNotes);
    }
}
