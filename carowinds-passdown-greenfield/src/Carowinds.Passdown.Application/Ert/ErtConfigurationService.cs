using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Ert;

/// <summary>Caller-supplied content for saving a month's Early Ride Time configuration (Phase 17B spec Part 2). No <see cref="MonthlyErtConfiguration.ModifiedUtc"/> — owned exclusively by <see cref="IErtConfigurationService"/>.</summary>
public sealed record MonthlyErtDraft(
    int Year,
    int Month,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Note);

/// <summary>Caller-supplied content for saving a date's Early Ride Time override (Phase 17B spec Part 3).</summary>
public sealed record ErtDateOverrideDraft(
    DateOnly OperatingDate,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Reason);

/// <summary>
/// Application-layer seam for monthly Early Ride Time configuration and date overrides (Phase 17B
/// spec Part 20) — owns validation, normalization, and monthly/override save semantics. Depends only
/// on the two repositories and <see cref="IClock"/>, mirroring <see cref="OperationalNotes.OperationalNotesService"/>;
/// resolution (read-side precedence) is deliberately a separate seam, <see cref="IEffectiveErtResolver"/>,
/// so a caller that only needs the effective value never pulls in save/validate concerns.
/// </summary>
public interface IErtConfigurationService
{
    Task<MonthlyErtConfiguration?> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>Validates and upserts the month's configuration (spec Part 33). A month is always a full replace — there is no field-level partial update.</summary>
    Task<MonthlyErtConfiguration> SaveMonthlyAsync(MonthlyErtDraft draft, CancellationToken cancellationToken = default);

    Task<ErtDateOverride?> GetOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    Task<ErtDateOverride> SaveOverrideAsync(ErtDateOverrideDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Removes the date's override so the date immediately falls back to the monthly default (spec Part 8/28). A no-op when no override exists.</summary>
    Task RemoveOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IErtConfigurationService"/>
public sealed class ErtConfigurationService(
    IMonthlyErtRepository monthlyRepository,
    IErtDateOverrideRepository overrideRepository,
    IClock clock,
    ISyncChangeTracker? syncChangeTracker = null) : IErtConfigurationService
{
    public Task<MonthlyErtConfiguration?> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default) =>
        monthlyRepository.GetAsync(year, month, cancellationToken);

    public async Task<MonthlyErtConfiguration> SaveMonthlyAsync(MonthlyErtDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateMonth(draft.Month);

        var (state, startTime, rides) = Normalize(draft.State, draft.StartTime, draft.Rides);
        var configuration = new MonthlyErtConfiguration(
            draft.Year,
            draft.Month,
            state,
            startTime,
            rides,
            NormalizeNote(draft.Note),
            clock.UtcNow);

        await monthlyRepository.UpsertAsync(configuration, cancellationToken: cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(draft.Year, draft.Month), cancellationToken).ConfigureAwait(false);
        return configuration;
    }

    public Task<ErtDateOverride?> GetOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) =>
        overrideRepository.GetAsync(operatingDate, cancellationToken);

    public async Task<ErtDateOverride> SaveOverrideAsync(ErtDateOverrideDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var (state, startTime, rides) = Normalize(draft.State, draft.StartTime, draft.Rides);
        var dateOverride = new ErtDateOverride(
            draft.OperatingDate,
            state,
            startTime,
            rides,
            NormalizeNote(draft.Reason),
            clock.UtcNow);

        await overrideRepository.UpsertAsync(dateOverride, cancellationToken: cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(SyncEntityType.ErtDateOverride, SyncEntityIdentity.ForErtDateOverride(draft.OperatingDate), cancellationToken).ConfigureAwait(false);
        return dateOverride;
    }

    public async Task RemoveOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var existing = await overrideRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        await overrideRepository.DeleteAsync(operatingDate, cancellationToken: cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(SyncEntityType.ErtDateOverride, SyncEntityIdentity.ForErtDateOverride(operatingDate), cancellationToken).ConfigureAwait(false);
    }

    private Task MarkChangedAsync(SyncEntityType entityType, string entityId, CancellationToken cancellationToken) =>
        syncChangeTracker?.MarkLocalChangeAsync(entityType, entityId, cancellationToken) ?? Task.CompletedTask;

    private static void ValidateMonth(int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }
    }

    /// <summary>
    /// Spec Part 33: a configuration must never be simultaneously "no ERT" and carry a start
    /// time/rides. Rather than reject an inconsistent caller-supplied combination outright (a remote
    /// sync payload must never crash reconciliation), this normalizes: <see cref="ErtConfigurationState.NoErt"/>
    /// always clears <see cref="TimeOnly"/>/rides; <see cref="ErtConfigurationState.Configured"/>
    /// requires a start time. Rides are trimmed, blank entries dropped, and case-insensitive
    /// duplicates collapsed to the first occurrence — an empty ride list is otherwise allowed (spec
    /// Part 33's "empty ride selection if allowed": allowed, since a start time alone is still
    /// meaningful operational content).
    /// </summary>
    private static (ErtConfigurationState State, TimeOnly? StartTime, IReadOnlyList<string> Rides) Normalize(
        ErtConfigurationState state, TimeOnly? startTime, IReadOnlyList<string>? rides)
    {
        if (state == ErtConfigurationState.NoErt)
        {
            return (state, null, []);
        }

        if (startTime is null)
        {
            throw new ArgumentException("A configured Early Ride Time requires a start time.", nameof(startTime));
        }

        var normalizedRides = (rides ?? [])
            .Select(ride => ride.Trim())
            .Where(ride => ride.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (state, startTime, normalizedRides);
    }

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
