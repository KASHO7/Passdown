using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IPersonalSyncService"/> for <see cref="PersonalSyncCoordinator"/> tests — never touches the filesystem or a real database.</summary>
internal sealed class FakePersonalSyncService : IPersonalSyncService
{
    private readonly Lock _lock = new();
    private readonly Queue<PersonalSyncResult> _queuedResults = new();

    public PersonalSyncResult NextResult { get; set; } = new(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null);

    public int RunCallCount { get; private set; }

    /// <summary>When set, each call to <see cref="RunAsync"/> waits on this until <see cref="ReleaseNextRun"/> is called — lets a test hold a pass "in progress" to exercise overlap/coalescing.</summary>
    public bool HoldRuns { get; set; }

    private TaskCompletionSource? _runGate;

    public void QueueResult(PersonalSyncResult result)
    {
        lock (_lock)
        {
            _queuedResults.Enqueue(result);
        }
    }

    public async Task<PersonalSyncResult> RunAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource? gate = null;
        lock (_lock)
        {
            RunCallCount++;
            if (HoldRuns)
            {
                gate = _runGate = new TaskCompletionSource();
            }
        }

        if (gate is not null)
        {
            await gate.Task.ConfigureAwait(false);
        }

        lock (_lock)
        {
            return _queuedResults.Count > 0 ? _queuedResults.Dequeue() : NextResult;
        }
    }

    /// <summary>Releases exactly one held <see cref="RunAsync"/> call started while <see cref="HoldRuns"/> was true.</summary>
    public void ReleaseNextRun()
    {
        TaskCompletionSource? gate;
        lock (_lock)
        {
            gate = _runGate;
            _runGate = null;
        }

        gate?.TrySetResult();
    }
}
