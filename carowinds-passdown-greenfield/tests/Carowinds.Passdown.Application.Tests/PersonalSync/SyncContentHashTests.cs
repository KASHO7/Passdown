using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16C §6: deterministic, formatting-independent payload fingerprinting.</summary>
public class SyncContentHashTests
{
    [Fact]
    public void ComputeDraftHash_SamePayloadTwice_ProducesTheSameHash()
    {
        var payload = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 12000 };

        var first = SyncContentHash.ComputeDraftHash(payload);
        var second = SyncContentHash.ComputeDraftHash(payload);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeDraftHash_DifferentPayload_ProducesADifferentHash()
    {
        var a = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 12000 };
        var b = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 15000 };

        Assert.NotEqual(SyncContentHash.ComputeDraftHash(a), SyncContentHash.ComputeDraftHash(b));
    }

    [Fact]
    public void ComputeDraftHash_IsIndependentOfHowThePayloadWasDeserialized()
    {
        // A payload built directly and one round-tripped through the storage serializer must hash identically —
        // the hash must never depend on incidental formatting from wherever the JSON came from (§6).
        var original = AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 9000, PreSolds = 200 };
        var roundTripped = AssociateDraftPayloadSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(original));

        Assert.Equal(SyncContentHash.ComputeDraftHash(original), SyncContentHash.ComputeDraftHash(roundTripped));
    }

    [Fact]
    public void ComputeNoteHash_SamePayloadTwice_ProducesTheSameHash()
    {
        var note = new OperationalNote(
            Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), "Title", "Body", OperationalNoteCategory.Staffing, OperationalNotePriority.Important,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), OperationalNoteStatus.Active,
            new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), null);
        var payload = OperationalNoteSyncPayload.FromDomain(note);

        Assert.Equal(SyncContentHash.ComputeNoteHash(payload), SyncContentHash.ComputeNoteHash(payload));
    }

    [Fact]
    public void ComputeNoteHash_DifferentBody_ProducesADifferentHash()
    {
        var note = new OperationalNote(
            Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), "Title", "Body", OperationalNoteCategory.Staffing, OperationalNotePriority.Important,
            new DateOnly(2026, 9, 1), null, OperationalNoteStatus.Active,
            new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero), null);
        var changed = note with { Body = "Different body" };

        Assert.NotEqual(
            SyncContentHash.ComputeNoteHash(OperationalNoteSyncPayload.FromDomain(note)),
            SyncContentHash.ComputeNoteHash(OperationalNoteSyncPayload.FromDomain(changed)));
    }
}
