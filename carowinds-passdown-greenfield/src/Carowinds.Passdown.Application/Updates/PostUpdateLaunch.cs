using Carowinds.Passdown.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: what the app does at startup about an in-app update. If the updater is waiting for this exact
/// version to confirm it started, confirm — then wait (briefly, in the background) for the updater to record the
/// final result. What's New opens only when that result is a confirmed success for the running version and its
/// notes were not acknowledged yet (<see cref="WhatsNewPolicy"/>). A clean install, an ordinary restart, or a
/// failed/rolled-back attempt never opens it.
/// </summary>
public sealed class PostUpdateLaunch(
    IUpdateOutcomeStore outcomes,
    SemanticVersion runningVersion,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
    ILogger<PostUpdateLaunch>? logger = null)
{
    public static readonly TimeSpan ResultWaitTimeout = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? Task.Delay;

    /// <summary>Returns the latest outcome after confirming (and waiting for) this launch when it was an update.</summary>
    public async Task<UpdateInstallOutcome?> ConfirmAsync(CancellationToken cancellationToken = default)
    {
        var latest = outcomes.LoadLatest();
        if (latest is not { Status: UpdateInstallStatus.AwaitingLaunchConfirmation } || latest.ToVersion != runningVersion.Normalized)
        {
            return latest;
        }

        if (!outcomes.ConfirmLaunch(latest.RequestId, runningVersion))
        {
            logger?.LogWarning("Could not confirm the update launch to the updater.");
            return latest;
        }

        logger?.LogInformation("Confirmed launch of updated version {Version}.", runningVersion.Normalized);
        var waited = TimeSpan.Zero;
        while (waited < ResultWaitTimeout)
        {
            await _delayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            waited += PollInterval;
            var current = outcomes.LoadLatest();
            if (current is not null && current.RequestId == latest.RequestId && current.Status != UpdateInstallStatus.AwaitingLaunchConfirmation)
            {
                return current;
            }
        }

        return outcomes.LoadLatest();
    }

    public bool ShouldShowWhatsNew(UpdateInstallOutcome? outcome, string? lastAcknowledgedVersion) =>
        WhatsNewPolicy.ShouldShowAfterUpdate(outcome, runningVersion, lastAcknowledgedVersion);
}
