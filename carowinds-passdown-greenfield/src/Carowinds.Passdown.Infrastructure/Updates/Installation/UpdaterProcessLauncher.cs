using System.Diagnostics;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Updates.Installation;

/// <summary>
/// Phase 23D: <see cref="IUpdateInstallLauncher"/>. Writes the install request and starts the updater that
/// shipped inside the verified, staged new version — never an executable named by remote metadata. The
/// command line is exactly <c>--request &lt;path&gt;</c>, passed as a separate argument (no shell, no string
/// concatenation), and the path always points into this user's own requests folder.
/// </summary>
public sealed class UpdaterProcessLauncher(
    UpdatePaths paths,
    string? installDirectory = null,
    Func<ProcessStartInfo, bool>? startProcess = null,
    ILogger<UpdaterProcessLauncher>? logger = null) : IUpdateInstallLauncher
{
    public const string RequestArgument = "--request";

    private readonly string _installDirectory = Path.TrimEndingDirectorySeparator(installDirectory ?? AppContext.BaseDirectory);
    private readonly Func<ProcessStartInfo, bool> _startProcess = startProcess ?? StartDetached;

    public UpdateLaunchResult Launch(StagedUpdate staged, SemanticVersion installedVersion)
    {
        var requestId = Guid.NewGuid();
        var updater = Path.Combine(staged.StagedDirectory, UpdateManifestJson.UpdaterExecutable);
        if (!UpdatePaths.IsWithin(staged.StagedDirectory, paths.Staging) || !File.Exists(updater) || UpdatePaths.IsLink(updater))
        {
            return new UpdateLaunchResult(UpdateLaunchFailureKind.StagedUpdaterMissing, requestId);
        }

        DateTimeOffset? startUtc = null;
        try
        {
            using var current = Process.GetCurrentProcess();
            startUtc = current.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The updater then waits on the process id alone.
        }

        var request = new UpdateInstallRequest(
            UpdateInstallRequest.CurrentFormatVersion,
            requestId,
            UpdateManifest.CarowindsOperationsProductId,
            installedVersion.Normalized,
            staged.Offer.Version.Normalized,
            _installDirectory,
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(staged.StagedDirectory)),
            Environment.ProcessId,
            startUtc,
            DateTimeOffset.UtcNow,
            staged.Offer.Manifest.Files);

        var requestPath = paths.RequestFile(requestId);
        try
        {
            UpdateJson.WriteAtomic(requestPath, request);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Install request could not be written ({ExceptionType}).", ex.GetType().Name);
            return new UpdateLaunchResult(UpdateLaunchFailureKind.RequestNotWritten, requestId);
        }

        var startInfo = new ProcessStartInfo(updater)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = staged.StagedDirectory,
        };
        startInfo.ArgumentList.Add(RequestArgument);
        startInfo.ArgumentList.Add(requestPath);

        if (!_startProcess(startInfo))
        {
            try
            {
                File.Delete(requestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return new UpdateLaunchResult(UpdateLaunchFailureKind.UpdaterDidNotStart, requestId);
        }

        logger?.LogInformation("External updater started for {Version}.", staged.Offer.Version.Normalized);
        return new UpdateLaunchResult(UpdateLaunchFailureKind.None, requestId);
    }

    private static bool StartDetached(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
