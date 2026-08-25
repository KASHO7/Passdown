using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Tests.Finalization;

/// <summary>
/// Exercises <see cref="FinalizedPassdownFinalizationService"/> against in-memory fakes only (spec
/// Phase 9 Part J/N/O). Nothing here is wired into the running app yet (spec Part R) — this proves
/// the service is ready for a future Phase 9B caller, not that anything calls it today.
/// </summary>
public class FinalizedPassdownFinalizationServiceTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);
    private static readonly DateTimeOffset FinalizedAt = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    private static AssociateWorkspace Workspace(DateOnly date, int? projectedAttendance = 18500) => new(
        date,
        AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = projectedAttendance, OperationalNotes = ["Gate crew short-staffed."] },
        new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
            AssociateVerificationInfo.NotCheckedYet,
            OfficialIsClosed: false,
            OfficialOpening: new TimeOnly(10, 0),
            OfficialClosing: new TimeOnly(21, 0)),
        Products: [],
        CategoryStatuses: [],
        AssociateSaveStatus.NeverSaved);

    private static (FinalizedPassdownFinalizationService Service, InMemoryFinalizedPassdownRepository Repository, FakeFinalizedPassdownSnapshotBuilder Builder) CreateService()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        var builder = new FakeFinalizedPassdownSnapshotBuilder();
        return (new FinalizedPassdownFinalizationService(repository, builder), repository, builder);
    }

    [Fact]
    public async Task FinalizeAsync_NoExistingRecord_CreatesOne()
    {
        var (service, repository, _) = CreateService();

        var result = await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Manual, FinalizedAt);

        Assert.Equal(FinalizationOutcome.Created, result.Outcome);
        Assert.Equal(Date, result.FinalizedPassdown.PassdownDate);
        Assert.Equal(FinalizedAt, result.FinalizedPassdown.FinalizedAtUtc);
        Assert.Equal(FinalizationOrigin.Manual, result.FinalizedPassdown.Origin);
        Assert.Equal(FinalizedPassdownSnapshot.CurrentSchemaVersion, result.FinalizedPassdown.SnapshotSchemaVersion);
        Assert.NotEqual(Guid.Empty, result.FinalizedPassdown.Id);

        Assert.NotNull(await repository.GetByDateAsync(Date));
    }

    [Fact]
    public async Task FinalizeAsync_PopulatesContentHashFromTheStoredSnapshotJson()
    {
        var (service, _, _) = CreateService();

        var result = await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Manual, FinalizedAt);

        var expectedHash = FinalizedPassdownSnapshotSerializer.ComputeContentHash(result.FinalizedPassdown.SnapshotJson);
        Assert.Equal(expectedHash, result.FinalizedPassdown.ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", result.FinalizedPassdown.ContentHash);
    }

    [Fact]
    public async Task FinalizeAsync_CalledTwiceForSameDate_IsIdempotentAndDoesNotRebuild()
    {
        var (service, repository, builder) = CreateService();

        var first = await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Scheduled, FinalizedAt);
        var second = await service.FinalizeAsync(Date, Workspace(Date, projectedAttendance: 99999), FinalizationOrigin.Scheduled, FinalizedAt.AddHours(1));

        Assert.Equal(FinalizationOutcome.Created, first.Outcome);
        Assert.Equal(FinalizationOutcome.AlreadyFinalized, second.Outcome);
        Assert.Equal(first.FinalizedPassdown.Id, second.FinalizedPassdown.Id);
        Assert.Equal(1, builder.CallCount); // the second call never rebuilds/overwrites.

        var summaries = await repository.ListAsync();
        Assert.Single(summaries);
    }

    [Fact]
    public async Task FinalizeAsync_DifferentDates_CreatesSeparateRecords()
    {
        var (service, repository, _) = CreateService();
        var otherDate = Date.AddDays(1);

        await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Manual, FinalizedAt);
        await service.FinalizeAsync(otherDate, Workspace(otherDate), FinalizationOrigin.Manual, FinalizedAt);

        var summaries = await repository.ListAsync();
        Assert.Equal(2, summaries.Count);
    }

    [Fact]
    public async Task FinalizeAsync_NeverCallsAnyNetworkOrRetrievalDependency()
    {
        // The fake builder only ever touches the in-memory AssociateWorkspace handed to it (see
        // FakeFinalizedPassdownSnapshotBuilder) — no HttpClient, Playwright, or retrieval
        // orchestrator is even constructible here, so a passing finalization proves the whole
        // call path (service -> builder -> serializer -> repository) needed none (spec Part J/U).
        var (service, _, builder) = CreateService();

        await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Manual, FinalizedAt);

        Assert.Equal(1, builder.CallCount);
        Assert.Equal(Date, builder.ReceivedWorkspaces[0].OperatingDate);
    }
}
