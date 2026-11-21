using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Application.ProductDetails;

/// <summary>Phase 31B: the reference-document ids for official product details — one per official channel, so one channel's failure never touches another's last-known-good copy.</summary>
public static class OfficialProductDetailsDocumentIds
{
    public const string SeasonPasses = "product-details.season-passes.official";
    public const string MilitaryFirstResponder = "product-details.military-first-responder.official";
    public const string Promotions = "product-details.promotions.official";

    public static string For(OfficialProductChannel channel) => channel switch
    {
        OfficialProductChannel.SeasonPasses => SeasonPasses,
        OfficialProductChannel.MilitaryFirstResponder => MilitaryFirstResponder,
        OfficialProductChannel.Promotions => Promotions,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown official product channel."),
    };

    public static IReadOnlyList<OfficialProductChannel> AllChannels { get; } =
        [OfficialProductChannel.SeasonPasses, OfficialProductChannel.MilitaryFirstResponder, OfficialProductChannel.Promotions];
}

/// <summary>
/// Phase 31B: what one successful official check read about product details on one channel.
/// <see cref="UnreadableDetailIds"/> lists cards that were present but could not be read — they keep
/// their last verified details and are never treated as removed.
/// </summary>
public sealed record OfficialProductDetailsObservation(OfficialProductChannel Channel, IReadOnlyList<OfficialProductDetails> Products)
{
    public IReadOnlyList<string> UnreadableDetailIds { get; init; } = [];

    /// <summary>Optional pages that could not be reached; cards previously seen only there keep their details as unverified.</summary>
    public IReadOnlyList<Uri> UnreachablePageUrls { get; init; } = [];
}

/// <summary>Phase 31B: the stored last-known-good product-details document of one channel.</summary>
public sealed record OfficialProductDetailsDocument(IReadOnlyList<OfficialProductDetails> Products)
{
    public static OfficialProductDetailsDocument Empty { get; } = new([]);

    /// <summary>How long a no-longer-listed card is kept (for its history and the "no longer listed" status).</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(400);

    /// <summary>How many superseded term sets are kept per card.</summary>
    public const int MaxPreviousTerms = 5;

    /// <summary>
    /// Merges a successful observation: an observed card replaces its previous version, keeping
    /// <see cref="OfficialProductDetails.FirstSeenAt"/> and — when the official statements changed —
    /// moving the previous statements into <see cref="OfficialProductDetails.PreviousTerms"/> (provenance
    /// preserved, never rewritten). Unreadable cards keep their previous details flagged
    /// <see cref="OfficialProductDetails.LastCheckUnreadable"/>; other previous cards are kept as they were
    /// (shown as no longer listed) until <see cref="HistoryRetention"/> passes. Deterministic: products
    /// are ordered by title, then id.
    /// </summary>
    public OfficialProductDetailsDocument Merge(OfficialProductDetailsObservation observation, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var previousById = Products.ToDictionary(p => p.DetailsId, StringComparer.Ordinal);
        var merged = new Dictionary<string, OfficialProductDetails>(StringComparer.Ordinal);

        foreach (var fresh in observation.Products)
        {
            if (!previousById.TryGetValue(fresh.DetailsId, out var previous))
            {
                merged[fresh.DetailsId] = fresh;
                continue;
            }

            var history = previous.PreviousTerms;
            if (!SameStatements(previous.Statements, fresh.Statements))
            {
                history = new[] { new SupersededProductTerms(previous.Statements, previous.FirstSeenAt, previous.LastVerifiedAt) }
                    .Concat(previous.PreviousTerms)
                    .Take(MaxPreviousTerms)
                    .ToList();
            }

            merged[fresh.DetailsId] = fresh with
            {
                FirstSeenAt = previous.FirstSeenAt == default || fresh.FirstSeenAt < previous.FirstSeenAt ? fresh.FirstSeenAt : previous.FirstSeenAt,
                PreviousTerms = history,
            };
        }

        var unreadable = observation.UnreadableDetailIds.ToHashSet(StringComparer.Ordinal);
        var unreachable = observation.UnreachablePageUrls.Select(u => u.AbsoluteUri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in Products)
        {
            if (merged.ContainsKey(previous.DetailsId))
            {
                continue;
            }

            var onlyOnUnreachablePage = previous.SourceUrls.Count > 0 && previous.SourceUrls.All(u => unreachable.Contains(u.AbsoluteUri));
            if (unreadable.Contains(previous.DetailsId) || onlyOnUnreachablePage)
            {
                merged[previous.DetailsId] = previous with { LastCheckUnreadable = true };
                continue;
            }

            if (checkedAt - previous.LastVerifiedAt <= HistoryRetention)
            {
                merged[previous.DetailsId] = previous with { LastCheckUnreadable = false };
            }
        }

        return new OfficialProductDetailsDocument(merged.Values
            .OrderBy(p => p.Title, StringComparer.Ordinal)
            .ThenBy(p => p.DetailsId, StringComparer.Ordinal)
            .ToList());
    }

    private static bool SameStatements(IReadOnlyList<ProductDetailStatement> a, IReadOnlyList<ProductDetailStatement> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First == pair.Second);
}
