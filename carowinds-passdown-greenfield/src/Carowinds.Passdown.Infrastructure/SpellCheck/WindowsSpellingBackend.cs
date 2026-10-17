using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Carowinds.Passdown.Application.SpellCheck;

namespace Carowinds.Passdown.Infrastructure.SpellCheck;

/// <summary>
/// Phase 20A: <see cref="ISpellingBackend"/> over the Windows Spell Checking API
/// (<c>ISpellCheckerFactory</c>/<c>ISpellChecker</c>, built into Windows 8+ and Windows 11). It is the
/// same local, offline engine Windows itself uses: dictionaries are installed language packs, nothing
/// is sent anywhere, and "Add to dictionary" writes the user's own Windows custom dictionary (shared by
/// other Windows apps, never by Personal Sync). No NuGet package is involved — built-in COM interop.
///
/// The COM objects are created lazily on first use, so a missing/broken spell checker can never affect
/// app startup or the database; any exception is contained by <see cref="SpellCheckService"/>.
///
/// WINDOWS-ONLY — needs Windows VM validation (the dev host cannot exercise it).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSpellingBackend : ISpellingBackend
{
    private static readonly Guid SpellCheckerFactoryClsid = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    private readonly string[] _preferredLanguages;
    private readonly Lazy<ISpellChecker?> _checker;

    /// <param name="preferredLanguages">BCP-47 tags tried in order; the first supported one is used.</param>
    public WindowsSpellingBackend(IReadOnlyList<string> preferredLanguages)
    {
        ArgumentNullException.ThrowIfNull(preferredLanguages);
        _preferredLanguages = preferredLanguages.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _checker = new Lazy<ISpellChecker?>(CreateChecker, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _checker.Value is not null;

    public bool CanAddToDictionary => IsAvailable;

    public bool IsCorrect(string word)
    {
        var checker = _checker.Value;
        if (checker is null)
        {
            return true;
        }

        Marshal.ThrowExceptionForHR(checker.Check(word, out var errors));
        try
        {
            // S_OK with an error means misspelled; S_FALSE (1) means the enumeration is empty.
            var hr = errors.Next(out var error);
            if (hr == 0 && error is not null)
            {
                Marshal.ReleaseComObject(error);
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        var checker = _checker.Value;
        if (checker is null)
        {
            return [];
        }

        Marshal.ThrowExceptionForHR(checker.Suggest(word, out var suggestions));
        var results = new List<string>();
        try
        {
            var buffer = new string[1];
            while (results.Count < 10 && suggestions.Next(1, buffer, IntPtr.Zero) == 0)
            {
                if (!string.IsNullOrWhiteSpace(buffer[0]))
                {
                    results.Add(buffer[0]);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(suggestions);
        }

        return results;
    }

    public void AddToDictionary(string word)
    {
        if (_checker.Value is { } checker)
        {
            Marshal.ThrowExceptionForHR(checker.Add(word));
        }
    }

    private ISpellChecker? CreateChecker()
    {
        var factoryType = Type.GetTypeFromCLSID(SpellCheckerFactoryClsid, throwOnError: false);
        if (factoryType is null || Activator.CreateInstance(factoryType) is not ISpellCheckerFactory factory)
        {
            return null;
        }

        foreach (var language in _preferredLanguages)
        {
            if (factory.IsSupported(language, out var supported) == 0 && supported != 0 &&
                factory.CreateSpellChecker(language, out var checker) == 0)
            {
                return checker;
            }
        }

        return null;
    }

    // Vtable order follows spellcheck.h; only the members this class calls (and those before them) are declared.

    [ComImport]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        [PreserveSig]
        int get_SupportedLanguages(out IEnumString value);

        [PreserveSig]
        int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);

        [PreserveSig]
        int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
    }

    [ComImport]
    [Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [PreserveSig]
        int get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string value);

        [PreserveSig]
        int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);

        [PreserveSig]
        int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString value);

        [PreserveSig]
        int Add([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport]
    [Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError? value);
    }

    [ComImport]
    [Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
    }
}
