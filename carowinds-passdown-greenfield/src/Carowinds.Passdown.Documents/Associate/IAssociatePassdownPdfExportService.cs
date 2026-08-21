using Carowinds.Passdown.Application.Associate;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// The clean build-model-then-generate boundary spec Part I asks for, so a later Desktop export
/// command (Phase 4F/final integration) has exactly one call to make instead of wiring the builder
/// and generator itself. Deliberately does not open a Save dialog, pick a filesystem path, or write
/// a file — that native file-picker/export UX belongs to Desktop, not Documents (spec Part I/J).
/// </summary>
public interface IAssociatePassdownPdfExportService
{
    /// <summary>Builds the print-ready <see cref="AssociatePassdownDocumentModel"/> from <paramref name="workspace"/> and generates the PDF in one call.</summary>
    AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAssociatePassdownPdfExportService"/>
public sealed class AssociatePassdownPdfExportService(IAssociatePassdownPdfGenerator generator) : IAssociatePassdownPdfExportService
{
    public AssociatePassdownPdfResult GeneratePdf(AssociateWorkspace workspace, DateTimeOffset generatedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, generatedAt);
        return generator.Generate(model, cancellationToken);
    }
}
