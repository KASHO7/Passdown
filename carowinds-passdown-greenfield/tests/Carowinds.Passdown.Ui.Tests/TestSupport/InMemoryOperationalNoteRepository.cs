using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>In-memory <see cref="IOperationalNoteRepository"/> mirroring the SQLite implementation's shape (a mutable dictionary keyed by <see cref="OperationalNote.Id"/>), used together with the real <see cref="OperationalNotesService"/> so <see cref="Carowinds.Passdown.Desktop.ViewModels.OperationalNotesViewModel"/> integration tests exercise real Application-layer logic end to end (spec Phase 11B Part AJ) rather than <c>FakeOperationalNotesService</c>'s reimplementation. <see cref="DeleteAsync"/> exists only as the Phase 16D sync-tombstone primitive (§13) — no product delete action calls it.</summary>
internal sealed class InMemoryOperationalNoteRepository : IOperationalNoteRepository
{
    private readonly Dictionary<Guid, OperationalNote> _notes = new();

    public Task<OperationalNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notes.GetValueOrDefault(id));

    public Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OperationalNote>>(_notes.Values.ToList());

    public Task AddAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _notes[note.Id] = note;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OperationalNote note, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _notes[note.Id] = note;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, IUnitOfWork? unitOfWork = null, CancellationToken cancellationToken = default)
    {
        _notes.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OperationalNote> relevant = _notes.Values
            .Where(note => note.Status == OperationalNoteStatus.Active
                && note.EffectiveFrom <= operatingDate
                && (note.EffectiveThrough is null || operatingDate <= note.EffectiveThrough))
            .ToList();

        return Task.FromResult(relevant);
    }
}
