using System.Globalization;
using Avalonia.Data.Converters;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Maps a <see cref="ParkHoursBadgeKind"/> to a small supplementary status-dot color. Spec §10.4
/// "do not communicate status by color alone" — the dot always sits next to the badge's own text
/// label (see <see cref="ParkHoursPresentation.BadgeText"/>), never replaces it.
/// Phase 22A: colors are the shared status tokens, resolved for the bound theme variant (second
/// MultiBinding value) so the dot follows a live light/dark switch.
/// </summary>
public sealed class ParkHoursBadgeBrushConverter : IValueConverter, IMultiValueConverter
{
    public static readonly ParkHoursBadgeBrushConverter Instance = new();

    internal static string ResourceKeyFor(object? kind) => kind switch
    {
        ParkHoursBadgeKind.Open => "PassdownSuccessBrush",
        ParkHoursBadgeKind.Closed => "PassdownUnavailableBrush",
        ParkHoursBadgeKind.ManualOverride or ParkHoursBadgeKind.PrivateBuyout => "PassdownWarningBrush",
        _ => "PassdownBorderStrongBrush",
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeBrushLookup.Find(ResourceKeyFor(value), null);

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeBrushLookup.Find(ResourceKeyFor(values.Count > 0 ? values[0] : null), values.Count > 1 ? values[1] : null);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
