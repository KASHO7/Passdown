using System.Globalization;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Carowinds.Passdown.Documents.Teams;

namespace Carowinds.Passdown.Desktop.Converters;

/// <summary>
/// Phase 29B: turns the composer's <see cref="TeamsPreviewSpan"/> list into text runs for the preview's
/// <c>SelectableTextBlock.Inlines</c> — bold and italic only, as local text. No HTML is parsed or rendered
/// and nothing is loaded; colors are inherited from the theme, so light and dark both work.
/// </summary>
public sealed class TeamsPreviewInlinesConverter : IValueConverter
{
    public static readonly TeamsPreviewInlinesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IReadOnlyList<TeamsPreviewSpan> spans ? ToInlines(spans) : new InlineCollection();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static InlineCollection ToInlines(IReadOnlyList<TeamsPreviewSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var inlines = new InlineCollection();
        foreach (var span in spans)
        {
            var lines = span.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    inlines.Add(new LineBreak());
                }

                if (lines[i].Length > 0)
                {
                    inlines.Add(new Run(lines[i])
                    {
                        FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal,
                        FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal,
                    });
                }
            }
        }

        return inlines;
    }
}
