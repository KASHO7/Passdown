using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for the Daily Feature type picker (Phase 8A; formerly "Daily Fun"),
/// including the "not set" choice that <see cref="DailyFeatureType"/> itself cannot express as a
/// plain enum value. A ComboBox binds <c>SelectedItem</c> to one of <see cref="All"/> rather than to
/// the nullable enum directly, so clearing the Daily Feature is an explicit, labeled choice instead
/// of an unreachable XAML binding state.
/// </summary>
public sealed record DailyFeatureTypeOption(DailyFeatureType? Value, string Label)
{
    public static IReadOnlyList<DailyFeatureTypeOption> All { get; } =
    [
        new(null, "Not set"),
        new(DailyFeatureType.QuestionOfTheDay, "Question of the Day"),
        new(DailyFeatureType.UselessFactOfTheDay, "Useless Fact of the Day"),
        new(DailyFeatureType.QuoteOfTheDay, "Quote of the Day"),
    ];

    public static DailyFeatureTypeOption For(DailyFeatureType? value) => All.First(option => option.Value == value);
}
