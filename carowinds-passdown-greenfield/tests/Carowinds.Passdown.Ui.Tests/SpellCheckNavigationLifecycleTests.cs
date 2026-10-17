using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.SpellCheck;
using Carowinds.Passdown.Desktop.Spelling;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 20C: the spell-check underline belongs to the editor that is on screen. The shell navigates
/// by hiding whole sections (<c>IsVisible</c> on a wrapper Border, views never detached) — the defect
/// was the window-level adorner staying in the adorner layer and rendering over the next screen.
/// These tests host a real (headless, off-screen) <see cref="Window"/> laid out like the shell — a
/// section Border wrapping the editor — and inspect the window's actual <see cref="AdornerLayer"/>.
///
/// Every test is synchronous (no <c>await</c>), so it stays on the thread that set up the headless
/// platform; like the other real-Window classes it is also run alone in the deterministic partition.
/// </summary>
public sealed class SpellCheckNavigationLifecycleTests : IDisposable
{
    private const string Misspelled = "gate will opne";

    private sealed class Backend(params string[] words) : ISpellingBackend
    {
        private readonly HashSet<string> _words = new(words, StringComparer.OrdinalIgnoreCase);

        public bool IsAvailable => true;

        public bool CanAddToDictionary => true;

        public bool IsCorrect(string word) => _words.Contains(word);

        public IReadOnlyList<string> Suggest(string word) => [];

        public void AddToDictionary(string word) => _words.Add(word);
    }

    /// <summary>Counts checks and live DictionaryChanged subscriptions (to prove no handler stacking).</summary>
    private sealed class CountingService(ISpellCheckService inner) : ISpellCheckService
    {
        private EventHandler? _handlers;

        public int Checks { get; private set; }

        public int Subscribers => _handlers?.GetInvocationList().Length ?? 0;

        public bool IsAvailable => inner.IsAvailable;

        public bool CanAddToDictionary => inner.CanAddToDictionary;

        public IReadOnlyList<SpellingIssue> Check(string? text)
        {
            Checks++;
            return inner.Check(text);
        }

        public IReadOnlyList<string> Suggest(string word) => inner.Suggest(word);

        public void Ignore(string word)
        {
            inner.Ignore(word);
            _handlers?.Invoke(this, EventArgs.Empty);
        }

        public void AddToDictionary(string word) => inner.AddToDictionary(word);

        public event EventHandler? DictionaryChanged
        {
            add => _handlers += value;
            remove => _handlers -= value;
        }
    }

    private sealed record Shell(Window Window, Border Section, Border OtherSection, TextBox Box, SpellCheckController Controller, AdornerLayer Layer) : IDisposable
    {
        public IReadOnlyList<SpellCheckAdorner> Adorners => Layer.Children.OfType<SpellCheckAdorner>().ToList();

        /// <summary>Navigate the way MainWindow does: hide this section, show another.</summary>
        public void NavigateAway()
        {
            Section.IsVisible = false;
            OtherSection.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
        }

        public void NavigateBack()
        {
            OtherSection.IsVisible = false;
            Section.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
            }
        }
    }

    public void Dispose() => SpellCheck.Service = null;

    private static Shell Open(ISpellCheckService service, string? text = Misspelled)
    {
        SpellCheck.Service = service;
        var box = new TextBox { Text = text, Width = 300 };
        SpellCheck.SetIsEnabled(box, true);
        var section = new Border { Child = box };
        var other = new Border { Child = new TextBlock { Text = "Dashboard" }, IsVisible = false };
        var window = new Window { Width = 500, Height = 300, Content = new Grid { Children = { section, other } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var layer = window.GetVisualDescendants().OfType<AdornerLayer>().Single();
        return new Shell(window, section, other, box, SpellCheck.GetController(box)!, layer);
    }

    /// <summary>Lets the real 350 ms debounce elapse, then runs what it posted to the UI thread.</summary>
    private static void WaitForDebounce()
    {
        Thread.Sleep(SpellCheck.CheckDelay + TimeSpan.FromMilliseconds(250));
        Dispatcher.UIThread.RunJobs();
    }

    private static SpellCheckService NewService() => new(new Backend("gate", "will", "open"));

    [Fact]
    public void Misspelling_IsUnderlined_ThenNavigatingAwayRemovesTheAdornment_TextUntouched()
    {
        using var shell = Open(NewService());
        WaitForDebounce();

        var adorner = Assert.Single(shell.Adorners);
        Assert.Equal("opne", Assert.Single(adorner.Issues).Word);

        shell.NavigateAway();

        Assert.Empty(shell.Adorners);
        Assert.Empty(shell.Controller.Issues);
        Assert.False(shell.Controller.IsLive);
        Assert.Equal(Misspelled, shell.Box.Text);
    }

    [Fact]
    public void PendingCheck_NavigatingAwayBeforeTheDelay_NeverRendersAfterwards()
    {
        using var shell = Open(NewService(), text: "gate");
        WaitForDebounce();
        Assert.Empty(shell.Adorners.SelectMany(a => a.Issues));

        shell.Box.Text = Misspelled; // starts a debounced check
        shell.NavigateAway(); // before the 350 ms elapse
        WaitForDebounce();

        Assert.Empty(shell.Adorners);
        Assert.Empty(shell.Controller.Issues);
        Assert.Equal(Misspelled, shell.Box.Text);
    }

    [Fact]
    public void NavigatingBack_ReChecksTheUnchangedMisspelling()
    {
        using var shell = Open(NewService());
        WaitForDebounce();
        shell.NavigateAway();

        shell.NavigateBack();
        WaitForDebounce();

        Assert.Equal(Misspelled, shell.Box.Text);
        var adorner = Assert.Single(shell.Adorners);
        Assert.Equal("opne", Assert.Single(adorner.Issues).Word);
        Assert.True(shell.Controller.IsLive);
    }

    [Fact]
    public void RepeatedNavigation_NeverStacksAdornersOrHandlers()
    {
        var service = new CountingService(NewService());
        using var shell = Open(service);
        WaitForDebounce();

        for (var i = 0; i < 5; i++)
        {
            shell.NavigateAway();
            Assert.Empty(shell.Adorners);
            Assert.Equal(0, service.Subscribers); // a hidden editor is not held by the shared service

            shell.NavigateBack();
            WaitForDebounce();
            Assert.Single(shell.Adorners);
            Assert.Equal(1, service.Subscribers);
        }

        // One dictionary change → exactly one re-check (one handler, not six).
        var before = service.Checks;
        service.Ignore("zzz");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, service.Checks);
    }

    [Fact]
    public void HiddenEditor_DictionaryChange_DoesNotCheckOrRender()
    {
        var service = new CountingService(NewService());
        using var shell = Open(service);
        WaitForDebounce();
        shell.NavigateAway();

        var before = service.Checks;
        service.Ignore("zzz");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, service.Checks);
        Assert.Empty(shell.Adorners);
    }

    [Fact]
    public void DetachingAndReattachingTheEditor_RemovesThenRestoresExactlyOneAdorner()
    {
        using var shell = Open(NewService());
        WaitForDebounce();
        Assert.Single(shell.Adorners);

        shell.Section.Child = null; // control removed from the visual tree
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.Adorners);

        shell.Section.Child = shell.Box; // recreated/re-added
        Dispatcher.UIThread.RunJobs();
        WaitForDebounce();

        var adorner = Assert.Single(shell.Adorners);
        Assert.Equal("opne", Assert.Single(adorner.Issues).Word);
    }

    [Fact]
    public void WindowClose_WithAPendingCheck_NoCallbackTouchesTheClosedUi()
    {
        var service = new CountingService(NewService());
        var shell = Open(service, text: "gate");
        WaitForDebounce();
        var checksBefore = service.Checks;

        shell.Box.Text = Misspelled; // pending debounced check
        shell.Window.Close();
        WaitForDebounce();

        Assert.Equal(checksBefore, service.Checks);
        Assert.Empty(shell.Controller.Issues);
        Assert.False(shell.Controller.IsLive);
        Assert.Null(shell.Controller.Adorner);
        Assert.Equal(0, service.Subscribers);
    }
}
