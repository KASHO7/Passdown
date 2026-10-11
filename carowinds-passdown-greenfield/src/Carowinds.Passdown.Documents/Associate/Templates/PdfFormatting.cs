using System.Globalization;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Trivial text-formatting helpers shared by all three template renderers (spec Phase 17C §32: "avoid
/// scattered one-off" duplication without building a design-system project). Deliberately tiny — each
/// renderer still owns its own palette/spacing/composition, this only avoids retyping the same
/// null-formatting rules three times.
/// </summary>
internal static class PdfFormatting
{
    /// <summary>"—" rather than a fabricated zero when a count was never entered (spec §8.1; Phase 17D §11 master layout — "Attendance   —   Pre-solds   —").</summary>
    public static string FormatCount(int? value) => value?.ToString("N0", PresentationCulture.UnitedStates) ?? "—";

    /// <summary>"Not configured for {Month Year}." (spec Phase 17B Part 13) — an explicit, dated message rather than a bare "Not configured".</summary>
    public static string NotConfiguredText(DateOnly? operatingDate) =>
        operatingDate is { } date
            ? $"Not configured for {date.ToString("MMMM yyyy", PresentationCulture.UnitedStates)}."
            : "Not configured.";

    /// <summary>
    /// The one Early Ride Time display line every Phase 17D+ template renderer prints (spec §10) —
    /// rides only, bullet-separated, never a start time. Shared here so the three renderers can never
    /// drift on ERT wording (one prints "None", another prints "No Early Ride Time today", etc.).
    /// </summary>
    public static string ErtLineText(EffectiveErtKind kind, IReadOnlyList<string> rides, DateOnly? operatingDate) => kind switch
    {
        EffectiveErtKind.NotConfigured => NotConfiguredText(operatingDate),
        EffectiveErtKind.NoErt => "None",
        _ => rides.Count > 0 ? string.Join(" • ", rides) : "Configured",
    };
}
