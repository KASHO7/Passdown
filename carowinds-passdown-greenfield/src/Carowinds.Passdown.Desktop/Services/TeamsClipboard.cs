using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Documents.Teams;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>Phase 29: how one Teams copy ended.</summary>
public enum TeamsCopyOutcome
{
    Copied,

    /// <summary>The formatted copy failed but the identical plain text was copied instead.</summary>
    CopiedAsPlainTextFallback,

    Failed,
}

/// <summary>
/// Phase 29: the one place a <see cref="TeamsMessage"/> goes to the clipboard, shared by the existing Copy
/// menu and the morning-message composer. Plain and formatted output are rendered from the same message.
/// A formatted copy that fails falls back to plain text; nothing here ever throws to the caller.
/// </summary>
public static class TeamsClipboard
{
    public static async Task<TeamsCopyOutcome> CopyAsync(IClipboardService clipboard, TeamsMessage message, TeamsCopyMode mode, ILogger? logger, string context)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(message);

        var plain = TeamsPlainTextRenderer.Render(message);

        if (mode == TeamsCopyMode.Formatted)
        {
            try
            {
                await clipboard.SetTextAndHtmlAsync(plain, TeamsHtmlRenderer.Render(message)).ConfigureAwait(true);
                return TeamsCopyOutcome.Copied;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "Formatted Teams copy ({Context}) failed; trying plain text.", context);
            }

            return await TryPlainAsync(clipboard, plain, logger, context).ConfigureAwait(true)
                ? TeamsCopyOutcome.CopiedAsPlainTextFallback
                : TeamsCopyOutcome.Failed;
        }

        return await TryPlainAsync(clipboard, plain, logger, context).ConfigureAwait(true) ? TeamsCopyOutcome.Copied : TeamsCopyOutcome.Failed;
    }

    public const string FailureMessage = "Couldn't copy to the clipboard. Try again.";

    public const string FallbackSuffix = " as plain text (formatted copy wasn't available)";

    private static async Task<bool> TryPlainAsync(IClipboardService clipboard, string plain, ILogger? logger, string context)
    {
        try
        {
            await clipboard.SetTextAsync(plain).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Teams copy ({Context}) failed.", context);
            return false;
        }
    }
}
