namespace Carowinds.Passdown.Application.Persistence;

/// <summary>
/// One Associate Passdown manual draft, keyed by operating date (spec §6.2).
/// <see cref="PayloadJson"/> is opaque to persistence — the Associate form's own
/// schema evolves independently of storage; <see cref="SchemaVersion"/> lets a
/// future reader detect and migrate older payload shapes.
/// </summary>
public sealed record AssociateDraft(
    DateOnly OperatingDate,
    int SchemaVersion,
    string PayloadJson,
    DateTimeOffset UpdatedAt);

public interface IAssociateDraftRepository
{
    Task<AssociateDraft?> GetAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);

    /// <summary>Inserts or fully replaces the draft for <paramref name="draft"/>'s operating date.</summary>
    Task SaveAsync(AssociateDraft draft, CancellationToken cancellationToken = default);
}
