using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 19A: the one shared source of the subtle official-data freshness line shown on both the
/// Dashboard and the Associate screen (a single app-lifetime instance, so the two can never disagree
/// and no screen keeps its own refresh state). Reflects <see cref="IOfficialDataRefreshCoordinator.CurrentStatus"/>;
/// every change raised on a background thread is applied on the UI thread.
/// </summary>
public partial class OfficialDataStatusViewModel : ViewModelBase
{
    private readonly IClock _clock;

    [ObservableProperty]
    public partial OfficialDataStatusPresentation Presentation { get; set; }

    public OfficialDataStatusViewModel(IOfficialDataRefreshCoordinator coordinator, IClock clock, IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        var dispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();

        Presentation = OfficialDataStatusPresentation.Build(coordinator.CurrentStatus, clock.UtcNow);
        coordinator.StatusChanged += (_, status) => dispatcher.Post(() => Apply(status));
    }

    internal void Apply(OfficialDataStatus status) => Presentation = OfficialDataStatusPresentation.Build(status, _clock.UtcNow);
}
