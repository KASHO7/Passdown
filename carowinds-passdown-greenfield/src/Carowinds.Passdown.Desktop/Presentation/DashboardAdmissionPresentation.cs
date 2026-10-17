using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// Phase 22C: display strings for the Dashboard's "Daily Admission" tile, built only from
/// <see cref="DashboardAdmissionSummary"/> (the selection policy lives in the Application layer). A price
/// is always shown with its product name and its own freshness line; an exact dated price says "for
/// today", an advertised floor says "From" and "Advertised starting price", and no price says so plainly.
/// Timestamps are Eastern (the park's operating-day zone).
/// </summary>
public sealed record DashboardAdmissionPresentation(
    string? ProductName,
    string PriceText,
    string QualifierText,
    string? FreshnessText,
    bool HasPrice,
    bool IsStale,
    string AutomationText)
{
    public const string NotAvailableText = "Price not available";

    public bool HasProductName => ProductName is not null;

    public bool HasFreshnessText => FreshnessText is not null;

    public static DashboardAdmissionPresentation Build(DashboardAdmissionSummary summary, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary is { Kind: not DashboardAdmissionKind.NotAvailable, PriceAmount: { } amount })
        {
            var isStale = summary.PriceState == AssociateVerificationState.Stale;
            var exact = summary.Kind == DashboardAdmissionKind.ExactDated;
            var priceText = exact ? PriceFormatting.Format(amount) : $"From {PriceFormatting.Format(amount)}";
            var qualifier = exact ? "Official price for today" : "Advertised starting price";

            string? freshness = summary.LastVerifiedAt is { } verifiedAt
                ? isStale
                    ? $"Last verified {FormatEastern(verifiedAt, nowUtc)} — may be out of date"
                    : $"Verified {FormatEastern(verifiedAt, nowUtc)}"
                : isStale ? "Older information — may be out of date" : null;

            var automation = $"Daily admission: {summary.ProductName}, {priceText}, {qualifier}." + (freshness is null ? string.Empty : $" {freshness}.");
            return new DashboardAdmissionPresentation(summary.ProductName, priceText, qualifier, freshness, HasPrice: true, isStale, automation);
        }

        var reason = summary.UnavailableReason switch
        {
            DashboardAdmissionUnavailableReason.Ambiguous => "More than one Park Ticket price is listed for today",
            DashboardAdmissionUnavailableReason.Historical => "Today's prices are not shown for past dates",
            _ => summary.CategoryState switch
            {
                AssociateVerificationState.NotPublished => "Today's dated prices are not published yet",
                AssociateVerificationState.Unavailable => "No official dated price for today",
                AssociateVerificationState.Closed => "No dated admission offered today",
                AssociateVerificationState.Error => "Dated prices could not be checked",
                AssociateVerificationState.Stale => "Dated prices could not be rechecked",
                AssociateVerificationState.NotCheckedYet => "Dated prices have not been checked yet",
                _ => "No general admission price identified for today",
            },
        };

        // Attempt-only timestamp: "checked", never "verified".
        string? checkedText = summary.CategoryLastCheckedAt is { } checkedAt ? $"Last checked {FormatEastern(checkedAt, nowUtc)}" : null;
        var unavailableAutomation = $"Daily admission: {NotAvailableText}. {reason}." + (checkedText is null ? string.Empty : $" {checkedText}.");
        return new DashboardAdmissionPresentation(null, NotAvailableText, reason, checkedText, HasPrice: false, IsStale: false, unavailableAutomation);
    }

    /// <summary>Eastern time; the date is omitted when it is the same Eastern day as <paramref name="nowUtc"/> (matches the official-data status line).</summary>
    internal static string FormatEastern(DateTimeOffset instantUtc, DateTimeOffset nowUtc)
    {
        var eastern = EasternTimeZone.ConvertFromUtc(instantUtc);
        var nowEastern = EasternTimeZone.ConvertFromUtc(nowUtc);
        return eastern.Date == nowEastern.Date
            ? eastern.ToString("h:mm tt", PresentationCulture.UnitedStates)
            : eastern.ToString("MMM d, h:mm tt", PresentationCulture.UnitedStates);
    }
}
