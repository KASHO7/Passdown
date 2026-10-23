using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 29: the Morning Message composer as the Associate screen drives it — opening on the current date's
/// on-screen state, message-only editing that never touches the draft, ordering, copy modes, OD Notes
/// separation, clipboard failures, local preference persistence, and the existing Copy actions' formatted mode.
/// Message text itself is covered by Documents.Tests' MorningMessageComposerTests.
/// </summary>
public class MorningMessageViewModelTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static AssociateProduct Product(string suffix, string name, SourceCategory category, decimal price) => new(
        AssociateProductIdentity.ForDatedProduct(category, suffix), name, category, IsCustom: false, price,
        OfficialDisplayPrice: null, ComparisonPriceAmount: null, PriceQualifier: null, DateSpecific: true, Today,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, null), IncludeOnPdf: true, DisplayPriceOverride: null);

    private sealed record Harness(
        AssociateViewModel ViewModel,
        FakeAssociatePassdownService Service,
        FakeClipboardService Clipboard,
        InMemoryMorningMessagePreferenceStore Preferences,
        InMemoryMorningMessageContentStore Content)
    {
        public MorningMessageViewModel Composer => ViewModel.MorningMessage;
    }

    private static async Task<Harness> CreateAsync(
        InMemoryMorningMessagePreferenceStore? preferences = null,
        InMemoryMorningMessageContentStore? content = null,
        IReadOnlyList<string>? draftNotes = null)
    {
        var service = new FakeAssociatePassdownService { OfficialHours = (new TimeOnly(11, 0), new TimeOnly(20, 0)) };
        service.WorkspaceFactory = date =>
        {
            var draft = service.StoredDraft(date) ?? AssociateDraftPayload.CreateDefault() with
            {
                ProjectedAttendance = 18000,
                DailyFeatureType = DailyFeatureType.QuoteOfTheDay,
                DailyFeatureText = "Keep calm and coaster on.",
                OperationalNotes = draftNotes ?? [],
            };
            return FakeAssociatePassdownService.DefaultWorkspace(date) with
            {
                Draft = draft,
                ParkHours = new AssociateParkHoursView(
                    ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(20, 0), false, null, null, false, null, null),
                    new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, null), false, new TimeOnly(11, 0), new TimeOnly(20, 0)),
                Products = [Product("reg", "Park Ticket", SourceCategory.DatedAdmissionTickets, 59m)],
            };
        };

        var clipboard = new FakeClipboardService();
        var scheduler = new ManualDebounceScheduler();
        preferences ??= new InMemoryMorningMessagePreferenceStore();
        content ??= new InMemoryMorningMessageContentStore();
        var viewModel = new AssociateViewModel(
            service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay, clipboardService: clipboard,
            morningMessagePreferenceStore: preferences, morningMessageContentStore: content);
        await viewModel.PendingLoad;
        return new Harness(viewModel, service, clipboard, preferences, content);
    }

    private static async Task<Harness> OpenAsync(Harness? harness = null)
    {
        harness ??= await CreateAsync();
        await harness.ViewModel.OpenMorningMessageCommand.ExecuteAsync(null);
        return harness;
    }

    [Fact]
    public async Task Open_ShowsTheCurrentDate_DefaultOrder_GreetingFirst_DailyFeatureLast()
    {
        var h = await OpenAsync();

        Assert.True(h.Composer.IsOpen);
        Assert.Equal("Friday, August 21, 2026", h.Composer.DateText);
        Assert.Equal(
            ["Greeting", "Today's Operations", "DAP / Placement", "Reminders", "Ticket Prices", "Tentative Break Plan", "Daily Feature"],
            h.Composer.Sections.Select(row => row.Name));
        Assert.StartsWith("Good Morning!\r\n\r\nToday's Operations:\r\nPark Hours: 11:00 AM - 8:00 PM", h.Composer.PreviewText, StringComparison.Ordinal);
        Assert.EndsWith("Quote of the Day:\r\nKeep calm and coaster on.", h.Composer.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_ReadsUnsavedOnScreenEdits_WithoutSavingOrCheckingOnline()
    {
        var h = await CreateAsync();
        var loads = h.Service.LoadCalls.Count;
        h.ViewModel.PreSoldsText = "1,250"; // never autosaved — the debounce never elapses

        await OpenAsync(h);

        Assert.Contains("Pre-solds: 1,250", h.Composer.PreviewText);
        Assert.Empty(h.Service.SaveCalls);
        Assert.Empty(h.Service.CheckOnlineCalls);
        Assert.Equal(loads, h.Service.LoadCalls.Count);
        Assert.Equal(Today, h.ViewModel.SelectedDate);
    }

    [Fact]
    public async Task ComposerEdits_AreMessageOnly_NeverChangeTheDraft()
    {
        var h = await OpenAsync();
        var draftBefore = h.ViewModel.Workspace!.Draft;

        h.Composer.Reminders = "- Hydrate\n- Smile";
        h.Composer.BreakPlan = "Avery   10:30";
        h.Composer.OdNotes = "Ride 4 down";
        h.Composer.Greeting = "Gooooood Morning!";
        await h.Composer.CloseCommand.ExecuteAsync(null);

        Assert.Empty(h.Service.SaveCalls);
        Assert.Same(draftBefore, h.ViewModel.Workspace!.Draft);
        Assert.False(h.Composer.IsOpen);
    }

    [Fact]
    public async Task CopyPlainText_CopiesExactlyThePreview_NoHtml()
    {
        var h = await OpenAsync();
        h.Composer.Reminders = "Hydrate";

        await h.Composer.CopyPlainTextCommand.ExecuteAsync(null);

        Assert.Equal(h.Composer.PreviewText, Assert.Single(h.Clipboard.Texts));
        Assert.Null(Assert.Single(h.Clipboard.Htmls));
        Assert.Equal("Morning message copied (plain text)", h.Composer.StatusMessage);
        Assert.False(h.Composer.StatusIsError);
    }

    [Fact]
    public async Task CopyFormatted_PutsHtmlAndTheSamePlainText()
    {
        var h = await OpenAsync();
        h.Composer.Reminders = "<b>Hydrate</b> & smile";

        await h.Composer.CopyFormattedCommand.ExecuteAsync(null);

        Assert.Equal(h.Composer.PreviewText, Assert.Single(h.Clipboard.Texts));
        var html = Assert.Single(h.Clipboard.Htmls)!;
        Assert.Contains("<p><b style=\"font-weight:bold\">Reminders:</b><br>&lt;b&gt;Hydrate&lt;/b&gt; &amp; smile</p>", html);
        Assert.Equal("Morning message copied (formatted)", h.Composer.StatusMessage);
    }

    [Fact]
    public async Task FormattedCopyFailure_FallsBackToPlainText_AndSaysSo()
    {
        var h = await OpenAsync();
        h.Clipboard.FailFormattedWith = new InvalidOperationException("HTML format rejected");

        await h.Composer.CopyFormattedCommand.ExecuteAsync(null);

        Assert.Equal(h.Composer.PreviewText, Assert.Single(h.Clipboard.Texts));
        Assert.Null(Assert.Single(h.Clipboard.Htmls));
        Assert.Contains("as plain text", h.Composer.StatusMessage, StringComparison.Ordinal);
        Assert.False(h.Composer.StatusIsError);
    }

    [Fact]
    public async Task ClipboardFailure_ShowsShortError_NeverThrows()
    {
        var h = await OpenAsync();
        h.Clipboard.FailWith = new InvalidOperationException("Clipboard locked");

        await h.Composer.CopyFormattedCommand.ExecuteAsync(null);
        Assert.Equal("Couldn't copy to the clipboard. Try again.", h.Composer.StatusMessage);
        Assert.True(h.Composer.StatusIsError);

        await h.Composer.CopyPlainTextCommand.ExecuteAsync(null);
        Assert.True(h.Composer.StatusIsError);
        Assert.Empty(h.Clipboard.Texts);
    }

    [Fact]
    public async Task OdNotes_NotInMainMessage_CopiedSeparately_FromComposerAndMenu()
    {
        var h = await OpenAsync();
        h.Composer.OdNotes = "Ride 4 down until noon.";

        Assert.DoesNotContain("Ride 4 down", h.Composer.PreviewText);
        Assert.Equal("OD Notes:\r\nRide 4 down until noon.", h.Composer.OdNotesPreviewText);

        await h.Composer.CopyOdNotesPlainTextCommand.ExecuteAsync(null);
        Assert.Equal("OD Notes:\r\nRide 4 down until noon.", Assert.Single(h.Clipboard.Texts));

        await h.Composer.CloseCommand.ExecuteAsync(null);
        await h.ViewModel.CopyOdNotesCommand.ExecuteAsync(null);
        Assert.Equal("OD Notes:\r\nRide 4 down until noon.", h.Clipboard.Texts[^1]);
        Assert.Equal("OD Notes copied", h.ViewModel.CopyMessage);
    }

    [Fact]
    public async Task MenuOdNotes_WhenNoneEntered_SaysSo_CopiesNothing()
    {
        var h = await CreateAsync();

        await h.ViewModel.CopyOdNotesCommand.ExecuteAsync(null);

        Assert.Empty(h.Clipboard.Texts);
        Assert.Equal("No OD Notes for this date — add them in Morning Message.", h.ViewModel.CopyMessage);
    }

    [Fact]
    public async Task MoveUpDown_ReordersRowsAndMessage_AndPersistsOrder()
    {
        var h = await OpenAsync();
        h.Composer.BreakPlan = "Avery 10:30";
        h.Composer.Reminders = "Hydrate";

        var breakPlan = h.Composer.Sections.Single(row => row.Id == MorningMessageSectionId.BreakPlan);
        breakPlan.MoveUpCommand.Execute(null);
        breakPlan.MoveUpCommand.Execute(null);

        Assert.Equal(4, breakPlan.Position);
        Assert.True(h.Composer.PreviewText.IndexOf("Tentative Break Plan:", StringComparison.Ordinal) < h.Composer.PreviewText.IndexOf("Reminders:", StringComparison.Ordinal));
        Assert.False(h.Composer.Sections[0].CanMoveUp);
        Assert.False(h.Composer.Sections[^1].CanMoveDown);

        await h.Composer.FlushSaveAsync();
        Assert.Equal(MorningMessageSectionId.BreakPlan, h.Preferences.Current.Layout.Sections[3].Id);
    }

    [Fact]
    public async Task DisablingSection_RemovesItFromTheMessage_RowStaysWithPreview()
    {
        var h = await OpenAsync();
        var prices = h.Composer.Sections.Single(row => row.Id == MorningMessageSectionId.TicketPrices);

        prices.IsEnabled = false;

        Assert.DoesNotContain("Ticket Prices", h.Composer.PreviewText);
        Assert.Contains("Park Ticket: $59.00", prices.PreviewText);
        Assert.Contains("not included", prices.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Greeting_IsRemembered_AcrossRestarts_WithoutRetyping()
    {
        var preferences = new InMemoryMorningMessagePreferenceStore();
        var first = await OpenAsync(await CreateAsync(preferences));
        first.Composer.Greeting = "Gooooood Morning!";
        await first.Composer.CloseCommand.ExecuteAsync(null);

        var second = await OpenAsync(await CreateAsync(preferences));

        Assert.Equal("Gooooood Morning!", second.Composer.Greeting);
        Assert.StartsWith("Gooooood Morning!", second.Composer.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageText_IsKeptPerDate_OnThisComputer()
    {
        var content = new InMemoryMorningMessageContentStore();
        var first = await OpenAsync(await CreateAsync(content: content));
        first.Composer.Reminders = "Radio check 9:45";
        first.Composer.BreakPlan = "Avery   10:30";
        await first.Composer.CloseCommand.ExecuteAsync(null);

        var second = await OpenAsync(await CreateAsync(content: content));

        Assert.Equal("Radio check 9:45", second.Composer.Reminders);
        Assert.Equal("Avery   10:30", second.Composer.BreakPlan);
    }

    [Fact]
    public async Task PassdownNotes_AreOfferedButNeverAddedUntilChosen()
    {
        var h = await OpenAsync(await CreateAsync(draftNotes: ["Lot B closed for paving"]));

        Assert.DoesNotContain("Lot B", h.Composer.PreviewText);
        var note = Assert.Single(h.Composer.NoteOptions);

        h.Composer.SelectedNoteOption = note;
        h.Composer.AddNoteToRemindersCommand.Execute(null);

        Assert.Equal("Lot B closed for paving", h.Composer.Reminders);
        Assert.Contains("Reminders:\r\nLot B closed for paving", h.Composer.PreviewText);
    }

    [Fact]
    public async Task ReuseLastReminders_CopiesTheMostRecentEarlierDate_OnlyWhenAsked()
    {
        var content = new InMemoryMorningMessageContentStore();
        await content.SaveAsync(Today.AddDays(-1), MorningMessageContent.Empty with { Reminders = "Yesterday's reminder" });
        var h = await OpenAsync(await CreateAsync(content: content));

        Assert.Equal(string.Empty, h.Composer.Reminders);
        await h.Composer.ReuseLastRemindersCommand.ExecuteAsync(null);

        Assert.Equal("Yesterday's reminder", h.Composer.Reminders);
    }

    [Fact]
    public async Task DapImageReminder_ShownAfterCopy_ImageNeverInText()
    {
        var h = await OpenAsync();
        h.Composer.DapPlacement = "Rotate at 2 PM";
        h.Composer.DapImageReminder = true;

        await h.Composer.CopyPlainTextCommand.ExecuteAsync(null);

        Assert.Contains("DAP / Placement:\r\nRotate at 2 PM", Assert.Single(h.Clipboard.Texts));
        Assert.NotNull(h.Composer.AfterCopyReminder);
        Assert.Empty(h.Clipboard.ImageFiles);
    }

    [Fact]
    public async Task CopyImage_MissingFile_IsReported_NothingCopied()
    {
        var h = await OpenAsync();
        h.Composer.DapImagePath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");

        await h.Composer.CopyDapImageCommand.ExecuteAsync(null);

        Assert.True(h.Composer.StatusIsError);
        Assert.Empty(h.Clipboard.ImageFiles);
    }

    [Fact]
    public async Task ExistingCopyActions_FormattedDefault_AddHtml_PlainTextUnchanged()
    {
        var preferences = new InMemoryMorningMessagePreferenceStore(MorningMessagePreferences.Default with { DefaultCopyMode = TeamsCopyMode.Formatted });
        var h = await CreateAsync(preferences);

        await h.ViewModel.CopyForTeamsCommand.ExecuteAsync(AssociateTeamsCopyKind.TodaysPricing);

        Assert.Equal("Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00", Assert.Single(h.Clipboard.Texts));
        Assert.Equal("<p><b style=\"font-weight:bold\">Ticket Prices:</b></p><p><b style=\"font-weight:bold\">Daily Admission</b><br>Park Ticket: $59.00</p>", Assert.Single(h.Clipboard.Htmls));
        Assert.Equal("Today's pricing copied", h.ViewModel.CopyMessage);
    }

    [Fact]
    public async Task ExistingCopyActions_PlainDefault_StayPlainTextOnly()
    {
        var h = await CreateAsync();

        foreach (var kind in Enum.GetValues<AssociateTeamsCopyKind>())
        {
            await h.ViewModel.CopyForTeamsCommand.ExecuteAsync(kind);
        }

        Assert.Equal(3, h.Clipboard.Texts.Count);
        Assert.All(h.Clipboard.Htmls, Assert.Null);
    }

    // ---------------------------------------------------------------- view: keyboard, accessibility, themes

    private static string Markup(string file) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{file}"));

    [Fact]
    public void View_EscapeCloses_GreetingIsTheInitialFocusTarget()
    {
        var view = new MorningMessageView();

        var binding = Assert.Single(view.KeyBindings);
        Assert.Equal(new KeyGesture(Key.Escape), binding.Gesture);
        Assert.Contains(view.GetLogicalDescendants().OfType<Avalonia.Controls.TextBox>(), box => box.Name == "GreetingBox");
        Assert.Contains("Command=\"{Binding CloseCommand}\"", Markup("MorningMessageView.axaml"));
    }

    [Fact]
    public void View_ReorderingUsesNamedMoveUpMoveDownButtons_NotDragOnly()
    {
        var markup = Markup("MorningMessageView.axaml");

        Assert.Contains("Command=\"{Binding MoveUpCommand}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding MoveUpAutomationName}\"", markup);
        Assert.Contains("Command=\"{Binding MoveDownCommand}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding MoveDownAutomationName}\"", markup);
        Assert.DoesNotContain("DragDrop", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void View_EveryButtonCheckBoxAndTextBox_HasAScreenReaderName()
    {
        var markup = Regex.Replace(Markup("MorningMessageView.axaml"), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        var controls = Regex.Matches(markup, @"<(Button|CheckBox|TextBox|ComboBox)\b[^>]*>", RegexOptions.Singleline);

        Assert.NotEmpty(controls);
        Assert.All(controls, control => Assert.Contains("AutomationProperties.Name=", control.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void View_UsesOnlyThemeTokens_DefinedForLightAndDark()
    {
        var markup = Markup("MorningMessageView.axaml");
        Assert.DoesNotMatch("(Background|Foreground|BorderBrush)=\"#", markup);

        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));
        var light = tokens[tokens.IndexOf("x:Key=\"Light\"", StringComparison.Ordinal)..tokens.IndexOf("x:Key=\"Dark\"", StringComparison.Ordinal)];
        var dark = tokens[tokens.IndexOf("x:Key=\"Dark\"", StringComparison.Ordinal)..];

        var used = Regex.Matches(markup, @"\{DynamicResource (\w+)\}").Select(match => match.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, key =>
        {
            Assert.Contains($"x:Key=\"{key}\"", light, StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"{key}\"", dark, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MainWindow_HostsTheComposerAsAnOverlay_NotANewNavigationSection()
    {
        var markup = Markup("MainWindow.axaml");

        Assert.Contains("<views:MorningMessageView DataContext=\"{Binding MorningMessage}\" />", markup);
        Assert.Contains("IsVisible=\"{Binding IsMorningMessageOpen}\"", markup);
        Assert.DoesNotContain("MorningMessageSection", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void View_PreviewSaysMentionsDoNotTagAnyone()
    {
        Assert.Contains("does not tag or notify anyone", MorningMessageViewModel.MentionHint, StringComparison.Ordinal);
        Assert.Contains("MorningMessageViewModel.MentionHint", Markup("MorningMessageView.axaml"), StringComparison.Ordinal);
    }

    // ---- Phase 29B: heading, styled preview, clipboard format order ----

    [Fact]
    public async Task Phase29B_PreviewSpans_FollowTheCopyMode_SameWordsAsPreviewText()
    {
        var h = await OpenAsync();
        h.Composer.Reminders = "- Hydrate\n- Smile";

        h.Composer.UseFormattedByDefault = true;
        var formatted = h.Composer.PreviewSpans;
        Assert.Equal(h.Composer.PreviewText.Replace("\r\n", "\n", StringComparison.Ordinal), string.Concat(formatted.Select(span => span.Text)));
        Assert.Contains(formatted, span => span.Bold && span.Text == "Today's Operations:");
        Assert.Contains(formatted, span => span.Bold && span.Text == "Reminders:");
        Assert.Contains(formatted, span => span.Bold && span.Text == "Park Hours:");
        Assert.Contains("• Hydrate\n• Smile", string.Concat(formatted.Select(span => span.Text)), StringComparison.Ordinal);

        h.Composer.UseFormattedByDefault = false;
        var plain = h.Composer.PreviewSpans;
        Assert.Equal(h.Composer.PreviewText.Replace("\r\n", "\n", StringComparison.Ordinal), Assert.Single(plain).Text);
        Assert.False(plain[0].Bold || plain[0].Italic);
    }

    [Fact]
    public async Task Phase29B_PreviewSpans_NeverIncludeOdNotes()
    {
        var h = await OpenAsync();
        h.Composer.OdNotes = "SECRET OD note";
        h.Composer.UseFormattedByDefault = true;

        Assert.DoesNotContain("SECRET OD note", string.Concat(h.Composer.PreviewSpans.Select(span => span.Text)), StringComparison.Ordinal);
        Assert.DoesNotContain("OD Notes:", string.Concat(h.Composer.PreviewSpans.Select(span => span.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Phase29B_OpenDateNotToday_StillTodaysOperations_InPreviewAndBothCopies()
    {
        var h = await CreateAsync();
        h.ViewModel.SelectedDate = Today.AddDays(1);
        await h.ViewModel.PendingLoad;
        await h.ViewModel.OpenMorningMessageCommand.ExecuteAsync(null);

        Assert.Equal("Saturday, August 22, 2026", h.Composer.DateText);

        Assert.Contains("\r\nToday's Operations:\r\n", h.Composer.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations for", h.Composer.PreviewText, StringComparison.Ordinal);

        await h.Composer.CopyFormattedCommand.ExecuteAsync(null);
        var html = Assert.Single(h.Clipboard.Htmls)!;
        Assert.Contains("<b style=\"font-weight:bold\">Today&#39;s Operations:</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations for", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations for", Assert.Single(h.Clipboard.Texts), StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_PreviewInlines_BoldAndItalicRuns_LineBreaks_NoMarkup()
    {
        var inlines = Desktop.Converters.TeamsPreviewInlinesConverter.ToInlines(
        [
            new("Reminders:", Bold: true),
            new("\n• Hydrate\n\n"),
            new("note", Italic: true),
        ]);

        var runs = inlines.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Equal(["Reminders:", "• Hydrate", "note"], runs.Select(run => run.Text));
        Assert.Equal(Avalonia.Media.FontWeight.Bold, runs[0].FontWeight);
        Assert.Equal(Avalonia.Media.FontWeight.Normal, runs[1].FontWeight);
        Assert.Equal(Avalonia.Media.FontStyle.Italic, runs[2].FontStyle);
        Assert.Equal(3, inlines.OfType<Avalonia.Controls.Documents.LineBreak>().Count());
        Assert.All(runs, run => Assert.False(run.IsSet(Avalonia.Controls.Documents.TextElement.ForegroundProperty))); // theme color in light and dark
    }

    [Fact]
    public void Phase29B_View_PreviewBindsStyledInlines_NotRawText()
    {
        var markup = Markup("MorningMessageView.axaml");

        Assert.Contains("Inlines=\"{Binding PreviewSpans, Converter={x:Static conv:TeamsPreviewInlinesConverter.Instance}}\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Morning message preview\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_FormattedClipboardData_ListsHtmlBeforePlainText()
    {
        var data = Desktop.Services.AvaloniaClipboardService.CreateFormattedData("Plain", "<p>Plain</p>");

        var item = Assert.Single(data.Items);
        Assert.Equal(2, item.Formats.Count);
        Assert.Equal(Desktop.Services.AvaloniaClipboardService.HtmlFormat(), item.Formats[0]);
        Assert.Equal(DataFormat.Text, item.Formats[1]);
        Assert.Equal("Plain", item.TryGetValue(DataFormat.Text));
        Assert.Equal(Desktop.Services.AvaloniaClipboardService.HtmlPayload("<p>Plain</p>"), item.TryGetValue(Desktop.Services.AvaloniaClipboardService.HtmlFormat()));
    }
}
