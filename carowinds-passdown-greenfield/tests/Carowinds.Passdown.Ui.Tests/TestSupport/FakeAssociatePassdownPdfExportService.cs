using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Documents.Associate;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Scriptable <see cref="IAssociatePassdownPdfExportService"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel.CreatePdfCommand"/> tests — records exactly which workspace snapshot the command handed it, so a test can prove pending edits were flushed first without depending on real QuestPDF output.</summary>
internal sealed class FakeAssociatePassdownPdfExportService : IAssociatePassdownPdfExportService
{
    public List<AssociateWorkspace> Calls { get; } = [];

    public Func<AssociateWorkspace, AssociatePassdownPdfResult>? ResultFactory { get; set; }

    public AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, CancellationToken cancellationToken = default)
    {
        Calls.Add(workspace);
        return ResultFactory?.Invoke(workspace) ?? Generated();
    }

    public static AssociatePassdownPdfResult Generated() => new(
        AssociatePassdownPdfStatus.Generated,
        [1, 2, 3],
        new AssociatePassdownPreflightResult(AssociatePassdownPreflightStatus.FitsOnePage, [], [], [], [], []));

    public static AssociatePassdownPdfResult ValidationFailed() => new(
        AssociatePassdownPdfStatus.ValidationFailed,
        null,
        new AssociatePassdownPreflightResult(AssociatePassdownPreflightStatus.MissingOperatingDate, ["An operating date is required."], [], [], [], []));
}
