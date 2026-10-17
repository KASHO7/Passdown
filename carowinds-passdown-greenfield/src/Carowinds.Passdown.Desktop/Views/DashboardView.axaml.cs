using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class DashboardView : UserControl
{
    // Phase 21C: re-evaluates which of today's shows are still upcoming while the view is shown
    // (local text only — no data read, no network).
    private readonly DispatcherTimer _showsTick = new() { Interval = TimeSpan.FromMinutes(1) };

    public DashboardView()
    {
        InitializeComponent();
        _showsTick.Tick += (_, _) => (DataContext as OperationsDashboardViewModel)?.RefreshShowsForTime(DateTimeOffset.UtcNow);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _showsTick.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _showsTick.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
