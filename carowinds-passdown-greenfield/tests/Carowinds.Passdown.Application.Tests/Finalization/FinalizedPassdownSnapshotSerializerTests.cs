using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Application.Tests.Finalization;

public class FinalizedPassdownSnapshotSerializerTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);

    private static FinalizedPassdownSnapshot Sample(string operationalNote = "Fury 325 closed for maintenance.") => new(
        FinalizedPassdownSnapshot.CurrentSchemaVersion,
        Date,
        "Tuesday, August 25, 2026",
        new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.Open, "10:00 AM - 9:00 PM", "Manual Override", "9:30 AM", "9:15 AM", "Last verified Aug 25 at 6:00 AM", null),
        new FinalizedAttendanceSnapshot(18500, 4200),
        new FinalizedEarlyRideTimeSnapshot(true, "Early Ride Time from 8:30 AM.", ["Fury 325", "Intimidator"]),
        new FinalizedDailyFeatureSnapshot("Question of the Day", "What year did Carowinds open?"),
        [operationalNote, "Extra staffing at North Gate."],
        [
            new FinalizedProductCategorySnapshot("Daily Admission", FinalizedProductSectionTier.KeySelling,
            [
                new FinalizedProductRowSnapshot("1-Day Ticket", "$64.99", "$79.99", null, null),
            ]),
        ],
        HasUnverifiedProductPricing: false,
        GeneratedAtText: "Aug 25 at 6:00 AM");

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsEveryField()
    {
        // Re-serializing the deserialized snapshot and comparing JSON text (rather than
        // Assert.Equal(snapshot, roundTripped) directly) is deliberate: the record's compiler-
        // generated Equals compares IReadOnlyList<T>-typed fields by reference, not by content, so
        // two structurally identical snapshots built from separately-materialized lists (exactly
        // what deserialization produces) would spuriously fail a direct record-equality check.
        var snapshot = Sample();
        var json = FinalizedPassdownSnapshotSerializer.Serialize(snapshot);

        var roundTripped = FinalizedPassdownSnapshotSerializer.Deserialize(json);
        var roundTrippedJson = FinalizedPassdownSnapshotSerializer.Serialize(roundTripped);

        Assert.Equal(json, roundTrippedJson);

        // Scalar/nested-record fields (no collection-typed member of their own) are safe to assert
        // directly via record equality.
        Assert.Equal(snapshot.PassdownDate, roundTripped.PassdownDate);
        Assert.Equal(snapshot.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(snapshot.ParkHours, roundTripped.ParkHours);
        Assert.Equal(snapshot.Attendance, roundTripped.Attendance);
        Assert.Equal(snapshot.DailyFeature, roundTripped.DailyFeature);
        Assert.Equal(snapshot.OperationalNotes, roundTripped.OperationalNotes);
        Assert.Equal(snapshot.EarlyRideTime.ParticipatingAttractions, roundTripped.EarlyRideTime.ParticipatingAttractions);
    }

    [Fact]
    public void Serialize_StoresSchemaVersion()
    {
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Sample());

        Assert.Contains($"\"SchemaVersion\":{FinalizedPassdownSnapshot.CurrentSchemaVersion}", json);
    }

    [Fact]
    public void Serialize_EnumsAreWrittenAsStableNamesNotIntegers()
    {
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Sample());

        Assert.Contains("\"Status\":\"Open\"", json);
        Assert.Contains("\"Tier\":\"KeySelling\"", json);
    }

    [Fact]
    public void ComputeContentHash_SameJsonTwice_ProducesSameHash()
    {
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Sample());

        var first = FinalizedPassdownSnapshotSerializer.ComputeContentHash(json);
        var second = FinalizedPassdownSnapshotSerializer.ComputeContentHash(json);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeContentHash_IsLowercaseHexSha256()
    {
        var json = FinalizedPassdownSnapshotSerializer.Serialize(Sample());

        var hash = FinalizedPassdownSnapshotSerializer.ComputeContentHash(json);

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeContentHash_DifferentSnapshotContent_ProducesDifferentHash()
    {
        var jsonA = FinalizedPassdownSnapshotSerializer.Serialize(Sample("Fury 325 closed for maintenance."));
        var jsonB = FinalizedPassdownSnapshotSerializer.Serialize(Sample("Different note entirely."));

        var hashA = FinalizedPassdownSnapshotSerializer.ComputeContentHash(jsonA);
        var hashB = FinalizedPassdownSnapshotSerializer.ComputeContentHash(jsonB);

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void Serialize_TwoEqualSnapshotsBuiltIndependently_ProduceIdenticalJson()
    {
        var jsonA = FinalizedPassdownSnapshotSerializer.Serialize(Sample());
        var jsonB = FinalizedPassdownSnapshotSerializer.Serialize(Sample());

        Assert.Equal(jsonA, jsonB);
    }
}
