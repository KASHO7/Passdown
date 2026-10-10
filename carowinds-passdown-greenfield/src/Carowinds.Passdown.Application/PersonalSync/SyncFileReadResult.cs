namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>How a sync-folder file read attempt ended (Phase 16B §11).</summary>
public enum SyncFileReadStatus
{
    Success,
    Missing,
    Malformed,
    UnsupportedVersion,
    IoError,
}

/// <summary>
/// Result of one safe sync-file read. Never a raw filesystem/JSON exception — every failure mode is
/// a distinguished, structured value so a caller (and eventually the UI) can react without a
/// try/catch of its own.
/// </summary>
public sealed record SyncFileReadResult<T>(SyncFileReadStatus Status, T? Value, string? ErrorMessage)
{
    public static SyncFileReadResult<T> Success(T value) => new(SyncFileReadStatus.Success, value, null);

    public static SyncFileReadResult<T> Missing() => new(SyncFileReadStatus.Missing, default, null);

    public static SyncFileReadResult<T> Malformed(string message) => new(SyncFileReadStatus.Malformed, default, message);

    public static SyncFileReadResult<T> UnsupportedVersion(string message) => new(SyncFileReadStatus.UnsupportedVersion, default, message);

    public static SyncFileReadResult<T> IoError(string message) => new(SyncFileReadStatus.IoError, default, message);
}
