using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Maps a composed <c>Application.Associate.AssociateWorkspace</c> into the print-ready
/// <see cref="AssociatePassdownDocumentModel"/> (spec Part A). Every value here is either passed
/// straight through from the workspace or turned into a display string — no business rule is
/// (re)computed: hours precedence already ran in <see cref="ParkHoursCalculator"/>, and the
/// official-vs-override effective price already ran in <see cref="AssociateProduct.EffectiveDisplayPrice"/>.
/// This class only decides how those already-final values print.
/// </summary>
public static class AssociatePassdownDocumentModelBuilder
{
    private const string CustomProductCategoryName = "Custom / Internal Items";

    public static AssociatePassdownDocumentModel Build(AssociateWorkspace workspace, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var productCategories = BuildProductCategories(workspace);

        return new AssociatePassdownDocumentModel(
            workspace.OperatingDate,
            FormatOperatingDate(workspace.OperatingDate),
            BuildParkHours(workspace),
            new PdfAttendance(workspace.Draft.ProjectedAttendance, workspace.Draft.PreSolds),
            BuildEarlyRideTime(workspace.EarlyRideTime),
            BuildDailyFeature(workspace.Draft),
            BuildOperationalNotes(workspace.Draft),
            productCategories,
            HasUnverifiedProductPricing(workspace),
            FormatTimestamp(generatedAt));
    }

    private static string FormatOperatingDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates);

    private static PdfParkHours BuildParkHours(AssociateWorkspace workspace)
    {
        var view = workspace.ParkHours;
        var effective = view.Effective;

        var status = effective.Status switch
        {
            EffectiveHoursStatus.Open => PdfParkHoursStatus.Open,
            EffectiveHoursStatus.Closed => PdfParkHoursStatus.Closed,
            _ => PdfParkHoursStatus.NotCheckedYet,
        };

        var headline = effective.Status switch
        {
            EffectiveHoursStatus.Open => $"{FormatTime(effective.Opening)} - {FormatTime(effective.Closing)}",
            EffectiveHoursStatus.Closed => "Park is closed",
            _ => "Hours not checked yet",
        };

        string? badge = effective.Status == EffectiveHoursStatus.Open
            ? effective switch
            {
                { IsPrivateBuyoutApplied: true } => "Private Buyout",
                { IsManualOverrideApplied: true } => "Manual Override",
                _ => null,
            }
            : null;

        var verificationText = BuildVerificationText(view.Verification, effective);

        string? buyoutDetails = effective.IsPrivateBuyoutApplied
            ? workspace.Draft.PrivateBuyout.Details
            : null;

        return new PdfParkHours(
            status,
            headline,
            badge,
            FormatTime(effective.NorthGateOpening),
            FormatTime(effective.AvenueOpening),
            verificationText,
            string.IsNullOrWhiteSpace(buyoutDetails) ? null : buyoutDetails.Trim());
    }

    /// <summary>
    /// Phase 13D Part Y: the printed/frozen Associate Passdown no longer carries retrieval-
    /// verification metadata ("Last verified ...", staleness) at all — that diagnostic signal remains
    /// fully visible in-app (Dashboard Data Status, Associate's own on-screen Park Hours panel, and
    /// the Operations Calendar all read the same <see cref="AssociateVerificationInfo"/> this method
    /// used to format), so the printed handoff stays operationally focused (spec Part AS). Kept as an
    /// always-null method (rather than deleted, with <see cref="PdfParkHours.VerificationText"/> and
    /// <see cref="Application.Finalization.FinalizedParkHoursSnapshot.VerificationText"/> removed) so
    /// neither the print model's nor the finalized-snapshot's shape changes — no schema migration.
    /// </summary>
    private static string? BuildVerificationText(AssociateVerificationInfo verification, EffectiveHours effective) => null;

    /// <summary>Phase 17B: a straight passthrough of the workspace's already-resolved <see cref="EffectiveErt"/> — Documents formats the start time (the resolver's own convention, <see cref="FormatTime"/>) but decides nothing about precedence/configuration state.</summary>
    private static PdfEarlyRideTime BuildEarlyRideTime(EffectiveErt earlyRideTime) => new(
        earlyRideTime.Kind,
        earlyRideTime.Source,
        earlyRideTime.StartTime is { } startTime ? FormatTime(startTime) : null,
        earlyRideTime.Rides,
        string.IsNullOrWhiteSpace(earlyRideTime.Note) ? null : earlyRideTime.Note.Trim());

    /// <summary>
    /// Phase 11D Part AB/L: the Operational Notes panel's content is the associate's own manual
    /// daily notes followed by any persistent notes the supervisor explicitly selected for this date
    /// — both already-frozen display strings, combined into the same list
    /// <see cref="AssociatePassdownDocumentModel.OperationalNotes"/> already was before Phase 11D, so
    /// no new document-model field or finalized-snapshot field is needed (snapshot schema stays at
    /// 1). Manual notes lines print unlabeled exactly as before; a selected persistent note's line is
    /// prefixed with "Persistent" (plus "Important" when applicable) so the two sources stay visually
    /// distinguishable within the one shared bulleted panel (<c>AssociatePassdownPdfGenerator.ComposeNotes</c>).
    /// Gated by the same <see cref="PassdownSectionSelection.IncludeOperationalNotes"/> flag as
    /// before — hiding the Operational Notes section hides both manual and selected content.
    /// </summary>
    private static IReadOnlyList<string> BuildOperationalNotes(AssociateDraftPayload draft)
    {
        if (!draft.SectionSelection.IncludeOperationalNotes)
        {
            return [];
        }

        var lines = new List<string>(draft.OperationalNotes);
        lines.AddRange(draft.SelectedOperationalNotes.Select(FormatSelectedOperationalNote));
        return lines;
    }

    private static string FormatSelectedOperationalNote(AssociateOperationalNoteSelection selection)
    {
        var prefix = selection.Priority == OperationalNotePriority.Important
            ? "Persistent (Important)"
            : "Persistent";

        var text = string.IsNullOrWhiteSpace(selection.Body)
            ? selection.Title
            : $"{selection.Title} — {selection.Body}";

        return $"{prefix}: {text}";
    }

    private static PdfDailyFeature BuildDailyFeature(AssociateDraftPayload draft)
    {
        var label = draft.DailyFeatureType switch
        {
            DailyFeatureType.QuestionOfTheDay => "Question of the Day",
            DailyFeatureType.UselessFactOfTheDay => "Useless Fact of the Day",
            DailyFeatureType.QuoteOfTheDay => "Quote of the Day",
            _ => null,
        };

        return new PdfDailyFeature(label, string.IsNullOrWhiteSpace(draft.DailyFeatureText) ? null : draft.DailyFeatureText.Trim());
    }

    /// <summary>
    /// Spec Phase 8A/8B, revised Phase 15A: every product-bearing <see cref="ProductDisplaySection"/>
    /// maps to its own <see cref="PassdownSectionSelection"/> flag — keyed by the resolved display
    /// section rather than the raw <see cref="SourceCategory"/>, so a product folded into a
    /// more-specific section (e.g. Military/First Responder into Daily Admission) is governed by that
    /// section's own toggle, not a separate one it no longer visibly belongs to.
    /// </summary>
    private static bool IsSectionIncluded(ProductDisplaySection section, PassdownSectionSelection selection) => section switch
    {
        ProductDisplaySection.DailyAdmission => selection.IncludeDailyAdmission,
        ProductDisplaySection.FastLane => selection.IncludeFastLane,
        ProductDisplaySection.Promotions => selection.IncludePromotions,
        ProductDisplaySection.SeasonPasses => selection.IncludeSeasonPasses,
        ProductDisplaySection.AddOns => selection.IncludeAddOns,
        ProductDisplaySection.SCarowinds => selection.IncludeHauntedAttractions,
        _ => true,
    };

    /// <summary>
    /// Spec Phase 15A §5: every included product resolves through the one authoritative
    /// <see cref="ProductDisplaySectionResolver"/> rather than being grouped by its raw retrieval
    /// <see cref="SourceCategory"/> — this is what stops the same real-world product (Bring-A-Friend
    /// Duo, Military/First Responder, an SCarowinds-specific promotion) from printing under two
    /// differently-named headings. A product whose category has no dedicated section (Bring-A-Friend
    /// Duo's own dated category, the new dated ordinary-Bring-A-Friend category) resolves to
    /// <see langword="null"/> and is skipped here — it is already represented by its Promotions
    /// catalog counterpart.
    /// </summary>
    private static IReadOnlyList<PdfProductCategory> BuildProductCategories(AssociateWorkspace workspace)
    {
        var categories = new List<PdfProductCategory>();

        var officialBySection = ProductDisplaySectionResolver.ResolveAll(workspace.Products.Where(product => !product.IsCustom && product.IncludeOnPdf))
            .Where(entry => IsSectionIncluded(entry.Section, workspace.Draft.SectionSelection))
            .GroupBy(entry => entry.Section, entry => entry.Product)
            .OrderBy(group => (int)group.Key);

        foreach (var group in officialBySection)
        {
            var rows = group.Select(BuildOfficialRow).ToList();
            if (rows.Count > 0)
            {
                categories.Add(new PdfProductCategory(PdfCategoryDisplayNames.For(group.Key), group.Key, TierFor(group.Key), rows));
            }
        }

        var customRows = workspace.Draft.CustomProducts
            .Where(custom => custom.IncludeOnPdf)
            .Select(BuildCustomRow)
            .ToList();

        if (customRows.Count > 0)
        {
            categories.Add(new PdfProductCategory(CustomProductCategoryName, Section: null, PdfProductSectionTier.Reference, customRows));
        }

        return categories;
    }

    /// <summary>
    /// Spec Parts 3/4/5's tier assignment, revised Phase 15A for <see cref="ProductDisplaySection"/>:
    /// Daily Admission/Fast Lane are what an associate needs to remember quickly (Key Selling) — this
    /// now includes Military/First Responder rows, which carry a real comparison ("$80 Gate Price")
    /// that Key Selling's tile rendering already supports identically to the old Promotional tier, so
    /// folding them into Daily Admission loses nothing (docs/phase13-hardening.md Phase 15A section).
    /// Promotions gets its own callout treatment; everything else (Season Passes, Add-Ons, SCarowinds)
    /// is reference-oriented and rendered compactly.
    /// </summary>
    private static PdfProductSectionTier TierFor(ProductDisplaySection section) => section switch
    {
        ProductDisplaySection.DailyAdmission => PdfProductSectionTier.KeySelling,
        ProductDisplaySection.FastLane => PdfProductSectionTier.KeySelling,
        ProductDisplaySection.Promotions => PdfProductSectionTier.Promotional,
        _ => PdfProductSectionTier.Reference,
    };

    /// <summary>
    /// Phase 17D §8/9: the PDF prints the operational current price only — no comparison ("was $x")
    /// price and no ecommerce qualifier wording ("Only", "Online Price", "Sale Price"). The one
    /// qualifier semantics that materially changes the number's meaning, "Starting From"
    /// (<see cref="PriceKind.StartingFrom"/>), is preserved by folding it into the price text itself
    /// ("From $37") rather than a separate qualifier line (spec §9's "Preferred row: Military / First
    /// Responder   From $37"). Comparison pricing and raw qualifier text remain fully available in the
    /// Products screen/product history (<see cref="AssociateProduct.ComparisonPriceAmount"/>/
    /// <see cref="AssociateProduct.PriceQualifier"/> themselves are untouched) — only the PDF's own
    /// row stops carrying them.
    /// </summary>
    private static PdfProductRow BuildOfficialRow(AssociateProduct product) => new(
        product.DisplayName,
        FormatPrice(product.PriceKind, product.EffectiveDisplayPrice),
        ComparisonPriceText: null,
        Qualifier: null,
        Notes: BundleNote(product));

    private static string? FormatPrice(PriceKind kind, decimal? amount)
    {
        var formatted = FormatMoney(amount);
        return formatted is null ? null : kind == PriceKind.StartingFrom ? $"From {formatted}" : formatted;
    }

    /// <summary>
    /// Phase 13C Part I/W: a source-described bundle (e.g. "Two Friends Just $50") carries its
    /// per-person breakdown here — a derived value, explicitly labeled as such — rather than
    /// silently replacing <see cref="PdfProductRow.EffectivePriceText"/> (the bundle total) or
    /// <see cref="PdfProductRow.ComparisonPriceText"/> (a "was" price, a different concept). No
    /// <see cref="PdfProductRow"/>/<see cref="Application.Finalization.FinalizedPassdownSnapshot"/>
    /// schema change was needed: this reuses the existing free-text <c>Notes</c> field.
    /// </summary>
    private static string? BundleNote(AssociateProduct product) =>
        product is { BundleQuantity: { } quantity, PerPersonPriceAmount: { } perPerson }
            ? $"{quantity} for {FormatMoney(product.BundleTotalPriceAmount)} — {FormatMoney(perPerson)} per person"
            : null;

    private static PdfProductRow BuildCustomRow(CustomProductEntry custom) => new(
        custom.DisplayName,
        FormatMoney(custom.Price),
        ComparisonPriceText: null,
        Qualifier: null,
        string.IsNullOrWhiteSpace(custom.Notes) ? null : custom.Notes.Trim());

    /// <summary>Spec Part D: flags the footer note only when an included official product's price is not currently backed by a successful verification — never per-row clutter.</summary>
    private static bool HasUnverifiedProductPricing(AssociateWorkspace workspace) =>
        workspace.Products.Any(product =>
            !product.IsCustom
            && product.IncludeOnPdf
            && product.Verification.State != AssociateVerificationState.Verified);

    private static string? FormatMoney(decimal? amount) => amount is { } value ? PresentationCulture.FormatUsd(value) : null;

    private static string FormatTime(TimeOnly? time) => time?.ToString("h:mm tt", PresentationCulture.UnitedStates) ?? "-";

    /// <summary>"Sep 15 • 1:52 PM" (Phase 17D §19 — replaces the old "Generated Sep 15 at 1:52 PM"; no app name, no "Generated" verb, kept small and quiet by every renderer's footer).</summary>
    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d", PresentationCulture.UnitedStates)
        + " • " + value.ToLocalTime().ToString("h:mm tt", PresentationCulture.UnitedStates);
}
