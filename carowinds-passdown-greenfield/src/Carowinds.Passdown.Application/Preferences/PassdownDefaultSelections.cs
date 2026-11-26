using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Shows;

namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 31D: Settings → Passdown Preferences — which Include in Passdown sections start checked for a
/// date that has no saved draft yet. A device preference (like the PDF template and appearance), saved on
/// this computer only and never part of Personal Sync. It never touches a draft that already exists: a
/// saved date always restores its own saved selections, so changing a default affects new Passdowns only.
/// SCarowinds/Haunt is not configurable here and keeps its existing default (off).
/// </summary>
public sealed record PassdownDefaultSelections(
    bool DailyAdmission,
    bool FastLane,
    bool Promotions,
    bool SeasonPasses,
    bool AddOns,
    bool OperationalNotes,
    bool Shows)
{
    /// <summary>
    /// The built-in defaults: the pre-31D new-draft selection (Daily Admission, Fast Lane and Promotions on;
    /// Season Passes, Add-Ons and Shows off), except Operational Notes, which is now on by default.
    /// </summary>
    public static PassdownDefaultSelections BuiltIn { get; } = new(
        DailyAdmission: true,
        FastLane: true,
        Promotions: true,
        SeasonPasses: false,
        AddOns: false,
        OperationalNotes: true,
        Shows: false);

    /// <summary>The section selection a brand-new draft starts with. SCarowinds/Haunt keeps its existing default (off).</summary>
    public PassdownSectionSelection ToSectionSelection() => new(
        IncludeDailyAdmission: DailyAdmission,
        IncludeFastLane: FastLane,
        IncludePromotions: Promotions,
        IncludeSeasonPasses: SeasonPasses,
        IncludeAddOns: AddOns,
        IncludeOperationalNotes: OperationalNotes,
        IncludeHauntedAttractions: PassdownSectionSelection.Default.IncludeHauntedAttractions);

    /// <summary>A brand-new draft's Shows value: the section switch only — no show is ever pre-selected.</summary>
    public AssociateShowsSelection? ToShowsSelection() => new AssociateShowsSelection(Shows, []).NullIfEmpty();
}

/// <summary>
/// Phase 31D: local persistence for <see cref="PassdownDefaultSelections"/> — one small settings file on this
/// computer (outside any sync folder), the same convention as <see cref="IAppearancePreferenceStore"/>.
/// Never synced; does not require Personal Sync.
/// </summary>
public interface IPassdownDefaultsPreferenceStore
{
    /// <summary>
    /// Never throws: a missing file returns <see cref="PassdownDefaultSelections.BuiltIn"/>; a missing individual
    /// value falls back to its built-in default; an unreadable file returns the built-in defaults.
    /// </summary>
    Task<PassdownDefaultSelections> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes only the Passdown defaults file; no draft, database row or sync state is touched.</summary>
    Task SaveAsync(PassdownDefaultSelections defaults, CancellationToken cancellationToken = default);
}
