using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for the "PDF Style" template picker (Phase 17C §37), mirroring
/// <see cref="DailyFeatureTypeOption"/>'s ComboBox-over-a-fixed-list pattern — a ComboBox binds
/// <c>SelectedItem</c> to one of <see cref="All"/> rather than to the raw enum directly, so the
/// displayed label can differ from the enum member name without a converter.
/// </summary>
public sealed record AssociatePassdownTemplateOption(AssociatePassdownTemplate Value, string Label)
{
    public static IReadOnlyList<AssociatePassdownTemplateOption> All { get; } =
    [
        new(AssociatePassdownTemplate.Classic, "Classic"),
        new(AssociatePassdownTemplate.Compact, "Compact"),
        new(AssociatePassdownTemplate.Modern, "Modern"),
    ];

    public static AssociatePassdownTemplateOption For(AssociatePassdownTemplate value) => All.First(option => option.Value == value);
}
