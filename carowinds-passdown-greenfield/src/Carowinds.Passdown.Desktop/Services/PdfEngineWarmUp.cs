using System.Diagnostics;
using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>What the one background PDF warm-up did.</summary>
public enum PdfEngineWarmUpOutcome
{
    NotStarted,
    Running,

    /// <summary>A real PDF had already started generating (it pays the start-up cost itself) — nothing was done.</summary>
    AlreadyWarm,

    Completed,

    /// <summary>The app began shutting down first.</summary>
    Cancelled,

    /// <summary>Something failed. Logged at debug level only; the next real PDF simply pays the cold cost.</summary>
    Failed,
}

/// <summary>
/// Phase 34B-2 (audit finding F04): pays the PDF engine's one-time start-up cost (QuestPDF font
/// registration and JIT, ≈ 130–190 ms on the Mac host; PDFium's native library load, ≈ 300 ms once) in the
/// background after the window opens, so the first real preview, Create PDF or Print does not.
/// <list type="bullet">
/// <item>Runs at most once per process (<see cref="TryStart"/>), on one below-normal-priority background
/// thread — never on the UI thread, with no timer or polling.</item>
/// <item>Uses <see cref="AssociatePassdownPdfWarmUpDocument"/> only: no Passdown data is read, no draft is
/// touched (so autosave never runs), nothing is displayed, saved, printed or written anywhere.</item>
/// <item>Skips the whole warm-up once a real PDF has started generating (that render is already paying the
/// cost — typically the live preview's first page at startup), and skips the PDFium step when PDFium is
/// already loaded. Never holds anything a real PDF waits on beyond
/// the work that request would have had to do itself: QuestPDF's font registration is a CLR static
/// constructor (runs once, thread-safe), separate documents generate independently, and the PDFium lock is
/// held only while the library loads and one tiny page renders.</item>
/// <item><see cref="Cancel"/> (on shutdown) skips any step not yet started; the thread is a background
/// thread, so it can never keep the process alive.</item>
/// </list>
/// </summary>
public sealed class PdfEngineWarmUp
{
    /// <summary>Render scale for the PDFium step: a ~30 × 40 px page — enough to load the library and exercise rendering.</summary>
    internal const double WarmUpPixelsPerPoint = 0.05;

    private readonly IAssociatePassdownPdfGenerator _generator;
    private readonly IPdfRasterizer _rasterizer;
    private readonly IAssociatePassdownTemplatePreferenceStore? _templatePreferences;
    private readonly Func<bool> _isGenerationWarm;
    private readonly Func<bool> _isRasterizerWarm;
    private readonly Func<Action, Task> _runInBackground;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellation = new();
    private int _started;
    private volatile PdfEngineWarmUpOutcome _outcome = PdfEngineWarmUpOutcome.NotStarted;

    /// <param name="isGenerationWarm">Whether a real PDF has started generating. Defaults to <see cref="AssociatePassdownPdfGenerator.HasStartedRendering"/>.</param>
    /// <param name="isRasterizerWarm">Defaults to "never warm" (a test seam); the app passes PDFium's loaded flag.</param>
    /// <param name="runInBackground">Test seam; defaults to a dedicated below-normal-priority background thread.</param>
    public PdfEngineWarmUp(
        IAssociatePassdownPdfGenerator generator,
        IPdfRasterizer rasterizer,
        IAssociatePassdownTemplatePreferenceStore? templatePreferences = null,
        Func<bool>? isGenerationWarm = null,
        Func<bool>? isRasterizerWarm = null,
        Func<Action, Task>? runInBackground = null,
        ILogger<PdfEngineWarmUp>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(rasterizer);
        _generator = generator;
        _rasterizer = rasterizer;
        _templatePreferences = templatePreferences;
        _isGenerationWarm = isGenerationWarm ?? (() => AssociatePassdownPdfGenerator.HasStartedRendering);
        _isRasterizerWarm = isRasterizerWarm ?? (() => false);
        _runInBackground = runInBackground ?? RunOnLowPriorityThread;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public PdfEngineWarmUpOutcome Outcome => _outcome;

    /// <summary>The background warm-up, for tests to await. Completed when it never started.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Starts the warm-up unless it already started (or was cancelled). Returns immediately.</summary>
    public bool TryStart()
    {
        if (_cancellation.IsCancellationRequested || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return false;
        }

        _outcome = PdfEngineWarmUpOutcome.Running;
        Completion = _runInBackground(() => _outcome = Run(_cancellation.Token));
        return true;
    }

    /// <summary>Skips whatever has not started yet. Safe to call any number of times, from any thread.</summary>
    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private PdfEngineWarmUpOutcome Run(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // A real PDF that has already started (typically the live preview at startup) pays the QuestPDF cost
            // itself and is followed by its own PDFium render, so warming in parallel would only duplicate work.
            var generateNeeded = !_isGenerationWarm();
            var renderNeeded = !_isRasterizerWarm();
            if (!generateNeeded)
            {
                return PdfEngineWarmUpOutcome.AlreadyWarm;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var template = LoadTemplate(cancellationToken);

            // The PDFium step needs bytes to open; once QuestPDF is warm this costs a few milliseconds.
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = _generator.Generate(AssociatePassdownPdfWarmUpDocument.Create(), template, cancellationToken).PdfBytes;
            var generatedMs = stopwatch.Elapsed.TotalMilliseconds;

            if (renderNeeded && bytes is { Length: > 0 } && !_isRasterizerWarm())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var document = _rasterizer.Open(bytes);
                if (document.PageCount > 0)
                {
                    _ = document.RenderPage(0, WarmUpPixelsPerPoint, PdfRenderPurpose.Display, cancellationToken);
                }
            }

            _logger.LogDebug(
                "PDF engine warm-up finished in {ElapsedMs:F0} ms (generation {GeneratedMs:F0} ms; renderer was {RendererState}).",
                stopwatch.Elapsed.TotalMilliseconds, generatedMs, renderNeeded ? "cold" : "warm");
            return PdfEngineWarmUpOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PdfEngineWarmUpOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            // Never user-visible: the first real PDF just pays the cold cost, and reports its own errors.
            _logger.LogDebug("PDF engine warm-up skipped ({ExceptionType}).", ex.GetType().Name);
            return PdfEngineWarmUpOutcome.Failed;
        }
    }

    private AssociatePassdownTemplate LoadTemplate(CancellationToken cancellationToken)
    {
        if (_templatePreferences is null)
        {
            return AssociatePassdownTemplate.Classic;
        }

        try
        {
            // The store never throws for a missing or malformed file; it is one tiny local read.
            return _templatePreferences.LoadAsync(cancellationToken).GetAwaiter().GetResult().Template;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AssociatePassdownTemplate.Classic;
        }
    }

    private static Task RunOnLowPriorityThread(Action work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            finally
            {
                completion.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "PDF engine warm-up",
        };
        thread.Start();
        return completion.Task;
    }
}
