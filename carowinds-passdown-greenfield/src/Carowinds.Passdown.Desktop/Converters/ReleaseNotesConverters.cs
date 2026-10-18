using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>Phase 23D: release-note headings render semi-bold; paragraphs and bullets render normally.</summary>
public static class ReleaseNotesConverters
{
    public static readonly IValueConverter HeadingWeight =
        new FuncValueConverter<bool, FontWeight>(isHeading => isHeading ? FontWeight.SemiBold : FontWeight.Normal);
}
