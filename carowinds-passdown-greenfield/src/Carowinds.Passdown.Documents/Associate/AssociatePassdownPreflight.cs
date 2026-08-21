namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Whether <see cref="AssociatePassdownDocumentModel"/> content is expected to fit on one Letter
/// page (spec Part E/F/G). Never itself a rendering result — see <see cref="AssociatePassdownPdfResult"/>
/// for what was actually generated.
/// </summary>
public enum AssociatePassdownPreflightStatus
{
    /// <summary>Everything fits on one page — the normal, expected case.</summary>
    FitsOnePage,

    /// <summary>Content exceeds one page's estimated capacity (spec §11.4's "unusually busy days"). Nothing is dropped: <see cref="AssociatePassdownPreflightResult.ContinuationProductCategories"/>/<see cref="AssociatePassdownPreflightResult.ContinuationNotes"/> hold what spills to the "Supporting Details" continuation page.</summary>
    RequiresContinuationPage,

    /// <summary>The model has no operating date — generation cannot proceed at all (spec Part G).</summary>
    MissingOperatingDate,
}

/// <summary>
/// The outcome of checking an <see cref="AssociatePassdownDocumentModel"/> for one-page fit (spec
/// Part F/G), including the exact page split <see cref="AssociatePassdownPdfGenerator"/> renders
/// from — computed once here so the generator never re-derives its own, possibly inconsistent,
/// notion of what fits. Deterministic and presentation-neutral: this is a line-count estimate over
/// already-final display strings, never an actual QuestPDF layout pass.
/// </summary>
public sealed record AssociatePassdownPreflightResult(
    AssociatePassdownPreflightStatus Status,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<PdfProductCategory> PageOneProductCategories,
    IReadOnlyList<string> PageOneNotes,
    IReadOnlyList<PdfProductCategory> ContinuationProductCategories,
    IReadOnlyList<string> ContinuationNotes)
{
    public bool CanGenerate => Status != AssociatePassdownPreflightStatus.MissingOperatingDate;

    public bool NeedsContinuationPage => Status == AssociatePassdownPreflightStatus.RequiresContinuationPage;
}

public interface IAssociatePassdownPreflightService
{
    AssociatePassdownPreflightResult Evaluate(AssociatePassdownDocumentModel model);
}

/// <summary>
/// Line-budget based one-page fit estimate (spec Part F/G). The budget constants below are tuned
/// against real rendered output (Phase 4E's visual sample-PDF review) rather than derived from font
/// metrics — this class intentionally stays a cheap, deterministic estimate rather than an actual
/// QuestPDF layout pass, so preflight can run instantly and independent of the PDF engine.
/// </summary>
public sealed class AssociatePassdownPreflightService : IAssociatePassdownPreflightService
{
    /// <summary>Estimated printable body lines available on one Letter page beneath the fixed header/hours/attendance/ERT/Daily Fun block, tuned so the associate/dense fixtures in Documents.Tests render as exactly one real QuestPDF page.</summary>
    private const int TotalBodyLineBudget = 46;

    private const int WrapCharsPerLine = 95;
    private const int ProductNoteWrapCharsPerLine = 70;

    public AssociatePassdownPreflightResult Evaluate(AssociatePassdownDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.OperatingDate is null)
        {
            return new AssociatePassdownPreflightResult(
                AssociatePassdownPreflightStatus.MissingOperatingDate,
                ["An operating date is required to generate the Associate Passdown PDF."],
                [], [], [], []);
        }

        var remaining = TotalBodyLineBudget - EstimateFixedLines(model);

        var (productsFit, pageOneCategories, continuationCategories, remainingAfterProducts) = SplitProducts(model.ProductCategories, remaining);

        List<string> pageOneNotes;
        List<string> continuationNotes;
        bool notesFit;

        if (!productsFit)
        {
            // Products alone already exceeded the page — notes (which print after products, spec
            // §11.2) have no room left and move to the continuation page in full.
            pageOneNotes = [];
            continuationNotes = model.OperationalNotes.ToList();
            notesFit = model.OperationalNotes.Count == 0;
        }
        else
        {
            (notesFit, pageOneNotes, continuationNotes) = SplitNotes(model.OperationalNotes, remainingAfterProducts);
        }

        if (productsFit && notesFit)
        {
            return new AssociatePassdownPreflightResult(
                AssociatePassdownPreflightStatus.FitsOnePage,
                [],
                model.ProductCategories,
                model.OperationalNotes,
                [],
                []);
        }

        return new AssociatePassdownPreflightResult(
            AssociatePassdownPreflightStatus.RequiresContinuationPage,
            ["Product and/or operational-notes content exceeds one page. Additional items are printed on a Supporting Details continuation page."],
            pageOneCategories,
            pageOneNotes,
            continuationCategories,
            continuationNotes);
    }

    private static int EstimateFixedLines(AssociatePassdownDocumentModel model)
    {
        var lines = 2; // header block: title line + date line

        lines += 1; // "Park Hours" heading
        lines += 1; // headline
        lines += model.ParkHours.BadgeText is null ? 0 : 1;
        lines += 1; // North Gate / Avenue line
        lines += model.ParkHours.VerificationText is null ? 0 : 1;
        lines += EstimateWrappedLines(model.ParkHours.PrivateBuyoutDetails, WrapCharsPerLine);

        lines += 2; // "Attendance" heading + combined projected/pre-sold line

        lines += model.EarlyRideTime.Enabled
            ? 1 + Math.Max(1, EstimateWrappedLines(model.EarlyRideTime.Details, WrapCharsPerLine))
                + (model.EarlyRideTime.ParticipatingAttractions.Count > 0
                    ? EstimateWrappedLines(string.Join(", ", model.EarlyRideTime.ParticipatingAttractions), WrapCharsPerLine)
                    : 0)
            : 2; // heading + "Not applicable for this date."

        lines += model.DailyFun.HasContent
            ? 1 + Math.Max(1, EstimateWrappedLines(model.DailyFun.Text, WrapCharsPerLine))
            : 0;

        return lines;
    }

    private static (bool Fits, List<PdfProductCategory> PageOne, List<PdfProductCategory> Continuation, int RemainingBudget) SplitProducts(
        IReadOnlyList<PdfProductCategory> categories, int budget)
    {
        var remaining = budget;
        if (categories.Count > 0)
        {
            remaining -= 1; // "Products" section heading
        }

        for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
        {
            var category = categories[categoryIndex];

            if (remaining <= 0)
            {
                return (false, categories.Take(categoryIndex).ToList(), categories.Skip(categoryIndex).ToList(), remaining);
            }

            remaining -= 1; // category heading line

            for (var rowIndex = 0; rowIndex < category.Products.Count; rowIndex++)
            {
                var rowLines = 1 + EstimateWrappedLines(category.Products[rowIndex].Notes, ProductNoteWrapCharsPerLine);
                if (remaining < rowLines)
                {
                    var pageOne = categories.Take(categoryIndex).ToList();
                    var continuation = categories.Skip(categoryIndex + 1).ToList();

                    if (rowIndex > 0)
                    {
                        pageOne.Add(category with { Products = category.Products.Take(rowIndex).ToList() });
                        continuation.Insert(0, category with { Products = category.Products.Skip(rowIndex).ToList() });
                    }
                    else
                    {
                        continuation.Insert(0, category);
                    }

                    return (false, pageOne, continuation, remaining);
                }

                remaining -= rowLines;
            }
        }

        return (true, categories.ToList(), [], remaining);
    }

    private static (bool Fits, List<string> PageOne, List<string> Continuation) SplitNotes(IReadOnlyList<string> notes, int budget)
    {
        var remaining = budget;
        if (notes.Count > 0)
        {
            remaining -= 1; // "Operational Notes" heading
        }

        for (var noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            var noteLines = Math.Max(1, EstimateWrappedLines(notes[noteIndex], WrapCharsPerLine));
            if (remaining < noteLines)
            {
                return (false, notes.Take(noteIndex).ToList(), notes.Skip(noteIndex).ToList());
            }

            remaining -= noteLines;
        }

        return (true, notes.ToList(), []);
    }

    private static int EstimateWrappedLines(string? text, int charsPerLine) =>
        string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Trim().Length / (double)charsPerLine);
}
