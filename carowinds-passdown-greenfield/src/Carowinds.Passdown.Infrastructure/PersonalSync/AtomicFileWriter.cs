using System.Text;
using System.Text.RegularExpressions;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// Reusable write-temp-then-rename primitive (Phase 16A §F, Phase 16B §10): a reader must never be
/// able to observe a partially written final file. The temp file is always created in the same
/// directory as the final target (never a global temp folder), so the final <see cref="File.Move(string, string, bool)"/>
/// is same-volume and atomic on both NTFS and the Unix filesystems this codebase develops/tests on.
/// Used for every small persistent file the app writes: Personal Sync envelopes/PDFs and the sync
/// manifest, the sync configuration, the device id, and the PDF-template preference.
/// <para>
/// Phase 18B REL-003 (power-loss durability). The sequence is:
/// create a new temp file (<see cref="FileMode.CreateNew"/>, never reusing a leftover name) →
/// write the full content → flush the managed buffer → <see cref="FileStream.Flush(bool)"/> with
/// <c>flushToDisk: true</c> (<c>FlushFileBuffers</c> on Windows, <c>fsync</c> on Unix), so the temp
/// file's data and size are on stable storage → close → rename over the destination. Because the
/// data is durable <i>before</i> the rename can be, a power loss at any instant leaves the
/// destination as either the complete previous version or the complete new version — never a
/// zero-length or partially written file (the pre-18B failure mode: the rename could reach the NTFS
/// journal while the data was still only in the cache).
/// </para>
/// <para>
/// Residual risk (documented in docs/personal-sync-architecture.md §16B.1): the rename itself is not
/// forced to disk. .NET exposes no portable way to fsync a directory, and on Windows that would need
/// <c>MoveFileEx(MOVEFILE_WRITE_THROUGH)</c>/P-Invoke. So a power loss within the filesystem's
/// metadata-commit window (typically a few seconds) after a write "completed" can roll the
/// destination back to the previous complete version. The write is then simply redone: sync re-exports
/// anything whose synced state was not recorded, and settings are re-saved by the next user action.
/// </para>
/// </summary>
public static partial class AtomicFileWriter
{
    /// <summary>Marker between the destination file name and the 32-hex-digit suffix of every temp file this type creates.</summary>
    public const string TempMarker = ".tmp-";

    /// <summary>UTF-8 without a BOM — byte-identical to the <see cref="File.WriteAllTextAsync(string, string?, CancellationToken)"/> output this type produced before 18B.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
        WriteAsync(path, Utf8NoBom.GetBytes(content), faults: null, cancellationToken);

    public static Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default) =>
        WriteAsync(path, content, faults: null, cancellationToken);

    /// <summary>Synchronous variant for callers that are themselves synchronous (the device-id provider).</summary>
    public static void WriteText(string path, string content) =>
        WriteAsync(path, Utf8NoBom.GetBytes(content), faults: null, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// True when <paramref name="fileName"/> is exactly the shape of a temp file this type creates
    /// (<c>&lt;final name&gt;.tmp-&lt;32 lowercase hex&gt;</c>); <paramref name="finalFileName"/> is the
    /// destination it was destined for. Used by stale-temp cleanup, which must never touch anything else.
    /// </summary>
    public static bool TryParseTempFileName(string fileName, out string finalFileName)
    {
        var match = TempFileNamePattern().Match(fileName);
        finalFileName = match.Success ? match.Groups["final"].Value : string.Empty;
        return match.Success;
    }

    [GeneratedRegex(@"^(?<final>[^\\/]+)\.tmp-[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TempFileNamePattern();

    internal static async Task WriteAsync(string path, ReadOnlyMemory<byte> content, IAtomicWriteFaults? faults, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? string.Empty, $"{Path.GetFileName(path)}{TempMarker}{Guid.NewGuid():N}");

        try
        {
            faults?.At(AtomicWriteStage.BeforeTempCreated, tempPath);

            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous))
            {
                // Two halves so a fault test can stop with a genuinely partial temp file on disk.
                var half = content.Length / 2;
                await stream.WriteAsync(content[..half], cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                faults?.At(AtomicWriteStage.DuringWrite, tempPath);
                await stream.WriteAsync(content[half..], cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                // The durability point: data + size on stable storage before the rename can exist.
                stream.Flush(flushToDisk: true);
            }

            faults?.At(AtomicWriteStage.BeforeReplace, tempPath);
            cancellationToken.ThrowIfCancellationRequested();
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
            // Best-effort cleanup only — a failed write already threw the error that matters. A temp
            // file left behind here is removed later by stale-temp cleanup (Phase 18B REL-007).
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above.
        }
    }
}

/// <summary>Points in <see cref="AtomicFileWriter"/>'s sequence where a test can inject a failure.</summary>
internal enum AtomicWriteStage
{
    BeforeTempCreated,
    DuringWrite,
    BeforeReplace,
}

/// <summary>Test-only fault injection seam; production callers always pass <see langword="null"/>.</summary>
internal interface IAtomicWriteFaults
{
    void At(AtomicWriteStage stage, string tempPath);
}
