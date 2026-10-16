using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>Test seam: the controller attached to <paramref name="textBox"/>, if any.</summary>
    internal static SpellCheckController? GetController(TextBox textBox) => textBox.GetValue(ControllerProperty);

    private static void OnIsEnabledChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        textBox.GetValue(ControllerProperty)?.Dispose();
        textBox.SetValue(ControllerProperty, e.NewValue is true ? new SpellCheckController(textBox) : null);
    }
}

/// <summary>Per-TextBox spell-check wiring; see <see cref="SpellCheck"/>.</summary>
internal sealed class SpellCheckController : IDisposable
{
    private readonly TextBox _textBox;
    private CancellationTokenSource? _pendingCheck;
    private TextPresenter? _presenter;
    private SpellCheckAdorner? _adorner;
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
            AttachToTemplate();
            Subscribe();
            ScheduleCheck();
        }
    }

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
            Issues = SpellCheck.Service?.Check(text) ?? [];
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
        Unsubscribe();
        DetachAdorner();
    }

    private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        DetachAdorner();
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        AttachAdorner();
    }

    private void AttachToTemplate()
    {
        if (_presenter is null)
        {
            _presenter = FindPresenter(_textBox);
            AttachAdorner();
        }
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
        _presenter.LayoutUpdated += OnPresenterLayoutUpdated;
        _adorner.Update(_textBox.Text, Issues);
    }

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

        _adorner = null;
    }

    private void OnPresenterLayoutUpdated(object? sender, EventArgs e) => _adorner?.InvalidateVisual();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachToTemplate();
        Subscribe();
        ScheduleCheck();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        CancelPendingCheck();
        Unsubscribe();
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
        if (_disposed || SpellCheck.Service is null)
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
            if (!cancellationToken.IsCancellationRequested)
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

    private void OnDictionaryChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(CheckNow);

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
