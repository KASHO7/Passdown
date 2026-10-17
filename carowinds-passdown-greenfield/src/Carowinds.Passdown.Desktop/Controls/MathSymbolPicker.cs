using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Desktop.Controls;

/// <summary>
/// Phase 21D: the compact "∑ Math" button beside the Daily Feature editor. It opens a small
/// categorized popover of common symbols (<see cref="MathSymbolCatalog"/>) and equation templates;
/// a click inserts at the editor's caret — replacing any selected text — through
/// <see cref="TextBoxInsertion"/>, so it is an ordinary, undoable, autosaved edit. The popover is
/// built when first opened (nothing extra at view load) and closes after an insertion so focus goes
/// straight back to the editor. Picker buttons never take focus, and the editor's last caret/selection
/// is remembered while it has focus, so opening the picker never loses the insertion point.
/// </summary>
public sealed class MathSymbolPicker : Button
{
    public static readonly StyledProperty<TextBox?> TargetProperty =
        AvaloniaProperty.Register<MathSymbolPicker, TextBox?>(nameof(Target));

    private (int Start, int End)? _rememberedSelection;
    private TextBox? _observed;

    static MathSymbolPicker()
    {
        TargetProperty.Changed.AddClassHandler<MathSymbolPicker>((picker, _) => picker.ObserveTarget());
    }

    public MathSymbolPicker()
    {
        Content = "∑ Math";
        Focusable = false;
        ToolTip.SetTip(this, "Insert a math symbol or equation");
        AutomationProperties.SetName(this, "Insert math symbol or equation");

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        flyout.Opening += (_, _) => flyout.Content ??= BuildContent();
        Flyout = flyout;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The editor symbols are inserted into.</summary>
    public TextBox? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Inserts a symbol (test seam and click handler).</summary>
    public bool InsertSymbol(string symbol) => InsertCore(symbol, null, 0);

    /// <summary>Inserts a template and selects its placeholder (test seam and click handler).</summary>
    public bool InsertTemplate(MathTemplate template) => InsertCore(template.Text, template.SelectStart, template.SelectLength);

    private bool InsertCore(string text, int? selectOffset, int selectLength)
    {
        if (Target is not { } target)
        {
            return false;
        }

        var (start, end) = _rememberedSelection ?? (target.SelectionStart, target.SelectionEnd);
        Flyout?.Hide();
        var inserted = TextBoxInsertion.Insert(target, text, start, end, selectOffset, selectLength);

        // From here the editor's own caret is current again (tracked while it has focus).
        _rememberedSelection = null;
        return inserted;
    }

    private void ObserveTarget()
    {
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnTargetPropertyChanged;
        }

        _observed = Target;
        _rememberedSelection = null;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnTargetPropertyChanged;
        }
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not TextBox target)
        {
            return;
        }

        if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty || e.Property == TextBox.CaretIndexProperty)
        {
            // Only while the editor has focus: losing focus may collapse the selection, which is not the user's choice.
            if (target.IsFocused)
            {
                _rememberedSelection = (target.SelectionStart, target.SelectionEnd);
            }
        }
        else if (e.Property == TextBox.TextProperty && !target.IsFocused)
        {
            // New text arrived from elsewhere (another date loaded, sync): an old position means nothing now.
            _rememberedSelection = null;
        }
    }

    private Control BuildContent()
    {
        var panel = new StackPanel { Spacing = 6, Width = 312 };

        foreach (var category in MathSymbolCatalog.Categories)
        {
            panel.Children.Add(Heading(category.Name));
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var entry in category.Symbols)
            {
                var button = new Button
                {
                    Content = entry.Symbol,
                    Width = 34,
                    Height = 30,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 3, 3),
                    FontSize = 15,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Focusable = false,
                };
                ToolTip.SetTip(button, entry.Name);
                AutomationProperties.SetName(button, $"Insert {entry.Name}");
                button.Click += (_, _) => InsertSymbol(entry.Symbol);
                wrap.Children.Add(button);
            }

            panel.Children.Add(wrap);
        }

        panel.Children.Add(Heading("Equation templates"));
        var templates = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var template in MathSymbolCatalog.Templates)
        {
            var button = new Button
            {
                Content = template.Label,
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 12,
                Focusable = false,
            };
            ToolTip.SetTip(button, template.Text);
            AutomationProperties.SetName(button, $"Insert {template.Label} template");
            button.Click += (_, _) => InsertTemplate(template);
            templates.Children.Add(button);
        }

        panel.Children.Add(templates);
        panel.Children.Add(new TextBlock
        {
            Text = "Symbols work anywhere. Templates use \\( … \\) for math in a sentence or \\[ … \\] for an equation on its own line.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Opacity = 0.75,
        });

        return new ScrollViewer { Content = panel, MaxHeight = 420 };
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.75,
        Margin = new Thickness(0, 2, 0, 0),
    };
}
