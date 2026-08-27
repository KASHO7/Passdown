using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4D Part M: ViewModel-level tests for product/category editing, include/exclude,
/// display-price override, custom products, and autosave/refresh preservation of all of the above.
/// Pure C# — no Avalonia rendering — using <see cref="FakeAssociatePassdownService"/> and
/// <see cref="ManualDebounceScheduler"/>, exactly like <c>AssociateViewModelDraftEditingTests</c>.
///
/// Application-layer composition (reconciliation, catalog/dated merge, category status derivation)
/// is already exhaustively covered by <c>AssociatePassdownServiceTests</c> — these tests instead
/// prove the ViewModel wiring on top of it: that <see cref="ProductRowViewModel"/>/
/// <see cref="CustomProductRowViewModel"/> edits reach <see cref="AssociateViewModel.SaveDraftAsync"/>-bound
/// drafts through the same single debounce/save pipeline as every other field, with no second save
/// queue and no lost edits across a date switch or Check Online.
/// </summary>
public class AssociateViewModelProductEditingTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) CreateViewModel(
        Func<DateOnly, AssociateWorkspace>? workspaceFactory = null)
    {
        var service = new FakeAssociatePassdownService { WorkspaceFactory = workspaceFactory };
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay);
        return (viewModel, service, scheduler);
    }

    private static async Task ElapseAndAwaitSaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler)
    {
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    private static AssociateProduct SampleProduct(
        string identitySuffix = "PKG-A",
        SourceCategory category = SourceCategory.DatedAdmissionTickets,
        string name = "Sample Ticket",
        decimal? price = 60m,
        decimal? comparisonPrice = null,
        string? qualifier = null,
        bool includeOnPdf = true,
        decimal? displayPriceOverride = null,
        AssociateVerificationState verificationState = AssociateVerificationState.Verified) => new(
        AssociateProductIdentity.ForDatedProduct(category, identitySuffix),
        name,
        category,
        IsCustom: false,
        price,
        OfficialDisplayPrice: null,
        comparisonPrice,
        qualifier,
        DateSpecific: true,
        Today,
        new AssociateVerificationInfo(verificationState, FixedNow, verificationState == AssociateVerificationState.Stale ? TimeSpan.FromHours(3) : null, "Detail."),
        includeOnPdf,
        displayPriceOverride);

    // 1 & 2. Official products render dynamically, grouped by their actual category — nothing about
    // an unusual/never-before-seen product name is special-cased anywhere in the ViewModel.
    [Fact]
    public async Task OfficialProducts_RenderDynamically_GroupedByActualCategory_NoHardCodedNames()
    {
        var product = SampleProduct(name: "Totally Unexpected Brand New Attraction Pass", category: SourceCategory.HauntedAttractions);
        var (viewModel, _, _) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var section = Assert.Single(viewModel.ProductSections);
        Assert.Equal("SCarowinds", section.CategoryDisplayName);
        var row = Assert.Single(section.Products);
        Assert.Equal("Totally Unexpected Brand New Attraction Pass", row.DisplayName);
    }

    // Phase 15A §16: the Associate Products screen groups by the same ProductDisplaySectionResolver
    // the PDF uses — Military/First Responder folds into Daily Admission rather than getting its own
    // section.
    [Fact]
    public async Task MilitaryFirstResponderProduct_RendersUnderDailyAdmission_NotItsOwnSection()
    {
        var product = SampleProduct(name: "Military & First Responder Tickets", category: SourceCategory.MilitaryFirstResponder);
        var (viewModel, _, _) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var section = Assert.Single(viewModel.ProductSections);
        Assert.Equal("Daily Admission", section.CategoryDisplayName);
        Assert.Contains(section.Products, r => r.DisplayName == "Military & First Responder Tickets");
    }

    // Phase 15A §16: Bring-A-Friend Duo's own dated category never gets a section (product row or
    // status banner) on the Associate Products screen — its real-world offer is represented by its
    // Promotions catalog counterpart instead.
    [Fact]
    public async Task BringAFriendDuoDatedProduct_NeverRendersItsOwnSection()
    {
        var product = SampleProduct(name: "Celebrate America! Bring-A-Friend Duo Offer", category: SourceCategory.BringAFriendDuo);
        var (viewModel, _, _) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        Assert.Empty(viewModel.ProductSections);
    }

    // 26. An empty category (no rows, no status entry) is not built as a section at all.
    [Fact]
    public async Task EmptyCategory_WithNoStatus_DoesNotAppearAsASection()
    {
        var (viewModel, _, _) = CreateViewModel(); // default fake workspace: no products, no category statuses.
        await viewModel.PendingLoad;

        Assert.Empty(viewModel.ProductSections);
        Assert.False(viewModel.HasProductSections);
    }

    // 4 & 5. Include toggle updates the draft and autosaves.
    [Fact]
    public async Task IncludeToggle_UpdatesDraft_AndAutosaves()
    {
        var product = SampleProduct();
        var (viewModel, service, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.IncludeOnPdf = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var draft = service.SaveCalls.Last().Draft;
        Assert.False(draft.ProductOverrides[product.ProductIdentity].IncludeOnPdf);
    }

    // 9. A valid price override persists and 10. the effective displayed price uses it.
    [Fact]
    public async Task PriceOverride_ValidValue_PersistsAndComputesEffectivePrice()
    {
        var product = SampleProduct(price: 60m);
        var (viewModel, service, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.DisplayPriceOverrideText = "45.00";

        Assert.True(row.HasOverride);
        Assert.Equal("$45.00", row.EffectivePriceText);

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var draft = service.SaveCalls.Last().Draft;
        Assert.Equal(45.00m, draft.ProductOverrides[product.ProductIdentity].DisplayPriceOverride);
    }

    // 10 (reset half). Reset removes the override and restores the official price.
    [Fact]
    public async Task ResetToOfficial_RemovesOverride_AndRestoresOfficialPrice()
    {
        var product = SampleProduct(price: 60m, displayPriceOverride: 45m, includeOnPdf: false);
        var (viewModel, service, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        Assert.True(row.HasOverride);

        row.ResetToOfficialCommand.Execute(null);

        Assert.False(row.HasOverride);
        Assert.Equal("$60.00", row.EffectivePriceText);

        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var draft = service.SaveCalls.Last().Draft;
        Assert.Null(draft.ProductOverrides[product.ProductIdentity].DisplayPriceOverride);
    }

    // 11. Invalid override text does not corrupt the last valid draft state and surfaces inline validation.
    [Fact]
    public async Task PriceOverride_InvalidText_DoesNotCorruptLastValidOverride()
    {
        var product = SampleProduct(price: 60m);
        var (viewModel, service, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.DisplayPriceOverrideText = "45.00";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        row.DisplayPriceOverrideText = "not a price";

        Assert.NotNull(row.DisplayPriceOverrideError);
        Assert.Equal(45.00m, row.DisplayPriceOverride); // last valid value untouched.

        var draft = service.SaveCalls.Last().Draft; // no new save was triggered by the invalid edit.
        Assert.Equal(45.00m, draft.ProductOverrides[product.ProductIdentity].DisplayPriceOverride);
    }

    // 12 & 13 & 14. Comparison price, qualifier, and official price are source-backed and never mutated by draft editing.
    [Fact]
    public async Task ComparisonPriceQualifierAndOfficialPrice_RemainSourceBacked_AfterDraftEdits()
    {
        var product = SampleProduct(price: 70m, comparisonPrice: 90m, qualifier: "Online Price");
        var (viewModel, _, _) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.IncludeOnPdf = false;
        row.DisplayPriceOverrideText = "10.00";

        Assert.Equal("$70.00", row.OfficialPriceText);
        Assert.Equal("$90.00", row.ComparisonPriceText);
        Assert.Equal("Online Price", row.PriceQualifier);
    }

    // 15, 16, 17, 18. Custom products can be added, edited (including Notes), and removed.
    [Fact]
    public async Task CustomProduct_CanBeAdded_Edited_AndRemoved()
    {
        var (viewModel, service, scheduler) = CreateViewModel(); // default per-date store; no official products involved.
        await viewModel.PendingLoad;

        viewModel.AddCustomProductCommand.Execute(null);
        Assert.True(viewModel.HasCustomProducts);
        var row = Assert.Single(viewModel.CustomProducts);

        row.DisplayNameText = "Group Rate Special";
        row.PriceText = "15.00";
        row.NotesText = "Ask a manager";
        row.IncludeOnPdf = true;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var saved = Assert.Single(service.SaveCalls.Last().Draft.CustomProducts);
        Assert.Equal("Group Rate Special", saved.DisplayName);
        Assert.Equal(15.00m, saved.Price);
        Assert.Equal("Ask a manager", saved.Notes);
        Assert.True(saved.IncludeOnPdf);

        row.RemoveCommand.Execute(null);
        Assert.False(viewModel.HasCustomProducts);
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        Assert.Empty(service.SaveCalls.Last().Draft.CustomProducts);
    }

    // 19. Custom products survive a save/load round trip (through the fake's real per-date draft store).
    [Fact]
    public async Task CustomProduct_SurvivesSaveLoadRoundTrip()
    {
        var (viewModel, _, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.AddCustomProductCommand.Execute(null);
        var row = viewModel.CustomProducts.Single();
        row.DisplayNameText = "Employee Family Day";
        row.NotesText = "Contact HR";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var otherDate = Today.AddDays(1);
        viewModel.SelectedDate = otherDate;
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        var reloaded = Assert.Single(viewModel.CustomProducts);
        Assert.Equal("Employee Family Day", reloaded.DisplayNameText);
        Assert.Equal("Contact HR", reloaded.NotesText);
    }

    // 20. Custom products are unaffected by Check Online (through the fake's real per-date draft store).
    [Fact]
    public async Task CustomProduct_SurvivesCheckOnline()
    {
        var (viewModel, service, scheduler) = CreateViewModel();
        await viewModel.PendingLoad;

        viewModel.AddCustomProductCommand.Execute(null);
        viewModel.CustomProducts.Single().DisplayNameText = "Group Rate Special";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        await viewModel.CheckOnlineCommand.ExecuteAsync(null);

        var afterCheck = Assert.Single(viewModel.CustomProducts);
        Assert.Equal("Group Rate Special", afterCheck.DisplayNameText);
        Assert.Contains(service.SaveCalls, call => call.Draft.CustomProducts.Any(c => c.DisplayName == "Group Rate Special"));
    }

    // 21. Custom products never get an official-looking (catalog/dated-prefixed) identity.
    [Fact]
    public void CustomProduct_NeverGetsAnOfficialLookingId()
    {
        var (viewModel, _, _) = CreateViewModel();

        viewModel.AddCustomProductCommand.Execute(null);

        var id = viewModel.CustomProducts.Single().Id;
        Assert.DoesNotContain("dated|", id, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog|", id, StringComparison.Ordinal);
    }

    // Phase 13D Part AC/AD/AG: a saved price override must reach Workspace.Products immediately — this
    // is what AssociatePassdownDocumentModelBuilder (and therefore the generated PDF) actually reads
    // via AssociateProduct.EffectiveDisplayPrice, not Draft.ProductOverrides directly. Before the 13D
    // fix, SaveDraftNowAsync updated Draft/SaveStatus/ParkHours but left Products holding whatever the
    // last full compose produced, so CreatePdfAsync's PDF (built from Workspace) silently kept showing
    // the official price even though the draft itself was saved correctly.
    [Fact]
    public async Task PriceOverride_AfterAutosave_FlowsIntoWorkspaceProducts_ForPdfGeneration()
    {
        var product = SampleProduct(price: 60m);
        var (viewModel, _, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.DisplayPriceOverrideText = "45.00";
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var workspaceProduct = Assert.Single(viewModel.Workspace!.Products);
        Assert.Equal(45.00m, workspaceProduct.DisplayPriceOverride);
        Assert.Equal(45.00m, workspaceProduct.EffectiveDisplayPrice);
    }

    // Same fix, generic second product (not Bring-A-Friend/any specific name) proving the fix is not
    // product-name-specific, plus proves IncludeOnPdf flows through the same path.
    [Fact]
    public async Task IncludeToggle_AfterAutosave_FlowsIntoWorkspaceProducts_ForPdfGeneration()
    {
        var product = SampleProduct(identitySuffix: "PKG-B", name: "Ordinary Season Pass", category: SourceCategory.SeasonPasses);
        var (viewModel, _, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.IncludeOnPdf = false;
        await ElapseAndAwaitSaveAsync(viewModel, scheduler);

        var workspaceProduct = Assert.Single(viewModel.Workspace!.Products);
        Assert.False(workspaceProduct.IncludeOnPdf);
    }

    // 27. A product edit made while a save is already in flight is not lost.
    [Fact]
    public async Task ProductEditDuringActiveSave_IsEventuallyPersisted()
    {
        var product = SampleProduct();
        var (viewModel, service, scheduler) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var firstSaveGate = new TaskCompletionSource<bool>();
        service.SaveGates.Enqueue(firstSaveGate);

        var row = viewModel.ProductSections.Single().Products.Single();
        row.IncludeOnPdf = false;
        scheduler.ElapseLatest(); // first save starts and blocks on firstSaveGate.
        Assert.Single(service.SaveCalls);

        row.DisplayPriceOverrideText = "33.00"; // a second edit arrives while the first save is still in flight.

        firstSaveGate.SetResult(true);
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;

        Assert.Equal(2, service.SaveCalls.Count);
        var finalOverride = service.SaveCalls.Last().Draft.ProductOverrides[product.ProductIdentity];
        Assert.False(finalOverride.IncludeOnPdf);
        Assert.Equal(33.00m, finalOverride.DisplayPriceOverride);
    }

    // 28. Date switch flushes a pending product edit before the new date's workspace loads.
    [Fact]
    public async Task DateSwitch_FlushesPendingProductEdits()
    {
        var product = SampleProduct();
        var (viewModel, service, _) = CreateViewModel(date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] });
        await viewModel.PendingLoad;

        var row = viewModel.ProductSections.Single().Products.Single();
        row.IncludeOnPdf = false; // unsaved; debounce never elapsed.

        var newDate = Today.AddDays(2);
        viewModel.SelectedDate = newDate;
        await viewModel.PendingLoad;

        Assert.Contains(
            service.SaveCalls,
            call => call.OperatingDate == Today && call.Draft.ProductOverrides[product.ProductIdentity].IncludeOnPdf == false);
    }
}
