using System.Text.Json;
using Carowinds.Passdown.Application.Freshness;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The one authoritative composition of official/manual-override/private-buyout park hours into an
/// <see cref="AssociateParkHoursView"/> (spec §8.2) — extracted from <see cref="AssociatePassdownService"/>
/// in Phase 12B so <see cref="Calendar.OperationsCalendarService"/> can reuse the exact same
/// calculation for every day in a displayed month, rather than a second/duplicated implementation
/// (CLAUDE.md "do not duplicate hours calculations"; spec Phase 12B Part A: "Do NOT create a new
/// Calendar-specific hours engine"). <see cref="AssociatePassdownService"/> and
/// <see cref="Calendar.OperationsCalendarService"/> both call <see cref="Compose"/> directly — neither
/// re-implements any part of the official→manual-override→private-buyout precedence, which still
/// lives in exactly one place, <see cref="Domain.Hours.ParkHoursCalculator.Calculate"/>.
/// </summary>
public static class AssociateParkHoursComposer
{
    public static AssociateParkHoursView Compose(DatedResultRecord? record, AssociateDraftPayload draft, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var lastKnownGood = record?.ToLastKnownGood() ?? LastKnownGood<string>.Empty;
        var verification = AssociateVerificationInfo.From(lastKnownGood, asOf);

        // Uses the raw last-successful evidence's own status, not the (possibly Stale-relabeled)
        // `verification.State` — a "Closed" result that later became stale (a subsequent check
        // failed outright) must still compute as officially closed, just flagged Stale via
        // `verification` for presentation, per spec §6.7.
        var officialIsClosed = lastKnownGood.SuccessEvidence?.Status == RetrievalStatus.Closed;
        var officialPayload = lastKnownGood.SuccessValue is { } json ? JsonSerializer.Deserialize<ParkHoursPayload>(json) : null;

        var effective = ParkHoursCalculator.Calculate(
            officialIsClosed,
            officialPayload?.OpeningTime,
            officialPayload?.ClosingTime,
            draft.ManualHoursOverride.Enabled,
            draft.ManualHoursOverride.OpeningTime,
            draft.ManualHoursOverride.ClosingTime,
            draft.PrivateBuyout.Enabled,
            draft.PrivateBuyout.OpeningTime,
            draft.PrivateBuyout.ClosingTime);

        return new AssociateParkHoursView(
            effective,
            verification,
            officialIsClosed,
            officialPayload?.OpeningTime,
            officialPayload?.ClosingTime,
            officialPayload?.WaterparkOpeningTime,
            officialPayload?.WaterparkClosingTime);
    }
}
