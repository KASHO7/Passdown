using Avalonia;
using Avalonia.Controls;

namespace Carowinds.Passdown.Desktop.Controls;

/// <summary>
/// Phase 23B: lays the Dashboard's show entries out in as many equal columns as fit (at least
/// <see cref="MinColumnWidth"/> wide each, at most <see cref="MaxColumns"/>), filled row by row so visual order
/// and screen-reader order are the same chronological order. Each line of entries is as tall as its tallest
/// entry, so an expanded time list pushes the next line down instead of overlapping it. Every entry is
/// measured at the real column width, so its text wraps rather than running into a neighbor. Layout only.
/// </summary>
public sealed class ShowColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ShowColumnsPanel, double>(nameof(MinColumnWidth), 340);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<ShowColumnsPanel, int>(nameof(MaxColumns), 3);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ShowColumnsPanel, double>(nameof(ColumnSpacing), 24);

    static ShowColumnsPanel()
    {
        AffectsMeasure<ShowColumnsPanel>(MinColumnWidthProperty, MaxColumnsProperty, ColumnSpacingProperty);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>How many columns <paramref name="width"/> holds (1 when the width is unconstrained or too narrow).</summary>
    public int ColumnsFor(double width)
    {
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
        {
            return 1;
        }

        var fit = (int)Math.Floor((width + ColumnSpacing) / (Math.Max(1, MinColumnWidth) + ColumnSpacing));
        return Math.Clamp(fit, 1, Math.Max(1, MaxColumns));
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, (width - (ColumnSpacing * (columns - 1))) / columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(child => child.IsVisible).ToList();
        var columns = ColumnsFor(availableSize.Width);
        var columnWidth = ColumnWidth(availableSize.Width, columns);
        double height = 0;
        double widest = 0;

        for (var start = 0; start < visible.Count; start += columns)
        {
            double lineHeight = 0;
            foreach (var child in visible.Skip(start).Take(columns))
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
                widest = Math.Max(widest, child.DesiredSize.Width);
            }

            height += lineHeight;
        }

        var width = double.IsInfinity(availableSize.Width) ? widest : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Where(child => child.IsVisible).ToList();
        var columns = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, columns);
        double top = 0;

        for (var start = 0; start < visible.Count; start += columns)
        {
            var line = visible.Skip(start).Take(columns).ToList();
            var lineHeight = line.Max(child => child.DesiredSize.Height);
            for (var i = 0; i < line.Count; i++)
            {
                line[i].Arrange(new Rect(i * (columnWidth + ColumnSpacing), top, columnWidth, lineHeight));
            }

            top += lineHeight;
        }

        return finalSize;
    }
}
