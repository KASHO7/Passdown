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

    /// <summary>
    /// POSTs a JSON body with caller-supplied headers. Added for
    /// <see cref="Sources.DatedAdmissionSource"/> (spec Phase 3C), whose official endpoint is a
    /// JSON POST API rather than a plain GET like <see cref="Sources.ParkHoursSource"/>'s — see
    /// docs/retrieval/dated-admission-source.md. <paramref name="headers"/> carries the public
    /// storefront identification headers the endpoint requires (not credentials/auth).
    /// </summary>
    Task<RetrievalHttpResponse> PostJsonAsync(
        Uri requestUri,
        string jsonBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken);
}

public sealed record RetrievalHttpResponse(HttpStatusCode StatusCode, string? ContentType, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
}
