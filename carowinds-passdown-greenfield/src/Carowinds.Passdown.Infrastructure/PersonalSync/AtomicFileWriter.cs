namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Reusable write-temp-then-rename primitive (Phase 16A §F, Phase 16B §10): a reader must never be
/// able to observe a partially written final file. The temp file is always created in the same
/// directory as the final target (never a global temp folder), so the final <see cref="File.Move"/>
/// is same-volume and atomic on both NTFS and the Unix filesystems this codebase develops/tests on.
/// Used today for local settings files (device ID, sync configuration) and the sync manifest; the
/// same primitive is what 16C+ will reuse for entity/PDF files once real sync writes exist.
/// </summary>
public static class AtomicFileWriter
{
    public static Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        WriteAsync(path, tempPath => File.WriteAllTextAsync(tempPath, content, cancellationToken), cancellationToken);

    public static Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
        WriteAsync(path, tempPath => File.WriteAllBytesAsync(tempPath, content, cancellationToken), cancellationToken);

    private static async Task WriteAsync(string path, Func<string, Task> writeTemp, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");

        try
        {
            await writeTemp(tempPath).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup only — a failed write already threw the error that matters.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above.
        }
    }
}
