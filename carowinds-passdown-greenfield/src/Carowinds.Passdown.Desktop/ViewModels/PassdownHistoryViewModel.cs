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

    private IReadOnlyList<PassdownHistorySummaryPresentation> _allItems = [];
    private int _detailLoadVersion;
    private Task _pendingLoad = Task.CompletedTask;
    private Task _pendingDetailLoad = Task.CompletedTask;
    private Task _pendingSelectByDate = Task.CompletedTask;

    /// <summary>The finalized records the current search and filters show (all of them when nothing narrows the list), newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilteredEmptyState))]
    [NotifyPropertyChangedFor(nameof(RecordCountText))]
    public partial IReadOnlyList<PassdownHistorySummaryPresentation> HistoryItems { get; set; } = [];

    /// <summary>
    /// Phase 35F: the search box. Matches each record's own metadata only — its date (long, short and numeric
    /// forms), finalized time, origin and PDF availability — so searching never opens a stored snapshot and never
    /// reads anything but the already-loaded list. Every word must match.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>Phase 35F: the record-type segment — all records, or only Scheduled / Recovery / Manual finalizations.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsAllOriginsSelected))]
    [NotifyPropertyChangedFor(nameof(IsScheduledSelected))]
    [NotifyPropertyChangedFor(nameof(IsRecoverySelected))]
    [NotifyPropertyChangedFor(nameof(IsManualSelected))]
    public partial FinalizationOrigin? OriginFilter { get; set; }

    /// <summary>Phase 35F: the Year filter — "All years" or one of the years that have finalized records.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    public partial HistoryYearOption YearFilter { get; set; } = HistoryYearOption.AllYears;

    /// <summary>Phase 35F: "All years" plus each year that has at least one finalized record, newest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<HistoryYearOption> YearOptions { get; set; } = [HistoryYearOption.AllYears];

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
    [NotifyPropertyChangedFor(nameof(ShowFilteredEmptyState))]
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

    /// <summary>Whether any finalized record exists at all (whatever the search and filters show).</summary>
    public bool HasHistory => _allItems.Count > 0;

    /// <summary>Phase 35F: records exist, but none match the current search or filters.</summary>
    public bool ShowFilteredEmptyState => !IsLoadingList && HasHistory && HistoryItems.Count == 0;

    /// <summary>Phase 35F: whether the search, record type or year narrows the list (drives Clear filters).</summary>
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(SearchText) || OriginFilter is not null || YearFilter.Year is not null;

    /// <summary>Phase 35F: "1 record" / "17 records" — how many records the list shows.</summary>
    public string RecordCountText => HistoryItems.Count == 1 ? "1 record" : $"{HistoryItems.Count} records";

    public bool IsAllOriginsSelected => OriginFilter is null;

    public bool IsScheduledSelected => OriginFilter == FinalizationOrigin.Scheduled;

    public bool IsRecoverySelected => OriginFilter == FinalizationOrigin.Recovery;

    public bool IsManualSelected => OriginFilter == FinalizationOrigin.Manual;

    /// <summary>Phase 35F: Manual finalizations are rare; their segment is offered only when such a record exists.</summary>
    public bool HasManualRecords => _allItems.Any(item => item.OriginText == nameof(FinalizationOrigin.Manual));

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
        var match = _allItems.FirstOrDefault(item => item.PassdownDate == date);
        if (match is not null)
        {
            // Phase 35F: a search or filter must never hide the record the Calendar asked for.
            if (!HistoryItems.Contains(match))
            {
                ClearFilters();
            }

            SelectedItem = match;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnOriginFilterChanged(FinalizationOrigin? value) => ApplyFilters();

    partial void OnYearFilterChanged(HistoryYearOption value) => ApplyFilters();

    [RelayCommand]
    private void SetOriginFilter(FinalizationOrigin? origin) => OriginFilter = origin;

    /// <summary>Phase 35F: clears the search, record type and year at once.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        OriginFilter = null;
        YearFilter = HistoryYearOption.AllYears;
    }

    /// <summary>
    /// Phase 35F: narrows the loaded records in memory (newest first is kept). A selected record that the
    /// filters now hide is deselected rather than shown beside a list that no longer contains it. Nothing is
    /// read from storage and no record changes.
    /// </summary>
    private void ApplyFilters()
    {
        IEnumerable<PassdownHistorySummaryPresentation> filtered = _allItems;
        if (OriginFilter is { } origin)
        {
            filtered = filtered.Where(item => item.OriginText == origin.ToString());
        }

        if (YearFilter.Year is { } year)
        {
            filtered = filtered.Where(item => item.PassdownDate.Year == year);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var terms = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(item => terms.All(term => item.SearchKey.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        HistoryItems = filtered.ToList();
        if (SelectedItem is { } selected && !HistoryItems.Contains(selected))
        {
            SelectedItem = null;
        }
    }

    private async Task LoadHistoryAsync()
    {
        IsLoadingList = true;
        ListErrorMessage = null;
        try
        {
            var summaries = await _historyService.ListAsync().ConfigureAwait(true);
            SetAllItems(summaries.Select(PassdownHistorySummaryPresentation.Build).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load finalized-passdown history.");
            ListErrorMessage = "History could not be loaded right now. Try again later.";
            SetAllItems([]);
        }
        finally
        {
            IsLoadingList = false;
        }
    }

    private void SetAllItems(IReadOnlyList<PassdownHistorySummaryPresentation> items)
    {
        _allItems = items;
        YearOptions = [HistoryYearOption.AllYears, .. items.Select(item => item.PassdownDate.Year).Distinct().OrderDescending().Select(HistoryYearOption.For)];
        if (YearFilter.Year is { } year && !items.Any(item => item.PassdownDate.Year == year))
        {
            YearFilter = HistoryYearOption.AllYears;
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasManualRecords));
        ApplyFilters();
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

/// <summary>Phase 35F: one choice in the History Year filter — "All years" (<see cref="Year"/> null) or a single year.</summary>
public sealed record HistoryYearOption(int? Year, string Label)
{
    public static HistoryYearOption AllYears { get; } = new(null, "All years");

    public static HistoryYearOption For(int year) => new(year, year.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public override string ToString() => Label;
}
