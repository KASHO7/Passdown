using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 13E Parts AH/AI: <see cref="AssociateViewModel"/>-level coverage for the typable Manual
/// Hours/Private Buyout time entry that replaced the stock <c>TimePicker</c> — normalization, invalid
/// text handling, the "required while enabled" rule, disabled-section retention, and autosave
/// interaction. Complements <c>OperationalTimeParserTests</c> (pure parsing, no ViewModel) and the
/// pre-existing round-trip coverage in <see cref="AssociateViewModelDraftEditingTests"/>.
/// </summary>
public class AssociateViewModelOperationalTimeEntryTests
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

    // AH #4 / Part D/P: a non-canonical valid format normalizes to the canonical 12-hour display.
    [Theory]
    [InlineData("930am", "9:30 AM")]
    [InlineData("9", "9:00 AM")]
    [InlineData("10pm", "10:00 PM")]
    [InlineData("10:30 PM", "10:30 PM")] // already canonical — left as-is, not merely re-set.
    public void ManualOpeningTimeText_ValidInput_NormalizesToCanonicalDisplay(string typed, string expectedDisplay)
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = typed;

        Assert.Equal(expectedDisplay, viewModel.ManualOpeningTimeText);
        Assert.Null(viewModel.ManualOpeningTimeError);
    }

    // AH #5 / Part L/N: unparseable text produces validation and never becomes the active value.
    [Fact]
    public async Task ManualOpeningTimeText_InvalidText_ProducesErrorAndLeavesRawTextVisible()
    {
        var (viewModel, _, _) = CreateViewModel();
        await viewModel.PendingLoad;
        viewModel.ManualHoursOverrideEnabled = true;

        viewModel.ManualOpeningTimeText = "abc";

        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, viewModel.ManualOpeningTimeError);
        Assert.Equal("abc", viewModel.ManualOpeningTimeText); // never silently rewritten to midnight/blank.
        Assert.NotNull(viewModel.ParkHours); // reading live effective hours never throws on invalid text.
    }

    // AH #9/#10 / Part N: an invalid keystroke can never overwrite the last valid committed value —
    // it must not become active effective-hours data.
    [Fact]
    public async Task ManualOpeningTimeText_InvalidAfterValid_PreservesLastValidValue_ForEffectiveHoursAndSave()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        // Now type something unparseable into Open without ever completing a new valid value.
        viewModel.ManualOpeningTimeText = "9:";

        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, viewModel.ManualOpeningTimeError);
        Assert.Equal(new TimeOnly(9, 0), viewModel.Workspace!.ParkHours.Effective.Opening); // unchanged from the last valid commit.

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        // No new save was scheduled by the invalid keystroke — the most recent save still reflects 9:00 AM.
        Assert.Equal(new TimeOnly(9, 0), service.SaveCalls.Last().Draft.ManualHoursOverride.OpeningTime);
    }

    // Part R: blank is fine while the section is disabled...
    [Fact]
    public void ManualOpeningTimeText_Blank_WhileDisabled_NoError()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.ManualHoursOverrideEnabled = false;
        viewModel.ManualOpeningTimeText = null;

        Assert.Null(viewModel.ManualOpeningTimeError);
    }

    // ...but required once Manual Hours is enabled (Part R) — and toggling Enabled alone (with no
    // text edit) must (re)compute the error, since RequiredTimeError depends on both.
    [Fact]
    public void ManualOpeningTimeText_Blank_WhileEnabled_ShowsRequiredValidation()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.ManualHoursOverrideEnabled = true;

        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, viewModel.ManualOpeningTimeError);
        Assert.Equal(OperationalTimeParser.InvalidTimeMessage, viewModel.ManualClosingTimeError);
    }

    // Part K: disabling Manual Hours must not clear the typed values (current, audited product
    // behavior — re-enabling should restore what was there, unlike Early Ride Time's own
    // disable-clears convention).
    [Fact]
    public void ManualHoursOverrideEnabled_Disabled_RetainsTypedValues()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:00 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";

        viewModel.ManualHoursOverrideEnabled = false;

        Assert.Equal("9:00 AM", viewModel.ManualOpeningTimeText);
        Assert.Equal("10:00 PM", viewModel.ManualClosingTimeText);
        Assert.Null(viewModel.ManualOpeningTimeError); // not required while disabled.
    }

    [Fact]
    public void PrivateBuyoutEnabled_Disabled_RetainsTypedValues()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";

        viewModel.PrivateBuyoutEnabled = false;

        Assert.Equal("6:00 PM", viewModel.PrivateBuyoutOpeningTimeText);
        Assert.Equal("11:00 PM", viewModel.PrivateBuyoutClosingTimeText);
        Assert.Null(viewModel.PrivateBuyoutOpeningTimeError);
    }

    // Part M: the open<close ordering check remains a soft, non-blocking warning, unchanged from
    // pre-13E behavior — distinct from the per-field parse/required errors.
    [Fact]
    public async Task ManualHours_CloseBeforeOpen_WarnsButStillSaves()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "10:00 PM";
        viewModel.ManualClosingTimeText = "9:00 AM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Equal("Closing time must be later than opening time.", viewModel.ManualHoursWarningText);
        Assert.Null(viewModel.ManualOpeningTimeError);
        Assert.Null(viewModel.ManualClosingTimeError);
        var saved = service.SaveCalls.Last().Draft.ManualHoursOverride;
        Assert.Equal(new TimeOnly(22, 0), saved.OpeningTime);
        Assert.Equal(new TimeOnly(9, 0), saved.ClosingTime);
    }

    // AI #3/#5: an invalid/partial keystroke never triggers a save of its own; only a valid commit
    // (re)starts the debounce, and correcting the text afterward resumes normal autosave.
    [Fact]
    public async Task InvalidText_DoesNotScheduleAutosave_ValidCorrectionResumesIt()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualClosingTimeText = "10:00 PM"; // valid — schedules autosave.
        var scheduleCountAfterValidClose = scheduler.ScheduleCount;
        Assert.True(scheduleCountAfterValidClose > 0);

        viewModel.ManualOpeningTimeText = "9:"; // partial/invalid — must NOT schedule a new autosave.
        Assert.Equal(scheduleCountAfterValidClose, scheduler.ScheduleCount);

        viewModel.ManualOpeningTimeText = "9:00 AM"; // corrected — resumes normal autosave.
        Assert.True(scheduler.ScheduleCount > scheduleCountAfterValidClose);

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var saved = service.SaveCalls.Last().Draft.ManualHoursOverride;
        Assert.Equal(new TimeOnly(9, 0), saved.OpeningTime);
        Assert.Equal(new TimeOnly(22, 0), saved.ClosingTime);
    }

    // AH #1-3 / Part S: an existing saved draft loads into the new text fields already normalized.
    [Fact]
    public async Task ExistingSavedDraft_LoadsIntoTextFields_AlreadyNormalized()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.ManualHoursOverrideEnabled = true;
        viewModel.ManualOpeningTimeText = "9:30 AM";
        viewModel.ManualClosingTimeText = "10:00 PM";
        viewModel.PrivateBuyoutEnabled = true;
        viewModel.PrivateBuyoutOpeningTimeText = "6:00 PM";
        viewModel.PrivateBuyoutClosingTimeText = "11:00 PM";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        // Simulate a fresh load of the same date (e.g. app restart) by constructing a new ViewModel
        // against the same backing service/store.
        var reloaded = new AssociateViewModel(service, new FakeClock(FixedNow));
        await reloaded.PendingLoad;

        Assert.Equal("9:30 AM", reloaded.ManualOpeningTimeText);
        Assert.Equal("10:00 PM", reloaded.ManualClosingTimeText);
        Assert.Equal("6:00 PM", reloaded.PrivateBuyoutOpeningTimeText);
        Assert.Equal("11:00 PM", reloaded.PrivateBuyoutClosingTimeText);
    }
}
