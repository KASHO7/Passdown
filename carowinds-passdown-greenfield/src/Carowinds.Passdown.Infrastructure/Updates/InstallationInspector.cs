using Carowinds.Passdown.Application.Updates;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: <see cref="IInstallationInspector"/>. In-place updating needs a Windows portable release folder
/// (the main executable, its DLL and the bundled updater side by side — the experimental single-file EXE and a
/// development build have no updater next to them) that the current user can actually write to. Writability is
/// tested by creating and deleting a probe file, never inferred from the folder's name or location, and no
/// elevation is ever requested.
/// </summary>
public sealed class InstallationInspector(string? installDirectory = null, bool? isWindows = null) : IInstallationInspector
{
    private readonly string _installDirectory = Path.TrimEndingDirectorySeparator(installDirectory ?? AppContext.BaseDirectory);
    private readonly bool _isWindows = isWindows ?? OperatingSystem.IsWindows();

    public InstallationReadiness Inspect()
    {
        if (!_isWindows)
        {
            return new InstallationReadiness(InstallationReadinessKind.UnsupportedPlatform, _installDirectory);
        }

        var required = new[] { UpdateManifestJson.MainExecutable, "Carowinds.Passdown.Desktop.dll", UpdateManifestJson.UpdaterExecutable };
        if (required.Any(name => !File.Exists(Path.Combine(_installDirectory, name))) || UpdatePaths.IsLink(_installDirectory))
        {
            return new InstallationReadiness(InstallationReadinessKind.NotPortableInstallation, _installDirectory);
        }

        return CanWrite(_installDirectory)
            ? new InstallationReadiness(InstallationReadinessKind.Ready, _installDirectory)
            : new InstallationReadiness(InstallationReadinessKind.NotWritable, _installDirectory);
    }

    public static bool CanWrite(string directory)
    {
        var probe = Path.Combine(directory, $".carowinds-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
