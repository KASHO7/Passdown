using System.Globalization;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>Whether a line prints a part of the outlook, and in which form.</summary>
internal enum WeatherPartForm
{
    None,
    Full,
    Short,
}

/// <summary>
/// One printed line of a Weather Outlook layout level: which parts of <see cref="PdfWeatherOutlook"/> it carries.
/// A line that carries the temperature is a headline line (headline type); otherwise a secondary line. Parts the
/// outlook does not have are simply left out, and a line left empty is not printed.
/// </summary>
internal sealed record WeatherLine(
    bool Label = false,
    bool Icon = false,
    bool Temperature = false,
    WeatherPartForm Condition = WeatherPartForm.None,
    WeatherPartForm Rain = WeatherPartForm.None,
    WeatherPartForm Attribution = WeatherPartForm.None);

/// <summary>Template typography for the Weather Outlook — each renderer passes its own existing sizes and colours.</summary>
internal sealed record WeatherOutlookStyle(
    float LabelSize,
    float LabelLetterSpacing,
    string LabelColor,
    float HeadlineSize,
    float SecondarySize,
    string SecondaryColor,
    float AttributionSize,
    string AttributionColor,
    float IconSize,
    string InkColor,
    float LineSpacing);

/// <summary>
/// Phase 37F: the shared adaptive Weather Outlook. A template places it in existing header whitespace with a fixed
/// width and a height budget (the height of the operations lines beside it), and lists its layout levels from most to
/// least detailed. At layout time each level is measured by QuestPDF (<see cref="DynamicContext.CreateElement"/>); the
/// first one whose natural size fits both the available width and the height budget is drawn. When none fits, nothing
/// is drawn and no space is taken. Type sizes never shrink to make a level fit. A new instance is created for every
/// document generation; it is not thread-safe and is never shared.
/// </summary>
internal sealed class PdfWeatherOutlookComponent(
    PdfWeatherOutlook outlook,
    WeatherOutlookStyle style,
    float heightBudget,
    IReadOnlyList<IReadOnlyList<WeatherLine>> levels) : IDynamicComponent
{
    private const float Tolerance = 0.01f;

    // QuestPDF may compose a dynamic component several times per layout pass. One instance serves one document
    // generation (never shared across threads), so the resolved levels, the chosen level for a given space and the
    // parsed icon are worked out once per document instead of on every pass.
    private List<List<ResolvedLine>>? _resolved;
    private (float Width, float Height, int Level)? _chosen;
    private SvgImage? _icon;

    public DynamicComponentComposeResult Compose(DynamicContext context)
    {
        _resolved ??= levels
            .Select(Resolve)
            .Where(lines => lines.Count > 0 && lines.Any(line => line.Attribution is not null)) // every printed outlook names its source and time
            .ToList();

        var width = context.AvailableSize.Width;
        var maxHeight = Math.Min(heightBudget, context.AvailableSize.Height);

        if (_chosen is { } chosen && chosen.Width == width && chosen.Height == maxHeight)
        {
            return Result(context, chosen.Level);
        }

        for (var level = 0; level < _resolved.Count; level++)
        {
            var lines = _resolved[level];
            var element = context.CreateElement(container => Draw(container, lines));
            if (element.Size.Width <= width + Tolerance && element.Size.Height <= maxHeight + Tolerance)
            {
                _chosen = (width, maxHeight, level);
                return new DynamicComponentComposeResult { Content = element, HasMoreContent = false };
            }
        }

        _chosen = (width, maxHeight, -1);
        return Result(context, -1);
    }

    private DynamicComponentComposeResult Result(DynamicContext context, int level) => new()
    {
        Content = level < 0 ? context.CreateElement(_ => { }) : context.CreateElement(container => Draw(container, _resolved![level])),
        HasMoreContent = false,
    };

    /// <summary>One line's resolved text pieces (null = not printed).</summary>
    private sealed record ResolvedLine(bool Label, bool Icon, string? Headline, string? Secondary, string? Attribution);

    private List<ResolvedLine> Resolve(IReadOnlyList<WeatherLine> level)
    {
        var lines = new List<ResolvedLine>();
        var conditionShown = false;
        foreach (var line in level)
        {
            string? headline = null;
            var secondary = new List<string>();

            if (line.Temperature)
            {
                if (outlook.TemperatureText is { } temperature)
                {
                    headline = temperature;
                }
                else if (!conditionShown && Pick(outlook.ConditionText, outlook.ShortConditionText, line.Condition == WeatherPartForm.Full ? WeatherPartForm.Full : WeatherPartForm.Short) is { } asHeadline)
                {
                    // No temperature: the condition takes the headline slot instead of leaving it empty.
                    headline = asHeadline;
                    conditionShown = true;
                }
            }

            if (!conditionShown && Pick(outlook.ConditionText, outlook.ShortConditionText, line.Condition) is { } condition)
            {
                secondary.Add(condition);
                conditionShown = true;
            }

            if (Pick(outlook.RainText, outlook.ShortRainText, line.Rain) is { } rain)
            {
                secondary.Add(rain);
            }

            var attribution = Pick(outlook.AttributionText, outlook.ShortAttributionText, line.Attribution);
            var icon = line.Icon && outlook.Icon != PdfWeatherIcon.None && headline is not null;
            var secondaryText = secondary.Count == 0 ? null : string.Join(" · ", secondary);

            if (headline is null && secondaryText is null && attribution is null && !(line.Label && level.Count > 1))
            {
                continue;
            }

            lines.Add(new ResolvedLine(line.Label, icon, headline, secondaryText, attribution));
        }

        return lines;
    }

    private static string? Pick(string? full, string? brief, WeatherPartForm form) => form switch
    {
        WeatherPartForm.Full => full,
        WeatherPartForm.Short => brief,
        _ => null,
    };

    private void Draw(IContainer container, IReadOnlyList<ResolvedLine> lines) =>
        container.Column(column =>
        {
            column.Spacing(style.LineSpacing);
            foreach (var line in lines)
            {
                column.Item().AlignRight().Text(text =>
                {
                    text.AlignRight();
                    var started = false;

                    if (line.Label)
                    {
                        text.Span("WEATHER").FontSize(style.LabelSize).Bold().LetterSpacing(style.LabelLetterSpacing).FontColor(style.LabelColor);
                        started = true;
                    }

                    if (line.Icon)
                    {
                        if (started)
                        {
                            text.Span("  ").FontSize(style.LabelSize);
                        }

                        var size = style.IconSize;
                        _icon ??= SvgImage.FromText(PdfWeatherIcons.Svg(outlook.Icon, style.InkColor));
                        text.Element(TextInjectedElementAlignment.Middle).Width(size).Height(size).Svg(_icon);
                        text.Span(" ").FontSize(style.HeadlineSize);
                        started = true;
                    }
                    else if (started && line.Headline is not null)
                    {
                        text.Span("  ").FontSize(style.LabelSize);
                    }

                    if (line.Headline is { } headline)
                    {
                        text.Span(headline).FontSize(style.HeadlineSize).Bold().FontColor(style.InkColor);
                        started = true;
                    }

                    if (line.Secondary is { } secondary)
                    {
                        if (started)
                        {
                            text.Span("  ").FontSize(style.SecondarySize);
                        }

                        text.Span(secondary).FontSize(style.SecondarySize).FontColor(style.SecondaryColor);
                        started = true;
                    }

                    if (line.Attribution is { } attribution)
                    {
                        if (started)
                        {
                            text.Span(" · ").FontSize(style.AttributionSize).FontColor(style.AttributionColor);
                        }

                        text.Span(attribution).FontSize(style.AttributionSize).FontColor(style.AttributionColor);
                    }
                });
            }
        });
}

/// <summary>
/// Phase 37F: the Weather Outlook's vector icons — a small locally defined set on a 24-unit grid with one stroke
/// weight, drawn in the template's ink colour (white fills only mask what a cloud covers), so they print the same in
/// colour and grayscale and need no image file, font or emoji.
/// </summary>
internal static class PdfWeatherIcons
{
    private const string Cloud = "M7 18h9.5a3.5 3.5 0 0 0 .4-6.98 5 5 0 0 0-9.65-1.15A3.5 3.5 0 0 0 7 18z";
    private const string LargeCloud = "M6.5 19h11a4 4 0 0 0 .6-7.96 6 6 0 0 0-11.6-1.38A4.5 4.5 0 0 0 6.5 19z";
    private const string RaisedCloud = "M6.5 15.5h11a4 4 0 0 0 .6-7.96 6 6 0 0 0-11.6-1.38A4.5 4.5 0 0 0 6.5 15.5z";

    public static string Svg(PdfWeatherIcon icon, string ink)
    {
        var body = icon switch
        {
            PdfWeatherIcon.Sun => "<circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 2v2.5M12 19.5V22M4.93 4.93l1.77 1.77M17.3 17.3l1.77 1.77M2 12h2.5M19.5 12H22M4.93 19.07l1.77-1.77M17.3 6.7l1.77-1.77\"/>",
            PdfWeatherIcon.Moon => "<path d=\"M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z\"/>",
            PdfWeatherIcon.PartlyCloudy => $"<path d=\"M12 2v2M4.93 4.93l1.41 1.41M2 12h2M17.66 6.34l1.41-1.41\"/><path d=\"M10.5 7.5a4 4 0 0 1 5.4 3.5\"/><path d=\"{Cloud}\" fill=\"#FFFFFF\"/>",
            PdfWeatherIcon.PartlyCloudyNight => $"<path d=\"M21 9.5A5 5 0 0 1 14.5 3a4 4 0 1 0 6.5 6.5z\"/><path d=\"{Cloud}\" fill=\"#FFFFFF\"/>",
            PdfWeatherIcon.Cloudy => $"<path d=\"{LargeCloud}\"/>",
            PdfWeatherIcon.Rain => $"<path d=\"{RaisedCloud}\"/><path d=\"M8.5 18.5l-1 2.5M12.5 18.5l-1 2.5M16.5 18.5l-1 2.5\"/>",
            PdfWeatherIcon.Thunderstorm => $"<path d=\"{RaisedCloud}\"/><path d=\"M13 16.5l-2 3h3l-2 3\"/>",
            PdfWeatherIcon.Snow => $"<path d=\"{RaisedCloud}\"/><path d=\"M8.5 19.5h.01M12 19.5h.01M15.5 19.5h.01M10.25 22h.01M13.75 22h.01\" stroke-width=\"2.4\"/>",
            PdfWeatherIcon.Fog => "<path d=\"M4 8h16M3 12h18M5 16h14M8 20h8\"/>",
            _ => string.Empty,
        };

        return string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"{ink}\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{body}</svg>");
    }
}
