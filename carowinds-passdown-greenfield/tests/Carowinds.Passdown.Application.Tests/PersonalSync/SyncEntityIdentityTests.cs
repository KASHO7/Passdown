using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16C §3/§18/§19: the one centralized entity-id mapping — draft dates and note GUIDs round-trip exactly, and non-canonical text is rejected rather than guessed at.</summary>
public class SyncEntityIdentityTests
{
    [Fact]
    public void ForDraft_UsesInvariantIsoDate()
    {
        Assert.Equal("2026-09-09", SyncEntityIdentity.ForDraft(new DateOnly(2026, 9, 9)));
    }

    [Fact]
    public void TryParseDraftId_RoundTripsAFormattedId()
    {
        var date = new DateOnly(2026, 12, 25);

        Assert.True(SyncEntityIdentity.TryParseDraftId(SyncEntityIdentity.ForDraft(date), out var parsed));
        Assert.Equal(date, parsed);
    }

    [Theory]
    [InlineData("2026-9-9")]
    [InlineData("2026-09-09-DESKTOP1")]
    [InlineData("not-a-date")]
    [InlineData("")]
    public void TryParseDraftId_NonCanonicalText_ReturnsFalse(string text)
    {
        Assert.False(SyncEntityIdentity.TryParseDraftId(text, out _));
    }

    [Fact]
    public void ForNote_UsesLowercaseHyphenatedGuid()
    {
        var id = Guid.Parse("3FA85F64-5717-4562-B3FC-2C963F66AFA6");

        Assert.Equal("3fa85f64-5717-4562-b3fc-2c963f66afa6", SyncEntityIdentity.ForNote(id));
    }

    [Fact]
    public void TryParseNoteId_RoundTripsAFormattedId()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        Assert.True(SyncEntityIdentity.TryParseNoteId(SyncEntityIdentity.ForNote(id), out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6-DESKTOP1")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void TryParseNoteId_NonCanonicalText_ReturnsFalse(string text)
    {
        Assert.False(SyncEntityIdentity.TryParseNoteId(text, out _));
    }
}
