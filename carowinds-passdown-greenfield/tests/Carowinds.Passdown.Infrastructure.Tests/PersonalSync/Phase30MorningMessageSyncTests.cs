using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Carowinds.Passdown.Infrastructure.Preferences;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 30: optional Morning Message sync between simulated computers — each with its own real local files
/// (content, preferences, sync state, sync configuration) and one shared Personal Sync folder in a temp
/// directory. Synthetic data only.
/// </summary>
public sealed class Phase30MorningMessageSyncTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Oct22 = new(2026, 10, 22);
    private static readonly DateOnly Oct23 = new(2026, 10, 23);

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"carowinds-phase30-{Guid.NewGuid():N}");

    public Phase30MorningMessageSyncTests() => Directory.CreateDirectory(Root);

    private string Root => Path.Combine(_workspace, "shared");

    private string MessageFolder => SyncPathService.GetMorningMessageDirectoryPath(Root);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(_workspace, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(path, Directory.Exists(path) ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                catch (IOException)
                {
                }
            }

            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class Device
    {
        private readonly string _directory;

        public Device(string directory, Guid id, CapturingLogger? log = null)
        {
            _directory = directory;
            Id = id;
            Directory.CreateDirectory(directory);
            Config = new FileSyncConfigurationStore(Path.Combine(directory, "sync-config.json"));
            Content = new FileMorningMessageContentStore(Path.Combine(directory, "morning-message-content.json"));
            Preferences = new FileMorningMessagePreferenceStore(Path.Combine(directory, "morning-message.json"));
            State = new MorningMessageSyncStateStore(Path.Combine(directory, "morning-message-sync-state.json"));
            Session = new MorningMessageEditorSession();
            Sync = new MorningMessageSync(Config, Content, Preferences, State, Session, new FixedDeviceId(id), log);
        }

        public Guid Id { get; }

        public FileSyncConfigurationStore Config { get; }

        public FileMorningMessageContentStore Content { get; }

        public FileMorningMessagePreferenceStore Preferences { get; }

        public MorningMessageSyncStateStore State { get; }

        public MorningMessageEditorSession Session { get; }

        public MorningMessageSync Sync { get; }

        public string ContentPath => Content.FilePath;

        public string StatePath => State.FilePath;

        public Task<MorningMessageSyncResult> RunAsync(string root) => Sync.RunAsync(root, Id, Now);

        public Task SaveAsync(DateOnly date, MorningMessageContent content) => Content.SaveAsync(date, content);

        public Task<MorningMessageContent> LoadAsync(DateOnly date) => Content.LoadAsync(date);

        /// <summary>The same computer after an app restart: new objects over the same files.</summary>
        public Device Restart() => new(_directory, Id);
    }

    private async Task<Device> CreateAsync(string name, bool enabled = true, Guid? id = null)
    {
        var device = new Device(Path.Combine(_workspace, name), id ?? Guid.NewGuid());
        await device.Config.SaveAsync(new SyncConfiguration(true, Root, MorningMessageSyncEnabled: enabled));
        return device;
    }

    private static MorningMessageContent Text(string reminders = "", string breakPlan = "", string odNotes = "", string priceNote = "", string dap = "", bool dapReminder = false) =>
        new(reminders, breakPlan, dap, dapReminder, null, odNotes, priceNote);

    private static async Task SyncAllAsync(string root, params Device[] devices)
    {
        // Two rounds: every computer sees every other computer's latest file.
        for (var round = 0; round < 2; round++)
        {
            foreach (var device in devices)
            {
                await device.RunAsync(root);
            }
        }
    }

    private Dictionary<string, byte[]> SharedSnapshot() =>
        Directory.Exists(MessageFolder) ? Directory.GetFiles(MessageFolder).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes) : [];

    // ----- Part B: the optional, per-computer setting -----

    [Fact]
    public async Task Setting_IsOffByDefault_IncludingForAConfigurationFileWrittenBeforePhase30()
    {
        Assert.False(SyncConfiguration.Disabled.MorningMessageSyncEnabled);
        Assert.False(new SyncConfiguration(true, Root).MorningMessageSyncEnabled);

        var path = Path.Combine(_workspace, "legacy-sync-config.json");
        await File.WriteAllTextAsync(path, """{ "PersonalSyncEnabled": true, "SyncRootPath": "/Users/associate/OneDrive", "LastKnownSyncSpaceId": "4a6d2c9e-58f4-4d3b-8a51-8e3f0c6b1d22" }""");
        var loaded = await new FileSyncConfigurationStore(path).LoadAsync();

        Assert.True(loaded.PersonalSyncEnabled);
        Assert.Equal("/Users/associate/OneDrive", loaded.SyncRootPath);
        Assert.False(loaded.MorningMessageSyncEnabled);
    }

    [Fact]
    public async Task Setting_SurvivesRestart_AndNeverResetsTheFolderOrOtherPersonalSyncSettings()
    {
        var device = new Device(Path.Combine(_workspace, "a"), Guid.NewGuid());
        var spaceId = Guid.NewGuid();
        await device.Config.SaveAsync(new SyncConfiguration(true, Root, spaceId));

        await device.Sync.SetEnabledAsync(true);
        var restarted = device.Restart();
        var afterRestart = await restarted.Config.LoadAsync();

        Assert.Equal(new SyncConfiguration(true, Root, spaceId, MorningMessageSyncEnabled: true), afterRestart);

        // Changing Personal Sync's own settings keeps the Morning Message choice.
        var settings = new PersonalSyncSettingsService(restarted.Config, new SyncFolderValidator(), new NoSuggestions(), new FixedDeviceId(restarted.Id), new FixedClock());
        await settings.SetSyncRootAsync(Root);
        await settings.SetEnabledAsync(false);
        await settings.SetEnabledAsync(true);
        Assert.True((await restarted.Config.LoadAsync()).MorningMessageSyncEnabled);

        await restarted.Sync.SetEnabledAsync(false);
        Assert.Equal(new SyncConfiguration(true, Root, spaceId, MorningMessageSyncEnabled: false), await restarted.Config.LoadAsync());
    }

    [Fact]
    public async Task ComputerWithTheOptionOff_NeitherImportsNorPublishes_AndKeepsItsOwnMessages()
    {
        var a = await CreateAsync("a", enabled: true);
        var b = await CreateAsync("b", enabled: false);
        await a.SaveAsync(Oct22, Text(reminders: "From A"));
        await b.SaveAsync(Oct22, Text(reminders: "B's own"));
        await a.RunAsync(Root);
        var shared = SharedSnapshot();
        var bBefore = File.ReadAllBytes(b.ContentPath);

        var result = await b.RunAsync(Root);

        Assert.False(result.Ran);
        Assert.Equal(bBefore, File.ReadAllBytes(b.ContentPath));
        Assert.False(File.Exists(b.StatePath));
        Assert.False(File.Exists(SyncPathService.GetMorningMessageFilePath(Root, b.Id)));
        Assert.Equal(shared.Keys, SharedSnapshot().Keys);
        Assert.Single(shared);
    }

    [Fact]
    public async Task ComputerEnabledLater_JoinsTheSharedMessages_WithoutLosingItsOwn()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b", enabled: false);
        await a.SaveAsync(Oct22, Text(reminders: "A on the 22nd"));
        await b.SaveAsync(Oct23, Text(reminders: "B on the 23rd"));
        await a.RunAsync(Root);
        await b.RunAsync(Root);

        await b.Sync.SetEnabledAsync(true);
        await SyncAllAsync(Root, b, a);

        Assert.Equal("A on the 22nd", (await b.LoadAsync(Oct22)).Reminders);
        Assert.Equal("B on the 23rd", (await b.LoadAsync(Oct23)).Reminders);
        Assert.Equal("B on the 23rd", (await a.LoadAsync(Oct23)).Reminders);
    }

    [Fact]
    public async Task TurningTheOptionOff_DeletesNothing_LocalSharedOrHistory()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Shared", odNotes: "OD"));
        await SyncAllAsync(Root, a, b);
        var shared = SharedSnapshot();
        var local = File.ReadAllBytes(a.ContentPath);
        var state = File.ReadAllBytes(a.StatePath);

        await a.Sync.SetEnabledAsync(false);
        var result = await a.RunAsync(Root);

        Assert.False(result.Ran);
        Assert.Equal(2, shared.Count);
        Assert.Equal(shared.Keys.Order(), SharedSnapshot().Keys.Order());
        Assert.All(shared, entry => Assert.Equal(entry.Value, SharedSnapshot()[entry.Key]));
        Assert.Equal(local, File.ReadAllBytes(a.ContentPath));
        Assert.Equal(state, File.ReadAllBytes(a.StatePath));
        Assert.Equal("Shared", (await b.LoadAsync(Oct22)).Reminders);
    }

    [Fact]
    public async Task DisableEditReEnable_WithAConflictingSharedEdit_IsDetected_AndNothingIsLost()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Baseline", breakPlan: "Plan"));
        await SyncAllAsync(Root, a, b);

        await a.Sync.SetEnabledAsync(false);
        await a.SaveAsync(Oct22, Text(reminders: "Edited on A while off", breakPlan: "Plan"));
        await b.SaveAsync(Oct22, Text(reminders: "Edited on B", breakPlan: "Plan"));
        await b.RunAsync(Root);
        await a.RunAsync(Root); // off: nothing happens
        Assert.Equal("Edited on A while off", (await a.LoadAsync(Oct22)).Reminders);

        await a.Sync.SetEnabledAsync(true);
        var result = await a.RunAsync(Root);

        Assert.Equal(1, result.ConflictCount);
        Assert.Equal("Edited on A while off", (await a.LoadAsync(Oct22)).Reminders);
        var conflict = Assert.Single(await a.Sync.GetConflictsAsync());
        Assert.Equal(Oct22, conflict.OperatingDate);
        var field = Assert.Single(conflict.Fields);
        Assert.Equal(("Edited on A while off", "Edited on B"), (field.ThisDevice, field.OtherDevice));

        // B, too, sees the independent edit — and keeps its own text until someone chooses.
        await b.RunAsync(Root);
        Assert.Equal("Edited on B", (await b.LoadAsync(Oct22)).Reminders);
        Assert.Single(await b.Sync.GetConflictsAsync());
    }

    // ----- Part C: what synchronizes -----

    [Fact]
    public async Task EveryDailyField_AndEveryPreference_Synchronize_WithUnicodeMultilineAndSpecialCharacters()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var content = new MorningMessageContent(
            "Hydrate 💧\r\n- Smile at guests\n\nNew paragraph — é ñ 中文",
            "Avery    10:30\nJordan   11:00\n<Kim> & \"Lee\"   11:30",
            "Lot B, row 4",
            true,
            "/Users/a/Pictures/dap.png",
            "OD only: ride 4 down 'until noon'",
            "Fast Lane note: <b>not</b> a price & never official");
        await a.SaveAsync(Oct22, content);
        await a.SaveAsync(Oct23, Text(breakPlan: "Second date"));
        var preferences = new MorningMessagePreferences(
            "Gooood Morning! ☀️",
            MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.BreakPlan).WithEnabled(MorningMessageSectionId.DapPlacement, false),
            TeamsCopyMode.Formatted,
            [ProductDisplaySection.DailyAdmission, ProductDisplaySection.SeasonPasses],
            RemindersAsBullets: true);
        await a.Preferences.SaveAsync(preferences);

        await SyncAllAsync(Root, a, b);

        Assert.Equal(content with { DapImagePath = null }, await b.LoadAsync(Oct22)); // the local image path never travels
        Assert.Equal("Second date", (await b.LoadAsync(Oct23)).BreakPlan);
        Assert.Equal(preferences, b.Preferences.Load());
        Assert.Equal("/Users/a/Pictures/dap.png", (await a.LoadAsync(Oct22)).DapImagePath);
        Assert.Empty(await a.Sync.GetConflictsAsync());
        Assert.Empty(await b.Sync.GetConflictsAsync());
    }

    [Fact]
    public async Task SharedFile_HoldsOnlyMessageContent_KeyedByOperatingDate_WithNoPersonalIdentifiers()
    {
        var a = await CreateAsync("a");
        var content = Text(reminders: "R", odNotes: "OD") with { DapImagePath = "/Users/someone/Desktop/secret-dap.png" };
        await a.SaveAsync(Oct22, content);

        await a.RunAsync(Root);

        var path = SyncPathService.GetMorningMessageFilePath(Root, a.Id);
        var json = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "DeviceId", "Format", "FormatVersion", "Preferences", "Dates" }.Order(), document.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(MorningMessageSyncFileSerializer.FormatName, document.RootElement.GetProperty("Format").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal(["2026-10-22"], document.RootElement.GetProperty("Dates").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("secret-dap", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Operations", json, StringComparison.Ordinal); // no rendered message, no official data
        Assert.False(File.Exists(Path.Combine(MessageFolder, "manifest.json")));
    }

    [Fact]
    public async Task ClearingAField_Synchronizes_AsAnEmptyValue_WhileAFieldMissingFromTheFileChangesNothing()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Reminder", breakPlan: "Plan"));
        await SyncAllAsync(Root, a, b);

        await a.SaveAsync(Oct22, Text(reminders: string.Empty, breakPlan: "Plan"));
        await SyncAllAsync(Root, a, b);
        Assert.Equal(Text(breakPlan: "Plan"), await b.LoadAsync(Oct22));

        // Clearing every field keeps the (empty) date and synchronizes as a clear, never a deletion elsewhere.
        await a.SaveAsync(Oct22, MorningMessageContent.Empty);
        await SyncAllAsync(Root, a, b);
        Assert.Equal(MorningMessageContent.Empty, await b.LoadAsync(Oct22));
        Assert.True((await b.Content.LoadAllAsync()).ContainsKey(Oct22));

        // A file from a third computer that only carries one field leaves every other field alone.
        var c = Guid.NewGuid();
        await WriteForeignFileAsync(c, $$"""
            { "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "{{c:D}}",
              "Dates": { "2026-10-23": { "OdNotes": { "Value": "Only OD", "Version": { "{{c:D}}": 1 } } } } }
            """);
        await b.SaveAsync(Oct23, Text(reminders: "B keeps this"));
        await b.RunAsync(Root);
        Assert.Equal(Text(reminders: "B keeps this", odNotes: "Only OD"), await b.LoadAsync(Oct23));
    }

    [Fact]
    public async Task OfficialData_IsNeverSynchronized_OnlyMessageText()
    {
        // The synchronized field list is closed: nothing for hours, attendance, pre-solds, prices, the Daily Feature or a rendered message.
        Assert.Equal(
            ["BreakPlan", "DapImageReminder", "DapPlacement", "OdNotes", "PriceNote", "Reminders"],
            MorningMessageSyncFields.ContentFields.Order());
        Assert.Equal(
            ["DefaultCopyMode", "Greeting", "PriceSections", "RemindersAsBullets", "Sections"],
            MorningMessageSyncFields.PreferenceFields.Order());

        var a = await CreateAsync("a");
        await WriteForeignFileAsync(Guid.Parse("11111111-2222-3333-4444-555555555555"), """
            { "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "11111111-2222-3333-4444-555555555555",
              "Dates": { "2026-10-22": {
                  "ParkHours": { "Value": "10 AM - 2 AM", "Version": { "11111111-2222-3333-4444-555555555555": 1 } },
                  "RenderedMessage": { "Value": "Good Morning! Operations for…", "Version": { "11111111-2222-3333-4444-555555555555": 1 } } } } }
            """);

        var result = await a.RunAsync(Root);

        Assert.Equal(0, result.DatesUpdated);
        Assert.Equal(0, result.RejectedValues); // unknown fields from a newer version are ignored, not errors
        Assert.False(File.Exists(a.ContentPath));
    }

    [Fact]
    public async Task SixtyDayRetention_IsRespected_AndOlderDatesAreNeverPulledBackIn()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var start = new DateOnly(2026, 8, 1);
        for (var i = 0; i < 60; i++)
        {
            await b.SaveAsync(start.AddDays(i), Text(reminders: $"Old {i}"));
            await a.SaveAsync(start.AddDays(60 + i), Text(reminders: $"New {i}"));
        }

        await SyncAllAsync(Root, a, b);
        var aDates = (await a.Content.LoadAllAsync()).Keys.Order().ToList();
        var bDates = (await b.Content.LoadAllAsync()).Keys.Order().ToList();

        Assert.Equal(60, aDates.Count);
        Assert.Equal(start.AddDays(60), aDates[0]); // A kept its own newer 60; nothing older came in
        Assert.Equal(60, bDates.Count);
        Assert.Equal(aDates, bDates);

        // Further passes are quiet: no ping-pong of dates aging in and out.
        var again = await a.RunAsync(Root);
        var againB = await b.RunAsync(Root);
        Assert.Equal(0, again.DatesUpdated + againB.DatesUpdated);
        Assert.False(again.Exported || againB.Exported);
    }

    // ----- Part E: conflicts -----

    [Fact]
    public async Task SameDate_IdenticalContentOnBothComputers_FirstSync_IsNotAConflict()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Same", breakPlan: "Same plan"));
        await b.SaveAsync(Oct22, Text(reminders: "Same", breakPlan: "Same plan"));

        await SyncAllAsync(Root, a, b);

        Assert.Empty(await a.Sync.GetConflictsAsync());
        Assert.Empty(await b.Sync.GetConflictsAsync());
    }

    [Fact]
    public async Task IndependentFieldEdits_FromACommonBaseline_AreBothKept()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "R0", breakPlan: "P0"));
        await SyncAllAsync(Root, a, b);

        await a.SaveAsync(Oct22, Text(reminders: "Reminders edited on A", breakPlan: "P0"));
        await b.SaveAsync(Oct22, Text(reminders: "R0", breakPlan: "Break plan edited on B"));
        await SyncAllAsync(Root, a, b);

        var expected = Text(reminders: "Reminders edited on A", breakPlan: "Break plan edited on B");
        Assert.Equal(expected, await a.LoadAsync(Oct22));
        Assert.Equal(expected, await b.LoadAsync(Oct22));
        Assert.Empty(await a.Sync.GetConflictsAsync());
        Assert.Empty(await b.Sync.GetConflictsAsync());
    }

    [Fact]
    public async Task SameFieldEditedDifferently_IsAConflict_NeitherIsOverwritten_AndOtherFieldsStillMerge()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Parking meeting", breakPlan: "P0"));
        await SyncAllAsync(Root, a, b);

        await a.SaveAsync(Oct22, Text(reminders: "Parking meeting at 9:00.", breakPlan: "P0"));
        await b.SaveAsync(Oct22, Text(reminders: "Parking meeting at 9:30.", breakPlan: "Plan from B"));
        await SyncAllAsync(Root, a, b);

        Assert.Equal(Text(reminders: "Parking meeting at 9:00.", breakPlan: "Plan from B"), await a.LoadAsync(Oct22));
        Assert.Equal(Text(reminders: "Parking meeting at 9:30.", breakPlan: "Plan from B"), await b.LoadAsync(Oct22));
        var conflict = Assert.Single(await a.Sync.GetConflictsAsync());
        Assert.Equal(MorningMessageSyncFields.Reminders, Assert.Single(conflict.Fields).Field);
        Assert.Equal("Parking meeting at 9:30.", conflict.Fields[0].OtherDevice);

        // The losing text is preserved across restarts until resolved.
        var restarted = a.Restart();
        Assert.Equal("Parking meeting at 9:30.", Assert.Single(await restarted.Sync.GetConflictsAsync()).Fields[0].OtherDevice);
    }

    [Theory]
    [InlineData(MorningMessageConflictResolution.UseOtherDevice, "From A")]
    [InlineData(MorningMessageConflictResolution.KeepThisDevice, "From B")]
    public async Task AConflictResolvedOnOneComputer_IsAdoptedByTheOther(MorningMessageConflictResolution choiceOnB, string expected)
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(odNotes: "From A"));
        await b.SaveAsync(Oct22, Text(odNotes: "From B"));
        await SyncAllAsync(Root, a, b);
        Assert.Single(await a.Sync.GetConflictsAsync());
        Assert.Single(await b.Sync.GetConflictsAsync());

        Assert.Equal(MorningMessageResolveOutcome.Resolved, await b.Sync.ResolveAsync(Oct22, choiceOnB));
        await SyncAllAsync(Root, b, a);

        Assert.Equal(expected, (await a.LoadAsync(Oct22)).OdNotes);
        Assert.Equal(expected, (await b.LoadAsync(Oct22)).OdNotes);
        Assert.Empty(await a.Sync.GetConflictsAsync());
        Assert.Empty(await b.Sync.GetConflictsAsync());
        Assert.Equal(MorningMessageResolveOutcome.NoConflictFound, await b.Sync.ResolveAsync(Oct22, choiceOnB));
    }

    [Fact]
    public async Task DifferentSectionOrders_OnFirstSync_AreAPreferencesConflict_NotSilentlyOverwritten()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var c = await CreateAsync("c");
        var aPrefs = MorningMessagePreferences.Default with { Layout = MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.BreakPlan) };
        var bPrefs = MorningMessagePreferences.Default with { Layout = MorningMessageLayout.Default.MoveDown(MorningMessageSectionId.Greeting), Greeting = "Hi team" };
        await a.Preferences.SaveAsync(aPrefs);
        await b.Preferences.SaveAsync(bPrefs);

        await SyncAllAsync(Root, a, b);

        Assert.Equal(aPrefs with { Greeting = "Hi team" }, a.Preferences.Load()); // greeting was only changed on B: adopted
        Assert.Equal(bPrefs, b.Preferences.Load()); // B's order stays until someone chooses
        var conflict = Assert.Single(await a.Sync.GetConflictsAsync());
        Assert.Null(conflict.OperatingDate);
        Assert.Equal("Morning message preferences", conflict.Title);
        Assert.Equal(MorningMessageSyncFields.Sections, Assert.Single(conflict.Fields).Field);

        // A computer still on fresh-install preferences simply adopts what is shared (here: whichever order
        // it sees first, with the other kept for review — never a silent drop).
        await c.RunAsync(Root);
        Assert.Equal("Hi team", c.Preferences.Load().Greeting);

        Assert.Equal(MorningMessageResolveOutcome.Resolved, await a.Sync.ResolveAsync(null, MorningMessageConflictResolution.KeepThisDevice));
        await SyncAllAsync(Root, a, b, c);
        Assert.Equal(aPrefs.Layout, b.Preferences.Load().Layout);
        Assert.Equal(aPrefs.Layout, c.Preferences.Load().Layout);
        Assert.Empty(await b.Sync.GetConflictsAsync());
    }

    [Fact]
    public async Task FirstTimeEnablement_ThirtyDaysOnEachComputer_ImportsNewDates_FlagsDifferentOnes_AndPreservesAllLocalText()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var start = new DateOnly(2026, 9, 1);
        for (var i = 0; i < 30; i++)
        {
            await a.SaveAsync(start.AddDays(i), Text(reminders: $"A day {i}", breakPlan: $"A plan {i}"));
        }

        for (var i = 15; i < 45; i++)
        {
            var text = i < 25 ? Text(reminders: $"A day {i}", breakPlan: $"A plan {i}") // same as A
                : i < 30 ? Text(reminders: $"B day {i}", breakPlan: $"A plan {i}")   // reminders differ
                : Text(reminders: $"B day {i}");                                      // only on B
            await b.SaveAsync(start.AddDays(i), text);
        }

        await SyncAllAsync(Root, a, b);

        for (var i = 0; i < 45; i++)
        {
            var date = start.AddDays(i);
            var onA = await a.LoadAsync(date);
            var onB = await b.LoadAsync(date);
            if (i is >= 25 and < 30)
            {
                Assert.Equal($"A day {i}", onA.Reminders); // each keeps its own until resolved
                Assert.Equal($"B day {i}", onB.Reminders);
                Assert.Equal($"A plan {i}", onB.BreakPlan);
            }
            else
            {
                Assert.Equal(onA, onB);
                Assert.Equal(i < 30 ? $"A day {i}" : $"B day {i}", onA.Reminders);
            }
        }

        Assert.Equal(5, (await a.Sync.GetConflictsAsync()).Count);
        Assert.Equal(5, (await b.Sync.GetConflictsAsync()).Count);
    }

    [Fact]
    public async Task ThreeComputers_EditingDifferentThings_Converge_InAnyOrder()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var c = await CreateAsync("c");
        await a.SaveAsync(Oct22, Text(reminders: "A reminders"));
        await b.SaveAsync(Oct22, Text(breakPlan: "B plan"));
        await c.SaveAsync(Oct23, Text(odNotes: "C OD notes"));
        await c.Preferences.SaveAsync(MorningMessagePreferences.Default with { RemindersAsBullets = true });

        await SyncAllAsync(Root, c, a, b);

        foreach (var device in new[] { a, b, c })
        {
            Assert.Equal(Text(reminders: "A reminders", breakPlan: "B plan"), await device.LoadAsync(Oct22));
            Assert.Equal(Text(odNotes: "C OD notes"), await device.LoadAsync(Oct23));
            Assert.True(device.Preferences.Load().RemindersAsBullets);
            Assert.Empty(await device.Sync.GetConflictsAsync());
        }
    }

    [Fact]
    public async Task ThreeComputers_AllDisagreeingOnOneField_ConvergeOnceConflictsAreResolved_WithNoValueDroppedUnseen()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        var c = await CreateAsync("c");
        await a.SaveAsync(Oct22, Text(reminders: "A"));
        await b.SaveAsync(Oct22, Text(reminders: "B"));
        await c.SaveAsync(Oct22, Text(reminders: "C"));

        var seenByA = new HashSet<string>();
        for (var round = 0; round < 6; round++)
        {
            await SyncAllAsync(Root, a, b, c);
            var conflicts = await a.Sync.GetConflictsAsync();
            if (conflicts.Count == 0)
            {
                break;
            }

            seenByA.Add(conflicts[0].Fields[0].OtherDevice);
            await a.Sync.ResolveAsync(Oct22, MorningMessageConflictResolution.KeepThisDevice);
        }

        await SyncAllAsync(Root, a, b, c);
        Assert.Equal(["B", "C"], seenByA.Order()); // both other values were put in front of the user
        foreach (var device in new[] { a, b, c })
        {
            Assert.Equal("A", (await device.LoadAsync(Oct22)).Reminders);
            Assert.Empty(await device.Sync.GetConflictsAsync());
        }
    }

    [Fact]
    public async Task RepeatedPasses_AreIdempotent_AndDoNotRewriteFiles()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Once"));
        await SyncAllAsync(Root, a, b);
        var shared = SharedSnapshot();
        var bContent = File.ReadAllBytes(b.ContentPath);

        var first = await b.RunAsync(Root);
        var second = await b.Restart().RunAsync(Root); // import after restart

        foreach (var result in new[] { first, second })
        {
            Assert.Equal(0, result.DatesUpdated);
            Assert.False(result.PreferencesUpdated);
            Assert.False(result.Exported);
            Assert.Equal(0, result.ConflictCount);
        }

        Assert.Equal(bContent, File.ReadAllBytes(b.ContentPath));
        Assert.All(shared, entry => Assert.Equal(entry.Value, SharedSnapshot()[entry.Key]));
    }

    [Fact]
    public async Task OfflineEdits_OnBothComputers_SynchronizeLater_WithoutLoss()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Base"));
        await SyncAllAsync(Root, a, b);

        // Shared folder unreachable: the morning-message folder is replaced by a file squatting on its name.
        var hidden = MessageFolder + ".away";
        Directory.Move(MessageFolder, hidden);
        await File.WriteAllTextAsync(MessageFolder, string.Empty);

        await a.SaveAsync(Oct22, Text(reminders: "Base", breakPlan: "Offline plan from A"));
        await b.SaveAsync(Oct23, Text(reminders: "Offline reminder from B"));
        var offlineA = await a.RunAsync(Root);
        var offlineB = await b.RunAsync(Root);

        Assert.True(offlineA.IoFailures > 0);
        Assert.True(offlineB.IoFailures > 0);
        Assert.Equal("Offline plan from A", (await a.LoadAsync(Oct22)).BreakPlan); // editing is never blocked
        Assert.Equal(MorningMessageSyncStatus.FolderUnavailable, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status);

        File.Delete(MessageFolder);
        Directory.Move(hidden, MessageFolder);
        await SyncAllAsync(Root, a, b);

        Assert.Equal(Text(reminders: "Base", breakPlan: "Offline plan from A"), await b.LoadAsync(Oct22));
        Assert.Equal("Offline reminder from B", (await a.LoadAsync(Oct23)).Reminders);
        Assert.Equal(MorningMessageSyncStatus.Synced, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status);
    }

    [Fact]
    public async Task LostLocalSyncState_NeverCausesANewEditToBeIgnoredElsewhere()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        for (var i = 0; i < 5; i++)
        {
            await a.SaveAsync(Oct22, Text(reminders: $"Edit {i}"));
            await SyncAllAsync(Root, a, b);
        }

        File.Delete(a.StatePath);
        var restarted = a.Restart();
        await restarted.SaveAsync(Oct22, Text(reminders: "Edit after losing state"));
        await SyncAllAsync(Root, restarted, b);

        Assert.Equal("Edit after losing state", (await b.LoadAsync(Oct22)).Reminders);
        Assert.Empty(await b.Sync.GetConflictsAsync());
    }

    // ----- Part F: data safety, corruption and compatibility -----

    private async Task WriteForeignFileAsync(Guid device, string json, string? name = null)
    {
        Directory.CreateDirectory(MessageFolder);
        await File.WriteAllTextAsync(Path.Combine(MessageFolder, name ?? $"{device:D}.json"), json);
    }

    private static string ValidFile(Guid device, string date = "2026-10-22", string reminders = "Imported") => $$"""
        { "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "{{device:D}}",
          "Preferences": {},
          "Dates": { "{{date}}": { "Reminders": { "Value": "{{reminders}}", "Version": { "{{device:D}}": 1 } } } } }
        """;

    [Theory]
    [InlineData("""{ "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": """)] // truncated
    [InlineData("not json at all")]
    [InlineData("""{ "Format": "carowinds-morning-message", "FormatVersion": 2, "DeviceId": "DEVICE", "Dates": {} }""")] // newer format
    [InlineData("""{ "Format": "something-else", "FormatVersion": 1, "DeviceId": "DEVICE" }""")]
    [InlineData("""{ "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "00000000-0000-0000-0000-000000000001" }""")] // not the file's device
    [InlineData("[]")]
    public async Task UnreadableSharedFiles_AreSkipped_LeftUntouched_AndNeverBlockValidOnes(string badJson)
    {
        var a = await CreateAsync("a");
        await a.SaveAsync(Oct23, Text(reminders: "Local stays"));
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        await WriteForeignFileAsync(bad, badJson.Replace("DEVICE", bad.ToString("D"), StringComparison.Ordinal));
        await WriteForeignFileAsync(good, ValidFile(good));
        var badBytes = File.ReadAllBytes(Path.Combine(MessageFolder, $"{bad:D}.json"));

        var result = await a.RunAsync(Root);

        Assert.Equal(1, result.UnreadableFiles);
        Assert.Equal("Imported", (await a.LoadAsync(Oct22)).Reminders);
        Assert.Equal("Local stays", (await a.LoadAsync(Oct23)).Reminders);
        Assert.Equal(badBytes, File.ReadAllBytes(Path.Combine(MessageFolder, $"{bad:D}.json")));
    }

    [Fact]
    public async Task OversizedSharedFile_IsSkippedUnread()
    {
        var a = await CreateAsync("a");
        var big = Guid.NewGuid();
        Directory.CreateDirectory(MessageFolder);
        await using (var stream = File.Create(Path.Combine(MessageFolder, $"{big:D}.json")))
        {
            stream.SetLength(MorningMessageSyncFileSerializer.MaxFileBytes + 1);
        }

        var result = await a.RunAsync(Root);

        Assert.Equal(1, result.UnreadableFiles);
        Assert.False(File.Exists(a.ContentPath));
    }

    [Fact]
    public async Task InvalidValues_AreRejectedIndividually_AndValidOnesInTheSameFileStillImport()
    {
        var a = await CreateAsync("a");
        var c = Guid.NewGuid();
        var tooLong = new string('x', MorningMessageContent.MaxFieldLength + 1);
        await WriteForeignFileAsync(c, $$"""
            { "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "{{c:D}}",
              "Preferences": {
                "Sections": { "Value": "Greeting,Operations,OdNotes,Reminders,TicketPrices,BreakPlan,DailyFeature", "Version": { "{{c:D}}": 1 } },
                "Greeting": { "Value": "Valid greeting", "Version": { "{{c:D}}": 1 } } },
              "Dates": {
                "2026-13-45": { "Reminders": { "Value": "bad date", "Version": { "{{c:D}}": 1 } } },
                "../../etc": { "Reminders": { "Value": "path", "Version": { "{{c:D}}": 1 } } },
                "2026-10-22": {
                  "Reminders": { "Value": "{{tooLong}}", "Version": { "{{c:D}}": 1 } },
                  "BreakPlan": { "Value": "Zero counter", "Version": { "{{c:D}}": 0 } },
                  "OdNotes": { "Value": null, "Version": { "{{c:D}}": 1 } },
                  "PriceNote": { "Value": "No version" },
                  "DapImageReminder": { "Value": "maybe", "Version": { "{{c:D}}": 1 } },
                  "DapPlacement": { "Value": "Valid placement", "Version": { "{{c:D}}": 1 } } } } }
            """);

        var result = await a.RunAsync(Root);

        Assert.Equal(0, result.UnreadableFiles);
        Assert.Equal(8, result.RejectedValues);
        Assert.Equal(Text(dap: "Valid placement"), await a.LoadAsync(Oct22));
        Assert.Equal("Valid greeting", a.Preferences.Load().Greeting);
        Assert.Equal(MorningMessageLayout.Default, a.Preferences.Load().Layout);
        Assert.Equal([Oct22], (await a.Content.LoadAllAsync()).Keys);
    }

    [Fact]
    public async Task FileWithTooManyDates_OrTooManyBadValues_IsNotTrustedAtAll()
    {
        var a = await CreateAsync("a");
        var c = Guid.NewGuid();
        var dates = string.Join(",", Enumerable.Range(0, MorningMessageSyncFileSerializer.MaxDatesPerFile + 1)
            .Select(i => $$"""
                "{{new DateOnly(2026, 1, 1).AddDays(i):yyyy-MM-dd}}": { "Reminders": { "Value": "x", "Version": { "{{c:D}}": 1 } } }
                """));
        await WriteForeignFileAsync(c, $$"""{ "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "{{c:D}}", "Dates": { {{dates}} } }""");

        var d = Guid.NewGuid();
        var badValues = string.Join(",", Enumerable.Range(0, MorningMessageSyncFileSerializer.MaxRejectedValuesPerFile + 1)
            .Select(i => $$"""
                "{{new DateOnly(2026, 1, 1).AddDays(i):yyyy-MM-dd}}": { "DapImageReminder": { "Value": "bad", "Version": { "{{d:D}}": 1 } } }
                """));
        await WriteForeignFileAsync(d, $$"""{ "Format": "carowinds-morning-message", "FormatVersion": 1, "DeviceId": "{{d:D}}", "Dates": { {{badValues}} } }""");

        var result = await a.RunAsync(Root);

        Assert.Equal(2, result.UnreadableFiles);
        Assert.False(File.Exists(a.ContentPath));
    }

    [Fact]
    public async Task InterruptedWrites_LeftoverTempFiles_AreIgnored_AndStaleOnesCleanedOnlyByName()
    {
        var a = await CreateAsync("a");
        var other = Guid.NewGuid();
        Directory.CreateDirectory(MessageFolder);
        var partial = Path.Combine(MessageFolder, $"{other:D}.json{AtomicFileWriter.TempMarker}{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(partial, ValidFile(other)[..40]);
        var conflictCopy = Path.Combine(MessageFolder, $"{other:D}-LAPTOP.json");
        await File.WriteAllTextAsync(conflictCopy, ValidFile(other, reminders: "From conflict copy"));
        var stray = Path.Combine(MessageFolder, "notes.txt");
        await File.WriteAllTextAsync(stray, "not ours");

        var result = await a.RunAsync(Root);

        Assert.Equal(0, result.UnreadableFiles);
        Assert.Equal("From conflict copy", (await a.LoadAsync(Oct22)).Reminders); // a provider's conflict copy is just another version
        Assert.True(File.Exists(partial));
        Assert.True(File.Exists(stray));

        File.SetLastWriteTimeUtc(partial, DateTime.UtcNow.AddDays(-3));
        Assert.Equal(1, SyncTempFileCleaner.CleanStaleTempFiles(Root));
        Assert.False(File.Exists(partial));
        Assert.True(File.Exists(conflictCopy));
        Assert.True(File.Exists(stray));
        Assert.True(File.Exists(SyncPathService.GetMorningMessageFilePath(Root, a.Id)));
    }

    [Fact]
    public async Task UnreadableLocalMessageFile_IsNeverReplacedBySync()
    {
        var a = await CreateAsync("a");
        var other = Guid.NewGuid();
        await WriteForeignFileAsync(other, ValidFile(other));
        Directory.CreateDirectory(Path.GetDirectoryName(a.ContentPath)!);
        await File.WriteAllTextAsync(a.ContentPath, "{ \"Dates\": { \"2026-10-20\": { \"Reminders\": \"recoverable te");
        var before = File.ReadAllBytes(a.ContentPath);

        var result = await a.RunAsync(Root);

        Assert.True(result.IoFailures > 0);
        Assert.Equal(before, File.ReadAllBytes(a.ContentPath));
        Assert.False(File.Exists(SyncPathService.GetMorningMessageFilePath(Root, a.Id))); // nothing published either
    }

    [Fact]
    public async Task UnwritableSharedFolder_AndUnreadableSharedFile_AreRetryableFailures_ThatChangeNothingLocally()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix permissions stand in for a locked file here; Windows locking is covered by the VM checklist.
        }

        var a = await CreateAsync("a");
        var b = Guid.NewGuid();
        await WriteForeignFileAsync(b, ValidFile(b));
        var locked = Path.Combine(MessageFolder, $"{b:D}.json");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        await a.SaveAsync(Oct23, Text(reminders: "Mine"));

        var readFailure = await a.RunAsync(Root);
        Assert.Equal(1, readFailure.IoFailures);
        Assert.Equal(MorningMessageContent.Empty, await a.LoadAsync(Oct22));
        Assert.True(File.Exists(SyncPathService.GetMorningMessageFilePath(Root, a.Id))); // other files still flow

        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(MessageFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        await a.SaveAsync(Oct23, Text(reminders: "Mine, edited"));
        var writeFailure = await a.RunAsync(Root);

        Assert.True(writeFailure.IoFailures > 0);
        Assert.Equal("Mine, edited", (await a.LoadAsync(Oct23)).Reminders);
        Assert.Equal("Imported", (await a.LoadAsync(Oct22)).Reminders);

        File.SetUnixFileMode(MessageFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var recovered = await a.RunAsync(Root);
        Assert.Equal(0, recovered.IoFailures);
        Assert.True(recovered.Exported);
    }

    [Fact]
    public async Task SyncOrder_DoesNotChangeTheOutcome()
    {
        async Task<IReadOnlyDictionary<DateOnly, MorningMessageContent>> RunScenario(string prefix, bool reverse)
        {
            var root = Path.Combine(_workspace, prefix + "-shared");
            Directory.CreateDirectory(root);
            var devices = new List<Device>();
            foreach (var name in new[] { "a", "b", "c" })
            {
                var device = new Device(Path.Combine(_workspace, prefix + name), Guid.Parse($"{name}{name}{name}{name}0000-0000-0000-0000-000000000000"));
                await device.Config.SaveAsync(new SyncConfiguration(true, root, MorningMessageSyncEnabled: true));
                devices.Add(device);
            }

            await devices[0].SaveAsync(Oct22, Text(reminders: "A"));
            await devices[1].SaveAsync(Oct22, Text(breakPlan: "B"));
            await devices[2].SaveAsync(Oct23, Text(odNotes: "C"));
            var order = reverse ? devices.AsEnumerable().Reverse().ToArray() : devices.ToArray();
            await SyncAllAsync(root, order);
            return await devices[0].Content.LoadAllAsync();
        }

        var forward = await RunScenario("f", reverse: false);
        var backward = await RunScenario("r", reverse: true);

        Assert.Equal(forward.OrderBy(e => e.Key), backward.OrderBy(e => e.Key));
        Assert.Equal(Text(reminders: "A", breakPlan: "B"), forward[Oct22]);
    }

    // ----- Composer safety -----

    [Fact]
    public async Task WhileTheComposerIsOpen_ItsDateAndPreferencesAreHeld_AndAnEditMadeMeanwhileBecomesAConflict()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "Base"));
        await SyncAllAsync(Root, a, b);
        var changes = new List<MorningMessageSyncChange>();
        b.Session.Changed += (_, change) => changes.Add(change);

        await a.SaveAsync(Oct22, Text(reminders: "Changed on A"));
        await a.SaveAsync(Oct23, Text(reminders: "Another date from A"));
        await a.Preferences.SaveAsync(MorningMessagePreferences.Default with { Greeting = "Hello from A" });
        await a.RunAsync(Root);

        using (await b.Session.LockAsync())
        {
            b.Session.MarkOpen(Oct22);
        }

        var held = await b.RunAsync(Root);

        Assert.Equal("Base", (await b.LoadAsync(Oct22)).Reminders);           // the open date is untouched
        Assert.Equal("Another date from A", (await b.LoadAsync(Oct23)).Reminders); // other dates still arrive
        Assert.Equal(MorningMessagePreferences.DefaultGreeting, b.Preferences.Load().Greeting);
        Assert.Equal(2, held.DeferredCount);
        var notice = Assert.Single(changes);
        Assert.Equal([Oct22], notice.DeferredDates);
        Assert.True(notice.PreferencesDeferred);
        Assert.Equal([Oct23], notice.UpdatedDates);
        Assert.Equal(MorningMessageSyncStatus.ChangesWaiting, (await b.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status);
        Assert.Equal(MorningMessageResolveOutcome.NoConflictFound, await b.Sync.ResolveAsync(Oct23, MorningMessageConflictResolution.KeepThisDevice));

        // The user keeps typing in the open composer, then closes it.
        await b.SaveAsync(Oct22, Text(reminders: "Typed on B meanwhile"));
        using (await b.Session.LockAsync())
        {
            b.Session.MarkClosed();
        }

        await b.RunAsync(Root);

        Assert.Equal("Typed on B meanwhile", (await b.LoadAsync(Oct22)).Reminders);
        Assert.Equal("Changed on A", Assert.Single(await b.Sync.GetConflictsAsync()).Fields[0].OtherDevice);
        Assert.Equal("Hello from A", b.Preferences.Load().Greeting); // applied once the composer closed
    }

    [Fact]
    public async Task ResolvingWhileTheComposerIsOpenOnThatDate_IsRefused()
    {
        var a = await CreateAsync("a");
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "A"));
        await b.SaveAsync(Oct22, Text(reminders: "B"));
        await SyncAllAsync(Root, a, b);
        using (await b.Session.LockAsync())
        {
            b.Session.MarkOpen(Oct22);
        }

        Assert.Equal(MorningMessageResolveOutcome.ComposerOpen, await b.Sync.ResolveAsync(Oct22, MorningMessageConflictResolution.UseOtherDevice));
        Assert.Equal("B", (await b.LoadAsync(Oct22)).Reminders);
    }

    // ----- Status -----

    [Fact]
    public async Task Status_FollowsTheSettingTheFolderLocalEditsAndConflicts()
    {
        var a = await CreateAsync("a", enabled: false);
        var b = await CreateAsync("b");

        Assert.Equal(MorningMessageSyncStatus.NotSyncing, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status);
        await a.Sync.SetEnabledAsync(true);
        Assert.True(a.Session.SyncEnabled);
        Assert.Equal(MorningMessageSyncStatus.NeedsSyncFolder, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.NotConfigured)).Status);
        Assert.Equal(MorningMessageSyncStatus.NeedsSyncFolder, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Off)).Status);
        Assert.Equal(MorningMessageSyncStatus.FolderUnavailable, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.FolderUnavailable)).Status);
        Assert.Equal(MorningMessageSyncStatus.ChangesWaiting, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status); // never synced yet

        await a.SaveAsync(Oct22, Text(reminders: "A"));
        await a.RunAsync(Root);
        var synced = await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready);
        Assert.Equal(MorningMessageSyncStatus.Synced, synced.Status);
        Assert.Equal(Now, synced.LastSyncedUtc);

        await a.SaveAsync(Oct22, Text(reminders: "A, edited"));
        Assert.Equal(MorningMessageSyncStatus.ChangesWaiting, (await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready)).Status);

        await b.SaveAsync(Oct22, Text(reminders: "B"));
        await SyncAllAsync(Root, b, a);
        var conflicted = await a.Sync.GetOverviewAsync(PersonalSyncStatus.Ready);
        Assert.Equal(MorningMessageSyncStatus.ConflictNeedsReview, conflicted.Status);
        Assert.Equal(1, conflicted.ConflictCount);
    }

    // ----- Privacy -----

    [Fact]
    public async Task Logs_NeverContainMessageText()
    {
        var log = new CapturingLogger();
        var a = new Device(Path.Combine(_workspace, "logged"), Guid.NewGuid(), log);
        await a.Config.SaveAsync(new SyncConfiguration(true, Root, MorningMessageSyncEnabled: true));
        var b = await CreateAsync("b");
        await a.SaveAsync(Oct22, Text(reminders: "SECRET-associate-name break at 10"));
        await b.SaveAsync(Oct22, Text(reminders: "SECRET-other-name"));
        await WriteForeignFileAsync(Guid.NewGuid(), "{ \"Format\": \"carowinds-morning-message\", \"SECRET-in-a-broken-file");

        await SyncAllAsync(Root, b, a);

        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain(log.Messages, message => message.Contains("SECRET", StringComparison.Ordinal));
    }

    private sealed class FixedDeviceId(Guid id) : IDeviceIdProvider
    {
        public Guid GetOrCreateDeviceId() => id;
    }

    private sealed class NoSuggestions : IOneDriveLocationSuggestionProvider
    {
        public IReadOnlyList<string> GetSuggestedRoots() => [];
    }

    private sealed class FixedClock : Application.Time.IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class CapturingLogger : ILogger<MorningMessageSync>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception) + (exception?.ToString() ?? string.Empty));
            }
        }
    }
}
