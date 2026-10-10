using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IOperationalNotesService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.OperationalNotesViewModel"/> tests — an in-memory stand-in, never touches SQLite. Mirrors the real <c>OperationalNotesService</c>'s semantics (validation, resolve idempotency, no delete) closely enough for view-model tests, without being a second copy of its business logic under test elsewhere.</summary>
internal sealed class FakeOperationalNotesService : IOperationalNotesService
{
    private readonly List<OperationalNote> _notes = [];

    public Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public Exception? ListException { get; set; }

    public Exception? CreateException { get; set; }

    public Exception? UpdateException { get; set; }

    public Exception? ResolveException { get; set; }

    public int ListCallCount { get; private set; }

    public IReadOnlyList<OperationalNote> Seed(params OperationalNote[] notes)
    {
        _notes.AddRange(notes);
        return _notes;
    }

    public Task<OperationalNote> CreateAsync(OperationalNoteDraft draft, CancellationToken cancellationToken = default)
    {
        if (CreateException is not null)
        {
            throw CreateException;
        }

        Validate(draft);

        var now = Now();
        var note = new OperationalNote(
            Guid.NewGuid(),
            draft.Title.Trim(),
            NormalizeBody(draft.Body),
            draft.Category,
            draft.Priority,
            draft.EffectiveFrom,
            draft.EffectiveThrough,
            OperationalNoteStatus.Active,
            now,
            now,
            null);

        _notes.Add(note);
        return Task.FromResult(note);
    }

    public Task<OperationalNote> UpdateAsync(Guid id, OperationalNoteDraft draft, CancellationToken cancellationToken = default)
    {
        if (UpdateException is not null)
        {
            throw UpdateException;
        }

        Validate(draft);

        var index = _notes.FindIndex(n => n.Id == id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Operational note {id} was not found.");
        }

        var updated = _notes[index] with
        {
            Title = draft.Title.Trim(),
            Body = NormalizeBody(draft.Body),
            Category = draft.Category,
            Priority = draft.Priority,
            EffectiveFrom = draft.EffectiveFrom,
            EffectiveThrough = draft.EffectiveThrough,
            UpdatedAtUtc = Now(),
        };
        _notes[index] = updated;
        return Task.FromResult(updated);
    }

    public Task<OperationalNote> ResolveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (ResolveException is not null)
        {
            throw ResolveException;
        }

        var index = _notes.FindIndex(n => n.Id == id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Operational note {id} was not found.");
        }

        if (_notes[index].Status == OperationalNoteStatus.Resolved)
        {
            return Task.FromResult(_notes[index]);
        }

        var now = Now();
        var resolved = _notes[index] with { Status = OperationalNoteStatus.Resolved, ResolvedAtUtc = now, UpdatedAtUtc = now };
        _notes[index] = resolved;
        return Task.FromResult(resolved);
    }

    public Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default)
    {
        ListCallCount++;
        if (ListException is not null)
        {
            throw ListException;
        }

        return Task.FromResult(OperationalNoteOrdering.ByDefaultListOrder(_notes));
    }

    public Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var relevant = _notes.Where(n => OperationalNoteRelevance.IsRelevantForDate(n, operatingDate));
        return Task.FromResult(OperationalNoteOrdering.ByRelevance(relevant));
    }

    private DateTimeOffset Now() => UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow;

    private static string? NormalizeBody(string? body) => string.IsNullOrWhiteSpace(body) ? null : body.Trim();

    private static void Validate(OperationalNoteDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Title);

        if (draft.Title.Trim().Length > OperationalNote.MaxTitleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), draft.Title.Length, "Title too long.");
        }

        if (draft.Body is { Length: > 0 } body && body.Length > OperationalNote.MaxBodyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), body.Length, "Body too long.");
        }

        if (draft.EffectiveThrough is { } effectiveThrough && effectiveThrough < draft.EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), effectiveThrough, "EffectiveThrough must be on or after EffectiveFrom.");
        }
    }
}
