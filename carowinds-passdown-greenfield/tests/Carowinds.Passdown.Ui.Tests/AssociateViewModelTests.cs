using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4B Part I ViewModel-level tests for <see cref="AssociateViewModel"/>. Pure C# — no
/// Avalonia rendering involved — using <see cref="FakeAssociatePassdownService"/> so nothing here
/// touches SQLite, HTTP, or Playwright.
/// </summary>
public class AssociateViewModelTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero); // 11:00 AM EDT

    private static AssociateViewModel CreateViewModel(FakeAssociatePassdownService? service = null, FakeClock? clock = null) =>
        new(service ?? new FakeAssociatePassdownService(), clock ?? new FakeClock(FixedNow));

    // 2. Initial date loads locally.
    [Fact]
    public async Task Construction_LoadsInitialDateLocally()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);

        await viewModel.PendingLoad;

        Assert.Single(service.LoadCalls);
        Assert.True(viewModel.HasWorkspace);
        Assert.Empty(service.CheckOnlineCalls);
    }

    [Fact]
    public void Construction_SelectsTodayInEasternTime()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(Today, viewModel.SelectedDate);
    }

    // 3 & 4. Changing the date calls LoadAssociateDateAsync only — never the online check.
    [Fact]
    public async Task SelectedDateChange_CallsLoadAssociateDateAsync_AndNeverCallsCheckOnline()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;
        service.LoadCalls.Clear();

        var newDate = Today.AddDays(3);
        viewModel.SelectedDate = newDate;
        await viewModel.PendingLoad;

        Assert.Equal([newDate], service.LoadCalls);
        Assert.Empty(service.CheckOnlineCalls);
    }

    [Fact]
    public async Task SelectedDateChange_ComposesWorkspaceForTheNewDateOnly()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        var newDate = Today.AddDays(1);
        viewModel.SelectedDate = newDate;
        await viewModel.PendingLoad;

        Assert.Equal(newDate, viewModel.Workspace!.OperatingDate);
    }

    // 5. Check Online invokes CheckAssociateDateOnlineAsync.
    [Fact]
    public async Task CheckOnlineCommand_InvokesCheckAssociateDateOnlineAsync_ForSelectedDate()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        Assert.Equal([Today], service.CheckOnlineCalls);
    }

    // 6. Refresh-in-progress state disables duplicate invocation.
    [Fact]
    public async Task CheckOnlineCommand_CannotExecuteAgain_WhileAlreadyRefreshing()
    {
        var service = new FakeAssociatePassdownService { CheckOnlineGate = new TaskCompletionSource<bool>() };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        var inFlight = viewModel.CheckOnlineCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsCheckingOnline);
        Assert.False(viewModel.CheckOnlineCommand.CanExecute(null));

        service.CheckOnlineGate.SetResult(true);
        await inFlight;

        Assert.False(viewModel.IsCheckingOnline);
        Assert.True(viewModel.CheckOnlineCommand.CanExecute(null));
        Assert.Single(service.CheckOnlineCalls); // the second, blocked attempt never reached the service.
    }

    // 7. A partial refresh result is represented without crashing.
    [Fact]
    public async Task CheckOnlineCommand_PartialFailure_SetsResultMessageAndFailureFlag_WithoutThrowing()
    {
        var service = new FakeAssociatePassdownService
        {
            CheckOnlineResultFactory = date => new AssociateOnlineCheckResult(
                FakeAssociatePassdownService.DefaultWorkspace(date),
                FakeAssociatePassdownService.PartialFailureSummary(date)),
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        var exception = await Record.ExceptionAsync(() => viewModel.CheckOnlineCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.True(viewModel.RefreshHadFailures);
        Assert.Contains("1 source", viewModel.RefreshResultMessage);
        Assert.DoesNotContain("Exception", viewModel.RefreshResultMessage);
    }

    [Fact]
    public async Task CheckOnlineCommand_ServiceThrows_LeavesScreenUsableWithoutThrowing()
    {
        var service = new FakeAssociatePassdownService
        {
            CheckOnlineResultFactory = _ => throw new InvalidOperationException("simulated failure reaching the refresh orchestrator"),
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;
        var workspaceBeforeCheck = viewModel.Workspace;

        var exception = await Record.ExceptionAsync(() => viewModel.CheckOnlineCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.False(viewModel.IsCheckingOnline);
        Assert.True(viewModel.RefreshHadFailures);
        Assert.NotNull(viewModel.RefreshResultMessage);
        Assert.DoesNotContain("InvalidOperationException", viewModel.RefreshResultMessage);
        Assert.Same(workspaceBeforeCheck, viewModel.Workspace); // still usable with whatever was already loaded.
    }

    // 8. Closed hours presentation.
    [Fact]
    public async Task ParkHours_OfficialClosed_PresentsClosedBadgeAndHeadline()
    {
        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed: true, null, null,
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);
        var service = WithParkHours(effective, new AssociateVerificationInfo(AssociateVerificationState.Closed, FixedNow, null, "Closed."), officialIsClosed: true, officialOpening: null, officialClosing: null);
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Equal(ParkHoursBadgeKind.Closed, viewModel.ParkHours!.BadgeKind);
        Assert.Equal("Closed", viewModel.ParkHours.BadgeText);
        Assert.Equal("Park is closed", viewModel.ParkHours.HeadlineText);
    }

    // 9. Verified open hours presentation.
    [Fact]
    public async Task ParkHours_VerifiedOpen_PresentsTimeRangeAndVerifiedText()
    {
        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed: false, new TimeOnly(10, 0), new TimeOnly(21, 0),
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);
        var service = WithParkHours(effective, new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, "Verified."), officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(21, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Equal(ParkHoursBadgeKind.Open, viewModel.ParkHours!.BadgeKind);
        Assert.Equal("10:00 AM – 9:00 PM", viewModel.ParkHours.HeadlineText);
        Assert.Equal("9:30 AM", viewModel.ParkHours.NorthGateText);
        Assert.Equal("9:15 AM", viewModel.ParkHours.AvenueText);
        Assert.Contains("Verified", viewModel.ParkHours.VerificationText);
    }

    // 10. Stale hours presentation.
    [Fact]
    public async Task ParkHours_Stale_PresentsStaleNoteUsingLastVerifiedInformation()
    {
        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed: false, new TimeOnly(10, 0), new TimeOnly(21, 0),
            manualOverrideEnabled: false, null, null,
            privateBuyoutEnabled: false, null, null);
        var verification = new AssociateVerificationInfo(AssociateVerificationState.Stale, FixedNow.AddHours(-6), TimeSpan.FromHours(6), "Could not be rechecked.");
        var service = WithParkHours(effective, verification, officialIsClosed: false, officialOpening: new TimeOnly(10, 0), officialClosing: new TimeOnly(21, 0));
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.NotNull(viewModel.ParkHours!.StaleNoteText);
        Assert.Contains("last verified information", viewModel.ParkHours.StaleNoteText);
        Assert.Contains("may be out of date", viewModel.ParkHours.VerificationText);
    }

    // 11. Not-checked-yet presentation.
    [Fact]
    public async Task ParkHours_NotCheckedYet_PresentsNotCheckedBadge()
    {
        var viewModel = CreateViewModel(); // default fake workspace uses NotCheckedYet hours.
        await viewModel.PendingLoad;

        Assert.Equal(ParkHoursBadgeKind.NotChecked, viewModel.ParkHours!.BadgeKind);
        Assert.Equal("Not checked yet", viewModel.ParkHours.BadgeText);
        Assert.Equal("Hours not checked yet", viewModel.ParkHours.HeadlineText);
    }

    // 12. Category-level Error with zero product rows can still be displayed.
    [Fact]
    public async Task CategoryStatuses_ErrorWithNoProductRows_IsDisplayed()
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                CategoryStatuses = [new AssociateProductCategoryStatus(SourceCategory.DatedAdmissionTickets, AssociateVerificationState.Error, FixedNow, "Could not be checked.")],
            },
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        var status = Assert.Single(viewModel.CategoryStatuses);
        Assert.Equal("Daily Admission", status.CategoryDisplayName);
        Assert.Equal("Unable to verify current pricing", status.StatusText);
        Assert.NotNull(status.DetailText);
    }

    // 13. Category-level Unavailable/NotPublished can be displayed.
    [Theory]
    [InlineData(AssociateVerificationState.Unavailable, "Not available for this date")]
    [InlineData(AssociateVerificationState.NotPublished, "Not published for this date")]
    public async Task CategoryStatuses_UnavailableOrNotPublished_IsDisplayed(AssociateVerificationState state, string expectedText)
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                CategoryStatuses = [new AssociateProductCategoryStatus(SourceCategory.HauntedAttractions, state, FixedNow, "Detail.")],
            },
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        var status = Assert.Single(viewModel.CategoryStatuses);
        Assert.Equal("SCarowinds", status.CategoryDisplayName);
        Assert.Equal(expectedText, status.StatusText);
    }

    [Fact]
    public async Task CategoryStatuses_DoesNotFabricateProductRows_ForAnEmptyCategory()
    {
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                CategoryStatuses = [new AssociateProductCategoryStatus(SourceCategory.DatedFastLane, AssociateVerificationState.Unavailable, FixedNow, null)],
            },
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Empty(viewModel.ProductSummaries);
        Assert.Single(viewModel.CategoryStatuses);
    }

    // 14. Save-state presentation.
    [Fact]
    public async Task SaveStatus_NeverSaved_PresentsNotSavedYet()
    {
        var viewModel = CreateViewModel(); // default fake workspace uses AssociateSaveStatus.NeverSaved.
        await viewModel.PendingLoad;

        Assert.Equal("Not saved yet", viewModel.SaveStatus!.Text);
    }

    [Fact]
    public async Task SaveStatus_Saved_PresentsSavedTimestamp()
    {
        var savedAt = new DateTimeOffset(2026, 8, 21, 18, 14, 0, TimeSpan.Zero);
        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                SaveStatus = new AssociateSaveStatus(AssociateSaveState.Saved, savedAt),
            },
        };
        var viewModel = CreateViewModel(service);
        await viewModel.PendingLoad;

        Assert.Contains("Saved on this computer at", viewModel.SaveStatus!.Text);
    }

    private static FakeAssociatePassdownService WithParkHours(EffectiveHours effective, AssociateVerificationInfo verification, bool officialIsClosed = false, TimeOnly? officialOpening = null, TimeOnly? officialClosing = null) => new()
    {
        WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
        {
            ParkHours = new AssociateParkHoursView(effective, verification, officialIsClosed, officialOpening, officialClosing),
        },
    };
}
