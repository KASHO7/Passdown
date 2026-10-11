using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IDeviceIdProvider"/> backed by one plain-text file holding a <see cref="Guid"/> (Phase
/// 16A §C, Phase 16B §1) — mirrors <see cref="Concurrency.FileScheduledRefreshLock"/>'s own flat,
/// single-purpose file convention under <see cref="AppPaths.LocalApplicationDataDirectory"/> rather
/// than the architecture doc's illustrative <c>Settings/device-id.txt</c> nested path. A missing or
/// unparsable file (first run, or a corrupted/hand-edited one) regenerates safely rather than
/// throwing — a device ID that changes on reinstall is explicitly acceptable per the architecture
/// (Phase 16A §C: "safe... conflict rules do not depend on a DeviceId being long-lived").
/// Phase 18A REL-006: a file that exists but cannot be read <i>right now</i> (an antivirus/backup
/// scanner holding it, a transient permissions error) is not "missing" — that failure propagates as
/// an <see cref="IOException"/> and the existing id is left untouched. Pre-18A it was regenerated and
/// overwritten, so this device's own previously exported sync files suddenly looked "remote" and
/// every entity with a pending local edit surfaced as a spurious sync conflict. Every caller (the
/// sync pass, the Settings screen, conflict resolution) already treats an exception as a retryable
/// failure.
/// </summary>
public sealed class FileDeviceIdProvider(string deviceIdFilePath) : IDeviceIdProvider
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "device-id.txt");

    public string DeviceIdFilePath { get; } = deviceIdFilePath;

    public Guid GetOrCreateDeviceId()
    {
        if (TryReadExisting(out var existing))
        {
            return existing;
        }

        var deviceId = Guid.NewGuid();
        WriteDeviceId(deviceId);
        return deviceId;
    }

    private bool TryReadExisting(out Guid deviceId)
    {
        deviceId = Guid.Empty;

        if (!File.Exists(DeviceIdFilePath))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(DeviceIdFilePath).Trim();
        }
        catch (FileNotFoundException)
        {
            return false; // Deleted between the existence check and the read — genuinely missing.
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("The device ID file exists but could not be read right now; it was left unchanged.", ex);
        }

        return Guid.TryParse(text, out deviceId);
    }

    /// <summary>Phase 18B REL-003: the same durable temp-write → flush-to-disk → rename as every other small settings file (<see cref="AtomicFileWriter"/>).</summary>
    private void WriteDeviceId(Guid deviceId) => AtomicFileWriter.WriteText(DeviceIdFilePath, deviceId.ToString("D"));
}
