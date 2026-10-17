using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Carowinds.Passdown.Documents.DailyFeatureMath;

namespace Carowinds.Passdown.Desktop.Controls;

/// <summary>
/// Phase 21D: draws a Daily Feature laid out by the shared <see cref="MathParagraphRenderer"/> — the
/// same vector outlines the PDFs print — at the control's own width, in the inherited theme
/// foreground (so it follows light/dark mode). Purely presentational: no data access, no network.
/// </summary>
public sealed class MathParagraphView : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MathParagraphView, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MathParagraphView, double>(nameof(FontSize), 14);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<MathParagraphView>();

    private MathRenderedParagraph? _paragraph;
    private IReadOnlyList<(double Top, Geometry Fill, IReadOnlyList<Geometry> Strokes, MathRenderedLine Line)> _geometry = [];

    static MathParagraphView()
    {
        AffectsMeasure<MathParagraphView>(TextProperty, FontSizeProperty);
        AffectsRender<MathParagraphView>(ForegroundProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The current layout (test seam); null when there is nothing to draw.</summary>
    public MathRenderedParagraph? Paragraph => _paragraph;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0 ? availableSize.Width : 560;
        var text = Text;
        var renderer = MathParagraphRenderer.Shared;
        if (string.IsNullOrEmpty(text) || renderer is null)
        {
            SetParagraph(null);
            return default;
        }

        var paragraph = renderer.Render(text, width, new MathParagraphStyle(FontSize, 1.35, ItalicProse: false));
        if (!ReferenceEquals(paragraph, _paragraph))
        {
            SetParagraph(paragraph);
        }

        return paragraph is null ? default : new Size(width, paragraph.Height);
    }

    private void SetParagraph(MathRenderedParagraph? paragraph)
    {
        _paragraph = paragraph;
        var geometry = new List<(double, Geometry, IReadOnlyList<Geometry>, MathRenderedLine)>();
        var top = 0.0;
        foreach (var line in paragraph?.Lines ?? [])
        {
            geometry.Add((top, BuildFill(line.Fill), line.Strokes.Select(BuildStroke).ToList(), line));
            top += line.Height;
        }

        _geometry = geometry;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? Brushes.Black;
        foreach (var (top, fill, strokes, line) in _geometry)
        {
            using (context.PushTransform(Matrix.CreateTranslation(0, top)))
            {
                context.DrawGeometry(brush, null, fill);
                for (var i = 0; i < strokes.Count; i++)
                {
                    var pen = new Pen(brush, line.Strokes[i].Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    context.DrawGeometry(null, pen, strokes[i]);
                }
            }
        }
    }

    private static Geometry BuildFill(IReadOnlyList<PathCommand> commands)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.SetFillRule(FillRule.NonZero);
        var open = false;
        foreach (var c in commands)
        {
            switch (c.Verb)
            {
                case PathVerb.MoveTo:
                    if (open)
                    {
                        ctx.EndFigure(true);
                    }

                    ctx.BeginFigure(new Point(c.X, c.Y), isFilled: true);
                    open = true;
                    break;
                case PathVerb.LineTo:
                    ctx.LineTo(new Point(c.X, c.Y));
                    break;
                case PathVerb.QuadTo:
                    ctx.QuadraticBezierTo(new Point(c.X1, c.Y1), new Point(c.X, c.Y));
                    break;
                case PathVerb.Close:
                    if (open)
                    {
                        ctx.EndFigure(true);
                        open = false;
                    }

                    break;
            }
        }

        if (open)
        {
            ctx.EndFigure(true);
        }

        return geometry;
    }

    private static Geometry BuildStroke(MathStroke stroke)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var point = new Point(stroke.Points[i].X, stroke.Points[i].Y);
            if (i == 0)
            {
                ctx.BeginFigure(point, isFilled: false);
            }
            else
            {
                ctx.LineTo(point);
            }
        }

        if (stroke.Points.Count > 0)
        {
            ctx.EndFigure(false);
        }

        return geometry;
    }
}
