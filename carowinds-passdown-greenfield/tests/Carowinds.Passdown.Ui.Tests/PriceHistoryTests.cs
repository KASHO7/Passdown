using Avalonia.Controls;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>Phase 20A: Season Price History formatting, screen behavior and Products wiring.</summary>
public class PriceHistoryTests
{
    // 16:00 UTC = noon Eastern (EDT), so the Eastern calendar date equals the UTC date here.
    private static DateTimeOffset On(int month, int day) => new(2026, month, day, 16, 0, 0, TimeSpan.Zero);

    private static PriceHistoryEntry Entry(int sequence, decimal amount, PriceKind kind, DateTimeOffset at, PriceHistoryEntry? previous = null)
    {
        var interval = new SeasonPriceHistoryInterval("k", sequence, amount, kind, null, null, "Gold", "src", at, at.AddDays(2), SeasonPriceHistoryOrigin.Observed);
        return new PriceHistoryEntry(interval, previous is null ? null : SeasonPriceChange.Between(previous.Interval.State, interval.State));
    }

    [Fact]
    public void Formatting_UsesUsCurrencyAndDates_AndExactFromSemantics()
    {
        Assert.Equal("$105.00", PriceHistoryPresentation.FormatPrice(105m, PriceKind.StandardCatalog));
        Assert.Equal("From $105.00", PriceHistoryPresentation.FormatPrice(105m, PriceKind.StartingFrom));
        Assert.Equal("$1,099.00", PriceHistoryPresentation.FormatPrice(1099m, PriceKind.StandardCatalog));
        Assert.Equal("Sep 19, 2026", PriceHistoryPresentation.FormatDate(On(9, 19)));
    }

    [Fact]
    public void Rows_ShowIncreaseDecreaseAndBaseline()
    {
        var a = Entry(1, 99m, PriceKind.StandardCatalog, On(8, 12));
        var b = Entry(2, 105m, PriceKind.StandardCatalog, On(9, 19), a);
        var c = Entry(3, 95m, PriceKind.StandardCatalog, On(10, 10), b);

        var rows = PriceHistoryPresentation.BuildRows([a, b, c]);

        Assert.Equal(["—", "+$6.00 (+6.1%)", "-$10.00 (-9.5%)"], rows.Select(r => r.ChangeText));
        Assert.Equal(["Aug 12, 2026", "Sep 19, 2026", "Oct 10, 2026"], rows.Select(r => r.FirstSeenText));
        Assert.Equal("Aug 14, 2026", rows[0].LastConfirmedText);
        Assert.True(rows[1].IsIncrease);
        Assert.True(rows[2].IsDecrease);
        Assert.All(rows, r => Assert.False(r.HasNote));
    }

    [Fact]
    public void Rows_SemanticOnlyChange_ShowsAZeroDeltaAndAVisibleNote()
    {
        var a = Entry(1, 105m, PriceKind.StandardCatalog, On(9, 1));
        var b = Entry(2, 105m, PriceKind.StartingFrom, On(9, 5), a);

        var row = PriceHistoryPresentation.BuildRows([a, b])[1];

        Assert.Equal("$0.00", row.ChangeText);
        Assert.Equal("From $105.00", row.PriceText);
        Assert.Equal("Price type changed: $105.00 → From $105.00.", row.NoteText);
    }

    [Fact]
    public void Rows_ReappearanceAtTheSamePrice_IsExplained()
    {
        var a = Entry(1, 105m, PriceKind.StandardCatalog, On(9, 1));
        var b = Entry(2, 105m, PriceKind.StandardCatalog, On(10, 1), a);

        Assert.Equal("Published again after being retired.", PriceHistoryPresentation.BuildRows([a, b])[1].NoteText);
    }

    [Theory]
    [InlineData(0, 10, "+$10.00")]               // previous zero: no nonsense percentage
    [InlineData(899, 900, "+$1.00 (+0.1%)")]
    [InlineData(100000, 100001, "+$1.00 (+<0.1%)")]
    public void Change_PercentEdgeCases(int before, int after, string expected) =>
        Assert.Equal(expected, PriceHistoryPresentation.FormatChange(
            SeasonPriceChange.Between(new SeasonPriceState(before, PriceKind.StandardCatalog), new SeasonPriceState(after, PriceKind.StandardCatalog))));

    private static FakeSeasonPriceHistoryService Service() => new FakeSeasonPriceHistoryService()
        .With("gold", "2027 Gold Pass", active: true, PriceHistoryFamily.SeasonPass,
            (99m, PriceKind.StandardCatalog, On(8, 12)), (105m, PriceKind.StandardCatalog, On(9, 19)))
        .With("silver", "2027 Silver Pass", active: true, PriceHistoryFamily.SeasonPass, (79m, PriceKind.StandardCatalog, On(8, 12)))
        .With("dining", "All Season Dining", active: true, PriceHistoryFamily.SeasonPassAddOn)
        .With("old", "2026 Gold Pass", active: false, PriceHistoryFamily.SeasonPass, (89m, PriceKind.StandardCatalog, On(5, 1)));

    [Fact]
    public async Task Open_ListsActiveTrackedProducts_AndSelectsTheRequestedOne()
    {
        var vm = new PriceHistoryViewModel(Service());

        await vm.OpenAsync("gold");

        Assert.True(vm.IsOpen);
        Assert.Equal(["2027 Gold Pass", "2027 Silver Pass", "All Season Dining"], vm.Products.Select(p => p.DisplayName));
        Assert.Equal("gold", vm.SelectedProduct!.ProductKey);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Null(vm.StatusText);
        Assert.Equal("Price history available starting from Aug 12, 2026.", vm.AvailabilityText);
    }

    [Fact]
    public async Task OneBaselineState_ShowsThePrice_AndNoChangesYet()
    {
        var vm = new PriceHistoryViewModel(Service());

        await vm.OpenAsync("silver");

        Assert.Single(vm.Rows);
        Assert.Equal("$79.00", vm.Rows[0].PriceText);
        Assert.Equal(PriceHistoryPresentation.NoChangesYetText, vm.StatusText);
    }

    [Fact]
    public async Task TrackedProductWithoutAnyState_SaysSoWithoutImplyingNoHistoryExists()
    {
        var vm = new PriceHistoryViewModel(Service());

        await vm.OpenAsync("dining");

        Assert.Empty(vm.Rows);
        Assert.Equal(PriceHistoryPresentation.NoStatesText, vm.StatusText);
    }

    [Fact]
    public async Task RetiredProduct_IsHiddenByDefault_ButReachable()
    {
        var vm = new PriceHistoryViewModel(Service());
        await vm.OpenAsync(null);
        Assert.DoesNotContain(vm.Products, p => p.ProductKey == "old");

        vm.IncludeInactive = true;

        var old = Assert.Single(vm.Products, p => p.ProductKey == "old");
        Assert.Equal("Season Pass · Retired", old.DetailText);
    }

    [Fact]
    public async Task OpeningARetiredProductDirectly_TurnsOnIncludeRetired()
    {
        var vm = new PriceHistoryViewModel(Service());

        await vm.OpenAsync("old");

        Assert.True(vm.IncludeInactive);
        Assert.Equal("old", vm.SelectedProduct!.ProductKey);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task Search_FiltersByName()
    {
        var vm = new PriceHistoryViewModel(Service());
        await vm.OpenAsync(null);

        vm.SearchText = "dining";

        Assert.Equal(["All Season Dining"], vm.Products.Select(p => p.DisplayName));
        Assert.Equal("dining", vm.SelectedProduct!.ProductKey);
    }

    [Fact]
    public async Task NothingTrackedYet_ShowsTheEmptyState()
    {
        var vm = new PriceHistoryViewModel(new FakeSeasonPriceHistoryService());

        await vm.OpenAsync(null);

        Assert.True(vm.ShowNoProducts);
        Assert.False(vm.HasProducts);
    }

    [Fact]
    public async Task LoadFailure_IsShownInline_AndCloseStillWorks()
    {
        var vm = new PriceHistoryViewModel(new FakeSeasonPriceHistoryService { FailList = true });

        await vm.OpenAsync(null);
        Assert.Equal(PriceHistoryPresentation.LoadErrorText, vm.StatusText);

        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsOpen);
    }

    private static AssociateProduct Product(string name, SourceCategory category, string? priceHistoryKey) =>
        new AssociateProduct(
            AssociateProductIdentity.ForCatalogProduct($"{category}:id:{name}"), name, category, IsCustom: false,
            105m, "$105", null, null, DateSpecific: false, ApplicableDate: null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UtcNow, null, null),
            IncludeOnPdf: true, DisplayPriceOverride: null)
        {
            PriceHistoryProductKey = priceHistoryKey,
        };

    [Fact]
    public void ProductRow_ExposesPriceHistory_OnlyForTrackedProducts()
    {
        string? opened = null;
        var tracked = new ProductRowViewModel(Product("2027 Gold Pass", SourceCategory.SeasonPasses, "SeasonPasses:id:gold"), () => { }, key => opened = key);
        var daily = new ProductRowViewModel(Product("Park Ticket", SourceCategory.DatedAdmissionTickets, null), () => { }, key => opened = key);

        Assert.True(tracked.HasPriceHistory);
        Assert.False(daily.HasPriceHistory);

        tracked.ViewPriceHistoryCommand.Execute(null);
        Assert.Equal("SeasonPasses:id:gold", opened);
    }

    [Fact]
    public async Task AssociateRequest_OpensTheOverlay_WithoutChangingTheSection()
    {
        var main = MainWithPriceHistory(out var associate, out var priceHistory);
        main.ShowAssociateSectionCommand.Execute(null);
        Assert.False(main.IsPriceHistoryOpen);

        associate.OpenPriceHistoryCommand.Execute(null);
        await priceHistory.PendingHistoryLoad;

        Assert.True(main.IsPriceHistoryOpen);
        Assert.Equal(AppSection.AssociatePassdown, main.SelectedSection);

        priceHistory.CloseCommand.Execute(null);
        Assert.False(main.IsPriceHistoryOpen);
    }

    private static MainViewModel MainWithPriceHistory(out AssociateViewModel associate, out PriceHistoryViewModel priceHistory)
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 19, 15, 0, 0, TimeSpan.Zero));
        associate = new AssociateViewModel(new FakeAssociatePassdownService(), clock);
        priceHistory = new PriceHistoryViewModel(Service());
        return new MainViewModel(
            new OperationsDashboardViewModel(new FakeOperationsDashboardService()),
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), clock),
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), clock),
            new PassdownHistoryViewModel(new FakePassdownHistoryService(), new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()),
            priceHistory);
    }

    [Fact]
    public async Task PriceHistoryView_ConstructsThroughTheRealXamlLoader()
    {
        var vm = new PriceHistoryViewModel(Service());
        await vm.OpenAsync("gold");

        var view = new PriceHistoryView { DataContext = vm };

        Assert.NotNull(view.FindControl<ListBox>("ProductList"));
        Assert.NotNull(view.FindControl<TextBox>("SearchBox"));
    }

    [Fact]
    public void Markup_KeyboardAndAccessibility()
    {
        var view = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PriceHistoryView.axaml"));
        Assert.Contains("<KeyBinding Gesture=\"Escape\" Command=\"{Binding CloseCommand}\" />", view);
        Assert.Contains("AutomationProperties.Name=\"Tracked products\"", view);
        Assert.Contains("AutomationProperties.Name=\"{Binding AutomationName}\"", view);
        Assert.DoesNotContain("Chart", view);

        var associate = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        Assert.Contains("IsVisible=\"{Binding HasPriceHistory}\"", associate);
        Assert.Contains("Command=\"{Binding OpenPriceHistoryCommand}\"", associate);

        var shell = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml"));
        Assert.Contains("IsVisible=\"{Binding IsPriceHistoryOpen}\"", shell);
    }
}

internal static class MarkupFiles
{
    public static string Find(string relativePath)
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
