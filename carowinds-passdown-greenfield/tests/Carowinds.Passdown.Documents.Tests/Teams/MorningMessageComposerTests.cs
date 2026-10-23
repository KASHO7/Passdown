using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Teams;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Teams;

/// <summary>
/// Phase 29: <see cref="MorningMessageComposer"/> — the full Teams morning message from a real workspace,
/// through the same document-model builder the PDF uses.
/// </summary>
public sealed class MorningMessageComposerTests
{
    private static readonly DateOnly Date = new(2026, 10, 22);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 11, 0, 0, TimeSpan.Zero);
    private static readonly AssociateVerificationInfo Verified = new(AssociateVerificationState.Verified, Now, null, null);
    private static readonly AssociateVerificationInfo Stale = new(AssociateVerificationState.Stale, new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(3), null);

    private static AssociateProduct Product(string name, SourceCategory category, decimal price, AssociateVerificationInfo? verification = null, bool include = true) => new(
        $"test|{category}|{name}", name, category, IsCustom: false, price, OfficialDisplayPrice: null, ComparisonPriceAmount: null, PriceQualifier: null,
        DateSpecific: true, ApplicableDate: Date, verification ?? Verified, include, DisplayPriceOverride: null, PriceKind.ExactDated);

    private static List<AssociateProduct> Products(AssociateVerificationInfo? fastLaneVerification = null) =>
    [
        Product("Park Ticket", SourceCategory.DatedAdmissionTickets, 59m),
        Product("Fast Lane Reserve", SourceCategory.DatedFastLane, 65m, fastLaneVerification),
        Product("Gold Season Pass", SourceCategory.SeasonPasses, 129m),
    ];

    private static AssociateWorkspace Workspace(
        IReadOnlyList<AssociateProduct>? products = null,
        int? projected = 18000,
        int? preSolds = 1000,
        string? dailyFeature = "Why did the coaster blush? It saw the loop's bottom.",
        bool hoursChecked = true,
        AssociateVerificationInfo? hoursVerification = null,
        bool waterpark = false,
        PassdownSectionSelection? pdfSelection = null)
    {
        var effective = hoursChecked
            ? ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(20, 0), false, null, null, false, null, null)
            : ParkHoursCalculator.Calculate(false, null, null, false, null, null, false, null, null);

        var draft = AssociateDraftPayload.CreateDefault() with
        {
            ProjectedAttendance = projected,
            PreSolds = preSolds,
            DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
            DailyFeatureText = dailyFeature,
            OperationalNotes = ["Private note — never copied unless chosen."],
            SectionSelection = pdfSelection ?? PassdownSectionSelection.Default,
            CustomProducts = [new CustomProductEntry("c1", "Staff Lunch Voucher", 5m, null, true)],
        };

        return new AssociateWorkspace(
            Date, draft,
            new AssociateParkHoursView(
                effective, hoursVerification ?? (hoursChecked ? Verified : AssociateVerificationInfo.NotCheckedYet), false,
                hoursChecked ? new TimeOnly(10, 0) : null, hoursChecked ? new TimeOnly(20, 0) : null,
                waterpark ? new TimeOnly(11, 0) : null, waterpark ? new TimeOnly(19, 0) : null),
            products ?? Products(), [], AssociateSaveStatus.NeverSaved,
            EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(9, 30), ["Fury 325"], null));
    }

    private static MorningMessageContent Content(
        string reminders = "- Hydrate often\n- Check radios",
        string breakPlan = "Avery   10:30  Gate 1\nJordan  11:00  Gate 2",
        string dap = "",
        string odNotes = "Ride 4 down until noon.",
        string priceNote = "") =>
        MorningMessageContent.Empty with { Reminders = reminders, BreakPlan = breakPlan, DapPlacement = dap, OdNotes = odNotes, PriceNote = priceNote };

    private static MorningMessageComposition Compose(AssociateWorkspace? workspace = null, MorningMessagePreferences? preferences = null, MorningMessageContent? content = null, DateOnly? today = null) =>
        MorningMessageComposer.Compose(workspace ?? Workspace(), preferences ?? MorningMessagePreferences.Default, content ?? Content(), today ?? Date, Now);

    private static string Plain(MorningMessageComposition composition) => TeamsPlainTextRenderer.Render(composition.Message);

    [Fact]
    public void CompleteMessage_DefaultOrder_ExactPlainText()
    {
        Assert.Equal(
            "Good Morning!\r\n\r\n"
            + "Today's Operations:\r\nPark Hours: 10:00 AM - 8:00 PM\r\nAvenue: 9:15 AM\r\nNorth Gate: 9:30 AM\r\nProjected Attendance: 18,000\r\nPre-solds: 1,000\r\nERT: Fury 325\r\n\r\n"
            + "Reminders:\r\n• Hydrate often\r\n• Check radios\r\n\r\n"
            + "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\n\r\n"
            + "Tentative Break Plan:\r\nAvery   10:30  Gate 1\r\nJordan  11:00  Gate 2\r\n\r\n"
            + "Question of the Day:\r\nWhy did the coaster blush? It saw the loop's bottom.",
            Plain(Compose()));
    }

    [Fact]
    public void DefaultOrder_IsThePreferredWorkflow_DailyFeatureLast()
    {
        Assert.Equal(
            [
                MorningMessageSectionId.Greeting, MorningMessageSectionId.Operations, MorningMessageSectionId.DapPlacement,
                MorningMessageSectionId.Reminders, MorningMessageSectionId.TicketPrices, MorningMessageSectionId.BreakPlan,
                MorningMessageSectionId.DailyFeature,
            ],
            Compose().Preview.Select(section => section.Id));

        Assert.Equal("daily-feature", Compose(content: Content() with { DapPlacement = "Placement: see chart" }).Message.Sections[^1].Key);
    }

    [Fact]
    public void CustomOrder_IsFollowed()
    {
        var layout = MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.BreakPlan).MoveUp(MorningMessageSectionId.BreakPlan);
        var composition = Compose(preferences: MorningMessagePreferences.Default with { Layout = layout });

        Assert.Equal(["greeting", "operations", "break-plan", "reminders", "pricing", "daily-feature"], composition.Message.Sections.Select(section => section.Key));
    }

    [Fact]
    public void DisabledSections_AreLeftOut_ButStillPreviewed()
    {
        var layout = MorningMessageLayout.Default.WithEnabled(MorningMessageSectionId.TicketPrices, false).WithEnabled(MorningMessageSectionId.Greeting, false);
        var composition = Compose(preferences: MorningMessagePreferences.Default with { Layout = layout });

        Assert.DoesNotContain("Ticket Prices", Plain(composition));
        Assert.DoesNotContain("Good Morning", Plain(composition));
        Assert.False(composition.Preview.Single(p => p.Id == MorningMessageSectionId.TicketPrices).Enabled);
        Assert.Contains("Park Ticket", composition.Preview.Single(p => p.Id == MorningMessageSectionId.TicketPrices).PlainText);
    }

    [Fact]
    public void MissingSourceFields_AreOmitted_AndReportedInThePreview()
    {
        var composition = Compose(Workspace(projected: null, preSolds: null, dailyFeature: null, hoursChecked: false), content: MorningMessageContent.Empty);
        var plain = Plain(composition);

        Assert.Contains("Park Hours: Not checked yet", plain);
        Assert.DoesNotContain("Projected Attendance", plain);
        Assert.DoesNotContain("Pre-solds", plain);
        Assert.DoesNotContain("Question of the Day", plain);
        Assert.DoesNotContain("Reminders:", plain);
        Assert.DoesNotContain("Tentative Break Plan:", plain);

        var operations = composition.Preview.Single(p => p.Id == MorningMessageSectionId.Operations);
        Assert.Contains(operations.Notices, notice => notice.Contains("not been checked online", StringComparison.Ordinal));
        Assert.Contains(operations.Notices, notice => notice.Contains("Projected attendance", StringComparison.Ordinal));
        Assert.Contains(operations.Notices, notice => notice.Contains("Pre-solds", StringComparison.Ordinal));
        Assert.NotNull(composition.Preview.Single(p => p.Id == MorningMessageSectionId.DailyFeature).EmptyText);
        Assert.NotNull(composition.Preview.Single(p => p.Id == MorningMessageSectionId.Reminders).EmptyText);
    }

    [Fact]
    public void StaleTicketPrice_IsWarned_AndMessageSaysLastConfirmed_NotNewlyVerified()
    {
        var composition = Compose(Workspace(Products(fastLaneVerification: Stale)));
        var prices = composition.Preview.Single(p => p.Id == MorningMessageSectionId.TicketPrices);

        var notice = Assert.Single(prices.Notices);
        Assert.StartsWith("Fast Lane Reserve: not re-verified (stale) (last verified Oct 19, 10:00 AM)", notice, StringComparison.Ordinal);
        Assert.Contains("Fast Lane Reserve: $65.00", Plain(composition));
        Assert.Contains("Some prices could not be re-verified today; they are the last confirmed official prices.", Plain(composition));
        Assert.Contains("<i style=\"font-style:italic\">Some prices could not be re-verified today", TeamsHtmlRenderer.Render(composition.Message), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifiedTicketPrices_HaveNoWarning()
    {
        var composition = Compose();

        Assert.Empty(composition.Preview.Single(p => p.Id == MorningMessageSectionId.TicketPrices).Notices);
        Assert.DoesNotContain("re-verified", Plain(composition));
    }

    [Fact]
    public void TicketPrices_SeasonPassesAndCustomItemsExcludedByDefault_SelectionDoesNotChangeThePdf()
    {
        var workspace = Workspace(pdfSelection: PassdownSectionSelection.AllIncluded);
        var plain = Plain(Compose(workspace));

        Assert.DoesNotContain("Gold Season Pass", plain);
        Assert.DoesNotContain("Staff Lunch Voucher", plain);

        // Opting in adds Season Passes; the workspace (and so the PDF) is not modified.
        var withPasses = MorningMessagePreferences.Default with { PriceSections = [ProductDisplaySection.DailyAdmission, ProductDisplaySection.SeasonPasses] };
        var optedIn = Plain(Compose(workspace, withPasses));
        Assert.Contains("Gold Season Pass: $129.00", optedIn);
        Assert.DoesNotContain("Fast Lane", optedIn);
        Assert.Equal(PassdownSectionSelection.AllIncluded, workspace.Draft.SectionSelection);
    }

    [Fact]
    public void TicketPrices_FollowPerProductPdfDeselection()
    {
        var products = Products();
        products[1] = products[1] with { IncludeOnPdf = false };

        Assert.DoesNotContain("Fast Lane Reserve", Plain(Compose(Workspace(products))));
    }

    [Fact]
    public void TicketPrices_NoSelectedSections_ExplainsWhy()
    {
        var composition = Compose(preferences: MorningMessagePreferences.Default with { PriceSections = [] });

        Assert.DoesNotContain("Ticket Prices", Plain(composition));
        Assert.Contains("No price sections are selected", Assert.Single(composition.Preview.Single(p => p.Id == MorningMessageSectionId.TicketPrices).Notices), StringComparison.Ordinal);
    }

    [Fact]
    public void PriceNote_IsItalic_AfterOfficialPrices()
    {
        var composition = Compose(content: Content(priceNote: "Ask about group rates at Guest Services."));
        var html = TeamsHtmlRenderer.Render(composition.Message);

        Assert.Contains("Fast Lane Reserve: $65.00\r\n\r\nAsk about group rates at Guest Services.", Plain(composition));
        Assert.Contains("<p><i style=\"font-style:italic\">Ask about group rates at Guest Services.</i></p>", html);
    }

    [Fact]
    public void MultilineReminders_KeepParagraphsAndLineBreaks()
    {
        var plain = Plain(Compose(content: Content(reminders: "Wear sunscreen.\nRadio check at 9:45.\n\n- Smile\n- Hydrate")));

        Assert.Contains("Reminders:\r\nWear sunscreen.\r\nRadio check at 9:45.\r\n\r\n• Smile\r\n• Hydrate", plain);
    }

    [Fact]
    public void RemindersAsBulletsPreference_BulletsEveryLine()
    {
        var plain = Plain(Compose(preferences: MorningMessagePreferences.Default with { RemindersAsBullets = true }, content: Content(reminders: "Hydrate\nSmile")));

        Assert.Contains("Reminders:\r\n• Hydrate\r\n• Smile", plain);
    }

    [Fact]
    public void BreakPlan_KeepsOrderAndSpacing_InBothFormats()
    {
        var composition = Compose();
        var html = TeamsHtmlRenderer.Render(composition.Message);

        Assert.Contains("Tentative Break Plan:\r\nAvery   10:30  Gate 1\r\nJordan  11:00  Gate 2", Plain(composition));
        Assert.Contains("Avery &nbsp;&nbsp;10:30 &nbsp;Gate 1<br>Jordan &nbsp;11:00 &nbsp;Gate 2", html);
    }

    [Fact]
    public void OdNotes_NeverInTheMainMessage_AndCopiedSeparately()
    {
        var composition = Compose();

        Assert.DoesNotContain("OD Notes", Plain(composition));
        Assert.DoesNotContain("Ride 4 down", Plain(composition));
        Assert.Equal("OD Notes:\r\nRide 4 down until noon.", TeamsPlainTextRenderer.Render(composition.OdNotes));
        Assert.True(MorningMessageComposer.OdNotes(MorningMessageContent.Empty).IsEmpty);
    }

    [Fact]
    public void PrivateOperationalNotes_AreNeverIncludedAutomatically()
    {
        Assert.DoesNotContain("Private note", Plain(Compose()));
    }

    [Fact]
    public void Greeting_IsTheSavedPreference_AndMayBeMultiline()
    {
        var plain = Plain(Compose(preferences: MorningMessagePreferences.Default with { Greeting = "Gooooood Morning!\nHappy Thursday" }));

        Assert.StartsWith("Gooooood Morning!\r\nHappy Thursday\r\n\r\nToday's Operations:", plain, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(-30)]
    public void OperationsTitle_IsAlwaysTodaysOperations_NeverDated(int daysFromOpenDate)
    {
        // Phase 29B: the dated "Operations for Thursday, October 22:" heading is gone, whichever date is open.
        var plain = Plain(Compose(today: Date.AddDays(daysFromOpenDate)));

        Assert.Contains("\r\nToday's Operations:\r\nPark Hours:", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations for", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Thursday", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("October", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void CarolinaHarborHours_IncludedOnlyWhenPublished()
    {
        Assert.Contains("North Gate: 9:30 AM\r\nCarolina Harbor: 11:00 AM - 7:00 PM\r\nProjected", Plain(Compose(Workspace(waterpark: true))));
        Assert.DoesNotContain("Carolina Harbor", Plain(Compose()));
    }

    [Fact]
    public void DapImage_IsNeverPartOfTheText_ReminderInstead()
    {
        var composition = Compose(content: Content(dap: "Placement: rotate at 2 PM") with { DapImageReminder = true });

        Assert.Contains("DAP / Placement:\r\nPlacement: rotate at 2 PM", Plain(composition));
        Assert.DoesNotContain("<img", TeamsHtmlRenderer.Render(composition.Message), StringComparison.Ordinal);
        Assert.Single(composition.AfterCopyReminders);
    }

    [Fact]
    public void DailyFeature_WithFormula_IsCopiedAsTyped_WithPreviewNotice()
    {
        var composition = Compose(Workspace(dailyFeature: "What is \\(2^{10}\\)?"));

        Assert.EndsWith("Question of the Day:\r\nWhat is \\(2^{10}\\)?", Plain(composition), StringComparison.Ordinal);
        Assert.Single(composition.Preview.Single(p => p.Id == MorningMessageSectionId.DailyFeature).Notices);
    }

    [Fact]
    public void FormattedAndPlain_SameWordsSameOrder()
    {
        var composition = Compose(Workspace(Products(Stale)), content: Content(priceNote: "Note & <b>"));

        Assert.Equal(
            TeamsRenderingTests.Words(Plain(composition)),
            TeamsRenderingTests.Words(TeamsRenderingTests.HtmlToText(TeamsHtmlRenderer.Render(composition.Message))));
    }

    [Theory]
    [InlineData(AssociateTeamsCopyKind.OperationsSummary)]
    [InlineData(AssociateTeamsCopyKind.TodaysPricing)]
    [InlineData(AssociateTeamsCopyKind.OperationsAndPricing)]
    public void ExistingCopyActions_PlainIsUnchanged_FormattedHasSameWords(AssociateTeamsCopyKind kind)
    {
        var model = AssociatePassdownDocumentModelBuilder.Build(Workspace(), Now);
        var message = Assert.IsType<TeamsMessage>(AssociateTeamsTextFormatter.BuildMessage(model, kind));

        Assert.Equal(AssociateTeamsTextFormatter.Format(model, kind), TeamsPlainTextRenderer.Render(message));
        Assert.Equal(
            TeamsRenderingTests.Words(TeamsPlainTextRenderer.Render(message)),
            TeamsRenderingTests.Words(TeamsRenderingTests.HtmlToText(TeamsHtmlRenderer.Render(message))));
    }

    [Fact]
    public void Compose_DoesNotModifyTheWorkspaceOrDraft()
    {
        var workspace = Workspace();
        var draftBefore = workspace.Draft;

        _ = Compose(workspace, content: Content(reminders: "x", breakPlan: "y", odNotes: "z"));

        Assert.Same(draftBefore, workspace.Draft);
        Assert.Equal(PassdownSectionSelection.Default, workspace.Draft.SectionSelection);
    }

    // ---- Phase 29B: undated heading, formatted/plain equivalence, bold semantics, CF_HTML ----

    private const string Bold = "<b style=\"font-weight:bold\">";

    private static string Html(MorningMessageComposition composition) => TeamsHtmlRenderer.Render(composition.Message);

    [Fact]
    public void Phase29B_CompleteMessage_ExactFormattedHtml_BoldHeadingsAndLabels_BulletedReminders()
    {
        Assert.Equal(
            "<p>Good Morning!</p>"
            + "<p>" + Bold + "Today&#39;s Operations:</b><br>" + Bold + "Park Hours:</b> 10:00 AM - 8:00 PM<br>" + Bold + "Avenue:</b> 9:15 AM<br>"
            + Bold + "North Gate:</b> 9:30 AM<br>" + Bold + "Projected Attendance:</b> 18,000<br>" + Bold + "Pre-solds:</b> 1,000<br>" + Bold + "ERT:</b> Fury 325</p>"
            + "<p>" + Bold + "Reminders:</b></p><ul><li>Hydrate often</li><li>Check radios</li></ul>"
            + "<p>" + Bold + "Ticket Prices:</b></p><p>" + Bold + "Daily Admission</b><br>Park Ticket: $59.00</p><p>" + Bold + "Fast Lane</b><br>Fast Lane Reserve: $65.00</p>"
            + "<p>" + Bold + "Tentative Break Plan:</b><br>Avery &nbsp;&nbsp;10:30 &nbsp;Gate 1<br>Jordan &nbsp;11:00 &nbsp;Gate 2</p>"
            + "<p>" + Bold + "Question of the Day:</b><br>Why did the coaster blush? It saw the loop&#39;s bottom.</p>",
            Html(Compose()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public void Phase29B_ExactlyOneTodaysOperationsHeading_InPlainHtmlAndRegeneratedOutput(int daysFromOpenDate)
    {
        var first = Compose(today: Date.AddDays(daysFromOpenDate));
        var regenerated = Compose(today: Date.AddDays(daysFromOpenDate));

        foreach (var composition in new[] { first, regenerated })
        {
            var plain = Plain(composition);
            var html = Html(composition);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(plain, "Today's Operations:"));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Today&#39;s Operations:"));
            Assert.Contains(Bold + "Today&#39;s Operations:</b>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Operations for", plain + html, StringComparison.Ordinal);
        }

        Assert.Equal(Plain(first), Plain(regenerated));
        Assert.Equal(Html(first), Html(regenerated));
        Assert.Equal("Today's Operations:", Assert.Single(first.Message.Sections, section => section.Key == "operations").Title);
    }

    [Fact]
    public void Phase29B_FormattedAndPlain_SameWordsSameOrder_EveryHeadingBold()
    {
        var composition = Compose(content: Content(reminders: "Safety First\n- Hydrate & rest\n- Close <before> 9", priceNote: "Ask about combos."));
        var plain = Plain(composition);
        var html = Html(composition);

        Assert.Equal(TeamsRenderingTests.Words(plain), TeamsRenderingTests.Words(TeamsRenderingTests.HtmlToText(html)));
        foreach (var title in composition.Message.Sections.Select(section => section.Title).OfType<string>())
        {
            Assert.Contains(Bold + TeamsHtmlRenderer.Encode(title) + "</b>", html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("<strong", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<em", html, StringComparison.Ordinal);
        Assert.Contains("<i style=\"font-style:italic\">Ask about combos.</i>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_Reminders_AreRealBullets_InBothForms()
    {
        var composition = Compose(content: Content(reminders: "- Hydrate often\n- Check radios\n- Smile"));

        Assert.Contains("Reminders:\r\n• Hydrate often\r\n• Check radios\r\n• Smile", Plain(composition), StringComparison.Ordinal);
        Assert.Contains("<ul><li>Hydrate often</li><li>Check radios</li><li>Smile</li></ul>", Html(composition), StringComparison.Ordinal);

        var preview = string.Concat(TeamsPreviewRenderer.Render(composition.Message, TeamsCopyMode.Formatted).Select(span => span.Text));
        Assert.Contains("Reminders:\n• Hydrate often\n• Check radios\n• Smile", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_UserText_IsEscaped_InHtmlAndNeverStyledByTheUser()
    {
        var html = Html(Compose(content: Content(reminders: "<b style=\"color:red\">x</b> & <script>alert(1)</script>")));

        Assert.Contains("&lt;b style=&quot;color:red&quot;&gt;x&lt;/b&gt; &amp; &lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("color:red\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_OdNotes_ExcludedFromMainMessage_PlainHtmlAndPreview()
    {
        var composition = Compose(content: Content(odNotes: "SECRET OD note"));

        Assert.DoesNotContain("SECRET OD note", Plain(composition), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET OD note", Html(composition), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET OD note", string.Concat(TeamsPreviewRenderer.Render(composition.Message, TeamsCopyMode.Formatted).Select(span => span.Text)), StringComparison.Ordinal);
        Assert.Equal("<p>" + Bold + "OD Notes:</b><br>SECRET OD note</p>", TeamsHtmlRenderer.Render(composition.OdNotes));
    }

    [Fact]
    public void Phase29B_CfHtml_RoundTrips_TheStyledFragment_WithValidOffsets()
    {
        var fragment = Html(Compose(content: Content(reminders: "- Café — 10 min early")));
        var payload = CfHtml.Build(fragment);
        var parsed = CfHtml.Parse(payload);

        Assert.Equal(fragment, parsed.Fragment);
        Assert.NotEqual(0xEF, payload[0]);
        Assert.NotEqual(0, payload[^1]);
        Assert.Contains(Bold + "Today&#39;s Operations:</b>", parsed.Html, StringComparison.Ordinal);
        Assert.Contains("<li>Café — 10 min early</li>", parsed.Fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase29B_PreviewSpans_PlainModeIsThePlainCopy_FormattedModeAddsOnlyStyling()
    {
        var composition = Compose(content: Content(priceNote: "Ask about combos."));
        var plain = Plain(composition).Replace("\r\n", "\n", StringComparison.Ordinal);

        var plainSpans = TeamsPreviewRenderer.Render(composition.Message, TeamsCopyMode.PlainText);
        var formattedSpans = TeamsPreviewRenderer.Render(composition.Message, TeamsCopyMode.Formatted);

        Assert.Equal(plain, Assert.Single(plainSpans).Text);
        Assert.Equal(plain, string.Concat(formattedSpans.Select(span => span.Text)));
        Assert.Equal(
            ["Today's Operations:", "Park Hours:", "Avenue:", "North Gate:", "Projected Attendance:", "Pre-solds:", "ERT:", "Reminders:", "Ticket Prices:", "Daily Admission", "Fast Lane", "Tentative Break Plan:", "Question of the Day:"],
            formattedSpans.Where(span => span.Bold).Select(span => span.Text));
        Assert.Equal("Ask about combos.", Assert.Single(formattedSpans, span => span.Italic).Text);
    }
}
