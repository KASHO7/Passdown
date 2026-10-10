using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>The sync-wire shape of an <see cref="ErtDateOverride"/> (Phase 17B spec Part 24) — mirrors <see cref="MonthlyErtSyncPayload"/>/<see cref="OperationalNoteSyncPayload"/>.</summary>
public sealed record ErtDateOverrideSyncPayload(
    DateOnly OperatingDate,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Reason,
    DateTimeOffset ModifiedUtc)
{
    public const int CurrentSchemaVersion = 1;

    public static ErtDateOverrideSyncPayload FromDomain(ErtDateOverride dateOverride) => new(
        dateOverride.OperatingDate, dateOverride.State,
        dateOverride.StartTime, dateOverride.Rides, dateOverride.Reason, dateOverride.ModifiedUtc);

    public ErtDateOverride ToDomain() => new(OperatingDate, State, StartTime, Rides, Reason, ModifiedUtc);
}

/// <summary>Canonical (compact, stable-order) JSON for <see cref="ErtDateOverrideSyncPayload"/> — used both for the exported envelope payload and, via <see cref="SyncContentHash"/>, for content fingerprinting.</summary>
public static class ErtDateOverrideSyncPayloadSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ErtDateOverrideSyncPayload payload) => JsonSerializer.Serialize(payload, Options);

    public static ErtDateOverrideSyncPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<ErtDateOverrideSyncPayload>(json, Options)
            ?? throw new InvalidOperationException("ERT date override sync payload JSON deserialized to null.");

    public static ErtDateOverrideSyncPayload Deserialize(JsonElement payload) =>
        payload.Deserialize<ErtDateOverrideSyncPayload>(Options)
            ?? throw new InvalidOperationException("ERT date override sync payload JSON deserialized to null.");
}
