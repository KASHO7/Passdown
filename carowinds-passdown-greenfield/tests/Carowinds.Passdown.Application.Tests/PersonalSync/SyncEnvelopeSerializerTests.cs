using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.PersonalSync;

/// <summary>Phase 16B §8/§9: the versioned envelope contract — round-trip and malformed-JSON behavior only (no entity is actually wrapped/written yet).</summary>
public class SyncEnvelopeSerializerTests
{
    private static SyncEnvelope BuildEnvelope() => new()
    {
        SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
        EntityType = SyncEntityType.AssociateDraft,
        EntityId = "2026-09-09",
        DeviceId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
        Revision = 3,
        ModifiedUtc = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero),
        IsDeleted = false,
        PayloadSchemaVersion = 1,
        Payload = JsonDocument.Parse("""{"Hello":"World"}""").RootElement.Clone(),
        ContentHash = null,
    };

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsExactly()
    {
        var original = BuildEnvelope();

        var json = SyncEnvelopeSerializer.Serialize(original);
        var roundTripped = SyncEnvelopeSerializer.Deserialize(json);

        Assert.Equal(original.SyncSchemaVersion, roundTripped.SyncSchemaVersion);
        Assert.Equal(original.EntityType, roundTripped.EntityType);
        Assert.Equal(original.EntityId, roundTripped.EntityId);
        Assert.Equal(original.DeviceId, roundTripped.DeviceId);
        Assert.Equal(original.Revision, roundTripped.Revision);
        Assert.Equal(original.ModifiedUtc, roundTripped.ModifiedUtc);
        Assert.Equal(original.IsDeleted, roundTripped.IsDeleted);
        Assert.Equal(original.PayloadSchemaVersion, roundTripped.PayloadSchemaVersion);
        Assert.Equal("World", roundTripped.Payload.GetProperty("Hello").GetString());
        Assert.Equal(original.ContentHash, roundTripped.ContentHash);
    }

    [Fact]
    public void Serialize_UsesEntityTypeNameNotInteger()
    {
        var json = SyncEnvelopeSerializer.Serialize(BuildEnvelope());

        Assert.Contains("\"AssociateDraft\"", json);
    }

    [Fact]
    public void Serialize_ContentHash_DefaultsToNull_ForNonHistoryEntities()
    {
        var json = SyncEnvelopeSerializer.Serialize(BuildEnvelope());
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("ContentHash").ValueKind);
    }

    [Fact]
    public void Deserialize_MalformedJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => SyncEnvelopeSerializer.Deserialize("{ not valid json"));
    }

    [Fact]
    public void Deserialize_MissingRequiredField_Throws()
    {
        const string missingDeviceId = """
            {
              "SyncSchemaVersion": 1,
              "EntityType": "AssociateDraft",
              "EntityId": "2026-09-09",
              "Revision": 1,
              "ModifiedUtc": "2026-09-09T00:00:00+00:00",
              "IsDeleted": false,
              "PayloadSchemaVersion": 1,
              "Payload": {}
            }
            """;

        Assert.ThrowsAny<JsonException>(() => SyncEnvelopeSerializer.Deserialize(missingDeviceId));
    }
}
