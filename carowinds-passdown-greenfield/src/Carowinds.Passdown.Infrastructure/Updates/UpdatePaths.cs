namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: where update work happens. Packages and staged versions live under their own per-user folder,
/// <c>%LOCALAPPDATA%\Carowinds Operations Updates</c> — a sibling of the data folder, never inside it (the
/// SQLite database, drafts, History PDFs and sync settings are never next to update packages) and never in
/// a cloud-synced folder. The installation folder only ever gets the short-lived
/// <see cref="InstallWorkDirectoryName"/> folder (incoming copy and rollback backup) during an install.
/// </summary>
public sealed class UpdatePaths(string root)
{
    /// <summary>Hidden-by-convention work folder inside the installation folder; never part of a release package.</summary>
    public const string InstallWorkDirectoryName = ".carowinds-update";

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Carowinds Operations Updates");

    public static UpdatePaths Default { get; } = new(DefaultRoot);

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>Partial and complete package downloads, one folder per version.</summary>
    public string Downloads => Path.Combine(Root, "downloads");

    /// <summary>Extracted, verified versions waiting to be installed. The updater runs from here.</summary>
    public string Staging => Path.Combine(Root, "staging");

    /// <summary>Install requests written by the app for the updater.</summary>
    public string Requests => Path.Combine(Root, "requests");

    /// <summary>Install records written by the updater (status, failure code, backup location).</summary>
    public string Records => Path.Combine(Root, "records");

    public string RequestFile(Guid requestId) => Path.Combine(Requests, $"install-{requestId:N}.json");

    public string RecordFile(Guid requestId) => Path.Combine(Records, $"install-{requestId:N}.json");

    public string ConfirmationFile(Guid requestId) => Path.Combine(Records, $"install-{requestId:N}.launched");

    /// <summary>True when <paramref name="path"/> is <paramref name="directory"/> itself or lies inside it (after full normalization).</summary>
    public static bool IsWithin(string path, string directory)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullPath.Equals(fullDirectory, comparison)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// True when the file or folder itself is a symbolic link or junction. Deliberately not "any reparse
    /// point": OneDrive Files On-Demand marks ordinary synced files and folders with a cloud reparse tag, and a
    /// portable folder extracted to a redirected Desktop must still be updatable. Links are what could make a
    /// write land outside the intended folder, so links are what is refused.
    /// </summary>
    public static bool IsLink(FileSystemInfo info) => info.Exists && info.LinkTarget is not null;

    public static bool IsLink(string path)
    {
        if (Directory.Exists(path))
        {
            return IsLink(new DirectoryInfo(path));
        }

        return File.Exists(path) && IsLink(new FileInfo(path));
    }
}
