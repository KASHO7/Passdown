using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Application.Tests.Preferences;

/// <summary>Phase 23C: the pure System / Light / Dark resolution rule.</summary>
public class AppearanceResolverTests
{
    [Theory]
    [InlineData(AppearancePreference.System, EffectiveAppearance.Light, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.System, EffectiveAppearance.Dark, EffectiveAppearance.Dark)]
    [InlineData(AppearancePreference.Light, EffectiveAppearance.Light, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.Light, EffectiveAppearance.Dark, EffectiveAppearance.Light)]
    [InlineData(AppearancePreference.Dark, EffectiveAppearance.Light, EffectiveAppearance.Dark)]
    [InlineData(AppearancePreference.Dark, EffectiveAppearance.Dark, EffectiveAppearance.Dark)]
    public void Resolve_ForcedOptionsOverrideTheSystem_SystemFollowsIt(AppearancePreference preference, EffectiveAppearance system, EffectiveAppearance expected) =>
        Assert.Equal(expected, AppearanceResolver.Resolve(preference, system));

    [Theory]
    [InlineData(EffectiveAppearance.Light)]
    [InlineData(EffectiveAppearance.Dark)]
    public void Resolve_UndefinedPreference_FollowsTheSystem(EffectiveAppearance system) =>
        Assert.Equal(system, AppearanceResolver.Resolve((AppearancePreference)99, system));

    [Fact]
    public void Default_IsSystem() => Assert.Equal(AppearancePreference.System, default(AppearancePreference));

    [Fact]
    public void Sanitize_RejectsUndefinedValues()
    {
        Assert.Equal(AppearancePreference.Dark, AppearanceResolver.Sanitize(AppearancePreference.Dark));
        Assert.Equal(AppearancePreference.System, AppearanceResolver.Sanitize((AppearancePreference)(-3)));
        Assert.Equal(AppearancePreference.System, AppearanceResolver.Sanitize((AppearancePreference)3));
    }

    [Fact]
    public void PersistedNames_AreStableIdentifiers_NotDisplayLabels() =>
        Assert.Equal(["System", "Light", "Dark"], Enum.GetNames<AppearancePreference>());
}
