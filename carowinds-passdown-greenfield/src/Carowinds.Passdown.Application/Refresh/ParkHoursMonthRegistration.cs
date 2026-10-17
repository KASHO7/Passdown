using System.Globalization;
using System.Text.Json;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Refresh;

/// <summary>
/// Phase 22E.1: Deep Refresh's forward published-schedule discovery for Park Hours. Injected into
/// <see cref="RefreshOrchestrator"/>; when present, Deep Refresh reads the near-term rolling window
/// one date at a time (unchanged priority) and every later date through
/// <see cref="Freshness.ParkHoursCoveragePlanner.DiscoveryHorizon"/> with one official month request
/// per month, instead of one request per date through the end of next month. It is not a second
/// polling engine: it only runs inside a Deep Refresh (daily maintenance, the manual button, the
/// Check Online bootstrap).
/// </summary>
public sealed class ParkHoursScheduleDiscovery(
    IParkHoursScheduleSource source,
    IDatedResultRepository datedResultRepository,
    int monthsAhead = Freshness.ParkHoursCoveragePlanner.DefaultDiscoveryMonths)
{
    public int MonthsAhead { get; } = Math.Clamp(monthsAhead, 1, Freshness.ParkHoursCoveragePlanner.MaxDiscoveryMonths);

    /// <summary>One month registration per calendar month touched by <paramref name="firstDate"/>..<paramref name="lastDate"/>; each stores only dates inside that range.</summary>
    public IReadOnlyList<(ParkHoursMonthRegistration Registration, DateOnly MonthStart)> PlanMonths(DateOnly firstDate, DateOnly lastDate)
    {
        if (lastDate < firstDate)
        {
            return [];
        }

        var registration = new ParkHoursMonthRegistration(source, datedResultRepository, firstDate, lastDate);
        return Freshness.ParkHoursCoveragePlanner.MonthsBetween(firstDate, lastDate)
            .Select(monthStart => (registration, monthStart))
            .ToList();
    }
}

/// <summary>
/// Phase 22E.1: one official month request (<see cref="ExecuteAsync"/>'s date is the month's first day),
/// persisted per date through the same <see cref="IDatedResultRepository"/> rows single-date checks use,
/// so the Calendar, Associate and PDFs read the results with no new storage. Rules:
/// <list type="bullet">
/// <item>Only dates inside this job's discovery range are stored (the rolling window is left to the
/// single-date requests, which keep priority).</item>
/// <item>A date the response does not list is not touched — never inferred closed.</item>
/// <item>A month request that fails stores nothing; every prior value stays (last-known-good).</item>
/// <item>An unreadable entry records a failed attempt only, leaving that date's last-known-good value.</item>
/// <item>A "not published" answer never replaces a date's stored Verified hours or confirmed Closed
/// status: from a month listing it is ambiguous coverage, not a retraction. Explicit changes (new hours,
/// a newly confirmed closure) are applied; a single-date check (rolling window, Check Online, the
/// Associate date check) still applies a genuine unpublish.</item>
/// </list>
/// Shares the Park Hours source id, so the source's scheduling row aggregates it with the daily requests.
/// </summary>
public sealed class ParkHoursMonthRegistration : SourceRegistration
{
    private readonly IParkHoursScheduleSource _source;
    private readonly IDatedResultRepository _datedResultRepository;
    private readonly DateOnly _firstDate;
    private readonly DateOnly _lastDate;

    public ParkHoursMonthRegistration(IParkHoursScheduleSource source, IDatedResultRepository datedResultRepository, DateOnly firstDate, DateOnly lastDate)
        : base(source.SourceId, SourceCategory.ParkHours, requiresBrowserSession: false)
    {
        _source = source;
        _datedResultRepository = datedResultRepository;
        _firstDate = firstDate;
        _lastDate = lastDate;
    }

    public override SourceScope Scope => SourceScope.Dated;

    public override async Task<SourceOperationResult> ExecuteAsync(DateOnly? requestedDate, CancellationToken cancellationToken)
    {
        if (requestedDate is not { } monthDate)
        {
            throw new ArgumentException("A published-schedule month operation requires the month's first date.", nameof(requestedDate));
        }

        var monthStart = new DateOnly(monthDate.Year, monthDate.Month, 1);
        var monthLabel = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var month = await _source.RetrieveMonthAsync(monthStart.Year, monthStart.Month, cancellationToken).ConfigureAwait(false);
        if (month.Failure is { } failure)
        {
            return SourceOperationResult.Failure(SourceId, Category, monthStart, failure.Status, failure.Detail) with { CoveredDates = [] };
        }

        var rangeStart = monthStart > _firstDate ? monthStart : _firstDate;
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var rangeEnd = monthEnd < _lastDate ? monthEnd : _lastDate;

        var covered = new List<DateOnly>();
        var errors = 0;
        var keptExisting = 0;
        foreach (var result in month.Dates)
        {
            if (result.Evidence.RequestedDate is not { } date || date < rangeStart || date > rangeEnd)
            {
                continue;
            }

            var key = new DatedResultKey(Category, date);
            var status = result.Evidence.Status;
            if (!status.IsSuccessfulCompletion())
            {
                await _datedResultRepository.RecordFailedAttemptAsync(key, result.Evidence, cancellationToken).ConfigureAwait(false);
                errors++;
                continue;
            }

            if (status == RetrievalStatus.NotPublished)
            {
                var existing = await _datedResultRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
                if (existing?.SuccessEvidence?.Status is RetrievalStatus.Verified or RetrievalStatus.Closed)
                {
                    keptExisting++;
                    covered.Add(date);
                    continue;
                }
            }

            var payloadJson = result.Payload is null ? null : JsonSerializer.Serialize(result.Payload);
            await _datedResultRepository.RecordSuccessAsync(key, result.Evidence, payloadJson, cancellationToken).ConfigureAwait(false);
            covered.Add(date);
        }

        var expected = rangeEnd < rangeStart ? 0 : rangeEnd.DayNumber - rangeStart.DayNumber + 1;
        var notListed = expected - covered.Count - errors;
        var detail = $"Published park schedule for {monthLabel}: {covered.Count} of {expected} dates recorded";
        if (notListed > 0)
        {
            detail += $"; {notListed} not listed by the official source (left unchanged)";
        }

        if (keptExisting > 0)
        {
            detail += $"; {keptExisting} kept their earlier verified result";
        }

        if (errors > 0)
        {
            detail += $"; {errors} could not be read (last-known-good kept)";
            return SourceOperationResult.Failure(SourceId, Category, monthStart, RetrievalStatus.Error, detail + ".") with { CoveredDates = covered };
        }

        return SourceOperationResult.Success(SourceId, Category, monthStart, RetrievalStatus.Verified, detail + ".") with { CoveredDates = covered };
    }
}
