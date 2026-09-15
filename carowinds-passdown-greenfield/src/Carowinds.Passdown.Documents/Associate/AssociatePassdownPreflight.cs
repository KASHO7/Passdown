using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Whether <see cref="AssociatePassdownDocumentModel"/> content is expected to fit on one Letter
/// page (spec Part E/F/G; Phase 8B-correction: the Associate Passdown is a strict one-page document
/// with no continuation page — see <see cref="AssociatePassdownPdfGenerator"/>). Never itself a
/// rendering result — see <see cref="AssociatePassdownPdfResult"/> for what was actually generated.
/// </summary>
public enum AssociatePassdownPreflightStatus
{
    /// <summary>Everything fits on one page — the normal, expected case.</summary>
    FitsOnePage,

    /// <summary>
    /// Content is estimated to exceed one page's capacity. Phase 13D Part AA: this is no longer a
    /// generation-refusal signal — <see cref="AssociatePassdownPdfGenerator"/> still generates and
    /// saves the PDF (QuestPDF paginates the overflow onto real additional pages rather than clipping
    /// it), and the caller is expected to surface an unmistakable warning from the generator's actual
    /// post-generation page count (see <see cref="AssociatePassdownPdfResult.PageCount"/>) rather than
    /// block the associate's work. This estimate remains only as an early, pre-render signal.
    /// </summary>
    ExceedsOnePage,

    /// <summary>The model has no operating date — generation cannot proceed at all (spec Part G).</summary>
    MissingOperatingDate,
}

/// <summary>
/// The outcome of checking an <see cref="AssociatePassdownDocumentModel"/> for one-page fit (spec
/// Part F/G; revised Phase 13D Part AA) — computed once here so the generator never re-derives its
/// own, possibly inconsistent, notion of what fits. Deterministic and presentation-neutral: this is a
/// line-count estimate over already-final display strings, never an actual QuestPDF layout pass.
/// </summary>
public sealed record AssociatePassdownPreflightResult(
    AssociatePassdownPreflightStatus Status,
    IReadOnlyList<string> Reasons)
{
    /// <summary>
    /// True whenever there is a document to render at all — Phase 13D Part AA: an estimated
    /// multi-page document is no longer refused, only one with no operating date at all (nothing to
    /// print). <see cref="AssociatePassdownPdfGenerator"/> is the actual, authoritative source of
    /// whether the real generated PDF exceeds one page (its own post-generation page count).
    /// </summary>
    public bool CanGenerate => Status != AssociatePassdownPreflightStatus.MissingOperatingDate;
}

public interface IAssociatePassdownPreflightService
{
    AssociatePassdownPreflightResult Evaluate(AssociatePassdownDocumentModel model);
}

/// <summary>
/// Line-budget based one-page fit estimate (spec Part F/G). The budget constants below are tuned
/// against real rendered output (visual sample-PDF review, verified with actual QuestPDF page
/// counts) rather than derived from font metrics — this class intentionally stays a cheap,
/// deterministic estimate rather than an actual QuestPDF layout pass, so preflight can run instantly
/// and independent of the PDF engine. Its job is strictly binary: can this content safely render on
/// exactly one page, yes or no — it no longer decides what would move to a continuation page,
/// because there is no continuation page.
/// </summary>
public sealed class AssociatePassdownPreflightService : IAssociatePassdownPreflightService
{
    /// <summary>Estimated printable body lines available on one Letter page beneath the fixed header/hours/attendance/ERT/Daily Feature block, tuned so the associate/dense fixtures in Documents.Tests render as exactly one real QuestPDF page under the Phase 17D master layout (every product is one row, never a tile/card).</summary>
    private const int TotalBodyLineBudget = 70;

    private const int WrapCharsPerLine = 95;
    private const int ProductNoteWrapCharsPerLine = 70;

    /// <summary>
    /// Shared with <see cref="AssociatePassdownPdfGenerator"/>'s post-generation page-count safety
    /// net, so both the fast estimate-based rejection and the rare actual-overflow rejection report
    /// the same actionable reason.
    /// </summary>
    internal const string ExceedsOnePageMessage =
        "The Associate Passdown cannot fit on one page. Shorten Operational Notes, reduce the number of included products, or hide an optional section, then try again.";

    public AssociatePassdownPreflightResult Evaluate(AssociatePassdownDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.OperatingDate is null)
        {
            return new AssociatePassdownPreflightResult(
                AssociatePassdownPreflightStatus.MissingOperatingDate,
                ["An operating date is required to generate the Associate Passdown PDF."]);
        }

        var remaining = TotalBodyLineBudget - EstimateFixedLines(model);
        remaining -= EstimateProductLines(model.ProductCategories);
        remaining -= EstimateNotesLines(model.OperationalNotes);

        return remaining >= 0
            ? new AssociatePassdownPreflightResult(AssociatePassdownPreflightStatus.FitsOnePage, [])
            : new AssociatePassdownPreflightResult(AssociatePassdownPreflightStatus.ExceedsOnePage, [ExceedsOnePageMessage]);
    }

    private static int EstimateFixedLines(AssociatePassdownDocumentModel model)
    {
        var lines = 1; // header: date heading + rule, one line-equivalent.

        lines += 1; // Park Hours row (headline + badge on one line).
        lines += 1; // North Gate / Avenue row.
        lines += EstimateWrappedLines(model.ParkHours.PrivateBuyoutDetails, WrapCharsPerLine);

        lines += 1; // Attendance / Pre-solds row.

        lines += EstimateEarlyRideTimeLines(model.EarlyRideTime);

        // Daily Feature is mandatory content (spec Phase 17D §15) — always one heading line plus at
        // least one content/"Not entered." line, regardless of whether text was actually entered.
        lines += 1 + Math.Max(1, EstimateWrappedLines(model.DailyFeature.Text, WrapCharsPerLine));

        return lines;
    }

    /// <summary>Phase 17D §10: the PDF shows rides only, on one row of the summary grid — mirrors <see cref="Templates.PdfFormatting.ErtLineText"/>'s shape so the estimate never drifts from what actually renders.</summary>
    private static int EstimateEarlyRideTimeLines(PdfEarlyRideTime earlyRideTime) => earlyRideTime.Kind switch
    {
        EffectiveErtKind.NotConfigured or EffectiveErtKind.NoErt => 1,
        _ => Math.Max(1, EstimateWrappedLines(string.Join(" • ", earlyRideTime.Rides), WrapCharsPerLine)),
    };

    /// <summary>
    /// Total estimated line-equivalent cost of every product category (Phase 17D §14 — every template
    /// now renders every product as one aligned row, never a tile/card, regardless of tier).
    /// </summary>
    private static int EstimateProductLines(IReadOnlyList<PdfProductCategory> categories)
    {
        var total = 0;

        foreach (var category in categories)
        {
            total += 1 + category.Products.Sum(product => 1 + EstimateWrappedLines(product.Notes, ProductNoteWrapCharsPerLine));
        }

        return total;
    }

    private static int EstimateNotesLines(IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return 0;
        }

        return 1 + notes.Sum(note => Math.Max(1, EstimateWrappedLines(note, WrapCharsPerLine))); // +1 for the "Operational Notes" heading.
    }

    private static int EstimateWrappedLines(string? text, int charsPerLine) =>
        string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Trim().Length / (double)charsPerLine);
}
