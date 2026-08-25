using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic, fully scriptable <see cref="IFinalizedPassdownPdfRetrievalService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PassdownHistoryViewModel"/> PDF-action tests — never touches SQLite and never generates a PDF.</summary>
internal sealed class FakeFinalizedPassdownPdfRetrievalService : IFinalizedPassdownPdfRetrievalService
{
    public List<DateOnly> Calls { get; } = [];

    public Func<DateOnly, FinalizedPassdownPdfRetrievalResult?>? ResultFactory { get; set; }

    public Task<FinalizedPassdownPdfRetrievalResult?> GetPdfAsync(DateOnly passdownDate, CancellationToken cancellationToken = default)
    {
        Calls.Add(passdownDate);
        FinalizedPassdownPdfRetrievalResult? result = ResultFactory is not null ? ResultFactory(passdownDate) : Default(passdownDate);
        return Task.FromResult(result);
    }

    public static FinalizedPassdownPdfRetrievalResult Default(DateOnly passdownDate)
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        return new FinalizedPassdownPdfRetrievalResult(
            bytes,
            $"Carowinds-Passdown-{passdownDate:yyyy-MM-dd}.pdf",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes.LongLength);
    }
}
