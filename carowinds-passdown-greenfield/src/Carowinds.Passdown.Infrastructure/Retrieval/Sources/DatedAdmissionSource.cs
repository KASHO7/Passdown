using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official Carowinds exact-date admission-ticket adapter (Phase 3C; see
/// docs/retrieval/dated-admission-source.md for the live investigation this is built from). Calls
/// the same public, unauthenticated accesso Passport JSON endpoint the official
/// `cf-ca.buy.sixflags.com` dated-ticket checkout itself calls once its embedded date-picker
/// wizard loads — confirmed by a real browser network trace, not guessed — so this uses
/// <see cref="RetrievalStrategy.DirectApi"/> rather than Playwright (spec §6.4: prefer a
/// dependable direct endpoint once one is actually observed). No cart/session bootstrap call is
/// needed first — directly confirmed live (docs) that the calendar endpoint accepts anonymous
/// requests without a prior `GetNewCartID` call.
/// </summary>
public sealed class DatedAdmissionSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedAdmissionPayload>
{
    private const string Keyword = "SnapWizardDailyTickets";

    /// <summary>
    /// How far past "today" the adapter looks to discover the calendar's current published
    /// horizon (docs/retrieval/dated-admission-source.md: live-observed horizon was ~80 days out
    /// at investigation time). Padded well beyond that observed figure so a requested date just
    /// past the true (and seasonally shifting) horizon is still reliably classified
    /// <see cref="RetrievalStatus.NotPublished"/> rather than mistaken for
    /// <see cref="RetrievalStatus.Unavailable"/> — see the max-date comparison in <see cref="RetrieveAsync"/>.
    /// </summary>
    private const int HorizonPaddingDays = 120;

    /// <summary>Directly observed for this keyword (docs/retrieval/dated-admission-source.md) — Fast Lane's `FastLaneOnly` keyword uses `"0"` instead, confirmed independently in Phase 3D (docs/retrieval/dated-fast-lane-source.md).</summary>
    private const string DisplayZeroCapacity = "1";

    /// <summary>
    /// The human-facing official page (spec §5.1) — used as <see cref="SourceEvidence.SourceUrl"/>
    /// so evidence points somewhere a person can verify, rather than the accesso API implementation
    /// detail. The exact endpoint actually queried is preserved in
    /// <see cref="SourceEvidence.TechnicalDetail"/> on failure instead.
    /// </summary>
    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/daily-tickets");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "dated-admission.official";

    public SourceCategory Category => SourceCategory.DatedAdmissionTickets;

    public async Task<SourceResult<DatedAdmissionPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated admission retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        // A date already in the past can never have an orderable dated-admission product — directly
        // confirmed live (yesterday-relative-to-"today" returned the same "No dates found" shape as
        // every other unavailable case; docs/retrieval/dated-admission-source.md). Short-circuiting
        // here avoids a network call and the unbounded look-back window a very old requested date
        // would otherwise force (spec §6.4 efficiency).
        if (requestedDate < today)
        {
            return Unavailable(timeChecked, requestedDate, "That date has already passed.");
        }

        var windowEnd = requestedDate > today.AddDays(HorizonPaddingDays) ? requestedDate : today.AddDays(HorizonPaddingDays);
        var body = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, today, windowEnd);

        RetrievalHttpResponse response;
        try
        {
            response = await httpClient.PostJsonAsync(AccessoKeywordCalendarRequest.Endpoint, body, AccessoKeywordCalendarRequest.Headers, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(timeChecked, requestedDate, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"POST {AccessoKeywordCalendarRequest.Endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        AccessoKeywordCalendarApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AccessoKeywordCalendarApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error(timeChecked, requestedDate, ex);
        }

        var service = parsed?.Service;
        if (service is null)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException($"Unexpected response shape from {AccessoKeywordCalendarRequest.Endpoint}: no SERVICE object. Body: {Truncate(response.Body)}"));
        }

        // Phase 19B: the endpoint's own "no dates found" answer for a window starting today is a
        // successful "nothing published from today onward" check, never a retrieval failure
        // (live-confirmed shape; see AccessoKeywordCalendarService.IsNoDatesFound).
        if (service.IsNoDatesFound)
        {
            return NotPublished(timeChecked, requestedDate);
        }

        if (!string.Equals(service.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            // The requested window always spans from "today" through at least HorizonPaddingDays,
            // and every live check performed during investigation returned at least one date within
            // that span. A totally empty result here has never been observed, so it is treated as an
            // unexpected shape rather than guessed at as a specific business status (CLAUDE.md
            // "Retrieval rules": never invent data).
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"SERVICE.status was '{service.Status}' (error_msg='{service.ErrorMessage}') for a window that should have included today."),
                "We couldn't check dated admission tickets right now.");
        }

        var entries = service.Dates?.Entries ?? [];
        var parsedEntries = new List<(DateOnly Date, AccessoKeywordCalendarDateEntry Entry)>(entries.Count);
        foreach (var entry in entries)
        {
            if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var entryDate))
            {
                return Error(
                    timeChecked,
                    requestedDate,
                    new InvalidOperationException($"Unparseable date '{entry.Date}' in calendar response. Body: {Truncate(response.Body)}"));
            }

            parsedEntries.Add((entryDate, entry));
        }

        if (parsedEntries.Count == 0)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException($"SERVICE.status was 'OK' but DATES.D contained no entries. Body: {Truncate(response.Body)}"));
        }

        var match = parsedEntries.Find(e => e.Date == requestedDate);
        if (match.Entry is null)
        {
            var maxDate = parsedEntries.Max(e => e.Date);
            return requestedDate > maxDate
                ? NotPublished(timeChecked, requestedDate)
                : Unavailable(timeChecked, requestedDate, DefaultUnavailableDetail(requestedDate));
        }

        var packages = match.Entry.Packages?.P ?? [];
        var products = BuildProducts(packages, requestedDate);
        if (products is null)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException($"Matched date entry for {requestedDate:yyyy-MM-dd} had no usable packages. Body: {Truncate(response.Body)}"));
        }

        return SourceResult<DatedAdmissionPayload>.Success(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Admission Tickets",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                $"Dated admission tickets confirmed for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate,
                confirmedDate: requestedDate),
            new DatedAdmissionPayload(products));
    }

    /// <summary>
    /// Groups packages by display name and, per spec §5.4's "never substitute", only ever reads
    /// prices from packages whose date has already been confirmed to equal the requested date by
    /// the caller. Within a name group, the lower price is the standard exact-date price and a
    /// second (higher) price, when present, is retained as <see cref="DatedAdmissionProduct.ComparisonPrice"/>
    /// — a directly-observed second price from this same response, not a fabricated value.
    /// <see cref="DatedAdmissionProduct.PriceQualifier"/> is deliberately left <c>null</c>: the
    /// live `getkeywordcalendar` response never carries an explicit label for why a second price
    /// exists (no field on <see cref="AccessoKeywordCalendarPackage"/> or
    /// <see cref="AccessoKeywordCalendarCustomerType"/> says so). An earlier version of this
    /// adapter inferred `"Same-day pricing"` from a one-time, investigation-only cross-reference
    /// against the separate static package catalog — that catalog is not fetched at runtime, so
    /// that label was not official-source evidence for a live response and has been removed (spec
    /// §5.2's "never fabricate or silently substitute"; see
    /// docs/retrieval/dated-admission-source.md's "PriceQualifier is not populated" section). If
    /// the endpoint ever starts returning an explicit qualifier field for a price, read it here
    /// instead of continuing to leave this <c>null</c>. Never reads the date-level `lowest_price`
    /// aggregate field (always `"0.00"` in every live response observed, and a marketing/aggregate
    /// figure even when populated — spec §5.4's "Starting at" rule).
    /// </summary>
    private static List<DatedAdmissionProduct>? BuildProducts(
        IReadOnlyList<AccessoKeywordCalendarPackage> packages,
        DateOnly applicableDate)
    {
        var byName = new Dictionary<string, List<(string Id, decimal Price)>>();
        foreach (var package in packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) ||
                string.IsNullOrWhiteSpace(package.Name) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return null;
            }

            if (!byName.TryGetValue(package.Name, out var group))
            {
                group = [];
                byName[package.Name] = group;
            }

            group.Add((package.Id, price));
        }

        if (byName.Count == 0)
        {
            return null;
        }

        var products = new List<DatedAdmissionProduct>(byName.Count);
        foreach (var (name, group) in byName)
        {
            // Exactly one or two entries per name is the only shape ever observed live (a single
            // price, or a standard/comparison pair). A third differently-named-but-same-title
            // entry has no defined pairing rule — rather than silently drop it (min/max would
            // discard everything but the extremes), treat it as an unexpected response shape.
            if (group.Count > 2)
            {
                return null;
            }

            var lowest = group.MinBy(p => p.Price);
            var highest = group.MaxBy(p => p.Price);
            var hasComparison = highest.Price > lowest.Price;

            products.Add(new DatedAdmissionProduct(
                lowest.Id,
                name,
                lowest.Price,
                hasComparison ? highest.Price : null,
                // PriceQualifier: never explicitly provided by this endpoint — see remarks above.
                null,
                applicableDate));
        }

        return products;
    }

    private static string DefaultUnavailableDetail(DateOnly requestedDate) =>
        $"Dated admission tickets are not available for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}.";

    private SourceResult<DatedAdmissionPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        SourceResult<DatedAdmissionPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Admission Tickets",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                $"Dated admission tickets for {requestedDate.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)} have not been published yet.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedAdmissionPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<DatedAdmissionPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                "Carowinds Dated Admission Tickets",
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedAdmissionPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check dated admission tickets right now.") =>
        SourceResult<DatedAdmissionPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                "Carowinds Dated Admission Tickets",
                OfficialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}
