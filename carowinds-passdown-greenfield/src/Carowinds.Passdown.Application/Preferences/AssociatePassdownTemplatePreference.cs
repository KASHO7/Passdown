namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Persisted local Associate Passdown PDF template choice (Phase 17C §15/§16). A device/presentation
/// preference, not draft data — deliberately not part of Personal Sync (docs/associate-passdown-templates.md
/// "Template preference sync"): it is acceptable for two devices belonging to the same associate to
/// show different default templates, exactly like a font size or window position would be.
/// </summary>
public sealed record AssociatePassdownTemplatePreference(AssociatePassdownTemplate Template)
{
    /// <summary>Classic is the default for existing and fresh installs alike (Phase 17C §38) — no existing user is silently switched to a different template.</summary>
    public static AssociatePassdownTemplatePreference Default { get; } = new(AssociatePassdownTemplate.Classic);
}

/// <summary>Local persistence for <see cref="AssociatePassdownTemplatePreference"/> — one small settings file, never a synced one, mirroring <c>PersonalSync.ISyncConfigurationStore</c>'s convention.</summary>
public interface IAssociatePassdownTemplatePreferenceStore
{
    /// <summary>Never throws for a missing or malformed file — returns <see cref="AssociatePassdownTemplatePreference.Default"/> instead, so a corrupt local settings file can never block normal app startup or PDF generation.</summary>
    Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default);
}
