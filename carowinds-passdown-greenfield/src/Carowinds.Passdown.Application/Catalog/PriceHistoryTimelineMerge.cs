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
/// Every piece of evidence (a local interval or an imported observation) is a span — one price state
/// shown from first-observed to last-confirmed by one contributor. Spans are sorted by a total,
/// content-only order, so the result depends only on the <em>set</em> of spans, never on sync order.
/// </para>
/// <para>
/// Phase 31B grouping rules (fixing repeated first-seen dates and duplicate-looking rows when two or
/// three computers' observations interleave):
/// <list type="number">
/// <item><b>Identical-origin evidence folds.</b> Spans with the same contributor, contributor sequence,
/// price state and first-observed instant are one recorded interval (e.g. this computer's own
/// observation relayed back after its local row was re-keyed); they fold into one span with the latest
/// confirmation and every provenance flag. Distinct genuine observations are never dropped.</item>
/// <item><b>A span joins the most recent row with the same price state</b> (amount <b>and</b> price
/// type — different types at the same amount are never conflated), not merely the last row, unless a
/// <b>verified different price intervenes</b>: a row with a different state, started after that row,
/// that was still confirmed after that row's last confirmation. Another computer's brief, concurrent
/// disagreement (fully inside a period the matching row keeps confirming) therefore no longer splits
/// one period into alternating rows; a real sale ($105 → $95 → $105) stays three rows.</item>
/// <item><b>A contributor's own reappearance is preserved:</b> after a gap that no other evidence
/// bridges, the same contributor's next interval at the same price stays its own row (the product was
/// retired and published again).</item>
/// </list>
/// A row's First Seen is the earliest first observation of its spans and Last Confirmed the latest
/// confirmation. With no imported observations the result is exactly the local history.
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

        public DateTimeOffset LastConfirmedAt { get; set; } = first.Interval.LastConfirmedAt;

        public PriceHistoryProvenance Provenance { get; set; } = first.Provenance;

        public HashSet<(string Contributor, int Sequence)> Members { get; } = [(first.Contributor, first.Sequence)];

        public SeasonPriceState State => First.Interval.State;
    }

    /// <param name="localDeviceId">
    /// Phase 31B: this computer's Personal Sync device id, when known, so an observation this computer
    /// itself contributed (relayed back through another computer's file) is recognized as the same
    /// origin as its local interval.
    /// </param>
    public static IReadOnlyList<PriceHistoryEntry> Merge(
        string productKey,
        IReadOnlyList<SeasonPriceHistoryInterval> localIntervals,
        IReadOnlyList<OfficialPriceObservation> importedObservations,
        Guid? localDeviceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);
        ArgumentNullException.ThrowIfNull(localIntervals);
        ArgumentNullException.ThrowIfNull(importedObservations);

        var localContributor = localDeviceId is { } id && id != Guid.Empty ? id.ToString("D") : LocalContributor;

        var rawSpans = localIntervals
            .Where(i => i.ProductKey == productKey)
            .Select(i => new Span(localContributor, i.Sequence, $"local:{i.Sequence:D10}", i, PriceHistoryProvenance.CollectedHere))
            .Concat(importedObservations
                .Where(o => o.ProductKey == productKey)
                .GroupBy(o => o.ObservationId, StringComparer.Ordinal)
                .Select(g => g.MaxBy(o => o.LastConfirmedUtc.UtcTicks)!)
                .Select(o => new Span(
                    o.ContributorDeviceId.ToString("D"),
                    o.ContributorSequence,
                    o.ObservationId,
                    new SeasonPriceHistoryInterval(
                        o.ProductKey, o.ContributorSequence, o.Amount, o.PriceKind, o.DisplayPrice, o.PriceQualifier,
                        o.DisplayName, o.SourceName, o.FirstObservedUtc, o.LastConfirmedUtc, o.Origin),
                    PriceHistoryProvenance.ImportedThroughSync)));

        // Rule 1: identical-origin evidence folds (local copy preferred as the representative).
        var spans = rawSpans
            .GroupBy(sp => (sp.Contributor, sp.Sequence, sp.Interval.State, sp.Interval.FirstObservedAt.UtcTicks))
            .Select(g =>
            {
                var representative = g
                    .OrderBy(sp => sp.Provenance == PriceHistoryProvenance.CollectedHere ? 0 : 1)
                    .ThenBy(sp => sp.Id, StringComparer.Ordinal)
                    .First();
                var lastConfirmed = g.Max(sp => sp.Interval.LastConfirmedAt);
                var provenance = g.Aggregate(PriceHistoryProvenance.None, (acc, sp) => acc | sp.Provenance);
                return representative with
                {
                    Interval = representative.Interval with { LastConfirmedAt = lastConfirmed },
                    Provenance = provenance,
                };
            })
            .OrderBy(sp => sp.Interval.FirstObservedAt.UtcTicks)
            .ThenBy(sp => sp.Interval.PriceKind)
            .ThenBy(sp => sp.Interval.Amount)
            .ThenBy(sp => sp.Interval.LastConfirmedAt.UtcTicks)
            .ThenBy(sp => sp.Interval.DisplayName, StringComparer.Ordinal)
            .ThenBy(sp => sp.Interval.SourceName, StringComparer.Ordinal)
            .ThenBy(sp => sp.Interval.DisplayPrice ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(sp => sp.Interval.PriceQualifier ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(sp => sp.Contributor, StringComparer.Ordinal)
            .ThenBy(sp => sp.Id, StringComparer.Ordinal);

        var groups = new List<Group>();
        foreach (var span in spans)
        {
            var target = FindJoinableGroup(groups, span);
            if (target is null)
            {
                groups.Add(new Group(span));
                continue;
            }

            if (span.Interval.LastConfirmedAt > target.LastConfirmedAt)
            {
                target.LastConfirmedAt = span.Interval.LastConfirmedAt;
            }

            target.Provenance |= span.Provenance;
            target.Members.Add((span.Contributor, span.Sequence));
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
                OverlapsEarlierPeriod = index > 0 && groups.Take(index).Any(g => g.State != group.State && g.LastConfirmedAt > group.LastConfirmedAt),
            });
            previous = merged;
        }

        return entries;
    }

    /// <summary>Rules 2 and 3: the most recent same-state row this span may join, or null for a new row.</summary>
    private static Group? FindJoinableGroup(List<Group> groups, Span span)
    {
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];
            if (group.State != span.Interval.State)
            {
                continue;
            }

            // A verified different price after this row's last confirmation separates the periods.
            for (var later = index + 1; later < groups.Count; later++)
            {
                if (groups[later].State != group.State && groups[later].LastConfirmedAt > group.LastConfirmedAt)
                {
                    return null;
                }
            }

            var overlaps = span.Interval.FirstObservedAt <= group.LastConfirmedAt;
            if (!overlaps && group.Members.Contains((span.Contributor, span.Sequence - 1)))
            {
                return null; // the contributor's own recorded reappearance after a gap nobody bridged
            }

            return group;
        }

        return null;
    }
}
