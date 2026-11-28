using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

/// <summary>How the Dashboard arranges its gate grid and card columns at the current width (Phase 35E Dashboard).</summary>
public enum DashboardLayoutMode
{
    /// <summary>Gate grid in one row of four; passdown/shows and pricing/notes/perks side by side (1.88 : 1).</summary>
    Wide,

    /// <summary>Gate grid 2 × 2; the two card columns stay side by side (1.4 : 1).</summary>
    Medium,

    /// <summary>Gate grid 2 × 2; the card columns stack (passdown and shows first).</summary>
    Narrow,
}

public partial class DashboardView : UserControl
{
    /// <summary>Content widths (window minus sidebar and page margins) at which the layout changes, matching the approved reference's breakpoints.</summary>
    internal const double WideMinWidth = 1000;

    internal const double MediumMinWidth = 680;

    private const double ContentHorizontalMargin = 48;

    // Phase 21C: re-evaluates which of today's shows are still upcoming while the view is shown
    // (local text only — no data read, no network).
    private readonly DispatcherTimer _showsTick = new() { Interval = TimeSpan.FromMinutes(1) };

    private DashboardLayoutMode? _layoutMode;

    public DashboardView()
    {
        InitializeComponent();
        _showsTick.Tick += (_, _) => (DataContext as OperationsDashboardViewModel)?.RefreshShowsForTime(DateTimeOffset.UtcNow);
        ApplyLayout(DashboardLayoutMode.Wide);
    }

    /// <summary>The arrangement currently applied (tests and the visual harness read it).</summary>
    public DashboardLayoutMode LayoutMode => _layoutMode ?? DashboardLayoutMode.Wide;

    internal static DashboardLayoutMode ModeForContentWidth(double contentWidth) => contentWidth switch
    {
        >= WideMinWidth => DashboardLayoutMode.Wide,
        >= MediumMinWidth => DashboardLayoutMode.Medium,
        _ => DashboardLayoutMode.Narrow,
    };

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

    /// <summary>
    /// Layout only: picks the arrangement for the new width. The content width is set by the window, not by
    /// the content, so re-arranging never feeds back into another size change. Nothing is read or loaded.
    /// </summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyLayout(ModeForContentWidth(e.NewSize.Width - ContentHorizontalMargin));
    }

    private void ApplyLayout(DashboardLayoutMode mode)
    {
        if (_layoutMode == mode)
        {
            return;
        }

        _layoutMode = mode;

        // Gate grid: Park hours, Avenue Entrance, North Gate, Early Ride Time.
        var gateCells = new Control[] { GateHoursColumn, GateAvenueColumn, GateNorthColumn, GateErtColumn };
        if (mode == DashboardLayoutMode.Wide)
        {
            GateGrid.ColumnDefinitions = new ColumnDefinitions("1.45*,1*,1*,1.22*");
            for (var i = 0; i < gateCells.Length; i++)
            {
                Grid.SetRow(gateCells[i], 0);
                Grid.SetColumn(gateCells[i], i);
                gateCells[i].Classes.Set("leading", i == 0);
            }
        }
        else
        {
            GateGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
            for (var i = 0; i < gateCells.Length; i++)
            {
                Grid.SetRow(gateCells[i], i / 2);
                Grid.SetColumn(gateCells[i], i % 2);
                gateCells[i].Classes.Set("leading", i % 2 == 0);
            }
        }

        // Card columns.
        if (mode == DashboardLayoutMode.Narrow)
        {
            DashboardColumns.ColumnDefinitions = new ColumnDefinitions("*");
            DashboardColumns.RowSpacing = 16;
            Grid.SetColumn(DashboardPrimaryColumn, 0);
            Grid.SetRow(DashboardPrimaryColumn, 0);
            Grid.SetColumn(DashboardSecondaryColumn, 0);
            Grid.SetRow(DashboardSecondaryColumn, 1);
        }
        else
        {
            var secondary = new ColumnDefinition(1, GridUnitType.Star) { MinWidth = mode == DashboardLayoutMode.Wide ? 330 : 290 };
            DashboardColumns.ColumnDefinitions =
            [
                new ColumnDefinition(mode == DashboardLayoutMode.Wide ? 1.88 : 1.4, GridUnitType.Star),
                new ColumnDefinition(16, GridUnitType.Pixel),
                secondary,
            ];
            DashboardColumns.RowSpacing = 0;
            Grid.SetColumn(DashboardPrimaryColumn, 0);
            Grid.SetRow(DashboardPrimaryColumn, 0);
            Grid.SetColumn(DashboardSecondaryColumn, 2);
            Grid.SetRow(DashboardSecondaryColumn, 0);
        }
    }
}
