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

    [Fact]
    public void ForFinalizedPassdown_UsesLowercaseHyphenatedGuid()
    {
        var id = Guid.Parse("3FA85F64-5717-4562-B3FC-2C963F66AFA6");

        Assert.Equal("3fa85f64-5717-4562-b3fc-2c963f66afa6", SyncEntityIdentity.ForFinalizedPassdown(id));
    }

    [Fact]
    public void TryParseFinalizedPassdownId_RoundTripsAFormattedId()
    {
        var id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        Assert.True(SyncEntityIdentity.TryParseFinalizedPassdownId(SyncEntityIdentity.ForFinalizedPassdown(id), out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("3fa85f64-5717-4562-b3fc-2c963f66afa6-DESKTOP1")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void TryParseFinalizedPassdownId_NonCanonicalText_ReturnsFalse(string text)
    {
        Assert.False(SyncEntityIdentity.TryParseFinalizedPassdownId(text, out _));
    }

    [Fact]
    public void FinalizedPassdownIdentity_IsIndependentOfDate_TwoDifferentIdsSameDateNeverCollide()
    {
        // Phase 16E §2/§14: identity is the Guid, never the date — two independent finalizations for
        // the same operating date must map to two distinct entity ids, never the same one.
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        Assert.NotEqual(SyncEntityIdentity.ForFinalizedPassdown(idA), SyncEntityIdentity.ForFinalizedPassdown(idB));
    }

    // ----- Phase 17B: MonthlyErt / ErtDateOverride -----

    [Fact]
    public void ForMonthlyErt_UsesInvariantYearMonth()
    {
        Assert.Equal("2026-09", SyncEntityIdentity.ForMonthlyErt(2026, 9));
    }

    [Fact]
    public void TryParseMonthlyErtId_RoundTripsAFormattedId()
    {
        Assert.True(SyncEntityIdentity.TryParseMonthlyErtId(SyncEntityIdentity.ForMonthlyErt(2026, 9), out var year, out var month));
        Assert.Equal(2026, year);
        Assert.Equal(9, month);
    }

    [Theory]
    [InlineData("2026-9")]
    [InlineData("2026-09-09")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("not-a-month")]
    [InlineData("")]
    public void TryParseMonthlyErtId_NonCanonicalText_ReturnsFalse(string text)
    {
        Assert.False(SyncEntityIdentity.TryParseMonthlyErtId(text, out _, out _));
    }

    // Spec Part 5/30: September 2026 and September 2027 are different identities.
    [Fact]
    public void MonthlyErtIdentity_SameMonthDifferentYear_NeverCollide()
    {
        Assert.NotEqual(SyncEntityIdentity.ForMonthlyErt(2026, 9), SyncEntityIdentity.ForMonthlyErt(2027, 9));
    }

    [Fact]
    public void ForErtDateOverride_UsesInvariantIsoDate()
    {
        Assert.Equal("2026-09-19", SyncEntityIdentity.ForErtDateOverride(new DateOnly(2026, 9, 19)));
    }

    [Fact]
    public void TryParseErtDateOverrideId_RoundTripsAFormattedId()
    {
        var date = new DateOnly(2026, 9, 19);

        Assert.True(SyncEntityIdentity.TryParseErtDateOverrideId(SyncEntityIdentity.ForErtDateOverride(date), out var parsed));
        Assert.Equal(date, parsed);
    }

    [Theory]
    [InlineData("2026-9-19")]
    [InlineData("not-a-date")]
    [InlineData("")]
    public void TryParseErtDateOverrideId_NonCanonicalText_ReturnsFalse(string text)
    {
        Assert.False(SyncEntityIdentity.TryParseErtDateOverrideId(text, out _));
    }
}
