namespace Carowinds.Passdown.Application.Links;

/// <summary>
/// Phase 26: the one allowlist an official source address must pass before the app ever offers to
/// open it in the system browser. Accepts only an absolute <c>https</c> address on an official Six
/// Flags/Carowinds host, on the default port, with no user-info — and on <c>sixflags.com</c> only a
/// Carowinds page. Everything else (<c>file:</c>, <c>javascript:</c>, <c>http:</c>, another host, a
/// relative or malformed address, a local path) is refused. The stored source URL is data recorded
/// from a retrieval; it is never passed to a shell or command line, only to the platform launcher as a
/// validated <see cref="Uri"/>.
/// </summary>
public static class OfficialSourceLinkPolicy
{
    private static readonly HashSet<string> SixFlagsHosts = new(StringComparer.OrdinalIgnoreCase) { "www.sixflags.com", "sixflags.com" };

    private static readonly HashSet<string> CarowindsHosts = new(StringComparer.OrdinalIgnoreCase) { "www.carowinds.com", "carowinds.com" };

    /// <summary>True when <paramref name="candidate"/> is a safe official page; <paramref name="safe"/> is a fresh copy of it.</summary>
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
            || candidate.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = candidate.IdnHost;
        var isSixFlags = SixFlagsHosts.Contains(host);
        if (!isSixFlags && !CarowindsHosts.Contains(host))
        {
            return false;
        }

        var path = candidate.AbsolutePath;
        if (isSixFlags && !(path.Equals("/carowinds", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/carowinds/", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
        {
            return false;
        }

        return Uri.TryCreate(candidate.AbsoluteUri, UriKind.Absolute, out safe!);
    }

    /// <summary>"www.sixflags.com/carowinds/memberships-and-season-passes" — how the page is named in plain text.</summary>
    public static string DisplayAddress(Uri safe)
    {
        ArgumentNullException.ThrowIfNull(safe);
        return $"{safe.Host}{safe.AbsolutePath}".TrimEnd('/');
    }
}
