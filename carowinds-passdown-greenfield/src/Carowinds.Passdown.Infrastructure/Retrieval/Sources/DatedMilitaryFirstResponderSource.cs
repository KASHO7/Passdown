using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Official exact-date Military and First Responder ticket adapter (Phase 21A; see
/// docs/phase21a-pricing-historical-integrity.md for the live investigation this is built from).
/// The official Military &amp; First Responders page's "Buy Now" (keyword <c>MilitaryWizard</c>)
/// opens a store with a "Select a date to begin!" calendar that shows each date's Military and First
/// Responder price before any ID.me step — ID.me is only prompted after products are chosen. That
/// calendar's data comes from the same anonymous <c>getkeywordcalendar</c> endpoint every other dated
/// source calls, keyword <c>MilitaryWizardDailyTickets</c> (the "Wizard + DailyTickets" pattern Daily
/// Admission and Bring-A-Friend already follow). Live-confirmed 2026-10-16: the store showed $55.00
/// for Oct 16 and $60.00 for Oct 17, exactly the amounts this keyword returned anonymously. No ID.me,
/// cookie, cart or purchase step is ever touched.
///
/// The response carries separate "Military Ticket" and "First Responder Ticket" packages, sometimes
/// with a seasonal prefix ("Fall Military Ticket") and sometimes both a prefixed and a plain package
/// for the same eligibility on the same date (observed 2026-11-08). <see cref="BuildProducts"/> only
/// produces a price for an eligibility when every one of its packages agrees on one amount — differing
/// amounts would make any single number a guess, so that eligibility is left without an exact price
/// rather than reporting a minimum as exact. Never reads the date-level <c>lowest_price</c> aggregate
/// (spec §5.4). No comparison price exists in this response; the "$80 Gate Price" lives only on the
/// catalog card (<see cref="MilitaryFirstResponderCatalogSource"/>).
///
/// Structurally mirrors <see cref="DatedBringAFriendSource"/>: primary single-day request, horizon
/// fallback only when it comes back empty, past dates never requested.
/// </summary>
public sealed class DatedMilitaryFirstResponderSource(IRetrievalHttpClient httpClient, IClock clock) : IOfficialDataSource<DatedMilitaryFirstResponderPayload>
{
    private const string Keyword = "MilitaryWizardDailyTickets";

    private const string SourceName = "Carowinds Dated Military & First Responder Tickets";

    private const string MilitaryPackageName = "Military Ticket";

    private const string FirstResponderPackageName = "First Responder Ticket";

    /// <summary>Same value the official store and every sibling keyword send.</summary>
    private const string DisplayZeroCapacity = "1";

    private const int HorizonPaddingDays = 120;

    private static readonly Uri OfficialPageUrl = new("https://www.sixflags.com/carowinds/military-discounts");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string SourceId => "dated-military-first-responder.official";

    public SourceCategory Category => SourceCategory.DatedMilitaryFirstResponder;

    public async Task<SourceResult<DatedMilitaryFirstResponderPayload>> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestedDate is not { } requestedDate)
        {
            throw new ArgumentException("Dated Military/First Responder retrieval requires a requested date.", nameof(request));
        }

        var timeChecked = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        var today = DateOnly.FromDateTime(timeChecked.DateTime);

        // The official calendar never offers past dates (live: "No dates found" for 2026-10-10 on
        // 2026-10-16), so a past date is never requested — no history exists to retrieve.
        if (requestedDate < today)
        {
            return Unavailable(timeChecked, requestedDate, "That date has already passed.");
        }

        var primaryBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, requestedDate, requestedDate);
        var primaryResult = await CallCalendarAsync(primaryBody, timeChecked, requestedDate, cancellationToken).ConfigureAwait(false);
        if (primaryResult.IsError)
        {
            return primaryResult.Error!;
        }

        var primaryService = primaryResult.Service!;
        if (string.Equals(primaryService.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return FromConfirmedDate(primaryService, timeChecked, requestedDate);
        }

        var windowEnd = requestedDate > today.AddDays(HorizonPaddingDays) ? requestedDate : today.AddDays(HorizonPaddingDays);
        var horizonBody = AccessoKeywordCalendarRequest.BuildBody(Keyword, DisplayZeroCapacity, today, windowEnd);
        var horizonResult = await CallCalendarAsync(horizonBody, timeChecked, requestedDate, cancellationToken).ConfigureAwait(false);
        if (horizonResult.IsError)
        {
            return horizonResult.Error!;
        }

        var horizonService = horizonResult.Service!;
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
                    $"SERVICE.status was '{horizonService.Status}' (error_msg='{horizonService.ErrorMessage}') for a horizon window that should have included today."));
        }

        var horizonDates = new List<DateOnly>();
        foreach (var entry in horizonService.Dates?.Entries ?? [])
        {
            if (!DateOnly.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var entryDate))
            {
                return Error(timeChecked, requestedDate, new InvalidOperationException($"Unparseable date '{entry.Date}' in horizon calendar response."));
            }

            horizonDates.Add(entryDate);
        }

        if (horizonDates.Count == 0)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException("SERVICE.status was 'OK' but the horizon window's DATES.D contained no entries."));
        }

        // A date beyond the last published one is not published yet; a date inside the published
        // range that has no entry is one the store does not sell (e.g. the park is closed).
        return requestedDate > horizonDates.Max()
            ? NotPublished(timeChecked, requestedDate)
            : Unavailable(
                timeChecked,
                requestedDate,
                $"Military/First Responder tickets are not offered for {FormatDate(requestedDate)}.");
    }

    private SourceResult<DatedMilitaryFirstResponderPayload> FromConfirmedDate(
        AccessoKeywordCalendarService service,
        DateTimeOffset timeChecked,
        DateOnly requestedDate)
    {
        var entries = service.Dates?.Entries ?? [];
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
            return Error(timeChecked, requestedDate, new InvalidOperationException($"Unparseable date '{entries[0].Date}' in calendar response."));
        }

        if (confirmedDate != requestedDate)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException($"Requested {requestedDate:yyyy-MM-dd} but the response confirmed '{entries[0].Date}'."),
                "We couldn't confirm Military/First Responder pricing for the requested date.");
        }

        var built = BuildProducts(entries[0].Packages?.P ?? [], requestedDate);
        if (built.Malformed is { } malformed)
        {
            return Error(timeChecked, requestedDate, new InvalidOperationException(malformed));
        }

        if (built.RecognizedPackageCount == 0)
        {
            return Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Matched date entry for {requestedDate:yyyy-MM-dd} had no '{MilitaryPackageName}' or '{FirstResponderPackageName}' package."));
        }

        if (built.Products.Count == 0)
        {
            // Every eligibility had differing amounts — a successful check, but no single exact price.
            return Unavailable(
                timeChecked,
                requestedDate,
                $"Military/First Responder pricing for {FormatDate(requestedDate)} lists more than one price, so no single exact price is shown.");
        }

        var detail = built.AmbiguousEligibilities.Count == 0
            ? $"Military/First Responder pricing confirmed for {FormatDate(requestedDate)}."
            : $"Military/First Responder pricing confirmed for {FormatDate(requestedDate)}; {string.Join(" and ", built.AmbiguousEligibilities.Select(Describe))} lists more than one price.";

        return SourceResult<DatedMilitaryFirstResponderPayload>.Success(
            SourceId,
            new SourceEvidence(
                SourceName,
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Verified,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate,
                confirmedDate: requestedDate),
            new DatedMilitaryFirstResponderPayload(built.Products));
    }

    /// <summary>
    /// Groups the date's packages by eligibility (official name ending "Military Ticket" or
    /// "First Responder Ticket", tolerating seasonal prefixes such as "Fall ") and keeps one product
    /// per eligibility only when all of its packages agree on the amount. Any other package (e.g. a
    /// future add-on) is ignored. The kept package id prefers the unprefixed package when present so
    /// the identity does not depend on response order.
    /// </summary>
    private static BuiltProducts BuildProducts(IReadOnlyList<AccessoKeywordCalendarPackage> packages, DateOnly applicableDate)
    {
        var byEligibility = new Dictionary<MilitaryFirstResponderEligibility, List<(string Id, string Name, decimal Price)>>();
        var recognized = 0;

        foreach (var package in packages)
        {
            var name = package.Name?.Trim();
            if (string.IsNullOrEmpty(name) || EligibilityOf(name) is not { } eligibility)
            {
                continue;
            }

            recognized++;
            if (string.IsNullOrWhiteSpace(package.Id) ||
                !decimal.TryParse(package.CustomerType?.RetailAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                return new BuiltProducts([], [], recognized, $"Package '{name}' for {applicableDate:yyyy-MM-dd} had no id or no parseable retail_amount.");
            }

            if (!byEligibility.TryGetValue(eligibility, out var list))
            {
                byEligibility[eligibility] = list = [];
            }

            list.Add((package.Id.Trim(), name, price));
        }

        var products = new List<DatedMilitaryFirstResponderProduct>();
        var ambiguous = new List<MilitaryFirstResponderEligibility>();
        foreach (var eligibility in new[] { MilitaryFirstResponderEligibility.Military, MilitaryFirstResponderEligibility.FirstResponder })
        {
            if (!byEligibility.TryGetValue(eligibility, out var list))
            {
                continue;
            }

            if (list.Select(p => p.Price).Distinct().Count() != 1)
            {
                ambiguous.Add(eligibility);
                continue;
            }

            var baseName = BaseName(eligibility);
            var chosen = list.FirstOrDefault(p => string.Equals(p.Name, baseName, StringComparison.Ordinal));
            if (chosen.Id is null)
            {
                chosen = list[0];
            }

            products.Add(new DatedMilitaryFirstResponderProduct(chosen.Id, baseName, eligibility, chosen.Price, applicableDate));
        }

        return new BuiltProducts(products, ambiguous, recognized, Malformed: null);
    }

    private static MilitaryFirstResponderEligibility? EligibilityOf(string packageName) =>
        packageName.EndsWith(FirstResponderPackageName, StringComparison.Ordinal) ? MilitaryFirstResponderEligibility.FirstResponder
        : packageName.EndsWith(MilitaryPackageName, StringComparison.Ordinal) ? MilitaryFirstResponderEligibility.Military
        : null;

    private static string BaseName(MilitaryFirstResponderEligibility eligibility) =>
        eligibility == MilitaryFirstResponderEligibility.Military ? MilitaryPackageName : FirstResponderPackageName;

    private static string Describe(MilitaryFirstResponderEligibility eligibility) =>
        eligibility == MilitaryFirstResponderEligibility.Military ? "Military" : "First Responder";

    private static string FormatDate(DateOnly date) => date.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates);

    private sealed record BuiltProducts(
        IReadOnlyList<DatedMilitaryFirstResponderProduct> Products,
        IReadOnlyList<MilitaryFirstResponderEligibility> AmbiguousEligibilities,
        int RecognizedPackageCount,
        string? Malformed);

    private async Task<CalendarCallResult> CallCalendarAsync(
        string body,
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        CancellationToken cancellationToken)
    {
        RetrievalHttpResponse response;
        try
        {
            response = await httpClient
                .PostJsonAsync(AccessoKeywordCalendarRequest.Endpoint, body, AccessoKeywordCalendarRequest.Headers, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CalendarCallResult.Failed(Error(timeChecked, requestedDate, ex));
        }

        if (!response.IsSuccessStatusCode)
        {
            return CalendarCallResult.Failed(Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"POST {AccessoKeywordCalendarRequest.Endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}")));
        }

        AccessoKeywordCalendarApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AccessoKeywordCalendarApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return CalendarCallResult.Failed(Error(timeChecked, requestedDate, ex));
        }

        if (parsed?.Service is null)
        {
            return CalendarCallResult.Failed(Error(
                timeChecked,
                requestedDate,
                new InvalidOperationException(
                    $"Unexpected response shape from {AccessoKeywordCalendarRequest.Endpoint}: no SERVICE object. Body: {Truncate(response.Body)}")));
        }

        return CalendarCallResult.Succeeded(parsed.Service);
    }

    private SourceResult<DatedMilitaryFirstResponderPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate) =>
        SourceResult<DatedMilitaryFirstResponderPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                SourceName,
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                $"Military/First Responder pricing for {FormatDate(requestedDate)} has not been published yet.",
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedMilitaryFirstResponderPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<DatedMilitaryFirstResponderPayload>.WithoutPayload(
            SourceId,
            new SourceEvidence(
                SourceName,
                OfficialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    private SourceResult<DatedMilitaryFirstResponderPayload> Error(
        DateTimeOffset timeChecked,
        DateOnly requestedDate,
        Exception exception,
        string userFacingDetail = "We couldn't check Military/First Responder pricing right now.") =>
        SourceResult<DatedMilitaryFirstResponderPayload>.WithoutPayload(
            SourceId,
            RetrievalDiagnostics.Error(
                SourceName,
                OfficialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";

    private readonly struct CalendarCallResult
    {
        private CalendarCallResult(AccessoKeywordCalendarService? service, SourceResult<DatedMilitaryFirstResponderPayload>? error)
        {
            Service = service;
            Error = error;
        }

        public AccessoKeywordCalendarService? Service { get; }

        public SourceResult<DatedMilitaryFirstResponderPayload>? Error { get; }

        public bool IsError => Error is not null;

        public static CalendarCallResult Succeeded(AccessoKeywordCalendarService service) => new(service, null);

        public static CalendarCallResult Failed(SourceResult<DatedMilitaryFirstResponderPayload> error) => new(null, error);
    }
}
