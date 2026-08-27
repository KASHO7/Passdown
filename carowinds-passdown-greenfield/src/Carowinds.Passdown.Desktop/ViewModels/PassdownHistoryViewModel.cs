using System.Security.Cryptography;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.History;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 9D's read-only Passdown History screen state. Owns its own selection independently of
/// <see cref="AssociateViewModel.SelectedDate"/> (spec Part O/T) — nothing here ever touches
/// <see cref="AssociateViewModel"/>, and every value shown comes from <see cref="IPassdownHistoryService"/>
/// resolving <see cref="Persistence.FinalizedPassdown.SnapshotJson"/>, never the current Associate draft
/// or catalog tables (spec Part D). There is deliberately no edit/save/delete/finalize/refresh command
/// anywhere on this type (spec Part E).
/// </summary>
public partial class PassdownHistoryViewModel : ViewModelBase
{
    private readonly IPassdownHistoryService _historyService;
    private readonly IFinalizedPassdownPdfRetrievalService _pdfRetrievalService;
    private readonly IPdfSaveFilePicker _pdfSavePicker;
    private readonly IFinalizedPdfLauncher _pdfLauncher;
    private readonly ILogger<PassdownHistoryViewModel>? _logger;

    private int _detailLoadVersion;
    private Task _pendingLoad = Task.CompletedTask;
    private Task _pendingDetailLoad = Task.CompletedTask;
    private Task _pendingSelectByDate = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial IReadOnlyList<PassdownHistorySummaryPresentation> HistoryItems { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial PassdownHistorySummaryPresentation? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnapshot))]
    [NotifyCanExecuteChangedFor(nameof(OpenFinalPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCopyCommand))]
    public partial PassdownHistoryDetail? SelectedDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsLoadingList { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial string? ListErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? DetailErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFinalPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCopyCommand))]
    public partial bool IsOpeningPdf { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFinalPdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCopyCommand))]
    public partial bool IsSavingPdf { get; set; }

    [ObservableProperty]
    public partial string? PdfActionMessage { get; set; }

    public PassdownHistoryViewModel(
        IPassdownHistoryService historyService,
        IFinalizedPassdownPdfRetrievalService pdfRetrievalService,
        ILogger<PassdownHistoryViewModel>? logger = null,
        IPdfSaveFilePicker? pdfSavePicker = null,
        IFinalizedPdfLauncher? pdfLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(pdfRetrievalService);

        _historyService = historyService;
        _pdfRetrievalService = pdfRetrievalService;
        _logger = logger;
        _pdfSavePicker = pdfSavePicker ?? new UnavailablePdfSaveFilePicker();
        _pdfLauncher = pdfLauncher ?? new UnavailableFinalizedPdfLauncher();

        _pendingLoad = LoadHistoryAsync();
    }

    public bool HasHistory => HistoryItems.Count > 0;

    public bool HasSelection => SelectedItem is not null;

    public bool HasSnapshot => SelectedDetail?.Snapshot is not null;

    /// <summary>Spec Part G: the concise empty state, shown only once loading has finished and nothing failed.</summary>
    public bool ShowEmptyState => !IsLoadingList && !HasHistory && string.IsNullOrEmpty(ListErrorMessage);

    /// <summary>The in-flight (or most recently finished) history list load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    /// <summary>The in-flight (or most recently finished) detail load triggered by <see cref="SelectedItem"/> changing.</summary>
    public Task PendingDetailLoad => _pendingDetailLoad;

    /// <summary>The in-flight (or most recently finished) call started by <see cref="SelectByDateAsync"/>, so a test can await it deterministically.</summary>
    public Task PendingSelectByDate => _pendingSelectByDate;

    /// <summary>
    /// Phase 12D Part I/J/K: the clean date-based seam <see cref="MainViewModel"/> uses to preselect a
    /// finalized record when the Operations Calendar explicitly routes here for a specific date —
    /// never constructed/searched from outside this type, and never used for plain top-level History
    /// navigation (which must preserve whatever was already selected). Waits for the list load first so
    /// a match can actually be found even if this is called before <see cref="LoadHistoryAsync"/>
    /// finishes; if no matching row exists, History simply opens with its current selection untouched —
    /// never a crash, and never a fabricated selection.
    /// </summary>
    public Task SelectByDateAsync(DateOnly date) => _pendingSelectByDate = SelectByDateCoreAsync(date);

    private async Task SelectByDateCoreAsync(DateOnly date)
    {
        await PendingLoad.ConfigureAwait(true);
        var match = HistoryItems.FirstOrDefault(item => item.PassdownDate == date);
        if (match is not null)
        {
            SelectedItem = match;
        }
    }

    private async Task LoadHistoryAsync()
    {
        IsLoadingList = true;
        ListErrorMessage = null;
        try
        {
            var summaries = await _historyService.ListAsync().ConfigureAwait(true);
            HistoryItems = summaries.Select(PassdownHistorySummaryPresentation.Build).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load finalized-passdown history.");
            ListErrorMessage = "History could not be loaded right now. Try again later.";
            HistoryItems = [];
        }
        finally
        {
            IsLoadingList = false;
        }
    }

    /// <summary>
    /// Spec Part O: switching the History selection is entirely local to this view model — it never
    /// reads or writes <see cref="AssociateViewModel.SelectedDate"/> and never triggers any load on
    /// that type.
    /// </summary>
    partial void OnSelectedItemChanged(PassdownHistorySummaryPresentation? value) => _pendingDetailLoad = LoadDetailAsync(value);

    private async Task LoadDetailAsync(PassdownHistorySummaryPresentation? item)
    {
        var version = ++_detailLoadVersion;
        PdfActionMessage = null;

        if (item is null)
        {
            SelectedDetail = null;
            DetailErrorMessage = null;
            return;
        }

        IsLoadingDetail = true;
        DetailErrorMessage = null;
        try
        {
            var detail = await _historyService.GetAsync(item.PassdownDate).ConfigureAwait(true);
            if (version != _detailLoadVersion)
            {
                return; // a newer selection was made before this one finished loading.
            }

            SelectedDetail = detail;
            DetailErrorMessage = detail is null
                ? "This finalized passdown could not be found."
                : detail.UnavailableReason;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load finalized-passdown detail for {PassdownDate}.", item.PassdownDate);
            if (version == _detailLoadVersion)
            {
                SelectedDetail = null;
                DetailErrorMessage = "This finalized passdown could not be loaded. Try again.";
            }
        }
        finally
        {
            if (version == _detailLoadVersion)
            {
                IsLoadingDetail = false;
            }
        }
    }

    /// <summary>Spec Part I: opens the exact stored PDF using only <see cref="IFinalizedPassdownPdfRetrievalService"/> — never regenerates it.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFinalPdf))]
    private async Task OpenFinalPdfAsync()
    {
        if (SelectedDetail is not { Summary.HasFrozenPdf: true } detail)
        {
            return;
        }

        IsOpeningPdf = true;
        PdfActionMessage = null;
        try
        {
            var pdf = await _pdfRetrievalService.GetPdfAsync(detail.Summary.PassdownDate).ConfigureAwait(true);
            if (!TryValidate(pdf, out var validationError))
            {
                PdfActionMessage = validationError;
                return;
            }

            var result = await _pdfLauncher.OpenAsync(pdf!.PdfBytes, pdf.SuggestedFileName).ConfigureAwait(true);
            PdfActionMessage = result.Outcome == PdfOpenOutcome.Opened ? null : (result.ErrorMessage ?? "The PDF could not be opened. Try again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Open Final PDF failed unexpectedly for {PassdownDate}.", detail.Summary.PassdownDate);
            PdfActionMessage = "The PDF could not be opened. Try again.";
        }
        finally
        {
            IsOpeningPdf = false;
        }
    }

    private bool CanOpenFinalPdf() => SelectedDetail?.Summary.HasFrozenPdf == true && !IsOpeningPdf && !IsSavingPdf;

    /// <summary>Spec Part J: saves the exact stored PDF via the same native save-file picker the Associate screen's Create PDF action already uses — never regenerates it.</summary>
    [RelayCommand(CanExecute = nameof(CanSavePdfCopy))]
    private async Task SavePdfCopyAsync()
    {
        if (SelectedDetail is not { Summary.HasFrozenPdf: true } detail)
        {
            return;
        }

        IsSavingPdf = true;
        PdfActionMessage = null;
        try
        {
            var pdf = await _pdfRetrievalService.GetPdfAsync(detail.Summary.PassdownDate).ConfigureAwait(true);
            if (!TryValidate(pdf, out var validationError))
            {
                PdfActionMessage = validationError;
                return;
            }

            var saveResult = await _pdfSavePicker.SaveAsync(pdf!.PdfBytes, pdf.SuggestedFileName).ConfigureAwait(true);
            PdfActionMessage = saveResult.Outcome switch
            {
                PdfSaveOutcome.Saved => "PDF saved.",
                PdfSaveOutcome.Cancelled => null,
                _ => saveResult.ErrorMessage ?? "The PDF could not be saved. Try again.",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Save PDF Copy failed unexpectedly for {PassdownDate}.", detail.Summary.PassdownDate);
            PdfActionMessage = "The PDF could not be saved. Try again.";
        }
        finally
        {
            IsSavingPdf = false;
        }
    }

    private bool CanSavePdfCopy() => SelectedDetail?.Summary.HasFrozenPdf == true && !IsOpeningPdf && !IsSavingPdf;

    /// <summary>
    /// Spec Part U: never hands a caller PDF bytes that don't match the stored size/hash metadata — a
    /// support-facing signal that the row's stored metadata is inconsistent, rather than silently
    /// opening/saving something that might not be the exact frozen PDF. Never "repairs" the record.
    /// </summary>
    private bool TryValidate(FinalizedPassdownPdfRetrievalResult? pdf, out string? errorMessage)
    {
        if (pdf is null || pdf.PdfBytes.Length == 0)
        {
            errorMessage = "Exact historical PDF unavailable.";
            return false;
        }

        if (pdf.PdfBytes.LongLength != pdf.PdfSizeBytes)
        {
            _logger?.LogWarning(
                "Finalized PDF size mismatch: expected {ExpectedBytes} bytes, retrieved {ActualBytes}.",
                pdf.PdfSizeBytes, pdf.PdfBytes.LongLength);
            errorMessage = "This PDF's stored data looks inconsistent. Contact support.";
            return false;
        }

        var actualHash = Convert.ToHexStringLower(SHA256.HashData(pdf.PdfBytes));
        if (!string.Equals(actualHash, pdf.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Finalized PDF hash mismatch for a stored record.");
            errorMessage = "This PDF's stored data looks inconsistent. Contact support.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>Default <see cref="IPdfSaveFilePicker"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.AvaloniaPdfSaveFilePicker"/>.</summary>
    private sealed class UnavailablePdfSaveFilePicker : IPdfSaveFilePicker
    {
        public Task<PdfSaveResult> SaveAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(PdfSaveResult.Failed("PDF export is not available."));
    }

    /// <summary>Default <see cref="IFinalizedPdfLauncher"/> when none is supplied — only reachable if the composition root failed to register the real one; every production path registers <see cref="Services.TempFileFinalizedPdfLauncher"/>.</summary>
    private sealed class UnavailableFinalizedPdfLauncher : IFinalizedPdfLauncher
    {
        public Task<PdfOpenResult> OpenAsync(byte[] pdfBytes, string suggestedFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(PdfOpenResult.Failed("Opening PDFs is not available."));
    }
}
