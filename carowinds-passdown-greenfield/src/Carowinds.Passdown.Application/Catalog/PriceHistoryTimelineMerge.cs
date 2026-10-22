using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Domain.Catalog;

namespace Carowinds.Passdown.Application.Catalog;

/// <summary>Phase 26: where the evidence behind one Price History row came from.</summary>
[Flags]
public enum PriceHistoryProvenance
{
    None = 0,

    /// <summary>This computer's own official checks.</summary>
    CollectedHere = 1,

    /// <summary>Another computer's official checks, merged in through Personal Sync.</summary>
    ImportedThroughSync = 2,
}

/// <summary>
/// Phase 26: the pure rule that turns this computer's own intervals plus every observation merged in
/// through Personal Sync into one chronological price history for a product. Nothing is stored — the
/// local <c>season_price_history</c> rows and the imported observations stay exactly as recorded, and
/// this view is recomputed on every read.
/// <para>
/// Algorithm: every piece of evidence (a local interval or an imported observation) is a span — one
/// price state shown from first-observed to last-confirmed by one contributor. Spans are sorted by a
/// total, content-only order (first observed, price kind, amount, last confirmed, names, identity), so
/// the result depends only on the <em>set</em> of spans, never on sync order or arrival time. Walking
/// that order, a span joins the current row when it shows the same state (amount <b>and</b> kind) and
/// either overlaps the row in time, or comes from a different contributor (another computer's
/// observation of the same price is no evidence the price changed). A span stays a separate row when
/// it is the next interval of the same contributor's own series after a gap — that contributor's
/// recorder saw the product retired and published again (a reappearance), which is preserved.
/// With no imported observations the result is exactly the local history.
/// </para>
/// </summary>
public static class PriceHistoryTimelineMerge
{
    private const string LocalContributor = "local";

    private sealed record Span(
        string Contributor,
        int Sequence,
        string Id,
        SeasonPriceHistoryInterval Interval,
        PriceHistoryProvenance Provenance);

    private sealed class Group(Span first)
    {
        public Span First { get; } = first;

        public Span Latest { get; set; } = first;

        public DateTimeOffset LastConfirmedAt { get; set; } = first.Interval.LastConfirmedAt;

        public PriceHistoryProvenance Provenance { get; set; } = first.Provenance;

        public SeasonPriceState State => First.Interval.State;
    }

    public static IReadOnlyList<PriceHistoryEntry> Merge(
        string productKey,
        IReadOnlyList<SeasonPriceHistoryInterval> localIntervals,
        IReadOnlyList<OfficialPriceObservation> importedObservations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);
        ArgumentNullException.ThrowIfNull(localIntervals);
        ArgumentNullException.ThrowIfNull(importedObservations);

        var spans = localIntervals
            .Where(i => i.ProductKey == productKey)
            .Select(i => new Span(LocalContributor, i.Sequence, $"local:{i.Sequence:D10}", i, PriceHistoryProvenance.CollectedHere))
            .Concat(importedObservations
                .Where(o => o.ProductKey == productKey)
                .DistinctBy(o => o.ObservationId)
                .Select(o => new Span(
                    o.ContributorDeviceId.ToString("D"),
                    o.ContributorSequence,
                    o.ObservationId,
                    new SeasonPriceHistoryInterval(
                        o.ProductKey, o.ContributorSequence, o.Amount, o.PriceKind, o.DisplayPrice, o.PriceQualifier,
                        o.DisplayName, o.SourceName, o.FirstObservedUtc, o.LastConfirmedUtc, o.Origin),
                    PriceHistoryProvenance.ImportedThroughSync)))
            .OrderBy(s => s.Interval.FirstObservedAt.UtcTicks)
            .ThenBy(s => s.Interval.PriceKind)
            .ThenBy(s => s.Interval.Amount)
            .ThenBy(s => s.Interval.LastConfirmedAt.UtcTicks)
            .ThenBy(s => s.Interval.DisplayName, StringComparer.Ordinal)
            .ThenBy(s => s.Interval.SourceName, StringComparer.Ordinal)
            .ThenBy(s => s.Interval.DisplayPrice ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.Interval.PriceQualifier ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal);

        var groups = new List<Group>();
        foreach (var span in spans)
        {
            var current = groups.Count == 0 ? null : groups[^1];
            if (current is not null && JoinsGroup(current, span))
            {
                if (span.Interval.LastConfirmedAt > current.LastConfirmedAt)
                {
                    current.LastConfirmedAt = span.Interval.LastConfirmedAt;
                }

                current.Provenance |= span.Provenance;
                current.Latest = span;
                continue;
            }

            groups.Add(new Group(span));
        }

        var entries = new List<PriceHistoryEntry>(groups.Count);
        SeasonPriceHistoryInterval? previous = null;
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var first = group.First.Interval;
            var merged = first with
            {
                ProductKey = productKey,
                Sequence = index + 1,
                LastConfirmedAt = group.LastConfirmedAt,
            };

            entries.Add(new PriceHistoryEntry(merged, previous is null ? null : SeasonPriceChange.Between(previous.State, merged.State))
            {
                Provenance = group.Provenance,
            });
            previous = merged;
        }

        return entries;
    }

    private static bool JoinsGroup(Group group, Span span)
    {
        if (group.State != span.Interval.State)
        {
            return false;
        }

        if (span.Interval.FirstObservedAt <= group.LastConfirmedAt)
        {
            return true; // overlapping evidence of the same price
        }

        // After a gap: the same contributor's next interval at the same price is its recorded reappearance.
        var latest = group.Latest;
        var isReappearance = latest.Contributor == span.Contributor && span.Sequence == latest.Sequence + 1;
        return !isReappearance;
    }
}
