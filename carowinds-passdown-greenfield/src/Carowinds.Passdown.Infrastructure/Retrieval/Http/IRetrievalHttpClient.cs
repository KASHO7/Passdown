using System.Net;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Http;

/// <summary>
/// Shared HTTP transport for official-source adapters (spec §6.4: prefer direct HTTP/API
/// endpoints over browser automation, and share transport rather than each adapter owning a
/// client). Returns the raw response only — no HTML/JSON parsing happens here, so one source's
/// markup/schema changes never touch this type.
/// </summary>
public interface IRetrievalHttpClient
{
    Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken);
}

public sealed record RetrievalHttpResponse(HttpStatusCode StatusCode, string? ContentType, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
}
