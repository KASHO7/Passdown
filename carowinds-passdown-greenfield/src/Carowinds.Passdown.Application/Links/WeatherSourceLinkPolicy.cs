namespace Carowinds.Passdown.Application.Links;

/// <summary>
/// Phase 37B: the allowlist a weather source address must pass before the app offers to open it. Separate
/// from <see cref="OfficialSourceLinkPolicy"/>, which stays Carowinds/Six Flags only (spec §6.1). Accepts an
/// absolute <c>https</c> address on the default port, without user-info, on exactly one of the four NWS
/// hosts. Everything else is refused.
/// </summary>
public static class WeatherSourceLinkPolicy
{
    private static readonly HashSet<string> NwsHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.weather.gov",
        "forecast.weather.gov",
        "alerts.weather.gov",
        "api.weather.gov",
    };

    /// <summary>True when <paramref name="candidate"/> is a safe NWS page; <paramref name="safe"/> is a fresh copy of it.</summary>
    public static bool TryValidate(Uri? candidate, out Uri safe)
    {
        safe = null!;
        if (candidate is null || !candidate.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !candidate.IsDefaultPort
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || candidate.IsUnc
            || candidate.IsFile
            || candidate.HostNameType != UriHostNameType.Dns
            || !NwsHosts.Contains(candidate.IdnHost)
            || candidate.AbsolutePath.Contains('\\'))
        {
            return false;
        }

        safe = new Uri(candidate.AbsoluteUri, UriKind.Absolute);
        return true;
    }
}
