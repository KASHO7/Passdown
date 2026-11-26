using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35D (docs/phase35d-shows-notes-shared-polish.md): Shows &amp; Entertainment and Passdown Notes view-model and
/// markup contracts, the shared dropdown chevron and the title-bar tokens. View-model and markup only (mixed partition);
/// the real-window checks are in <c>Phase35DShowsNotesWindowTests</c> (a partial of the run-alone preview class).
/// </summary>
public class Phase35DShowsNotesPolishTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero); // 11:00 AM ET
    private static readonly DateOnly Today = new(2026, 8, 21);

    private static TimeOnly T(string time) => TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture);

    internal static readonly string[] LongTimes =
        ["11:15", "11:30", "11:45", "12:15", "12:30", "12:45", "14:00", "14:15", "14:30", "15:00", "15:30", "16:00", "16:30", "17:00", "17:30", "17:45"];

    private static readonly ScheduledShow MeetAndGreet = new("SHOW-MEET", "Character Meet and Greet", "Gathering Spot", [.. LongTimes.Select(T)]);
    private static readonly ScheduledShow Kickoff = new("SHOW-KICK", "Keg Kickoff", null, [T("11:45")]);
    private static readonly ScheduledShow Contest = new("SHOW-CONTEST", "Eating Contest", "Harmony Hall Midway", [T("12:45"), T("16:15")]);
    private static readonly ScheduledShow FiveTimes = new("SHOW-FIVE", "Five Show", "Plaza", [T("10:00"), T("11:00"), T("12:00"), T("13:00"), T("14:00")]);

    private static ShowScheduleView Scheduled(DateOnly date, params ScheduledShow[] shows) =>
        new(date, ShowScheduleState.Scheduled, shows, false, FixedNow.AddHours(-1), FixedNow.AddHours(-1));

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Autosave) Create(Action<FakeAssociatePassdownService>? configure = null)
    {
        var service = new FakeAssociatePassdownService();
        service.ShowSchedules[Today] = Scheduled(Today, MeetAndGreet, Kickoff, Contest, FiveTimes);
        configure?.Invoke(service);
        var autosave = new ManualDebounceScheduler();
        return (new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: autosave.Delay), service, autosave);
    }

    private static string Markup(string view = "AssociateView") => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));

    private static string Tokens => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    private static string Styles => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));

    private static string Element(string markup, string name)
    {
        var start = markup.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} not found");
        var open = markup.LastIndexOf('<', start);
        var tag = Regex.Match(markup[open..], "^<([\\w:.]+)").Groups[1].Value;
        var depth = 0;
        foreach (Match m in Regex.Matches(markup[open..], $"<{Regex.Escape(tag)}[\\s>]|</{Regex.Escape(tag)}>"))
        {
            depth += m.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0)
            {
                return markup.Substring(open, m.Index + m.Length);
            }
        }

        throw new InvalidOperationException($"{name} has no closing tag");
    }

    // ---- Shows: real data, selection, long schedules -------------------------------------------------------------

    [Fact]
    public async Task Shows_RenderTheRealSchedule_EveryTimeKept_InOperatingDayOrder()
    {
        var (viewModel, _, _) = Create();
        await viewModel.PendingLoad;

        Assert.Equal(["Character Meet and Greet", "Keg Kickoff", "Eating Contest", "Five Show"], viewModel.ShowRows.Select(r => r.Name));
        var meet = viewModel.ShowRows[0];
        Assert.Equal(16, meet.AllTimes.Count);
        Assert.Equal("11:15 AM", meet.AllTimes[0]);
        Assert.Equal("5:45 PM", meet.AllTimes[^1]);
        Assert.Equal("Gathering Spot", meet.VenueText);
        Assert.Equal("Location not listed", viewModel.ShowRows[1].VenueText); // the feed gave no venue — said plainly, never guessed
        Assert.Equal("Performance times for Character Meet and Greet: " + string.Join(", ", meet.AllTimes), meet.TimesAccessibleName);
    }

    [Fact]
    public async Task Shows_LongSchedule_PreviewsThreeTimes_AndCountsTheRest_ShortListsShowEverything()
    {
        var (viewModel, _, _) = Create();
        await viewModel.PendingLoad;

        var meet = viewModel.ShowRows[0];
        Assert.True(meet.CanExpand);
        Assert.Equal("11:15 AM · 11:30 AM · 11:45 AM", meet.TimesPreviewText);
        Assert.Equal(" · +13 more", meet.MoreTimesText);
        Assert.Equal("All 16 scheduled performances", meet.AllTimesHeading);
        Assert.Equal("View times", meet.ToggleText);
        Assert.Equal("Show all 16 performance times for Character Meet and Greet", meet.ToggleAccessibleName);

        // Five or fewer times: every time shown, no control (the shared ShowTimesDisclosure threshold).
        var five = viewModel.ShowRows.Single(r => r.Name == "Five Show");
        Assert.False(five.CanExpand);
        Assert.Equal(5, five.TimesPreviewText.Split(" · ").Length);
        Assert.Equal(string.Empty, five.MoreTimesText);
        Assert.Equal("12:45 PM · 4:15 PM", viewModel.ShowRows.Single(r => r.Name == "Eating Contest").TimesPreviewText);
    }

    [Fact]
    public async Task Shows_ExpandingNeverChangesSelection_NeverSaves_NeverLoads()
    {
        var (viewModel, service, autosave) = Create();
        await viewModel.PendingLoad;
        var loads = service.LoadCalls.Count;
        var scheduled = autosave.ScheduleCount;
        var meet = viewModel.ShowRows[0];

        for (var i = 0; i < 20; i++)
        {
            meet.ToggleExpandedCommand.Execute(null);
        }

        meet.ToggleExpandedCommand.Execute(null);
        Assert.True(meet.IsExpanded);
        Assert.Equal("Less", meet.ToggleText);
        Assert.False(meet.IsSelected);
        Assert.Equal(scheduled, autosave.ScheduleCount);
        Assert.Empty(service.SaveCalls);
        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Empty(service.CheckOnlineCalls);

        // A short list cannot expand at all.
        var kickoff = viewModel.ShowRows[1];
        kickoff.IsExpanded = true;
        Assert.False(kickoff.IsExpanded);
    }

    [Fact]
    public async Task Shows_Expansion_SurvivesARebuildOfTheSameDate_AndResetsOnAnotherDate()
    {
        var note = new OperationalNote(Guid.NewGuid(), "Scanner down", null, OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
            Today, null, OperationalNoteStatus.Active, FixedNow.AddDays(-1), FixedNow.AddDays(-1), null);
        var (viewModel, service, _) = Create(s =>
        {
            s.RelevantOperationalNotesByDate[Today] = [note];
            s.ShowSchedules[Today.AddDays(1)] = Scheduled(Today.AddDays(1), MeetAndGreet);
        });
        await viewModel.PendingLoad;
        viewModel.ShowRows[0].IsExpanded = true;

        // Adding a persistent note rebuilds every row from the returned workspace — the open list stays open.
        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);
        Assert.True(viewModel.ShowRows.Single(r => r.Key == "SHOW-MEET").IsExpanded);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        Assert.False(Assert.Single(viewModel.ShowRows).IsExpanded);
        Assert.Contains(Today.AddDays(1), service.LoadCalls);
    }

    [Fact]
    public async Task Shows_SelectionSummary_CountsTheRows_AndFollowsSelectAllAndClear()
    {
        var (viewModel, service, autosave) = Create();
        await viewModel.PendingLoad;
        Assert.Equal("0 of 4 shows selected", viewModel.ShowsSelectionSummary);

        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        viewModel.ShowRows[2].IsSelected = true;
        Assert.Equal("1 of 4 shows selected", viewModel.ShowsSelectionSummary);
        Assert.Contains(nameof(AssociateViewModel.ShowsSelectionSummary), changes);

        viewModel.SelectAllShowsCommand.Execute(null);
        Assert.Equal("4 of 4 shows selected", viewModel.ShowsSelectionSummary);
        autosave.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Equal(4, service.SaveCalls.Last().Draft.Shows!.SelectedShows.Count);

        viewModel.ClearShowsCommand.Execute(null);
        Assert.Equal("0 of 4 shows selected", viewModel.ShowsSelectionSummary);
    }

    [Fact]
    public async Task Shows_Subtitle_IsTheDateAndTheRealFreshness_AndStatesStayDistinct()
    {
        var (viewModel, _, _) = Create(s =>
        {
            s.ShowSchedules[Today.AddDays(1)] = new ShowScheduleView(Today.AddDays(1), ShowScheduleState.NotPublished, [], false, null, null);
            s.ShowSchedules[Today.AddDays(2)] = new ShowScheduleView(Today.AddDays(2), ShowScheduleState.CouldNotCheck, [], false, null, FixedNow.AddHours(-1));
        });
        await viewModel.PendingLoad;
        Assert.Equal("Friday, August 21, 2026 · Official schedule checked 10:00 AM ET", viewModel.ShowsSubtitleText);

        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        Assert.False(viewModel.HasShowRows);
        Assert.Contains("not published", viewModel.ShowsStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Saturday, August 22, 2026", viewModel.ShowsSubtitleText, StringComparison.Ordinal);

        viewModel.SelectedDate = Today.AddDays(2);
        await viewModel.PendingLoad;
        Assert.Contains("Couldn't check", viewModel.ShowsStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("No shows", viewModel.ShowsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_RowStateNotes_ArePreserved_ForASelectedShowNoLongerListed()
    {
        var (viewModel, service, autosave) = Create();
        await viewModel.PendingLoad;
        viewModel.IncludeShows = true;
        viewModel.ShowRows.Single(r => r.Name == "Keg Kickoff").IsSelected = true;
        autosave.ElapseLatest();
        await viewModel.PendingAutosave;

        service.ShowSchedules[Today] = Scheduled(Today, MeetAndGreet);
        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;

        var gone = viewModel.ShowRows.Single(r => r.Name == "Keg Kickoff");
        Assert.True(gone.IsSelected);
        Assert.Equal("No longer on the official schedule for this date — won't print.", gone.StateNote);
    }

    [Fact]
    public void ShowsMarkup_IsOneSheet_WithHeadingCountColumnsAndExpansion_AndNoAnimation()
    {
        var shows = Element(Markup(), "ShowsSectionScroll");

        Assert.Contains("x:Name=\"ShowsPanel\" Classes=\"editorSheet\"", shows);
        Assert.Contains("Text=\"Shows &amp; Entertainment\"", shows);
        Assert.Contains("Text=\"{Binding ShowsSubtitleText}\"", shows);
        Assert.Contains("Text=\"{Binding ShowsSelectionSummary}\"", shows);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", shows);
        Assert.Contains("Text=\"Show and location\"", shows);
        Assert.Contains("Text=\"Scheduled performances\"", shows);
        Assert.Contains("Text=\"{Binding ShowsStatusText}\"", shows);
        Assert.Contains("AutomationProperties.Name=\"{Binding ToggleAccessibleName}\"", shows);
        Assert.Contains("IsVisible=\"{Binding CanExpand}\"", shows);
        Assert.Contains("<Run Text=\"{Binding TimesPreviewText, Mode=OneWay}\" />", shows); // Run.Text is TwoWay by default
        Assert.DoesNotContain("KeyFrame", shows);
        Assert.DoesNotContain("Transitions", shows);
        Assert.DoesNotContain("Animation", shows);
    }

    // ---- Notes: daily text and persistent notes ---------------------------------------------------------------------

    private static OperationalNote Note(string title, OperationalNotePriority priority = OperationalNotePriority.Normal) => new(
        Guid.NewGuid(), title, "Body text.", OperationalNoteCategory.Equipment, priority,
        Today, null, OperationalNoteStatus.Active, FixedNow.AddDays(-1), FixedNow.AddDays(-1), null);

    [Fact]
    public async Task DailyNotes_CountNonEmptyLines_LikeTheDraftAndThePdf()
    {
        var (viewModel, service, autosave) = Create();
        await viewModel.PendingLoad;

        viewModel.OperationalNotesText = "First note\r\n\r\n   \nSecond note  \n  Third";
        Assert.Equal(3, viewModel.DailyNoteLineCount);
        Assert.Equal("3 notes", viewModel.DailyNoteLineCountText);
        Assert.Equal("3 daily notes · 0 persistent included", viewModel.NotesSummaryText);

        autosave.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Equal(["First note", "Second note", "Third"], service.SaveCalls.Last().Draft.OperationalNotes); // unchanged serialization

        viewModel.OperationalNotesText = "Only";
        Assert.Equal("1 note", viewModel.DailyNoteLineCountText);
        Assert.Equal("1 daily note · 0 persistent included", viewModel.NotesSummaryText);
        viewModel.OperationalNotesText = "";
        Assert.Equal("0 notes", viewModel.DailyNoteLineCountText);
    }

    [Fact]
    public async Task PersistentNotes_CheckboxAdds_UncheckRemoves_ThroughTheExistingSelection_Only()
    {
        var note = Note("Scanner down");
        var (viewModel, service, _) = Create(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;
        var available = Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
        Assert.False(available.IsIncluded);
        Assert.StartsWith("Equipment · Normal priority", available.MetaText, StringComparison.Ordinal);

        available.IsIncluded = true; // the checkbox's two-way binding, then its command
        await available.AddCommand.ExecuteAsync(null);

        var included = Assert.Single(viewModel.SelectedOperationalNotes);
        Assert.True(included.IsIncluded);
        Assert.Equal("Included in this Passdown (1)", viewModel.IncludedPersistentNotesHeading);
        Assert.Equal("0 daily notes · 1 persistent included", viewModel.NotesSummaryText);
        Assert.Equal([(Today, note.Id)], service.SelectOperationalNoteCalls);

        included.IsIncluded = false;
        await included.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedOperationalNotes);
        Assert.Equal([(Today, note.Id)], service.DeselectOperationalNoteCalls);
        // The source note itself is never touched — it is still exactly what the notes store offered.
        Assert.Same(note, Assert.Single(service.RelevantOperationalNotesByDate[Today]));
        Assert.Equal(OperationalNoteStatus.Active, note.Status);
        Assert.Empty(service.ReplaceOperationalNoteCalls);
    }

    [Fact]
    public async Task PersistentNotes_ABlockedAdd_ReturnsTheCheckboxToUnchecked()
    {
        var note = Note("Scanner down");
        var (viewModel, service, autosave) = Create(s => s.RelevantOperationalNotesByDate[Today] = [note]);
        await viewModel.PendingLoad;
        service.SaveException = new IOException("disk I/O error");
        viewModel.ProjectedAttendanceText = "38,500";
        autosave.ElapseLatest();
        await viewModel.PendingAutosave;

        var available = viewModel.AvailableOperationalNoteSuggestions.Single();
        available.IsIncluded = true;
        await available.AddCommand.ExecuteAsync(null);

        Assert.Empty(service.SelectOperationalNoteCalls);
        Assert.False(available.IsIncluded); // never claims an inclusion that did not happen
        Assert.Equal(AssociateViewModel.NoteChangeBlockedMessage, viewModel.SaveBlockedMessage);
    }

    [Fact]
    public async Task PersistentNotes_AreNeverIncludedAutomatically_AndSelectionsAreDateSpecific()
    {
        var note = Note("Scanner down");
        var (viewModel, _, _) = Create(s =>
        {
            s.RelevantOperationalNotesByDate[Today] = [note];
            s.RelevantOperationalNotesByDate[Today.AddDays(1)] = [note];
        });
        await viewModel.PendingLoad;
        Assert.Empty(viewModel.SelectedOperationalNotes);
        Assert.Equal("Included in this Passdown (0)", viewModel.IncludedPersistentNotesHeading);

        await viewModel.AvailableOperationalNoteSuggestions.Single().AddCommand.ExecuteAsync(null);
        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;

        Assert.Empty(viewModel.SelectedOperationalNotes);
        Assert.Single(viewModel.AvailableOperationalNoteSuggestions);
    }

    [Fact]
    public async Task ManageNotes_OpensOperationalNotes_WithoutTouchingTheDateOrDraft()
    {
        var (associate, service, _) = Create();
        await associate.PendingLoad;
        var main = new MainViewModel(
            new OperationsDashboardViewModel(new FakeOperationsDashboardService()),
            new OperationsCalendarViewModel(new FakeOperationsCalendarService(), new FakeClock(FixedNow)),
            associate,
            new OperationalNotesViewModel(new FakeOperationalNotesService(), new FakeClock(FixedNow)),
            new PassdownHistoryViewModel(new FakePassdownHistoryService { Summaries = [new FinalizedPassdownSummary(Today, FixedNow, FinalizationOrigin.Scheduled, true, 100)] }, new FakeFinalizedPassdownPdfRetrievalService()),
            new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator()));
        main.ShowAssociateSectionCommand.Execute(null);
        associate.ShowNotesSectionCommand.Execute(null);
        var saves = service.SaveCalls.Count;

        associate.OpenOperationalNotesCommand.Execute(null);

        Assert.True(main.IsOperationalNotesSectionSelected);
        Assert.Equal(Today, associate.SelectedDate);
        Assert.True(associate.IsNotesSectionSelected);
        Assert.Equal(saves, service.SaveCalls.Count);
    }

    [Fact]
    public void NotesMarkup_SeparatesDailyAndPersistent_AndKeepsTheEditorPlainText()
    {
        var notes = Element(Markup(), "NotesSectionScroll");

        Assert.Contains("x:Name=\"NotesSheet\" Classes=\"editorSheet\"", notes);
        Assert.Contains("x:Name=\"DailyNotesSection\"", notes);
        Assert.Contains("x:Name=\"PersistentNotesSection\"", notes);
        Assert.True(notes.IndexOf("DailyNotesSection", StringComparison.Ordinal) < notes.IndexOf("PersistentNotesSection", StringComparison.Ordinal));

        var editor = Regex.Match(notes, "<TextBox x:Name=\"DailyNotesTextBox\".*?/>", RegexOptions.Singleline).Value;
        Assert.Contains("Text=\"{Binding OperationalNotesText}\"", editor);
        Assert.Contains("AcceptsReturn=\"True\"", editor);
        Assert.Contains("spell:SpellCheck.IsEnabled=\"True\"", editor);
        Assert.Contains("AutomationProperties.Name=\"Operational notes\"", editor);
        Assert.DoesNotContain("RichText", notes);
        Assert.DoesNotContain("Markdown", notes);

        Assert.Contains("Text=\"Each non-empty line becomes its own note in the Passdown.\"", notes);
        Assert.Contains("Text=\"{Binding NotesSummaryText}\"", notes);
        Assert.Contains("Command=\"{Binding OpenOperationalNotesCommand}\"", notes);
        Assert.Contains("Command=\"{Binding RemoveCommand}\"", notes);
        Assert.Contains("Command=\"{Binding AddCommand}\"", notes);
        Assert.Equal(2, Regex.Matches(notes, Regex.Escape("IsChecked=\"{Binding IsIncluded, Mode=TwoWay}\"")).Count);
        Assert.Contains("They are never added to a Passdown automatically.", notes);
        Assert.Contains("it never resolves, edits or deletes the original note.", notes);
        Assert.DoesNotContain("KeyFrame", notes);
        Assert.DoesNotContain("Transitions", notes);
    }

    // ---- Shared polish: dropdown chevrons, Actions menu, title bar tokens ------------------------------------------

    [Fact]
    public void DropdownChevrons_UseOneFixedGeometry_CentredInATwelvePixelBox_NeverStretched()
    {
        var geometry = Regex.Match(Tokens, "<StreamGeometry x:Key=\"PassdownDropdownChevronGeometry\">([^<]+)</StreamGeometry>").Groups[1].Value;
        var points = Regex.Matches(geometry, "([\\d.]+),([\\d.]+)").Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(3, points.Count);
        Assert.Equal(6.0, (points.Min(p => p.X) + points.Max(p => p.X)) / 2, 3);
        Assert.Equal(6.0, (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2, 3);

        var right = Regex.Match(Tokens, "<StreamGeometry x:Key=\"PassdownDisclosureChevronRightGeometry\">([^<]+)</StreamGeometry>").Groups[1].Value;
        var rp = Regex.Matches(right, "([\\d.]+),([\\d.]+)").Select(m => (X: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(6.0, (rp.Min(p => p.X) + rp.Max(p => p.X)) / 2, 3);
        Assert.Equal(6.0, (rp.Min(p => p.Y) + rp.Max(p => p.Y)) / 2, 3);

        var style = Regex.Match(Styles, "<Style Selector=\"Path.dropdownChevron\">.*?</Style>", RegexOptions.Singleline).Value;
        Assert.Contains("<Setter Property=\"Width\" Value=\"12\" />", style);
        Assert.Contains("<Setter Property=\"Height\" Value=\"12\" />", style);
        Assert.Contains("<Setter Property=\"Stretch\" Value=\"None\" />", style);
        Assert.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Center\" />", style);

        var markup = Markup();
        Assert.Equal(2, Regex.Matches(markup, "<Path[^>]*Classes=\"dropdownChevron\"").Count); // Actions and the date button
        Assert.DoesNotContain("M 2,5 L 8,11 L 14,5", markup); // the old stretched 2:1 glyph
        Assert.DoesNotContain("navGlyph small", markup);
        Assert.DoesNotContain("M6 9l6 6 6-6", markup);
        var category = Regex.Match(markup, "<Style Selector=\"Path.categoryChevron\">.*?</Style>", RegexOptions.Singleline).Value;
        Assert.Contains("<Setter Property=\"Stretch\" Value=\"None\" />", category);
    }

    [Fact]
    public void ActionsMenu_KeepsEveryCommand_InOrder_WithItsShortcut()
    {
        var markup = Markup();
        var button = Element(markup, "PassdownActionsButton");

        var headers = Regex.Matches(button, "<MenuItem[^>]*?Header=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(
        [
            "Print…", "Save PDF…", "Copy for Teams: Operations Summary", "Copy for Teams: Today's Pricing", "Copy for Teams: Operations + Pricing",
            "{Binding Preview.TogglePreviewLabel}", "PDF Style: Classic", "PDF Style: Compact", "PDF Style: Modern",
            "Check Online (refresh official information)", "Season Price History…",
        ], headers);
        Assert.Contains("InputGesture=\"Ctrl+P\"", button);
        Assert.Contains("<StackPanel Classes=\"dropdownLabel\">", button);
        Assert.Contains("<Path Classes=\"dropdownChevron\" Data=\"{StaticResource PassdownDropdownChevronGeometry}\" />", button);
    }

    [Fact]
    public void TitleBar_KeepsTheNativeChrome_OriginalIcon_AndName_AndTheNavyTokensMeetContrast()
    {
        var window = Markup("MainWindow");
        Assert.Contains("Icon=\"/Assets/logo.ico\"", window);
        Assert.Contains("Title=\"Carowinds Operations\"", window);
        // Native chrome only: no custom title bar, no extended client area, no replaced decorations.
        Assert.DoesNotContain("ExtendClientArea", window);
        Assert.DoesNotContain("SystemDecorations", window);
        Assert.DoesNotContain("WindowDecorations", window);
        Assert.DoesNotContain("TitleBar", window);

        var caption = Regex.Match(Tokens, "<Color x:Key=\"PassdownTitleBarColor\">#([0-9A-F]{6})</Color>").Groups[1].Value;
        var text = Regex.Match(Tokens, "<Color x:Key=\"PassdownTitleBarTextColor\">#([0-9A-F]{6})</Color>").Groups[1].Value;
        Assert.Equal("1B3553", caption);
        Assert.Equal("FFFFFF", text);
        Assert.True(Contrast(caption, text) >= 7.0, $"title text contrast {Contrast(caption, text):F1}:1 is below AAA");
    }

    private static double Contrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int i)
            {
                var c = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
        }

        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
