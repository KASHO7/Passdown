namespace Carowinds.Passdown.Infrastructure.Retrieval.Browser;

/// <summary>
/// Starts one <see cref="IBrowserSession"/> per refresh job (spec §6.4). Ownership is explicit:
/// whoever calls <see cref="StartAsync"/> owns the returned session and is responsible for
/// disposing it — disposal is what closes the context, browser process, and Playwright driver
/// ("close pages and browser processes reliably").
/// </summary>
public interface IBrowserSessionFactory
{
    Task<IBrowserSession> StartAsync(CancellationToken cancellationToken);
}
