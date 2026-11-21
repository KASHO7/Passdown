using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Domain.Tests.Ert;

/// <summary>Phase 31B: order-independent ERT ride equality that never collapses genuinely different rides.</summary>
public class Phase31BErtRideSelectionTests
{
    [Fact]
    public void SameRidesInADifferentOrder_AreTheSameSelection()
    {
        Assert.True(ErtRideSelection.SameSelection(["Copperhead Strike", "Mountain Gliders"], ["Mountain Gliders", "Copperhead Strike"]));
    }

    [Theory]
    [InlineData("  Copperhead   Strike ")]
    [InlineData("COPPERHEAD STRIKE")]
    [InlineData("copperhead\tstrike")]
    public void WhitespaceAndCapitalization_DoNotChangeTheRide(string spelling)
    {
        Assert.Equal(ErtRideSelection.RideKey("Copperhead Strike"), ErtRideSelection.RideKey(spelling));
    }

    [Fact]
    public void TypographicApostrophesAndDashes_DoNotChangeTheRide()
    {
        Assert.Equal(ErtRideSelection.RideKey("Snoopy's Racing Railway"), ErtRideSelection.RideKey("Snoopy’s Racing Railway"));
        Assert.Equal(ErtRideSelection.RideKey("Fury 325 - Front"), ErtRideSelection.RideKey("Fury 325 – Front"));
    }

    [Fact]
    public void Duplicates_AreIgnoredForEquality()
    {
        Assert.True(ErtRideSelection.SameSelection(["Fury 325", "fury 325", "Afterburn"], ["Afterburn", "Fury 325"]));
    }

    [Fact]
    public void GenuinelyDifferentRides_AreNeverEqual()
    {
        Assert.False(ErtRideSelection.SameSelection(["Copperhead Strike"], ["Fury 325"]));
        Assert.False(ErtRideSelection.SameSelection(["Copperhead Strike", "Mountain Gliders"], ["Copperhead Strike"]));
        Assert.False(ErtRideSelection.SameSelection(["Intimidator"], ["Intimidator 305"]));
    }

    [Fact]
    public void BlankEntries_AreIgnored_AndEmptySelectionsAreEqual()
    {
        Assert.True(ErtRideSelection.SameSelection(["", "  "], []));
        Assert.Equal(string.Empty, ErtRideSelection.RideKey("   "));
    }

    [Fact]
    public void NoAliasIsAssumedWithoutVerification()
    {
        Assert.Empty(ErtRideSelection.VerifiedAliases);
        Assert.NotEqual(ErtRideSelection.RideKey("Copperhead"), ErtRideSelection.RideKey("Copperhead Strike"));
    }

    [Fact]
    public void Merge_KeepsThisDevicesOrder_ThenAddsTheOtherDevicesExtraRides_OncePerRide()
    {
        var merged = ErtRideSelection.Merge(["Copperhead Strike", "Mountain Gliders"], ["mountain gliders", "Fury 325", "Copperhead Strike"]);

        Assert.Equal(["Copperhead Strike", "Mountain Gliders", "Fury 325"], merged);
    }

    [Fact]
    public void Distinct_PreservesTheUsersOrderAndFirstSpelling()
    {
        Assert.Equal(["Fury 325", "AFTERBURN"], ErtRideSelection.Distinct([" Fury 325 ", "", "AFTERBURN", "fury 325"]));
    }

    [Fact]
    public void CanonicalSet_IsTheSameForEveryOrdering()
    {
        string[] rides = ["Copperhead Strike", "Mountain Gliders", "Fury 325"];
        var expected = ErtRideSelection.CanonicalSet(rides);
        foreach (var permutation in new[] { new[] { 2, 0, 1 }, new[] { 1, 2, 0 }, new[] { 2, 1, 0 } })
        {
            Assert.Equal(expected, ErtRideSelection.CanonicalSet(permutation.Select(i => rides[i])));
        }
    }
}
