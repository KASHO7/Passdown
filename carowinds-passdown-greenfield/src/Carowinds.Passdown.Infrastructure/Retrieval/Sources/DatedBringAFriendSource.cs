using System.Globalization;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official exact-date ordinary (non-Duo) Bring-A-Friend adapter (Phase 15A; see
/// docs/phase13-hardening.md's Phase 15A section for the live investigation this is built from).
/// Supersedes Phase 13C's documented limitation ("only reachable after an authenticated passholder
/// login") — that conclusion was based on the shared <c>SnapWizard</c>/<c>SnapWizardDailyTickets</c>
/// keywords ordinary Daily Admission already uses, which genuinely never return a Bring-A-Friend line
/// item. Live re-investigation of the anonymous Special Offers page's own "Select a date to begin!"
/// calendar (reached from the site's "Bring-A-Friend Ticket" nav link, no login prompt at any point)
/// found it calls a *different*, previously-untried keyword against the same
/// <c>getkeywordcalendar</c> endpoint every other dated source already calls:
/// <c>BAFWizardDailyTickets</c>. A plain anonymous POST with this keyword returns real, date-scoped
/// pricing with no authentication of any kind — confirmed for 2026-08-29 ($39.00) and 2026-08-30
/// ($35.00), matching the regression values <c>docs/phase13-hardening.md</c> locked in.
///
/// The same keyword's response always carries two packages per date — "Celebrate America!
/// Bring-A-Friend Duo Offer" (identical package id/price to <see cref="DatedBringAFriendDuoSource"/>'s
/// own <c>SUBAF50</c> keyword) and "Bring-A-Friend Ticket" — so <see cref="BuildProduct"/> selects
/// only the package named "Bring-A-Friend Ticket" by its official name, never both; the Duo offer
/// stays exclusively owned by its own dedicated source/category. No live response for this keyword
/// ever included a comparison ("was") price — that value lives only on the static Promotions catalog
/// card (already retrieved by <see cref="PromotionsCatalogSource"/>) — so this adapter never
/// populates one; composition pairs the two official values instead of fabricating a third.
///
/// Structurally mirrors <see cref="DatedBringAFriendDuoSource"/> (primary single-day request, horizon
/// fallback only when that comes back empty) — same keyword-calendar contract, independently
/// re-verified live for this keyword rather than assumed.
/// </summary>
public sealed class DatedBringAFriendSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedBringAFriendPayload>
{
    private const string Keyword = "BAFWizardDailyTickets";

    private const string TargetPackageName = "Bring-A-Friend Ticket";

    /// <summary>Directly observed live for this keyword (docs/phase13-hardening.md Phase 15A section) — matches the sibling Bring-A-Friend Duo keyword's convention.</summary>
    private const string DisplayZeroCapacity = "1";

    private const int HorizonPaddingDays = 120;

    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/special-offers");

    private const string Id = "dated-bring-a-friend.official";

    /// <summary>Phase 34C: the shared evidence construction for this source's payload-less results (<see cref="AccessoDatedResults{TPayload}"/>); wording stays here.</summary>
    private static readonly AccessoDatedResults<DatedBringAFriendPayload> Results = new(Id, "Carowinds Dated Bring-A-Friend", OfficialPageUrl);

    public string SourceId => Id;

    public SourceCategory Category => SourceCategory.DatedBringAFriend;

    public async Task<SourceResult<DatedBringAFriendPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated Bring-A-Friend retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        if (requestedDate < today)
        {
            return Unavailable(timeChecked, requestedDate, "That date has already passed.");
        }

        var primaryBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, requestedDate, requestedDate);
        var primaryResult = await AccessoKeywordCalendarCall.PostAsync(httpClient, primaryBody, cancellationToken).ConfigureAwait(false);
        if (primaryResult.Failure is { } primaryFailure)
        {
            return Error(timeChecked, requestedDate, primaryFailure);
        }

        var primaryService = primaryResult.Service!;
        if (string.Equals(primaryService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            var entries = primaryService.Dates?.Entries ?? [];
            if (entries.Count != 1)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Single-day getkeywordcalendar request for {requestedDate:yyyy-MM-dd} returned {entries.Count} date entries; expected exactly 1."));
            }

            if (!DateOnly.TryParseExact(entries[0].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var confirmedDate))
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Unparseable date '{entries[0].Date}' in calendar response."));
            }

            if (confirmedDate != requestedDate)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{entries[0].Date}'."),
                    "We couldn't confirm Bring-A-Friend availability for the requested date.");
            }

            var product = BuildProduct(entries[0].Packages?.P ?? [], requestedDate);
            if (product is null)
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException(
                        $"Matched date entry for {requestedDate:yyyy-MM-dd} had no '{TargetPackageName}' package."));
            }

            return SourceResult<DatedBringAFriendPayload>.Success(
                SourceId,
                new SourceEvidence(
                    "Carowinds Dated Bring-A-Friend",
                    OfficialPageUrl,
                    timeChecked,
                    RetrievalStatus.Verified,
                    $"Bring-A-Friend confirmed for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.",
                    RetrievalStrategy.DirectApi,
                    requestedDate: requestedDate,
                    confirmedDate: requestedDate),
                new DatedBringAFriendPayload([product]));
        }

        var windowEnd = requestedDate > today.AddDays(HorizonPaddingDays) ? requestedDate : today.AddDays(HorizonPaddingDays);
        var horizonBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, today, windowEnd);
        var horizonResult = await AccessoKeywordCalendarCall.PostAsync(httpClient, horizonBody, cancellationToken).ConfigureAwait(false);
        if (horizonResult.Failure is { } horizonFailure)
        {
            return Error(timeChecked, requestedDate, horizonFailure);
        }

        var horizonService = horizonResult.Service!;

        // Phase 19B: the endpoint's own "no dates found" answer for a window starting today means
        // the offer has no published dates from today onward (e.g. it ended) — a
        // successful "not currently published" check, never a retrieval failure (live-confirmed
        // shape; see AccessoKeywordCalendarService.IsNoDatesFound).
        if (horizonService.IsNoDatesFound)
        {
            return NotPublished(timeChecked, requestedDate);
        }

        if (!string.Equals(horizonService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"SERVICE.status was '{horizonService.Status}' (error_msg='{horizonService.ErrorMessage}') for a horizon window that should have included today."),
                "We couldn't check Bring-A-Friend availability right now.");
        }

        var horizonEntries = horizonService.Dates?.Entries ?? [];
        var parsedHorizonDates = new List<DateOnly>(horizonEntries.Count);
        foreach (var entry in horizonEntries)
        {
            if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var entryDate))
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Unparseable date '{entry.Date}' in horizon calendar response."));
            }

            parsedHorizonDates.Add(entryDate);
        }

        if (parsedHorizonDates.Count == 0)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException("SERVICE.status was 'OK' but the horizon window's DATES.D contained no entries."));
        }

        var maxDate = parsedHorizonDates.Max();
        return requestedDate > maxDate
            ? NotPublished(timeChecked, requestedDate)
            : Unavailable(timeChecked, requestedDate, DefaultUnavailableDetail(requestedDate));
    }

    /// <summary>
    /// Selects the one package officially named "Bring-A-Friend Ticket" from whatever this keyword's
    /// response returned — every live response observed also carried a "Celebrate America!
    /// Bring-A-Friend Duo Offer" package alongside it (Phase 15A investigation), which this adapter
    /// deliberately ignores; that offer is already owned by <see cref="DatedBringAFriendDuoSource"/>.
    /// Never reads the date-level <c>lowest_price</c> aggregate field (spec §5.4).
    ///
    /// Phase 15F live spot check found the official site prefixes this same package with a seasonal
    /// label for dates outside the immediate near-term window (observed live: "Fall Bring-A-Friend
    /// Ticket", $40.00, 2026-10-15 — vs. plain "Bring-A-Friend Ticket" for 2026-08-29) — Phase 15A's
    /// original investigation only ever observed the unprefixed name, so its exact match silently
    /// reported "Unavailable" for every seasonally-labeled date even though real pricing exists.
    /// Matching by suffix instead of exact equality tolerates any such prefix while still excluding
    /// the Duo Offer package (which ends in "Duo Offer", not "Bring-A-Friend Ticket") — the stored
    /// product name stays the stable <see cref="TargetPackageName"/> constant either way (unchanged
    /// design; this fix only widens which live package qualifies as a match, not what gets displayed).
    /// </summary>
    private static DatedBringAFriendProduct? BuildProduct(
        IReadOnlyList<AccessoKeywordCalendarPackage> packages,
        DateOnly applicableDate)
    {
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Name) || !package.Name.Trim().EndsWith(TargetPackageName, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(package.Id) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return null;
            }

            return new DatedBringAFriendProduct(package.Id, TargetPackageName, price, applicableDate);
        }

        return null;
    }

    private static string DefaultUnavailableDetail(DateOnly requestedDate) =>
        $"Bring-A-Friend is not available for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.";

    private SourceResult<DatedBringAFriendPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        Results.NotPublished(
            timeChecked,
            requestedDate,
            $"Bring-A-Friend for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)} has not been published yet.");

    private SourceResult<DatedBringAFriendPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        Results.Unavailable(timeChecked, requestedDate, detail);

    private SourceResult<DatedBringAFriendPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check Bring-A-Friend availability right now.") =>
        Results.Error(timeChecked, requestedDate, exception, userFacingDetail);
}
