using System.Collections.ObjectModel;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Documents.Teams;
using Carowinds.Passdown.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 29: the Morning Message composer — an overlay opened from Associate → Copy ▾ that assembles the
/// whole Teams morning message from the open date's passdown, lets the user order/include sections and type
/// message-only content (greeting, DAP / placement, reminders, price note, break plan, OD Notes), previews it,
/// and copies it formatted or as plain text.
/// <para>
/// Message-only by design: nothing typed here changes the Associate draft, PDF, History or any official
/// value; passdown values (hours, attendance, prices, Daily Feature) are read-only here and edited on the
/// Associate screen. Never checks online, saves the draft, or changes the selected date. Preferences and the
/// date's message text are kept in local files on this computer.
/// </para>
/// <para>
/// Phase 30: when Sync Morning Messages is on, those files are also exchanged with the user's other computers.
/// While open, the composer holds its date and the preferences in <see cref="MorningMessageEditorSession"/>, so
/// sync never replaces what is being typed; changes that arrive meanwhile are announced in
/// <see cref="SyncNotice"/> and applied after the composer closes.
/// </para>
/// </summary>
public partial class MorningMessageViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;
    private readonly IMorningMessagePreferenceStore _preferenceStore;
    private readonly IMorningMessageContentStore _contentStore;
    private readonly IImageFilePicker? _imagePicker;
    private readonly Func<AssociateWorkspace?> _workspaceProvider;
    private readonly Func<DateOnly> _easternToday;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger? _logger;
    private readonly Func<CancellationToken, Task> _saveDelay;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly MorningMessageEditorSession _session;

    private MorningMessagePreferences? _preferences;
    private AssociateWorkspace? _workspace;
    private MorningMessageComposition? _composition;
    private bool _suppressChanges;
    private bool _contentDirty;
    private bool _preferencesDirty;
    private CancellationTokenSource? _saveCts;

    public MorningMessageViewModel(
        IClipboardService clipboard,
        Func<AssociateWorkspace?> workspaceProvider,
        Func<DateOnly> easternToday,
        Func<DateTimeOffset> now,
        IMorningMessagePreferenceStore? preferenceStore = null,
        IMorningMessageContentStore? contentStore = null,
        IImageFilePicker? imagePicker = null,
        ILogger? logger = null,
        Func<CancellationToken, Task>? saveDelay = null,
        MorningMessageEditorSession? session = null,
        Action<Action>? postToUi = null)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _workspaceProvider = workspaceProvider ?? throw new ArgumentNullException(nameof(workspaceProvider));
        _easternToday = easternToday ?? throw new ArgumentNullException(nameof(easternToday));
        _now = now ?? throw new ArgumentNullException(nameof(now));
        _preferenceStore = preferenceStore ?? new InMemoryMorningMessagePreferenceStore();
        _contentStore = contentStore ?? new InMemoryMorningMessageContentStore();
        _imagePicker = imagePicker;
        _logger = logger;
        _saveDelay = saveDelay ?? (token => Task.Delay(TimeSpan.FromMilliseconds(500), token));
        _session = session ?? new MorningMessageEditorSession();

        var post = postToUi ?? (action => action());
        _session.Changed += (_, change) => post(() => OnSyncChanged(change));
    }

    /// <summary>The saved preferences, loaded on first use (one small local file) and reloaded each time the composer opens.</summary>
    public MorningMessagePreferences Preferences => _preferences ??= _preferenceStore.Load();

    /// <summary>Phase 30: shown while changes from another computer are waiting for the composer to close.</summary>
    [ObservableProperty]
    public partial string? SyncNotice { get; set; }

    public const string SyncWaitingNotice =
        "Changes to this morning message from another computer are waiting. They will be added after you close this window; anything you type here is kept.";

    /// <summary>The in-flight (or most recent) local save, so a test can await it.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial DateOnly? OperatingDate { get; set; }

    [ObservableProperty]
    public partial string DateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Reminders { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RemindersAsBullets { get; set; }

    [ObservableProperty]
    public partial string BreakPlan { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DapPlacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DapImageReminder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDapImage))]
    [NotifyPropertyChangedFor(nameof(DapImageName))]
    [NotifyCanExecuteChangedFor(nameof(CopyDapImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearDapImageCommand))]
    public partial string? DapImagePath { get; set; }

    [ObservableProperty]
    public partial string OdNotes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PriceNote { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlainTextDefault))]
    public partial bool UseFormattedByDefault { get; set; }

    public bool IsPlainTextDefault => !UseFormattedByDefault;

    public bool HasDapImage => !string.IsNullOrWhiteSpace(DapImagePath);

    public string? DapImageName => HasDapImage ? Path.GetFileName(DapImagePath) : null;

    public bool CanPickImage => _imagePicker is not null;

    public ObservableCollection<MorningMessageSectionRowViewModel> Sections { get; } = [];

    public ObservableCollection<MorningMessagePriceSectionOption> PriceSections { get; } = [];

    /// <summary>The open date's passdown notes the user may deliberately add to Reminders — never added automatically.</summary>
    public ObservableCollection<MorningMessageNoteOption> NoteOptions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddNoteToRemindersCommand))]
    public partial MorningMessageNoteOption? SelectedNoteOption { get; set; }

    public bool HasNoteOptions => NoteOptions.Count > 0;

    /// <summary>The whole main message exactly as Copy Plain Text puts it on the clipboard.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyFormattedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyPlainTextCommand))]
    public partial string PreviewText { get; set; } = string.Empty;

    /// <summary>
    /// Phase 29B: the same message as <see cref="PreviewText"/>, styled for the selected copy mode — bold
    /// headings and labels when formatted copy is the default, unstyled when plain text is.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<TeamsPreviewSpan> PreviewSpans { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyOdNotesFormattedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyOdNotesPlainTextCommand))]
    public partial string OdNotesPreviewText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    /// <summary>Shown after copying the main message when the user plans to attach an image — Teams media is always attached by hand.</summary>
    [ObservableProperty]
    public partial string? AfterCopyReminder { get; set; }

    /// <summary>The order of section names as they will be pasted, for the preview's summary line.</summary>
    [ObservableProperty]
    public partial string OrderSummary { get; set; } = string.Empty;

    /// <summary>Mentions are never created by pasting text (Phase 29 Part D §18).</summary>
    public static string MentionHint => "Typing @Name here does not tag or notify anyone. Mention people in Teams after pasting.";

    /// <summary>Opens the composer for the Associate screen's current date and on-screen edits. Does nothing when no passdown is loaded.</summary>
    public async Task OpenAsync()
    {
        if (_workspaceProvider() is not { } workspace)
        {
            return;
        }

        await FlushSaveAsync().ConfigureAwait(true);

        _workspace = workspace;
        MorningMessageContent content;
        MorningMessagePreferences preferences;
        using (await _session.LockAsync().ConfigureAwait(true))
        {
            // Held before loading, so a sync pass can't change this date or the preferences underneath the
            // editor; reloaded so anything another computer sent since the last open is shown.
            _session.MarkOpen(workspace.OperatingDate);
            _preferences = null;
            content = await _contentStore.LoadAsync(workspace.OperatingDate).ConfigureAwait(true);
            preferences = Preferences;
        }

        _suppressChanges = true;
        try
        {
            OperatingDate = workspace.OperatingDate;
            DateText = workspace.OperatingDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, MMMM d, yyyy", PresentationCulture.UnitedStates);
            Greeting = preferences.Greeting;
            RemindersAsBullets = preferences.RemindersAsBullets;
            UseFormattedByDefault = preferences.DefaultCopyMode == TeamsCopyMode.Formatted;
            Reminders = content.Reminders;
            BreakPlan = content.BreakPlan;
            DapPlacement = content.DapPlacement;
            DapImageReminder = content.DapImageReminder;
            DapImagePath = content.DapImagePath;
            OdNotes = content.OdNotes;
            PriceNote = content.PriceNote;
            StatusMessage = null;
            StatusIsError = false;
            AfterCopyReminder = null;
            SyncNotice = null;

            RebuildSectionRows();
            RebuildPriceOptions();
            RebuildNoteOptions(workspace);
        }
        finally
        {
            _suppressChanges = false;
        }

        Recompose();
        IsOpen = true;
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        IsOpen = false;
        await FlushSaveAsync().ConfigureAwait(true);

        using (await _session.LockAsync().ConfigureAwait(true))
        {
            _session.MarkClosed();
        }

        SyncNotice = null;
        _session.RequestSync(); // carries this session's edits, and anything held while it was open
    }

    /// <summary>Phase 30: sync changed or held back Morning Message data (already on the UI thread).</summary>
    private void OnSyncChanged(MorningMessageSyncChange change)
    {
        if (IsOpen && ((OperatingDate is { } date && change.DeferredDates.Contains(date)) || change.PreferencesDeferred))
        {
            SyncNotice = SyncWaitingNotice;
        }

        if (!IsOpen && change.PreferencesUpdated)
        {
            _preferences = null; // the Copy menu's default copy mode follows the synchronized preference
        }
    }

    /// <summary>The current main message (for tests and the copy actions).</summary>
    public TeamsMessage Message => _composition?.Message ?? TeamsMessage.Empty;

    public TeamsMessage OdNotesMessage => _composition?.OdNotes ?? TeamsMessage.Empty;

    [RelayCommand(CanExecute = nameof(HasMessage))]
    private Task CopyFormattedAsync() => CopyMainAsync(TeamsCopyMode.Formatted);

    [RelayCommand(CanExecute = nameof(HasMessage))]
    private Task CopyPlainTextAsync() => CopyMainAsync(TeamsCopyMode.PlainText);

    [RelayCommand(CanExecute = nameof(HasOdNotes))]
    private Task CopyOdNotesFormattedAsync() => CopyOdNotesAsync(TeamsCopyMode.Formatted);

    [RelayCommand(CanExecute = nameof(HasOdNotes))]
    private Task CopyOdNotesPlainTextAsync() => CopyOdNotesAsync(TeamsCopyMode.PlainText);

    private bool HasMessage() => !string.IsNullOrEmpty(PreviewText);

    private bool HasOdNotes() => !string.IsNullOrEmpty(OdNotesPreviewText);

    private async Task CopyMainAsync(TeamsCopyMode mode)
    {
        AfterCopyReminder = null;
        var outcome = await CopyAsync(Message, mode, "Morning message copied", "morning message").ConfigureAwait(true);
        if (outcome != TeamsCopyOutcome.Failed && _composition is { AfterCopyReminders.Count: > 0 } composition)
        {
            AfterCopyReminder = string.Join(" ", composition.AfterCopyReminders);
        }
    }

    private Task<TeamsCopyOutcome> CopyOdNotesAsync(TeamsCopyMode mode) => CopyAsync(OdNotesMessage, mode, "OD Notes copied", "OD notes");

    private async Task<TeamsCopyOutcome> CopyAsync(TeamsMessage message, TeamsCopyMode mode, string successText, string context)
    {
        StatusMessage = null;
        StatusIsError = false;

        if (message.IsEmpty)
        {
            StatusMessage = "Nothing to copy yet.";
            return TeamsCopyOutcome.Failed;
        }

        var outcome = await TeamsClipboard.CopyAsync(_clipboard, message, mode, _logger, context).ConfigureAwait(true);
        (StatusMessage, StatusIsError) = outcome switch
        {
            TeamsCopyOutcome.Copied => (mode == TeamsCopyMode.Formatted ? successText + " (formatted)" : successText + " (plain text)", false),
            TeamsCopyOutcome.CopiedAsPlainTextFallback => (successText + TeamsClipboard.FallbackSuffix, false),
            _ => (TeamsClipboard.FailureMessage, true),
        };

        await FlushSaveAsync().ConfigureAwait(true);
        return outcome;
    }

    /// <summary>
    /// Phase 29: Copy ▾ → OD Notes without opening the composer — the date's saved OD Notes, in the default
    /// copy mode. Null when the date has none. Pending composer edits are saved first so the copy matches.
    /// </summary>
    public async Task<TeamsCopyOutcome?> CopySavedOdNotesAsync(DateOnly operatingDate)
    {
        await FlushSaveAsync().ConfigureAwait(true);
        var content = await _contentStore.LoadAsync(operatingDate).ConfigureAwait(true);
        var message = MorningMessageComposer.OdNotes(content);
        if (message.IsEmpty)
        {
            return null;
        }

        return await TeamsClipboard.CopyAsync(_clipboard, message, Preferences.DefaultCopyMode, _logger, "OD notes").ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasDapImage))]
    private async Task CopyDapImageAsync()
    {
        StatusMessage = null;
        StatusIsError = false;
        if (DapImagePath is not { } path)
        {
            return;
        }

        if (!File.Exists(path))
        {
            StatusMessage = "The chosen image file can't be found. Choose it again.";
            StatusIsError = true;
            return;
        }

        try
        {
            await _clipboard.SetImageFileAsync(path).ConfigureAwait(true);
            StatusMessage = "DAP image copied — paste it into Teams as its own attachment.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Copying the DAP image failed.");
            StatusMessage = "Couldn't copy the image. Attach it in Teams instead.";
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ChooseDapImageAsync()
    {
        if (_imagePicker is null)
        {
            return;
        }

        try
        {
            if (await _imagePicker.PickImageAsync().ConfigureAwait(true) is { } path)
            {
                DapImagePath = path;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Choosing the DAP image failed.");
            StatusMessage = "Couldn't open the image picker.";
            StatusIsError = true;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDapImage))]
    private void ClearDapImage() => DapImagePath = null;

    [RelayCommand(CanExecute = nameof(CanAddNote))]
    private void AddNoteToReminders()
    {
        if (SelectedNoteOption is not { } note)
        {
            return;
        }

        Reminders = string.IsNullOrWhiteSpace(Reminders) ? note.Text : Reminders.TrimEnd() + Environment.NewLine + note.Text;
        SelectedNoteOption = null;
    }

    private bool CanAddNote() => SelectedNoteOption is not null;

    /// <summary>Explicitly copies the most recent earlier date's reminders into today's (appended when today already has some).</summary>
    [RelayCommand]
    private async Task ReuseLastRemindersAsync()
    {
        if (OperatingDate is not { } date)
        {
            return;
        }

        var previous = await _contentStore.FindPreviousRemindersAsync(date).ConfigureAwait(true);
        if (previous is not { } found)
        {
            StatusMessage = "No earlier reminders on this computer.";
            StatusIsError = false;
            return;
        }

        Reminders = string.IsNullOrWhiteSpace(Reminders) ? found.Reminders : Reminders.TrimEnd() + Environment.NewLine + found.Reminders;
        StatusMessage = "Added reminders from " + found.Date.ToDateTime(TimeOnly.MinValue).ToString("MMM d", PresentationCulture.UnitedStates) + ".";
        StatusIsError = false;
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _preferences = Preferences with { Layout = MorningMessageLayout.Default };
        RebuildSectionRows();
        PreferencesChanged();
    }

    internal void MoveSection(MorningMessageSectionId id, bool up)
    {
        var layout = up ? Preferences.Layout.MoveUp(id) : Preferences.Layout.MoveDown(id);
        if (layout.Equals(Preferences.Layout))
        {
            return;
        }

        _preferences = Preferences with { Layout = layout };
        var row = Sections.First(section => section.Id == id);
        var from = Sections.IndexOf(row);
        Sections.Move(from, up ? from - 1 : from + 1);
        UpdateRowPositions();
        PreferencesChanged();
    }

    internal void SetSectionEnabled(MorningMessageSectionId id, bool enabled)
    {
        if (_suppressChanges || Preferences.Layout.IsEnabled(id) == enabled)
        {
            return;
        }

        _preferences = Preferences with { Layout = Preferences.Layout.WithEnabled(id, enabled) };
        UpdateRowPositions();
        PreferencesChanged();
    }

    internal void SetPriceSectionSelected(ProductDisplaySection section, bool selected)
    {
        if (_suppressChanges)
        {
            return;
        }

        var sections = MorningMessagePreferences.SelectablePriceSections
            .Where(candidate => candidate == section ? selected : Preferences.PriceSections.Contains(candidate))
            .ToList();
        _preferences = Preferences with { PriceSections = sections };
        PreferencesChanged();
    }

    partial void OnGreetingChanged(string value)
    {
        if (_suppressChanges)
        {
            return;
        }

        _preferences = Preferences with { Greeting = (value ?? string.Empty).Length > MorningMessagePreferences.MaxGreetingLength ? value![..MorningMessagePreferences.MaxGreetingLength] : value ?? string.Empty };
        PreferencesChanged();
    }

    partial void OnRemindersAsBulletsChanged(bool value)
    {
        if (!_suppressChanges)
        {
            _preferences = Preferences with { RemindersAsBullets = value };
            PreferencesChanged();
        }
    }

    partial void OnUseFormattedByDefaultChanged(bool value)
    {
        RefreshPreviewSpans();
        if (!_suppressChanges)
        {
            _preferences = Preferences with { DefaultCopyMode = value ? TeamsCopyMode.Formatted : TeamsCopyMode.PlainText };
            PreferencesChanged();
        }
    }

    partial void OnRemindersChanged(string value) => ContentChanged();

    partial void OnBreakPlanChanged(string value) => ContentChanged();

    partial void OnDapPlacementChanged(string value) => ContentChanged();

    partial void OnDapImageReminderChanged(bool value) => ContentChanged();

    partial void OnDapImagePathChanged(string? value) => ContentChanged();

    partial void OnOdNotesChanged(string value) => ContentChanged();

    partial void OnPriceNoteChanged(string value) => ContentChanged();

    private MorningMessageContent CurrentContent() => new MorningMessageContent(
        Reminders ?? string.Empty, BreakPlan ?? string.Empty, DapPlacement ?? string.Empty, DapImageReminder, DapImagePath,
        OdNotes ?? string.Empty, PriceNote ?? string.Empty).Sanitized();

    private void ContentChanged()
    {
        if (_suppressChanges)
        {
            return;
        }

        _contentDirty = true;
        Recompose();
        ScheduleSave();
    }

    private void PreferencesChanged()
    {
        _preferencesDirty = true;
        Recompose();
        ScheduleSave();
    }

    private void Recompose()
    {
        if (_workspace is null)
        {
            return;
        }

        _composition = MorningMessageComposer.Compose(_workspace, Preferences, CurrentContent(), _easternToday(), _now());
        foreach (var preview in _composition.Preview)
        {
            Sections.FirstOrDefault(row => row.Id == preview.Id)?.Apply(preview);
        }

        PreviewText = TeamsPlainTextRenderer.Render(_composition.Message);
        RefreshPreviewSpans();
        OdNotesPreviewText = TeamsPlainTextRenderer.Render(_composition.OdNotes);
        OrderSummary = string.Join(" → ", _composition.Preview.Where(section => section.Enabled).Select(section => section.Name));
    }

    private void RefreshPreviewSpans() =>
        PreviewSpans = TeamsPreviewRenderer.Render(Message, UseFormattedByDefault ? TeamsCopyMode.Formatted : TeamsCopyMode.PlainText);

    private void RebuildSectionRows()
    {
        Sections.Clear();
        foreach (var setting in Preferences.Layout.Sections)
        {
            Sections.Add(new MorningMessageSectionRowViewModel(this, setting.Id, MorningMessageComposer.SectionName(setting.Id), setting.Enabled));
        }

        UpdateRowPositions();
    }

    private void UpdateRowPositions()
    {
        for (var i = 0; i < Sections.Count; i++)
        {
            Sections[i].SetPosition(i + 1, Sections.Count);
        }
    }

    private void RebuildPriceOptions()
    {
        PriceSections.Clear();
        foreach (var section in MorningMessagePreferences.SelectablePriceSections)
        {
            PriceSections.Add(new MorningMessagePriceSectionOption(this, section, MorningMessageComposer.PriceSectionName(section), Preferences.PriceSections.Contains(section)));
        }
    }

    private void RebuildNoteOptions(AssociateWorkspace workspace)
    {
        NoteOptions.Clear();
        foreach (var note in workspace.Draft.SelectedOperationalNotes)
        {
            var text = string.IsNullOrWhiteSpace(note.Body) ? note.Title.Trim() : $"{note.Title.Trim()}: {note.Body.Trim()}";
            NoteOptions.Add(new MorningMessageNoteOption(note.Title.Trim(), text));
        }

        foreach (var line in workspace.Draft.OperationalNotes.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            NoteOptions.Add(new MorningMessageNoteOption(line.Trim(), line.Trim()));
        }

        SelectedNoteOption = null;
        OnPropertyChanged(nameof(HasNoteOptions));
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        PendingSave = SaveAfterDelayAsync(cts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await _saveDelay(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await SaveNowAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Writes any pending preference/content change now (on copy, close, or before reopening).</summary>
    public async Task FlushSaveAsync()
    {
        _saveCts?.Cancel();
        await SaveNowAsync().ConfigureAwait(true);
    }

    private async Task SaveNowAsync()
    {
        await _saveGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var saved = false;
            if (_contentDirty && OperatingDate is { } date)
            {
                _contentDirty = false;
                await _contentStore.SaveAsync(date, CurrentContent()).ConfigureAwait(true);
                saved = true;
            }

            if (_preferencesDirty)
            {
                _preferencesDirty = false;
                await _preferenceStore.SaveAsync(Preferences).ConfigureAwait(true);
                saved = true;
            }

            if (saved)
            {
                _session.RequestSync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger?.LogWarning("Morning message text could not be saved on this computer ({ExceptionType}).", ex.GetType().Name);
            StatusMessage = "Couldn't save the message text on this computer. Copying still works.";
            StatusIsError = true;
        }
        finally
        {
            _saveGate.Release();
        }
    }
}

/// <summary>Phase 29: one row of the composer's section list — include switch, Move Up / Move Down, and its preview.</summary>
public partial class MorningMessageSectionRowViewModel : ObservableObject
{
    private readonly MorningMessageViewModel _owner;

    public MorningMessageSectionRowViewModel(MorningMessageViewModel owner, MorningMessageSectionId id, string name, bool isEnabled)
    {
        _owner = owner;
        Id = id;
        Name = name;
        _isEnabledBacking = isEnabled;
    }

    private bool _isEnabledBacking;

    public MorningMessageSectionId Id { get; }

    public string Name { get; }

    public bool IsEnabled
    {
        get => _isEnabledBacking;
        set
        {
            if (SetProperty(ref _isEnabledBacking, value))
            {
                _owner.SetSectionEnabled(Id, value);
                OnPropertyChanged(nameof(AutomationName));
                OnPropertyChanged(nameof(IncludeAutomationName));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial int Position { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool CanMoveUp { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool CanMoveDown { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial string PreviewText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText))]
    public partial string? EmptyText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotices))]
    public partial IReadOnlyList<string> Notices { get; private set; } = [];

    public bool HasPreview => !string.IsNullOrEmpty(PreviewText);

    public bool HasEmptyText => !string.IsNullOrEmpty(EmptyText);

    public bool HasNotices => Notices.Count > 0;

    private int _count;

    public string AutomationName => $"{Name}, position {Position} of {_count}, {(IsEnabled ? "included" : "not included")}";

    public string IncludeAutomationName => $"Include {Name} in the morning message";

    public string MoveUpAutomationName => $"Move {Name} up";

    public string MoveDownAutomationName => $"Move {Name} down";

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.MoveSection(Id, up: true);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _owner.MoveSection(Id, up: false);

    internal void SetPosition(int position, int count)
    {
        _count = count;
        Position = position;
        CanMoveUp = position > 1;
        CanMoveDown = position < count;
        OnPropertyChanged(nameof(AutomationName));
    }

    internal void Apply(MorningMessagePreviewSection preview)
    {
        PreviewText = preview.PlainText;
        EmptyText = preview.EmptyText;
        Notices = preview.Notices;
    }
}

/// <summary>Phase 29: one official price section the message's Ticket Prices may include.</summary>
public partial class MorningMessagePriceSectionOption : ObservableObject
{
    private readonly MorningMessageViewModel _owner;
    private bool _isSelected;

    public MorningMessagePriceSectionOption(MorningMessageViewModel owner, ProductDisplaySection section, string label, bool isSelected)
    {
        _owner = owner;
        Section = section;
        Label = label;
        _isSelected = isSelected;
    }

    public ProductDisplaySection Section { get; }

    public string Label { get; }

    public string AutomationName => $"Include {Label} prices in the morning message";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _owner.SetPriceSectionSelected(Section, value);
            }
        }
    }
}

/// <summary>Phase 29: a passdown note offered for deliberate insertion into Reminders.</summary>
public sealed record MorningMessageNoteOption(string Label, string Text)
{
    public override string ToString() => Label;
}
