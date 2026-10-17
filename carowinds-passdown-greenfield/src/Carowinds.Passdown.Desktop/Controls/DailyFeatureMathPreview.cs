using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Documents.DailyFeatureMath;

namespace Carowinds.Passdown.Desktop.Controls;

/// <summary>
/// Phase 21D: the compact "Preview" under the Daily Feature editor. It appears only while the text
/// contains a math delimiter (<c>\(</c> or <c>\[</c>) — ordinary text gets no preview and no visual
/// change — and shows the text formatted by the same renderer the PDFs use. Edits are coalesced
/// (<see cref="UpdateDelay"/> after the last change) so a burst of typing re-lays the formula once;
/// rendering is bounded by the parser/renderer limits, and nothing here reads or writes the database.
/// A formula that can't be formatted is shown as typed, with one quiet warning line — never an
/// exception, never a change to the editor's text.
/// </summary>
public sealed class DailyFeatureMathPreview : Border
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<DailyFeatureMathPreview, string?>(nameof(Source));

    private readonly MathParagraphView _view;
    private readonly TextBlock _warning;
    private CancellationTokenSource? _pending;

    static DailyFeatureMathPreview()
    {
        SourceProperty.Changed.AddClassHandler<DailyFeatureMathPreview>((preview, _) => preview.ScheduleUpdate());
    }

    public DailyFeatureMathPreview()
    {
        IsVisible = false;
        CornerRadius = new CornerRadius(5);
        Padding = new Thickness(10, 7, 10, 8);
        AutomationProperties.SetName(this, "Daily Feature math preview");

        _view = new MathParagraphView { FontSize = 14 };
        _warning = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, Classes = { "fieldWarning" } };

        var heading = new TextBlock { Text = "PREVIEW", FontSize = 10.5, FontWeight = FontWeight.SemiBold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) };
        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { heading, _view, _warning } };
    }

    /// <summary>How long after the last edit the preview re-renders.</summary>
    public static TimeSpan UpdateDelay { get; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The Daily Feature source text (bound to the editor's text).</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>The warning currently shown, or null (test seam).</summary>
    public string? WarningText => _warning.IsVisible ? _warning.Text : null;

    /// <summary>The text currently drawn (test seam).</summary>
    public string? RenderedText => _view.Text;

    /// <summary>True while an edit is waiting for the coalescing delay (test seam).</summary>
    public bool HasPendingUpdate => _pending is not null;

    /// <summary>Applies the latest <see cref="Source"/> now (also used by tests instead of waiting).</summary>
    public void FlushPendingUpdate()
    {
        CancelPending();
        Apply(Source);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Nothing pending survives the editor leaving the screen; re-attaching shows the current text.
        CancelPending();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply(Source);
    }

    private void ScheduleUpdate()
    {
        var text = Source;

        // Leaving math (or loading another date's plain text) hides the preview at once; no delay needed.
        if (!DailyFeatureMathText.MayContainMath(text))
        {
            CancelPending();
            Apply(text);
            return;
        }

        CancelPending();
        var pending = new CancellationTokenSource();
        _pending = pending;
        _ = DelayThenApplyAsync(pending);
    }

    private async Task DelayThenApplyAsync(CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(UpdateDelay, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // A superseded or abandoned update never renders.
            if (ReferenceEquals(_pending, pending) && !pending.IsCancellationRequested)
            {
                FlushPendingUpdate();
            }
        });
    }

    private void CancelPending()
    {
        var pending = _pending;
        _pending = null;
        pending?.Cancel();
        pending?.Dispose();
    }

    private void Apply(string? text)
    {
        if (!DailyFeatureMathText.MayContainMath(text))
        {
            IsVisible = false;
            _view.Text = null;
            _warning.IsVisible = false;
            return;
        }

        string? warning;
        try
        {
            var analysis = DailyFeatureMathText.Analyze(text);
            warning = analysis.Issues.Count == 0 ? null : $"Shown as typed: {analysis.Issues[0].Message}";
            if (MathParagraphRenderer.Shared is null)
            {
                warning = "Math preview isn’t available on this computer; the Daily Feature is saved and printed as typed.";
            }
        }
        catch (Exception)
        {
            warning = "This math couldn’t be previewed; it’s saved exactly as typed.";
        }

        _view.Text = text;
        _warning.Text = warning;
        _warning.IsVisible = warning is not null;
        IsVisible = true;
    }
}
