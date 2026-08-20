namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One Manager Passdown manual draft, keyed by operating date and shift (spec §6.2
/// "manager-drafts keyed by date or operating day/shift"). Follows the same
/// opaque-payload/schema-version approach as <see cref="AssociateDraft"/>.
/// </summary>
public sealed record ManagerDraft(
    DateOnly OperatingDate,
    string Shift,
    int SchemaVersion,
    string PayloadJson,
    DateTimeOffset UpdatedAt);

public interface IManagerDraftRepository
{
    Task<ManagerDraft?> GetAsync(DateOnly operatingDate, string shift, CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the draft for <paramref name="draft"/>'s operating date and shift.</summary>
    Task SaveAsync(ManagerDraft draft, CancellationToken cancellationToken = default);
}
