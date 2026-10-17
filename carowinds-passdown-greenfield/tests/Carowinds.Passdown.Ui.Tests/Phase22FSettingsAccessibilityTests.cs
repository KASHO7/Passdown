using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22F: Settings organization (every existing control still reachable, nothing implied that the app
/// cannot do), problem styling flags, Diagnostics result state, accessibility properties (current-section
/// status, accessible names on icon-only controls and progress indicators), and popup styling. Markup is
/// asserted by its binding strings, never by bound values (mixed partition rule).
/// </summary>
public class Phase22FSettingsAccessibilityTests
{
    private static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));

    // ---------------------------------------------------------------- Settings organization

    [Fact]
    public void Settings_IsOnePage_WithFiveGroupsInOrder()
    {
        var markup = Markup("PersonalSyncSettingsView");
        var order = new[] { "\"Appearance\"", "\"Data Refresh\"", "\"Personal Sync\"", "\"Diagnostics\"", "\"About this build\"" }
            .Select(title => markup.IndexOf($"Text={title} Classes=\"sectionTitle\"", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.DoesNotContain("<TabControl", markup);
        Assert.DoesNotContain("<Expander", markup);
        Assert.Single(Regex.Matches(markup, "<ScrollViewer "));
    }

    [Fact]
    public void Settings_KeepsEveryExistingBindingCommandAndAccessibleName()
    {
        var markup = Markup("PersonalSyncSettingsView");
        foreach (var expected in new[]
        {
            "PersonalSyncEnabled", "SyncRootPath", "FirstSuggestedRoot", "ChooseFolderCommand", "StatusPresentation.Text", "StatusDetail",
            "AutomationStatusPresentation.Text", "AutomationStatusPresentation.DetailText", "LastSyncedText", "SyncNowCommand", "SyncResultSummary",
            "HasConflicts", "ConflictStatusText", "ReviewConflictsCommand", "IsReviewingConflicts", "OpenConflictCommand", "CloseConflictsCommand",
            "SelectedConflictDetail.Fields", "KeepLocalCommand", "UseRemoteCommand", "AcceptDeletionCommand", "CloseConflictDetailCommand",
            "ConflictActionMessage", "DeviceId", "BuildProductName", "BuildVersionText", "BuildExecutablePathText",
            "Diagnostics.ExportDiagnosticsCommand", "Diagnostics.DiagnosticsExportMessage",
            "AutomationProperties.Name=\"Enable Personal Sync\"", "AutomationProperties.Name=\"Choose Personal Sync Folder\"",
            "AutomationProperties.Name=\"Sync Now\"", "AutomationProperties.Name=\"Review Conflicts\"", "AutomationProperties.Name=\"Export Diagnostics\"",
            "AutomationProperties.Name=\"Keep This Device\"", "AutomationProperties.Name=\"Use Other Device\"", "AutomationProperties.Name=\"Accept Deletion\"",
        })
        {
            Assert.Contains(expected, markup);
        }
    }

    [Fact]
    public void AppearanceAndRefresh_AreInformationOnly_AndNeverImplyControlsTheAppDoesNotHave()
    {
        var markup = Markup("PersonalSyncSettingsView");
        var info = markup[markup.IndexOf("x:Name=\"AppearanceSection\"", StringComparison.Ordinal)..markup.IndexOf("x:Name=\"PersonalSyncSection\"", StringComparison.Ordinal)];

        foreach (var control in new[] { "<ComboBox", "<RadioButton", "<CheckBox", "<ToggleSwitch", "<Button", "<TextBox", "Command=" })
        {
            Assert.DoesNotContain(control, info);
        }

        Assert.Contains("Follows Windows", info);
        Assert.Contains("every four hours", info);
        Assert.Contains("Daily at 6 AM", info);
        Assert.Contains("not from this screen", info); // the scheduled task is set up outside Settings
        Assert.Contains("12 months ahead", info);
    }

    [Fact]
    public void PersonalSync_ShowsTheMixedVersionCaution_WithoutClaimingProtection()
    {
        var markup = Markup("PersonalSyncSettingsView");
        Assert.Contains("x:Name=\"MixedVersionNote\"", markup);
        Assert.Contains("An older version can drop show selections when it re-saves a synced passdown draft.", markup);
        var visibleText = string.Join(" ", Regex.Matches(markup, "Text=\"([^\"{]*)\"").Select(m => m.Groups[1].Value));
        Assert.DoesNotContain("protect", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("automatic", visibleText.Replace("Automatic sync", string.Empty, StringComparison.Ordinal).Replace("automatically", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_LongPathsWrap_AndTheValueColumnFlexes()
    {
        var markup = Markup("PersonalSyncSettingsView");
        Assert.Contains("<SelectableTextBlock Text=\"{Binding SyncRootPath}\" TextWrapping=\"Wrap\"", markup);
        Assert.Contains("Text=\"{Binding BuildExecutablePathText}\" TextWrapping=\"Wrap\"", markup);
        Assert.Matches(new Regex("Classes=\"settingsRows\" ColumnDefinitions=\"140,\\*\""), markup);
        Assert.Contains("MaxWidth=\"720\"", markup);
    }

    [Theory]
    [InlineData(PersonalSyncStatus.Off, false)]
    [InlineData(PersonalSyncStatus.NotConfigured, false)]
    [InlineData(PersonalSyncStatus.Ready, false)]
    [InlineData(PersonalSyncStatus.FolderUnavailable, true)]
    [InlineData(PersonalSyncStatus.ConfigurationError, true)]
    public void FolderStatus_IsAProblem_OnlyWhenUnreachableOrMisconfigured(PersonalSyncStatus status, bool problem)
    {
        Assert.Equal(problem, PersonalSyncStatusPresentation.Build(status).IsProblem);
    }

    [Theory]
    [InlineData(PersonalSyncAutomationState.Off, false)]
    [InlineData(PersonalSyncAutomationState.UpToDate, false)]
    [InlineData(PersonalSyncAutomationState.Syncing, false)]
    [InlineData(PersonalSyncAutomationState.Pending, false)]
    [InlineData(PersonalSyncAutomationState.Offline, false)] // safe locally — never alarming (Phase 16F §11)
    [InlineData(PersonalSyncAutomationState.ConflictAttention, true)]
    [InlineData(PersonalSyncAutomationState.HistoryConflictAttention, true)]
    [InlineData(PersonalSyncAutomationState.Error, true)]
    public void AutomaticSync_IsAProblem_OnlyForErrorsAndConflicts(PersonalSyncAutomationState state, bool problem)
    {
        var presentation = PersonalSyncAutomationStatusPresentation.Build(PersonalSyncAutomationSnapshot.Initial with { State = state, ConflictCount = 1 });
        Assert.Equal(problem, presentation.IsProblem);
    }

    [Fact]
    public async Task Diagnostics_FlagsOnlyFailures_AndResetsOnTheNextAttempt()
    {
        var picker = new FakeDiagnosticExportFilePicker { ResultFactory = () => DiagnosticExportSaveResult.Failed("The folder could not be written to.") };
        var diagnostics = new DiagnosticsExportViewModel(new FakeDiagnosticExportService(), picker);

        await diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null);
        Assert.True(diagnostics.IsDiagnosticsExportProblem);
        Assert.Equal("The folder could not be written to.", diagnostics.DiagnosticsExportMessage);

        picker.ResultFactory = () => DiagnosticExportSaveResult.Saved("/fake/diagnostics.zip");
        await diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null);
        Assert.False(diagnostics.IsDiagnosticsExportProblem);
        Assert.Equal("Diagnostics saved.", diagnostics.DiagnosticsExportMessage);

        picker.ResultFactory = DiagnosticExportSaveResult.Cancelled;
        await diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null);
        Assert.False(diagnostics.IsDiagnosticsExportProblem);
        Assert.Null(diagnostics.DiagnosticsExportMessage);
    }

    [Fact]
    public async Task Diagnostics_AnUnexpectedFailure_IsAProblem()
    {
        var service = new FakeDiagnosticExportService { ResultFactory = () => throw new InvalidOperationException("boom") };
        var diagnostics = new DiagnosticsExportViewModel(service, new FakeDiagnosticExportFilePicker());

        await diagnostics.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.True(diagnostics.IsDiagnosticsExportProblem);
        Assert.Equal("Diagnostics could not be created. Try again.", diagnostics.DiagnosticsExportMessage);
    }

    [Fact]
    public void Diagnostics_ResultLineIsStyledByOutcome_AndAnnouncedPolitely()
    {
        var markup = Markup("PersonalSyncSettingsView");
        var section = markup[markup.IndexOf("x:Name=\"DiagnosticsSection\"", StringComparison.Ordinal)..markup.IndexOf("x:Name=\"AboutSection\"", StringComparison.Ordinal)];
        Assert.Contains("Classes.statusError=\"{Binding Diagnostics.IsDiagnosticsExportProblem}\"", section);
        Assert.Contains("Classes.statusSuccess=\"{Binding !Diagnostics.IsDiagnosticsExportProblem}\"", section);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", section);
    }

    [Fact]
    public void About_StillIdentifiesTheRunningBuild_FromTheUnchangedBuildIdentity()
    {
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            buildIdentity: new Carowinds.Passdown.Application.Diagnostics.BuildIdentity("Carowinds Operations", "0.1.0-rc.6", "f685b72", @"C:\Apps\Carowinds.Passdown.Desktop.exe"));

        Assert.Equal("Carowinds Operations", settings.BuildProductName);
        Assert.StartsWith("0.1.0-rc.6", settings.BuildVersionText, StringComparison.Ordinal);
        Assert.Contains("Carowinds.Passdown.Desktop.exe", settings.BuildExecutablePathText, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- Associate save failure (fixture)

    [Fact]
    public async Task AFailedAutosave_StaysVisibleAsUnsaved_AndLeavesTheStoredDraftUntouched()
    {
        var service = new FakeAssociatePassdownService();
        var scheduler = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(2026, 10, 17, 15, 0, 0, TimeSpan.Zero)), autosaveDelay: scheduler.Delay);
        await viewModel.PendingLoad;
        service.SaveException = new IOException("simulated disk failure");

        viewModel.ProjectedAttendanceText = "38,500";
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;

        Assert.Equal("Unsaved changes", viewModel.SaveStatusText);
        Assert.Null(service.StoredDraft(viewModel.SelectedDate));
    }

    // ---------------------------------------------------------------- accessibility

    [Fact]
    public void AssociateSectionRail_AnnouncesTheCurrentSection_LikeTheAppSectionStrip()
    {
        // Phase 22B exposes the strip's current page through its .active style; the rail now does the same.
        var shared = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current page\" />", shared);

        var markup = Markup("AssociateView");
        var style = markup[markup.IndexOf("<Style Selector=\"Button.railItem.active\">", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current section\" />", style);
        foreach (var flag in new[] { "IsOverviewSectionSelected", "IsProductsSectionSelected", "IsNotesSectionSelected" })
        {
            Assert.Contains($"Classes=\"railItem\" Classes.active=\"{{Binding {flag}}}\"", markup);
        }
    }

    [Theory]
    [InlineData("MainWindow")]
    [InlineData("DashboardView")]
    [InlineData("OperationsCalendarView")]
    [InlineData("AssociateView")]
    [InlineData("OperationalNotesView")]
    [InlineData("HistoryView")]
    [InlineData("PersonalSyncSettingsView")]
    [InlineData("PriceHistoryView")]
    public void IconOnlyAndTemplatedControls_HaveAccessibleNames(string view)
    {
        var document = XDocument.Parse(Markup(view));
        var unnamed = document.Descendants()
            .Where(e => e.Name.LocalName is "Button" or "ToggleButton" or "Expander" or "ProgressBar" or "ComboBox" or "ListBox" or "TextBox")
            .Where(e => e.Attribute("AutomationProperties.Name") is null)
            .Where(e => e.Name.LocalName is "ProgressBar" or "Expander" or "ComboBox" or "ListBox" or "TextBox"
                || e.Attribute("Content") is not { } content || content.Value.StartsWith('{'))
            .Select(e => $"{e.Name.LocalName} line {((System.Xml.IXmlLineInfo)e).LineNumber}")
            .ToList();

        Assert.Empty(unnamed);
    }

    [Fact]
    public void CalendarPlaceholder_IsAnnounced_AndTheHeadingHasAnAccessibleName()
    {
        var markup = Markup("OperationsCalendarView");
        Assert.Contains("x:Name=\"SelectedDetailPlaceholder\" Text=\"{Binding SelectedDetailPlaceholderText}\"", markup);
        Assert.Matches(new Regex("x:Name=\"SelectedDetailPlaceholder\"[^>]*AutomationProperties.LiveSetting=\"Polite\"", RegexOptions.Singleline), markup);
        Assert.Contains("AutomationProperties.Name=\"{Binding SelectedDateHeadingText, StringFormat='Selected date: {0}'}\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Previous day\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Next day\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Show schedule\"", markup); // ExpandCollapse via the shared disclosure
    }

    [Fact]
    public void HistoryGateTimes_AreLabelled_ForSightedAndScreenReaderUsers()
    {
        var markup = Markup("HistoryView");
        Assert.Contains("<Run Text=\"North Gate · \"", markup);
        Assert.Contains("<Run Text=\"Avenue · \"", markup);
        Assert.Contains("StringFormat='North Gate: {0}'", markup);
        Assert.Contains("StringFormat='Avenue: {0}'", markup);
    }

    [Fact]
    public void DayCellHours_WrapAfterTheDash_InsteadOfTruncating()
    {
        var day = new Carowinds.Passdown.Application.Calendar.OperationsCalendarDay(new DateOnly(2026, 12, 5), true, false, false, false, 0,
            Carowinds.Passdown.Application.Calendar.OperationsCalendarOperatingState.Open,
            new AssociateParkHoursView(
                Carowinds.Passdown.Domain.Hours.ParkHoursCalculator.Calculate(false, new TimeOnly(12, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
                new AssociateVerificationInfo(AssociateVerificationState.Verified, DateTimeOffset.UnixEpoch, null, null),
                OfficialIsClosed: false, OfficialOpening: new TimeOnly(12, 0), OfficialClosing: new TimeOnly(21, 0)));
        var cell = new OperationsCalendarDayViewModel(day, isSelected: false, _ => { });

        Assert.Equal("12 PM–9 PM", cell.EffectiveHoursLine);
        Assert.Equal("12\u00A0PM–9\u00A0PM", cell.EffectiveHoursLineDisplay);
        Assert.Contains("12 PM to 9 PM", cell.AutomationName, StringComparison.Ordinal);

        var markup = Markup("OperationsCalendarView");
        Assert.Contains("Text=\"{Binding EffectiveHoursLineDisplay}\" Classes=\"dayHoursLine\"", markup);
        var style = markup[markup.IndexOf("<Style Selector=\"TextBlock.dayHoursLine\">", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Contains("<Setter Property=\"TextWrapping\" Value=\"Wrap\" />", style);
        Assert.Contains("<Setter Property=\"MaxLines\" Value=\"2\" />", style);
    }

    // ---------------------------------------------------------------- popup styling

    [Fact]
    public void DatePopup_MutesAdjacentMonthDays_WithThemeTokens()
    {
        var markup = Markup("AssociateView");
        Assert.Contains("<Style Selector=\"Calendar.datePopupCalendar CalendarDayButton:inactive /template/ Border#Root\">", markup);
        var start = markup.IndexOf("CalendarDayButton:inactive /template/ ContentPresenter#PART_ContentPresenter", StringComparison.Ordinal);
        Assert.True(start > 0);
        Assert.Contains("{DynamicResource PassdownTextMutedBrush}", markup[start..(start + 260)]);

        // Hover still wins over the inactive fill (declared after it).
        Assert.True(markup.IndexOf("CalendarDayButton:pointerover /template/ Border#Root", StringComparison.Ordinal)
            > markup.IndexOf("CalendarDayButton:inactive /template/ Border#Root", StringComparison.Ordinal));
    }

    [Fact]
    public void PopupSurfaceTokens_ExistInBothThemes()
    {
        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var start = tokens.IndexOf($"x:Key=\"{theme}\"", StringComparison.Ordinal);
            var block = tokens[start..tokens.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal)];
            foreach (var key in new[] { "PassdownPopupSurfaceBrush", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground", "ToolTipBackground", "PassdownTextMutedBrush", "PassdownErrorBrush", "PassdownSuccessBrush", "PassdownWarningBrush" })
            {
                Assert.Contains($"x:Key=\"{key}\"", block);
            }
        }
    }

    [Fact]
    public void Settings_UsesOnlySharedStatusStyles()
    {
        var markup = Markup("PersonalSyncSettingsView");
        var shared = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));
        foreach (var cls in new[] { "Border.statusBadge.error", "Border.statusBadge.warning", "TextBlock.statusError", "TextBlock.statusSuccess", "TextBlock.fieldWarning", "TextBlock.fieldLabel", "Border.inset" })
        {
            Assert.Contains($"Selector=\"{cls}", shared);
            Assert.DoesNotContain($"<Style Selector=\"{cls}\"", markup);
        }
    }
}
