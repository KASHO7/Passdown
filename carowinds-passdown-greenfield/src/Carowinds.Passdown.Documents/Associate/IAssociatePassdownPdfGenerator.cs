using Carowinds.Passdown.Application.Preferences;

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
    /// <param name="template">
    /// Phase 17C: which visual template renders <paramref name="model"/> — Classic/Compact/Modern all
    /// receive the exact same template-neutral model (spec §21) and produce a deterministic result for
    /// a given (model, template) pair. Defaults to <see cref="AssociatePassdownTemplate.Classic"/> so
    /// every pre-17C caller (and pre-17C test) keeps producing byte-identical output without change.
    /// </param>
    AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default);

    /// <summary>Convenience form of <see cref="Generate"/> for a caller that wants a stream rather than a byte array (spec Part H/I) — null when generation did not run (see <see cref="AssociatePassdownPdfResult.Status"/> on <paramref name="result"/>).</summary>
    Stream? GenerateToStream(AssociatePassdownDocumentModel model, out AssociatePassdownPdfResult result, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default)
    {
        result = Generate(model, template, cancellationToken);
        return result.PdfBytes is { } bytes ? new MemoryStream(bytes) : null;
    }
}
