using System.Globalization;
using Avalonia.Data.Converters;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Phase 22D: renders a stored whole-number count (History detail's frozen snapshot) as
/// "{label}: 38,500", or "{label}: not recorded" when the snapshot has none — the label is the
/// ConverterParameter. Display only: the snapshot's integer is never rewritten, and the frozen PDF is
/// served byte-for-byte as before. Formatting always uses <see cref="OperationalCount"/> (en-US),
/// never the binding's culture.
/// </summary>
public sealed class OperationalCountTextConverter : IValueConverter
{
    public static readonly OperationalCountTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var label = parameter as string ?? string.Empty;
        var text = value is int count ? OperationalCount.Format(count) : "not recorded";
        return label.Length == 0 ? text : $"{label}: {text}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
