using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Gives the save-status chip a brand-tinted fill once a save has actually completed, and a neutral
/// fill while unsaved/saving — purely decorative, since the chip's bound text (spec §10.4 "do not
/// communicate status by color alone") already states the real state in words. Phase 23: a failed
/// save uses the shared error background.
/// Phase 22A: resolved for the bound theme variant (second MultiBinding value) so the chip follows a
/// live light/dark switch.
/// </summary>
public sealed class SaveStatusBrushConverter : IValueConverter, IMultiValueConverter
{
    public static readonly SaveStatusBrushConverter Instance = new();

    internal static string? ResourceKeyFor(object? state) => state switch
    {
        AssociateAutosaveState.Saved => "PassdownChipAccentBrush",
        AssociateAutosaveState.SaveFailed => "PassdownErrorBackgroundBrush",
        AssociateAutosaveState => "PassdownChipBrush",
        _ => null,
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ResourceKeyFor(value) is { } key ? ThemeBrushLookup.Find(key, null) : AvaloniaProperty.UnsetValue;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        ResourceKeyFor(values.Count > 0 ? values[0] : null) is { } key
            ? ThemeBrushLookup.Find(key, values.Count > 1 ? values[1] : null)
            : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
