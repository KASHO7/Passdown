using System.Text.Json;

namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The one authoritative deserialization of a persisted <see cref="AssociateDraftPayload"/>, including
/// every legacy-schema migration-on-read step — extracted from <see cref="AssociatePassdownService"/>
/// in Phase 12B so <see cref="Calendar.OperationsCalendarService"/> can resolve a saved draft's manual-
/// hours/private-buyout override for many dates (spec Phase 12B Part Q) without a second, drifting
/// deserialization path (CLAUDE.md "do not duplicate ... logic").
/// </summary>
public static class AssociateDraftPayloadSerializer
{
    /// <summary>
    /// Deserializes a persisted draft, migrating older payload shapes forward. Schema-v1 predates
    /// both the Daily Fun -> Daily Feature rename and <see cref="PassdownSectionSelection"/>, so its
    /// JSON has neither "DailyFeatureType"/"DailyFeatureText" (only the old "DailyFunType"/
    /// "DailyFunText" names) nor a "SectionSelection" object at all — both come back null from the
    /// plain deserialize below, which this fills in from the legacy property names (the enum's
    /// underlying integer values are unchanged by the rename, so no value translation is needed,
    /// just reading it under its old key) or the all-included default. Phase 8A's own persisted
    /// "SectionSelection" object predates <see cref="PassdownSectionSelection.IncludeHauntedAttractions"/>
    /// (added Phase 8B), so that one property comes back <see langword="false"/> — the JSON
    /// deserializer's own missing-bool default, not an associate's real choice to hide it — from an
    /// otherwise-present "SectionSelection" object; <see cref="HasIncludeHauntedAttractionsProperty"/>
    /// distinguishes that case from an explicit "false" so it can be corrected to included, matching
    /// how every other Phase 8A section already defaults to included when missing entirely. This is
    /// presence-based rather than gated on the record's own <c>SchemaVersion</c> field, matching how
    /// this method already ignored that field before Phase 8A — it only ever needs to backfill
    /// whatever the plain deserialize left null/default, regardless of what schema_version says.
    /// </summary>
    public static AssociateDraftPayload Deserialize(string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<AssociateDraftPayload>(payloadJson) ?? AssociateDraftPayload.CreateDefault();

        if (payload.DailyFeatureType is null && payload.DailyFeatureText is null)
        {
            var (legacyType, legacyText) = ReadLegacyDailyFun(payloadJson);
            payload = payload with { DailyFeatureType = legacyType, DailyFeatureText = legacyText };
        }

        var sectionSelection = payload.SectionSelection ?? PassdownSectionSelection.AllIncluded;
        if (!HasIncludeHauntedAttractionsProperty(payloadJson))
        {
            sectionSelection = sectionSelection with { IncludeHauntedAttractions = true };
        }

        // Phase 11D Part AO: a pre-11D payload has no "SelectedOperationalNotes" property at all, so
        // the plain deserialize above leaves it null — a legacy draft must load with zero selections,
        // never a fabricated one, matching every other optional-field migration in this method.
        var selectedOperationalNotes = payload.SelectedOperationalNotes ?? [];

        return payload with { SectionSelection = sectionSelection, SelectedOperationalNotes = selectedOperationalNotes };
    }

    private static bool HasIncludeHauntedAttractionsProperty(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty("SectionSelection", out var section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty("IncludeHauntedAttractions", out _);
    }

    private static (DailyFeatureType? Type, string? Text) ReadLegacyDailyFun(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        var type = root.TryGetProperty("DailyFunType", out var typeElement) && typeElement.ValueKind != JsonValueKind.Null
            ? typeElement.Deserialize<DailyFeatureType?>()
            : null;

        var text = root.TryGetProperty("DailyFunText", out var textElement) && textElement.ValueKind != JsonValueKind.Null
            ? textElement.GetString()
            : null;

        return (type, text);
    }
}
