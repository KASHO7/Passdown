using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35C (docs/phase35c-products-pricing-redesign.md): the Products display price as text and the one anchored
/// price popover — which product and date it edits, Save / Cancel / Use official price through the rows' existing
/// override commands, invalid input, the stale-date guard, custom items, collapse/search view state, and that only a
/// successful Save reaches the draft and the preview. Pure view-model tests over fakes (mixed partition).
/// </summary>
public class Phase35CPriceEditorTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Product(
        string suffix,
        decimal? price = 60m,
        string? officialText = null,
        decimal? displayOverride = null,
        PriceKind kind = PriceKind.Unknown,
        AssociateVerificationState state = AssociateVerificationState.Verified,
        SourceCategory category = SourceCategory.DatedAdmissionTickets,
        bool include = true) => new(
        AssociateProductIdentity.ForDatedProduct(category, suffix),
        $"Ticket {suffix}",
        category,
        IsCustom: false,
        price,
        officialText,
        null,
        null,
        DateSpecific: true,
        Today,
        new AssociateVerificationInfo(state, FixedNow, state == AssociateVerificationState.Stale ? TimeSpan.FromHours(3) : null, "Detail."),
        include,
        displayOverride,
        PriceKind: kind);

    private static async Task<(AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Autosave)> LoadedAsync(
        params AssociateProduct[] products)
    {
        var service = new FakeAssociatePassdownService();
        service.WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
        {
            // Like the real service: a saved override comes back on the product for that date.
            Products = products.Select(p => p with
            {
                DisplayPriceOverride = service.StoredDraft(date)?.ProductOverrides.GetValueOrDefault(p.ProductIdentity)?.DisplayPriceOverride ?? p.DisplayPriceOverride,
            }).ToList(),
        };
        var autosave = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: autosave.Delay);
        await viewModel.PendingLoad;
        return (viewModel, service, autosave);
    }

    private static ProductRowViewModel Row(AssociateViewModel viewModel, string suffix) =>
        viewModel.ProductSections.SelectMany(s => s.Products).Single(r => r.DisplayName == $"Ticket {suffix}");

    private static async Task SaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler autosave)
    {
        autosave.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    // ------------------------------------------------------------------ price display

    [Fact]
    public async Task OfficialValue_NoOverride_ShowsOfficialAsPlainText_NoManualLabel()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        var row = Row(vm, "A");

        Assert.Equal("$50.00", row.OfficialPriceText);
        Assert.Equal("$50.00", row.EffectivePriceText);
        Assert.False(row.HasOverride);
        Assert.False(row.HasNoOfficialValue);
        Assert.True(row.CanEditPrice);
        Assert.Equal("Edit display price for Ticket A, currently $50.00", row.EditPriceAutomationName);
    }

    [Fact]
    public async Task ManualOverride_ShowsEffectiveValue_KeepsOfficialVisible()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m, displayOverride: 44m));
        var row = Row(vm, "A");

        Assert.Equal("$50.00", row.OfficialPriceText); // the official value is never hidden by an override
        Assert.Equal("$44.00", row.EffectivePriceText);
        Assert.True(row.HasOverride);
        Assert.Equal("Manual override", ProductRowViewModel.ManualOverrideText);
    }

    [Fact]
    public async Task NoOfficialValue_IsFlagged_AndNotPresentedAsAPrice()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", price: null));
        var row = Row(vm, "A");

        Assert.Equal("Not priced", row.OfficialPriceText);
        Assert.True(row.HasNoOfficialValue);
        vm.PriceEditor.Open(row, Today);
        Assert.Equal(string.Empty, vm.PriceEditor.EditText); // nothing invented to prefill
    }

    [Fact]
    public async Task TextualPromotionalOfficialWording_IsShownAsIs_AndNotFlaggedMissing()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", price: null, officialText: "3rd Night FREE"));
        var row = Row(vm, "A");

        Assert.Equal("3rd Night FREE", row.OfficialPriceText);
        Assert.Equal("3rd Night FREE", row.EffectivePriceText);
        Assert.False(row.HasNoOfficialValue);
        Assert.True(row.MatchesSearch("night free", "Promotions"));
    }

    [Fact]
    public async Task FreeOfficialPrice_SavedUnchanged_CreatesNoOverride()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", price: 0m));
        var row = Row(vm, "A");
        var officialText = row.OfficialPriceText;

        vm.PriceEditor.Open(row, Today);
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.False(row.HasOverride);
        Assert.Equal(officialText, row.EffectivePriceText);
        Assert.Equal(AssociateAutosaveState.Saved, vm.AutosaveState); // nothing to save
    }

    [Fact]
    public async Task StaleVerifiedData_KeepsItsWarning_AndStaysEditable()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m, state: AssociateVerificationState.Stale));
        var row = Row(vm, "A");

        Assert.Equal("Stale — showing the last verified price.", row.VerificationNoteText);
        Assert.True(row.CanEditPrice);
    }

    [Fact]
    public async Task StartingPrice_PrintNote_And_FromPrefixIsAccepted()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 40m, kind: PriceKind.StartingFrom));
        var row = Row(vm, "A");
        Assert.Equal("From $40.00", row.OfficialPriceText);

        vm.PriceEditor.Open(row, Today);
        Assert.Equal("$40.00", vm.PriceEditor.EditText); // the amount; "From" prints from the row's price kind
        Assert.True(vm.PriceEditor.HasPrintNote);
        vm.PriceEditor.EditText = "From $35";
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Equal(35m, row.DisplayPriceOverride);
        Assert.Equal("From $35.00", row.EffectivePriceText);
    }

    // ------------------------------------------------------------------ opening

    [Fact]
    public async Task EditCommand_OpensTheEditor_OnThatRow_ForTheDateInEdit_PrefilledWithTheEffectiveValue()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m), Product("B", 60m, displayOverride: 55m));

        Row(vm, "B").EditPriceCommand.Execute(null);

        Assert.True(vm.PriceEditor.IsOpen);
        Assert.Same(Row(vm, "B"), vm.PriceEditor.Target);
        Assert.Equal(Today, vm.PriceEditor.OperatingDate);
        Assert.Equal("$55.00", vm.PriceEditor.EditText);
        Assert.Equal("Ticket B", vm.PriceEditor.TargetName);
        Assert.Equal("Official price: $60.00", vm.PriceEditor.OfficialLine);
        Assert.Equal("Edit display price for Ticket B", vm.PriceEditor.AccessibleName);
        Assert.Equal("Display price for Ticket B", vm.PriceEditor.InputAccessibleName);
        Assert.True(vm.PriceEditor.CanUseOfficial);
    }

    [Fact]
    public async Task OnlyOneEditor_OpeningAnother_DiscardsThePendingEdit()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m), Product("B", 60m));
        var closes = new List<bool>();
        vm.PriceEditor.Closed += (_, committed) => closes.Add(committed);

        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "1.00";
        Row(vm, "B").EditPriceCommand.Execute(null);

        Assert.Same(Row(vm, "B"), vm.PriceEditor.Target);
        Assert.Equal("$60.00", vm.PriceEditor.EditText);
        Assert.False(Row(vm, "A").HasOverride); // never saved
        Assert.Equal([false], closes);
    }

    [Fact]
    public async Task RapidSwitching_BetweenProducts_NeverCarriesStagedTextAcross()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m), Product("B", 60m), Product("C", 70m));

        foreach (var suffix in new[] { "A", "B", "C", "A", "C", "B" })
        {
            Row(vm, suffix).EditPriceCommand.Execute(null);
            Assert.Same(Row(vm, suffix), vm.PriceEditor.Target);
            vm.PriceEditor.EditText = "9.99";
        }

        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.Equal(9.99m, Row(vm, "B").DisplayPriceOverride);
        Assert.False(Row(vm, "A").HasOverride);
        Assert.False(Row(vm, "C").HasOverride);
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public async Task Save_AppliesThroughTheExistingOverride_Autosaves_AndCloses()
    {
        var (vm, service, autosave) = await LoadedAsync(Product("A", 50m));
        var row = Row(vm, "A");
        var closes = new List<bool>();
        vm.PriceEditor.Closed += (_, committed) => closes.Add(committed);

        row.EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "$39";
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Equal([true], closes);
        Assert.Equal("$39.00", row.DisplayPriceOverrideText);
        Assert.Equal("$39.00", row.EffectivePriceText);
        Assert.Equal("$50.00", row.OfficialPriceText);

        await SaveAsync(vm, autosave);
        var draft = service.SaveCalls.Last().Draft;
        Assert.Equal(39m, draft.ProductOverrides[row.ProductIdentity].DisplayPriceOverride);
        var stored = Assert.Single(vm.Workspace!.Products);
        Assert.Equal(50m, stored.OfficialPriceAmount); // the official cached value is never modified
        Assert.Equal(39m, stored.EffectiveDisplayPrice);
    }

    [Fact]
    public async Task Enter_InTheEditor_IsTheSaveCommand()
    {
        // The view binds Enter in the input to SaveCommand; the command is the contract (real-window test covers the key).
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "48";

        Assert.True(vm.PriceEditor.SaveCommand.CanExecute(null));
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.Equal(48m, Row(vm, "A").DisplayPriceOverride);
    }

    [Fact]
    public async Task InvalidValue_KeepsTheEditorOpen_WithTheTypedTextAndMessage_AndChangesNothing()
    {
        var (vm, service, autosave) = await LoadedAsync(Product("A", 50m, displayOverride: 45m));
        var row = Row(vm, "A");

        row.EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "Special";
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.True(vm.PriceEditor.IsOpen);
        Assert.Equal("Special", vm.PriceEditor.EditText);
        Assert.True(vm.PriceEditor.HasError);
        Assert.Equal("Enter a valid price, e.g. 24.99.", vm.PriceEditor.ErrorText);
        Assert.Equal(45m, row.DisplayPriceOverride);
        Assert.Null(row.DisplayPriceOverrideError); // the row was never handed invalid text
        Assert.Equal(AssociateAutosaveState.Saved, vm.AutosaveState);
        Assert.Empty(service.SaveCalls);

        vm.PriceEditor.EditText = "-3";
        vm.PriceEditor.SaveCommand.Execute(null);
        Assert.True(vm.PriceEditor.IsOpen);
        Assert.Equal(45m, row.DisplayPriceOverride);

        vm.PriceEditor.EditText = "41";
        vm.PriceEditor.SaveCommand.Execute(null); // fixing it then saves normally
        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Equal(41m, row.DisplayPriceOverride);
        await SaveAsync(vm, autosave);
        Assert.Equal(41m, service.SaveCalls.Last().Draft.ProductOverrides[row.ProductIdentity].DisplayPriceOverride);
    }

    [Fact]
    public async Task BlankValue_OnAnOfficialRow_IsRefusedWithAMessage()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m, displayOverride: 45m));
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "   ";
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.True(vm.PriceEditor.IsOpen);
        Assert.Equal(PriceEditorViewModel.BlankValueMessage, vm.PriceEditor.ErrorText);
        Assert.Equal(45m, Row(vm, "A").DisplayPriceOverride);
    }

    // ------------------------------------------------------------------ cancel / light dismiss / use official

    [Fact]
    public async Task Cancel_DiscardsTheEdit_NoMutation_NoSave()
    {
        var (vm, service, _) = await LoadedAsync(Product("A", 50m, displayOverride: 45m));
        var row = Row(vm, "A");

        row.EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "12";
        vm.PriceEditor.CancelCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Null(vm.PriceEditor.Target);
        Assert.Equal(45m, row.DisplayPriceOverride);
        Assert.Equal("$45.00", row.EffectivePriceText);
        Assert.Equal(AssociateAutosaveState.Saved, vm.AutosaveState);
        Assert.Empty(service.SaveCalls);
    }

    [Fact]
    public async Task LightDismiss_ClosingThePopup_IsCancel()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        var closes = new List<bool>();
        vm.PriceEditor.Closed += (_, committed) => closes.Add(committed);
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "12";

        vm.PriceEditor.IsOpen = false; // what the popup's two-way IsOpen does on a click outside

        Assert.Null(vm.PriceEditor.Target);
        Assert.False(Row(vm, "A").HasOverride);
        Assert.Equal([false], closes);
    }

    [Fact]
    public async Task UseOfficialPrice_ClearsTheOverrideThroughTheExistingCommand_AndSaves()
    {
        var (vm, service, autosave) = await LoadedAsync(Product("A", 50m, displayOverride: 45m));
        var row = Row(vm, "A");

        row.EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "99"; // staged text is irrelevant to Use official
        vm.PriceEditor.UseOfficialCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.False(row.HasOverride);
        Assert.Null(row.DisplayPriceOverrideText);
        Assert.Equal("$50.00", row.EffectivePriceText);
        await SaveAsync(vm, autosave);
        Assert.Null(service.SaveCalls.Last().Draft.ProductOverrides[row.ProductIdentity].DisplayPriceOverride); // cleared, not a copied official string
    }

    [Fact]
    public async Task UseOfficialPrice_IsNotOffered_WithoutAnOverride()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);

        Assert.False(vm.PriceEditor.CanUseOfficial);
        vm.PriceEditor.UseOfficialCommand.Execute(null);
        Assert.True(vm.PriceEditor.IsOpen); // no-op
    }

    // ------------------------------------------------------------------ stale editors

    [Fact]
    public async Task DateSwitch_ClosesTheEditorUnsaved_AndNeverWritesToTheNewDate()
    {
        var (vm, service, autosave) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "12";

        vm.SelectedDate = Today.AddDays(1);
        Assert.False(vm.PriceEditor.IsOpen);
        vm.PriceEditor.SaveCommand.Execute(null); // a late Save does nothing
        await vm.PendingLoad;

        Assert.False(Row(vm, "A").HasOverride);
        Assert.DoesNotContain(service.SaveCalls, call => call.Draft.ProductOverrides.Values.Any(o => o.DisplayPriceOverride == 12m));
        Assert.Equal(Today.AddDays(1), vm.SelectedDate);
    }

    [Fact]
    public void StaleTarget_SaveIsRefused_WithANotice_AndNothingApplied()
    {
        var target = new RecordingTarget();
        var current = true;
        var editor = new PriceEditorViewModel((_, _) => current);
        editor.Open(target, Today);
        editor.EditText = "5";

        current = false; // the date or row list changed while the editor was open
        editor.SaveCommand.Execute(null);

        Assert.False(editor.IsOpen);
        Assert.Equal(0, target.Applied);
        Assert.Equal(PriceEditorViewModel.StaleTargetMessage, editor.Notice);

        editor.Open(target, Today);
        Assert.Null(editor.Notice); // cleared by the next edit
        current = false;
        editor.UseOfficialCommand.Execute(null);
        Assert.Equal(0, target.Reset);
    }

    [Fact]
    public async Task ListRebuild_ClosesTheEditor()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);

        await vm.CheckOnlineCommand.ExecuteAsync(null); // rebuilds ProductSections

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.False(Row(vm, "A").HasOverride);
    }

    [Fact]
    public async Task SectionSwitch_AndSearch_CloseTheEditorUnsaved()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        vm.ShowProductsSectionCommand.Execute(null);

        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.ShowShowsSectionCommand.Execute(null);
        Assert.False(vm.PriceEditor.IsOpen);

        vm.ShowProductsSectionCommand.Execute(null);
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.ProductSearchText = "Ticket";
        Assert.False(vm.PriceEditor.IsOpen);
        Assert.False(Row(vm, "A").HasOverride);
    }

    [Fact]
    public async Task CollapsingTheEditedRowsSection_ClosesTheEditor()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);

        vm.ProductSections.Single().ToggleExpandedCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
    }

    [Fact]
    public async Task SavedOverride_PersistsAfterClosingAndReopeningTheWorkspace()
    {
        var (vm, service, autosave) = await LoadedAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "42.50";
        vm.PriceEditor.SaveCommand.Execute(null);
        await SaveAsync(vm, autosave);

        var reopened = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: new ManualDebounceScheduler().Delay);
        await reopened.PendingLoad;

        var row = Row(reopened, "A");
        Assert.Equal(42.50m, row.DisplayPriceOverride);
        Assert.Equal("$42.50", row.EffectivePriceText);
        Assert.Equal("$50.00", row.OfficialPriceText);
    }

    // ------------------------------------------------------------------ custom items

    [Fact]
    public async Task CustomItem_PriceIsText_EditedThroughTheSamePopover_BlankAllowed_NoUseOfficial()
    {
        var (vm, service, autosave) = await LoadedAsync();
        vm.AddCustomProductCommand.Execute(null);
        var custom = Assert.Single(vm.CustomProducts);
        custom.DisplayNameText = "Group rate";
        Assert.Equal(CustomProductRowViewModel.NoPriceText, custom.PriceDisplayText);

        custom.EditPriceCommand.Execute(null);
        Assert.Same(custom, vm.PriceEditor.Target);
        Assert.Equal(string.Empty, vm.PriceEditor.EditText);
        Assert.False(vm.PriceEditor.CanUseOfficial);
        Assert.Contains("not an official Carowinds price", vm.PriceEditor.OfficialLine, StringComparison.Ordinal);
        vm.PriceEditor.EditText = "25";
        vm.PriceEditor.SaveCommand.Execute(null);

        Assert.Equal("$25.00", custom.PriceDisplayText);
        await SaveAsync(vm, autosave);
        Assert.Equal(25m, service.SaveCalls.Last().Draft.CustomProducts.Single().Price);

        custom.EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "";
        vm.PriceEditor.SaveCommand.Execute(null); // a custom item's price is optional
        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Equal(CustomProductRowViewModel.NoPriceText, custom.PriceDisplayText);
    }

    [Fact]
    public async Task RemovingTheCustomItemBeingEdited_ClosesTheEditor()
    {
        var (vm, _, _) = await LoadedAsync();
        vm.AddCustomProductCommand.Execute(null);
        var custom = vm.CustomProducts.Single();
        custom.EditPriceCommand.Execute(null);

        custom.RemoveCommand.Execute(null);

        Assert.False(vm.PriceEditor.IsOpen);
        Assert.Empty(vm.CustomProducts);
    }

    // ------------------------------------------------------------------ collapse, counts, search (view state only)

    [Fact]
    public async Task SectionSummary_FollowsInclusion_AndCollapseSurvivesARebuild()
    {
        var (vm, _, _) = await LoadedAsync(Product("A", 50m), Product("B", 60m, include: false));
        var section = vm.ProductSections.Single();
        Assert.Equal("2 items · 1 included", section.SummaryText);

        Row(vm, "B").IncludeOnPdf = true;
        Assert.Equal("2 items · 2 included", section.SummaryText);

        section.ToggleExpandedCommand.Execute(null);
        Assert.False(section.IsBodyVisible);
        Assert.EndsWith("collapsed", section.HeaderAccessibleName, StringComparison.Ordinal);

        await vm.CheckOnlineCommand.ExecuteAsync(null);
        Assert.False(vm.ProductSections.Single().IsExpanded); // remembered for the session, by category
    }

    [Fact]
    public async Task Search_FiltersRows_WithoutTouchingTheDraft()
    {
        var (vm, service, _) = await LoadedAsync(Product("Alpha", 50m), Product("Beta", 60m));
        vm.AddCustomProductCommand.Execute(null);
        vm.CustomProducts.Single().DisplayNameText = "Group rate";
        var saveCallsBefore = service.SaveCalls.Count;
        var state = vm.AutosaveState;

        vm.ProductSearchText = "beta";

        Assert.False(Row(vm, "Alpha").IsSearchMatch);
        Assert.True(Row(vm, "Beta").IsSearchMatch);
        Assert.False(vm.CustomProducts.Single().IsSearchMatch);
        Assert.Equal("1 item · 1 included", vm.ProductSections.Single().SummaryText);
        Assert.True(Row(vm, "Alpha").IncludeOnPdf); // filtering never changes what prints

        vm.ProductSearchText = "zzz";
        Assert.True(vm.HasNoProductSearchMatches);
        Assert.False(vm.ProductSections.Single().IsSectionVisible);

        vm.ProductSearchText = "";
        Assert.True(Row(vm, "Alpha").IsSearchMatch);
        Assert.False(vm.HasNoProductSearchMatches);
        Assert.Equal(saveCallsBefore, service.SaveCalls.Count);
        Assert.Equal(state, vm.AutosaveState);
    }

    // ------------------------------------------------------------------ preview

    private static async Task<(AssociateViewModel ViewModel, ContentEchoPdfExportService Export, ManualDebounceScheduler PreviewDebounce)> PreviewRigAsync(AssociateProduct product)
    {
        var service = new FakeAssociatePassdownService { WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with { Products = [product] } };
        var export = new ContentEchoPdfExportService();
        var previewDebounce = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(
            service,
            new FakeClock(FixedNow),
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: export,
            pdfSavePicker: new FakePdfSaveFilePicker(),
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: new FakePdfPrintService(),
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: new InlinePreviewWorkScheduler(),
            previewDelay: previewDebounce.Delay);
        viewModel.Preview.UpdateAvailableWidth(1500);
        viewModel.Preview.UpdateViewport(460, 700, 1.0);
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        previewDebounce.ElapseLatest();
        await viewModel.Preview.PendingRefresh;
        return (viewModel, export, previewDebounce);
    }

    [Fact]
    public async Task Save_RefreshesThePreview_WithTheNewDisplayPrice_AndTheOfficialValueIntact()
    {
        var (vm, export, debounce) = await PreviewRigAsync(Product("A", 50m));
        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "38";
        vm.PriceEditor.SaveCommand.Execute(null);
        debounce.ElapseLatest();
        await vm.Preview.PendingRefresh;

        var printed = Assert.Single(export.Calls[^1].Workspace.Products);
        Assert.Equal(38m, printed.EffectiveDisplayPrice);
        Assert.Equal(50m, printed.OfficialPriceAmount);
    }

    [Fact]
    public async Task Cancel_AndInvalidInput_NeverRefreshThePreview()
    {
        var (vm, export, debounce) = await PreviewRigAsync(Product("A", 50m));
        var exports = export.Calls.Count;
        var generations = vm.Preview.GenerationCount;

        Row(vm, "A").EditPriceCommand.Execute(null);
        vm.PriceEditor.EditText = "nope";
        vm.PriceEditor.SaveCommand.Execute(null);
        vm.PriceEditor.EditText = "12";
        vm.PriceEditor.CancelCommand.Execute(null);
        debounce.ElapseLatest();
        await vm.Preview.PendingRefresh;

        Assert.Equal(exports, export.Calls.Count);
        Assert.Equal(generations, vm.Preview.GenerationCount);
        Assert.Equal(50m, Assert.Single(export.Calls[^1].Workspace.Products).EffectiveDisplayPrice);
    }

    private sealed class RecordingTarget : IPriceEditTarget
    {
        public int Applied { get; private set; }

        public int Reset { get; private set; }

        public string PriceEditorName => "Recorded";

        public string PriceEditorInitialText => "$1.00";

        public string PriceEditorOfficialLine => "Official price: $1.00";

        public string? PriceEditorPrintNote => null;

        public bool PriceEditorCanUseOfficial => true;

        public bool PriceEditorAllowsBlank => false;

        public bool TryApplyPriceEdit(string? text, out string? error)
        {
            Applied++;
            error = null;
            return true;
        }

        public void UsePriceEditorOfficial() => Reset++;
    }
}
