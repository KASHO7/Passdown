using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.OperationalNotes;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The sync-wire shape of a persistent <see cref="OperationalNote"/> (Phase 16C §9) — Operational
/// Notes had no independent JSON payload before this phase (16A §D noted their fields map directly
/// onto envelope fields), so this defines one explicitly for <see cref="SyncEnvelope.Payload"/>/
/// <see cref="SyncEnvelope.PayloadSchemaVersion"/>. Every user-authored lifecycle field needed to
/// reproduce the same note on another device is included (Phase 16C §20) — title, body, category,
/// priority, effective date range, and the persisted <see cref="OperationalNoteStatus"/>
/// (Active/Resolved) plus its timestamps; there is no deletion field because the domain itself has no
/// delete path (16A §B/§G).
/// </summary>
public sealed record OperationalNoteSyncPayload(
    Guid Id,
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    OperationalNoteStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc)
{
    /// <summary>First sync-payload shape for Operational Notes — independent of any future domain-table schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    public static OperationalNoteSyncPayload FromDomain(OperationalNote note) => new(
        note.Id, note.Title, note.Body, note.Category, note.Priority,
        note.EffectiveFrom, note.EffectiveThrough, note.Status,
        note.CreatedAtUtc, note.UpdatedAtUtc, note.ResolvedAtUtc);

    public OperationalNote ToDomain() => new(
        Id, Title, Body, Category, Priority,
        EffectiveFrom, EffectiveThrough, Status,
        CreatedAtUtc, UpdatedAtUtc, ResolvedAtUtc);
}

/// <summary>Canonical (compact, stable-order) JSON for <see cref="OperationalNoteSyncPayload"/> — used both for the exported envelope payload and, via <see cref="SyncContentHash"/>, for content fingerprinting.</summary>
public static class OperationalNoteSyncPayloadSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(OperationalNoteSyncPayload payload) => JsonSerializer.Serialize(payload, Options);

    public static OperationalNoteSyncPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<OperationalNoteSyncPayload>(json, Options)
            ?? throw new InvalidOperationException("Operational note sync payload JSON deserialized to null.");

    public static OperationalNoteSyncPayload Deserialize(JsonElement payload) =>
        payload.Deserialize<OperationalNoteSyncPayload>(Options)
            ?? throw new InvalidOperationException("Operational note sync payload JSON deserialized to null.");
}
