using System.Reflection;

namespace Carowinds.Passdown.Application.Diagnostics;

/// <summary>
/// Phase 18D: which exact build is running — the visible product name, the release version, the
/// source commit it was built from, and the executable it was launched from. Exists so a stale
/// portable copy (e.g. one Windows Search found instead of the current release folder) can be told
/// apart from the current build at a glance: builds before Phase 18E all shared the version
/// 0.1.0-dev (numeric 0.1.0.0), so only the informational version's <c>+&lt;commit&gt;</c> suffix and
/// the executable path distinguish them; release candidates since 0.1.0-rc.1 also differ by version.
/// </summary>
public sealed record BuildIdentity(string ProductName, string Version, string? SourceRevision, string? ExecutablePath)
{
    /// <summary>The visible product name (Phase 13G); the legacy "Carowinds Passdown" identity is internal only.</summary>
    public const string DefaultProductName = "Carowinds Operations";

    private const int ShortRevisionLength = 7;

    /// <summary>The source commit abbreviated the way <c>git log --oneline</c> prints it.</summary>
    public string? ShortSourceRevision =>
        SourceRevision is { Length: > ShortRevisionLength } revision ? revision[..ShortRevisionLength] : SourceRevision;

    /// <summary>"0.1.0-rc.1 (096bc4a)" — for on-screen display.</summary>
    public string DisplayVersion => ShortSourceRevision is { } revision ? $"{Version} ({revision})" : Version;

    /// <summary>"0.1.0-rc.1+096bc4a8…" — the full, unambiguous form for support files.</summary>
    public string FullVersion => SourceRevision is { } revision ? $"{Version}+{revision}" : Version;

    /// <summary>
    /// Reads <paramref name="assembly"/>'s <see cref="AssemblyProductAttribute"/> and
    /// <see cref="AssemblyInformationalVersionAttribute"/> (the SDK appends <c>+&lt;commit&gt;</c> when
    /// built from a git checkout). Falls back to the numeric assembly version, then "unknown" —
    /// never a fabricated value.
    /// </summary>
    public static BuildIdentity FromAssembly(Assembly? assembly, string? executablePath)
    {
        var product = assembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        string version;
        string? revision = null;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            version = plus >= 0 ? informational[..plus] : informational;
            revision = plus >= 0 && plus < informational.Length - 1 ? informational[(plus + 1)..] : null;
        }
        else
        {
            version = assembly?.GetName().Version?.ToString() ?? "unknown";
        }

        return new BuildIdentity(
            string.IsNullOrWhiteSpace(product) ? DefaultProductName : product,
            version,
            revision,
            string.IsNullOrWhiteSpace(executablePath) ? null : executablePath);
    }

    /// <summary>The running application's identity: the entry assembly and <see cref="Environment.ProcessPath"/>.</summary>
    public static BuildIdentity Current() => FromAssembly(Assembly.GetEntryAssembly(), Environment.ProcessPath);
}
