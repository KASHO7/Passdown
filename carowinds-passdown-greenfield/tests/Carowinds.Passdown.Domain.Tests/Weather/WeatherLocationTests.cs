using Carowinds.Passdown.Domain.Weather;

namespace Carowinds.Passdown.Domain.Tests.Weather;

public class WeatherLocationTests
{
    [Fact]
    public void CarowindsCandidate_IsPendingVerification_AndDoesNotPermitRetrieval()
    {
        // Decision D8: no coordinate counts as accepted until the owner verifies it. Changing this status is an
        // owner decision recorded in docs/phase37b-weather-retrieval-and-cache.md, not a code change to make a test pass.
        Assert.Equal(WeatherReferenceVerification.PendingVerification, CarowindsWeatherLocation.Candidate.Verification);
        Assert.False(CarowindsWeatherLocation.Candidate.IsRetrievalPermitted);
    }

    [Fact]
    public void CarowindsCandidate_CoversBothStates_WithTheNorthCarolinaPointSupplyingTheForecast()
    {
        var location = CarowindsWeatherLocation.Candidate;

        Assert.Equal(["NC", "SC"], location.Points.Select(point => point.ExpectedState));
        Assert.Same(CarowindsWeatherLocation.NorthCarolina, location.ForecastPoint);
        Assert.Equal(2, location.Points.Select(point => point.Id).Distinct().Count());
    }

    [Fact]
    public void CarowindsCandidate_PointsAreTheRecordedCoordinates()
    {
        Assert.Equal("35.1050,-80.9420", CarowindsWeatherLocation.NorthCarolina.ToNwsPointText());
        Assert.Equal("35.1002,-80.9420", CarowindsWeatherLocation.SouthCarolina.ToNwsPointText());
    }

    [Fact]
    public void ToNwsPointText_UsesFourDecimalsAndInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var point = new WeatherReferencePoint("p", "P", 35.10278m, -80.94167m, "SC");
            Assert.Equal("35.1028,-80.9417", point.ToNwsPointText());
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void VerifiedLocation_PermitsRetrieval_ButAnEmptyOneDoesNot()
    {
        Assert.True(new WeatherLocation([CarowindsWeatherLocation.NorthCarolina], WeatherReferenceVerification.Verified, "test").IsRetrievalPermitted);
        Assert.False(new WeatherLocation([], WeatherReferenceVerification.Verified, "test").IsRetrievalPermitted);
    }

    [Fact]
    public void Units_ConvertAndMeasureDistance()
    {
        Assert.Equal(41.0, WeatherUnits.CelsiusToFahrenheit(5), 6);
        Assert.Equal(13.8, WeatherUnits.KilometresPerHourToMph(22.212), 1);
        Assert.Equal(22.4, WeatherUnits.MetresPerSecondToMph(10), 1);
        // Park NC point to Charlotte/Douglas (KCLT).
        Assert.InRange(WeatherUnits.DistanceKm(35.1050, -80.9420, 35.20833, -80.96139), 11.4, 11.8);
    }
}
