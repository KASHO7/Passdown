using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 18F: the Associate screen's "Copy ▾" Teams actions. Proves the ViewModel orchestration —
/// copies the current on-screen state (including not-yet-saved edits) through the same PDF document
/// model, never checks online / saves / changes the date, and turns clipboard failures into a small
/// message. Text content itself is covered by Documents.Tests' AssociateTeamsTextFormatterTests.
/// Official hours are 11:00 AM – 8:00 PM, so the gate defaults are North Gate 10:30 AM, Avenue 10:15 AM.
/// </summary>
public class AssociateViewModelCopyForTeamsTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Product(string suffix, string name, SourceCategory category, decimal price) => new(
        AssociateProductIdentity.ForDatedProduct(category, suffix),
        name,
        category,
        IsCustom: false,
        price,
        OfficialDisplayPrice: null,
        ComparisonPriceAmount: null,
        PriceQualifier: null,
        DateSpecific: true,
        Today,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, null),
        IncludeOnPdf: true,
        DisplayPriceOverride: null);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, FakeClipboardService Clipboard, ManualDebounceScheduler Scheduler) CreateViewModel(bool withProducts = true)
    {
        var service = new FakeAssociatePassdownService { OfficialHours = (new TimeOnly(11, 0), new TimeOnly(20, 0)) };
        if (withProducts)
        {
            service.WorkspaceFactory = date =>
            {
                var draft = service.StoredDraft(date) ?? AssociateDraftPayload.CreateDefault();
                return FakeAssociatePassdownService.DefaultWorkspace(date) with
                {
                    Draft = draft,
                    ParkHours = new AssociateParkHoursView(
                        ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(20, 0), false, null, null, false, null, null),
                        AssociateVerificationInfo.NotCheckedYet, false, new TimeOnly(11, 0), new TimeOnly(20, 0)),
                    Products =
                    [
                        Product("reg", "Park Ticket", SourceCategory.DatedAdmissionTickets, 59m),
                        Product("reserve", "Fast Lane Reserve", SourceCategory.DatedFastLane, 65m),
                        Product("ultimate", "Fast Lane Ultimate", SourceCategory.DatedFastLane, 109m),
                        Product("gold", "Gold Season Pass", SourceCategory.SeasonPasses, 129m),
                    ],
                };
            };
        }

        var clipboard = new FakeClipboardService();
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay, clipboardService: clipboard);
        return (viewModel, service, clipboard, scheduler);
    }

    [Fact]
    public async Task OperationsSummary_CopiesText_AndConfirmsSubtly()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsSummary);

        var text = Assert.Single(clipboard.Texts);
        Assert.StartsWith("Park Hours: 11:00 AM - 8:00 PM\r\nAvenue: 10:15 AM\r\nNorth Gate: 10:30 AM", text);
        Assert.Equal("Operations summary copied", viewModel.CopyMessage);
        Assert.False(viewModel.CopyMessageIsError);
    }

    [Fact]
    public async Task TodaysPricing_FollowsDefaultPdfSelection_SeasonPassesNotCopied()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.TodaysPricing);

        // A brand-new draft's default selection is Daily Admission + Fast Lane + Promotions (Phase 17D §6).
        Assert.Equal(
            "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\nFast Lane Ultimate: $109.00",
            Assert.Single(clipboard.Texts));
        Assert.Equal("Today's pricing copied", viewModel.CopyMessage);
    }

    [Fact]
    public async Task Copy_NeverChecksOnline_NeverSaves_NeverReloads_NeverChangesDate()
    {
        var (viewModel, service, _, _) = CreateViewModel();
        await viewModel.PendingLoad;
        var loadsBefore = service.LoadCalls.Count;

        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            await viewModel.CopyForTeamsCommand.ExecuteAsync(kind);
        }

        Assert.Empty(service.CheckOnlineCalls);
        Assert.Empty(service.SaveCalls);
        Assert.Equal(loadsBefore, service.LoadCalls.Count);
        Assert.Equal(Today, viewModel.SelectedDate);
    }

    [Fact]
    public async Task UnsavedProductDeselection_IsRespected_WithoutSaving()
    {
        var (viewModel, service, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        var ultimate = viewModel.ProductSections.SelectMany(s => s.Products).Single(p => p.DisplayName == "Fast Lane Ultimate");
        ultimate.IncludeOnPdf = false; // debounce never elapses — the edit is still unsaved.

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.TodaysPricing);

        var text = Assert.Single(clipboard.Texts);
        Assert.DoesNotContain("Ultimate", text);
        Assert.Contains("Fast Lane Reserve: $65.00", text);
        Assert.Empty(service.SaveCalls);
        Assert.Equal(AssociateAutosaveState.Unsaved, viewModel.AutosaveState);

        // The loaded catalog itself is untouched.
        Assert.Contains(viewModel.Workspace!.Products, p => p.DisplayName == "Fast Lane Ultimate" && p.IncludeOnPdf);
    }

    [Fact]
    public async Task UnsavedSectionDeselection_IsRespected()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.IncludeFastLane = false;
        viewModel.IncludeSeasonPasses = true;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.TodaysPricing);

        var text = Assert.Single(clipboard.Texts);
        Assert.DoesNotContain("Fast Lane", text);
        Assert.Contains("Gold Season Pass: $129.00", text);
    }

    [Fact]
    public async Task UnsavedGateOverride_CopiedAsEffectiveTime()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.AvenueOverrideText = "9:45 AM";

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsSummary);

        var text = Assert.Single(clipboard.Texts);
        Assert.Contains("Avenue: 9:45 AM", text);
        Assert.Contains("North Gate: 10:30 AM", text);
        Assert.DoesNotContain("Override", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TodaysPricing_NothingSelected_CopiesNothing_SaysNoPricingSelected()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.IncludeDailyAdmission = false;
        viewModel.IncludeFastLane = false;
        viewModel.IncludePromotions = false;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.TodaysPricing);

        Assert.Empty(clipboard.Texts);
        Assert.Equal("No pricing selected", viewModel.CopyMessage);
        Assert.False(viewModel.CopyMessageIsError);
    }

    [Fact]
    public async Task OperationsAndPricing_NothingSelected_CopiesOperationsOnly_AndSaysSo()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel(withProducts: false);
        await viewModel.PendingLoad;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsAndPricing);

        Assert.DoesNotContain("Ticket Prices", Assert.Single(clipboard.Texts));
        Assert.Equal("Operations summary copied (no pricing selected)", viewModel.CopyMessage);
    }

    [Fact]
    public async Task OperationsAndPricing_CopiesBoth()
    {
        var (viewModel, _, clipboard, _) = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsAndPricing);

        var text = Assert.Single(clipboard.Texts);
        Assert.StartsWith("Park Hours:", text);
        Assert.Contains("\r\n\r\nTicket Prices:\r\n\r\nDaily Admission", text);
        Assert.Equal("Operations and pricing copied", viewModel.CopyMessage);
    }

    [Fact]
    public async Task ClipboardFailure_IsNonFatal_ShowsShortMessage_KeepsDraftEdits()
    {
        var (viewModel, service, clipboard, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;
        clipboard.FailWith = new InvalidOperationException("OpenClipboard failed (0x800401D0) — raw platform detail");

        viewModel.ProjectedAttendanceText = "18000";

        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsSummary);

        Assert.True(viewModel.CopyMessageIsError);
        Assert.Equal("Couldn't copy to the clipboard. Try again.", viewModel.CopyMessage);
        Assert.DoesNotContain("0x8004", viewModel.CopyMessage);

        // The unsaved edit is untouched and still saves normally.
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Equal(18000, service.SaveCalls.Last().Draft.ProjectedAttendance);
    }

    [Fact]
    public async Task CopyMessage_ClearsOnDateChange()
    {
        var (viewModel, _, _, _) = CreateViewModel();
        await viewModel.PendingLoad;
        await viewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.OperationsSummary);
        Assert.NotNull(viewModel.CopyMessage);

        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;

        Assert.Null(viewModel.CopyMessage);
    }

    [Fact]
    public async Task Copy_DisabledWhileLoading()
    {
        var service = new FakeAssociatePassdownService { LoadGate = new TaskCompletionSource<bool>() };
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), clipboardService: new FakeClipboardService());

        Assert.False(viewModel.CopyForTeamsCommand.CanExecute(AssociateTeamsCopyKind.OperationsSummary));

        service.LoadGate.SetResult(true);
        await viewModel.PendingLoad;

        Assert.True(viewModel.CopyForTeamsCommand.CanExecute(AssociateTeamsCopyKind.OperationsSummary));
    }

    /// <summary>
    /// The Copy control is one labeled, keyboard-focusable button whose menu holds the three Phase 18F
    /// actions — unchanged, first, and discoverable without hover — followed by Phase 29's Morning
    /// Message… and OD Notes.
    /// </summary>
    [Fact]
    public async Task View_HasOneLabeledCopyMenu_ThreeOriginalItemsPreserved_ThenMorningMessageAndOdNotes()
    {
        var (viewModel, _, _, _) = CreateViewModel();
        await viewModel.PendingLoad;

        var view = new AssociateView { DataContext = viewModel };

        var copyButton = Assert.Single(view.GetLogicalDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Copy for Teams");
        Assert.True(copyButton.Focusable);
        Assert.True(copyButton.IsTabStop);
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetHelpText(copyButton)));

        var menu = Assert.IsType<MenuFlyout>(copyButton.Flyout);
        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Operations Summary", "Today's Pricing", "Operations + Pricing", "Morning Message…", "OD Notes"], items.Select(i => i.Header as string));

        Assert.Equal(
            [AssociateTeamsCopyKind.OperationsSummary, AssociateTeamsCopyKind.TodaysPricing, AssociateTeamsCopyKind.OperationsAndPricing],
            items.Take(3).Select(i => (AssociateTeamsCopyKind)i.CommandParameter!));
        Assert.All(items, item => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(item))));

        // Command bindings are asserted on the markup (bound values evaluate thread-dependently in this assembly).
        var markup = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(markup, @"Command=""\{Binding CopyForTeamsCommand\}""").Count);
        Assert.Contains("Command=\"{Binding OpenMorningMessageCommand}\"", markup);
        Assert.Contains("Command=\"{Binding CopyOdNotesCommand}\"", markup);
    }
}
