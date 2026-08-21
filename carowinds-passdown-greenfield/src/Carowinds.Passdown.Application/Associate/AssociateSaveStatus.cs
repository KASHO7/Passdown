namespace Carowinds.Passdown.Application.Associate;

/// <summary>
/// The two states the service itself can ever observe about a draft's save history (spec §8.3's
/// "visible Save status"). Deliberately does not include an in-flight "Saving" state: that is
/// inherently about a specific async call being awaited right now, which is a future debounced
/// ViewModel's own concern (it knows when it is awaiting <see cref="IAssociatePassdownService.SaveDraftAsync"/>),
/// not something this presentation-neutral service can observe about itself between calls.
/// </summary>
public enum AssociateSaveState
{
    /// <summary>This operating date has never been saved (still showing the default draft).</summary>
    NeverSaved,
    Saved,
}

/// <summary>What a future UI needs to render "Saved locally at 2:14 PM" (spec §8.3/§10.3), with no formatting baked in.</summary>
public sealed record AssociateSaveStatus(AssociateSaveState State, DateTimeOffset? LastSavedAt)
{
    public static AssociateSaveStatus NeverSaved { get; } = new(AssociateSaveState.NeverSaved, null);
}
