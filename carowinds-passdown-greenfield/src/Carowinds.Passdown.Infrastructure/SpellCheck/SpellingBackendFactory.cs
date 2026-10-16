using System.Globalization;
using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Infrastructure.SpellCheck;

/// <summary>
/// Phase 20A: picks the local spelling engine for this platform. Windows 11 (the shipped target) uses
/// the built-in Windows spell checker, preferring US English to match the app's US formatting and
/// falling back to the user's UI language. Other platforms (the macOS/Linux development hosts) get
/// <see cref="UnavailableSpellingBackend"/> — fields work normally, just without underlines.
/// </summary>
public static class SpellingBackendFactory
{
    public static ISpellingBackend Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSpellingBackend(["en-US", CultureInfo.CurrentUICulture.Name]);
        }

        return UnavailableSpellingBackend.Instance;
    }
}
