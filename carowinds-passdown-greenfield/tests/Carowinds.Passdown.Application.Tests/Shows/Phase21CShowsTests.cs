using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Shows;

/// <summary>Phase 21C: schedule composition, Associate selection/reconciliation, draft and snapshot compatibility.</summary>
public class Phase21CShowsTests
{
    /// <summary>2026-10-16 3:00 PM Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Oct17 = new(2026, 10, 17);
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/shows");

    private static TimeOnly T(int hour, int minute = 0) => new(hour, minute);

    private static ScheduledShow Wake(params TimeOnly[] times) => new("SHOW-030-00122", "Wake the Dead", "Carowinds Theatre", times.Length == 0 ? [T(19), T(20)] : times);

    private static ScheduledShow Drummers => new("SHOW-030-00185", "Blade Drummers - The Sacrifice", "HH Event Pad", [T(19, 15)]);

    private static SourceEvidence Evidence(RetrievalStatus status, DateOnly date, DateTimeOffset? at = null) =>
        new("Carowinds Show Schedule", Url, at ?? Now, status, $"{status}.", RetrievalStrategy.DirectApi, requestedDate: date,
            confirmedDate: status == RetrievalStatus.Error ? null : date);

    private static async Task<InMemoryDatedResultRepository> SeedVerified(DateOnly date, params ScheduledShow[] shows)
    {
        var repository = new InMemoryDatedResultRepository();
        await repository.RecordSuccessAsync(new DatedResultKey(SourceCategory.ShowSchedule, date), Evidence(RetrievalStatus.Verified, date),
            JsonSerializer.Serialize(new ShowSchedulePayload(shows)));
        return repository;
    }

    private static async Task<ShowScheduleView> Compose(InMemoryDatedResultRepository repository, DateOnly date, DateTimeOffset? asOf = null) =>
        ShowScheduleView.Compose(date, await repository.GetAsync(new DatedResultKey(SourceCategory.ShowSchedule, date)), asOf ?? Now);

    // ---- Schedule composition -------------------------------------------------------------------

    [Fact]
    public async Task Compose_VerifiedSchedule_IsScheduled_WithShowsAndCheckedTime()
    {
        var view = await Compose(await SeedVerified(Oct17, Wake(), Drummers), Oct17);

        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Equal(2, view.Shows.Count);
        Assert.Equal(Now, view.LastVerifiedAt);
        Assert.False(view.LastCheckFailed);
    }

    [Fact]
    public async Task Compose_VerifiedEmptySchedule_IsNoShows()
    {
        Assert.Equal(ShowScheduleState.NoShows, (await Compose(await SeedVerified(Oct17), Oct17)).State);
    }

    [Theory]
    [InlineData(RetrievalStatus.Closed, ShowScheduleState.ParkClosed)]
    [InlineData(RetrievalStatus.NotPublished, ShowScheduleState.NotPublished)]
    public async Task Compose_OfficialNonScheduleAnswers_AreNeverNoShows(RetrievalStatus status, ShowScheduleState expected)
    {
        var repository = new InMemoryDatedResultRepository();
        await repository.RecordSuccessAsync(new DatedResultKey(SourceCategory.ShowSchedule, Oct17), Evidence(status, Oct17), null);

        Assert.Equal(expected, (await Compose(repository, Oct17)).State);
    }

    [Fact]
    public async Task Compose_OnlyFailedAttempts_IsCouldNotCheck_NeverNoShows()
    {
        var repository = new InMemoryDatedResultRepository();
        await repository.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ShowSchedule, Oct17), Evidence(RetrievalStatus.Error, Oct17));

        var view = await Compose(repository, Oct17);

        Assert.Equal(ShowScheduleState.CouldNotCheck, view.State);
        Assert.Empty(view.Shows);
    }

    [Fact]
    public async Task Compose_FailureAfterVerified_KeepsShows_AndFlagsTheFailedCheck_WithoutNewVerifiedTime()
    {
        var repository = await SeedVerified(Oct17, Wake());
        await repository.RecordFailedAttemptAsync(new DatedResultKey(SourceCategory.ShowSchedule, Oct17), Evidence(RetrievalStatus.Error, Oct17, Now.AddHours(4)));

        var view = await Compose(repository, Oct17, Now.AddHours(4));

        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Single(view.Shows);
        Assert.True(view.LastCheckFailed);
        Assert.Equal(Now, view.LastVerifiedAt);
        Assert.Equal(Now.AddHours(4), view.LastCheckedAt);
    }

    [Fact]
    public async Task Compose_NoRecord_FutureIsNotChecked_PastIsHistoricalNotAvailable()
    {
        var repository = new InMemoryDatedResultRepository();

        Assert.Equal(ShowScheduleState.NotChecked, (await Compose(repository, Oct17)).State);
        Assert.Equal(ShowScheduleState.HistoricalNotAvailable, (await Compose(repository, new DateOnly(2026, 10, 1))).State);
    }

    [Fact]
    public async Task Compose_PastDateWithRecordedSchedule_ShowsThatDatesRecord()
    {
        var view = await Compose(await SeedVerified(Oct17, Wake()), Oct17, Now.AddDays(5));

        Assert.Equal(ShowScheduleState.Scheduled, view.State);
        Assert.Equal("Wake the Dead", Assert.Single(view.Shows).Name);
    }

    [Fact]
    public async Task Compose_NeverReadsAnotherDatesSchedule()
    {
        var repository = await SeedVerified(Oct17, Wake());

        Assert.Equal(ShowScheduleState.NotChecked, (await Compose(repository, Oct17.AddDays(1))).State);
    }

    [Fact]
    public void UpcomingPerformances_ExcludesStartedTimes_AndOrdersByStart()
    {
        var view = new ShowScheduleView(Oct17, ShowScheduleState.Scheduled, [Wake(T(19), T(20), T(0, 15)), Drummers], false, Now, Now);

        var upcoming = view.UpcomingPerformances(new DateTime(2026, 10, 17, 19, 30, 0));

        Assert.Equal([T(20), T(0, 15)], upcoming.Select(p => p.Time));
    }

    // ---- Associate selection & reconciliation ------------------------------------------------------

    private static ShowScheduleView Scheduled(params ScheduledShow[] shows) => new(Oct17, ShowScheduleState.Scheduled, shows, false, Now, Now);

    private static AssociateShowSelection Saved(ScheduledShow show) => new(show.ShowId, show.Name, show.Venue, show.Times, Now);

    [Fact]
    public void Compose_NewlyPublishedShows_AreNeverSelectedAutomatically()
    {
        var section = AssociateShowsSection.Compose(Scheduled(Wake(), Drummers), new AssociateShowsSelection(true, [Saved(Wake())]));

        Assert.True(section.Rows.Single(r => r.ShowId == "SHOW-030-00122").IsSelected);
        Assert.False(section.Rows.Single(r => r.ShowId == "SHOW-030-00185").IsSelected);
        Assert.Equal(["Wake the Dead"], section.PrintableRows.Select(r => r.Name));
    }

    [Fact]
    public void Compose_ChangedTimes_UseTheVerifiedTimes_AndAreFlagged()
    {
        var section = AssociateShowsSection.Compose(Scheduled(Wake(T(19, 30))), new AssociateShowsSelection(true, [Saved(Wake())]));

        var row = Assert.Single(section.PrintableRows);
        Assert.Equal(AssociateShowRowState.TimesChanged, row.State);
        Assert.Equal([T(19, 30)], row.Times);
    }

    [Fact]
    public void Compose_ShowRemovedByASuccessfulRefresh_StaysListedButDoesNotPrint()
    {
        var section = AssociateShowsSection.Compose(Scheduled(Drummers), new AssociateShowsSelection(true, [Saved(Wake())]));

        var removed = section.Rows.Single(r => r.ShowId == "SHOW-030-00122");
        Assert.True(removed.IsSelected);
        Assert.Equal(AssociateShowRowState.NoLongerListed, removed.State);
        Assert.Empty(section.PrintableRows);
    }

    [Theory]
    [InlineData(ShowScheduleState.ParkClosed)]
    [InlineData(ShowScheduleState.NotPublished)]
    public void Compose_ClosedOrUnpublished_SelectedShowsDoNotPrint(ShowScheduleState state)
    {
        var schedule = new ShowScheduleView(Oct17, state, [], false, Now, Now);

        var section = AssociateShowsSection.Compose(schedule, new AssociateShowsSelection(true, [Saved(Wake())]));

        Assert.Equal(AssociateShowRowState.NotOnCurrentSchedule, Assert.Single(section.Rows).State);
        Assert.Empty(section.PrintableRows);
    }

    [Theory]
    [InlineData(ShowScheduleState.NotChecked)]
    [InlineData(ShowScheduleState.CouldNotCheck)]
    public void Compose_NoScheduleOnThisComputer_PrintsTheSavedCopy(ShowScheduleState state)
    {
        var schedule = new ShowScheduleView(Oct17, state, [], state == ShowScheduleState.CouldNotCheck, null, null);

        var section = AssociateShowsSection.Compose(schedule, new AssociateShowsSelection(true, [Saved(Wake())]));

        var row = Assert.Single(section.PrintableRows);
        Assert.Equal(AssociateShowRowState.SavedCopy, row.State);
        Assert.Equal([T(19), T(20)], row.Times);
    }

    [Fact]
    public void Compose_SectionOff_PrintsNothing_EvenWithSelections()
    {
        var section = AssociateShowsSection.Compose(Scheduled(Wake()), new AssociateShowsSelection(false, [Saved(Wake())]));

        Assert.Empty(section.PrintableRows);
        Assert.True(Assert.Single(section.Rows).IsSelected);
    }

    [Fact]
    public void Compose_DuplicateSelectionsInADraft_NeverDuplicateRows()
    {
        var section = AssociateShowsSection.Compose(Scheduled(Wake()), new AssociateShowsSelection(true, [Saved(Wake()), Saved(Wake())]));

        Assert.Single(section.Rows);
    }

    [Fact]
    public void BuildSelection_RefreshesSavedCopyForShowsOnSchedule_KeepsSelectedTime_AndKeepsRemovedCopies()
    {
        var earlier = Now.AddHours(-3);
        var previous = new AssociateShowsSelection(true, [Saved(Wake()) with { SelectedAtUtc = earlier }, Saved(Drummers)]);
        var section = AssociateShowsSection.Compose(Scheduled(Wake(T(21))), previous);

        var built = AssociateShowsSection.BuildSelection(true, section.Rows.Where(r => r.IsSelected), previous, Now)!;

        var wake = built.SelectedShows.Single(s => s.ShowId == "SHOW-030-00122");
        Assert.Equal([T(21)], wake.Times);
        Assert.Equal(earlier, wake.SelectedAtUtc);
        Assert.Equal(Drummers.Times, built.SelectedShows.Single(s => s.ShowId == "SHOW-030-00185").Times);
    }

    [Fact]
    public void BuildSelection_OffAndEmpty_IsNull_SoTheDraftIsUnchanged()
    {
        Assert.Null(AssociateShowsSection.BuildSelection(false, [], null, Now));
        Assert.NotNull(AssociateShowsSection.BuildSelection(true, [], null, Now));
    }

    // ---- Draft compatibility & persistence -------------------------------------------------------

    [Fact]
    public void Draft_WithoutShows_SerializesExactlyAsBefore_AndHashesUnchanged()
    {
        var draft = AssociateDraftPayload.CreateDefault();
        var json = JsonSerializer.Serialize(draft);

        Assert.DoesNotContain("\"Shows\"", json);
        Assert.Equal(SyncContentHash.ComputeDraftHash(draft), SyncContentHash.ComputeDraftHash(AssociateDraftPayloadSerializer.Deserialize(json)));
    }

    [Fact]
    public void LegacyDraft_LoadsWithNoShowSelection_NeverAFabricatedOne()
    {
        var legacyJson = JsonSerializer.Serialize(AssociateDraftPayload.CreateDefault());

        var loaded = AssociateDraftPayloadSerializer.Deserialize(legacyJson);

        Assert.Null(loaded.Shows);
        Assert.Empty(AssociateShowsSection.Compose(Scheduled(Wake()), loaded.Shows).PrintableRows);
    }

    [Fact]
    public void Draft_WithShows_RoundTrips_PreservingUnrelatedFields()
    {
        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = 12000,
            Shows = new AssociateShowsSelection(true, [Saved(Wake())]),
        };

        var loaded = AssociateDraftPayloadSerializer.Deserialize(JsonSerializer.Serialize(draft));

        Assert.Equal(12000, loaded.ProjectedAttendance);
        Assert.True(loaded.Shows!.IncludeInPassdown);
        var saved = Assert.Single(loaded.Shows.SelectedShows);
        Assert.Equal("SHOW-030-00122", saved.ShowId);
        Assert.Equal([T(19), T(20)], saved.Times);
        Assert.Equal(SyncContentHash.ComputeDraftHash(draft), SyncContentHash.ComputeDraftHash(loaded));
    }

    private static AssociatePassdownService Service(InMemoryAssociateDraftRepository drafts, InMemoryDatedResultRepository dated, FakeClock clock) =>
        new(drafts, dated, new InMemoryCatalogProductRepository(), new FakeRefreshOrchestrator(),
            new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock), clock);

    [Fact]
    public async Task Service_SelectionsAreDateSpecific_AndSurviveRestart()
    {
        var clock = new FakeClock(Now);
        var drafts = new InMemoryAssociateDraftRepository();
        var dated = await SeedVerified(Oct17, Wake(), Drummers);
        var service = Service(drafts, dated, clock);

        var workspace = await service.LoadAssociateDateAsync(Oct17);
        var selection = AssociateShowsSection.BuildSelection(true, workspace.Shows.Rows.Where(r => r.ShowId == "SHOW-030-00122"), null, Now);
        await service.SaveDraftAsync(Oct17, workspace.Draft with { Shows = selection });

        var restarted = Service(drafts, dated, clock);
        var reloaded = await restarted.LoadAssociateDateAsync(Oct17);
        var otherDate = await restarted.LoadAssociateDateAsync(Oct17.AddDays(1));

        Assert.Equal(["Wake the Dead"], reloaded.Shows.PrintableRows.Select(r => r.Name));
        Assert.Null(otherDate.Draft.Shows);
        Assert.Empty(otherDate.Shows.Rows);
        Assert.Equal(ShowScheduleState.NotChecked, otherDate.ShowSchedule.State);
    }

    [Fact]
    public async Task Service_WorkspaceComposesOnlyFromLocalData_AndShowsNeverBecomeProducts()
    {
        var clock = new FakeClock(Now);
        var service = Service(new InMemoryAssociateDraftRepository(), await SeedVerified(Today, Wake()), clock);

        var workspace = await service.LoadAssociateDateAsync(Today);

        Assert.Equal(ShowScheduleState.Scheduled, workspace.ShowSchedule.State);
        Assert.DoesNotContain(workspace.Products, p => p.Category == SourceCategory.ShowSchedule);
        Assert.DoesNotContain(workspace.CategoryStatuses, s => s.Category == SourceCategory.ShowSchedule);
    }

    // ---- Snapshot compatibility ------------------------------------------------------------------

    private static FinalizedPassdownSnapshot Snapshot() => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        Oct17,
        "Saturday, October 17, 2026",
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "11:00 AM - 12:00 AM", null, "10:15 AM", "10:30 AM", null, null),
        new FinalizedAttendanceSnapshot(null, null),
        new FinalizedEarlyRideTimeSnapshot(false, null, [], EffectiveErtKind.NoErt),
        new FinalizedDailyFeatureSnapshot(null, null),
        [],
        [],
        false,
        "Oct 17 • 6:00 AM");

    [Fact]
    public void Snapshot_WithoutShows_SerializesWithoutTheProperty_SoExistingHashesAreUnchanged()
    {
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Snapshot());

        Assert.DoesNotContain("\"Shows\"", json);
        Assert.Null(FinalizedPassdownSnapshotSerializer.Deserialize(json).Shows);
    }

    [Fact]
    public void Snapshot_WithShows_RoundTrips_AndStaysSchemaVersion2()
    {
        var snapshot = Snapshot() with { Shows = [new FinalizedShowSnapshot("Wake the Dead", "Carowinds Theatre", "7:00 PM, 8:00 PM")] };

        var loaded = FinalizedPassdownSnapshotSerializer.Deserialize(FinalizedPassdownSnapshotSerializer.Serialize(snapshot));

        Assert.Equal(2, loaded.SchemaVersion);
        var show = Assert.Single(loaded.Shows!);
        Assert.Equal("7:00 PM, 8:00 PM", show.TimesText);
    }

    [Fact]
    public void OlderReader_IgnoresTheShowsProperty()
    {
        // An RC.5-shaped reader has no Shows property; an unknown JSON property must not break it.
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Snapshot() with { Shows = [new FinalizedShowSnapshot("A", null, "1:00 PM")] });

        var withoutShows = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(withoutShows.TryGetProperty("ParkHours", out _));
        Assert.Equal(Oct17, FinalizedPassdownSnapshotSerializer.Deserialize(json).PassdownDate);
    }
}
