using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>
/// One selectable entry for the Daily Fun type picker (spec §8.1), including the "not set" choice
/// that <see cref="DailyFunType"/> itself cannot express as a plain enum value. A ComboBox binds
/// <c>SelectedItem</c> to one of <see cref="All"/> rather than to the nullable enum directly, so
/// clearing Daily Fun is an explicit, labeled choice instead of an unreachable XAML binding state.
/// </summary>
public sealed record DailyFunTypeOption(DailyFunType? Value, string Label)
{
    public static IReadOnlyList<DailyFunTypeOption> All { get; } =
    [
        new(null, "Not set"),
        new(DailyFunType.QuestionOfTheDay, "Question of the Day"),
        new(DailyFunType.FactOfTheDay, "Fact of the Day"),
        new(DailyFunType.QuoteOfTheDay, "Quote of the Day"),
    ];

    public static DailyFunTypeOption For(DailyFunType? value) => All.First(option => option.Value == value);
}
