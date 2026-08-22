namespace Carowinds.Passdown.Desktop.ScheduledRefresh;

/// <summary>
/// Pure, cross-platform-testable description of the per-user Windows Task Scheduler entry (Phase
/// 6C Part A/J) that launches <c>--scheduled-refresh</c>. This type never calls a Windows API and
/// never touches Task Scheduler itself — <c>packaging/windows/Install-ScheduledRefresh.ps1</c> is
/// the only thing that actually registers a task. It exists so the rules that task's action fields
/// must follow (stable task name, fixed <see cref="CommandLineArguments.ScheduledRefreshFlag"/>
/// argument, an absolute executable path, a working directory that matches it) are testable on
/// every platform this solution builds on (CLAUDE.md: dev host Fedora, target Windows 11 x64), not
/// only on Windows.
/// </summary>
public static class ScheduledTaskPlan
{
    /// <summary>Stable task name (Phase 6C Part A/I) — install/uninstall must only ever target this exact name.</summary>
    public const string TaskName = "Carowinds Passdown Daily Refresh";

    /// <summary>
    /// Builds the task's action fields for a given installed executable path. Never reads a
    /// hard-coded or default path (Part F) — the caller (the install script, ultimately a human or
    /// Phase 7 packaging) supplies the actual installed location.
    /// </summary>
    public static ScheduledTaskDefinition Build(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path must not be null or empty.", nameof(executablePath));
        }

        if (!IsAbsolutePath(executablePath))
        {
            throw new ArgumentException($"Executable path must be absolute: '{executablePath}'.", nameof(executablePath));
        }

        var workingDirectory = GetParentDirectory(executablePath);

        return new ScheduledTaskDefinition(TaskName, executablePath, CommandLineArguments.ScheduledRefreshFlag, workingDirectory);
    }

    /// <summary>
    /// Recognizes both Windows (drive-letter or UNC) and Unix absolute paths regardless of the host
    /// OS this runs on. <see cref="Path.IsPathRooted(string)"/> alone is not enough here: on a Unix
    /// dev host it does not recognize a Windows-style path like <c>C:\Program Files\...</c> as
    /// rooted, and this validation must behave the same in tests on Fedora as it does for the real
    /// Windows paths the install script will pass at runtime.
    /// </summary>
    private static bool IsAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return true;
        }

        var isWindowsDriveRooted = path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');

        var isUncRooted = path.StartsWith(@"\\", StringComparison.Ordinal);

        return isWindowsDriveRooted || isUncRooted;
    }

    /// <summary>
    /// Splits off the parent directory using both possible separators directly, rather than
    /// <see cref="Path.GetDirectoryName(string)"/> — that method only recognizes the host platform's
    /// own separator, so it silently mishandles a Windows-style backslash path when this runs on the
    /// Unix dev host (and vice versa for a Unix-style path on Windows).
    /// </summary>
    private static string GetParentDirectory(string path)
    {
        var lastSeparator = path.LastIndexOfAny(['\\', '/']);
        if (lastSeparator < 0)
        {
            throw new ArgumentException($"Executable path has no parent directory: '{path}'.", nameof(path));
        }

        return path[..lastSeparator];
    }
}

/// <summary>
/// One resolved Task Scheduler action (Phase 6C Part A/G): the executable, its fixed argument, and
/// the working directory the install script registers. Registration itself passes
/// <see cref="ExecutablePath"/> and <see cref="Arguments"/> as separate Task Scheduler action
/// fields (<c>New-ScheduledTaskAction -Execute -Argument</c>) rather than one concatenated command
/// string, which is what avoids quoting/injection bugs in the first place;
/// <see cref="QuotedCommandLine"/> exists only to render a human-readable single-line command for
/// documentation and install-script console output.
/// </summary>
public sealed record ScheduledTaskDefinition(string TaskName, string ExecutablePath, string Arguments, string WorkingDirectory)
{
    public string QuotedCommandLine => $"{QuotePath(ExecutablePath)} {Arguments}";

    private static string QuotePath(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
