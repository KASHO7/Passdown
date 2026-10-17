using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Application.SpellCheck;
using Carowinds.Passdown.Desktop.Controls;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Spelling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21D: the Daily Feature math editing experience without a real window — symbol/template
/// insertion through the TextBox's own typing path, the coalesced preview, math-aware spell checking
/// and the Associate markup. Focus restoration and real-delay behavior live in
/// <see cref="Phase21DMathEditorWindowTests"/> (a real-Window class, run alone).
/// </summary>
public sealed partial class Phase21DDailyFeatureMathUiTests : IDisposable
{
    private sealed partial class FeatureViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string? Text { get; set; }

        public int Changes { get; private set; }

        partial void OnTextChanged(string? value) => Changes++;
    }

    private sealed class Backend(params string[] words) : ISpellingBackend
    {
        private readonly HashSet<string> _words = new(words, StringComparer.OrdinalIgnoreCase);

        public bool IsAvailable => true;

        public bool CanAddToDictionary => false;

        public bool IsCorrect(string word) => _words.Contains(word);

        public IReadOnlyList<string> Suggest(string word) => [];

        public void AddToDictionary(string word)
        {
        }
    }

    public void Dispose() => SpellCheck.Service = null;

    private static (TextBox Box, MathSymbolPicker Picker) Editor(string text)
    {
        var box = new TextBox { Text = text, AcceptsReturn = true };
        return (box, new MathSymbolPicker { Target = box });
    }

    // ---- Insertion -------------------------------------------------------------------------------

    [Fact]
    public void Symbol_IsInsertedAtTheCaret_PreservingSurroundingText()
    {
        var (box, picker) = Editor("Area = r");
        box.SelectionStart = box.SelectionEnd = 7;

        Assert.True(picker.InsertSymbol("π"));

        Assert.Equal("Area = πr", box.Text);
        Assert.Equal(8, box.CaretIndex);
    }

    [Fact]
    public void Symbol_ReplacesTheSelectedText()
    {
        var (box, picker) = Editor("x <= y");
        box.SelectionStart = 2;
        box.SelectionEnd = 4;

        picker.InsertSymbol("≤");

        Assert.Equal("x ≤ y", box.Text);
        Assert.Equal(3, box.CaretIndex);
    }

    [Fact]
    public void Symbol_AtTheStart_DoesNotAppendToTheEnd()
    {
        var (box, picker) = Editor("x dx");
        box.SelectionStart = box.SelectionEnd = 0;

        picker.InsertSymbol("∫");

        Assert.Equal("∫x dx", box.Text);
    }

    [Theory]
    [InlineData("Inline math")]
    [InlineData("Display equation")]
    [InlineData("Fraction")]
    [InlineData("Derivative")]
    [InlineData("Partial derivative")]
    [InlineData("Integral")]
    [InlineData("Summation")]
    [InlineData("Square root")]
    [InlineData("nth root")]
    [InlineData("Limit")]
    [InlineData("Power")]
    [InlineData("Subscript")]
    public void Template_IsInserted_WithItsPlaceholderSelected(string label)
    {
        var template = MathSymbolCatalog.Templates.Single(t => t.Label == label);
        var (box, picker) = Editor("Try ");
        box.SelectionStart = box.SelectionEnd = 4;

        Assert.True(picker.InsertTemplate(template));

        Assert.Equal("Try " + template.Text, box.Text);
        Assert.Equal(template.Text.Substring(template.SelectStart, template.SelectLength), box.SelectedText);
        Assert.False(string.IsNullOrWhiteSpace(box.SelectedText));
    }

    [Fact]
    public void TemplateTheory_CoversEveryTemplate() =>
        Assert.Equal(12, MathSymbolCatalog.Templates.Count); // keep the InlineData list above in step (no MemberData in this assembly)

    [Fact]
    public void Insertion_IsOneUndoableStep()
    {
        var (box, picker) = Editor("a + b");
        box.SelectionStart = box.SelectionEnd = 5;

        picker.InsertSymbol("²");
        Assert.Equal("a + b²", box.Text);

        box.Undo();
        Assert.Equal("a + b", box.Text);

        box.Redo();
        Assert.Equal("a + b²", box.Text);
    }

    [Fact]
    public void Insertion_FlowsThroughTheTwoWayBinding_LikeTyping()
    {
        var vm = new FeatureViewModel();
        var box = new TextBox { DataContext = vm };
        box.Bind(TextBox.TextProperty, new Binding(nameof(FeatureViewModel.Text)) { Mode = BindingMode.TwoWay });
        box.Text = "E = mc";
        var picker = new MathSymbolPicker { Target = box };
        box.SelectionStart = box.SelectionEnd = 6;
        var before = vm.Changes;

        picker.InsertSymbol("²");

        Assert.Equal("E = mc²", vm.Text);
        Assert.True(vm.Changes > before, "the edit reaches the view model, which schedules autosave");
    }

    [Fact]
    public void ReadOnlyEditor_IsNeverChanged()
    {
        var (box, picker) = Editor("Final text");
        box.IsReadOnly = true;
        box.SelectionStart = box.SelectionEnd = 5;

        Assert.False(picker.InsertSymbol("∑"));
        Assert.Equal("Final text", box.Text);
    }

    [Fact]
    public void ManyInsertions_NeverLoseText()
    {
        var (box, picker) = Editor("start|end");
        var random = new Random(21);
        var expected = "start|end";
        foreach (var symbol in MathSymbolCatalog.Categories.SelectMany(c => c.Symbols).Select(s => s.Symbol))
        {
            var caret = random.Next(0, expected.Length + 1);
            box.SelectionStart = box.SelectionEnd = caret;
            picker.InsertSymbol(symbol);
            expected = expected.Insert(caret, symbol);
            Assert.Equal(expected, box.Text);
        }
    }

    [Fact]
    public void Catalog_IsCompact_Categorized_AndEveryTemplateIsSupportedMath()
    {
        Assert.Equal(["Calculus", "Greek", "Operators", "Sets and logic", "Other"], MathSymbolCatalog.Categories.Select(c => c.Name));
        var symbols = MathSymbolCatalog.Categories.SelectMany(c => c.Symbols).ToList();
        Assert.InRange(symbols.Count, 30, 80);
        Assert.Equal(symbols.Count, symbols.Select(s => s.Symbol).Distinct().Count());
        foreach (var expected in new[] { "∫", "∮", "∂", "∇", "∞", "α", "β", "γ", "Δ", "θ", "λ", "μ", "π", "Σ", "Ω", "±", "×", "÷", "≈", "≠", "≤", "≥", "∑", "∏", "√", "∈", "∉", "⊂", "⊆", "∪", "∩", "∀", "∃", "⇒", "°", "∝", "→", "↔", "·", "∴" })
        {
            Assert.Contains(symbols, s => s.Symbol == expected);
        }

        Assert.All(MathSymbolCatalog.Templates, t => Assert.True(DailyFeatureMathText.Analyze(t.Text).HasRenderableMath, t.Label));
    }

    // ---- Preview ---------------------------------------------------------------------------------

    [Fact]
    public void Preview_IsHidden_ForOrdinaryText()
    {
        var preview = new DailyFeatureMathPreview { Source = "Question of the Day: What is your favorite roller coaster? $5, a_b, x^2" };
        Assert.False(preview.IsVisible);
        Assert.False(preview.HasPendingUpdate);
    }

    [Fact]
    public void Preview_CoalescesEdits_ThenShowsTheFormula()
    {
        var preview = new DailyFeatureMathPreview();
        preview.Source = @"The derivative is \(";
        preview.Source = @"The derivative is \(\frac{d}{dx}";
        preview.Source = @"The derivative is \(\frac{d}{dx}x^2=2x\).";

        Assert.True(preview.HasPendingUpdate);
        Assert.Null(preview.RenderedText); // nothing rendered per keystroke

        preview.FlushPendingUpdate();

        Assert.True(preview.IsVisible);
        Assert.Equal(@"The derivative is \(\frac{d}{dx}x^2=2x\).", preview.RenderedText);
        Assert.Null(preview.WarningText);
    }

    [Theory]
    [InlineData(@"\(\frac{1}{\)")]
    [InlineData(@"Incomplete \(\frac{1}{")]
    [InlineData(@"\(\unknowncommand\)")]
    [InlineData(@"\(" + "{{{{{{{{{{{{{{{{{{{{x}}}}}}}}}}}}}}}}}}}}" + @"\)")]
    public void Preview_InvalidMath_ShowsAQuietWarning_NeverThrows(string source)
    {
        var preview = new DailyFeatureMathPreview { Source = source };
        preview.FlushPendingUpdate();

        Assert.True(preview.IsVisible);
        Assert.StartsWith("Shown as typed:", preview.WarningText);
        Assert.DoesNotContain("Exception", preview.WarningText);
    }

    [Fact]
    public void Preview_HidesImmediately_WhenTheMathIsRemoved()
    {
        var preview = new DailyFeatureMathPreview { Source = @"\(x\)" };
        preview.FlushPendingUpdate();
        Assert.True(preview.IsVisible);

        preview.Source = "Just words now";

        Assert.False(preview.IsVisible);
        Assert.False(preview.HasPendingUpdate);
    }

    [Fact]
    public void MathParagraphView_MeasuresToTheRenderedHeight()
    {
        var view = new MathParagraphView { Text = "Fact\n\\[\\int_0^1 x^2\\,dx=\\frac13\\]" };
        view.Measure(new Size(400, double.PositiveInfinity));

        Assert.NotNull(view.Paragraph);
        Assert.Equal(2, view.Paragraph.Lines.Count);
        Assert.InRange(view.DesiredSize.Height, 30, 120);
    }

    // ---- Spell check -----------------------------------------------------------------------------

    [Fact]
    public void ExcludeMath_SkipsLatexCommands_ButStillChecksTheProse()
    {
        SpellCheck.Service = new SpellCheckService(new Backend("the", "is", "and"));
        var box = new TextBox { Text = @"The derivitive is \( \frac{d}{dx} spd \) and" };
        SpellCheck.SetIsEnabled(box, true);
        SpellCheck.SetExcludeMath(box, true);
        var controller = SpellCheck.GetController(box)!;

        controller.CheckNow();

        var issue = Assert.Single(controller.Issues);
        Assert.Equal("derivitive", issue.Word);
        Assert.Equal(4, issue.Start); // offsets are into the real text, so the underline lines up
    }

    [Fact]
    public void WithoutExcludeMath_OtherFieldsBehaveExactlyAsBefore()
    {
        var box = new TextBox();
        const string text = @"\( spd \)";
        Assert.Equal(text, SpellCheck.TextToCheck(box, text));
        SpellCheck.SetExcludeMath(box, true);
        Assert.Equal(new string(' ', text.Length), SpellCheck.TextToCheck(box, text));
    }

    // ---- Markup ----------------------------------------------------------------------------------

    private static string AssociateMarkup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml");
            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("AssociateView.axaml");
    }

    [Fact]
    public void Markup_AddsThePickerAndPreview_ToTheDailyFeatureOnly()
    {
        var markup = AssociateMarkup();

        Assert.Single(Regex.Matches(markup, "<controls:MathSymbolPicker"));
        Assert.Single(Regex.Matches(markup, "<controls:DailyFeatureMathPreview"));
        Assert.Single(Regex.Matches(markup, "SpellCheck.ExcludeMath=\"True\""));
        Assert.Contains("Target=\"{Binding #DailyFeatureTextBox}\"", markup);
        Assert.Contains("Source=\"{Binding #DailyFeatureTextBox.Text}\"", markup);

        var editor = Regex.Match(markup, "<TextBox x:Name=\"DailyFeatureTextBox\"[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains("Text=\"{Binding DailyFeatureText}\"", editor);
        Assert.Contains("spell:SpellCheck.IsEnabled=\"True\"", editor);
        Assert.Contains("spell:SpellCheck.ExcludeMath=\"True\"", editor);
    }
}
