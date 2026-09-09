using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.Time;

namespace Carowinds.Passdown.Application.OperationalNotes;

/// <summary>
/// Caller-supplied content for creating or updating a persistent note (spec Phase 11A Parts O/P).
/// Deliberately carries no <see cref="OperationalNote.Id"/>, <see cref="OperationalNote.Status"/>, or
/// timestamp — those are owned exclusively by <see cref="IOperationalNotesService"/>, never by the
/// caller, so a caller can never bypass service semantics (e.g. supplying an arbitrary
/// <c>CreatedAtUtc</c> or resolving a note through <see cref="IOperationalNotesService.UpdateAsync"/>
/// instead of the explicit <see cref="IOperationalNotesService.ResolveAsync"/>).
/// </summary>
public sealed record OperationalNoteDraft(
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// Application-layer seam for persistent Operational Notes (spec Phase 11A Part N). Depends only on
/// <see cref="IOperationalNoteRepository"/> and <see cref="IClock"/> — no SQLite detail, no UI
/// presentation logic, and (spec Part S) no dependency capable of writing into an Associate daily
/// draft: nothing here ever copies a persistent note's content into
/// <see cref="Associate.AssociateDraftPayload.OperationalNotes"/>. That remains a distinct future
/// operation (Phase 11D) driven by an explicit user selection, not anything automatic.
/// </summary>
public interface IOperationalNotesService
{
    Task<OperationalNote> CreateAsync(OperationalNoteDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing note's content. Identity, <see cref="OperationalNote.CreatedAtUtc"/>, and <see cref="OperationalNote.Status"/>/<see cref="OperationalNote.ResolvedAtUtc"/> are preserved — this never reopens a resolved note (spec Part R defers Reopen) and never lets a caller-supplied timestamp bypass <see cref="Time.IClock"/> (spec Part P).</summary>
    Task<OperationalNote> UpdateAsync(Guid id, OperationalNoteDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Marks a note Resolved (spec Part Q). Idempotent: resolving an already-resolved note returns it unchanged rather than overwriting its original <see cref="OperationalNote.ResolvedAtUtc"/>.</summary>
    Task<OperationalNote> ResolveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every note (Active and Resolved), in the deterministic default browse order (spec Part W).</summary>
    Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Notes relevant to <paramref name="operatingDate"/> (spec Part J), in relevance order (spec Part W). These are suggestions only — spec Part S: nothing calling this ever results in automatic inclusion in a daily passdown.</summary>
    Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IOperationalNotesService"/>
public sealed class OperationalNotesService(IOperationalNoteRepository repository, IClock clock, ISyncChangeTracker? syncChangeTracker = null) : IOperationalNotesService
{
    public async Task<OperationalNote> CreateAsync(OperationalNoteDraft draft, CancellationToken cancellationToken = default)
    {
        Validate(draft);

        var now = clock.UtcNow;
        var note = new OperationalNote(
            Guid.NewGuid(),
            draft.Title.Trim(),
            NormalizeBody(draft.Body),
            draft.Category,
            draft.Priority,
            draft.EffectiveFrom,
            draft.EffectiveThrough,
            OperationalNoteStatus.Active,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            ResolvedAtUtc: null);

        await repository.AddAsync(note, cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(note.Id, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<OperationalNote> UpdateAsync(Guid id, OperationalNoteDraft draft, CancellationToken cancellationToken = default)
    {
        Validate(draft);

        var existing = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Operational note {id} was not found.");

        var updated = existing with
        {
            Title = draft.Title.Trim(),
            Body = NormalizeBody(draft.Body),
            Category = draft.Category,
            Priority = draft.Priority,
            EffectiveFrom = draft.EffectiveFrom,
            EffectiveThrough = draft.EffectiveThrough,
            UpdatedAtUtc = clock.UtcNow,
        };

        await repository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(updated.Id, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<OperationalNote> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Operational note {id} was not found.");

        if (existing.Status == OperationalNoteStatus.Resolved)
        {
            return existing;
        }

        var now = clock.UtcNow;
        var resolved = existing with
        {
            Status = OperationalNoteStatus.Resolved,
            ResolvedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await repository.UpdateAsync(resolved, cancellationToken).ConfigureAwait(false);
        await MarkChangedAsync(resolved.Id, cancellationToken).ConfigureAwait(false);
        return resolved;
    }

    /// <summary>Phase 16C §7: SQLite save first, sync metadata second — never part of the same write, never skipped for any of the three ways a note's content can change.</summary>
    private Task MarkChangedAsync(Guid noteId, CancellationToken cancellationToken) =>
        syncChangeTracker?.MarkLocalChangeAsync(SyncEntityType.OperationalNote, SyncEntityIdentity.ForNote(noteId), cancellationToken) ?? Task.CompletedTask;

    public async Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default)
    {
        var notes = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return OperationalNoteOrdering.ByDefaultListOrder(notes);
    }

    public async Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var candidates = await repository.ListRelevantForDateAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var relevant = candidates.Where(note => OperationalNoteRelevance.IsRelevantForDate(note, operatingDate));
        return OperationalNoteOrdering.ByRelevance(relevant);
    }

    private static void Validate(OperationalNoteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);

        if (draft.Title.Trim().Length > OperationalNote.MaxTitleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), draft.Title.Length,
                $"Title must be {OperationalNote.MaxTitleLength} characters or fewer.");
        }

        if (draft.Body is { Length: > 0 } body && body.Length > OperationalNote.MaxBodyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), body.Length,
                $"Body must be {OperationalNote.MaxBodyLength} characters or fewer.");
        }

        if (draft.EffectiveThrough is { } effectiveThrough && effectiveThrough < draft.EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), effectiveThrough,
                "EffectiveThrough must be on or after EffectiveFrom.");
        }
    }

    private static string? NormalizeBody(string? body) => string.IsNullOrWhiteSpace(body) ? null : body.Trim();
}
