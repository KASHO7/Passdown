using Carowinds.Passdown.Application.PdfOutput;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Infrastructure.PdfOutput;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 34B-2: the background PDF engine warm-up runs at most once, uses only the synthetic warm-up document,
/// skips work a real PDF already did, never surfaces a failure, and stops on shutdown. Deterministic: the
/// background runner is inline unless a test says otherwise.
/// </summary>
public class Phase34B2PdfEngineWarmUpTests
{
    private sealed class RecordingGenerator(Func<CancellationToken, byte[]?>? produce = null) : IAssociatePassdownPdfGenerator
    {
        public List<(AssociatePassdownDocumentModel Model, AssociatePassdownTemplate Template)> Calls { get; } = [];

        public int? ThreadId { get; private set; }

        public AssociatePassdownPdfResult Generate(AssociatePassdownDocumentModel model, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic, CancellationToken cancellationToken = default)
        {
            Calls.Add((model, template));
            ThreadId = Environment.CurrentManagedThreadId;
            var bytes = produce is null ? [1, 2, 3] : produce(cancellationToken);
            return new AssociatePassdownPdfResult(AssociatePassdownPdfStatus.Generated, bytes, new AssociatePassdownPreflightService().Evaluate(model), PageCount: 1);
        }
    }

    private sealed class RecordingRasterizer : IPdfRasterizer
    {
        public List<byte[]> Opened { get; } = [];

        public List<double> RenderScales { get; } = [];

        public bool DocumentDisposed { get; private set; }

        public IPdfRasterDocument Open(byte[] pdfBytes)
        {
            Opened.Add(pdfBytes);
            return new Document(this);
        }

        private sealed class Document(RecordingRasterizer owner) : IPdfRasterDocument
        {
            public int PageCount => 1;

            public PdfPageSize GetPageSize(int pageIndex) => new(612, 792);

            public PdfRasterImage RenderPage(int pageIndex, double pixelsPerPoint, PdfRenderPurpose purpose = PdfRenderPurpose.Display, CancellationToken cancellationToken = default)
            {
                owner.RenderScales.Add(pixelsPerPoint);
                return new PdfRasterImage(1, 1, 4, new byte[4]);
            }

            public void Dispose() => owner.DocumentDisposed = true;
        }
    }

    private sealed class TemplateStore(AssociatePassdownTemplate template) : IAssociatePassdownTemplatePreferenceStore
    {
        public Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AssociatePassdownTemplatePreference(template));

        public Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The warm-up never saves preferences.");
    }

    private static Task Inline(Action work)
    {
        work();
        return Task.CompletedTask;
    }

    private static PdfEngineWarmUp Create(
        RecordingGenerator generator,
        RecordingRasterizer rasterizer,
        bool generationWarm = false,
        bool rasterizerWarm = false,
        Func<Action, Task>? runner = null,
        AssociatePassdownTemplate template = AssociatePassdownTemplate.Modern) =>
        new(generator, rasterizer, new TemplateStore(template), () => generationWarm, () => rasterizerWarm, runner ?? Inline);

    [Fact]
    public async Task ColdEngine_GeneratesTheSyntheticDocumentOnce_InTheSavedTemplate_AndRendersOneTinyPage()
    {
        var generator = new RecordingGenerator();
        var rasterizer = new RecordingRasterizer();
        var warmUp = Create(generator, rasterizer);

        Assert.True(warmUp.TryStart());
        await warmUp.Completion;

        var call = Assert.Single(generator.Calls);
        Assert.Equal(AssociatePassdownTemplate.Modern, call.Template);
        Assert.Equal("Warm-up", call.Model.OperatingDateText);
        Assert.Equal(AssociatePassdownPdfWarmUpDocument.Create().OperatingDate, call.Model.OperatingDate);
        Assert.Single(rasterizer.Opened);
        Assert.Equal(PdfEngineWarmUp.WarmUpPixelsPerPoint, Assert.Single(rasterizer.RenderScales));
        Assert.True(rasterizer.DocumentDisposed);
        Assert.Equal(PdfEngineWarmUpOutcome.Completed, warmUp.Outcome);
    }

    [Fact]
    public async Task TryStart_RunsAtMostOnce()
    {
        var generator = new RecordingGenerator();
        var warmUp = Create(generator, new RecordingRasterizer());

        Assert.True(warmUp.TryStart());
        Assert.False(warmUp.TryStart());
        Assert.False(warmUp.TryStart());
        await warmUp.Completion;

        Assert.Single(generator.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // the real PDF is still generating: it will render its own page, so PDFium is left to it too
    public async Task WhenARealPdfHasAlreadyStarted_NothingRuns(bool rasterizerWarm)
    {
        var generator = new RecordingGenerator();
        var rasterizer = new RecordingRasterizer();
        var warmUp = Create(generator, rasterizer, generationWarm: true, rasterizerWarm: rasterizerWarm);

        warmUp.TryStart();
        await warmUp.Completion;

        Assert.Empty(generator.Calls);
        Assert.Empty(rasterizer.Opened);
        Assert.Equal(PdfEngineWarmUpOutcome.AlreadyWarm, warmUp.Outcome);
    }

    [Fact]
    public async Task WhenOnlyTheRendererIsWarm_ItIsNotOpenedAgain()
    {
        var generator = new RecordingGenerator();
        var rasterizer = new RecordingRasterizer();
        var warmUp = Create(generator, rasterizer, rasterizerWarm: true);

        warmUp.TryStart();
        await warmUp.Completion;

        Assert.Single(generator.Calls);
        Assert.Empty(rasterizer.Opened);
    }

    [Fact]
    public async Task CancelBeforeStart_PreventsTheWarmUp()
    {
        var generator = new RecordingGenerator();
        var warmUp = Create(generator, new RecordingRasterizer());

        warmUp.Cancel();
        Assert.False(warmUp.TryStart());
        await warmUp.Completion;

        Assert.Empty(generator.Calls);
        Assert.Equal(PdfEngineWarmUpOutcome.NotStarted, warmUp.Outcome);
    }

    [Fact]
    public async Task CancelDuringGeneration_SkipsRendering_AndReportsCancelled()
    {
        PdfEngineWarmUp? warmUp = null;
        var generator = new RecordingGenerator(token =>
        {
            warmUp!.Cancel(); // shutdown arrives while generating
            token.ThrowIfCancellationRequested();
            return [1];
        });
        var rasterizer = new RecordingRasterizer();
        warmUp = Create(generator, rasterizer);

        warmUp.TryStart();
        await warmUp.Completion;

        Assert.Empty(rasterizer.Opened);
        Assert.Equal(PdfEngineWarmUpOutcome.Cancelled, warmUp.Outcome);
    }

    [Fact]
    public async Task AFailure_IsSwallowed_AndNeverRetried()
    {
        var generator = new RecordingGenerator(_ => throw new InvalidOperationException("font missing"));
        var warmUp = Create(generator, new RecordingRasterizer());

        Assert.True(warmUp.TryStart());
        await warmUp.Completion; // no exception escapes

        Assert.Equal(PdfEngineWarmUpOutcome.Failed, warmUp.Outcome);
        Assert.False(warmUp.TryStart());
        Assert.Single(generator.Calls);
    }

    [Fact]
    public async Task TheDefaultRunner_UsesOneBackgroundThread_NotTheCaller()
    {
        var generator = new RecordingGenerator();
        var warmUp = new PdfEngineWarmUp(generator, new RecordingRasterizer(), isGenerationWarm: () => false, isRasterizerWarm: () => true);
        var callerThread = Environment.CurrentManagedThreadId;

        Assert.True(warmUp.TryStart());
        await warmUp.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(generator.ThreadId);
        Assert.NotEqual(callerThread, generator.ThreadId);
        Assert.Equal(AssociatePassdownTemplate.Classic, Assert.Single(generator.Calls).Template); // no store: Classic
    }

    [Fact]
    public async Task RealEngine_WarmsQuestPdfAndPdfium_WithoutTouchingAnyFile()
    {
        var store = new TemplateStore(AssociatePassdownTemplate.Compact);
        var warmUp = new PdfEngineWarmUp(
            new AssociatePassdownPdfGenerator(), new PdfiumPdfRasterizer(), store,
            isGenerationWarm: () => false, isRasterizerWarm: () => false, runInBackground: Inline); // other tests may already have rendered in this process

        warmUp.TryStart();
        await warmUp.Completion;

        Assert.Equal(PdfEngineWarmUpOutcome.Completed, warmUp.Outcome);
        Assert.True(AssociatePassdownPdfGenerator.HasStartedRendering);
        Assert.True(PdfiumPdfRasterizer.IsLibraryLoaded);
    }

    [Fact]
    public void TheSyntheticDocument_GeneratesInEveryTemplate_AndCarriesNoOperationalData()
    {
        var generator = new AssociatePassdownPdfGenerator();
        var model = AssociatePassdownPdfWarmUpDocument.Create();

        Assert.All(model.OperationalNotes, note => Assert.Equal("Warm-up", note));
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            var result = generator.Generate(model, template);
            Assert.Equal(AssociatePassdownPdfStatus.Generated, result.Status);
            Assert.NotEmpty(result.PdfBytes!);
        }
    }

    [Fact]
    public void Startup_StartsTheWarmUpWhenIdleAfterTheWindowOpens_AndCancelsItOnShutdown()
    {
        var app = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/App.axaml.cs"));

        Assert.Contains("mainWindow.Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => pdfWarmUp.TryStart(), Avalonia.Threading.DispatcherPriority.ApplicationIdle);", app, StringComparison.Ordinal);
        Assert.Contains("desktop.ShutdownRequested += (_, _) => pdfWarmUp.Cancel();", app, StringComparison.Ordinal);
        Assert.Contains("isRasterizerWarm: () => PdfiumPdfRasterizer.IsLibraryLoaded", app, StringComparison.Ordinal);
    }
}
