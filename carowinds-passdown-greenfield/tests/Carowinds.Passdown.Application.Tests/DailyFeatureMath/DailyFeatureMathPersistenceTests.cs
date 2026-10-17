using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.DailyFeatureMath;

/// <summary>
/// Phase 21D: math is ordinary Daily Feature text — the editable source survives draft JSON, Personal
/// Sync validation and frozen snapshots byte-for-byte, with no schema change.
/// </summary>
public class DailyFeatureMathPersistenceTests
{
    private const string Source = "Fact:\n\\[\n\\int_0^1 x^2\\,dx=\\frac{1}{3}\n\\]\nand \\(\\frac{\\partial f}{\\partial x}\\), \\\\( literal, ∫₀¹ x² dx = ⅓ — \"quoted\" ’curly’";

    [Fact]
    public void SchemaVersions_AreUnchanged()
    {
        Assert.Equal(4, AssociateDraftPayload.CurrentSchemaVersion);
        Assert.Equal(2, FinalizedPassdownSnapshot.CurrentSchemaVersion);
        Assert.Equal(1, SyncEnvelopeSerializer.CurrentSyncSchemaVersion);
    }

    [Fact]
    public void Draft_RoundTripsTheExactSource()
    {
        var draft = AssociateDraftPayload.CreateDefault() with { DailyFeatureType = DailyFeatureType.UselessFactOfTheDay, DailyFeatureText = Source };

        // The same serialization the draft store and the sync payload use.
        var json = JsonSerializer.Serialize(draft);
        var roundTripped = AssociateDraftPayloadSerializer.Deserialize(json);

        Assert.Equal(Source, roundTripped.DailyFeatureText);
        Assert.Equal(DailyFeatureType.UselessFactOfTheDay, roundTripped.DailyFeatureType);
    }

    [Fact]
    public void Draft_WithMath_IsAValidSyncPayload()
    {
        var draft = AssociateDraftPayload.CreateDefault() with { DailyFeatureType = DailyFeatureType.QuestionOfTheDay, DailyFeatureText = Source };
        Assert.True(SyncPayloadValidator.IsValidDraft(AssociateDraftPayloadSerializer.Deserialize(JsonSerializer.Serialize(draft))));
    }

    [Fact]
    public void FinalizedSnapshot_RoundTripsTheExactSource()
    {
        var snapshot = new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion,
            new DateOnly(2026, 10, 17),
            "Saturday, October 17, 2026",
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "10:00 AM - 9:00 PM", null, null, null, null, null),
            new FinalizedAttendanceSnapshot(null, null),
            new FinalizedEarlyRideTimeSnapshot(false, null, []),
            new FinalizedDailyFeatureSnapshot("Useless Fact of the Day", Source),
            [],
            [],
            HasUnverifiedProductPricing: false,
            GeneratedAtText: "Oct 17 at 6:00 AM");

        var roundTripped = FinalizedPassdownSnapshotSerializer.Deserialize(FinalizedPassdownSnapshotSerializer.Serialize(snapshot));

        Assert.Equal(Source, roundTripped.DailyFeature.Text);
    }
}
