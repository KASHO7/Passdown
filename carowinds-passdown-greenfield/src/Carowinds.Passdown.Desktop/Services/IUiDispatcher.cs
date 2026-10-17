using Avalonia.Threading;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 19A: marshals work raised on a background thread (a refresh finishing, a local-change
/// notification) onto the UI thread before any view model state changes. A seam rather than a direct
/// <see cref="Dispatcher.UIThread"/> call so view-model tests — which have no running dispatcher loop —
/// can run the callback inline.
/// </summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
