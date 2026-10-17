using System.Globalization;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Converters;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 22D: Associate workspace refinement and operational-count formatting. View-model tests use the
/// fake service and a manual debounce; markup tests read AssociateView.axaml (bound values are never
/// asserted from a constructed view — see the deterministic-partition notes). No real Window here, so
/// this class runs in the mixed Ui partition.
/// </summary>
public sealed class Phase22DAssociateWorkspaceTests
{
    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    private static (AssociateViewModel ViewModel, FakeAssociatePassdownService Service, ManualDebounceScheduler Scheduler) Create(int? attendance = null, int? preSolds = null)
    {
        var service = new FakeAssociatePassdownService();
        if (attendance is not null || preSolds is not null)
        {
            service.WorkspaceFactory = date =>
            {
                var workspace = FakeAssociatePassdownService.DefaultWorkspace(date);
                return workspace with { Draft = workspace.Draft with { ProjectedAttendance = attendance, PreSolds = preSolds } };
            };
        }

        var scheduler = new ManualDebounceScheduler();
        return (new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: scheduler.Delay), service, scheduler);
    }

    private static string Markup(string view = "AssociateView") =>
        Regex.Replace(File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml")), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    // ------------------------------------------------------------------ count editors (Associate)

    [Fact]
    public async Task LoadedCounts_ShowGrouped_StoredValuesStayIntegers()
    {
        var (viewModel, _, _) = Create(38500, 21240);
        await viewModel.PendingLoad;

        Assert.Equal("38,500", viewModel.ProjectedAttendanceText);
        Assert.Equal("21,240", viewModel.PreSoldsText);
        Assert.Equal(38500, viewModel.Workspace!.Draft.ProjectedAttendance);
        Assert.Equal(21240, viewModel.Workspace.Draft.PreSolds);
        Assert.Null(viewModel.ProjectedAttendanceError);
    }

    [Fact]
    public async Task LoadedMissingCounts_StayEmpty_NeverZero()
    {
        var (viewModel, _, _) = Create();
        await viewModel.PendingLoad;

        Assert.True(string.IsNullOrEmpty(viewModel.ProjectedAttendanceText));
        Assert.True(string.IsNullOrEmpty(viewModel.PreSoldsText));
    }

    [Fact]
    public async Task LoadedZero_ShowsZero()
    {
        var (viewModel, _, _) = Create(0, 250);
        await viewModel.PendingLoad;

        Assert.Equal("0", viewModel.ProjectedAttendanceText);
        Assert.Equal("250", viewModel.PreSoldsText);
    }

    [Fact]
    public async Task Typing_IsNeverRewritten_UntilTheFieldLosesFocus()
    {
        var (viewModel, service, scheduler) = Create();
        await viewModel.PendingLoad;

        foreach (var keystroke in new[] { "3", "38", "385", "3850", "38500" })
        {
            viewModel.ProjectedAttendanceText = keystroke;
            Assert.Equal(keystroke, viewModel.ProjectedAttendanceText);
        }

        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Equal(38500, service.SaveCalls.Single().Draft.ProjectedAttendance);
        Assert.Equal("38500", viewModel.ProjectedAttendanceText); // autosave never rewrites the box either.

        viewModel.RegroupCountFields();
        Assert.Equal("38,500", viewModel.ProjectedAttendanceText);
    }

    [Fact]
    public async Task Regroup_IsDisplayOnly_NoExtraSave_NoDirtyState()
    {
        var (viewModel, service, scheduler) = Create();
        await viewModel.PendingLoad;
        viewModel.ProjectedAttendanceText = "38500";
        viewModel.PreSoldsText = "21240";
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
        var savesBefore = service.SaveCalls.Count;
        var stateBefore = viewModel.AutosaveState;

        viewModel.RegroupCountFields();

        Assert.Equal("38,500", viewModel.ProjectedAttendanceText);
        Assert.Equal("21,240", viewModel.PreSoldsText);
        Assert.Equal(stateBefore, viewModel.AutosaveState);
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Equal(savesBefore, service.SaveCalls.Count);
    }

    [Fact]
    public async Task EditingAGroupedValueInPlace_ParsesTheDigitsTyped()
    {
        var (viewModel, service, scheduler) = Create(38500, null);
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = "38,5001"; // caret at the end, one more digit.
        Assert.Null(viewModel.ProjectedAttendanceError);
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;

        Assert.Equal(385001, service.SaveCalls.Single().Draft.ProjectedAttendance);
        viewModel.RegroupCountFields();
        Assert.Equal("385,001", viewModel.ProjectedAttendanceText);
    }

    [Theory]
    [InlineData("abc", "Enter a whole number.")]
    [InlineData("1.5", "Enter a whole number.")]
    [InlineData("-5", "Enter a number 0 or greater.")]
    public async Task InvalidEntry_KeepsTheSameValidation_AndIsNotRegrouped(string text, string error)
    {
        var (viewModel, service, scheduler) = Create();
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = text;
        Assert.Equal(error, viewModel.ProjectedAttendanceError);
        viewModel.RegroupCountFields();

        Assert.Equal(text, viewModel.ProjectedAttendanceText);
        Assert.Equal(error, viewModel.ProjectedAttendanceError);
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;
        Assert.Empty(service.SaveCalls);
    }

    [Fact]
    public async Task ClearingAField_StaysMissing_NotZero()
    {
        var (viewModel, service, scheduler) = Create(38500, null);
        await viewModel.PendingLoad;

        viewModel.ProjectedAttendanceText = string.Empty;
        viewModel.RegroupCountFields();
        scheduler.ElapseLatest();
        await viewModel.PendingAutosave;

        Assert.True(string.IsNullOrEmpty(viewModel.ProjectedAttendanceText));
        Assert.Null(service.SaveCalls.Single().Draft.ProjectedAttendance);
    }

    // ------------------------------------------------------------------ section state

    [Fact]
    public async Task SwitchingSections_KeepsEditsDateAndShows_AndNeverChecksOnline()
    {
        var (viewModel, service, _) = Create();
        await viewModel.PendingLoad;
        viewModel.ProjectedAttendanceText = "12000";
        viewModel.DailyFeatureText = @"What is \(x^2\)?";
        var date = viewModel.SelectedDateForPicker;

        viewModel.ShowProductsSectionCommand.Execute(null);
        viewModel.ShowNotesSectionCommand.Execute(null);
        viewModel.ShowOverviewSectionCommand.Execute(null);

        Assert.Equal("12000", viewModel.ProjectedAttendanceText);
        Assert.Equal(@"What is \(x^2\)?", viewModel.DailyFeatureText);
        Assert.Equal(date, viewModel.SelectedDateForPicker);
        Assert.Empty(service.CheckOnlineCalls);
        Assert.Single(service.LoadCalls);
    }

    [Fact]
    public async Task RailNotesCount_ShowsOnlyWhenThereAreNotes()
    {
        var (viewModel, _, _) = Create();
        await viewModel.PendingLoad;
        Assert.False(viewModel.HasOperationalNotesCount);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        viewModel.OperationalNotesText = "Gate 3 scanner slow";

        Assert.True(viewModel.HasOperationalNotesCount);
        Assert.Equal(1, viewModel.OperationalNotesCount);
        Assert.Contains(nameof(AssociateViewModel.HasOperationalNotesCount), changed);
    }

    // ------------------------------------------------------------------ other count surfaces

    [Theory]
    [InlineData(38500, "38,500")]
    [InlineData(21240, "21,240")]
    [InlineData(1000000, "1,000,000")]
    [InlineData(0, "0")]
    public void Dashboard_FormatsCounts(int count, string expected)
    {
        var snapshot = FakeOperationsDashboardService.DefaultSnapshot(Today) with { ProjectedAttendance = count, PreSolds = count };
        var presentation = OperationsDashboardPresentation.Build(snapshot);

        Assert.Equal(expected, presentation.AttendanceText);
        Assert.Equal(expected, presentation.PreSoldsText);
    }

    [Fact]
    public void Dashboard_MissingCounts_StayNotEntered()
    {
        var presentation = OperationsDashboardPresentation.Build(FakeOperationsDashboardService.DefaultSnapshot(Today) with { ProjectedAttendance = null, PreSolds = null });

        Assert.Equal("Not entered", presentation.AttendanceText);
        Assert.Equal("Not entered", presentation.PreSoldsText);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("hi-IN")]
    public void HistoryConverter_FormatsStoredCounts_InEnUs(string culture)
    {
        var converter = OperationalCountTextConverter.Instance;
        var host = CultureInfo.GetCultureInfo(culture);

        Assert.Equal("Projected attendance: 38,500", converter.Convert(38500, typeof(string), "Projected attendance", host));
        Assert.Equal("Pre-solds: 21,240", converter.Convert(21240, typeof(string), "Pre-solds", host));
        Assert.Equal("Pre-solds: 0", converter.Convert(0, typeof(string), "Pre-solds", host));
        Assert.Equal("Projected attendance: not recorded", converter.Convert(null, typeof(string), "Projected attendance", host));
    }

    [Fact]
    public void HistoryMarkup_RendersCountsThroughTheSharedConverter()
    {
        var markup = Markup("HistoryView");

        Assert.Contains("SelectedDetail.Snapshot.Attendance.ProjectedAttendance, Converter={x:Static converters:OperationalCountTextConverter.Instance}, ConverterParameter='Projected attendance'", markup);
        Assert.Contains("SelectedDetail.Snapshot.Attendance.PreSolds, Converter={x:Static converters:OperationalCountTextConverter.Instance}, ConverterParameter='Pre-solds'", markup);
        Assert.DoesNotContain("StringFormat='Projected attendance: {0}'", markup);
    }

    [Fact]
    public void SyncConflictComparison_GroupsCounts_MissingIsDash()
    {
        var local = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 38500, PreSolds = null };
        var remote = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 21240, PreSolds = 1000 };

        var detail = new DraftConflictDetail(Today, local, remote, true, true, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var rows = detail.BuildFieldComparisons().ToDictionary(r => r.Label);

        Assert.Equal("38,500", rows["Projected Attendance"].LocalValue);
        Assert.Equal("21,240", rows["Projected Attendance"].RemoteValue);
        Assert.Equal("—", rows["Pre-Solds"].LocalValue);
        Assert.Equal("1,000", rows["Pre-Solds"].RemoteValue);
    }

    // ------------------------------------------------------------------ Associate markup

    [Fact]
    public void Toolbar_HasOneAccentAction_CreatePdf_AndCheckOnlineIsQuiet()
    {
        var markup = Markup();
        var header = markup[..markup.IndexOf("x:Name=\"AssociateFeedback\"", StringComparison.Ordinal)];

        var primaries = Regex.Matches(header, "<Button [^>]*Classes=\"primaryAction\"[^>]*>", RegexOptions.Singleline);
        var primary = Assert.Single(primaries);
        Assert.Contains("CreatePdfCommand", primary.Value);

        var checkOnline = Regex.Match(header, "<Button Content=\"Check Online\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("Classes=\"toolbarButton\"", checkOnline);
        Assert.Contains("Rectangle Classes=\"toolbarDivider\"", header);
    }

    [Fact]
    public void Toolbar_KeepsEveryActionInTheSameOrder()
    {
        var markup = Markup();
        string[] order =
        [
            "AutomationProperties.Name=\"Previous day\"", "AutomationProperties.Name=\"Operating date\"", "AutomationProperties.Name=\"Next day\"",
            "AutomationProperties.Name=\"Today\"", "AutomationProperties.Name=\"Check this date online\"", "AutomationProperties.Name=\"Create PDF\"",
            "AutomationProperties.Name=\"Copy for Teams\"", "AutomationProperties.Name=\"More passdown actions\"",
        ];
        var positions = order.Select(o => markup.IndexOf(o, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Contains("x:Name=\"AssociateActionBar\" Orientation=\"Horizontal\"", markup); // WrapPanel still wraps.
    }

    [Fact]
    public void SaveAndOfficialStatus_AreSeparate_LabeledIndicators()
    {
        var markup = Markup();

        Assert.Single(Regex.Matches(markup, "x:Name=\"SaveStatusChip\""));
        Assert.Single(Regex.Matches(markup, Regex.Escape("{Binding SaveStatusText")));
        Assert.Contains("<TextBlock Text=\"Draft\" Classes=\"muted\"", markup);
        Assert.Contains("x:Name=\"AssociateOfficialDataStatusText\"", markup);
        Assert.True(markup.IndexOf("AssociateOfficialDataStatusText", StringComparison.Ordinal) < markup.IndexOf("SaveStatusChip", StringComparison.Ordinal));
        Assert.DoesNotContain("Text=\"Draft: \"", markup); // the old duplicate status strip is gone.
    }

    [Fact]
    public void Feedback_StaysVisibleOnEverySection_NothingCriticalRemoved()
    {
        var markup = Markup();
        var feedbackStart = markup.IndexOf("x:Name=\"AssociateFeedback\"", StringComparison.Ordinal);
        var feedbackEnd = markup.IndexOf("ColumnDefinitions=\"140,*\"", StringComparison.Ordinal);
        var feedback = markup[feedbackStart..feedbackEnd];

        foreach (var binding in new[] { "HasMultiPagePdfWarning", "RefreshStatusMessage", "RefreshResultMessage", "RefreshHadFailures", "PdfExportMessage", "CopyMessage", "LoadErrorMessage", "SyncConflictMessage" })
        {
            Assert.Contains("{Binding " + binding, feedback);
        }

        Assert.Equal(2, Regex.Matches(feedback, "PassdownErrorBrush").Count); // load error + sync conflict keep the error color.
    }

    [Fact]
    public void EveryPre22DBinding_IsStillPresent()
    {
        var markup = Markup();
        string[] required =
        [
            "ParkHours.HeadlineText", "ParkHours.BadgeText", "ParkHours.VerificationText", "ParkHours.StaleNoteText", "ParkHours.WaterparkHeadlineText",
            "ParkHours.NorthGateText", "ParkHours.AvenueText", "NorthGateOverrideText", "AvenueOverrideText", "ResetNorthGateOverrideCommand", "ResetAvenueOverrideCommand",
            "ProjectedAttendanceText", "ProjectedAttendanceError", "PreSoldsText", "PreSoldsError", "EarlyRideTimeStatusText", "OpenErtEditorCommand",
            "DailyFeatureTypeOptions", "SelectedDailyFeatureTypeOption", "DailyFeatureText", "IsErtEditorOpen", "ManualHoursOverrideEnabled", "PrivateBuyoutEnabled",
            "PrivateBuyoutDetails", "ManualHoursWarningText", "PrivateBuyoutHoursWarningText", "IncludeShows", "ShowsFreshnessText", "ShowsStatusText", "ShowRows",
            "SelectAllShowsCommand", "ClearShowsCommand", "StateNote", "TimesText", "Venue", "IncludeDailyAdmission", "IncludeFastLane", "IncludePromotions",
            "IncludeSeasonPasses", "IncludeAddOns", "IncludeHauntedAttractions", "IncludeOperationalNotes", "OpenPriceHistoryCommand", "HistoricalPricingNotice",
            "ProductSections", "Status.StatusText", "Status.DetailText", "OfficialPriceText", "ComparisonPriceText", "PriceQualifier", "BundleNoteText",
            "VerificationNoteText", "ViewPriceHistoryCommand", "DisplayPriceOverrideText", "OverridePlaceholderText", "DisplayPriceOverrideError",
            "ResetToOfficialCommand", "AddCustomProductCommand", "CustomProducts", "OperationalNotesText", "SelectedOperationalNotes",
            "AvailableOperationalNoteSuggestions", "ReplaceCommand", "AddCommand", "OperationalNotesCount", "IsOverviewSectionSelected",
            "IsProductsSectionSelected", "IsNotesSectionSelected",
        ];

        foreach (var binding in required)
        {
            Assert.Contains("{Binding " + binding, markup);
        }
    }

    [Fact]
    public void OfficialAndManualRegions_SayWhereValuesComeFrom_InWords()
    {
        var markup = Markup();

        Assert.True(Regex.Matches(markup, "Classes=\"sourceTag official\"><TextBlock Text=\"Official").Count >= 3); // hours, shows, products
        Assert.True(Regex.Matches(markup, "Classes=\"sourceTag\"><TextBlock Text=\"Entered here\"").Count >= 4); // attendance, daily feature, overrides, notes, custom
        Assert.Contains("<TextBlock Text=\"Your setting\" />", markup); // Early Ride configuration
        Assert.Contains("Selector=\"Border.sourceTag.official TextBlock\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Park hours\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Attendance\"", markup);
        Assert.True(markup.IndexOf("AutomationProperties.Name=\"Park hours\"", StringComparison.Ordinal) < markup.IndexOf("AutomationProperties.Name=\"Attendance\"", StringComparison.Ordinal));
    }

    [Fact]
    public void CountInputs_RegroupOnBlur_AndKeepTheirNames()
    {
        var markup = Markup();

        foreach (var name in new[] { "ProjectedAttendanceTextBox", "PreSoldsTextBox" })
        {
            var box = Regex.Match(markup, $"<TextBox x:Name=\"{name}\"[^>]*/>", RegexOptions.Singleline).Value;
            Assert.Contains("Classes=\"countInput\"", box);
            Assert.Contains("LostFocus=\"CountTextBox_OnLostFocus\"", box);
            Assert.DoesNotContain("UpdateSourceTrigger", box); // same per-keystroke binding (and autosave) as before.
        }

        Assert.Contains("AutomationProperties.Name=\"Projected attendance\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Pre-solds\"", markup);
        Assert.Contains("Selector=\"TextBox.countInput\"", markup);
    }

    [Fact]
    public void MathPicker_KeepsTabOrderAfterTheEditor_AndPreviewHangsOffIt()
    {
        var markup = Markup();
        var editor = markup.IndexOf("<TextBox x:Name=\"DailyFeatureTextBox\"", StringComparison.Ordinal);
        var preview = markup.IndexOf("<controls:DailyFeatureMathPreview", StringComparison.Ordinal);
        var picker = markup.IndexOf("<controls:MathSymbolPicker", StringComparison.Ordinal);
        var typeCombo = markup.IndexOf("AutomationProperties.Name=\"Daily Feature type\"", StringComparison.Ordinal);

        Assert.True(typeCombo < editor && editor < preview && preview < picker);
        Assert.Contains("Classes=\"attachedPreview\"", markup);
        Assert.Contains("Selector=\"controls|DailyFeatureMathPreview.attachedPreview\"", markup);
    }

    [Fact]
    public void ShowsRows_AreCompactTwoLineRows()
    {
        var markup = Markup();
        var template = Regex.Match(markup, "<DataTemplate x:DataType=\"vm:AssociateShowRowViewModel\">.*?</DataTemplate>", RegexOptions.Singleline).Value;

        Assert.Contains("Classes=\"listRow\"", template);
        Assert.Contains("{Binding Name}", template);
        Assert.Contains("{Binding TimesText}", template);
        Assert.Contains("{Binding Venue}", template);
        Assert.Contains("{Binding AccessibleName}", template);
        Assert.True(template.IndexOf("{Binding TimesText}", StringComparison.Ordinal) < template.IndexOf("{Binding Venue}", StringComparison.Ordinal));
        Assert.DoesNotContain("Classes=\"panel\"", template);
        Assert.DoesNotContain("Classes=\"inset\"", template);
    }

    [Fact]
    public void Products_HaveARightAlignedOfficialPriceColumn()
    {
        var markup = Markup();
        var template = Regex.Match(markup, "<DataTemplate x:DataType=\"vm:ProductRowViewModel\">.*?</DataTemplate>", RegexOptions.Singleline).Value;

        Assert.Contains("Text=\"{Binding OfficialPriceText}\" Classes=\"officialPrice\"", template);
        Assert.Contains("ColumnDefinitions=\"Auto,*,120,180\"", template);
        Assert.Contains("Text=\"OFFICIAL\"", markup);
        Assert.Contains("Text=\"DISPLAY PRICE\"", markup);
        Assert.Contains("Selector=\"TextBlock.officialPrice\"", markup);
    }

    [Fact]
    public void NoteRows_AreHairlineRows_NotBoxedCards()
    {
        var markup = Markup();

        foreach (var type in new[] { "AssociateSelectedOperationalNoteRowViewModel", "AssociateOperationalNoteSuggestionRowViewModel" })
        {
            var template = Regex.Match(markup, $"<DataTemplate x:DataType=\"vm:{type}\">.*?</DataTemplate>", RegexOptions.Singleline).Value;
            Assert.Contains("<Border Classes=\"listRow\"", template);
            Assert.DoesNotContain("Classes=\"inset\"", template);
        }
    }

    [Fact]
    public void Rail_KeepsItsThreeSections_WithTheNotesCount()
    {
        var markup = Markup();

        Assert.Equal(3, Regex.Matches(markup, "<Button Classes=\"railItem\"").Count);
        Assert.Contains("{Binding HasOperationalNotesCount}", markup);
        Assert.Contains("AutomationProperties.Name=\"Notes section\"", markup);
        Assert.Contains("StringFormat='Operational notes: {0}'", markup);
    }

    [Fact]
    public void AssociateMarkup_UsesThemeTokensOnly()
    {
        Assert.DoesNotMatch(new Regex("=\"#[0-9A-Fa-f]{3,8}\""), Markup());
    }

    // ------------------------------------------------------------------ Season Price History

    [Fact]
    public void PriceHistoryList_PaintsThePopupSurface()
    {
        var markup = Markup("PriceHistoryView");
        Assert.Matches(new Regex("<ListBox x:Name=\"ProductList\" Classes=\"selectableRows popupList\""), markup);

        var styles = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));
        var popupList = Regex.Match(styles, "<Style Selector=\"ListBox.popupList\">.*?</Style>", RegexOptions.Singleline).Value;
        Assert.Contains("{DynamicResource PassdownPopupSurfaceBrush}", popupList);
        Assert.Contains("ListBox.selectableRows ListBoxItem:selected:pointerover", styles);
        Assert.Contains("ListBox.popupList ListBoxItem", styles);
    }
}
