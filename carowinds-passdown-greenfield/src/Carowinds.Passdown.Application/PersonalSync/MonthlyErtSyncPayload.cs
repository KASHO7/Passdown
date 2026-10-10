using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The sync-wire shape of a <see cref="MonthlyErtConfiguration"/> (Phase 17B spec Part 24) — mirrors
/// <see cref="OperationalNoteSyncPayload"/> exactly. Only user-authored ERT configuration is included
/// (spec Part 24: no park-hour cache, no public retrieval data, no source status).
/// </summary>
public sealed record MonthlyErtSyncPayload(
    int Year,
    int Month,
    ErtConfigurationState State,
    TimeOnly? StartTime,
    IReadOnlyList<string> Rides,
    string? Note,
    DateTimeOffset ModifiedUtc)
{
    /// <summary>First sync-payload shape for monthly Early Ride Time — independent of any other entity's payload schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    public static MonthlyErtSyncPayload FromDomain(MonthlyErtConfiguration configuration) => new(
        configuration.Year, configuration.Month, configuration.State,
        configuration.StartTime, configuration.Rides, configuration.Note, configuration.ModifiedUtc);

    public MonthlyErtConfiguration ToDomain() => new(Year, Month, State, StartTime, Rides, Note, ModifiedUtc);
}

/// <summary>Canonical (compact, stable-order) JSON for <see cref="MonthlyErtSyncPayload"/> — used both for the exported envelope payload and, via <see cref="SyncContentHash"/>, for content fingerprinting.</summary>
public static class MonthlyErtSyncPayloadSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(MonthlyErtSyncPayload payload) => JsonSerializer.Serialize(payload, Options);

    public static MonthlyErtSyncPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<MonthlyErtSyncPayload>(json, Options)
            ?? throw new InvalidOperationException("Monthly ERT sync payload JSON deserialized to null.");

    public static MonthlyErtSyncPayload Deserialize(JsonElement payload) =>
        payload.Deserialize<MonthlyErtSyncPayload>(Options)
            ?? throw new InvalidOperationException("Monthly ERT sync payload JSON deserialized to null.");
}
