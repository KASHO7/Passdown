using Carowinds.Passdown.Application.Refresh;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Phase 19A: scriptable <see cref="IOfficialDataRefreshCoordinator"/> — records date-check requests, never touches the network.</summary>
internal sealed class FakeOfficialDataRefreshCoordinator : IOfficialDataRefreshCoordinator
{
    private OfficialDataStatus _status = OfficialDataStatus.NotCheckedYet;

    public OfficialDataStatus CurrentStatus => _status;

    public event EventHandler<OfficialDataStatus>? StatusChanged;

    public List<DateOnly> DateCheckRequests { get; } = [];

    public List<DateOnly> CancelledDateChecks { get; } = [];

    /// <summary>When set, a date check awaits this before completing (lets a test change dates mid-check).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>What a date check reports; <see cref="DateCheckOutcome.Checked"/> also invokes the started callback.</summary>
    public Func<DateOnly, DateCheckOutcome> OutcomeFor { get; set; } = _ => DateCheckOutcome.SkippedFresh;

    /// <summary>Invoked when a check "completes" with <see cref="DateCheckOutcome.Checked"/> — e.g. to publish the OfficialData change the real gate would.</summary>
    public Action<DateOnly>? OnChecked { get; set; }

    public int StartCount { get; private set; }

    public void SetStatus(OfficialDataStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    public Task StartupCompletion => Task.CompletedTask;

    public void Start() => StartCount++;

    public Task StopAsync() => Task.CompletedTask;

    public async Task<DateCheckOutcome> CheckDateIfStaleAsync(DateOnly date, Action? onCheckStarted = null, CancellationToken cancellationToken = default)
    {
        DateCheckRequests.Add(date);
        var outcome = OutcomeFor(date);
        if (outcome == DateCheckOutcome.Checked)
        {
            onCheckStarted?.Invoke();
        }

        if (Gate is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancelledDateChecks.Add(date);
                throw;
            }
        }

        if (outcome == DateCheckOutcome.Checked)
        {
            OnChecked?.Invoke(date);
        }

        return outcome;
    }
}
