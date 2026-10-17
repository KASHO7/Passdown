using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.Controls;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 21D: the math editor in a real (headless, off-screen) <see cref="Window"/> — focus returns to
/// the editor after an insertion, the caret/selection survives the editor losing focus, the preview's
/// real coalescing delay, and that leaving the screen cancels pending preview work (no late render).
/// Like the other real-Window classes this one runs alone in the deterministic partition.
/// </summary>
public sealed class Phase21DMathEditorWindowTests
{
    private sealed record Shell(Window Window, Border Section, Border Other, TextBox Box, TextBox OtherBox, MathSymbolPicker Picker, DailyFeatureMathPreview Preview) : IDisposable
    {
        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
            }
        }
    }

    private static Shell Open(string text)
    {
        var box = new TextBox { Text = text, Width = 300, AcceptsReturn = true };
        var otherBox = new TextBox { Text = "Operational note", Width = 300 };
        var picker = new MathSymbolPicker { Target = box };
        var preview = new DailyFeatureMathPreview();
        var section = new Border { Child = new StackPanel { Children = { box, picker, preview, otherBox } } };
        var other = new Border { Child = new TextBlock { Text = "Dashboard" }, IsVisible = false };
        var window = new Window { Width = 520, Height = 400, Content = new Grid { Children = { section, other } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Shell(window, section, other, box, otherBox, picker, preview);
    }

    private static void WaitForPreview()
    {
        Thread.Sleep(DailyFeatureMathPreview.UpdateDelay + TimeSpan.FromMilliseconds(250));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Insertion_RestoresFocusToTheEditor()
    {
        using var shell = Open("x dx");
        shell.OtherBox.Focus();
        Dispatcher.UIThread.RunJobs();
        shell.Box.SelectionStart = shell.Box.SelectionEnd = 0;

        shell.Picker.InsertSymbol("∫");
        Dispatcher.UIThread.RunJobs();

        Assert.True(shell.Box.IsFocused);
        Assert.Equal("∫x dx", shell.Box.Text);
        Assert.Equal(1, shell.Box.CaretIndex);
    }

    [Fact]
    public void RememberedSelection_SurvivesTheEditorLosingFocus()
    {
        using var shell = Open("speed <= limit");
        shell.Box.Focus();
        Dispatcher.UIThread.RunJobs();
        shell.Box.SelectionStart = 6;
        shell.Box.SelectionEnd = 8;

        shell.OtherBox.Focus(); // e.g. focus moving while the picker opens
        Dispatcher.UIThread.RunJobs();

        shell.Picker.InsertSymbol("≤");

        Assert.Equal("speed ≤ limit", shell.Box.Text);
        Assert.Equal("Operational note", shell.OtherBox.Text);
    }

    [Fact]
    public void Preview_UpdatesAfterTheRealDelay_WithTheLatestTextOnly()
    {
        using var shell = Open(string.Empty);
        shell.Preview.Source = @"\(a";
        shell.Preview.Source = @"\(a+b\)";
        Assert.False(shell.Preview.IsVisible);

        WaitForPreview();

        Assert.True(shell.Preview.IsVisible);
        Assert.Equal(@"\(a+b\)", shell.Preview.RenderedText);
    }

    [Fact]
    public void NavigatingAway_CancelsPendingPreviewWork_AndReturningShowsTheCurrentText()
    {
        using var shell = Open(string.Empty);
        shell.Preview.Source = @"\(x^2\)";
        Assert.True(shell.Preview.HasPendingUpdate);

        // The shell navigates by hiding sections; detaching the editor's subtree is the stronger case.
        var stack = (StackPanel)shell.Section.Child!;
        stack.Children.Remove(shell.Preview);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Preview.HasPendingUpdate);

        WaitForPreview();
        Assert.Null(shell.Preview.RenderedText); // no late callback rendered into a detached preview

        stack.Children.Add(shell.Preview);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"\(x^2\)", shell.Preview.RenderedText);
    }
}
