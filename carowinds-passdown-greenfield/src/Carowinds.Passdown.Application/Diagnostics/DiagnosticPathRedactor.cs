using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Phase 18A PRIV-001: removes machine-/person-identifying filesystem paths from text that leaves the
/// machine in a diagnostic ZIP. The scheduled-refresh log is copied into the ZIP verbatim, and a logged
/// exception's message routinely embeds a full path (e.g. an <see cref="IOException"/>'s
/// <c>Could not find a part of the path 'C:\Users\jane.doe\OneDrive - Contoso\...'</c>), which exposes the
/// Windows account name and OneDrive tenant. Known roots are replaced first (longest first, so a
/// nested root wins over its parent); a generic profile-path pattern then catches anything left over
/// (another user's profile, a differently-cased spelling). The rest of each line is left intact so the
/// log stays useful for support.
/// </summary>
public static partial class DiagnosticPathRedactor
{
    public static string Redact(string text, IReadOnlyList<KeyValuePair<string, string>> knownRoots)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(knownRoots);

        foreach (var (root, token) in knownRoots.Where(pair => pair.Key.Length > 3).OrderByDescending(pair => pair.Key.Length))
        {
            text = text.Replace(root.TrimEnd('\\', '/'), token, StringComparison.OrdinalIgnoreCase);
        }

        text = WindowsProfilePath().Replace(text, "%USERPROFILE%");
        return UnixHomePath().Replace(text, "~");
    }

    /// <summary><c>C:\Users\&lt;name&gt;</c> — the name segment may contain spaces/dots but never a path separator or a character Windows forbids in a file name.</summary>
    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/:*?""<>|\r\n']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsProfilePath();

    [GeneratedRegex(@"/(?:Users|home)/[^/\s'""]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixHomePath();
}
