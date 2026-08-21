namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Generates an Associate Passdown PDF from an already-built <see cref="AssociatePassdownDocumentModel"/>
/// (spec Part A/H). Implementations must not touch the network, a repository, or the filesystem
/// beyond the stream/bytes they hand back — this is a pure, deterministic rendering step.
/// </summary>
public interface IAssociatePassdownPdfGenerator
{
    /// <summary>
    /// Runs preflight (spec Part G) and, if the model can generate at all, renders it — as one page
    /// when it fits, or a page-one summary plus a labeled continuation page when it does not (spec
    /// §11.4). Never silently truncates notes or drops an included product (spec Part F).
    /// </summary>
    AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, CancellationToken cancellationToken = default);

    /// <summary>Convenience form of <see cref="Generate"/> for a caller that wants a stream rather than a byte array (spec Part H/I) — null when generation did not run (see <see cref="AssociatePassdownPdfResult.Status"/> on <paramref name="result"/>).</summary>
    Stream? GenerateToStream(AssociatePassdownDocumentModel model, out AssociatePassdownPdfResult result, CancellationToken cancellationToken = default)
    {
        result = Generate(model, cancellationToken);
        return result.PdfBytes is { } bytes ? new MemoryStream(bytes) : null;
    }
}
