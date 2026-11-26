using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35B (docs/phase35b-passdown-workspace-redesign.md): the Associate Passdown workspace — compact toolbar with
/// one primary Create PDF and one Actions menu, Overview / Products &amp; Pricing / Shows / Notes section tabs, the
/// Overview as one sheet of four hairline-separated groups, and the real PDF preview beside it. Markup contracts and
/// headless construction only (no real Window — those live in the run-alone Phase33PreviewWindowTests partial), so
/// bound text values are never asserted here (thread-dependent in this assembly); binding strings are.
/// </summary>
public class Phase35BPassdownWorkspaceTests
{
    private static readonly DateOnly FixedDate = new(2026, 8, 21);

    private static string Markup => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml"));

    private static string SharedStyles => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));

    private static string Tokens => File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

    /// <summary>The element named <paramref name="name"/> through its closing tag (same-tag nesting counted).</summary>
    private static string Element(string markup, string name)
    {
        var nameIndex = markup.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"{name} not found");
        var start = markup.LastIndexOf('<', nameIndex);
        var tag = Regex.Match(markup[(start + 1)..], @"^[\w:]+").Value;
        var depth = 0;
        foreach (Match m in new Regex($@"<(/?){Regex.Escape(tag)}[\s>/]").Matches(markup, start))
        {
            var selfClosing = m.Groups[1].Value.Length == 0 && markup.IndexOf('>', m.Index) is var close && markup[close - 1] == '/';
            depth += m.Groups[1].Value.Length == 0 ? (selfClosing ? 0 : 1) : -1;
            if (depth == 0)
            {
                return markup[start..(markup.IndexOf('>', m.Index) + 1)];
            }
        }

        throw new InvalidOperationException($"{name} has no closing tag");
    }

    private static int IndexOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{value}' not found");
        return index;
    }

    // ------------------------------------------------------------------ toolbar

    [Fact]
    public void Toolbar_TitleDateAndDraftStatusWrapLeft_CreatePdfAndActionsStayRight()
    {
        var toolbar = Element(Markup, "PassdownToolbar");
        var left = Element(toolbar, "AssociateActionBar");
        var right = Element(toolbar, "PassdownActions");

        Assert.Contains("<WrapPanel Grid.Column=\"0\" x:Name=\"AssociateActionBar\"", toolbar);
        Assert.Contains("<StackPanel Grid.Column=\"1\" x:Name=\"PassdownActions\"", toolbar);
        Assert.Contains("<Grid ColumnDefinitions=\"*,Auto\"", toolbar); // the actions column is never squeezed off
        Assert.True(IndexOf(left, "Text=\"Associate Passdown\"") < IndexOf(left, "x:Name=\"DateNavigator\""));
        Assert.True(IndexOf(left, "x:Name=\"DateNavigator\"") < IndexOf(left, "x:Name=\"DraftStatus\""));
        Assert.True(IndexOf(right, "x:Name=\"TogglePreviewButton\"") < IndexOf(right, "x:Name=\"CreatePdfButton\""));
        Assert.True(IndexOf(right, "x:Name=\"CreatePdfButton\"") < IndexOf(right, "x:Name=\"PassdownActionsButton\""));

        var primaries = Regex.Matches(toolbar, "<Button [^>]*Classes=\"primaryAction\"[^>]*>", RegexOptions.Singleline);
        Assert.Contains("Command=\"{Binding CreatePdfCommand}\"", Assert.Single(primaries).Value);
    }

    [Fact]
    public void Toolbar_DraftStatus_IsQuiet_Labeled_AndNeverClaimsSync()
    {
        var status = Element(Markup, "DraftStatus");

        Assert.Contains("<TextBlock Text=\"Draft\" Classes=\"muted\"", status);
        Assert.Contains("x:Name=\"SaveStatusChip\"", status);
        Assert.Contains("{Binding SaveStatusText", status);
        Assert.Contains("x:Name=\"RetrySaveButton\"", status);
        Assert.Contains("IsVisible=\"{Binding IsSaveFailed}\"", status);
        Assert.DoesNotContain("Sync", status, StringComparison.OrdinalIgnoreCase); // a local draft is never shown as synced
        Assert.DoesNotContain("Verified", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toolbar_ShowPreview_AppearsOnlyWhileThePreviewIsHidden()
    {
        var toggle = Element(Markup, "TogglePreviewButton");

        Assert.Contains("Command=\"{Binding Preview.TogglePreviewCommand}\"", toggle);
        Assert.Contains("Content=\"{Binding Preview.TogglePreviewLabel}\"", toggle);
        Assert.Contains("<MultiBinding Converter=\"{x:Static BoolConverters.And}\">", toggle);
        Assert.Contains("<Binding Path=\"Preview.IsAvailable\" />", toggle);
        Assert.Contains("<Binding Path=\"!Preview.IsPreviewPaneShown\" />", toggle);
    }

    [Fact]
    public void ActionsMenu_IsFlat_AndKeepsEveryEarlierToolbarAndMoreAction()
    {
        var actions = Element(Markup, "PassdownActionsButton");
        var menu = actions[IndexOf(actions, "<MenuFlyout")..];

        // Flat: nested MenuItems fill an ItemCollection during XAML load (dispatcher access; breaks headless construction).
        Assert.DoesNotContain("</MenuItem>", menu);

        string[] commands =
        [
            "Preview.PrintCommand", "Preview.SavePdfCommand", "CopyForTeamsCommand", "CopyForTeamsCommand", "CopyForTeamsCommand",
            "Preview.TogglePreviewCommand", "SelectTemplateCommand", "SelectTemplateCommand", "SelectTemplateCommand",
            "CheckOnlineCommand", "OpenPriceHistoryCommand",
        ];
        var bound = Regex.Matches(menu, "Command=\"\\{Binding ([\\w.]+)\\}\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(commands, bound);

        Assert.Contains("InputGesture=\"Ctrl+P\"", menu);
        Assert.Contains("CommandParameter=\"{x:Static preferences:AssociatePassdownTemplate.Classic}\"", menu);
        Assert.Contains("CommandParameter=\"{x:Static preferences:AssociatePassdownTemplate.Compact}\"", menu);
        Assert.Contains("CommandParameter=\"{x:Static preferences:AssociatePassdownTemplate.Modern}\"", menu);
        Assert.Equal(3, Regex.Matches(menu, "ToggleType=\"Radio\" GroupName=\"PdfStyle\"").Count);
        Assert.Contains("Placement=\"BottomEdgeAlignedRight\"", menu);
    }

    [Fact]
    public void CtrlP_PrintsWithTheSameCommandAsActionsAndThePreview()
    {
        Assert.Contains("<KeyBinding Gesture=\"Ctrl+P\" Command=\"{Binding Preview.PrintCommand}\" />", Markup);
        Assert.Single(Regex.Matches(Markup, "<KeyBinding Gesture=\"Ctrl\\+P\"")); // one binding; the menu item only displays the gesture
    }

    [Fact]
    public async Task ActionsMenu_HeadlessView_EveryItemIsNamed_AndTheMenuButtonIsKeyboardReachable()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)));
        await viewModel.PendingLoad;
        var view = new AssociateView { DataContext = viewModel };

        var button = view.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Passdown actions");
        Assert.True(button.Focusable && button.IsTabStop);
        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetHelpText(button)));

        var items = Assert.IsType<MenuFlyout>(button.Flyout).Items.OfType<MenuItem>().ToList();
        Assert.Equal(11, items.Count); // Print, Save PDF, 3 × Copy, Show/Hide Preview, 3 × PDF style, Check Online, Price History
        Assert.All(items.Where(i => i.Name != "TogglePreviewMenuItem"), i => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(i))));
        Assert.Contains("AutomationProperties.Name=\"{Binding Preview.TogglePreviewLabel}\"", Element(Markup, "TogglePreviewMenuItem"));
        Assert.Equal(4, Assert.IsType<MenuFlyout>(button.Flyout).Items.OfType<Separator>().Count()); // output · copy · preview · style · online
    }

    // ------------------------------------------------------------------ section tabs

    [Fact]
    public void SectionTabs_SitOverTheEditorOnly_AndSwitchTheExistingSections()
    {
        var markup = Markup;
        var editor = Element(markup, "AssociateEditorPane");
        var tabs = Element(editor, "SectionTabs");

        Assert.Contains("x:Name=\"AssociateEditorPane\" RowDefinitions=\"Auto,*\"", editor);
        Assert.True(IndexOf(editor, "x:Name=\"SectionTabsBar\"") < IndexOf(editor, "x:Name=\"OverviewSectionScroll\""));
        Assert.DoesNotContain("SectionTabs", Element(markup, "PreviewPaneHost"));

        string[] order = ["ShowOverviewSectionCommand", "ShowProductsSectionCommand", "ShowShowsSectionCommand", "ShowNotesSectionCommand"];
        Assert.Equal(order, Regex.Matches(tabs, "Command=\"\\{Binding (Show\\w+SectionCommand)\\}\"").Select(m => m.Groups[1].Value));
        Assert.Contains("Content=\"Products &amp; Pricing\"", tabs);
        Assert.Contains("AutomationProperties.Name=\"Passdown sections\"", tabs);

        // Each section is its own ScrollViewer, kept alive and toggled by visibility (scroll and edits survive a switch).
        foreach (var flag in new[] { "IsOverviewSectionSelected", "IsProductsSectionSelected", "IsShowsSectionSelected", "IsNotesSectionSelected" })
        {
            Assert.Matches(new Regex($"<ScrollViewer [^>]*Grid\\.Row=\"1\"[^>]*Classes=\"sectionScroll\"\\s+IsVisible=\"\\{{Binding {flag}\\}}\""), editor);
        }

        Assert.Equal(4, Regex.Matches(editor, "Classes=\"sectionScroll\"").Count);
    }

    [Fact]
    public void SectionTabs_ActiveStateIsShapeWeightAndScreenReaderStatus_NotColourAlone()
    {
        var markup = Markup;
        var active = markup[IndexOf(markup, "<Style Selector=\"Button.sectionTab.active\">")..];
        active = active[..IndexOf(active, "</Style>")];

        Assert.Contains("<Setter Property=\"BorderBrush\" Value=\"{DynamicResource PassdownAccentForegroundBrush}\" />", active); // underline
        Assert.Contains("<Setter Property=\"FontWeight\" Value=\"SemiBold\" />", active);
        Assert.Contains("<Setter Property=\"AutomationProperties.ItemStatus\" Value=\"Current section\" />", active);
        Assert.Contains("<Style Selector=\"Button.sectionTab:pressed\">", markup); // no press-scale jitter
    }

    [Fact]
    public void WorkspaceMeta_NamesThePdfStyle_AndKeepsOfficialFreshnessQuiet()
    {
        var meta = Element(Markup, "WorkspaceMeta");

        Assert.Contains("Text=\"{Binding SelectedTemplateOption.Label, StringFormat='{}{0} PDF style'}\"", meta);
        Assert.Contains("x:Name=\"AssociateOfficialDataStatusText\"", meta);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", meta);
        Assert.Contains("ToolTip.Tip=\"{Binding OfficialDataStatus.Presentation.DetailText}\"", meta);
        Assert.Contains("IsVisible=\"{Binding IsAutoCheckingDate}\"", meta);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", meta);
    }

    // ------------------------------------------------------------------ Overview sheet

    [Fact]
    public void Overview_IsOneSheet_WithFourGroupsInTheApprovedOrder_AndNoNestedPanels()
    {
        var sheet = Element(Markup, "OverviewSheet");

        string[] headings = ["Park Hours &amp; Gates", "Attendance &amp; Early Ride Time", "Daily Feature", "Include in Passdown"];
        var positions = headings.Select(h => IndexOf(sheet, $"<TextBlock Text=\"{h}\" Classes=\"overviewTitle\" />")).ToList();
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Equal(4, Regex.Matches(sheet, "Classes=\"overviewSection( last)?\"").Count);
        Assert.Contains("x:Name=\"IncludeInPassdownPanel\" Classes=\"overviewSection last\"", sheet);
        Assert.DoesNotContain("Classes=\"panel", sheet); // one sheet, hairline-separated groups — no cards inside it
        Assert.DoesNotContain("heroPanel", sheet);
    }

    [Fact]
    public void ParkHours_KeepsTheOfficialHeadline_GateOverrides_AndTheEventSource()
    {
        var markup = Markup;
        var hours = markup[IndexOf(markup, "AutomationProperties.Name=\"Park hours\"")..IndexOf(markup, "Text=\"Attendance &amp; Early Ride Time\"")];

        foreach (var binding in new[]
                 {
                     "ParkHours.HeadlineText", "ParkHours.BadgeText", "ParkHours.VerificationText", "ParkHours.StaleNoteText",
                     "ParkHours.WaterparkHeadlineText", "ParkHours.NorthGateText", "ParkHours.AvenueText", "NorthGateIsOverridden", "AvenueIsOverridden",
                     "NorthGateOverrideText, UpdateSourceTrigger=LostFocus", "AvenueOverrideText, UpdateSourceTrigger=LostFocus",
                     "ResetNorthGateOverrideCommand", "ResetAvenueOverrideCommand", "NorthGateOverrideError", "AvenueOverrideError",
                     "ManualHoursOverrideEnabled", "PrivateBuyoutEnabled", "SeasonContext.HeadingText", "SeasonContext.Events", "SeasonContext.FreshnessText",
                 })
        {
            Assert.Contains("{Binding " + binding, hours);
        }

        Assert.Contains("<Binding Path=\"ParkHours.BadgeKind\" />", hours); // status dot (shape + text badge, never colour alone)
        Assert.Contains("<Border Classes=\"sourceTag official\"><TextBlock Text=\"Official\" /></Border>", hours);
        Assert.Contains("<Border Classes=\"sourceTag\"><TextBlock Text=\"Entered here\" /></Border>", hours); // overrides are manual
        Assert.Contains("x:Name=\"OfficialEventsBox\" Classes=\"sourceBox\" IsVisible=\"{Binding HasSeasonContext}\"", hours);
        Assert.Contains("<TextBlock Text=\"Main Park Hours\"", hours);
        Assert.Contains("<TextBlock Text=\"North Gate\"", hours);
        Assert.Contains("<TextBlock Text=\"Avenue Entrance\"", hours);
        Assert.DoesNotContain("<TextBox Text=\"{Binding ParkHours.HeadlineText", hours); // official hours are never an input
    }

    [Fact]
    public void ManualOverrideAndBuyoutFields_AppearOnlyWithTheirCheckbox_AndCommitOnBlurOrEnter()
    {
        var manual = Element(Markup, "ManualHoursFields");
        var buyout = Element(Markup, "PrivateBuyoutFields");

        Assert.Contains("IsVisible=\"{Binding ManualHoursOverrideEnabled}\"", manual);
        Assert.Contains("IsVisible=\"{Binding PrivateBuyoutEnabled}\"", buyout);
        foreach (var (group, bindings) in new[] { (manual, new[] { "ManualOpeningTimeText", "ManualClosingTimeText" }), (buyout, new[] { "PrivateBuyoutOpeningTimeText", "PrivateBuyoutClosingTimeText" }) })
        {
            foreach (var binding in bindings)
            {
                var box = Regex.Match(group, $"<TextBox Text=\"\\{{Binding {binding}, UpdateSourceTrigger=LostFocus\\}}\"[^>]*>", RegexOptions.Singleline).Value;
                Assert.Contains("KeyDown=\"TimeTextBox_OnKeyDown\"", box);
                Assert.Contains("AutomationProperties.Name=", box);
            }
        }

        Assert.Contains("{Binding PrivateBuyoutDetails}", buyout);
        Assert.Contains("{Binding ManualOpeningTimeError}", manual);
        Assert.Contains("{Binding PrivateBuyoutClosingTimeError}", buyout);
        Assert.Contains("{Binding ManualHoursWarningText}", Element(Markup, "HoursOverridesGroup"));
        Assert.Contains("{Binding PrivateBuyoutHoursWarningText}", Element(Markup, "HoursOverridesGroup"));
    }

    [Fact]
    public void AttendanceAndEarlyRide_KeepCountRegrouping_TheEditCommand_AndTheFullEditor()
    {
        var markup = Markup;
        var group = markup[IndexOf(markup, "Text=\"Attendance &amp; Early Ride Time\"")..IndexOf(markup, "<TextBlock Text=\"Daily Feature\" Classes=\"overviewTitle\" />")];

        Assert.Contains("x:Name=\"ProjectedAttendanceTextBox\" Classes=\"countInput\"", group);
        Assert.Contains("x:Name=\"PreSoldsTextBox\" Classes=\"countInput\"", group);
        Assert.Equal(2, Regex.Matches(group, "LostFocus=\"CountTextBox_OnLostFocus\"").Count);
        var edit = Regex.Match(group, "<Button Grid.Column=\"1\" Content=\"Edit Attractions\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("Command=\"{Binding OpenErtEditorCommand}\"", edit);
        Assert.Contains("AutomationProperties.Name=\"Edit Attractions for Early Ride Time\"", edit); // visible label is in the name
        Assert.Contains("{Binding EarlyRideTimeStatusText}", group);
        Assert.Contains("<TextBlock Text=\"Your setting\" />", group);
        Assert.Contains("IsVisible=\"{Binding IsErtEditorOpen}\"", group);
        foreach (var command in new[] { "SaveMonthlyErtCommand", "SaveOverrideCommand", "RemoveOverrideCommand", "CancelErtEditorCommand" })
        {
            Assert.Contains($"Command=\"{{Binding {command}}}\"", group);
        }
    }

    [Fact]
    public void DailyFeature_KeepsTheTypeSelector_MathPicker_TabOrder_AndRenderedPreview()
    {
        var markup = Markup;
        var group = markup[IndexOf(markup, "<TextBlock Text=\"Daily Feature\" Classes=\"overviewTitle\" />")..IndexOf(markup, "x:Name=\"IncludeInPassdownPanel\"")];

        Assert.Contains("ItemsSource=\"{Binding DailyFeatureTypeOptions}\"", group);
        Assert.Contains("SelectedItem=\"{Binding SelectedDailyFeatureTypeOption}\"", group);
        Assert.Contains("AutomationProperties.Name=\"Daily Feature type\"", group);
        Assert.Contains("<TextBlock Text=\"Feature type\"", group); // the visible label matches the accessible name
        Assert.Contains("spell:SpellCheck.ExcludeMath=\"True\"", group);
        Assert.Contains("<controls:DailyFeatureMathPreview Classes=\"attachedPreview\"", group);
        Assert.Contains("Source=\"{Binding #DailyFeatureTextBox.Text}\"", group);
        Assert.True(IndexOf(group, "x:Name=\"DailyFeatureTextBox\"") < IndexOf(group, "<controls:MathSymbolPicker"), "Tab order: type → text → Math");
        Assert.Contains("Target=\"{Binding #DailyFeatureTextBox}\"", group);
    }

    [Fact]
    public void IncludeInPassdown_KeepsEveryChoiceInOrder_AsSectionSwitchesOnly()
    {
        var include = Element(Markup, "IncludeInPassdownPanel");

        string[] order = ["IncludeDailyAdmission", "IncludeFastLane", "IncludePromotions", "IncludeSeasonPasses", "IncludeAddOns", "IncludeHauntedAttractions", "IncludeOperationalNotes", "IncludeShows"];
        Assert.Equal(order, Regex.Matches(include, "<CheckBox [^>]*IsChecked=\"\\{Binding (Include\\w+)\\}\"", RegexOptions.Singleline).Select(m => m.Groups[1].Value));
        Assert.Contains("IsVisible=\"{Binding ShowHauntedOption}\"", include);
        Assert.Contains("{Binding HauntedSeasonNote}", include);
        Assert.Contains("always print", include);
        Assert.DoesNotContain("ShowRows", include); // which shows print is chosen on the Shows section
        Assert.DoesNotContain("SelectedOperationalNotes", include); // persistent-note selection stays on Notes
        Assert.DoesNotContain("IncludeOnPdf", include); // product selection stays on Products
        Assert.Equal(8, Regex.Matches(include, "AutomationProperties.Name=\"Include [^\"]+ in the Passdown\"").Count);
    }

    [Fact]
    public void EveryInteractiveControl_OnTheToolbarTabsAndOverview_HasAnAccessibleName()
    {
        var markup = Markup;
        var start = IndexOf(markup, "x:Name=\"PassdownToolbar\"");
        var end = IndexOf(markup, "x:Name=\"OverviewSheet\"") + Element(markup, "OverviewSheet").Length;
        var region = markup[start..end];

        var controls = Regex.Matches(region, "<(Button|CheckBox|TextBox|ComboBox|MenuItem|controls:MathSymbolPicker)\\s[^>]*>", RegexOptions.Singleline);
        Assert.True(controls.Count > 40);
        foreach (Match control in controls)
        {
            // The Math picker names itself in code (MathSymbolPicker); everything else declares its name here.
            if (control.Groups[1].Value == "controls:MathSymbolPicker")
            {
                continue;
            }

            Assert.True(control.Value.Contains("AutomationProperties.Name=", StringComparison.Ordinal), $"no accessible name: {control.Value[..Math.Min(120, control.Value.Length)]}");
        }
    }

    // ------------------------------------------------------------------ divider, tokens, motion

    [Fact]
    public void Divider_IsAVisibleFocusableTrack_WithADecorativeGrip()
    {
        var markup = Markup;
        var splitter = Regex.Match(markup, "<GridSplitter [^>]*x:Name=\"PreviewSplitter\"[^>]*>", RegexOptions.Singleline).Value;

        Assert.Contains("Classes=\"previewDivider\"", splitter);
        Assert.Contains("Focusable=\"True\"", splitter);
        Assert.Contains("IsVisible=\"{Binding Preview.IsSplitLayout}\"", splitter);
        Assert.Contains("AutomationProperties.Name=\"Resize the PDF preview\"", splitter);
        var grip = Regex.Match(markup, "<Border Grid.Column=\"1\" x:Name=\"PreviewSplitterGrip\"[^>]*>", RegexOptions.Singleline).Value;
        Assert.Contains("IsHitTestVisible=\"False\"", grip);
        Assert.Contains("IsVisible=\"{Binding Preview.IsSplitLayout}\"", grip);
        Assert.Contains("<Style Selector=\"GridSplitter.previewDivider:pointerover\">", markup);
        Assert.Contains("<Style Selector=\"GridSplitter.previewDivider:focus-visible\">", markup);
    }

    [Fact]
    public void AssociateMarkup_UsesOnlyDefinedThemeTokens_InBothThemes()
    {
        var keys = Regex.Matches(Markup, "\\{DynamicResource (Passdown\\w+)\\}").Select(m => m.Groups[1].Value).Distinct().ToList();
        var light = Tokens[IndexOf(Tokens, "<ResourceDictionary x:Key=\"Light\">")..IndexOf(Tokens, "<ResourceDictionary x:Key=\"Dark\">")];
        var dark = Tokens[IndexOf(Tokens, "<ResourceDictionary x:Key=\"Dark\">")..];
        var invariant = Tokens[..IndexOf(Tokens, "<ResourceDictionary.ThemeDictionaries>")];

        foreach (var key in keys)
        {
            var defined = $"x:Key=\"{key}\"";
            Assert.True(invariant.Contains(defined, StringComparison.Ordinal) || (light.Contains(defined, StringComparison.Ordinal) && dark.Contains(defined, StringComparison.Ordinal)),
                $"{key} must be theme-invariant or defined for both Light and Dark");
        }
    }

    [Fact]
    public void Motion_ReusesThePhase35AScope_AndTheWorkspaceDeclaresNoTransitionsOfItsOwn()
    {
        var shared = SharedStyles;
        var scoped = shared[IndexOf(shared, "<Style Selector=\"Window.motion Button.sectionTab /template/ ContentPresenter#PART_ContentPresenter\">")..];
        scoped = scoped[..IndexOf(scoped, "</Style>")];

        Assert.Contains("<BrushTransition Property=\"BorderBrush\" Duration=\"{StaticResource PassdownMotionFast}\"", scoped);
        Assert.DoesNotContain("Transitions", Markup); // nothing animates unless the main window carries the motion class
        Assert.DoesNotContain("Animation", Markup);
        Assert.Single(Regex.Matches(shared, "Button\\.sectionTab")); // the shared styles touch the tabs only under Window.motion
    }

    // ------------------------------------------------------------------ view model contracts the workspace relies on

    [Fact]
    public async Task SwitchingSections_NeverRegeneratesThePreview_OrChecksOnline()
    {
        var service = new FakeAssociatePassdownService();
        var export = new ContentEchoPdfExportService();
        var debounce = new ManualDebounceScheduler();
        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            autosaveDelay: new ManualDebounceScheduler().Delay,
            pdfExportService: export,
            templatePreferenceStore: new FakeAssociatePassdownTemplatePreferenceStore(),
            pdfRasterizer: new FakePdfRasterizer(),
            pdfPrintService: new FakePdfPrintService(),
            pdfPreviewLayoutStore: new InMemoryPdfPreviewLayoutPreferenceStore(),
            pdfPageImageFactory: new FakePdfPageImageFactory(),
            previewWorkScheduler: new InlinePreviewWorkScheduler(),
            previewDelay: debounce.Delay);
        await viewModel.PendingLoad;
        await viewModel.Preview.PendingPreferenceLoad;
        viewModel.Preview.UpdateViewport(500, 700, 1.0);
        await viewModel.Preview.EnsureLatestDocumentAsync();
        var generations = viewModel.Preview.GenerationCount;
        var quietWindows = debounce.ScheduleCount;
        var calls = export.Calls.Count;
        var version = viewModel.Preview.ContentVersion;

        foreach (var command in new[] { viewModel.ShowProductsSectionCommand, viewModel.ShowShowsSectionCommand, viewModel.ShowNotesSectionCommand, viewModel.ShowOverviewSectionCommand })
        {
            command.Execute(null);
        }

        Assert.Equal(AssociateSection.Overview, viewModel.SelectedSection);
        Assert.Equal(generations, viewModel.Preview.GenerationCount);
        Assert.Equal(calls, export.Calls.Count);
        Assert.Equal(version, viewModel.Preview.ContentVersion);
        Assert.Empty(service.CheckOnlineCalls);
        Assert.Equal(quietWindows, debounce.ScheduleCount); // no refresh is even scheduled by a section switch
    }

    [Fact]
    public async Task DateArrows_LoadOnlyLocally_AndMoveTheWorkspaceToTheNewDate()
    {
        var service = new FakeAssociatePassdownService();
        var viewModel = new AssociateViewModel(service, new FakeClock(new DateTimeOffset(FixedDate.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero)),
            autosaveDelay: new ManualDebounceScheduler().Delay);
        await viewModel.PendingLoad;

        viewModel.NextDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(FixedDate.AddDays(1), viewModel.SelectedDate);
        Assert.Equal(FixedDate.AddDays(1), viewModel.Workspace?.OperatingDate);

        viewModel.PreviousDayCommand.Execute(null);
        viewModel.PreviousDayCommand.Execute(null);
        await viewModel.PendingLoad;
        Assert.Equal(FixedDate.AddDays(-1), viewModel.SelectedDate);
        Assert.Equal(FixedDate.AddDays(-1), viewModel.Workspace?.OperatingDate);
        Assert.Empty(service.CheckOnlineCalls); // changing the date never retrieves online
        Assert.Contains(FixedDate.AddDays(1), service.LoadCalls);
    }
}
