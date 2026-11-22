using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;

namespace Carowinds.Passdown.Infrastructure.Retrieval.Sources;

/// <summary>
/// Phase 34C: the transport and parse step that the six dated accesso sources (admission, Fast Lane,
/// haunted, Bring-A-Friend, Bring-A-Friend Duo, Military/First Responder) each used to copy verbatim.
/// One <c>getkeywordcalendar</c> POST, using the shared endpoint and headers from
/// <see cref="AccessoKeywordCalendarRequest"/>, becomes either a parsed
/// <see cref="AccessoKeywordCalendarService"/> or the exception that describes why it could not be read.
///
/// Deliberately does not decide anything about the answer: "no dates found", status handling, horizon
/// probes, date matching, product parsing and every user-facing sentence stay in each source. The source
/// turns <see cref="AccessoKeywordCalendarCallResult.Failure"/> into its own <c>Error</c> result, so the
/// evidence (source name, page, wording) is unchanged.
///
/// Caller cancellation propagates as before (an <see cref="OperationCanceledException"/> is never turned
/// into a failure), and the call goes through whatever <see cref="IRetrievalHttpClient"/> the source was
/// given — in the app that is the shared Phase 34B-2 coalescing client, whose accesso body key
/// (<see cref="AccessoKeywordCalendarCoalescing"/>) is not touched here.
/// </summary>
internal static class AccessoKeywordCalendarCall
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<AccessoKeywordCalendarCallResult> PostAsync(
        IRetrievalHttpClient httpClient,
        string body,
        CancellationToken cancellationToken)
    {
        RetrievalHttpResponse response;
        try
        {
            response = await httpClient
                .PostJsonAsync(AccessoKeywordCalendarRequest.Endpoint, body, AccessoKeywordCalendarRequest.Headers, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AccessoKeywordCalendarCallResult.Failed(ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            return AccessoKeywordCalendarCallResult.Failed(new InvalidOperationException(
                $"POST {AccessoKeywordCalendarRequest.Endpoint} returned {(int)response.StatusCode} {response.StatusCode}. Body: {Truncate(response.Body)}"));
        }

        AccessoKeywordCalendarApiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AccessoKeywordCalendarApiResponse>(response.Body, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AccessoKeywordCalendarCallResult.Failed(ex);
        }

        if (parsed?.Service is null)
        {
            return AccessoKeywordCalendarCallResult.Failed(new InvalidOperationException(
                $"Unexpected response shape from {AccessoKeywordCalendarRequest.Endpoint}: no SERVICE object. Body: {Truncate(response.Body)}"));
        }

        return AccessoKeywordCalendarCallResult.Succeeded(parsed.Service, response.Body);
    }

    /// <summary>Caps a response body quoted in a technical (log-only) detail at 500 characters.</summary>
    public static string Truncate(string value) => value.Length <= 500 ? value : value[..500] + "…";
}

/// <summary>
/// Result of one <see cref="AccessoKeywordCalendarCall.PostAsync"/>: either a parsed
/// <see cref="Service"/> (with the raw <see cref="Body"/>, which some sources quote in later technical
/// details) or the <see cref="Failure"/> that the calling source reports as its own <c>Error</c>.
/// </summary>
internal readonly struct AccessoKeywordCalendarCallResult
{
    private AccessoKeywordCalendarCallResult(AccessoKeywordCalendarService? service, string? body, Exception? failure)
    {
        Service = service;
        Body = body;
        Failure = failure;
    }

    public AccessoKeywordCalendarService? Service { get; }

    public string? Body { get; }

    public Exception? Failure { get; }

    public static AccessoKeywordCalendarCallResult Succeeded(AccessoKeywordCalendarService service, string body) => new(service, body, null);

    public static AccessoKeywordCalendarCallResult Failed(Exception failure) => new(null, null, failure);
}

/// <summary>
/// Phase 34C: builds the three payload-less results every dated accesso source returns — NotPublished,
/// Unavailable and Error — with that source's own id, evidence name and official page. These were six
/// copies of the same evidence construction. The wording is still supplied by each source (it differs per
/// product and is user-facing), and the strategy is always <see cref="RetrievalStrategy.DirectApi"/>, as
/// it was in every copy.
/// </summary>
internal sealed class AccessoDatedResults<TPayload>(string sourceId, string evidenceName, Uri officialPageUrl)
{
    public SourceResult<TPayload> NotPublished(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<TPayload>.WithoutPayload(
            sourceId,
            new SourceEvidence(
                evidenceName,
                officialPageUrl,
                timeChecked,
                RetrievalStatus.NotPublished,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    public SourceResult<TPayload> Unavailable(DateTimeOffset timeChecked, DateOnly requestedDate, string detail) =>
        SourceResult<TPayload>.WithoutPayload(
            sourceId,
            new SourceEvidence(
                evidenceName,
                officialPageUrl,
                timeChecked,
                RetrievalStatus.Unavailable,
                detail,
                RetrievalStrategy.DirectApi,
                requestedDate: requestedDate));

    public SourceResult<TPayload> Error(DateTimeOffset timeChecked, DateOnly requestedDate, Exception exception, string userFacingDetail) =>
        SourceResult<TPayload>.WithoutPayload(
            sourceId,
            RetrievalDiagnostics.Error(
                evidenceName,
                officialPageUrl,
                timeChecked,
                RetrievalStrategy.DirectApi,
                userFacingDetail,
                exception,
                requestedDate));
}
