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

        try
        {
            var text = File.ReadAllText(DeviceIdFilePath).Trim();
            return Guid.TryParse(text, out deviceId);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void WriteDeviceId(Guid deviceId)
    {
        var directory = Path.GetDirectoryName(DeviceIdFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(DeviceIdFilePath)}.tmp-{Guid.NewGuid():N}");
        File.WriteAllText(tempPath, deviceId.ToString("D"));
        File.Move(tempPath, DeviceIdFilePath, overwrite: true);
    }
}
