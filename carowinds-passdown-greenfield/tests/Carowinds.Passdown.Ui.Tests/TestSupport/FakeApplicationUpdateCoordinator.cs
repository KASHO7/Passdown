using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Phase 23D: an update coordinator whose state the test sets directly. No network.</summary>
public sealed class FakeApplicationUpdateCoordinator : IApplicationUpdateCoordinator
{
    public UpdateAvailabilitySnapshot Current { get; private set; } = new(UpdateAvailabilityStatus.NotChecked, null, null, null, null, false);

    public event EventHandler<UpdateAvailabilitySnapshot>? Changed;

    public string? LastAcknowledgedReleaseNotesVersion { get; private set; }

    public void Publish(UpdateAvailabilitySnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }

    public void Start()
    {
    }

    public Task StopAsync() => Task.CompletedTask;

    public Task<UpdateAvailabilitySnapshot> CheckNowAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SnoozeAsync(SemanticVersion version) => Task.CompletedTask;

    public Task AcknowledgeReleaseNotesAsync(SemanticVersion version)
    {
        LastAcknowledgedReleaseNotesVersion = version.Normalized;
        return Task.CompletedTask;
    }

    /// <summary>A verified-looking offer for display tests (no real signature involved at this layer).</summary>
    public static UpdateOffer Offer(string version = "0.1.0-rc.8")
    {
        var parsed = SemanticVersion.Parse(version);
        var manifest = new UpdateManifest(1, UpdateManifest.CarowindsOperationsProductId, parsed, UpdateChannel.ReleaseCandidate, "win-x64",
            new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), "Updates built into Settings",
            "## Highlights\n- Install approved updates from Settings.\n- What's New appears once after an update.\n## Fixes\n- Saving is safer when storage fails.",
            new UpdatePackageDescriptor(UpdateAssetNames.Package(parsed), 94_371_840, new string('a', 64), UpdateAssetNames.ReleaseName(parsed)), []);
        return new UpdateOffer(new ReleaseCandidate(UpdateAssetNames.Tag(parsed), parsed, null, null, false, []), manifest, 94_371_840);
    }
}
