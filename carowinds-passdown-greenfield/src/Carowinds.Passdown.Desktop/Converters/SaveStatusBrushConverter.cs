using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Gives the save-status chip a brand-tinted fill once a save has actually completed, and a neutral
/// fill while unsaved/saving — purely decorative, since the chip's bound text (spec §10.4 "do not
/// communicate status by color alone") already states the real state in words.
/// </summary>
public sealed class SaveStatusBrushConverter : IValueConverter
{
    public static readonly SaveStatusBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AssociateAutosaveState state || Avalonia.Application.Current is not { } app)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var key = state == AssociateAutosaveState.Saved ? "PassdownChipAccentBrush" : "PassdownChipBrush";
        return app.TryGetResource(key, app.ActualThemeVariant, out var resource) ? resource as IBrush : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
