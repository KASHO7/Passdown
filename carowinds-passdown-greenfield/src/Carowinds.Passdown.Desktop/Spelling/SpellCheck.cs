using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Desktop.Spelling;

/// <summary>
/// Phase 20A: opt-in, advisory spell checking for a <see cref="TextBox"/> —
/// <c>spell:SpellCheck.IsEnabled="True"</c> in XAML. Avalonia's TextBox has no platform spell check of
/// its own, so this attached behavior adds the familiar experience on top of it: red wavy underlines
/// (<see cref="SpellCheckAdorner"/>) and, on right-click (or the context-menu key) over a flagged word,
/// suggestions plus "Ignore" / "Add to dictionary" ahead of the usual Cut/Copy/Paste.
///
/// Advisory only: it never blocks typing, saving, autosave, PDF, finalization, Teams copy or shutdown,
/// and never changes text except when the user picks a suggestion (which goes through the normal Text
/// binding like typing). Checks are debounced and the service caches per word, so typing stays
/// responsive. Any failure simply leaves the field without underlines. View models know nothing about it.
/// </summary>
public static class SpellCheck
{
    /// <summary>Debounce between the last keystroke and a re-check.</summary>
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(350);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(SpellCheck));

    /// <summary>
    /// Phase 21D: when true (the Daily Feature editor), delimited math — <c>\( … \)</c> and
    /// <c>\[ … \]</c> — is left out of checking so LaTeX commands such as <c>\frac</c> or <c>\partial</c>
    /// are never underlined, while the prose around them is still checked at the same offsets.
    /// </summary>
    public static readonly AttachedProperty<bool> ExcludeMathProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("ExcludeMath", typeof(SpellCheck));

    private static readonly AttachedProperty<SpellCheckController?> ControllerProperty =
        AvaloniaProperty.RegisterAttached<TextBox, SpellCheckController?>("Controller", typeof(SpellCheck));

    static SpellCheck()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>(OnIsEnabledChanged);
    }

    /// <summary>The app's spell-check service, set once by the composition root. Null = spell check off.</summary>
    public static ISpellCheckService? Service { get; set; }

    public static bool GetIsEnabled(TextBox textBox) => textBox.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBox textBox, bool value) => textBox.SetValue(IsEnabledProperty, value);

    public static bool GetExcludeMath(TextBox textBox) => textBox.GetValue(ExcludeMathProperty);

    public static void SetExcludeMath(TextBox textBox, bool value) => textBox.SetValue(ExcludeMathProperty, value);

    /// <summary>The text actually handed to the spell checker for <paramref name="textBox"/>: math masked out when <see cref="ExcludeMathProperty"/> is set.</summary>
    internal static string? TextToCheck(TextBox textBox, string? text) =>
        GetExcludeMath(textBox) ? DailyFeatureMathText.MaskMathForSpellCheck(text) : text;

    /// <summary>Test seam: the controller attached to <paramref name="textBox"/>, if any.</summary>
    internal static SpellCheckController? GetController(TextBox textBox) => textBox.GetValue(ControllerProperty);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        textBox.GetValue(ControllerProperty)?.Dispose();
        textBox.SetValue(ControllerProperty, e.NewValue is true ? new SpellCheckController(textBox) : null);
    }
}

/// <summary>
/// Per-TextBox spell-check wiring; see <see cref="SpellCheck"/>.
///
/// Phase 20C lifecycle: the underline is drawn in the window's adorner layer, which is outside the
/// TextBox's own visual subtree — so it does not disappear just because the editor does. The shell
/// navigates by hiding whole sections (<c>IsVisible</c> on a wrapper; the views live for the app's
/// lifetime and are never detached), and Avalonia only removes an adorner when its element leaves the
/// visual tree, so before this phase the last underline kept rendering over whatever screen came next.
/// The controller therefore owns the adorner's presence: it exists only while the editor is
/// <em>live</em> (attached to a visual tree and effectively visible). Going not-live cancels the
/// pending debounced check, removes the adorner from the layer and stops listening for dictionary
/// changes; becoming live again re-attaches and re-checks the (unchanged) text.
/// </summary>
internal sealed class SpellCheckController : IDisposable
{
    private readonly TextBox _textBox;
    private CancellationTokenSource? _pendingCheck;
    private TextPresenter? _presenter;
    private SpellCheckAdorner? _adorner;
    private AdornerLayer? _adornerLayer;
    private readonly List<Visual> _watchedAncestors = [];
    private ISpellCheckService? _subscribedService;
    private bool _disposed;

    public SpellCheckController(TextBox textBox)
    {
        _textBox = textBox;

        _textBox.TemplateApplied += OnTemplateApplied;
        _textBox.AttachedToVisualTree += OnAttachedToVisualTree;
        _textBox.DetachedFromVisualTree += OnDetachedFromVisualTree;
        _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        _textBox.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        if (_textBox.IsAttachedToVisualTree())
        {
            WatchAncestors();
        }

        if (IsLive)
        {
            GoLive();
        }
    }

    /// <summary>True while the editor is on screen: attached to a visual tree and effectively visible.</summary>
    internal bool IsLive => !_disposed && _textBox.IsAttachedToVisualTree() && _textBox.IsEffectivelyVisible;

    /// <summary>The issues from the most recent check (test seam).</summary>
    public IReadOnlyList<SpellingIssue> Issues { get; private set; } = [];

    internal SpellCheckAdorner? Adorner => _adorner;

    /// <summary>Runs the pending check now (tests, and after a replacement/ignore).</summary>
    public void CheckNow()
    {
        CancelPendingCheck();
        if (_disposed)
        {
            return;
        }

        var text = _textBox.Text;
        try
        {
            Issues = SpellCheck.Service?.Check(SpellCheck.TextToCheck(_textBox, text)) ?? [];
        }
        catch (Exception)
        {
            Issues = []; // advisory: never let spell checking affect editing
        }

        _adorner?.Update(text, Issues);
    }

    /// <summary>The context-menu model for <paramref name="textIndex"/>, or null when no misspelled word is there (test seam).</summary>
    public SpellCheckMenuModel? BuildMenuModel(int textIndex) =>
        SpellCheck.Service is { } service ? SpellCheckMenuModel.Build(Issues, textIndex, service) : null;

    /// <summary>Replaces the flagged word with a suggestion through the normal Text property (and so the normal binding/autosave).</summary>
    public void ApplySuggestion(SpellingIssue issue, string replacement)
    {
        if (SpellCheckMenuModel.ApplyReplacement(_textBox.Text, issue, replacement, _textBox.MaxLength) is not { } result)
        {
            return;
        }

        _textBox.Text = result.Text;
        _textBox.CaretIndex = result.CaretIndex;
        CheckNow();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingCheck();
        _textBox.TemplateApplied -= OnTemplateApplied;
        _textBox.AttachedToVisualTree -= OnAttachedToVisualTree;
        _textBox.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        _textBox.RemoveHandler(Control.ContextRequestedEvent, OnContextRequested);
        UnwatchAncestors();
        Unsubscribe();
        DetachAdorner();
    }

    private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        DetachAdorner();
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        if (IsLive)
        {
            AttachAdorner();
        }
    }

    private void AttachToTemplate()
    {
        _presenter ??= FindPresenter(_textBox);
        AttachAdorner();
    }

    private static TextPresenter? FindPresenter(Visual root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is TextPresenter presenter)
            {
                return presenter;
            }

            if (FindPresenter(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void AttachAdorner()
    {
        if (_presenter is null || _adorner is not null)
        {
            return;
        }

        _adorner = new SpellCheckAdorner(_presenter);
        AdornerLayer.SetAdorner(_presenter, _adorner);
        _adornerLayer = AdornerLayer.GetAdornerLayer(_presenter);
        _presenter.LayoutUpdated += OnPresenterLayoutUpdated;
        _adorner.Update(_textBox.Text, Issues);
    }

    /// <summary>
    /// Removes the underline visual. Clearing the attached property alone is not enough when the editor
    /// has just left the visual tree: Avalonia then cannot walk up to the layer any more, so the adorner
    /// is also removed from the layer captured when it was added (a no-op if Avalonia already did).
    /// </summary>
    private void DetachAdorner()
    {
        if (_presenter is not null)
        {
            _presenter.LayoutUpdated -= OnPresenterLayoutUpdated;
            if (_adorner is not null)
            {
                AdornerLayer.SetAdorner(_presenter, null);
            }
        }

        if (_adorner is not null)
        {
            _adornerLayer?.Children.Remove(_adorner);
        }

        _adorner = null;
        _adornerLayer = null;
    }

    private void OnPresenterLayoutUpdated(object? sender, EventArgs e) => _adorner?.InvalidateVisual();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        WatchAncestors();
        UpdateLiveness();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnwatchAncestors();
        GoDormant();
    }

    /// <summary>
    /// Avalonia 12 has no public "effective visibility changed" event, so the editor and each visual
    /// ancestor (up to the window) is watched for <see cref="Visual.IsVisibleProperty"/> — the shell's
    /// section switch is exactly such an ancestor change. Watched only while attached; the ancestors are
    /// re-collected on every attach, so a re-parented editor never keeps listening to its old parents.
    /// </summary>
    private void WatchAncestors()
    {
        UnwatchAncestors();
        for (Visual? visual = _textBox; visual is not null; visual = visual.GetVisualParent())
        {
            visual.PropertyChanged += OnAncestorPropertyChanged;
            _watchedAncestors.Add(visual);
        }
    }

    private void UnwatchAncestors()
    {
        foreach (var visual in _watchedAncestors)
        {
            visual.PropertyChanged -= OnAncestorPropertyChanged;
        }

        _watchedAncestors.Clear();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
        {
            UpdateLiveness();
        }
    }

    private void UpdateLiveness()
    {
        if (IsLive)
        {
            GoLive();
        }
        else
        {
            GoDormant();
        }
    }

    /// <summary>Editor on screen (again): draw from a fresh check of the current, unchanged text.</summary>
    private void GoLive()
    {
        AttachToTemplate();
        Subscribe();
        ScheduleCheck();
    }

    /// <summary>
    /// Editor gone (navigated away, hidden, detached, window closed): no pending check may complete, no
    /// stale underline may stay on screen, and the shared service no longer references this editor. The
    /// text itself is untouched; issues are recomputed when the editor is live again.
    /// </summary>
    private void GoDormant()
    {
        CancelPendingCheck();
        Unsubscribe();
        DetachAdorner();
        Issues = [];
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            ScheduleCheck();
        }
    }

    /// <summary>
    /// Debounce: each edit cancels the previous pending check (the same cancellable-delay pattern the
    /// Associate autosave uses), so a burst of typing produces one check after the pause.
    /// </summary>
    private void ScheduleCheck()
    {
        if (!IsLive || SpellCheck.Service is null)
        {
            return;
        }

        CancelPendingCheck();
        var pending = new CancellationTokenSource();
        _pendingCheck = pending;
        _ = DelayThenCheckAsync(pending.Token);
    }

    private async Task DelayThenCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SpellCheck.CheckDelay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            // A late result never renders: the check was superseded, or the editor left the screen.
            if (!cancellationToken.IsCancellationRequested && IsLive)
            {
                CheckNow();
            }
        });
    }

    private void CancelPendingCheck()
    {
        _pendingCheck?.Cancel();
        _pendingCheck?.Dispose();
        _pendingCheck = null;
    }

    private void Subscribe()
    {
        if (_subscribedService is null && SpellCheck.Service is { } service)
        {
            _subscribedService = service;
            service.DictionaryChanged += OnDictionaryChanged;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedService is not null)
        {
            _subscribedService.DictionaryChanged -= OnDictionaryChanged;
            _subscribedService = null;
        }
    }

    private void OnDictionaryChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (IsLive)
        {
            CheckNow();
        }
    });

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled || SpellCheck.Service is not { } service || Issues.Count == 0)
        {
            return;
        }

        try
        {
            var index = ResolveTextIndex(e);
            if (BuildMenuModel(index) is not { } model)
            {
                return; // not on a flagged word: the normal Cut/Copy/Paste menu opens
            }

            BuildContextMenu(model, service).Open(_textBox);
            e.Handled = true;
        }
        catch (Exception)
        {
            // advisory: fall back to the TextBox's normal context menu
        }
    }

    private int ResolveTextIndex(ContextRequestedEventArgs e)
    {
        if (_presenter is not null && e.TryGetPosition(_presenter, out var point))
        {
            return _presenter.TextLayout.HitTestPoint(point).TextPosition;
        }

        return _textBox.CaretIndex; // keyboard (context-menu key / Shift+F10)
    }

    private ContextMenu BuildContextMenu(SpellCheckMenuModel model, ISpellCheckService service)
    {
        var items = new List<Control>();
        if (model.Suggestions.Count == 0)
        {
            items.Add(new MenuItem { Header = "No suggestions", IsEnabled = false });
        }

        foreach (var suggestion in model.Suggestions)
        {
            var item = new MenuItem { Header = suggestion, FontWeight = Avalonia.Media.FontWeight.SemiBold };
            item.Click += (_, _) => ApplySuggestion(model.Issue, suggestion);
            items.Add(item);
        }

        items.Add(new Separator());

        var ignore = new MenuItem { Header = "Ignore" };
        ignore.Click += (_, _) => service.Ignore(model.Issue.Word);
        items.Add(ignore);

        if (model.CanAddToDictionary)
        {
            var add = new MenuItem { Header = "Add to dictionary" };
            add.Click += (_, _) => service.AddToDictionary(model.Issue.Word);
            items.Add(add);
        }

        items.Add(new Separator());

        var cut = new MenuItem { Header = "Cut", IsEnabled = _textBox.CanCut };
        cut.Click += (_, _) => _textBox.Cut();
        var copy = new MenuItem { Header = "Copy", IsEnabled = _textBox.CanCopy };
        copy.Click += (_, _) => _textBox.Copy();
        var paste = new MenuItem { Header = "Paste", IsEnabled = _textBox.CanPaste };
        paste.Click += (_, _) => _textBox.Paste();
        items.Add(cut);
        items.Add(copy);
        items.Add(paste);

        return new ContextMenu { ItemsSource = items };
    }
}
