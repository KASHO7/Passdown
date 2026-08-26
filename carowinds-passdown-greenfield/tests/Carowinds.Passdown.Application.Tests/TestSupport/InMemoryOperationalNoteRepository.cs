using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>In-memory <see cref="IOperationalNoteRepository"/> mirroring the SQLite implementation's shape (a mutable dictionary keyed by <see cref="OperationalNote.Id"/>, no delete method) for Application-layer service tests.</summary>
internal sealed class InMemoryOperationalNoteRepository : IOperationalNoteRepository
{
    private readonly Dictionary<Guid, OperationalNote> _notes = new();

    public Task<OperationalNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notes.GetValueOrDefault(id));

    public Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OperationalNote>>(_notes.Values.ToList());

    public Task AddAsync(OperationalNote note, CancellationToken cancellationToken = default)
    {
        _notes[note.Id] = note;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OperationalNote note, CancellationToken cancellationToken = default)
    {
        _notes[note.Id] = note;
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
