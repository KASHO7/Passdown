using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Phase 22A: resolves a shared color token (Themes/PassdownTokens.axaml) for a specific theme
/// variant, so converters return the same brushes the XAML uses instead of hard-coded colors. Callers
/// bind the target element's own <c>ActualThemeVariant</c> as a second MultiBinding value, which is
/// what makes a live light/dark switch re-run the converter.
/// </summary>
internal static class ThemeBrushLookup
{
    public static object Find(string key, object? themeVariant)
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var variant = themeVariant as ThemeVariant ?? app.ActualThemeVariant;
        return app.TryGetResource(key, variant, out var resource) && resource is IBrush brush
            ? brush
            : AvaloniaProperty.UnsetValue;
    }
}
