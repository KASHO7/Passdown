using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4F Part L/Q: proves the redesigned Associate screen still resolves through Avalonia's real
/// XAML loader with a fully populated workspace (products, custom items — every DataTemplate/
/// StaticResource path at once, spec Part Q #3/#4/#20), and that the header/action-bar controls spec
/// Part L calls out by name carry an accessible label. Per-row product/custom-item automation names
/// are template text (<c>StringFormat</c> bound to each row's own display name) rather than static
/// strings an unattached headless container reliably realizes — <see cref="ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns"/>
/// guards those directly against the markup instead, the same technique
/// <see cref="NoManagerWorkflowTests.MainWindowMarkup_ContainsNoManagerText"/> already uses.
/// </summary>
public class AssociateViewAccessibilityTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);

    [Fact]
    public async Task AssociateView_WithPopulatedWorkspace_ConstructsAndExposesCriticalAutomationNames()
    {
        var product = new AssociateProduct(
            AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "1day"),
            "1-Day Ticket",
            SourceCategory.DatedAdmissionTickets,
            IsCustom: false,
            OfficialPriceAmount: 54.99m,
            OfficialDisplayPrice: null,
            ComparisonPriceAmount: null,
            PriceQualifier: null,
            DateSpecific: true,
            Today,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null),
            IncludeOnPdf: true,
            DisplayPriceOverride: null);

        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => new AssociateWorkspace(
                date,
                AssociateDraftPayload.CreateDefault() with
                {
                    CustomProducts = [new CustomProductEntry("c1", "Staff Meal Voucher", 8.00m, "Cafeteria only.", true)],
                },
                new AssociateParkHoursView(
                    ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
                    new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null),
                    OfficialIsClosed: false, OfficialOpening: new TimeOnly(9, 0), OfficialClosing: new TimeOnly(21, 0)),
                Products: [product],
                CategoryStatuses: [],
                AssociateSaveStatus.NeverSaved),
        };

        var viewModel = new AssociateViewModel(service, new FakeClock(DateTimeOffset.UtcNow));
        await viewModel.PendingLoad;

        Assert.True(viewModel.HasProductSections);
        Assert.True(viewModel.HasCustomProducts);

        // Constructing/assigning DataContext alone exercises every StaticResource/converter lookup
        // and every top-level DataTemplate the compiled bindings reference (spec Part Q #20).
        var view = new AssociateView { DataContext = viewModel };

        var names = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(control => AutomationProperties.GetName(control))
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet();

        Assert.Contains("Previous day", names);
        Assert.Contains("Next day", names);
        Assert.Contains("Operating date", names);
        Assert.Contains("Check this date online", names);
        Assert.Contains("Create PDF", names);
        Assert.Contains("Save status", names);
        Assert.Contains("Operational notes", names);
        Assert.Contains("Add a custom or internal product", names);

        // Phase 13E Part AC: the redesigned typable Manual Hours/Private Buyout fields carry
        // meaningful automation names (present in the logical tree regardless of the section's
        // current IsVisible state, exactly like every other compiled-binding AutomationProperties.Name
        // in this view).
        Assert.Contains("Use a manual hours override", names);
        Assert.Contains("Manual Hours Open", names);
        Assert.Contains("Manual Hours Close", names);
        Assert.Contains("Private buyout or custom hours", names);
        Assert.Contains("Private Buyout Open", names);
        Assert.Contains("Private Buyout Close", names);

        // Phase 17D §3: the single compact Early Ride Time [Edit] action, and the fields inside its
        // one combined Monthly Default/Date Override panel, carry static accessible names, present in
        // the logical tree regardless of the panel's current IsVisible state — the resolver-driven
        // status line's own accessible name is a data-bound ViewModel property (a StringFormat
        // binding) and, like the per-row product/custom-item template names above, is not reliably
        // realized by this unattached headless container — guarded directly against the markup
        // instead, see <see cref="ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns"/>.
        Assert.Contains("Edit Early Ride Time", names);
        Assert.Contains("Use an Early Ride Time override for this date", names);
        Assert.Contains("Monthly Early Ride Time rides", names);

        // Phase 11D Part AS: the two persistent-note section headings are always present, regardless
        // of whether any note is currently included/available.
        Assert.Contains("Included persistent operational notes", names);
        Assert.Contains("Available persistent operational notes", names);
    }

    // Phase 11D Part BA: structural/binding validation for a populated Included + Available
    // persistent-notes state (several selections, Important + Normal, a long title/body) — no raster
    // renderer exists in this environment (see docs/phase11-operational-notes.md), so this proves
    // every binding/DataTemplate path constructs cleanly rather than claiming a pixel PASS.
    [Fact]
    public async Task AssociateView_WithSelectedAndAvailablePersistentNotes_ConstructsCleanly()
    {
        var longTitle = new string('T', 180);
        var longBody = new string('B', 3500);
        var selection = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Important,
            Today, null, DateTimeOffset.UtcNow);
        var longSelection = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), longTitle, longBody,
            OperationalNoteCategory.Operations, OperationalNotePriority.Normal,
            Today, null, DateTimeOffset.UtcNow);

        var suggestion = new OperationalNote(
            Guid.NewGuid(), "Group check-in support at North Gate", null,
            OperationalNoteCategory.GuestServices, OperationalNotePriority.Normal,
            Today, Today, OperationalNoteStatus.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);

        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => new AssociateWorkspace(
                date,
                AssociateDraftPayload.CreateDefault() with { SelectedOperationalNotes = [selection, longSelection] },
                new AssociateParkHoursView(
                    ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
                    new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null),
                    OfficialIsClosed: false, OfficialOpening: new TimeOnly(9, 0), OfficialClosing: new TimeOnly(21, 0)),
                Products: [],
                CategoryStatuses: [],
                AssociateSaveStatus.NeverSaved),
        };
        service.RelevantOperationalNotesByDate[Today] = [suggestion];

        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(2026, 8, 21, 15, 0, 0, TimeSpan.Zero)));
        await viewModel.PendingLoad;

        Assert.Equal(2, viewModel.SelectedOperationalNotes.Count);
        Assert.Single(viewModel.AvailableOperationalNoteSuggestions);

        var view = new AssociateView { DataContext = viewModel };
        Assert.NotNull(view);
    }

    // Phase 11E Parts G-L: every source-comparison state constructs cleanly, including the
    // Replace-with-Current-Version action (ContentChanged) and the three informational-only states
    // that deliberately show no Replace action (Resolved/NoLongerApplicable/Missing).
    [Fact]
    public async Task AssociateView_WithSourceChangedSelections_ConstructsCleanly()
    {
        var changed = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Important, Today, null, DateTimeOffset.UtcNow);
        var resolved = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "Group check-in support at North Gate", null,
            OperationalNoteCategory.GuestServices, OperationalNotePriority.Normal, Today, null, DateTimeOffset.UtcNow);
        var noLongerApplicable = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "Storm drainage backup near Fury 325", null,
            OperationalNoteCategory.Operations, OperationalNotePriority.Normal, Today, null, DateTimeOffset.UtcNow);
        var missing = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "Temporary staffing shortage", null,
            OperationalNoteCategory.Staffing, OperationalNotePriority.Normal, Today, null, DateTimeOffset.UtcNow);

        var sourceStates = new Dictionary<Guid, OperationalNoteSourceComparisonState>
        {
            [changed.SourceOperationalNoteId] = OperationalNoteSourceComparisonState.ContentChanged,
            [resolved.SourceOperationalNoteId] = OperationalNoteSourceComparisonState.Resolved,
            [noLongerApplicable.SourceOperationalNoteId] = OperationalNoteSourceComparisonState.NoLongerApplicable,
            [missing.SourceOperationalNoteId] = OperationalNoteSourceComparisonState.Missing,
        };

        var service = new FakeAssociatePassdownService
        {
            WorkspaceFactory = date => new AssociateWorkspace(
                date,
                AssociateDraftPayload.CreateDefault() with { SelectedOperationalNotes = [changed, resolved, noLongerApplicable, missing] },
                new AssociateParkHoursView(
                    ParkHoursCalculator.Calculate(false, new TimeOnly(9, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
                    new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null),
                    OfficialIsClosed: false, OfficialOpening: new TimeOnly(9, 0), OfficialClosing: new TimeOnly(21, 0)),
                Products: [],
                CategoryStatuses: [],
                AssociateSaveStatus.NeverSaved,
                sourceStates),
        };

        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(2026, 8, 21, 15, 0, 0, TimeSpan.Zero)));
        await viewModel.PendingLoad;

        Assert.Equal(4, viewModel.SelectedOperationalNotes.Count);
        Assert.True(viewModel.SelectedOperationalNotes.Single(n => n.SourceOperationalNoteId == changed.SourceOperationalNoteId).ShowReplaceAction);
        Assert.False(viewModel.SelectedOperationalNotes.Single(n => n.SourceOperationalNoteId == resolved.SourceOperationalNoteId).ShowReplaceAction);
        Assert.False(viewModel.SelectedOperationalNotes.Single(n => n.SourceOperationalNoteId == noLongerApplicable.SourceOperationalNoteId).ShowReplaceAction);
        Assert.False(viewModel.SelectedOperationalNotes.Single(n => n.SourceOperationalNoteId == missing.SourceOperationalNoteId).ShowReplaceAction);

        var view = new AssociateView { DataContext = viewModel };
        Assert.NotNull(view);
    }

    [Fact]
    public void ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns()
    {
        var markup = File.ReadAllText(FindRepoFile("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("StringFormat='Include {0} on the PDF'", markup);
        Assert.Contains("StringFormat='Display price override for {0}'", markup);
        Assert.Contains("StringFormat='Use official price for {0}'", markup);
        Assert.Contains("StringFormat='Remove {0}'", markup);
        Assert.Contains("StringFormat='Remove {0} from this passdown'", markup);
        Assert.Contains("StringFormat='Replace {0} with current version'", markup);
        Assert.Contains("StringFormat='Add note {0}'", markup);

        // Phase 17D §2: the resolver-driven, rides-only Early Ride status line's accessible name.
        Assert.Contains("StringFormat='Early Ride: {0}'", markup);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }
}
