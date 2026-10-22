using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;

namespace Carowinds.Passdown.Application.Tests.MorningMessage;

/// <summary>Phase 29: section order/visibility rules and preference sanitizing — pure, no files.</summary>
public sealed class MorningMessageLayoutTests
{
    private static MorningMessageSectionId[] Order(MorningMessageLayout layout) => layout.Sections.Select(section => section.Id).ToArray();

    [Fact]
    public void Default_IsThePreferredWorkflow_AllEnabled_DailyFeatureLast()
    {
        Assert.Equal(
            [
                MorningMessageSectionId.Greeting, MorningMessageSectionId.Operations, MorningMessageSectionId.DapPlacement,
                MorningMessageSectionId.Reminders, MorningMessageSectionId.TicketPrices, MorningMessageSectionId.BreakPlan,
                MorningMessageSectionId.DailyFeature,
            ],
            Order(MorningMessageLayout.Default));
        Assert.All(MorningMessageLayout.Default.Sections, section => Assert.True(section.Enabled));
        Assert.Equal(Enum.GetValues<MorningMessageSectionId>().Length, MorningMessageLayout.Default.Sections.Count);
    }

    [Fact]
    public void MoveUpAndDown_SwapNeighbours_AndStopAtTheEnds()
    {
        var layout = MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.DailyFeature);
        Assert.Equal(MorningMessageSectionId.DailyFeature, layout.Sections[5].Id);
        Assert.Equal(MorningMessageSectionId.BreakPlan, layout.Sections[6].Id);

        Assert.Equal(MorningMessageLayout.Default, MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.Greeting));
        Assert.Equal(MorningMessageLayout.Default, MorningMessageLayout.Default.MoveDown(MorningMessageSectionId.DailyFeature));
        Assert.False(MorningMessageLayout.Default.CanMoveUp(MorningMessageSectionId.Greeting));
        Assert.False(MorningMessageLayout.Default.CanMoveDown(MorningMessageSectionId.DailyFeature));
        Assert.Equal(MorningMessageLayout.Default, layout.MoveDown(MorningMessageSectionId.DailyFeature));
    }

    [Fact]
    public void WithEnabled_TogglesOnlyThatSection_KeepsOrder()
    {
        var layout = MorningMessageLayout.Default.WithEnabled(MorningMessageSectionId.Reminders, false);

        Assert.False(layout.IsEnabled(MorningMessageSectionId.Reminders));
        Assert.Equal(Order(MorningMessageLayout.Default), Order(layout));
        Assert.DoesNotContain(MorningMessageSectionId.Reminders, layout.EnabledInOrder());
        Assert.Equal(6, layout.EnabledInOrder().Count);
    }

    [Fact]
    public void Normalize_DropsDuplicatesAndUnknownValues_AddsMissingAtDefaultPosition()
    {
        var stored = new[]
        {
            new MorningMessageSectionSetting(MorningMessageSectionId.DailyFeature, true),
            new MorningMessageSectionSetting(MorningMessageSectionId.Greeting, false),
            new MorningMessageSectionSetting(MorningMessageSectionId.Greeting, true),
            new MorningMessageSectionSetting((MorningMessageSectionId)99, true),
            new MorningMessageSectionSetting(MorningMessageSectionId.Reminders, true),
        };

        var layout = MorningMessageLayout.Normalize(stored);

        Assert.Equal(Enum.GetValues<MorningMessageSectionId>().Length, layout.Sections.Count);
        Assert.Equal(layout.Sections.Count, layout.Sections.Select(section => section.Id).Distinct().Count());
        Assert.Equal(MorningMessageSectionId.DailyFeature, layout.Sections[0].Id); // the user's own order is kept
        Assert.False(layout.IsEnabled(MorningMessageSectionId.Greeting)); // first occurrence wins
        Assert.True(layout.IndexOf(MorningMessageSectionId.Operations) > layout.IndexOf(MorningMessageSectionId.Greeting));
        Assert.True(layout.IndexOf(MorningMessageSectionId.TicketPrices) > layout.IndexOf(MorningMessageSectionId.Reminders));
    }

    [Fact]
    public void Normalize_NullOrEmpty_IsTheDefault()
    {
        Assert.Equal(MorningMessageLayout.Default, MorningMessageLayout.Normalize(null));
        Assert.Equal(MorningMessageLayout.Default, MorningMessageLayout.Normalize([]));
    }

    [Fact]
    public void Preferences_Default_IsPlainTextCopy_GoodMorning_EverydayPriceSections()
    {
        var preferences = MorningMessagePreferences.Default;

        Assert.Equal("Good Morning!", preferences.Greeting);
        Assert.Equal(TeamsCopyMode.PlainText, preferences.DefaultCopyMode);
        Assert.Equal([ProductDisplaySection.DailyAdmission, ProductDisplaySection.FastLane, ProductDisplaySection.Promotions], preferences.PriceSections);
        Assert.DoesNotContain(ProductDisplaySection.SeasonPasses, preferences.PriceSections);
        Assert.DoesNotContain(ProductDisplaySection.Memberships, MorningMessagePreferences.SelectablePriceSections);
    }

    [Fact]
    public void Preferences_Sanitize_TrimsGreeting_DropsUnknownPriceSectionsAndCopyModes()
    {
        var preferences = MorningMessagePreferences.Sanitize(
            "  Gooooood Morning!  " + new string('!', 500),
            null,
            (TeamsCopyMode)42,
            [ProductDisplaySection.Memberships, ProductDisplaySection.FastLane, (ProductDisplaySection)77],
            remindersAsBullets: true);

        Assert.StartsWith("Gooooood Morning!", preferences.Greeting, StringComparison.Ordinal);
        Assert.Equal(MorningMessagePreferences.MaxGreetingLength, preferences.Greeting.Length);
        Assert.Equal(TeamsCopyMode.PlainText, preferences.DefaultCopyMode);
        Assert.Equal([ProductDisplaySection.FastLane], preferences.PriceSections);
        Assert.Equal(MorningMessageLayout.Default, preferences.Layout);
        Assert.True(preferences.RemindersAsBullets);
    }

    [Fact]
    public void Content_Sanitized_NullsBecomeEmpty_LongFieldsAreCut_BlankIsEmpty()
    {
        var content = new MorningMessageContent(null!, new string('x', MorningMessageContent.MaxFieldLength + 10), "", false, "  ", null!, "").Sanitized();

        Assert.Equal(string.Empty, content.Reminders);
        Assert.Equal(MorningMessageContent.MaxFieldLength, content.BreakPlan.Length);
        Assert.Null(content.DapImagePath);
        Assert.True((MorningMessageContent.Empty with { Reminders = "  \n " }).IsEmpty);
        Assert.False((MorningMessageContent.Empty with { DapImageReminder = true }).IsEmpty);
    }
}
