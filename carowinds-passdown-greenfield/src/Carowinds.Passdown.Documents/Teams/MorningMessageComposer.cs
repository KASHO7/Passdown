using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.DailyFeatureMath;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Teams;

/// <summary>Phase 29: one section of the composer preview — shown whether or not it is enabled, so the user sees what each switch does.</summary>
public sealed record MorningMessagePreviewSection(
    MorningMessageSectionId Id,
    string Name,
    bool Enabled,
    TeamsMessageSection Section,
    string? EmptyText)
{
    /// <summary>The section exactly as it will be pasted (plain text); empty when there is nothing to include.</summary>
    public string PlainText => Section.IsEmpty ? string.Empty : TeamsPlainTextRenderer.RenderSection(Section);

    public IReadOnlyList<string> Notices => Section.Notices;
}

/// <summary>Phase 29: everything the composer shows and copies for one date.</summary>
public sealed record MorningMessageComposition(
    TeamsMessage Message,
    TeamsMessage OdNotes,
    IReadOnlyList<MorningMessagePreviewSection> Preview,
    IReadOnlyList<string> AfterCopyReminders);

/// <summary>
/// Phase 29: assembles the Teams morning message from the open date's Associate passdown (with on-screen
/// edits already applied), the user's saved preferences, and the date's message-only content. Read-only
/// and deterministic: never retrieves, saves, or changes the draft; never invents a value — a field that
/// does not exist is left out and reported in the preview instead.
/// <para>
/// Operations and Daily Feature come from the same <see cref="AssociatePassdownDocumentModel"/> the PDF
/// renders. Ticket Prices come from that builder too, over the workspace with only the section selection
/// replaced by the message's own price sections — so per-product include/exclude, price overrides,
/// "From $x" wording and the Phase 25 pass contract are identical to the PDF, while the message's
/// section choice never changes the PDF's. OD Notes are a separate message and never part of the main one.
/// </para>
/// </summary>
public static class MorningMessageComposer
{
    public const string OdNotesKey = "od-notes";

    /// <summary>Phase 29B: the Operations heading in every Morning Message — never dated.</summary>
    public const string OperationsTitle = "Today's Operations:";

    /// <remarks>
    /// <paramref name="easternToday"/> is kept for existing callers; since Phase 29B the message no longer
    /// depends on it (the Operations heading is always <see cref="OperationsTitle"/>).
    /// </remarks>
    public static MorningMessageComposition Compose(
        AssociateWorkspace workspace,
        MorningMessagePreferences preferences,
        MorningMessageContent content,
        DateOnly easternToday,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(content);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, now);
        var sections = new Dictionary<MorningMessageSectionId, MorningMessagePreviewSection>
        {
            [MorningMessageSectionId.Greeting] = Greeting(preferences),
            [MorningMessageSectionId.Operations] = Operations(workspace, model),
            [MorningMessageSectionId.DapPlacement] = DapPlacement(content),
            [MorningMessageSectionId.Reminders] = Reminders(content, preferences),
            [MorningMessageSectionId.TicketPrices] = TicketPrices(workspace, preferences, content, now),
            [MorningMessageSectionId.BreakPlan] = BreakPlan(content),
            [MorningMessageSectionId.DailyFeature] = DailyFeature(model),
        };

        var preview = preferences.Layout.Sections
            .Select(setting => sections[setting.Id] with { Enabled = setting.Enabled })
            .ToList();

        var message = new TeamsMessage(preview.Where(section => section.Enabled && !section.Section.IsEmpty).Select(section => section.Section).ToList());

        var afterCopy = new List<string>();
        if (preferences.Layout.IsEnabled(MorningMessageSectionId.DapPlacement) && (content.DapImageReminder || !string.IsNullOrWhiteSpace(content.DapImagePath)))
        {
            afterCopy.Add("Attach the DAP / placement image in Teams yourself — copying the message never attaches images.");
        }

        return new MorningMessageComposition(message, OdNotes(content), preview, afterCopy);
    }

    /// <summary>The separate OD Notes message: "OD Notes:" and the user's text, or an empty message when nothing was entered.</summary>
    public static TeamsMessage OdNotes(MorningMessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = TeamsUserText.ToBlocks(content.OdNotes);
        return blocks.Count == 0 ? TeamsMessage.Empty : new TeamsMessage(new TeamsMessageSection(OdNotesKey, "OD Notes:", blocks));
    }

    public static string SectionName(MorningMessageSectionId id) => id switch
    {
        MorningMessageSectionId.Greeting => "Greeting",
        MorningMessageSectionId.Operations => "Today's Operations",
        MorningMessageSectionId.DapPlacement => "DAP / Placement",
        MorningMessageSectionId.Reminders => "Reminders",
        MorningMessageSectionId.TicketPrices => "Ticket Prices",
        MorningMessageSectionId.BreakPlan => "Tentative Break Plan",
        MorningMessageSectionId.DailyFeature => "Daily Feature",
        _ => id.ToString(),
    };

    /// <summary>A price section's heading, exactly as the PDF and the copied message print it.</summary>
    public static string PriceSectionName(ProductDisplaySection section) => PdfCategoryDisplayNames.For(section);

    private static MorningMessagePreviewSection Preview(MorningMessageSectionId id, TeamsMessageSection section, string emptyText) =>
        new(id, SectionName(id), true, section, section.IsEmpty ? emptyText : null);

    private static MorningMessagePreviewSection Greeting(MorningMessagePreferences preferences) =>
        Preview(
            MorningMessageSectionId.Greeting,
            new TeamsMessageSection("greeting", null, TeamsUserText.ToBlocks(preferences.Greeting, detectBullets: false)),
            "No greeting — type one above to include it.");

    private static MorningMessagePreviewSection Operations(AssociateWorkspace workspace, AssociatePassdownDocumentModel model)
    {
        // Phase 29B: always "Today's Operations:" — the message is written for the morning it is sent,
        // so the operating date is never appended, whichever date is open.
        var section = AssociateTeamsMessageBuilder.Operations(model, OperationsTitle);

        // Carolina Harbor's hours, only when the official source published them for an open park day.
        if (model.ParkHours.Status == PdfParkHoursStatus.Open && workspace.ParkHours.HasWaterparkHours
            && section.Blocks[0] is TeamsParagraph paragraph)
        {
            var lines = paragraph.Lines.ToList();
            var insertAt = Math.Min(lines.Count, 1 + lines.Skip(1).TakeWhile(line => line.PlainText.StartsWith("Avenue:", StringComparison.Ordinal) || line.PlainText.StartsWith("North Gate:", StringComparison.Ordinal)).Count());
            lines.Insert(insertAt, TeamsLine.Labeled("Carolina Harbor", $"{FormatTime(workspace.ParkHours.WaterparkOpening)} - {FormatTime(workspace.ParkHours.WaterparkClosing)}"));
            section = section with { Blocks = [new TeamsParagraph(lines)] };
        }

        var notices = new List<string>();
        switch (workspace.ParkHours.Effective.Status)
        {
            case EffectiveHoursStatus.Open or EffectiveHoursStatus.Closed:
                if (workspace.ParkHours.Verification.State is not (AssociateVerificationState.Verified or AssociateVerificationState.Closed)
                    && !workspace.Draft.ManualHoursOverride.Enabled && !workspace.Draft.PrivateBuyout.Enabled)
                {
                    notices.Add("Park hours are from an earlier official check and were not re-verified" + LastVerified(workspace.ParkHours.Verification) + ".");
                }

                break;
            default:
                notices.Add("Park hours have not been checked online for this date — the message says \"Not checked yet\".");
                break;
        }

        if (model.Attendance.ProjectedAttendance is null)
        {
            notices.Add("Projected attendance is not entered, so it is left out.");
        }

        if (model.Attendance.PreSolds is null)
        {
            notices.Add("Pre-solds are not entered, so they are left out.");
        }

        return Preview(MorningMessageSectionId.Operations, section with { Notices = notices }, "Nothing to include.");
    }

    private static MorningMessagePreviewSection DapPlacement(MorningMessageContent content)
    {
        var notices = new List<string>();
        if (content.DapImageReminder || !string.IsNullOrWhiteSpace(content.DapImagePath))
        {
            notices.Add("The image is not part of the copied text. Paste or attach it in Teams separately.");
        }

        var section = new TeamsMessageSection("dap", "DAP / Placement:", TeamsUserText.ToBlocks(content.DapPlacement)) { Notices = notices };
        return Preview(MorningMessageSectionId.DapPlacement, section, "Nothing entered — the section is left out.");
    }

    private static MorningMessagePreviewSection Reminders(MorningMessageContent content, MorningMessagePreferences preferences) =>
        Preview(
            MorningMessageSectionId.Reminders,
            new TeamsMessageSection("reminders", "Reminders:", TeamsUserText.ToBlocks(content.Reminders, everyLineIsBullet: preferences.RemindersAsBullets)),
            "No reminders entered — the section is left out.");

    private static MorningMessagePreviewSection BreakPlan(MorningMessageContent content) =>
        Preview(
            MorningMessageSectionId.BreakPlan,
            new TeamsMessageSection("break-plan", "Tentative Break Plan:", TeamsUserText.ToBlocks(content.BreakPlan)),
            "No break plan entered — the section is left out.");

    private static MorningMessagePreviewSection TicketPrices(AssociateWorkspace workspace, MorningMessagePreferences preferences, MorningMessageContent content, DateTimeOffset now)
    {
        var selection = workspace.Draft.SectionSelection with
        {
            IncludeDailyAdmission = preferences.PriceSections.Contains(ProductDisplaySection.DailyAdmission),
            IncludeFastLane = preferences.PriceSections.Contains(ProductDisplaySection.FastLane),
            IncludePromotions = preferences.PriceSections.Contains(ProductDisplaySection.Promotions),
            IncludeSeasonPasses = preferences.PriceSections.Contains(ProductDisplaySection.SeasonPasses),
            IncludeAddOns = preferences.PriceSections.Contains(ProductDisplaySection.AddOns),
            IncludeHauntedAttractions = preferences.PriceSections.Contains(ProductDisplaySection.SCarowinds),
        };

        var priceModel = AssociatePassdownDocumentModelBuilder.Build(workspace with { Draft = workspace.Draft with { SectionSelection = selection } }, now);
        var pricing = AssociateTeamsMessageBuilder.Pricing(priceModel, includeCustomItems: false);
        var blocks = pricing.Blocks.ToList();
        var notices = new List<string>();

        // Freshness: the printed rows are the official last-known-good prices; any that were not
        // re-verified are said so, in the preview and in the message, never presented as newly checked.
        var priced = priceModel.ProductCategories.Where(category => category.Section is not null)
            .SelectMany(category => category.Products)
            .Where(row => !string.IsNullOrWhiteSpace(row.EffectivePriceText))
            .Select(row => row.DisplayName)
            .ToHashSet(StringComparer.Ordinal);

        var unverified = ProductDisplaySectionResolver.ResolveAll(workspace.Products.Where(product =>
                !product.IsCustom && product.IncludeOnPdf && product.IsPassdownExportable && priced.Contains(product.DisplayName)))
            .Where(entry => preferences.PriceSections.Contains(entry.Section))
            .Select(entry => entry.Product)
            .Where(product => product.Verification.State != AssociateVerificationState.Verified)
            .ToList();

        foreach (var product in unverified)
        {
            notices.Add($"{product.DisplayName}: {VerificationWord(product.Verification.State)}{LastVerified(product.Verification)} — shown as the last confirmed official price.");
        }

        if (blocks.Count > 0 && unverified.Count > 0)
        {
            blocks.Add(new TeamsParagraph(new TeamsLine(TeamsRun.Emphasis("Some prices could not be re-verified today; they are the last confirmed official prices."))));
        }

        if (blocks.Count == 0)
        {
            notices.Add(preferences.PriceSections.Count == 0
                ? "No price sections are selected for the message."
                : "No official prices are available for the selected sections on this date.");
        }

        var userNote = TeamsUserText.ToBlocks(content.PriceNote, detectBullets: false);
        if (blocks.Count > 0 && userNote.Count > 0)
        {
            // The user's own words, italic and after the official prices, so they are never mistaken for one.
            blocks.AddRange(userNote.Select(Italicize));
        }

        var section = pricing with { Blocks = blocks, Notices = notices };
        return Preview(MorningMessageSectionId.TicketPrices, section, "No prices to include — the section is left out.");
    }

    private static MorningMessagePreviewSection DailyFeature(AssociatePassdownDocumentModel model)
    {
        var feature = model.DailyFeature;
        var notices = new List<string>();
        if (feature.HasContent && DailyFeatureMathText.MayContainMath(feature.Text))
        {
            notices.Add("This Daily Feature contains a formula. Teams shows it exactly as typed (for example \\( x^2 \\)).");
        }

        var blocks = feature.HasContent ? TeamsUserText.ToBlocks(feature.Text, detectBullets: false) : [];
        var section = new TeamsMessageSection("daily-feature", (feature.TypeLabel ?? "Daily Feature") + ":", blocks) { Notices = notices };
        return Preview(MorningMessageSectionId.DailyFeature, section, "No Daily Feature entered for this date — the section is left out.");
    }

    private static TeamsBlock Italicize(TeamsBlock block) => block switch
    {
        TeamsParagraph paragraph => new TeamsParagraph(paragraph.Lines.Select(ItalicizeLine).ToList()),
        TeamsBulletList list => new TeamsBulletList(list.Items.Select(ItalicizeLine).ToList()),
        _ => block,
    };

    private static TeamsLine ItalicizeLine(TeamsLine line) => new(line.Runs.Select(run => run with { Italic = true }).ToList());

    private static string VerificationWord(AssociateVerificationState state) => state switch
    {
        AssociateVerificationState.Stale => "not re-verified (stale)",
        AssociateVerificationState.Unavailable => "official source unavailable",
        AssociateVerificationState.Error => "last check failed",
        AssociateVerificationState.NotPublished => "not published for this date",
        AssociateVerificationState.NotCheckedYet => "not checked yet",
        _ => "not verified",
    };

    private static string LastVerified(AssociateVerificationInfo verification) =>
        verification.LastVerifiedAt is { } at
            ? " (last verified " + Application.Time.EasternTimeZone.ConvertFromUtc(at).ToString("MMM d, h:mm tt", PresentationCulture.UnitedStates) + ")"
            : string.Empty;

    private static string FormatTime(TimeOnly? time) => time?.ToString("h:mm tt", PresentationCulture.UnitedStates) ?? "-";
}
