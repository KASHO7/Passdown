using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>Which lifecycle bucket the note list is currently filtered to (Phase 11B Part G). Default is <see cref="Active"/> — the operationally useful default — with the other buckets, including <see cref="All"/>, one click away.</summary>
public enum OperationalNoteLifecycleFilter
{
    All,
    Active,
    Upcoming,
    Expired,
    Resolved,
}

/// <summary>Whether the detail panel is showing read-only detail, a new-note form, or an edit form (Phase 11B Part K/W).</summary>
public enum OperationalNoteEditorMode
{
    None,
    Create,
    Edit,
}

/// <summary>
/// Phase 11B's Operational Notes Manager screen state. Depends only on <see cref="IOperationalNotesService"/>
/// (Application-layer) and <see cref="IClock"/> (spec Part C — used solely to derive "today" as the
/// reference date for lifecycle/date-range presentation, exactly like <see cref="AssociateViewModel"/>'s
/// own <c>EasternTimeZone.ConvertFromUtc(clock.UtcNow)</c> pattern; never a second clock abstraction).
/// It never touches SQLite directly, never recomputes relevance/lifecycle/ordering/validation rules
/// itself (spec Part AK — every one of those is reused from <see cref="OperationalNoteLifecycle"/>,
/// <see cref="OperationalNoteOrdering"/>, and the service's own validation), and has no dependency
/// capable of reaching <see cref="Associate.AssociateDraftPayload"/>, a finalized passdown, or the
/// Dashboard — persistent notes remain completely independent of all three (spec Parts Y/AA/AB).
///
/// Unsaved-changes protection (spec Part W/X) is deliberately the simplest option that still prevents
/// silently discarding work: while <see cref="EditorMode"/> is <see cref="OperationalNoteEditorMode.Create"/>
/// or <see cref="OperationalNoteEditorMode.Edit"/> (<see cref="IsEditing"/>), every command that would
/// change or leave the current note selection — Reload, New Note, Edit, Resolve, and (via
/// <see cref="MainViewModel"/> observing <see cref="IsEditing"/>) the top-level section buttons — is
/// disabled until the user explicitly saves or cancels. There is no global dirty-state framework and no
/// modal dialog; a disabled button is the entire mechanism.
/// </summary>
public partial class OperationalNotesViewModel : ViewModelBase
{
    private readonly IOperationalNotesService _service;
    private readonly IClock _clock;
    private readonly ILogger<OperationalNotesViewModel>? _logger;

    private IReadOnlyList<OperationalNote> _allNotes = [];
    private Guid? _editingNoteId;
    private Task _pendingLoad = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowFilteredEmptyState))]
    public partial IReadOnlyList<OperationalNoteRowPresentation> Notes { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowDetailPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowReadOnlyDetail))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestResolveCommand))]
    public partial OperationalNoteRowPresentation? SelectedNote { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestResolveCommand))]
    public partial OperationalNoteDetailPresentation? SelectedDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsActiveFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsUpcomingFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsExpiredFilterSelected))]
    [NotifyPropertyChangedFor(nameof(IsResolvedFilterSelected))]
    [NotifyPropertyChangedFor(nameof(FilteredEmptyStateMessage))]
    public partial OperationalNoteLifecycleFilter LifecycleFilter { get; set; } = OperationalNoteLifecycleFilter.Active;

    /// <summary>Category filter (Phase 11E Part A) — composes with <see cref="LifecycleFilter"/> and <see cref="PriorityFilter"/>; <see cref="OperationalNoteCategoryFilterOption.AllOption"/> means no category restriction.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSecondaryFilters))]
    [NotifyPropertyChangedFor(nameof(FilteredEmptyStateMessage))]
    public partial OperationalNoteCategoryFilterOption CategoryFilter { get; set; } = OperationalNoteCategoryFilterOption.AllOption;

    /// <summary>Priority filter (Phase 11E Part B) — composes with <see cref="LifecycleFilter"/> and <see cref="CategoryFilter"/>; deliberately just Normal/Important/All, no new severity terminology.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSecondaryFilters))]
    [NotifyPropertyChangedFor(nameof(FilteredEmptyStateMessage))]
    public partial OperationalNotePriorityFilterOption PriorityFilter { get; set; } = OperationalNotePriorityFilterOption.AllOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowFilteredEmptyState))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowFilteredEmptyState))]
    public partial string? ListErrorMessage { get; set; }

    /// <summary>Feedback for an action that failed after the list itself loaded fine (e.g. Resolve) — kept separate from <see cref="ListErrorMessage"/> so a resolve failure never suppresses the list's own empty state.</summary>
    [ObservableProperty]
    public partial string? ActionMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    [NotifyPropertyChangedFor(nameof(ShowDetailPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowReadOnlyDetail))]
    [NotifyPropertyChangedFor(nameof(ShowEditor))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewNoteCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestResolveCommand))]
    public partial OperationalNoteEditorMode EditorMode { get; set; } = OperationalNoteEditorMode.None;

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorBody { get; set; } = string.Empty;

    [ObservableProperty]
    public partial OperationalNoteCategoryOption EditorCategory { get; set; } = OperationalNoteCategoryOption.For(OperationalNoteCategory.General);

    [ObservableProperty]
    public partial OperationalNotePriorityOption EditorPriority { get; set; } = OperationalNotePriorityOption.For(OperationalNotePriority.Normal);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorEffectiveFromForPicker))]
    public partial DateOnly EditorEffectiveFrom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorEffectiveThroughForPicker))]
    public partial DateOnly? EditorEffectiveThrough { get; set; }

    /// <summary>The "Until resolved" affordance (spec Part J) — checked clears/disables <see cref="EditorEffectiveThrough"/> rather than requiring the supervisor to pick a far-future date.</summary>
    [ObservableProperty]
    public partial bool EditorUntilResolved { get; set; } = true;

    [ObservableProperty]
    public partial string? EditorValidationMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestResolveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmResolveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelResolveCommand))]
    public partial bool IsConfirmingResolve { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestResolveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmResolveCommand))]
    public partial bool IsResolving { get; set; }

    public OperationalNotesViewModel(IOperationalNotesService service, IClock clock, ILogger<OperationalNotesViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(clock);

        _service = service;
        _clock = clock;
        _logger = logger;

        _pendingLoad = LoadAsync();
    }

    /// <summary>"Today" in the park's own wall-clock time (mirrors <see cref="AssociateViewModel"/>'s convention) — read fresh on every load/filter pass rather than cached, so a Reload after midnight re-derives Upcoming/Active/Expired against the new day.</summary>
    private DateOnly ReferenceDate => DateOnly.FromDateTime(EasternTimeZone.ConvertFromUtc(_clock.UtcNow).DateTime);

    public bool HasSelection => SelectedNote is not null;

    public bool IsEditing => EditorMode != OperationalNoteEditorMode.None;

    public bool IsCreating => EditorMode == OperationalNoteEditorMode.Create;

    /// <summary>Detail column shows a plain placeholder: not editing, and nothing selected.</summary>
    public bool ShowDetailPlaceholder => !IsEditing && !HasSelection;

    /// <summary>Detail column shows read-only detail (spec Part K's default): not editing, and a note is selected.</summary>
    public bool ShowReadOnlyDetail => !IsEditing && HasSelection;

    /// <summary>Detail column shows the create/edit form.</summary>
    public bool ShowEditor => IsEditing;

    public bool IsAllFilterSelected => LifecycleFilter == OperationalNoteLifecycleFilter.All;

    public bool IsActiveFilterSelected => LifecycleFilter == OperationalNoteLifecycleFilter.Active;

    public bool IsUpcomingFilterSelected => LifecycleFilter == OperationalNoteLifecycleFilter.Upcoming;

    public bool IsExpiredFilterSelected => LifecycleFilter == OperationalNoteLifecycleFilter.Expired;

    public bool IsResolvedFilterSelected => LifecycleFilter == OperationalNoteLifecycleFilter.Resolved;

    /// <summary>Spec Part U: no notes exist at all, regardless of filter.</summary>
    public bool ShowEmptyState => !IsLoading && _allNotes.Count == 0 && string.IsNullOrEmpty(ListErrorMessage);

    /// <summary>Spec Part U: notes exist, but none match the current filter.</summary>
    public bool ShowFilteredEmptyState => !IsLoading && _allNotes.Count > 0 && Notes.Count == 0 && string.IsNullOrEmpty(ListErrorMessage);

    /// <summary>Spec Part D: distinguishes "no notes exist" from "no notes match the current filters", and (when category/priority narrow the view further) says so rather than only naming the lifecycle bucket.</summary>
    public string FilteredEmptyStateMessage
    {
        get
        {
            var lifecycleMessage = LifecycleFilter switch
            {
                OperationalNoteLifecycleFilter.Active => "No active operational notes.",
                OperationalNoteLifecycleFilter.Upcoming => "No upcoming operational notes.",
                OperationalNoteLifecycleFilter.Expired => "No expired operational notes.",
                OperationalNoteLifecycleFilter.Resolved => "No resolved operational notes.",
                _ => "No operational notes match this filter.",
            };

            return HasActiveSecondaryFilters ? $"{lifecycleMessage} Try clearing filters." : lifecycleMessage;
        }
    }

    /// <summary>The fixed category filter choices (spec Part A) — "All" plus the same closed set every other Operational Notes surface labels via <see cref="OperationalNoteCategoryOption"/>.</summary>
    public IReadOnlyList<OperationalNoteCategoryFilterOption> CategoryFilterOptions => OperationalNoteCategoryFilterOption.All;

    /// <summary>The fixed priority filter choices (spec Part B).</summary>
    public IReadOnlyList<OperationalNotePriorityFilterOption> PriorityFilterOptions => OperationalNotePriorityFilterOption.All;

    /// <summary>Spec Part D: whether Category and/or Priority currently narrow the view — drives the "Clear filters" affordance. <see cref="LifecycleFilter"/> is a primary navigation tab, not counted here.</summary>
    public bool HasActiveSecondaryFilters => CategoryFilter.Value is not null || PriorityFilter.Value is not null;

    /// <summary>The fixed category choices for the editor's ComboBox (spec Part J/S) — the same closed set <see cref="OperationalNoteCategoryOption.For"/> uses to label every row/detail, never a second list.</summary>
    public IReadOnlyList<OperationalNoteCategoryOption> EditorCategoryOptions => OperationalNoteCategoryOption.All;

    /// <summary>The fixed priority choices for the editor's ComboBox (spec Part J).</summary>
    public IReadOnlyList<OperationalNotePriorityOption> EditorPriorityOptions => OperationalNotePriorityOption.All;

    public DateTime? EditorEffectiveFromForPicker
    {
        get => EditorEffectiveFrom.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } picked)
            {
                EditorEffectiveFrom = DateOnly.FromDateTime(picked.Date);
            }
        }
    }

    public DateTime? EditorEffectiveThroughForPicker
    {
        get => EditorEffectiveThrough?.ToDateTime(TimeOnly.MinValue);
        set => EditorEffectiveThrough = value is { } picked ? DateOnly.FromDateTime(picked.Date) : null;
    }

    /// <summary>The in-flight (or most recently finished) load, so a test can await it deterministically instead of racing the constructor's fire-and-forget load.</summary>
    public Task PendingLoad => _pendingLoad;

    partial void OnEditorUntilResolvedChanged(bool value)
    {
        if (value)
        {
            EditorEffectiveThrough = null;
        }
    }

    partial void OnLifecycleFilterChanged(OperationalNoteLifecycleFilter value) => ApplyFilter();

    partial void OnCategoryFilterChanged(OperationalNoteCategoryFilterOption value) => ApplyFilter();

    partial void OnPriorityFilterChanged(OperationalNotePriorityFilterOption value) => ApplyFilter();

    /// <summary>Spec Part D: resets Category/Priority back to "All" without disturbing the lifecycle tab, which is primary navigation rather than a secondary narrowing filter.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        CategoryFilter = OperationalNoteCategoryFilterOption.AllOption;
        PriorityFilter = OperationalNotePriorityFilterOption.AllOption;
    }

    /// <summary>Cancels any pending resolve confirmation whenever the selection itself changes, and rebuilds <see cref="SelectedDetail"/> from the cached full note (never re-fetched — spec Part V's local-only guarantee applies to every read here, not only Reload).</summary>
    partial void OnSelectedNoteChanged(OperationalNoteRowPresentation? value)
    {
        IsConfirmingResolve = false;
        RefreshSelectedDetail();
    }

    [RelayCommand]
    private void SetFilter(OperationalNoteLifecycleFilter filter) => LifecycleFilter = filter;

    /// <summary>Spec Part V: rereads local persistent notes only — no network, no coupling to Check Online, and (per <see cref="CanReload"/>) disabled while an edit is in progress so it can never discard unsaved editor fields.</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync() => await LoadAsync().ConfigureAwait(true);

    private bool CanReload() => !IsEditing && !IsLoading;

    private async Task LoadAsync()
    {
        IsLoading = true;
        ListErrorMessage = null;
        try
        {
            _allNotes = await _service.ListAsync().ConfigureAwait(true);
            ApplyFilter();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to load operational notes.");
            _allNotes = [];
            Notes = [];
            SelectedNote = null;
            ListErrorMessage = "Operational notes could not be loaded right now. Try again later.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reapplies <see cref="LifecycleFilter"/> over the cached <see cref="_allNotes"/> using the shared <see cref="OperationalNoteLifecycle"/>/<see cref="OperationalNoteOrdering"/> rules (spec Part F/AK) — never a competing UI sort. Preserves the current selection by Id when it still matches the filter; otherwise clears it, exactly like a successful Resolve is expected to drop a note out of the Active view (spec Part N).</summary>
    private void ApplyFilter()
    {
        var referenceDate = ReferenceDate;
        var previousSelectedId = SelectedNote?.Id;

        IEnumerable<OperationalNote> filtered = LifecycleFilter switch
        {
            OperationalNoteLifecycleFilter.All => _allNotes,
            OperationalNoteLifecycleFilter.Active => _allNotes.Where(n => OperationalNoteLifecycle.Derive(n, referenceDate) == OperationalNoteLifecycleState.Active),
            OperationalNoteLifecycleFilter.Upcoming => _allNotes.Where(n => OperationalNoteLifecycle.Derive(n, referenceDate) == OperationalNoteLifecycleState.Upcoming),
            OperationalNoteLifecycleFilter.Expired => _allNotes.Where(n => OperationalNoteLifecycle.Derive(n, referenceDate) == OperationalNoteLifecycleState.Expired),
            OperationalNoteLifecycleFilter.Resolved => _allNotes.Where(n => OperationalNoteLifecycle.Derive(n, referenceDate) == OperationalNoteLifecycleState.Resolved),
            _ => _allNotes,
        };

        if (CategoryFilter.Value is { } category)
        {
            filtered = filtered.Where(n => n.Category == category);
        }

        if (PriorityFilter.Value is { } priority)
        {
            filtered = filtered.Where(n => n.Priority == priority);
        }

        Notes = filtered.Select(n => OperationalNoteRowPresentation.Build(n, referenceDate)).ToList();
        SelectedNote = previousSelectedId is { } id ? Notes.FirstOrDefault(row => row.Id == id) : null;
        RefreshSelectedDetail();
    }

    private void RefreshSelectedDetail()
    {
        var id = SelectedNote?.Id;
        var note = id is { } noteId ? _allNotes.FirstOrDefault(n => n.Id == noteId) : null;
        SelectedDetail = note is not null ? OperationalNoteDetailPresentation.Build(note, ReferenceDate) : null;
    }

    [RelayCommand(CanExecute = nameof(CanStartNewNote))]
    private void NewNote()
    {
        _editingNoteId = null;
        EditorTitle = string.Empty;
        EditorBody = string.Empty;
        EditorCategory = OperationalNoteCategoryOption.For(OperationalNoteCategory.General);
        EditorPriority = OperationalNotePriorityOption.For(OperationalNotePriority.Normal);
        EditorEffectiveFrom = ReferenceDate;
        EditorEffectiveThrough = null;
        EditorUntilResolved = true;
        EditorValidationMessage = null;
        EditorMode = OperationalNoteEditorMode.Create;
    }

    private bool CanStartNewNote() => !IsEditing;

    /// <summary>Spec Part O: a resolved note has no Edit control — <see cref="CanEdit"/> already prevents this, but Expired/Upcoming notes (spec Parts P/Q) remain editable.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        var id = SelectedNote?.Id;
        var note = id is { } noteId ? _allNotes.FirstOrDefault(n => n.Id == noteId) : null;
        if (note is null)
        {
            return;
        }

        _editingNoteId = note.Id;
        EditorTitle = note.Title;
        EditorBody = note.Body ?? string.Empty;
        EditorCategory = OperationalNoteCategoryOption.For(note.Category);
        EditorPriority = OperationalNotePriorityOption.For(note.Priority);
        EditorEffectiveFrom = note.EffectiveFrom;
        EditorUntilResolved = note.EffectiveThrough is null;
        EditorEffectiveThrough = note.EffectiveThrough;
        EditorValidationMessage = null;
        EditorMode = OperationalNoteEditorMode.Edit;
    }

    private bool CanEdit() => !IsEditing && SelectedDetail is { IsResolved: false };

    [RelayCommand(CanExecute = nameof(CanCancelEdit))]
    private void Cancel()
    {
        EditorMode = OperationalNoteEditorMode.None;
        EditorValidationMessage = null;
    }

    private bool CanCancelEdit() => IsEditing;

    /// <summary>
    /// Spec Part L/M: validates the two conditions the spec calls out by example (blank title;
    /// Effective Through before Effective From) locally for immediate feedback, then calls the
    /// Application-layer service — the sole authority for every other rule (title/body length, etc.).
    /// Any <see cref="ArgumentException"/> the service still throws is mapped to one generic message,
    /// never surfaced as a stack trace or exception type (spec Part M).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var title = EditorTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            EditorValidationMessage = "Enter a title.";
            return;
        }

        var effectiveThrough = EditorUntilResolved ? null : EditorEffectiveThrough;
        if (effectiveThrough is { } through && through < EditorEffectiveFrom)
        {
            EditorValidationMessage = "Effective Through cannot be before Effective From.";
            return;
        }

        IsSaving = true;
        EditorValidationMessage = null;
        try
        {
            var draft = new OperationalNoteDraft(
                title,
                string.IsNullOrWhiteSpace(EditorBody) ? null : EditorBody,
                EditorCategory.Value,
                EditorPriority.Value,
                EditorEffectiveFrom,
                effectiveThrough);

            var saved = EditorMode == OperationalNoteEditorMode.Create
                ? await _service.CreateAsync(draft).ConfigureAwait(true)
                : await _service.UpdateAsync(_editingNoteId!.Value, draft).ConfigureAwait(true);

            _logger?.LogInformation("Operational note saved: {NoteId}", saved.Id);

            EditorMode = OperationalNoteEditorMode.None;
            await LoadAsync().ConfigureAwait(true);
            SelectedNote = Notes.FirstOrDefault(row => row.Id == saved.Id);
            RefreshSelectedDetail();
        }
        catch (ArgumentException)
        {
            EditorValidationMessage = "This note could not be saved. Check the fields and try again.";
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Operational note update failed: note not found.");
            EditorValidationMessage = "This note is no longer available. Reload and try again.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Operational note save failed unexpectedly.");
            EditorValidationMessage = "This note could not be saved. Try again.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSave() => IsEditing && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanRequestResolve))]
    private void RequestResolve() => IsConfirmingResolve = true;

    private bool CanRequestResolve() => !IsEditing && !IsConfirmingResolve && !IsResolving && SelectedDetail is { IsResolved: false };

    [RelayCommand(CanExecute = nameof(CanCancelResolve))]
    private void CancelResolve() => IsConfirmingResolve = false;

    private bool CanCancelResolve() => IsConfirmingResolve;

    /// <summary>Spec Part N: calls <see cref="IOperationalNotesService.ResolveAsync"/> only — never deletes the row, and reloads afterward so a resolved note that no longer matches the current filter (e.g. Active) drops out of view.</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResolve))]
    private async Task ConfirmResolveAsync()
    {
        if (SelectedNote is not { } selected)
        {
            IsConfirmingResolve = false;
            return;
        }

        IsResolving = true;
        ActionMessage = null;
        try
        {
            var resolved = await _service.ResolveAsync(selected.Id).ConfigureAwait(true);
            _logger?.LogInformation("Operational note resolved: {NoteId}", resolved.Id);
            IsConfirmingResolve = false;
            await LoadAsync().ConfigureAwait(true);
            SelectedNote = Notes.FirstOrDefault(row => row.Id == resolved.Id);
            RefreshSelectedDetail();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Operational note resolve failed unexpectedly.");
            ActionMessage = "This note could not be resolved right now. Try again.";
            IsConfirmingResolve = false;
        }
        finally
        {
            IsResolving = false;
        }
    }

    private bool CanConfirmResolve() => IsConfirmingResolve && !IsResolving;
}
