using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 31D: Settings → Passdown Preferences, saved section selections in the Associate editor, and the Shows
/// switch moved into Include in Passdown (kept apart from which shows are selected). Phase 31E: show selection
/// moved to its own Shows rail section. View tests assert markup, never bound values (see the partition notes).
/// </summary>
public class Phase31DPassdownPreferencesTests
{
    /// <summary>2026-08-21 11:00 AM Eastern.</summary>
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 21);

    private static readonly ScheduledShow Parade = new("SHOW-030-00174", "Splash Parade", null, [new TimeOnly(12)]);
    private static readonly ScheduledShow Bubbles = new("SHOW-030-00178", "Gazillion Bubble Show", "Carowinds Theatre", [new TimeOnly(13, 15)]);

    private static readonly PassdownDefaultSelections Custom = new(
        DailyAdmission: false, FastLane: true, Promotions: false, SeasonPasses: true, AddOns: true, OperationalNotes: false, Shows: true);

    private static (AssociateViewModel ViewModel, ManualDebounceScheduler Scheduler) CreateAssociate(FakeAssociatePassdownService service)
    {
        service.ShowSchedules[Today] = new ShowScheduleView(Today, ShowScheduleState.Scheduled, [Parade, Bubbles], false, FixedNow.AddHours(-1), FixedNow.AddHours(-1));
        var scheduler = new ManualDebounceScheduler();
        return (new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay), scheduler);
    }

    private static async Task SaveAsync(AssociateViewModel viewModel, ManualDebounceScheduler scheduler)
    {
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
    }

    // ---- Associate: new and saved dates -----------------------------------------------------------

    [Fact]
    public async Task NewDate_BuiltInDefaults_CheckOperationalNotes()
    {
        var (viewModel, _) = CreateAssociate(new FakeAssociatePassdownService());
        await viewModel.PendingLoad;

        Assert.True(viewModel.IncludeOperationalNotes);
        Assert.True(viewModel.IncludeDailyAdmission);
        Assert.True(viewModel.IncludeFastLane);
        Assert.True(viewModel.IncludePromotions);
        Assert.False(viewModel.IncludeSeasonPasses);
        Assert.False(viewModel.IncludeAddOns);
        Assert.False(viewModel.IncludeShows);
    }

    [Fact]
    public async Task NewDate_ConfiguredDefaults_AreShown()
    {
        var (viewModel, _) = CreateAssociate(new FakeAssociatePassdownService { NewDraftDefaults = Custom });
        await viewModel.PendingLoad;

        Assert.False(viewModel.IncludeDailyAdmission);
        Assert.True(viewModel.IncludeFastLane);
        Assert.False(viewModel.IncludePromotions);
        Assert.True(viewModel.IncludeSeasonPasses);
        Assert.True(viewModel.IncludeAddOns);
        Assert.False(viewModel.IncludeOperationalNotes);
        Assert.True(viewModel.IncludeShows);
        Assert.All(viewModel.ShowRows, row => Assert.False(row.IsSelected)); // never a pre-selected show
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OperationalNotesChoice_IsSaved_AndRestoredAfterARestart(bool include)
    {
        var service = new FakeAssociatePassdownService { NewDraftDefaults = PassdownDefaultSelections.BuiltIn with { OperationalNotes = !include } };
        var (viewModel, scheduler) = CreateAssociate(service);
        await viewModel.PendingLoad;

        viewModel.IncludeOperationalNotes = include;
        await SaveAsync(viewModel, scheduler);
        Assert.Equal(include, service.StoredDraft(Today)!.SectionSelection.IncludeOperationalNotes);

        // A restart (or an updated app) is a new editor over the same stored drafts — even with other defaults.
        service.NewDraftDefaults = PassdownDefaultSelections.BuiltIn with { OperationalNotes = !include };
        var (restarted, _) = CreateAssociate(service);
        await restarted.PendingLoad;

        Assert.Equal(include, restarted.IncludeOperationalNotes);
    }

    [Fact]
    public async Task SavedDate_KeepsItsSelections_WhenDefaultsChange_AndSwitchingDatesNeverRewritesThem()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, scheduler) = CreateAssociate(service);
        await viewModel.PendingLoad;
        viewModel.IncludeOperationalNotes = false;
        viewModel.IncludeSeasonPasses = true;
        await SaveAsync(viewModel, scheduler);

        service.NewDraftDefaults = Custom;
        viewModel.SelectedDate = Today.AddDays(1);
        await viewModel.PendingLoad;
        Assert.True(viewModel.IncludeSeasonPasses); // new date: Custom
        Assert.False(viewModel.IncludeDailyAdmission);

        viewModel.SelectedDate = Today;
        await viewModel.PendingLoad;
        Assert.False(viewModel.IncludeOperationalNotes);
        Assert.True(viewModel.IncludeSeasonPasses);
        Assert.True(viewModel.IncludeDailyAdmission); // its own saved value, not Custom's
        Assert.Empty(service.CheckOnlineCalls); // date switching never retrieves online
    }

    // ---- Associate: Shows switch versus show selection --------------------------------------------

    [Fact]
    public async Task ShowsSwitch_IsIndependentOfWhichShowsAreSelected()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, scheduler) = CreateAssociate(service);
        await viewModel.PendingLoad;

        viewModel.ShowRows.Single(r => r.Name == "Splash Parade").IsSelected = true; // selecting needs no switch
        await SaveAsync(viewModel, scheduler);
        Assert.False(service.StoredDraft(Today)!.Shows!.IncludeInPassdown);
        Assert.Equal(["SHOW-030-00174"], service.StoredDraft(Today)!.Shows!.SelectedShows.Select(s => s.ShowId));

        viewModel.IncludeShows = true;
        await SaveAsync(viewModel, scheduler);
        viewModel.IncludeShows = false;
        await SaveAsync(viewModel, scheduler);

        Assert.True(viewModel.ShowRows.Single(r => r.Name == "Splash Parade").IsSelected); // turning it off keeps the choice
        Assert.False(service.StoredDraft(Today)!.Shows!.IncludeInPassdown);
        Assert.Single(service.StoredDraft(Today)!.Shows!.SelectedShows);
        Assert.Empty(viewModel.Workspace!.Shows.PrintableRows);

        // Re-checking prints exactly the shows that were already selected.
        viewModel.IncludeShows = true;
        await SaveAsync(viewModel, scheduler);
        Assert.Equal(["Splash Parade"], viewModel.Workspace!.Shows.PrintableRows.Select(r => r.Name));
    }

    [Fact]
    public async Task SwitchingToTheShowsPage_NeverLoadsOrSaves()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, _) = CreateAssociate(service);
        await viewModel.PendingLoad;
        var loads = service.LoadCalls.Count;

        viewModel.ShowShowsSectionCommand.Execute(null);
        viewModel.ShowOverviewSectionCommand.Execute(null);
        viewModel.ShowShowsSectionCommand.Execute(null);

        Assert.True(viewModel.IsShowsSectionSelected);
        Assert.Equal(loads, service.LoadCalls.Count);
        Assert.Empty(service.SaveCalls);
        Assert.Empty(service.CheckOnlineCalls);
    }

    [Fact]
    public async Task ShowsInclusionText_SaysWhetherTheSectionPrints()
    {
        var (viewModel, _) = CreateAssociate(new FakeAssociatePassdownService());
        await viewModel.PendingLoad;

        Assert.Equal(AssociateViewModel.ShowsNotIncludedText, viewModel.ShowsInclusionText);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        viewModel.IncludeShows = true;

        Assert.Equal(AssociateViewModel.ShowsIncludedText, viewModel.ShowsInclusionText);
        Assert.Contains(nameof(AssociateViewModel.ShowsInclusionText), raised);
    }

    // ---- Associate: changed defaults while a date is open -------------------------------------------

    [Fact]
    public async Task ChangedDefaults_UpdateAnOpenUnsavedDate()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, _) = CreateAssociate(service);
        await viewModel.PendingLoad;

        service.NewDraftDefaults = Custom;
        await viewModel.RefreshNewPassdownDefaultsAsync();

        Assert.False(viewModel.IncludeOperationalNotes);
        Assert.True(viewModel.IncludeShows);
        Assert.Empty(service.SaveCalls); // showing new defaults is not an edit
    }

    [Fact]
    public async Task ChangedDefaults_NeverTouchASavedDate()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, scheduler) = CreateAssociate(service);
        await viewModel.PendingLoad;
        viewModel.IncludePromotions = false;
        await SaveAsync(viewModel, scheduler);
        var loadsBefore = service.LoadCalls.Count;

        service.NewDraftDefaults = Custom;
        await viewModel.RefreshNewPassdownDefaultsAsync();

        Assert.True(viewModel.IncludeOperationalNotes);
        Assert.False(viewModel.IncludePromotions);
        Assert.Equal(loadsBefore, service.LoadCalls.Count);
    }

    [Fact]
    public async Task ChangedDefaults_NeverOverwriteUnsavedEdits()
    {
        var service = new FakeAssociatePassdownService();
        var (viewModel, _) = CreateAssociate(service);
        await viewModel.PendingLoad;
        viewModel.IncludeAddOns = true; // pending autosave, not yet saved

        service.NewDraftDefaults = Custom;
        await viewModel.RefreshNewPassdownDefaultsAsync();

        Assert.True(viewModel.IncludeAddOns);
        Assert.True(viewModel.IncludeOperationalNotes);
    }

    // ---- Settings → Passdown Preferences -----------------------------------------------------------

    private sealed class RecordingStore(PassdownDefaultSelections initial) : IPassdownDefaultsPreferenceStore
    {
        public PassdownDefaultSelections Value { get; private set; } = initial;

        public List<PassdownDefaultSelections> Saves { get; } = [];

        public Exception? SaveException { get; set; }

        public Task<PassdownDefaultSelections> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);

        public Task SaveAsync(PassdownDefaultSelections defaults, CancellationToken cancellationToken = default)
        {
            if (SaveException is { } ex)
            {
                return Task.FromException(ex);
            }

            Saves.Add(defaults);
            Value = defaults;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Settings_ShowsTheStoredDefaults_WithoutSavingOnLoad()
    {
        var store = new RecordingStore(Custom);
        var settings = new PassdownPreferencesSettingsViewModel(store);
        await settings.PendingLoad;

        Assert.Equal(Custom, settings.Current);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public async Task Settings_WithNoStoredFile_ShowsBuiltIn_WithOperationalNotesOn()
    {
        var settings = new PassdownPreferencesSettingsViewModel(new RecordingStore(PassdownDefaultSelections.BuiltIn));
        await settings.PendingLoad;

        Assert.Equal(PassdownDefaultSelections.BuiltIn, settings.Current);
        Assert.True(settings.OperationalNotes);
        Assert.False(settings.Shows);
    }

    [Fact]
    public async Task Settings_EachCheckbox_SavesIndependently_AndAnnouncesTheChange()
    {
        var store = new RecordingStore(PassdownDefaultSelections.BuiltIn);
        var settings = new PassdownPreferencesSettingsViewModel(store);
        await settings.PendingLoad;
        PassdownDefaultSelections? announced = null;
        settings.DefaultsSaved += (_, defaults) => announced = defaults;

        settings.Shows = true;
        Assert.True(await settings.PendingSave);
        settings.OperationalNotes = false;
        Assert.True(await settings.PendingSave);

        Assert.Equal(PassdownDefaultSelections.BuiltIn with { Shows = true, OperationalNotes = false }, store.Value);
        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(store.Value, announced);
        Assert.Null(settings.SaveMessage);
    }

    [Fact]
    public async Task Settings_FailedSave_SaysSo_AndAnnouncesNothing()
    {
        var store = new RecordingStore(PassdownDefaultSelections.BuiltIn) { SaveException = new IOException("disk full") };
        var settings = new PassdownPreferencesSettingsViewModel(store);
        await settings.PendingLoad;
        var announced = false;
        settings.DefaultsSaved += (_, _) => announced = true;

        settings.AddOns = true;

        Assert.False(await settings.PendingSave);
        Assert.Equal(PassdownPreferencesSettingsViewModel.SaveFailedMessage, settings.SaveMessage);
        Assert.False(announced);
    }

    // ---- Markup -----------------------------------------------------------------------------------

    private static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));

    private static string Element(string markup, string name)
    {
        var start = markup.LastIndexOf('<', markup.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal));
        var tag = Regex.Match(markup[start..], @"^<([\w:]+)").Groups[1].Value;
        var depth = 0;
        foreach (Match match in Regex.Matches(markup[start..], $@"<(/?){Regex.Escape(tag)}\b[^>]*?(/?)>"))
        {
            if (match.Groups[1].Value == "/")
            {
                depth--;
            }
            else if (match.Groups[2].Value != "/")
            {
                depth++;
            }

            if (depth == 0)
            {
                return markup[start..(start + match.Index + match.Length)];
            }
        }

        throw new InvalidOperationException($"{name} has no closing tag");
    }

    [Fact]
    public void AssociateView_HasExactlyOneShowsInclusionCheckbox_InIncludeInPassdown()
    {
        var markup = Markup("AssociateView");
        var include = Element(markup, "IncludeInPassdownPanel");
        var shows = Element(markup, "ShowsPanel");

        Assert.Single(Regex.Matches(markup, Regex.Escape("IsChecked=\"{Binding IncludeShows}\"")));
        Assert.Contains("x:Name=\"IncludeShowsCheckBox\" Content=\"Shows\" IsChecked=\"{Binding IncludeShows}\"", include);
        Assert.Contains("AutomationProperties.Name=\"Include Shows in the Passdown\"", include);
        Assert.DoesNotContain("{Binding IncludeShows}\"", shows);
        Assert.DoesNotContain("Include the Shows section in the Passdown", markup);

        // The other six checkboxes are still there, and Shows comes after Operational Notes.
        foreach (var binding in new[] { "IncludeDailyAdmission", "IncludeFastLane", "IncludePromotions", "IncludeSeasonPasses", "IncludeAddOns", "IncludeOperationalNotes" })
        {
            Assert.Contains($"IsChecked=\"{{Binding {binding}}}\"", include);
        }

        Assert.True(include.IndexOf("IncludeOperationalNotes", StringComparison.Ordinal) < include.IndexOf("IncludeShowsCheckBox", StringComparison.Ordinal));
    }

    [Fact]
    public void AssociateView_ShowsPanel_KeepsSelectionControls_AndSaysWhetherItPrints()
    {
        var shows = Element(Markup("AssociateView"), "ShowsPanel");

        Assert.Contains("ItemsSource=\"{Binding ShowRows}\"", shows);
        Assert.Contains("IsChecked=\"{Binding IsSelected}\"", shows);
        Assert.Contains("Command=\"{Binding SelectAllShowsCommand}\"", shows);
        Assert.Contains("Command=\"{Binding ClearShowsCommand}\"", shows);
        Assert.Contains("Text=\"{Binding ShowsInclusionText}\"", shows);
        Assert.Contains("<TextBlock Text=\"Official schedule\" />", shows);
        Assert.Contains("Text=\"{Binding Name}\" FontWeight=\"SemiBold\" TextWrapping=\"Wrap\"", shows); // names wrap, never clip
    }

    [Fact]
    public void AssociateView_SectionTabs_AreOverviewProductsShowsNotes()
    {
        var markup = Markup("AssociateView");
        var rail = Regex.Matches(markup, "<Button Classes=\"sectionTab\" Classes.active=\"\\{Binding (\\w+)\\}\"").Select(m => m.Groups[1].Value);

        Assert.Equal(["IsOverviewSectionSelected", "IsProductsSectionSelected", "IsShowsSectionSelected", "IsNotesSectionSelected"], rail);
        Assert.Contains("Content=\"Shows\" Command=\"{Binding ShowShowsSectionCommand}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Shows section\"", markup);
    }

    /// <summary>Phase 31E: the Shows panel lives on its own Shows page; Overview keeps only Include in Passdown.</summary>
    [Fact]
    public void AssociateView_ShowsPanel_IsOnTheShowsPage_NotOverview()
    {
        var markup = Markup("AssociateView");
        var showsPage = Element(markup, "ShowsSectionScroll");

        Assert.Contains("IsVisible=\"{Binding IsShowsSectionSelected}\"", showsPage);
        Assert.Contains("x:Name=\"ShowsPanel\"", showsPage);
        Assert.Single(Regex.Matches(markup, "x:Name=\"ShowsPanel\""));
        Assert.Single(Regex.Matches(markup, Regex.Escape("ItemsSource=\"{Binding ShowRows}\"")));
        Assert.DoesNotContain("IncludeInPassdownPanel", showsPage);
        Assert.DoesNotContain("PassdownChoicesLayout", markup);

        var overviewStart = markup.IndexOf("IsVisible=\"{Binding IsOverviewSectionSelected}\"", StringComparison.Ordinal);
        var productsStart = markup.IndexOf("IsVisible=\"{Binding IsProductsSectionSelected}\"", StringComparison.Ordinal);
        var overview = markup[overviewStart..productsStart];
        Assert.Contains("x:Name=\"IncludeInPassdownPanel\"", overview);
        Assert.Contains("SeasonContext.HeadingText", overview); // official seasonal-event information stays put
        Assert.DoesNotContain("ShowRows", overview);
        Assert.DoesNotContain("SelectAllShowsCommand", overview);
    }

    [Fact]
    public void AssociateView_CopyOptions_AreUnchanged()
    {
        var markup = Markup("AssociateView");
        var kinds = Regex.Matches(markup, @"CommandParameter=""\{x:Static documents:AssociateTeamsCopyKind\.(\w+)\}""").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(["OperationsSummary", "TodaysPricing", "OperationsAndPricing"], kinds);
    }

    [Fact]
    public void SettingsView_HasPassdownPreferences_WithSevenDefaults()
    {
        var section = Element(Markup("PersonalSyncSettingsView"), "PassdownPreferencesSection");
        var markup = Markup("PersonalSyncSettingsView");

        // Phase 35F: the group is titled "Passdown Defaults" (approved Settings prototype); same settings and bindings.
        Assert.Contains("<TextBlock Text=\"Passdown Defaults\" Classes=\"sectionTitle\"", markup);
        Assert.Contains("Text=\"Choose which sections are selected automatically when starting a new Passdown.\"", section);
        foreach (var (property, label) in new[]
                 {
                     ("DailyAdmission", "Daily Admission"), ("FastLane", "Fast Lane"), ("Promotions", "Promotions"),
                     ("SeasonPasses", "Season Passes"), ("AddOns", "Add-Ons"), ("OperationalNotes", "Operational Notes"), ("Shows", "Shows"),
                 })
        {
            Assert.Contains($"<CheckBox Content=\"{label}\" IsChecked=\"{{Binding PassdownPreferences.{property}}}\"", section);
        }

        Assert.Contains("{Binding PassdownPreferences.SaveMessage}", section);
        Assert.Equal(PassdownPreferencesSettingsViewModel.Description, "Choose which sections are selected automatically when starting a new Passdown.");
    }

    [Fact]
    public void SettingsView_PassdownPreferences_SitsAfterAppearance_AndBeforePersonalSync()
    {
        var markup = Markup("PersonalSyncSettingsView");

        var appearance = markup.IndexOf("x:Name=\"AppearanceSection\"", StringComparison.Ordinal);
        var preferences = markup.IndexOf("x:Name=\"PassdownPreferencesSection\"", StringComparison.Ordinal);
        var sync = markup.IndexOf("x:Name=\"PersonalSyncSection\"", StringComparison.Ordinal);
        Assert.True(appearance < preferences && preferences < sync);
    }
}
