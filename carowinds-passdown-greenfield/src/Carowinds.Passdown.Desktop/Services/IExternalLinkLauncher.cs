namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 26: the one seam for opening an official web page in the system's default browser — no
/// embedded browser, no shell command line. Callers pass only a <see cref="Uri"/> that already passed
/// <see cref="Application.Links.OfficialSourceLinkPolicy"/>. Returns <see langword="false"/> when the
/// platform could not open it; never throws for that.
/// </summary>
public interface IExternalLinkLauncher
{
    Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default);
}
