using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4C Part M: editable-field, autosave/debounce, and date-switch/Check-Online-preservation
/// tests for <see cref="AssociateViewModel"/>. Pure C# — no Avalonia rendering — using
/// <see cref="FakeAssociatePassdownService"/> (its Phase 4C per-date draft store lets a test assert
/// real save-then-reload round trips) and <see cref="ManualDebounceScheduler"/> (a deterministic
/// stand-in for the injected autosave delay, so no test here sleeps past a real debounce interval).
/// </summary>
public class AssociateViewModelDraftEditingTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) CreateViewModel()
    {
        var service = new FakeAssociatePassdownService();
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay);
        return (viewModel, service, scheduler);
    }

    private static async Task ElapseAndAwaitSaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler)
    {
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    // 1. Editing projected attendance updates draft state.
    [Fact]
    public async Task ProjectedAttendance_Edit_UpdatesDraftState()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "8500";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal(8500, service.SaveCalls.Single().Draft.ProjectedAttendance);
        Assert.Equal(8500, viewModel.Workspace!.Draft.ProjectedAttendance);
    }

    // 2. Editing pre-solds updates draft state.
    [Fact]
    public async Task PreSolds_Edit_UpdatesDraftState()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.PreSoldsText = "120";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal(120, service.SaveCalls.Single().Draft.PreSolds);
    }

    // 3. Operational notes persist.
    [Fact]
    public async Task OperationalNotes_Edit_PersistsAsConciseLines()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.OperationalNotesText = "Ride 3 down for maintenance\r\nExtra staff at North Gate\n\n";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal(
            ["Ride 3 down for maintenance", "Extra staff at North Gate"],
            service.SaveCalls.Single().Draft.OperationalNotes);
    }

    // 4. Daily Feature fields persist.
    [Fact]
    public async Task DailyFeature_Edit_PersistsTypeAndText()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.SelectedDailyFeatureTypeOption = DailyFeatureTypeOption.For(DailyFeatureType.QuoteOfTheDay);
        viewModel.DailyFeatureText = "\"Ride on!\" — Park motto";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var draft = service.SaveCalls.Last().Draft;
        Assert.Equal(DailyFeatureType.QuoteOfTheDay, draft.DailyFeatureType);
        Assert.Equal("\"Ride on!\" — Park motto", draft.DailyFeatureText);
    }

    // Phase 8A: optional-section selection persists.
    [Fact]
    public async Task SectionSelection_Edit_PersistsToDraft()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.IncludeFastLane = false;
        viewModel.IncludeOperationalNotes = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var selection = service.SaveCalls.Last().Draft.SectionSelection;
        Assert.False(selection.IncludeFastLane);
        Assert.False(selection.IncludeOperationalNotes);
        Assert.True(selection.IncludeDailyAdmission); // every other section stays included by default.
        Assert.True(selection.IncludePromotions);
        Assert.True(selection.IncludeSeasonPasses);
        Assert.True(selection.IncludeAddOns);
        Assert.True(selection.IncludeHauntedAttractions);
    }

    // Phase 8B: SCarowinds/Haunt behaves exactly like the other optional sections — defaults to
    // included, and hiding it persists.
    [Fact]
    public async Task SCarowinds_Edit_PersistsToDraft()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.True(viewModel.IncludeHauntedAttractions); // default before any edit.

        viewModel.IncludeHauntedAttractions = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.False(service.SaveCalls.Last().Draft.SectionSelection.IncludeHauntedAttractions);
    }

    // Phase 8B: reopening a saved date restores its SCarowinds/Haunt selection, matching every other
    // optional section.
    [Fact]
    public async Task SCarowinds_SurvivesDateSwitchAndReload()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.IncludeHauntedAttractions = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        Assert.False(viewModel.IncludeHauntedAttractions);
        Assert.False(viewModel.Workspace!.Draft.SectionSelection.IncludeHauntedAttractions);
    }

    // Phase 8A: reopening a saved date restores its section selection.
    [Fact]
    public async Task SectionSelection_SurvivesDateSwitchAndReload()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.IncludeSeasonPasses = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        Assert.False(viewModel.IncludeSeasonPasses);
        Assert.False(viewModel.Workspace!.Draft.SectionSelection.IncludeSeasonPasses);
    }

    // Phase 17B: Early Ride Time is no longer part of the draft (moved to monthly/override
    // configuration, its own repositories) — see AssociateViewModelEarlyRideTimeTests.cs for its
    // coverage instead of the draft-editing tests this file used to carry.

    // 7. Manual hours override persists.
    [Fact]
    public async Task ManualHoursOverride_Edit_PersistsToDraft()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "11:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var manualOverride = service.SaveCalls.Last().Draft.ManualHoursOverride;
        Assert.True(manualOverride.Enabled);
        Assert.Equal(new TimeOnly(11, 0), manualOverride.OpeningTime);
        Assert.Equal(new TimeOnly(22, 0), manualOverride.ClosingTime);
    }

    // 8. Private buyout / custom hours persists.
    [Fact]
    public async Task PrivateBuyout_Edit_PersistsToDraft()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        viewModel.PrivateBuyoutDetails = "Acme Corp holiday party";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var buyout = service.SaveCalls.Last().Draft.PrivateBuyout;
        Assert.True(buyout.Enabled);
        Assert.Equal(new TimeOnly(18, 0), buyout.OpeningTime);
        Assert.Equal(new TimeOnly(23, 0), buyout.ClosingTime);
        Assert.Equal("Acme Corp holiday party", buyout.Details);
    }

    // 9 & 10. Effective hours (and the North Gate/Avenue times derived from them) react immediately
    // to an override change, entirely via the canonical ParkHoursCalculator — no save round-trip.
    [Fact]
    public async Task ManualHoursOverride_LiveRecompute_UpdatesEffectiveHoursAndGateTimesImmediately()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.Equal(ParkHoursBadgeKind.NotChecked, viewModel.ParkHours!.BadgeKind); // official hours were never checked in the fake default workspace.

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "11:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";

        Assert.Equal(ParkHoursBadgeKind.ManualOverride, viewModel.ParkHours!.BadgeKind);
        Assert.Equal("11:00 AM – 10:00 PM", viewModel.ParkHours.HeadlineText);
        Assert.Equal("10:30 AM", viewModel.ParkHours.NorthGateText);
        Assert.Equal("10:15 AM", viewModel.ParkHours.AvenueText);
    }

    // Regression (Part G/H): Workspace.ParkHours.Effective — not just the live ParkHours getter
    // above — must reflect a manual override immediately once autosave completes, with no date
    // switch or Check Online in between. CreatePdfCommand (via
    // AssociatePassdownDocumentModelBuilder) builds straight from Workspace.ParkHours and never
    // recomputes it itself, so before this was fixed, enabling an override and immediately creating
    // a PDF silently produced official-only hours.
    [Fact]
    public async Task ManualHoursOverride_Autosave_UpdatesWorkspaceParkHoursEffective_Immediately()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "11:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var effective = viewModel.Workspace!.ParkHours.Effective;
        Assert.Equal(EffectiveHoursStatus.Open, effective.Status);
        Assert.True(effective.IsManualOverrideApplied);
        Assert.Equal(new TimeOnly(11, 0), effective.Opening);
        Assert.Equal(new TimeOnly(22, 0), effective.Closing);
    }

    [Fact]
    public async Task PrivateBuyout_Autosave_UpdatesWorkspaceParkHoursEffective_Immediately()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var effective = viewModel.Workspace!.ParkHours.Effective;
        Assert.Equal(EffectiveHoursStatus.Open, effective.Status);
        Assert.True(effective.IsPrivateBuyoutApplied);
        Assert.Equal(new TimeOnly(18, 0), effective.Opening);
        Assert.Equal(new TimeOnly(23, 0), effective.Closing);
    }

    [Fact]
    public async Task ManualHoursOverride_Disabled_RevertsWorkspaceParkHoursEffective_ToOfficial()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "11:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);
        Assert.True(viewModel.Workspace!.ParkHours.Effective.IsManualOverrideApplied);

        viewModel.ManualHoursOverrideEnabled = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var effective = viewModel.Workspace!.ParkHours.Effective;
        Assert.False(effective.IsManualOverrideApplied);
        Assert.Equal(EffectiveHoursStatus.NotCheckedYet, effective.Status); // no official hours in the fake default workspace.
    }

    // Part H #5: an override is date-specific — switching away and back must reload the correct
    // date's own override (or lack of one), not leak the previous date's edit.
    [Fact]
    public async Task ManualHoursOverride_SurvivesDateSwitchAndReload()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "11:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;
        Assert.False(viewModel.ManualHoursOverrideEnabled); // the other date has no override of its own.

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        Assert.True(viewModel.ManualHoursOverrideEnabled);
        Assert.Equal("11:00 AM", viewModel.ManualOpeningTimeText);
        Assert.Equal("10:00 PM", viewModel.ManualClosingTimeText);
        Assert.True(viewModel.Workspace!.ParkHours.Effective.IsManualOverrideApplied);
    }

    [Fact]
    public async Task PrivateBuyout_SurvivesDateSwitchAndReload()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        viewModel.PrivateBuyoutDetails = "Acme Corp holiday party";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        Assert.True(viewModel.PrivateBuyoutEnabled);
        Assert.Equal("6:00 PM", viewModel.PrivateBuyoutOpeningTimeText);
        Assert.Equal("11:00 PM", viewModel.PrivateBuyoutClosingTimeText);
        Assert.Equal("Acme Corp holiday party", viewModel.PrivateBuyoutDetails);
        Assert.True(viewModel.Workspace!.ParkHours.Effective.IsPrivateBuyoutApplied);
    }

    // Part G/H #7: the PDF document model — built exactly the way CreatePdfCommand builds it, from
    // Workspace directly — must carry the effective/manual hours immediately after autosave, with no
    // date switch or Check Online needed first.
    [Fact]
    public async Task ManualHoursOverride_PdfDocumentModel_UsesEffectiveHours_ImmediatelyAfterAutosave()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "11:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var model = AssociatePassdownDocumentModelBuilder.Build(viewModel.Workspace!, FixedNow);

        Assert.Equal("9:00 AM - 11:00 PM", model.ParkHours.HeadlineText);
        Assert.Equal("Manual Override", model.ParkHours.BadgeText);
    }

    [Fact]
    public async Task PrivateBuyout_PdfDocumentModel_UsesBuyoutHours_ImmediatelyAfterAutosave()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        viewModel.PrivateBuyoutDetails = "Acme Corp holiday party";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var model = AssociatePassdownDocumentModelBuilder.Build(viewModel.Workspace!, FixedNow);

        Assert.Equal("6:00 PM - 11:00 PM", model.ParkHours.HeadlineText);
        Assert.Equal("Private Buyout", model.ParkHours.BadgeText);
        Assert.Equal("Acme Corp holiday party", model.ParkHours.PrivateBuyoutDetails);
    }

    // 11. An edit marks the state unsaved immediately.
    [Fact]
    public async Task Edit_MarksStateUnsaved_Immediately()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;
        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);

        viewModel.OperationalNotesText = "Note";

        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);
    }

    // 12. Autosave transitions unsaved -> saving -> saved.
    [Fact]
    public async Task Autosave_TransitionsThroughUnsavedSavingSaved()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        var saveGate = new TaskCompletionSource<bool>();
        service.SaveGates.Enqueue(saveGate);

        viewModel.OperationalNotesText = "Note";
        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);

        scheduler.ElapseLatest(); // drives the debounce -> SaveDraftAsync synchronously up to the gate.
        Assert.Equal(AssociateAutosaveState.Saving, viewModel.AutosaveState);
        Assert.Single(service.SaveCalls);

        saveGate.SetResult(true);
        await viewModel.PendingAutosave;

        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
    }

    // 13. Debounce avoids one save per keystroke.
    [Fact]
    public async Task RapidEdits_AreDebounced_IntoASingleSaveWithTheFinalValue()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "1";
        viewModel.ProjectedAttendanceText = "12";
        viewModel.ProjectedAttendanceText = "123";
        viewModel.ProjectedAttendanceText = "1234";

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Single(service.SaveCalls);
        Assert.Equal(1234, service.SaveCalls.Single().Draft.ProjectedAttendance);
    }

    // 14 & 15. An edit made while a save is already in flight is not lost, is not corrupted by the
    // overlap, and is eventually persisted by a second, serialized save.
    [Fact]
    public async Task EditDuringActiveSave_IsSerializedAndEventuallyPersisted_WithoutCorruption()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        var firstSaveGate = new TaskCompletionSource<bool>();
        service.SaveGates.Enqueue(firstSaveGate);

        viewModel.ProjectedAttendanceText = "1000";
        scheduler.ElapseLatest(); // first save starts and blocks on firstSaveGate.
        Assert.Single(service.SaveCalls);

        // A second edit arrives while the first save is still in flight.
        viewModel.ProjectedAttendanceText = "2000";
        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);

        firstSaveGate.SetResult(true); // let the first save complete.
        scheduler.ElapseLatest(); // the second edit's own debounce elapses; its save waits for the semaphore, then runs.
        await viewModel.PendingAutosave;

        Assert.Equal(2, service.SaveCalls.Count);
        Assert.Equal(1000, service.SaveCalls[0].Draft.ProjectedAttendance);
        Assert.Equal(2000, service.SaveCalls[1].Draft.ProjectedAttendance);
        Assert.Equal(2000, viewModel.Workspace!.Draft.ProjectedAttendance);
        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
    }

    // 16. Changing the date flushes date A's pending edit before date B's workspace is loaded.
    [Fact]
    public async Task DateSwitch_FlushesPreviousDate_BeforeLoadingTheNewDate()
    {
        var (viewModel, service, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "5000"; // unsaved; debounce never elapsed.

        var newDate = Today.AddDays(2);
        viewModel.SelectedDate = newDate;
        await viewModel.PendingLoad;

        Assert.Contains(service.SaveCalls, call => call.OperatingDate == Today && call.Draft.ProjectedAttendance == 5000);
        Assert.Equal(newDate, viewModel.Workspace!.OperatingDate);
        Assert.Empty(service.CheckOnlineCalls); // 18: a date change never performs an online refresh.
    }

    // 17. Returning to date A restores its edits.
    [Fact]
    public async Task DateSwitch_ReturningToThePreviousDate_RestoresItsEdits()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.OperationalNotesText = "Original date note";
        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        Assert.Equal(["Original date note"], viewModel.Workspace!.Draft.OperationalNotes);
        Assert.Equal("Original date note", viewModel.OperationalNotesText);
    }

    // 19. Check Online preserves pending/manual edits (flushed before the recompose).
    [Fact]
    public async Task CheckOnline_PreservesPendingManualEdits()
    {
        var (viewModel, service, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.OperationalNotesText = "Keep an eye on the line queue";
        viewModel.PreSoldsText = "42";

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Equal(["Keep an eye on the line queue"], viewModel.Workspace!.Draft.OperationalNotes);
        Assert.Equal(42, viewModel.Workspace.Draft.PreSolds);
        Assert.Contains(service.SaveCalls, call => call.Draft.PreSolds == 42);
    }

    // 20. Check Online recomposes updated official data while manual fields remain.
    [Fact]
    public async Task CheckOnline_RecomposesUpdatedOfficialData_WhileKeepingManualFields()
    {
        var (viewModel, service, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.OperationalNotesText = "Watch the queue";
        service.OfficialParkHoursAfterCheckFactory = date => new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(20, 0), false, null, null, false, null, null),
            new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, "Verified."),
            OfficialIsClosed: false,
            OfficialOpening: new TimeOnly(9, 0),
            OfficialClosing: new TimeOnly(20, 0));

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Equal("9:00 AM – 8:00 PM", viewModel.ParkHours!.HeadlineText);
        Assert.Equal(["Watch the queue"], viewModel.Workspace!.Draft.OperationalNotes);
    }

    // 21. Invalid numeric input does not crash the app.
    [Fact]
    public async Task InvalidNumericInput_DoesNotCrash_AndSurfacesInlineError()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        var exception = Record.Exception(() => viewModel.ProjectedAttendanceText = "not a number");

        Assert.Null(exception);
        Assert.Equal("Enter a whole number.", viewModel.ProjectedAttendanceError);
    }

    [Fact]
    public async Task NegativeAttendance_IsRejected_WithInlineError()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "-5";

        Assert.Equal("Enter a number 0 or greater.", viewModel.ProjectedAttendanceError);
    }

    // 22. Validation prevents an invalid value from ever reaching the saved draft.
    [Fact]
    public async Task InvalidNumericInput_IsNeverPersisted_UntilCorrected()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "abc"; // invalid: no autosave scheduled for this field.
        Assert.Empty(service.SaveCalls);

        viewModel.ProjectedAttendanceText = "9000"; // corrected.
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Null(viewModel.ProjectedAttendanceError);
        Assert.Equal(9000, service.SaveCalls.Single().Draft.ProjectedAttendance);
    }

    // 23. The screen still works when official data has never been checked.
    [Fact]
    public async Task Screen_WorksWhenOfficialDataIsUnavailable()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        Assert.Equal(ParkHoursBadgeKind.NotChecked, viewModel.ParkHours!.BadgeKind);

        viewModel.ProjectedAttendanceText = "3000";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal(3000, service.SaveCalls.Single().Draft.ProjectedAttendance);
    }

    // 24. No Manager workflow or navigation appears anywhere in the shell.
    [Fact]
    public void MainViewModel_ExposesOnlyTheAssociateWorkflow_NoManagerNavigation()
    {
        var properties = typeof(MainViewModel).GetProperties();

        Assert.DoesNotContain(properties, p => p.Name.Contains("Manager", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(properties, p => p.Name == nameof(MainViewModel.Associate));
    }
}
