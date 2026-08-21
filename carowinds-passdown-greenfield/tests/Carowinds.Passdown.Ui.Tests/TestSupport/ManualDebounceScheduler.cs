using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>
/// A controllable stand-in for <see cref="AssociateViewModel"/>'s injected autosave-delay function
/// (spec Phase 4C Part M: "use deterministic/fake timing if needed for debounce tests" rather than
/// a real <c>Task.Delay</c> a test would have to sleep past). Each call schedules a new
/// <see cref="TaskCompletionSource"/> instead of actually waiting, and observes the
/// <see cref="CancellationToken"/> the ViewModel passes in exactly the way <c>Task.Delay</c> itself
/// would — so the ViewModel's own debounce-cancellation logic (a newer edit cancels the older
/// pending delay) is exercised for real, just without a real clock underneath it.
///
/// Deliberately uses default (synchronous) task continuations: completing a delay via
/// <see cref="ElapseLatest"/> drives the ViewModel's autosave chain forward on the calling (test)
/// thread up to its next genuine suspension point (e.g. an unreleased <c>SaveGates</c> entry), so a
/// test can assert intermediate state such as <c>AutosaveState.Saving</c> immediately after
/// <see cref="ElapseLatest"/> returns, with no polling loop needed.
/// </summary>
internal sealed class ManualDebounceScheduler
{
    private readonly List<TaskCompletionSource> _pending = [];

    public int ScheduleCount => _pending.Count;

    public Task Delay(TimeSpan interval, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pending.Add(tcs);
        return tcs.Task;
    }

    /// <summary>Elapses the most recently scheduled delay that has not already completed/cancelled — the one an associate's latest edit is waiting on.</summary>
    public void ElapseLatest()
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].TrySetResult())
            {
                return;
            }
        }
    }
}
