namespace Carowinds.Passdown.Infrastructure;

/// <summary>
/// Local, per-user storage locations (spec §3: data lives under the
/// current Windows user's local application-data directory).
/// </summary>
public static class AppPaths
{
    public static string LocalApplicationDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Carowinds Passdown");
}
