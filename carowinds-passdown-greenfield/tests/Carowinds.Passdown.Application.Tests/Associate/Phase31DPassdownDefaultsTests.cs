using System.Text.Json;
using System.Text.Json.Nodes;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.Associate;

/// <summary>
/// Phase 31D: Settings → Passdown Preferences and saved section selections. A date with no saved draft starts
/// from this computer's defaults; a saved draft (any version) always restores its own saved selections —
/// including explicitly unchecked ones — so a restart, an update or a changed default never rewrites them.
/// </summary>
public class Phase31DPassdownDefaultsTests
{
    private static readonly DateOnly Date = new(2026, 8, 21);
    private static readonly DateOnly OtherDate = new(2026, 8, 22);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 14, 0, 0, TimeSpan.Zero);

    private sealed class InMemoryDefaultsStore(PassdownDefaultSelections initial) : IPassdownDefaultsPreferenceStore
    {
        public PassdownDefaultSelections Value { get; set; } = initial;

        public int Loads { get; private set; }

        public Exception? LoadException { get; set; }

        public Task<PassdownDefaultSelections> LoadAsync(CancellationToken cancellationToken = default)
        {
            Loads++;
            return LoadException is { } ex ? Task.FromException<PassdownDefaultSelections>(ex) : Task.FromResult(Value);
        }

        public Task SaveAsync(PassdownDefaultSelections defaults, CancellationToken cancellationToken = default)
        {
            Value = defaults;
            return Task.CompletedTask;
        }
    }

    private static AssociatePassdownService CreateService(InMemoryAssociateDraftRepository drafts, IPassdownDefaultsPreferenceStore? store)
    {
        var clock = new FakeClock(FixedNow);
        return new AssociatePassdownService(
            drafts, new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock,
            passdownDefaultsStore: store);
    }

    private static readonly PassdownDefaultSelections Custom = new(
        DailyAdmission: false, FastLane: true, Promotions: false, SeasonPasses: true, AddOns: true, OperationalNotes: false, Shows: true);

    // ---- Built-in defaults ------------------------------------------------------------------------

    [Fact]
    public void BuiltIn_KeepsTheEarlierNewDraftDefaults_ExceptOperationalNotesIsNowOn()
    {
        var builtIn = PassdownDefaultSelections.BuiltIn;

        Assert.True(builtIn.DailyAdmission);
        Assert.True(builtIn.FastLane);
        Assert.True(builtIn.Promotions);
        Assert.False(builtIn.SeasonPasses);
        Assert.False(builtIn.AddOns);
        Assert.True(builtIn.OperationalNotes);
        Assert.False(builtIn.Shows);
    }

    [Fact]
    public void BuiltIn_AgreesWithEveryOtherNewDraftFallback()
    {
        Assert.Equal(PassdownSectionSelection.Default, PassdownDefaultSelections.BuiltIn.ToSectionSelection());
        Assert.Equal(AssociateDraftPayload.CreateDefault().SectionSelection, AssociateDraftPayload.CreateDefault(PassdownDefaultSelections.BuiltIn).SectionSelection);
        Assert.Null(AssociateDraftPayload.CreateDefault(PassdownDefaultSelections.BuiltIn).Shows); // Shows off -> omitted, as before
        Assert.False(PassdownSectionSelection.Default.IncludeHauntedAttractions); // not configurable; unchanged
    }

    [Fact]
    public void CreateDefault_WithShowsOn_TurnsTheSectionOn_ButNeverPreSelectsAShow()
    {
        var draft = AssociateDraftPayload.CreateDefault(PassdownDefaultSelections.BuiltIn with { Shows = true });

        Assert.NotNull(draft.Shows);
        Assert.True(draft.Shows!.IncludeInPassdown);
        Assert.Empty(draft.Shows.SelectedShows);
    }

    // ---- New (unsaved) dates ----------------------------------------------------------------------

    [Fact]
    public async Task UnsavedDate_StartsFromTheConfiguredDefaults()
    {
        var service = CreateService(new InMemoryAssociateDraftRepository(), new InMemoryDefaultsStore(Custom));

        var workspace = await service.LoadAssociateDateAsync(Date);

        Assert.Equal(AssociateSaveState.NeverSaved, workspace.SaveStatus.State);
        Assert.Equal(new PassdownSectionSelection(false, true, false, true, true, false, false), workspace.Draft.SectionSelection);
        Assert.True(workspace.Draft.Shows!.IncludeInPassdown);
        Assert.Empty(workspace.Draft.Shows.SelectedShows);
    }

    [Fact]
    public async Task UnsavedDate_WithoutAStore_UsesBuiltInDefaults_WithOperationalNotesOn()
    {
        var service = CreateService(new InMemoryAssociateDraftRepository(), store: null);

        var workspace = await service.LoadAssociateDateAsync(Date);

        Assert.True(workspace.Draft.SectionSelection.IncludeOperationalNotes);
        Assert.Equal(PassdownSectionSelection.Default, workspace.Draft.SectionSelection);
    }

    [Fact]
    public async Task UnreadableDefaults_FallBackToBuiltIn_NeverFailTheLoad()
    {
        var store = new InMemoryDefaultsStore(Custom) { LoadException = new IOException("locked") };
        var service = CreateService(new InMemoryAssociateDraftRepository(), store);

        var workspace = await service.LoadAssociateDateAsync(Date);

        Assert.Equal(PassdownSectionSelection.Default, workspace.Draft.SectionSelection);
    }

    [Fact]
    public async Task SavedDate_NeverReadsTheDefaults()
    {
        var drafts = new InMemoryAssociateDraftRepository();
        var store = new InMemoryDefaultsStore(Custom);
        var service = CreateService(drafts, store);
        await service.SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault());
        var loadsBefore = store.Loads;

        await service.LoadAssociateDateAsync(Date);

        Assert.Equal(loadsBefore, store.Loads);
    }

    // ---- Saved selections -------------------------------------------------------------------------

    [Fact]
    public async Task SavedSelections_SurviveARestart()
    {
        var drafts = new InMemoryAssociateDraftRepository();
        var selection = new PassdownSectionSelection(true, false, true, true, false, IncludeOperationalNotes: true, IncludeHauntedAttractions: false);
        await CreateService(drafts, new InMemoryDefaultsStore(PassdownDefaultSelections.BuiltIn))
            .SaveDraftAsync(Date, AssociateDraftPayload.CreateDefault() with { SectionSelection = selection, Shows = new AssociateShowsSelection(true, []) });

        // A new service over the same stored drafts — what the next app start (or an updated app) sees.
        var restarted = await CreateService(drafts, new InMemoryDefaultsStore(Custom)).LoadAssociateDateAsync(Date);

        Assert.Equal(selection, restarted.Draft.SectionSelection);
        Assert.True(restarted.Draft.Shows!.IncludeInPassdown);
    }

    [Fact]
    public async Task ExplicitlyUncheckedOperationalNotes_StaysUnchecked_EvenWhenTheDefaultIsOn()
    {
        var drafts = new InMemoryAssociateDraftRepository();
        var service = CreateService(drafts, new InMemoryDefaultsStore(PassdownDefaultSelections.BuiltIn));
        var unchecked_ = AssociateDraftPayload.CreateDefault() with
        {
            SectionSelection = PassdownSectionSelection.Default with { IncludeOperationalNotes = false },
        };
        await service.SaveDraftAsync(Date, unchecked_);

        var workspace = await service.LoadAssociateDateAsync(Date);

        Assert.False(workspace.Draft.SectionSelection.IncludeOperationalNotes);
    }

    [Fact]
    public async Task ChangingDefaults_AffectsNewPassdownsOnly()
    {
        var drafts = new InMemoryAssociateDraftRepository();
        var store = new InMemoryDefaultsStore(PassdownDefaultSelections.BuiltIn);
        var service = CreateService(drafts, store);
        var saved = (await service.LoadAssociateDateAsync(Date)).Draft;
        await service.SaveDraftAsync(Date, saved);

        store.Value = Custom;

        var savedAfter = await service.LoadAssociateDateAsync(Date);
        var newAfter = await service.LoadAssociateDateAsync(OtherDate);
        Assert.Equal(saved.SectionSelection, savedAfter.Draft.SectionSelection);
        Assert.True(savedAfter.Draft.SectionSelection.IncludeOperationalNotes);
        Assert.Equal(Custom.ToSectionSelection(), newAfter.Draft.SectionSelection);
        Assert.Equal(AssociateSaveState.NeverSaved, newAfter.SaveStatus.State);
    }

    [Fact]
    public async Task SelectingAPersistentNote_OnAnUnsavedDate_SavesTheConfiguredDefaults()
    {
        var clock = new FakeClock(FixedNow);
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var note = await notes.CreateAsync(new OperationalNoteDraft("Gate 3 closed", null, OperationalNoteCategory.General, OperationalNotePriority.Normal, Date, null));
        var drafts = new InMemoryAssociateDraftRepository();
        var service = new AssociatePassdownService(
            drafts, new InMemoryDatedResultRepository(), new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(),
            notes, clock, passdownDefaultsStore: new InMemoryDefaultsStore(Custom));

        await service.SelectOperationalNoteAsync(Date, note.Id);

        var stored = AssociateDraftPayloadSerializer.Deserialize((await drafts.GetAsync(Date))!.PayloadJson);
        Assert.Equal(Custom.ToSectionSelection(), stored.SectionSelection);
        Assert.Single(stored.SelectedOperationalNotes);
    }

    // ---- Older saved drafts: missing versus explicit false -----------------------------------------

    private static string SerializeWithout(AssociateDraftPayload draft, params string[] path)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(draft))!.AsObject();
        if (path.Length == 1)
        {
            node.Remove(path[0]);
        }
        else
        {
            node[path[0]]!.AsObject().Remove(path[1]);
        }

        return node.ToJsonString();
    }

    [Fact]
    public void OlderDraft_ExplicitFalse_IsKept()
    {
        var draft = AssociateDraftPayload.CreateDefault() with { SectionSelection = new PassdownSectionSelection(false, false, false, false, false, false, false) };

        var loaded = AssociateDraftPayloadSerializer.Deserialize(JsonSerializer.Serialize(draft));

        Assert.Equal(new PassdownSectionSelection(false, false, false, false, false, false, false), loaded.SectionSelection);
    }

    [Fact]
    public void OlderDraft_WithNoSectionSelectionAtAll_KeepsItsLegacyAllIncludedMeaning()
    {
        var json = SerializeWithout(AssociateDraftPayload.CreateDefault(), "SectionSelection");

        Assert.Equal(PassdownSectionSelection.AllIncluded, AssociateDraftPayloadSerializer.Deserialize(json).SectionSelection);
    }

    [Fact]
    public void OlderDraft_WithNoShowsValue_StaysOff_NeverTakesTheNewDefault()
    {
        var json = SerializeWithout(AssociateDraftPayload.CreateDefault() with { Shows = new AssociateShowsSelection(true, []) }, "Shows");

        Assert.Null(AssociateDraftPayloadSerializer.Deserialize(json).Shows);
    }

    [Fact]
    public void Pre31DDraft_WithOperationalNotesUnchecked_LoadsUnchecked()
    {
        // The exact shape every pre-31D new draft was saved with (Operational Notes false).
        const string json = """
            {"ManualHoursOverride":{"Enabled":false,"OpeningTime":null,"ClosingTime":null},
             "PrivateBuyout":{"Enabled":false,"OpeningTime":null,"ClosingTime":null,"Details":null},
             "ProjectedAttendance":null,"PreSolds":null,"OperationalNotes":[],"DailyFeatureType":null,"DailyFeatureText":null,
             "ProductOverrides":{},"CustomProducts":[],
             "SectionSelection":{"IncludeDailyAdmission":true,"IncludeFastLane":true,"IncludePromotions":true,"IncludeSeasonPasses":false,
                                 "IncludeAddOns":false,"IncludeOperationalNotes":false,"IncludeHauntedAttractions":false},
             "SelectedOperationalNotes":[]}
            """;

        var loaded = AssociateDraftPayloadSerializer.Deserialize(json);

        Assert.False(loaded.SectionSelection.IncludeOperationalNotes);
        Assert.Null(loaded.Shows);
    }

    // ---- Personal Sync ------------------------------------------------------------------------------

    [Fact]
    public void DraftSchemaVersion_IsUnchanged()
    {
        Assert.Equal(4, AssociateDraftPayload.CurrentSchemaVersion);
    }

    [Fact]
    public void ConfiguredDefaultDrafts_AreValidSyncPayloads_AndRoundTripTheirSelections()
    {
        foreach (var defaults in new[] { PassdownDefaultSelections.BuiltIn, Custom })
        {
            var draft = AssociateDraftPayload.CreateDefault(defaults);

            Assert.True(SyncPayloadValidator.IsValidDraft(draft));
            var remote = AssociateDraftPayloadSerializer.Deserialize(JsonSerializer.Serialize(draft)); // what another computer reads
            Assert.Equal(draft.SectionSelection, remote.SectionSelection);
            Assert.Equal(draft.Shows?.IncludeInPassdown ?? false, remote.Shows?.IncludeInPassdown ?? false);
            Assert.Equal(SyncContentHash.ComputeDraftHash(draft), SyncContentHash.ComputeDraftHash(remote));
        }
    }

    [Fact]
    public void ExistingDraftHashes_AreUnchanged_ByThisPhase()
    {
        // Pre-31D draft with Shows off: still serializes without a "Shows" property, so its sync hash is stable.
        var draft = AssociateDraftPayload.CreateDefault() with { SectionSelection = PassdownSectionSelection.Default with { IncludeOperationalNotes = false } };

        var json = JsonSerializer.Serialize(draft);

        Assert.DoesNotContain("\"Shows\"", json, StringComparison.Ordinal);
        Assert.Equal(SyncContentHash.ComputeDraftHash(draft), SyncContentHash.ComputeDraftHash(AssociateDraftPayloadSerializer.Deserialize(json)));
    }
}
