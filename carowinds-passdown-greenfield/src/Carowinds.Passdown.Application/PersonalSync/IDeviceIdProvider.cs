namespace Carowinds.Passdown.Application.PersonalSync;

/// <summary>
/// The one anonymous, per-installation identifier personal sync uses to tell which device wrote a
/// revision (Phase 16A §C). Never a machine name, Windows username, email address, or hardware ID —
/// a bare random <see cref="Guid"/> generated once and reused for the lifetime of this local install.
/// </summary>
public interface IDeviceIdProvider
{
    /// <summary>Returns the persisted device ID, generating and persisting one on first call if none exists yet.</summary>
    Guid GetOrCreateDeviceId();
}
