using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

internal sealed class InMemoryUpdateStateStore : IUpdateStateStore
{
    public UpdateLocalState State { get; set; } = UpdateLocalState.Empty;

    public int Saves { get; private set; }

    public UpdateLocalState Load() => State;

    public Task<bool> SaveAsync(UpdateLocalState state, CancellationToken cancellationToken = default)
    {
        State = state;
        Saves++;
        return Task.FromResult(true);
    }
}

/// <summary>Phase 23D: when checks happen, how they are coalesced and backed off, and the Remind Me Later / acknowledgement state.</summary>
public class ApplicationUpdateCoordinatorTests
{
    private static readonly SemanticVersion Installed = SemanticVersion.Parse("0.1.0-rc.7");

    private readonly FakeReleaseSource _source = new();
    private readonly InMemoryUpdateStateStore _store = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));

    private ApplicationUpdateCoordinator Coordinator(bool configured = true, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new UpdateDiscoveryService(_source, new FakeManifestVerifier(), UpdateFixtures.Configuration()), _store, _clock, Installed, configured, delay);

    [Fact]
    public async Task NotConfigured_NeverChecks_EvenWhenAsked()
    {
        var coordinator = Coordinator(configured: false);
        coordinator.Start();
        var snapshot = await coordinator.CheckNowAsync();

        Assert.Equal(UpdateAvailabilityStatus.NotConfigured, snapshot.Status);
        Assert.Null(coordinator.LoopTask);
        Assert.Equal(0, _source.ListCalls);
    }

    [Fact]
    public async Task ManualCheck_FindsUpdate_RecordsLastSuccessfulCheck_AndNotifies()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        var coordinator = Coordinator();
        var changes = new List<UpdateAvailabilityStatus>();
        coordinator.Changed += (_, s) => changes.Add(s.Status);

        var snapshot = await coordinator.CheckNowAsync();

        Assert.Equal(UpdateAvailabilityStatus.UpdateAvailable, snapshot.Status);
        Assert.True(snapshot.ShouldNotify);
        Assert.Equal(_clock.UtcNow, _store.State.LastSuccessfulCheckUtc);
        Assert.Equal([UpdateAvailabilityStatus.Checking, UpdateAvailabilityStatus.UpdateAvailable], changes);
    }

    [Fact]
    public async Task OverlappingChecks_ShareOneRequest()
    {
        _source.ListGate = new TaskCompletionSource();
        var coordinator = Coordinator();
        var first = coordinator.CheckNowAsync();
        var second = coordinator.CheckNowAsync();
        Assert.Same(first, second);

        _source.ListGate.SetResult();
        await first;
        Assert.Equal(1, _source.ListCalls);
    }

    [Fact]
    public async Task RepeatedManualPresses_WithinAMinute_ReuseTheResult()
    {
        var coordinator = Coordinator();
        await coordinator.CheckNowAsync();
        _clock.UtcNow += TimeSpan.FromSeconds(20);
        await coordinator.CheckNowAsync();
        Assert.Equal(1, _source.ListCalls);

        _clock.UtcNow += TimeSpan.FromMinutes(2);
        await coordinator.CheckNowAsync();
        Assert.Equal(2, _source.ListCalls);
    }

    [Fact]
    public async Task RateLimit_BlocksFurtherChecksUntilReset_AndPersistsTheBackoff()
    {
        var reset = _clock.UtcNow + TimeSpan.FromMinutes(40);
        _source.ListFailure = new UpdateSourceException(UpdateSourceFailureKind.RateLimited, "synthetic", reset);
        var coordinator = Coordinator();

        var snapshot = await coordinator.CheckNowAsync();
        Assert.Equal(UpdateAvailabilityStatus.CheckFailed, snapshot.Status);
        Assert.Equal(reset, _store.State.NextCheckNotBeforeUtc);

        _clock.UtcNow += TimeSpan.FromMinutes(5);
        await coordinator.CheckNowAsync();
        Assert.Equal(1, _source.ListCalls);
    }

    [Fact]
    public async Task Offline_IsAFailedCheck_WithBackoff_AndDoesNotThrow()
    {
        _source.ListFailure = new UpdateSourceException(UpdateSourceFailureKind.Offline, "synthetic");
        var snapshot = await Coordinator().CheckNowAsync();

        Assert.Equal(UpdateAvailabilityStatus.CheckFailed, snapshot.Status);
        Assert.Equal(UpdateSourceFailureKind.Offline, snapshot.FailureKind);
        Assert.Equal(_clock.UtcNow + ApplicationUpdateCoordinator.FailureBackoff, _store.State.NextCheckNotBeforeUtc);
    }

    [Fact]
    public async Task FailureAfterAnOffer_KeepsTheVerifiedOffer()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        var coordinator = Coordinator();
        await coordinator.CheckNowAsync();

        _source.ListFailure = new UpdateSourceException(UpdateSourceFailureKind.Offline, "synthetic");
        _clock.UtcNow += TimeSpan.FromMinutes(5);
        var snapshot = await coordinator.CheckNowAsync();

        Assert.Equal(UpdateAvailabilityStatus.CheckFailed, snapshot.Status);
        Assert.NotNull(snapshot.Offer);
    }

    [Fact]
    public async Task RemindMeLater_HidesTheIndicatorForThatVersionOnly()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        var coordinator = Coordinator();
        var snapshot = await coordinator.CheckNowAsync();

        await coordinator.SnoozeAsync(snapshot.Offer!.Version);
        Assert.False(coordinator.Current.ShouldNotify);
        Assert.Equal("0.1.0-rc.8", _store.State.SnoozedVersion);

        // A restart re-reads the snooze: still quiet for rc.8.
        _clock.UtcNow += TimeSpan.FromHours(7);
        var restarted = Coordinator();
        Assert.False((await restarted.CheckNowAsync()).ShouldNotify);

        // A newer version is a new reminder.
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.9"));
        _clock.UtcNow += TimeSpan.FromMinutes(5);
        Assert.True((await restarted.CheckNowAsync()).ShouldNotify);
    }

    [Fact]
    public async Task Snooze_ExpiresAfterSevenDays()
    {
        _source.Releases.Add(FakeReleaseSource.Release("0.1.0-rc.8"));
        var coordinator = Coordinator();
        await coordinator.SnoozeAsync(SemanticVersion.Parse("0.1.0-rc.8"));

        _clock.UtcNow += ApplicationUpdateCoordinator.SnoozeDuration + TimeSpan.FromMinutes(1);
        Assert.True((await coordinator.CheckNowAsync()).ShouldNotify);
    }

    [Fact]
    public async Task AutomaticLoop_WaitsForStartupDelay_ThenChecks_ThenWaitsTheInterval()
    {
        var delays = new List<TimeSpan>();
        var blockForever = new TaskCompletionSource();
        var coordinator = Coordinator(delay: (delay, token) =>
        {
            delays.Add(delay);
            return delays.Count <= 1 ? Task.CompletedTask : blockForever.Task.WaitAsync(token);
        });

        coordinator.Start();
        await WaitUntil(() => delays.Count >= 2);

        Assert.Equal(ApplicationUpdateCoordinator.StartupDelay, delays[0]);
        Assert.Equal(ApplicationUpdateCoordinator.AutomaticInterval, delays[1]);
        Assert.Equal(1, _source.ListCalls);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task AutomaticLoop_RespectsAPersistedBackoffAcrossRestarts()
    {
        _store.State = _store.State with { NextCheckNotBeforeUtc = _clock.UtcNow + TimeSpan.FromMinutes(20) };
        var delays = new List<TimeSpan>();
        var blockForever = new TaskCompletionSource();
        var coordinator = Coordinator(delay: (delay, token) =>
        {
            delays.Add(delay);
            return delays.Count <= 1 ? Task.CompletedTask : blockForever.Task.WaitAsync(token);
        });

        coordinator.Start();
        await WaitUntil(() => delays.Count >= 2);

        Assert.Equal(TimeSpan.FromMinutes(20), delays[1]);
        Assert.Equal(0, _source.ListCalls);
        await coordinator.StopAsync();
    }

    [Fact]
    public async Task AcknowledgedReleaseNotes_ArePersistedLocally()
    {
        var coordinator = Coordinator();
        await coordinator.AcknowledgeReleaseNotesAsync(SemanticVersion.Parse("0.1.0-rc.8"));
        Assert.Equal("0.1.0-rc.8", _store.State.LastAcknowledgedReleaseNotesVersion);
        Assert.Equal("0.1.0-rc.8", Coordinator().LastAcknowledgedReleaseNotesVersion);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
