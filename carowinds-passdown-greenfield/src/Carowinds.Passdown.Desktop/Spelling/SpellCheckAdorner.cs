using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Desktop.Spelling;

/// <summary>
/// Phase 20A: draws the familiar red wavy underline under misspelled words. Sits in the adorner layer
/// over a TextBox's own <see cref="TextPresenter"/>, using that presenter's text layout to find each
/// word's on-screen rectangles, so it follows wrapping, scrolling and font scaling. Purely visual: it
/// is not hit-testable and never changes the text.
/// </summary>
internal sealed class SpellCheckAdorner : Control
{
    private static readonly IPen UnderlinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)), 1);

    private readonly TextPresenter _presenter;
    private IReadOnlyList<SpellingIssue> _issues = [];
    private string? _checkedText;

    public SpellCheckAdorner(TextPresenter presenter)
    {
        _presenter = presenter;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>The issues currently drawn — exposed for tests of the integration boundary.</summary>
    public IReadOnlyList<SpellingIssue> Issues => _issues;

    public void Update(string? checkedText, IReadOnlyList<SpellingIssue> issues)
    {
        _checkedText = checkedText;
        _issues = issues;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var text = _presenter.Text;
        if (_issues.Count == 0 || string.IsNullOrEmpty(text))
        {
            return;
        }

        var layout = _presenter.TextLayout;
        foreach (var issue in _issues)
        {
            // Skip any issue the text has moved away from since the last (debounced) check.
            if (issue.End > text.Length ||
                (!ReferenceEquals(text, _checkedText) && !string.Equals(text.Substring(issue.Start, issue.Length), issue.Word, StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var rect in layout.HitTestTextRange(issue.Start, issue.Length))
            {
                if (rect.Width > 0)
                {
                    DrawSquiggle(context, rect.Left, rect.Right, rect.Bottom - 1.5);
                }
            }
        }
    }

    private static void DrawSquiggle(DrawingContext context, double left, double right, double baseline)
    {
        const double step = 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(left, baseline), isFilled: false);
            var up = true;
            for (var x = left + step; x <= right; x += step)
            {
                ctx.LineTo(new Point(x, up ? baseline - 1.5 : baseline));
                up = !up;
            }

            ctx.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, UnderlinePen, geometry);
    }
}
