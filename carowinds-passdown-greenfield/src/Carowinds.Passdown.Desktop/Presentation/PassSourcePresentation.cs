using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 25: the compact official-source line under the Season Passes and Memberships groups —
/// where the prices come from, when they were last verified (Eastern time), and the official page's
/// own verification summary (which carries its fee/tax disclosure verbatim). A partially verified or
/// stale check says so here and on the affected rows, so current verified pricing is never confused
/// with older observations.
/// </summary>
public static class PassSourcePresentation
{
    /// <summary>Memberships were never part of the passdown PDF/Teams Copy; said once for the group.</summary>
    public const string MembershipsNotPrintedText = "Memberships are shown for reference and are not printed on the passdown.";

    public static string? SourceNote(ProductDisplaySection section, IReadOnlyList<AssociateProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (section is not (ProductDisplaySection.SeasonPasses or ProductDisplaySection.Memberships))
        {
            return null;
        }

        var latest = products
            .Where(product => product.PassOffer is not null && product.Verification.LastVerifiedAt is not null)
            .MaxBy(product => product.Verification.LastVerifiedAt);
        if (latest is null)
        {
            return null;
        }

        var page = latest.OfficialSourceUrl is { } url ? $" ({url.Host}{url.AbsolutePath})" : string.Empty;
        var note = $"Official Carowinds source{page} · last verified {EasternTimestampFormat.DateAndTime(latest.Verification.LastVerifiedAt!.Value)}";
        if (!string.IsNullOrWhiteSpace(latest.Verification.Detail))
        {
            note = $"{note}. {latest.Verification.Detail}";
        }

        return section == ProductDisplaySection.Memberships ? $"{note} {MembershipsNotPrintedText}" : note;
    }
}
