using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Phase 26: the contents of one computer's <c>official-price-history/&lt;device-id&gt;.json</c> file —
/// every verified official price observation that computer knows (its own and the ones it merged from
/// others), so the combined history survives even if one computer's file later disappears.
/// <para>
/// This is an additive, independently versioned channel: it is not a <see cref="SyncEnvelope"/>, its
/// folder is one RC.8 never enumerates, and its own <see cref="FormatVersion"/> evolves independently of
/// <see cref="SyncEnvelopeSerializer.CurrentSyncSchemaVersion"/> (which stays 1 — the envelope format
/// and the existing folders are unchanged). Only the computer named by the filename ever writes a
/// file, so no two computers ever replace each other's file.
/// </para>
/// </summary>
/// <param name="Format">Always <see cref="OfficialPriceHistoryFileSerializer.FormatName"/>.</param>
/// <param name="FormatVersion">Readers accept only versions they understand; a newer one is left untouched and reported.</param>
/// <param name="ExporterDeviceId">The computer that wrote this file (informational; each observation names its own contributor).</param>
/// <param name="Observations">Sorted by <see cref="OfficialPriceObservation.ObservationId"/>, so identical content always produces identical bytes.</param>
public sealed record OfficialPriceHistoryFile(
    string Format,
    int FormatVersion,
    Guid ExporterDeviceId,
    IReadOnlyList<OfficialPriceObservation>? Observations);

/// <summary>Canonical JSON for <see cref="OfficialPriceHistoryFile"/> — deterministic output, strict enum parsing.</summary>
public static class OfficialPriceHistoryFileSerializer
{
    public const string FormatName = "carowinds-official-price-history";

    public const int CurrentFormatVersion = 1;

    /// <summary>Bounded work per file: a real history is a few hundred observations at most.</summary>
    public const int MaxObservationsPerFile = 20_000;

    /// <summary>A file with more invalid records than this is treated as damaged and skipped as a whole.</summary>
    public const int MaxInvalidObservationsPerFile = 200;

    /// <summary>At most this many contributor files are read per pass.</summary>
    public const int MaxFilesPerPass = 64;

    /// <summary>8 MiB — orders of magnitude above a real file (about 500 bytes per observation).</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        MaxDepth = 8,
    };

    public static string Serialize(Guid exporterDeviceId, IEnumerable<OfficialPriceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var ordered = observations.OrderBy(o => o.ObservationId, StringComparer.Ordinal).ToList();
        return JsonSerializer.Serialize(new OfficialPriceHistoryFile(FormatName, CurrentFormatVersion, exporterDeviceId, ordered), Options);
    }

    public static bool IsSupported(OfficialPriceHistoryFile file) =>
        file.FormatVersion is >= 1 and <= CurrentFormatVersion;
}

/// <summary>Phase 26: the plain-language wording for merged price-history observations, shared by every sync status line.</summary>
public static class OfficialPriceHistorySummary
{
    /// <summary>"Official price history: 28 new observations merged." — observations, never "price changes".</summary>
    public static string MergedText(int count) =>
        $"Official price history: {count} new observation{(count == 1 ? "" : "s")} merged.";

    /// <summary>"Official price history: 2 observations could not be verified and were skipped."</summary>
    public static string SkippedText(int count) =>
        $"Official price history: {count} observation{(count == 1 ? "" : "s")} could not be verified and {(count == 1 ? "was" : "were")} skipped.";
}
