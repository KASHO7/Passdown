using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IPdfSaveFilePicker"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel.CreatePdfCommand"/> tests — never touches Avalonia's real storage provider/native dialogs.</summary>
internal sealed class FakePdfSaveFilePicker : IPdfSaveFilePicker
{
    public List<(byte[] Bytes, string SuggestedFileName)> Calls { get; } = [];

    public Func<PdfSaveResult>? ResultFactory { get; set; }

    public Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default)
    {
        Calls.Add((pdfBytes, suggestedFileName));
        return Task.FromResult(ResultFactory?.Invoke() ?? PdfSaveResult.Saved("/fake/path/output.pdf"));
    }
}
