using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
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
    }

    [Fact]
    public void ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns()
    {
        var markup = File.ReadAllText(FindRepoFile("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

        Assert.Contains("StringFormat='Include {0} on the PDF'", markup);
        Assert.Contains("StringFormat='Display price override for {0}'", markup);
        Assert.Contains("StringFormat='Use official price for {0}'", markup);
        Assert.Contains("StringFormat='Remove {0}'", markup);
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
