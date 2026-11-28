using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Domain.Tests.Weather;

public class WeatherAlertRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 14, 40, 0, TimeSpan.FromHours(-4));

    private static WeatherAlert Alert(
        string id,
        string status = "Actual",
        string messageType = "Alert",
        string severity = "Moderate",
        string urgency = "Expected",
        DateTimeOffset? effective = null,
        DateTimeOffset? expires = null,
        DateTimeOffset? ends = null,
        DateTimeOffset? onset = null,
        string[]? references = null) =>
        new(id, "Test Event", status, messageType, severity, urgency, "Likely", effective, effective, onset, expires, ends, null, null, null, null, null, references ?? [], [], null);

    [Fact]
    public void IsActiveAt_StartedAndNotEnded_IsActive() =>
        Assert.True(WeatherAlertRules.IsActiveAt(Alert("a", effective: Now.AddHours(-1), expires: Now.AddHours(1)), Now));

    [Fact]
    public void IsActiveAt_AtOrAfterEnds_IsNotActive_EvenWhenExpiresIsLater()
    {
        var alert = Alert("a", effective: Now.AddHours(-1), expires: Now.AddHours(2), ends: Now);
        Assert.False(WeatherAlertRules.IsActiveAt(alert, Now));
    }

    [Fact]
    public void IsActiveAt_UsesExpiresWhenEndsIsMissing()
    {
        var alert = Alert("a", effective: Now.AddHours(-1), expires: Now.AddMinutes(-1));
        Assert.False(WeatherAlertRules.IsActiveAt(alert, Now));
        Assert.Equal(Now.AddMinutes(-1), WeatherAlertRules.EndsAt(alert));
    }

    [Fact]
    public void IsActiveAt_WithoutAnyEndTime_IsNeverActive() =>
        Assert.False(WeatherAlertRules.IsActiveAt(Alert("a", effective: Now.AddHours(-1)), Now));

    [Fact]
    public void IsActiveAt_NotYetEffective_IsNotActive() =>
        Assert.False(WeatherAlertRules.IsActiveAt(Alert("a", effective: Now.AddMinutes(5), expires: Now.AddHours(1)), Now));

    [Theory]
    [InlineData("Test")]
    [InlineData("Exercise")]
    [InlineData("System")]
    [InlineData("Draft")]
    public void IsActiveAt_NonActualStatus_IsNeverActive(string status) =>
        Assert.False(WeatherAlertRules.IsActiveAt(Alert("a", status: status, effective: Now.AddHours(-1), expires: Now.AddHours(1)), Now));

    [Fact]
    public void IsActiveAt_CancelMessage_IsNeverActive() =>
        Assert.False(WeatherAlertRules.IsActiveAt(Alert("c", messageType: "Cancel", effective: Now.AddHours(-1), expires: Now.AddHours(1)), Now));

    [Fact]
    public void ResolveActive_UpdateReplacesTheMessageItReferences()
    {
        var original = Alert("svr-1", effective: Now.AddMinutes(-35), expires: Now.AddMinutes(5));
        var update = Alert("svr-2", messageType: "Update", effective: Now.AddMinutes(-10), expires: Now.AddMinutes(20), references: ["svr-1"]);

        var active = WeatherAlertRules.ResolveActive([original, update], Now);

        Assert.Equal(["svr-2"], active.Select(alert => alert.Id));
    }

    [Fact]
    public void ResolveActive_CancelRemovesTheMessageItReferencesAndItself()
    {
        var original = Alert("flood-1", effective: Now.AddHours(-1), expires: Now.AddHours(2));
        var cancel = Alert("flood-2", messageType: "Cancel", effective: Now.AddMinutes(-5), expires: Now.AddHours(1), references: ["flood-1"]);

        Assert.Empty(WeatherAlertRules.ResolveActive([original, cancel], Now));
    }

    [Fact]
    public void ResolveActive_SameAlertFromBothReferencePoints_AppearsOnce()
    {
        var fromNorthCarolina = Alert("heat-1", effective: Now.AddHours(-3), expires: Now.AddHours(5));
        var fromSouthCarolina = Alert("heat-1", effective: Now.AddHours(-3), expires: Now.AddHours(5));

        Assert.Single(WeatherAlertRules.ResolveActive([fromNorthCarolina, fromSouthCarolina], Now));
    }

    [Fact]
    public void ResolveActive_SortsBySeverityThenUrgencyThenOnset()
    {
        var minor = Alert("minor", severity: "Minor", urgency: "Immediate", effective: Now.AddHours(-1), expires: Now.AddHours(1));
        var severeExpected = Alert("severe-expected", severity: "Severe", urgency: "Expected", effective: Now.AddHours(-1), expires: Now.AddHours(1));
        var severeImmediateLater = Alert("severe-immediate-later", severity: "Severe", urgency: "Immediate", effective: Now.AddHours(-1), expires: Now.AddHours(1), onset: Now.AddMinutes(-5));
        var severeImmediateEarlier = Alert("severe-immediate-earlier", severity: "Severe", urgency: "Immediate", effective: Now.AddHours(-1), expires: Now.AddHours(1), onset: Now.AddMinutes(-30));
        var extreme = Alert("extreme", severity: "Extreme", urgency: "Future", effective: Now.AddHours(-1), expires: Now.AddHours(1));
        var unknown = Alert("unknown", severity: "Unknown", effective: Now.AddHours(-1), expires: Now.AddHours(1));

        var active = WeatherAlertRules.ResolveActive([minor, unknown, severeExpected, severeImmediateLater, extreme, severeImmediateEarlier], Now);

        Assert.Equal(["extreme", "severe-immediate-earlier", "severe-immediate-later", "severe-expected", "minor", "unknown"], active.Select(alert => alert.Id));
    }
}
