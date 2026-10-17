using Carowinds.Passdown.Application.DailyFeatureMath;
using QuestPDF.Elements;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.DailyFeatureMath;

/// <summary>Each template's Daily Feature body typography. <see cref="LineHeight"/> null = the font's natural line height.</summary>
public sealed record DailyFeaturePdfTextStyle(float FontSize, float? LineHeight, string Color);

/// <summary>
/// Phase 21D: the one place every PDF template prints the Daily Feature body.
/// <list type="bullet">
/// <item>Text without a formula that parses (the normal case, including every pre-21D Daily Feature)
/// prints exactly as before — the same italic QuestPDF text — with STIX Two Math added only as a glyph
/// fallback so Unicode symbols such as ∫, ∑, ≤ or ∇ never print as missing-glyph boxes.</item>
/// <item>Text with at least one formatted formula is laid out by the shared
/// <see cref="MathParagraphRenderer"/> and printed as one vector SVG per line, inside a paging
/// component so a long Daily Feature can continue onto the next page between lines.</item>
/// <item>If the renderer is unavailable or fails, the plain text prints instead — PDF generation never
/// depends on math support.</item>
/// </list>
/// </summary>
public static class DailyFeaturePdfText
{
    public static void Compose(IContainer container, string text, DailyFeaturePdfTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        if (ShouldRenderMath(text, out var renderer))
        {
            container.Dynamic(new MathParagraphComponent(renderer!, text, style));
            return;
        }

        ComposePlain(container, text, style);
    }

    /// <summary>True when <paramref name="text"/> has a formula the renderer will draw (and the renderer is available).</summary>
    public static bool ShouldRenderMath(string text, out MathParagraphRenderer? renderer)
    {
        renderer = null;
        if (!DailyFeatureMathText.MayContainMath(text))
        {
            return false;
        }

        try
        {
            if (!DailyFeatureMathText.Analyze(text).HasRenderableMath)
            {
                return false;
            }

            renderer = MathParagraphRenderer.Shared;
            if (renderer is null)
            {
                MathRenderDiagnostics.Report(MathRenderStatus.RendererUnavailable);
            }

            return renderer is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MathRenderDiagnostics.Report(MathRenderStatus.RenderFailed);
            return false;
        }
    }

    /// <summary>The paragraph style the math renderer uses for a template's body text.</summary>
    public static MathParagraphStyle ParagraphStyle(DailyFeaturePdfTextStyle style) =>
        new(style.FontSize, style.LineHeight ?? 1.2, ItalicProse: true);

    private static void ComposePlain(IContainer container, string text, DailyFeaturePdfTextStyle style)
    {
        var span = container.Text(text)
            .FontFamily(Fonts.Lato, MathFontSet.MathFontFamily)
            .FontSize(style.FontSize)
            .FontColor(style.Color)
            .Italic();

        if (style.LineHeight is { } lineHeight)
        {
            span.LineHeight(lineHeight);
        }
    }

    /// <summary>
    /// Lays the paragraph out at the width QuestPDF actually offers, then emits whole lines while they
    /// fit; the rest continue on the next page. Every line is at most a fraction of a page tall (the
    /// renderer scales anything taller), so a fresh page always accepts at least one line.
    /// </summary>
    private sealed class MathParagraphComponent(MathParagraphRenderer renderer, string text, DailyFeaturePdfTextStyle style) : IDynamicComponent<int>
    {
        private double _layoutWidth = -1;
        private MathRenderedParagraph? _paragraph;
        private string[] _svgs = [];

        public int State { get; set; }

        public DynamicComponentComposeResult Compose(DynamicContext context)
        {
            // A hair narrower than offered, so float rounding can never make a line wider than its slot.
            var width = System.Math.Max(20, context.AvailableSize.Width - 0.5);
            if (_paragraph is null || System.Math.Abs(_layoutWidth - width) > 0.05)
            {
                _layoutWidth = width;
                _paragraph = renderer.Render(text, width, ParagraphStyle(style));
                _svgs = _paragraph?.Lines.Select(line => MathParagraphRenderer.ToSvg(line, style.Color)).ToArray() ?? [];
            }

            if (_paragraph is null)
            {
                // Unexpected renderer failure: print the source as plain text (no paging needed for a fallback).
                return new DynamicComponentComposeResult
                {
                    Content = context.CreateElement(element => ComposePlain(element, text, style)),
                    HasMoreContent = false,
                };
            }

            // Heights are compared exactly as the column below will sum them (float), with QuestPDF's own tolerance.
            var lines = _paragraph.Lines;
            var available = context.AvailableSize.Height;
            var first = State;
            var used = 0f;
            var next = first;
            while (next < lines.Count && used + (float)lines[next].Height <= available + Size.Epsilon)
            {
                used += (float)lines[next].Height;
                next++;
            }

            var content = context.CreateElement(element => element.Column(column =>
            {
                for (var i = first; i < next; i++)
                {
                    var line = lines[i];
                    column.Item().Width((float)line.Width).Height((float)line.Height).Svg(_svgs[i]);
                }
            }));

            State = next;
            return new DynamicComponentComposeResult { Content = content, HasMoreContent = next < lines.Count };
        }
    }
}
