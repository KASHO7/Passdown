using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Maps a <see cref="ParkHoursBadgeKind"/> to a small supplementary status-dot color. Spec §10.4
/// "do not communicate status by color alone" — the dot always sits next to the badge's own text
/// label (see <see cref="ParkHoursPresentation.BadgeText"/>), never replaces it.
/// </summary>
public sealed class ParkHoursBadgeBrushConverter : IValueConverter
{
    public static readonly ParkHoursBadgeBrushConverter Instance = new();

    private static readonly IBrush NotCheckedBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush ClosedBrush = new SolidColorBrush(Color.Parse("#546E7A"));
    private static readonly IBrush OpenBrush = new SolidColorBrush(Color.Parse("#1E5AA8"));
    private static readonly IBrush OverrideBrush = new SolidColorBrush(Color.Parse("#B26A00"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ParkHoursBadgeKind.Open => OpenBrush,
        ParkHoursBadgeKind.Closed => ClosedBrush,
        ParkHoursBadgeKind.ManualOverride or ParkHoursBadgeKind.PrivateBuyout => OverrideBrush,
        _ => NotCheckedBrush,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
