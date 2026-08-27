using Carowinds.Passdown.Application.Concurrency;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.Startup;

/// <summary>
/// Phase 6B Part C: a best-effort, non-blocking catch-up for a missed scheduled refresh, triggered
/// once at normal interactive startup (spec §6.6 "refresh the next time the app opens if that
/// day's job has not successfully run"). Not a second refresh implementation — this resolves the
/// exact same <see cref="IScheduledRefreshRunner"/> (and therefore the same
/// <see cref="IRefreshOrchestrator"/>/freshness planning) a <c>--scheduled-refresh</c> process
/// resolves, and every source it covers is skipped entirely unless it is actually due — this never
/// refreshes merely because the app opened or the selected date changed. Guarded by the same
/// <see cref="IScheduledRefreshLock"/> a scheduled process uses, so this can never run at the same
/// time as (or duplicate) an actual scheduled invocation. Callers must not <c>await</c> this from
/// the startup path — run it as a detached background task so it can never delay the window
/// opening — and any failure is caught and logged here, never allowed to reach the caller, so
/// cached data and the already-open UI are unaffected either way.
///
/// Phase 9B Part T: this naturally performs finalize-then-refresh ordering too, using the same
/// <see cref="IScheduledPassdownFinalizer"/> and the same lock/failure semantics as
/// <see cref="ScheduledRefresh.ScheduledRefreshHost"/> — a finalization failure here is caught by
/// the same outer catch below (never propagated, UI stays open) but still prevents the refresh
/// runner from being invoked for this catch-up attempt.
/// </summary>
public static class StartupCatchUpRefresh
{
    public static async Task RunAsync(
        IServiceProvider services,
        IScheduledRefreshLock scheduledLock,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!scheduledLock.TryAcquire())
            {
                logger.LogInformation("Startup catch-up refresh skipped: a scheduled refresh is already running.");
                return;
            }

            try
            {
                var finalizer = services.GetRequiredService<IScheduledPassdownFinalizer>();
                var finalizationSummary = await finalizer.FinalizeEligibleAsync(cancellationToken).ConfigureAwait(false);
                ScheduledRefreshHost.LogFinalizationSummary(logger, finalizationSummary);

                var runner = services.GetRequiredService<IScheduledRefreshRunner>();
                await runner.RunAsync(RefreshMode.StartupCatchUp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scheduledLock.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Best-effort only: a failure here must never affect the already-open UI or its cached
            // data (Part C "failures should not prevent the UI from opening") — this also covers a
            // Phase 9B finalization failure, which must still block the refresh runner call above
            // without propagating (Part T).
            logger.LogWarning(ex, "Startup catch-up refresh failed; cached data remains usable.");
        }
    }
}
