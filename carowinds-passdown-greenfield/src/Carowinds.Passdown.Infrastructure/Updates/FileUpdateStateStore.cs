using System.Text.Json;
using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Updates;

/// <summary>
/// Phase 23D: <see cref="IUpdateStateStore"/> as one small local JSON file (<c>updates.json</c>) next to
/// <c>appearance.json</c> — last check, Remind Me Later, and the last release notes acknowledged. Same flat-file
/// convention and atomic writer; never in the database, never exported, never synced. A missing or malformed
/// file is an empty state (one non-sensitive warning), which at worst shows an update reminder or What's New
/// once more — never hides a failure.
/// </summary>
public sealed class FileUpdateStateStore(string path, ILogger<FileUpdateStateStore>? logger = null) : IUpdateStateStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "updates.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string FilePath { get; } = path;

    public UpdateLocalState Load()
    {
        if (!File.Exists(FilePath))
        {
            return UpdateLocalState.Empty;
        }

        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > 64 * 1024)
            {
                logger?.LogWarning("Update state file is unexpectedly large; ignoring it.");
                return UpdateLocalState.Empty;
            }

            return JsonSerializer.Deserialize<UpdateLocalState>(File.ReadAllText(FilePath), Options) ?? UpdateLocalState.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Update state could not be read ({ExceptionType}); starting fresh.", ex.GetType().Name);
            return UpdateLocalState.Empty;
        }
    }

    public async Task<bool> SaveAsync(UpdateLocalState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await AtomicFileWriter.WriteTextAsync(FilePath, JsonSerializer.Serialize(state, Options), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Update state could not be saved ({ExceptionType}).", ex.GetType().Name);
            return false;
        }
    }
}
