using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Application.Tests.Preferences;

/// <summary>Phase 35A: effective motion = in-app choice On and the operating system not requesting reduced motion.</summary>
public class InterfaceMotionResolverTests
{
    [Theory]
    [InlineData(InterfaceAnimationPreference.On, SystemReducedMotion.NotRequested, true)]
    [InlineData(InterfaceAnimationPreference.On, SystemReducedMotion.NotDetected, true)]
    [InlineData(InterfaceAnimationPreference.On, SystemReducedMotion.Requested, false)]
    [InlineData(InterfaceAnimationPreference.Off, SystemReducedMotion.NotRequested, false)]
    [InlineData(InterfaceAnimationPreference.Off, SystemReducedMotion.NotDetected, false)]
    [InlineData(InterfaceAnimationPreference.Off, SystemReducedMotion.Requested, false)]
    public void IsMotionEffective_FollowsTheTruthTable(InterfaceAnimationPreference preference, SystemReducedMotion system, bool expected) =>
        Assert.Equal(expected, InterfaceMotionResolver.IsMotionEffective(preference, system));

    [Fact]
    public void TheDefaultPreference_IsOn() => Assert.Equal(InterfaceAnimationPreference.On, default(InterfaceAnimationPreference));

    [Fact]
    public void Sanitize_UndefinedValue_FallsBackToOn() =>
        Assert.Equal(InterfaceAnimationPreference.On, InterfaceMotionResolver.Sanitize((InterfaceAnimationPreference)42));

    [Fact]
    public void Sanitize_KeepsAnExplicitOff() =>
        Assert.Equal(InterfaceAnimationPreference.Off, InterfaceMotionResolver.Sanitize(InterfaceAnimationPreference.Off));
}
