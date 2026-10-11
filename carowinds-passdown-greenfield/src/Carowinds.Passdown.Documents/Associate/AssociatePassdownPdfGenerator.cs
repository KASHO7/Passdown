using System.Globalization;
using System.Reflection;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate.Templates;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// QuestPDF-backed <see cref="IAssociatePassdownPdfGenerator"/> (spec Part H; Phase 8B redesign to a
/// one-page-normal document; revised Phase 13D Part AA/AB so overflow content renders onto real
/// additional pages instead of being refused; refactored Phase 17C §20 into a thin orchestrator over
/// one <see cref="IAssociatePassdownTemplateRenderer"/> per <see cref="AssociatePassdownTemplate"/>).
/// No network, database, filesystem, or Desktop dependency — every value it prints comes from the
/// <see cref="AssociatePassdownDocumentModel"/> passed in, so the same (model, template) pair always
/// produces the same bytes. What stays here, shared identically across every template: font
/// registration, preflight (spec Part G), QuestPDF document assembly, and the real, authoritative
/// post-render page count (see <see cref="AssociatePassdownPdfResult.PageCount"/>). What moved to each
/// renderer: the entire visual composition of the page (spec §20/§21 — "shared business/data
/// composition must remain outside the template renderer").
/// </summary>
public sealed class AssociatePassdownPdfGenerator : IAssociatePassdownPdfGenerator
{
    private readonly IAssociatePassdownPreflightService _preflightService;
    private readonly IReadOnlyDictionary<AssociatePassdownTemplate, IAssociatePassdownTemplateRenderer> _renderers;

    static AssociatePassdownPdfGenerator()
    {
        // QuestPDF requires this to be set once before any document is generated (library
        // requirement, not application state we vary) — see spec Part L's licensing note.
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterEmbeddedLatoFont();
    }

    /// <summary>
    /// Phase 15F: registers the same "Lato" font files QuestPDF would otherwise auto-discover from
    /// a physical LatoFont folder next to the running executable, but from this assembly's embedded
    /// copy instead (Carowinds.Passdown.Documents.csproj). No document anywhere calls
    /// .FontFamily(...), so this reproduces the exact font selection every PDF has always used —
    /// only the discovery mechanism changes, from a disk scan to an embedded resource, which is what
    /// makes a single-file publish (no loose files next to the .exe) viable for PDF generation. See
    /// docs/release-strategy.md "Single-file publish" for the full investigation.
    /// </summary>
    private static void RegisterEmbeddedLatoFont()
    {
        var assembly = typeof(AssociatePassdownPdfGenerator).Assembly;
        var fontResourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("LatoFont.", StringComparison.Ordinal)
                && name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in fontResourceNames)
        {
            using var fontStream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found in assembly '{assembly.FullName}'.");
            FontManager.RegisterFont(fontStream);
        }
    }

    public AssociatePassdownPdfGenerator(
        IAssociatePassdownPreflightService? preflightService = null,
        IReadOnlyDictionary<AssociatePassdownTemplate, IAssociatePassdownTemplateRenderer>? renderers = null)
    {
        _preflightService = preflightService ?? new AssociatePassdownPreflightService();
        _renderers = renderers ?? DefaultRenderers();
    }

    private static IReadOnlyDictionary<AssociatePassdownTemplate, IAssociatePassdownTemplateRenderer> DefaultRenderers()
    {
        IAssociatePassdownTemplateRenderer[] renderers =
        [
            new ClassicAssociatePassdownTemplateRenderer(),
            new CompactAssociatePassdownTemplateRenderer(),
            new ModernAssociatePassdownTemplateRenderer(),
        ];

        return renderers.ToDictionary(renderer => renderer.Template);
    }

    public AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();

        var preflight = _preflightService.Evaluate(model);
        if (!preflight.CanGenerate)
        {
            // Phase 13D Part AA: the only remaining generation-refusal case is "nothing to print"
            // (no operating date) — an estimated one-page overflow no longer refuses generation.
            return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.ValidationFailed, null, preflight, PageCount: 0);
        }

        if (!_renderers.TryGetValue(template, out var renderer))
        {
            throw new ArgumentOutOfRangeException(nameof(template), template, $"No {nameof(IAssociatePassdownTemplateRenderer)} is registered for template '{template}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 13D Part AA/AB: the Associate Passdown is normally one page, but content that
        // overflows is now allowed to render onto real additional pages rather than being refused
        // outright — QuestPDF does not throw when a single Page()'s content overflows it, it simply
        // continues drawing onto additional physical pages. CountGeneratedPdfPages below reads the
        // actual, authoritative page count of what was produced (never the pre-render estimate) so
        // the caller can show an unmistakable warning without losing the associate's work. Identical
        // regardless of which renderer composed the page.
        var bytes = Render(renderer, model, PdfDensity.Standard, cancellationToken);
        var pageCount = CountGeneratedPdfPages(bytes);

        // Phase 17F "compress before paginating": a render that spills onto extra pages is retried
        // once with the renderer's tighter spacing profile (spacing only — same content, composition,
        // and type sizes), and the tighter output is kept only when it genuinely saves a page. A
        // one-page render is never re-rendered, so normal days pay no extra cost. Deterministic: the
        // same (model, template) always takes the same path and yields the same bytes.
        if (pageCount > 1)
        {
            var tightBytes = Render(renderer, model, PdfDensity.Tight, cancellationToken);
            var tightPageCount = CountGeneratedPdfPages(tightBytes);

            if (tightPageCount < pageCount)
            {
                bytes = tightBytes;
                pageCount = tightPageCount;
            }
        }

        return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.Generated, bytes, preflight, pageCount);
    }

    private static byte[] Render(IAssociatePassdownTemplateRenderer renderer, AssociatePassdownDocumentModel model, PdfDensity density, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Document.Create(container => container.Page(page => renderer.Compose(page, model, density, cancellationToken))).GeneratePdf();
    }

    /// <summary>
    /// Reads the page count directly out of QuestPDF's own generated bytes rather than pulling in a
    /// PDF-parsing library as a production dependency — safe to do only because this is trusted
    /// output from our own well-formed writer (never an externally supplied PDF), whose page tree
    /// root object always looks like <c>&lt;&lt;/Type /Pages ... /Count N&gt;&gt;</c> in
    /// uncompressed object bytes. Falls back to trusting the estimate (1) if that shape is ever not
    /// found, so a parsing quirk in this narrow safety net can never itself cause a spurious
    /// rejection of an otherwise-valid one-page document.
    /// </summary>
    private static int CountGeneratedPdfPages(byte[] pdfBytes)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdfBytes);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"/Type\s*/Pages\b.*?/Count\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
    }
}
