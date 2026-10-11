using Carowinds.Passdown.Application.Persistence;
using System.Globalization;

namespace Carowinds.Passdown.Application.Finalization;

/// <summary>
/// The bytes and metadata a future Passdown History UI needs to save/open one finalized passdown's
/// frozen PDF (spec Phase 9C Part V). <see cref="SuggestedFileName"/> follows the
/// <c>Carowinds-Operations-yyyy-MM-dd.pdf</c> convention (Phase 13G rename).
/// </summary>
public sealed record FinalizedPassdownPdfRetrievalResult(
    byte[] PdfBytes,
    string SuggestedFileName,
    string PdfSha256,
    long PdfSizeBytes);

/// <summary>
/// The smallest Application-layer seam a future Phase 9D History UI needs to retrieve an
/// already-frozen PDF (spec Phase 9C Part V) — deliberately never generates a PDF itself, only reads
/// whatever <see cref="IFinalizedPassdownRepository"/> already has stored. Not wired to any UI yet.
/// </summary>
public interface IFinalizedPassdownPdfRetrievalService
{
    /// <summary>Returns the stored frozen PDF for <paramref name="passdownDate"/>, or null when the date is not finalized or (legacy pre-9C row — spec Part J) has no frozen PDF.</summary>
    Task<FinalizedPassdownPdfRetrievalResult?> GetPdfAsync(DateOnly passdownDate, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IFinalizedPassdownPdfRetrievalService"/>
public sealed class FinalizedPassdownPdfRetrievalService(IFinalizedPassdownRepository repository) : IFinalizedPassdownPdfRetrievalService
{
    public async Task<FinalizedPassdownPdfRetrievalResult?> GetPdfAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        var finalized = await repository.GetByDateAsync(passdownDate, cancellationToken).ConfigureAwait(false);
        if (finalized is null || finalized.PdfBytes is null || finalized.PdfSha256 is null || finalized.PdfSizeBytes is null)
        {
            return null;
        }

        var fileName = string.Create(CultureInfo.InvariantCulture, $"Carowinds-Operations-{passdownDate:yyyy-MM-dd}.pdf");
        return new FinalizedPassdownPdfRetrievalResult(finalized.PdfBytes, fileName, finalized.PdfSha256, finalized.PdfSizeBytes.Value);
    }
}
