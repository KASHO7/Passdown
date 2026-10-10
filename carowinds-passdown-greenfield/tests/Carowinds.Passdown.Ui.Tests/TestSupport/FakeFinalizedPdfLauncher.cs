using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IFinalizedPdfLauncher"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.PassdownHistoryViewModel.OpenFinalPdfCommand"/> tests — never shells out to the OS.</summary>
internal sealed class FakeFinalizedPdfLauncher : IFinalizedPdfLauncher
{
    public List<(byte[] Bytes, string SuggestedFileName)> Calls { get; } = [];

    public Func<PdfOpenResult>? ResultFactory { get; set; }

    public Task<PdfOpenResult> OpenAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        Calls.Add((pdfBytes, suggestedFileName));
        return Task.FromResult(ResultFactory?.Invoke() ?? PdfOpenResult.Opened());
    }
}
