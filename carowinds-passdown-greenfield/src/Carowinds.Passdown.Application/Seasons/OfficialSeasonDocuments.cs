using System.Text.Json;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Domain.Seasons;

namespace Carowinds.Passdown.Application.Seasons;

/// <summary>Phase 31: the reference-document source ids (rows in the local official reference document cache).</summary>
public static class OfficialReferenceDocumentIds
{
    public const string OperatingEvents = "operating-events.official";

    /// <summary>Written by the Promotions catalog refresh alongside its catalog reconciliation.</summary>
    public const string Deals = "deals.official";
}

/// <summary>Phase 31: what one successful official event-schedule check observed.</summary>
public sealed record OperatingEventsPayload(IReadOnlyList<OfficialOperatingEvent> Events)
{
    /// <summary>Non-fatal notes, e.g. one event page could not be read while the listing was.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Phase 31: what one successful official deals check observed. <see cref="UnreadableOfferIds"/> lists
/// offers whose card was present but could not be read — they keep their last verified details and
/// are not treated as removed. <see cref="UnreachablePageUrls"/> are optional campaign pages that
/// failed; offers previously seen only there are likewise kept, not expired.
/// </summary>
public sealed record OfficialDealsObservation(IReadOnlyList<OfficialPromotion> Promotions)
{
    public IReadOnlyList<string> UnreadableOfferIds { get; init; } = [];

    public IReadOnlyList<Uri> UnreachablePageUrls { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Phase 31: the stored last-known-good deals document (current and previously verified offers).</summary>
public sealed record OfficialDealsDocument(IReadOnlyList<OfficialPromotion> Promotions)
{
    public static OfficialDealsDocument Empty { get; } = new([]);

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>How long a no-longer-listed offer is kept for the history view.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(400);

    /// <summary>
    /// Merges a successful observation into the previous document: observed offers replace their
    /// previous version (keeping <see cref="OfficialPromotion.FirstSeenAt"/>); unreadable offers, and
    /// offers last seen only on a page that could not be reached, keep their previous details flagged
    /// <see cref="OfficialPromotion.LastCheckUnreadable"/>; every other previous offer is kept as it
    /// was (so the status rules see it as no longer listed) until the retention window passes.
    /// </summary>
    public OfficialDealsDocument Merge(OfficialDealsObservation observation, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var previousById = Promotions.ToDictionary(p => p.PromotionId, StringComparer.Ordinal);
        var merged = new Dictionary<string, OfficialPromotion>(StringComparer.Ordinal);

        foreach (var fresh in observation.Promotions)
        {
            merged[fresh.PromotionId] = previousById.TryGetValue(fresh.PromotionId, out var previous)
                ? fresh with { FirstSeenAt = previous.FirstSeenAt == default ? fresh.FirstSeenAt : Min(previous.FirstSeenAt, fresh.FirstSeenAt) }
                : fresh;
        }

        var unreadable = observation.UnreadableOfferIds.ToHashSet(StringComparer.Ordinal);
        var unreachable = observation.UnreachablePageUrls.Select(u => u.AbsoluteUri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in Promotions)
        {
            if (merged.ContainsKey(previous.PromotionId))
            {
                continue;
            }

            var onlyOnUnreachablePage = previous.SourceUrls.Count > 0 &&
                previous.SourceUrls.All(u => unreachable.Contains(u.AbsoluteUri));
            if (unreadable.Contains(previous.PromotionId) || onlyOnUnreachablePage)
            {
                merged[previous.PromotionId] = previous with { LastCheckUnreadable = true };
                continue;
            }

            if (checkedAt - previous.LastVerifiedAt <= HistoryRetention)
            {
                merged[previous.PromotionId] = previous with { LastCheckUnreadable = false };
            }
        }

        return new OfficialDealsDocument(merged.Values.OrderBy(p => p.Title, StringComparer.Ordinal).ToList())
        {
            Warnings = observation.Warnings,
        };
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}

/// <summary>Phase 31: JSON for the reference documents (stable, indented-free, enum names as strings).</summary>
public static class OfficialSeasonJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Returns null for a missing or unreadable payload — a damaged cache row is "unknown", never an exception on screen.</summary>
    public static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Phase 31: freshness of a reference document as shown to the user.</summary>
public enum OfficialDocumentFreshness
{
    /// <summary>Never successfully checked and no attempt recorded.</summary>
    NotChecked,

    /// <summary>The latest attempt succeeded.</summary>
    Current,

    /// <summary>The latest attempt failed; any shown data is the previously verified copy.</summary>
    LastCheckFailed,
}

/// <summary>Phase 31: last-checked / last-error facts for a reference document.</summary>
public sealed record OfficialDocumentStatus(
    OfficialDocumentFreshness Freshness,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    Uri? SourceUrl)
{
    public static OfficialDocumentStatus NotChecked { get; } = new(OfficialDocumentFreshness.NotChecked, null, null, null, null);

    public bool HasVerifiedData => LastSuccessAt is not null;

    public static OfficialDocumentStatus From(OfficialReferenceDocumentRecord? record)
    {
        if (record is null)
        {
            return NotChecked;
        }

        return new OfficialDocumentStatus(
            record.LastAttemptFailed ? OfficialDocumentFreshness.LastCheckFailed : OfficialDocumentFreshness.Current,
            record.SuccessCheckedAt,
            record.LastAttemptCheckedAt,
            record.LastAttemptFailed ? record.LastAttemptDetail : null,
            record.SuccessSourceUrl ?? record.LastAttemptSourceUrl);
    }
}
