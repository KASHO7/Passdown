namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// One progress update for a running refresh job, suitable for a later phase's UI binding (Part
/// H). Presentation-neutral — no Avalonia types. Reported once per completed operation (not per
/// state transition), per Part H's "avoid extremely chatty progress updates if they add no user
/// value"; <see cref="LastCompleted"/> carries the source/date and success/failure status of
/// whichever operation just finished.
/// </summary>
public sealed record RefreshProgress(
    int TotalOperations,
    int CompletedOperations,
    SourceOperationResult LastCompleted,
    bool CancellationRequested);
