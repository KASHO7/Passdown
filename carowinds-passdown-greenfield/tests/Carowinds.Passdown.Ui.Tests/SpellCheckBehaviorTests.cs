using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Data;
using Carowinds.Passdown.Application.SpellCheck;
using Carowinds.Passdown.Desktop.Spelling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 20A: the spell-check integration boundary (attached behavior ↔ <see cref="ISpellCheckService"/>).
/// The on-screen red underline itself is platform rendering (Windows VM validation); these tests prove
/// what it draws from, the context-menu model, replacement through the normal binding, and that spell
/// checking can never block or break editing.
/// </summary>
public sealed partial class SpellCheckBehaviorTests : IDisposable
{
    private sealed class Backend(params string[] words) : ISpellingBackend
    {
        private readonly HashSet<string> _words = new(words, StringComparer.OrdinalIgnoreCase);

        public bool Throw { get; set; }

        public bool IsAvailable => true;

        public bool CanAddToDictionary => true;

        public bool IsCorrect(string word) => Throw ? throw new InvalidOperationException("boom") : _words.Contains(word);

        public IReadOnlyList<string> Suggest(string word) => word == "opne" ? ["open", "ope"] : [];

        public void AddToDictionary(string word) => _words.Add(word);
    }

    private sealed partial class NoteViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string? Text { get; set; }
    }

    public void Dispose() => SpellCheck.Service = null;

    private static (TextBox Box, SpellCheckController Controller, NoteViewModel Vm) Create(ISpellCheckService service)
    {
        SpellCheck.Service = service;
        var vm = new NoteViewModel();
        var box = new TextBox { DataContext = vm };
        box.Bind(TextBox.TextProperty, new Binding(nameof(NoteViewModel.Text)) { Mode = BindingMode.TwoWay });
        SpellCheck.SetIsEnabled(box, true);
        return (box, SpellCheck.GetController(box)!, vm);
    }

    [Fact]
    public void EnabledTextBox_FindsMisspellings_AndIgnoresDomainTerms()
    {
        var (box, controller, _) = Create(new SpellCheckService(new Backend("the", "gate", "will", "at")));

        box.Text = "The gate will opne at Carowinds";
        controller.CheckNow();

        var issue = Assert.Single(controller.Issues);
        Assert.Equal("opne", issue.Word);
    }

    [Fact]
    public void ContextMenuModel_OffersSuggestions_OnlyOverAFlaggedWord()
    {
        var (box, controller, _) = Create(new SpellCheckService(new Backend("gate", "will")));
        box.Text = "gate will opne";
        controller.CheckNow();

        var model = controller.BuildMenuModel(textIndex: 12);
        Assert.NotNull(model);
        Assert.Equal(["open", "ope"], model.Suggestions);
        Assert.True(model.CanAddToDictionary);
        Assert.Null(controller.BuildMenuModel(textIndex: 2));
    }

    [Fact]
    public void ApplyingASuggestion_UpdatesTheBoundViewModel_LikeTyping()
    {
        var (box, controller, vm) = Create(new SpellCheckService(new Backend("gate", "will", "open")));
        box.Text = "gate will opne";
        controller.CheckNow();

        controller.ApplySuggestion(controller.Issues.Single(), "open");

        Assert.Equal("gate will open", vm.Text);
        Assert.Equal(14, box.CaretIndex);
        Assert.Empty(controller.Issues);
    }

    [Fact]
    public void Ignore_ClearsTheWordForTheSession()
    {
        var service = new SpellCheckService(new Backend("gate"));
        var (box, controller, _) = Create(service);
        box.Text = "gate Zamperini";
        controller.CheckNow();
        Assert.Single(controller.Issues);

        service.Ignore("Zamperini");
        controller.CheckNow();

        Assert.Empty(controller.Issues);
    }

    [Fact]
    public void FailingSpellChecker_NeverBlocksEditingOrTheBinding()
    {
        var (box, controller, vm) = Create(new SpellCheckService(new Backend { Throw = true }));

        box.Text = "anything typed here sitll saves";
        controller.CheckNow();

        Assert.Empty(controller.Issues);
        Assert.Equal("anything typed here sitll saves", vm.Text);
    }

    [Fact]
    public void NoServiceConfigured_IsSimplyOff()
    {
        var (box, controller, vm) = Create(null!);
        SpellCheck.Service = null;

        box.Text = "wrnog";
        controller.CheckNow();

        Assert.Empty(controller.Issues);
        Assert.Equal("wrnog", vm.Text);
    }

    [Fact]
    public void DisablingTheBehavior_DetachesIt()
    {
        var (box, _, _) = Create(new SpellCheckService(new Backend()));

        SpellCheck.SetIsEnabled(box, false);

        Assert.Null(SpellCheck.GetController(box));
    }

    [Fact]
    public void Replacement_RespectsMaxLength_AndAStaleIssue()
    {
        var issue = new SpellingIssue(5, 4, "opne");

        Assert.Null(SpellCheckMenuModel.ApplyReplacement("gate opne", issue, "opened", maxLength: 10));
        Assert.Null(SpellCheckMenuModel.ApplyReplacement("gate shut", issue, "open"));
        Assert.Equal(("gate open", 9), SpellCheckMenuModel.ApplyReplacement("gate opne", issue, "open"));
    }

    /// <summary>The covered fields are exactly the substantial user-authored prose fields; structured fields stay unchecked.</summary>
    [Fact]
    public void Markup_SpellCheckIsOnForProseFields_AndOffForStructuredOnes()
    {
        var associate = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));
        var notes = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/OperationalNotesView.axaml"));
        var priceHistory = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/PriceHistoryView.axaml"));

        string[] covered = ["DailyFeatureText", "MonthlyErtNoteText", "OverrideReasonText", "PrivateBuyoutDetails", "NotesText", "OperationalNotesText"];
        foreach (var binding in covered)
        {
            Assert.Matches(new Regex($"spell:SpellCheck.IsEnabled=\"True\"\\s+Text=\"{{Binding {binding}"), associate);
        }

        Assert.Matches(new Regex("spell:SpellCheck.IsEnabled=\"True\"\\s+Text=\"{Binding EditorTitle}"), notes);
        Assert.Matches(new Regex("spell:SpellCheck.IsEnabled=\"True\"\\s+Text=\"{Binding EditorBody}"), notes);

        Assert.Equal(covered.Length, Regex.Matches(associate, "spell:SpellCheck.IsEnabled=\"True\"").Count);
        Assert.Equal(2, Regex.Matches(notes, "spell:SpellCheck.IsEnabled=\"True\"").Count);
        Assert.DoesNotContain("SpellCheck", priceHistory); // search box: not spell checked
    }
}
