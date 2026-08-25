using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;
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
            BuildEarlyRideTime(workspace.Draft.EarlyRideTime),
            BuildDailyFeature(workspace.Draft),
            workspace.Draft.SectionSelection.IncludeOperationalNotes ? workspace.Draft.OperationalNotes : [],
            productCategories,
            HasUnverifiedProductPricing(workspace),
            FormatTimestamp(generatedAt));
    }

    private static string FormatOperatingDate(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);

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

    /// <summary>Only surfaced when it could mislead the reader about how current the shown official hours are (spec Part D) — an active manual/buyout override is the associate's own authoritative input, not something stale official data undermines.</summary>
    private static string? BuildVerificationText(AssociateVerificationInfo verification, EffectiveHours effective)
    {
        if (effective.IsManualOverrideApplied || effective.IsPrivateBuyoutApplied)
        {
            return null;
        }

        return verification.State switch
        {
            AssociateVerificationState.NotCheckedYet => "Not verified",
            AssociateVerificationState.Stale => verification.LastVerifiedAt is { } staleAt
                ? $"Last verified {FormatTimestamp(staleAt)} - may be out of date"
                : "Not recently verified - may be out of date",
            AssociateVerificationState.Error or AssociateVerificationState.Unavailable => "Unavailable - could not be checked",
            AssociateVerificationState.Verified when verification.LastVerifiedAt is { } verifiedAt =>
                $"Last verified {FormatTimestamp(verifiedAt)}",
            _ => null,
        };
    }

    private static PdfEarlyRideTime BuildEarlyRideTime(EarlyRideTimeDetails details)
    {
        var enabled = !string.IsNullOrWhiteSpace(details.Details) || details.ParticipatingAttractions.Count > 0;
        return new PdfEarlyRideTime(enabled, string.IsNullOrWhiteSpace(details.Details) ? null : details.Details.Trim(), details.ParticipatingAttractions);
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

    /// <summary>Spec Phase 8A/8B: every product-bearing <see cref="SourceCategory"/> maps to its own <see cref="PassdownSectionSelection"/> flag; Phase 8B added <see cref="SourceCategory.HauntedAttractions"/> (SCarowinds/Haunt), correcting a Phase 8A omission that left it always included. <see cref="SourceCategory.ParkHours"/> never reaches here — it produces no <see cref="Application.Associate.AssociateProduct"/> rows — so the fallback below is unreachable in practice, kept only so a future category fails open rather than throwing.</summary>
    private static bool IsOptionalCategoryIncluded(SourceCategory category, PassdownSectionSelection selection) => category switch
    {
        SourceCategory.DatedAdmissionTickets => selection.IncludeDailyAdmission,
        SourceCategory.DatedFastLane => selection.IncludeFastLane,
        SourceCategory.Promotions => selection.IncludePromotions,
        SourceCategory.SeasonPasses => selection.IncludeSeasonPasses,
        SourceCategory.AddOns => selection.IncludeAddOns,
        SourceCategory.HauntedAttractions => selection.IncludeHauntedAttractions,
        _ => true,
    };

    private static IReadOnlyList<PdfProductCategory> BuildProductCategories(AssociateWorkspace workspace)
    {
        var categories = new List<PdfProductCategory>();

        var officialByCategory = workspace.Products
            .Where(product => !product.IsCustom && product.Category is not null && product.IncludeOnPdf
                && IsOptionalCategoryIncluded(product.Category!.Value, workspace.Draft.SectionSelection))
            .GroupBy(product => product.Category!.Value)
            .OrderBy(group => (int)group.Key);

        foreach (var group in officialByCategory)
        {
            var rows = group.Select(BuildOfficialRow).ToList();
            if (rows.Count > 0)
            {
                categories.Add(new PdfProductCategory(PdfCategoryDisplayNames.For(group.Key), TierFor(group.Key), rows));
            }
        }

        var customRows = workspace.Draft.CustomProducts
            .Where(custom => custom.IncludeOnPdf)
            .Select(BuildCustomRow)
            .ToList();

        if (customRows.Count > 0)
        {
            categories.Add(new PdfProductCategory(CustomProductCategoryName, PdfProductSectionTier.Reference, customRows));
        }

        return categories;
    }

    /// <summary>Spec Parts 3/4/5's tier assignment — Daily Admission/Fast Lane are what an associate needs to remember quickly (Key Selling), Promotions get their own callout treatment, and everything else (Season Passes, Add-Ons, SCarowinds/Haunt) is reference-oriented and rendered compactly.</summary>
    private static PdfProductSectionTier TierFor(SourceCategory category) => category switch
    {
        SourceCategory.DatedAdmissionTickets => PdfProductSectionTier.KeySelling,
        SourceCategory.DatedFastLane => PdfProductSectionTier.KeySelling,
        SourceCategory.Promotions => PdfProductSectionTier.Promotional,
        _ => PdfProductSectionTier.Reference,
    };

    private static PdfProductRow BuildOfficialRow(AssociateProduct product) => new(
        product.DisplayName,
        FormatMoney(product.EffectiveDisplayPrice),
        FormatMoney(product.ComparisonPriceAmount),
        string.IsNullOrWhiteSpace(product.PriceQualifier) ? null : product.PriceQualifier.Trim(),
        Notes: null);

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

    private static string? FormatMoney(decimal? amount) => amount is { } value ? "$" + value.ToString("0.00", CultureInfo.InvariantCulture) : null;

    private static string FormatTime(TimeOnly? time) => time?.ToString("h:mm tt", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
}
