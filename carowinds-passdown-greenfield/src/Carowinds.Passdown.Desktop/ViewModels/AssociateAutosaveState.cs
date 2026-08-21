namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Presentation-only autosave lifecycle for the Associate screen (spec §8.3's "auto-save after
/// meaningful changes ... visible Save status"), distinct from
/// <see cref="Application.Associate.AssociateSaveState"/> — that Application-layer state only ever
/// observes "never saved" vs. "saved" between calls; this adds the in-flight
/// <see cref="Saving"/>/<see cref="Unsaved"/> states a debounced ViewModel needs to show while a
/// save has not happened yet or is in progress.
/// </summary>
public enum AssociateAutosaveState
{
    Saved,
    Unsaved,
    Saving,
}
