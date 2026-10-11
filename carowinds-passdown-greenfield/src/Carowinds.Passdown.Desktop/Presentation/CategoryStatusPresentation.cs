using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// A single "Fast Lane — Not available for this date" style banner (spec Phase 4B Part E), built
/// from the Application layer's <see cref="AssociateProductCategoryStatus"/> — never fabricating a
/// product row, only explaining why a category currently has none.
/// </summary>
public sealed record CategoryStatusPresentation(string CategoryDisplayName, string StatusText, string? DetailText)
{
    /// <summary>
    /// Null when <paramref name="productRowCount"/> is positive: a category that already has
    /// visible product rows does not need a redundant status banner above them.
    /// </summary>
    public static CategoryStatusPresentation? Build(AssociateProductCategoryStatus status, int productRowCount)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (productRowCount > 0)
        {
            return null;
        }

        var statusText = status.State switch
        {
            AssociateVerificationState.NotCheckedYet => "Not checked yet",
            AssociateVerificationState.NotPublished => "Not published for this date",
            AssociateVerificationState.Unavailable => "Not available for this date",
            AssociateVerificationState.Closed => "Not available for this date",
            AssociateVerificationState.Error => "Unable to verify current pricing",
            AssociateVerificationState.Stale => "Unable to recheck — no earlier information is available",
            AssociateVerificationState.Verified => "Verified — no products currently listed",
            _ => "Not checked yet",
        };

        var detail = status.LastCheckedAt is { } checkedAt ? $"Last checked {FormatTimestamp(checkedAt)}" : null;

        return new CategoryStatusPresentation(SourceCategoryDisplay.FriendlyName(status.Category), statusText, detail);
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d 'at' h:mm tt", PresentationCulture.UnitedStates);
}
