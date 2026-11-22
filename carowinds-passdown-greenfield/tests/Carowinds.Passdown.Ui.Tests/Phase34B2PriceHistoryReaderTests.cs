using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34B-2: the Price History overlay reads the synced observations once per open (a reader) instead of once
/// per product click, and replaces that reader whenever its inputs can have changed. Deterministic fakes only.
/// </summary>
public class Phase34B2PriceHistoryReaderTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class Notifier : IDataChangeNotifier
    {
        public event EventHandler<DataChange>? Changed;

        public void Publish(DataChange change) => Changed?.Invoke(this, change);
    }

    /// <summary>Counts how the view model reads: readers opened, and history answered by a reader vs the service.</summary>
    private sealed class CountingService(FakeSeasonPriceHistoryService inner) : ISeasonPriceHistoryService
    {
        public int ReadersOpened;
        public int ServiceHistoryReads;
        public int ReaderHistoryReads;
        public TaskCompletionSource? HoldNextOpen;

        public FakeSeasonPriceHistoryService Inner => inner;

        public Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default) => inner.ListTrackedProductsAsync(cancellationToken);

        public Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
        {
            ServiceHistoryReads++;
            return inner.GetHistoryAsync(productKey, cancellationToken);
        }

        public async Task<ISeasonPriceHistoryReader> OpenReaderAsync(CancellationToken cancellationToken = default)
        {
            ReadersOpened++;
            var products = inner.Products.ToList(); // the snapshot this reader answers the list from
            if (HoldNextOpen is { } hold)
            {
                HoldNextOpen = null;
                await hold.Task;
            }

            return new Reader(this, products);
        }

        private sealed class Reader(CountingService owner, List<PriceHistoryProduct> products) : ISeasonPriceHistoryReader
        {
            public Task<IReadOnlyList<PriceHistoryProduct>> ListTrackedProductsAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<PriceHistoryProduct>>(products);

            public Task<IReadOnlyList<PriceHistoryEntry>> GetHistoryAsync(string productKey, CancellationToken cancellationToken = default)
            {
                owner.ReaderHistoryReads++;
                return owner.Inner.GetHistoryAsync(productKey, cancellationToken);
            }
        }
    }

    private static FakeSeasonPriceHistoryService Data() => new FakeSeasonPriceHistoryService()
        .With("gold", "Gold Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (99m, PriceKind.StandardCatalog, At))
        .With("silver", "Silver Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (79m, PriceKind.StandardCatalog, At))
        .With("platinum", "Platinum Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (149m, PriceKind.StandardCatalog, At));

    private static (PriceHistoryViewModel Vm, CountingService Service, Notifier Notifier) Create()
    {
        var service = new CountingService(Data());
        var notifier = new Notifier();
        return (new PriceHistoryViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher()), service, notifier);
    }

    [Fact]
    public async Task SwitchingProducts_UsesTheOneReaderOpenedForThisVisit()
    {
        var (vm, service, _) = Create();
        await vm.OpenAsync("gold");

        foreach (var product in vm.Products.ToList())
        {
            vm.SelectedProduct = product;
            await vm.PendingHistoryLoad;
            Assert.Single(vm.Rows);
        }

        Assert.Equal(1, service.ReadersOpened);
        Assert.Equal(0, service.ServiceHistoryReads);
        Assert.True(service.ReaderHistoryReads >= 3);
    }

    [Theory]
    [InlineData(DataChangeKind.OfficialPriceHistory)]
    [InlineData(DataChangeKind.OfficialData)]
    public async Task AChangeToSyncedObservationsOrOfficialData_ReplacesTheReader_KeepingTheSelection(DataChangeKind kind)
    {
        var (vm, service, notifier) = Create();
        await vm.OpenAsync("silver");
        await vm.PendingHistoryLoad;

        notifier.Publish(new DataChange(kind));
        await vm.PendingRefresh;
        await vm.PendingHistoryLoad;

        Assert.Equal(2, service.ReadersOpened);
        Assert.Equal("silver", vm.SelectedProduct!.ProductKey);
        Assert.Equal(0, service.ServiceHistoryReads);
    }

    [Fact]
    public async Task UnrelatedChanges_KeepTheReader()
    {
        var (vm, service, notifier) = Create();
        await vm.OpenAsync(null);

        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft));
        notifier.Publish(new DataChange(DataChangeKind.PersonalSync));
        notifier.Publish(new DataChange(DataChangeKind.OperationalNotes));
        await vm.PendingRefresh;

        Assert.Equal(1, service.ReadersOpened);
    }

    [Fact]
    public async Task Closing_DropsTheReader_AndReopeningReadsFreshData()
    {
        var (vm, service, notifier) = Create();
        await vm.OpenAsync("gold");
        vm.CloseCommand.Execute(null);

        notifier.Publish(new DataChange(DataChangeKind.OfficialPriceHistory)); // closed: nothing is read
        await vm.PendingRefresh;
        Assert.Equal(1, service.ReadersOpened);

        // A selection change while closed never answers from the dropped reader.
        var readsBefore = service.ReaderHistoryReads;
        vm.SelectedProduct = vm.Products.Single(p => p.ProductKey == "platinum");
        await vm.PendingHistoryLoad;
        Assert.Equal(readsBefore, service.ReaderHistoryReads);
        Assert.Equal(1, service.ServiceHistoryReads);

        await vm.OpenAsync("silver");
        Assert.Equal(2, service.ReadersOpened);
    }

    [Fact]
    public async Task AnOlderOpenFinishingLate_NeverReplacesTheNewerList()
    {
        var (vm, service, notifier) = Create();
        var hold = new TaskCompletionSource();
        service.HoldNextOpen = hold;

        var first = vm.OpenAsync("gold"); // waits inside OpenReaderAsync with the original three products
        Assert.False(first.IsCompleted);

        // Meanwhile a sync merge adds a product and a refresh completes with the newer list.
        service.Inner.With("diamond", "Diamond Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (199m, PriceKind.StandardCatalog, At));
        notifier.Publish(new DataChange(DataChangeKind.OfficialPriceHistory));
        await vm.PendingRefresh;
        Assert.Contains(vm.Products, p => p.ProductKey == "diamond");

        hold.SetResult();
        await first;

        Assert.Contains(vm.Products, p => p.ProductKey == "diamond"); // the stale three-product list was discarded
        Assert.Equal(2, service.ReadersOpened);
    }
}
