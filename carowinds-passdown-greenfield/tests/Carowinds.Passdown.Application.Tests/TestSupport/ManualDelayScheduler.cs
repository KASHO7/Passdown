namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>
/// A controllable stand-in for <see cref="Carowinds.Passdown.Application.PersonalSync.PersonalSyncCoordinator"/>'s
/// injected delay function (Phase 16F §32: "tests should deterministically advance debounce/periodic/
/// retry — do not write slow timing tests"). Each call records the requested interval instead of
/// actually waiting; a test elapses a specific pending delay by the interval it was scheduled with
/// (the coordinator runs three independent delay chains — debounce, periodic, retry — so tests need to
/// pick which one advances, not just "the most recent"). Uses default (synchronous) task continuations,
/// so completing a delay drives the coordinator's own chain forward on the calling thread up to its
/// next real suspension point — no polling needed.
/// </summary>
internal sealed class ManualDelayScheduler
{
    private readonly List<(TimeSpan Interval, TaskCompletionSource Tcs)> _pending = [];
    private readonly Lock _lock = new();

    public int PendingCount
    {
        get { lock (_lock) { return _pending.Count(p => p.Tcs.Task.Status == TaskStatus.WaitingForActivation); } }
    }

    public Task Delay(TimeSpan interval, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        lock (_lock)
        {
            _pending.Add((interval, tcs));
        }

        return tcs.Task;
    }

    /// <summary>
    /// Completes the most recently scheduled, still-pending delay whose requested interval equals
    /// <paramref name="interval"/> exactly. Deliberately finds the target while holding <see cref="_lock"/>
    /// but calls <see cref="TaskCompletionSource.TrySetResult()"/> after releasing it — completing a
    /// delay resumes the coordinator's own synchronous continuation chain inline (default
    /// <see cref="TaskCompletionSource"/> continuations run synchronously), which can itself call back
    /// into <see cref="Delay"/> (e.g. scheduling the next retry/periodic tick) on this same thread; doing
    /// that while still holding this lock would self-block.
    /// </summary>
    public bool Elapse(TimeSpan interval)
    {
        TaskCompletionSource? target = null;
        lock (_lock)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Interval == interval && _pending[i].Tcs.Task.Status == TaskStatus.WaitingForActivation)
                {
                    target = _pending[i].Tcs;
                    break;
                }
            }
        }

        return target?.TrySetResult() ?? false;
    }
}
