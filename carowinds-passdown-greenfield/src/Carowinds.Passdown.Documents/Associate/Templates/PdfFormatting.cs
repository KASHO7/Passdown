using System.Globalization;

namespace Carowinds.Passdown.Documents.Associate.Templates;

/// <summary>
/// Trivial text-formatting helpers shared by all three template renderers (spec Phase 17C §32: "avoid
/// scattered one-off" duplication without building a design-system project). Deliberately tiny — each
/// renderer still owns its own palette/spacing/composition, this only avoids retyping the same
/// null-formatting rules three times.
/// </summary>
internal static class PdfFormatting
{
    /// <summary>"Not entered" rather than a fabricated zero when a count was never entered (spec §8.1).</summary>
    public static string FormatCount(int? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "Not entered";

    /// <summary>"Not configured for {Month Year}." (spec Phase 17B Part 13) — an explicit, dated message rather than a bare "Not configured".</summary>
    public static string NotConfiguredText(DateOnly? operatingDate) =>
        operatingDate is { } date
            ? $"Not configured for {date.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}."
            : "Not configured.";
}
