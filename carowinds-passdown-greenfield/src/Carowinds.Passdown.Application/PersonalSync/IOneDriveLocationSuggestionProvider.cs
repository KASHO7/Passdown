namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// Best-effort OneDrive root suggestions (Phase 16A §K, Phase 16B §3). Suggestions only — never
/// assumed valid, never required, and never a substitute for the user's own folder choice
/// (<see cref="SyncConfiguration.SyncRootPath"/> remains authoritative regardless of what this
/// returns). An empty result is normal, not an error: a machine without OneDrive installed simply
/// has none of the environment variables this reads.
/// </summary>
public interface IOneDriveLocationSuggestionProvider
{
    /// <summary>Distinct, non-empty candidate roots, in no particular priority order. Empty when nothing is detected.</summary>
    IReadOnlyList<string> GetSuggestedRoots();
}
