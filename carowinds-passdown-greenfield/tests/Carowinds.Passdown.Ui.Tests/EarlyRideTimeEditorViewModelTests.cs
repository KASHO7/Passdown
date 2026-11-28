using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 36C (docs/phase36c-ert-editor-maintainability.md): <see cref="EarlyRideTimeEditorViewModel"/> on its own — the
/// compact Early Ride Time panel driven by a parent-supplied current date, a workspace-refresh callback and a sync-conflict
/// callback. The workspace integration is covered by <see cref="Phase36CErtEditorIntegrationTests"/> and the existing
/// <see cref="AssociateViewModelEarlyRideTimeTests"/> / <see cref="OpenEditorSyncProtectionTests"/>.
/// </summary>
public class EarlyRideTimeEditorViewModelTests
{
    private static readonly DateOnly August21 = new(2026, 8, 21);
    private static readonly DateOnly September3 = new(2026, 9, 3);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    /// <summary>The fake ERT service with optional gates (to hold a read or save in flight) and an injectable save failure.</summary>
    private sealed class GatedErtService(FakeErtConfigurationService inner) : IErtConfigurationService
    {
        public TaskCompletionSource? ReadGate { get; set; }

        public TaskCompletionSource? SaveGate { get; set; }

        public Exception? SaveException { get; set; }

        public List<(int Year, int Month)> MonthlyReads { get; } = [];

        public List<DateOnly> OverrideReads { get; } = [];

        public async Task<MonthlyErtConfiguration?> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default)
        {
            MonthlyReads.Add((year, month));
            if (ReadGate is { } gate)
            {
                await gate.Task;
            }

            return await inner.GetMonthlyAsync(year, month, cancellationToken);
        }

        public Task<ErtDateOverride?> GetOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
        {
            OverrideReads.Add(operatingDate);
            return inner.GetOverrideAsync(operatingDate, cancellationToken);
        }

        public async Task<ErtEditorSaveResult> SaveMonthlyFromEditorAsync(MonthlyErtDraft draft, string? expectedContentHash, CancellationToken cancellationToken = default)
        {
            await BeforeSaveAsync();
            return await inner.SaveMonthlyFromEditorAsync(draft, expectedContentHash, cancellationToken);
        }

        public async Task<ErtEditorSaveResult> SaveOverrideFromEditorAsync(ErtDateOverrideDraft draft, string? expectedContentHash, CancellationToken cancellationToken = default)
        {
            await BeforeSaveAsync();
            return await inner.SaveOverrideFromEditorAsync(draft, expectedContentHash, cancellationToken);
        }

        public async Task<ErtEditorSaveResult> RemoveOverrideFromEditorAsync(DateOnly operatingDate, string? expectedContentHash, CancellationToken cancellationToken = default)
        {
            await BeforeSaveAsync();
            return await inner.RemoveOverrideFromEditorAsync(operatingDate, expectedContentHash, cancellationToken);
        }

        public Task<MonthlyErtConfiguration> SaveMonthlyAsync(MonthlyErtDraft draft, CancellationToken cancellationToken = default) => inner.SaveMonthlyAsync(draft, cancellationToken);

        public Task<ErtDateOverride> SaveOverrideAsync(ErtDateOverrideDraft draft, CancellationToken cancellationToken = default) => inner.SaveOverrideAsync(draft, cancellationToken);

        public Task RemoveOverrideAsync(DateOnly operatingDate, CancellationToken cancellationToken = default) => inner.RemoveOverrideAsync(operatingDate, cancellationToken);

        private async Task BeforeSaveAsync()
        {
            if (SaveGate is { } gate)
            {
                await gate.Task;
            }

            if (SaveException is { } exception)
            {
                throw exception;
            }
        }
    }

    /// <summary>The editor plus the parent's side of the contract: a mutable selected date and recorded callbacks.</summary>
    private sealed class Harness
    {
        public Harness(Action<Harness>? seed = null)
        {
            seed?.Invoke(this);
            Editor = new EarlyRideTimeEditorViewModel(
                Service,
                () => CurrentDate,
                date =>
                {
                    Refreshes.Add(date);
                    Editor!.CloseForWorkspaceReplacement(); // as the parent's ApplyWorkspaceToFields does
                    return Task.CompletedTask;
                },
                Conflicts.Add);
        }

        public FakeErtConfigurationService Store { get; } = new();

        public GatedErtService Service => _service ??= new GatedErtService(Store);

        public DateOnly CurrentDate { get; set; } = August21;

        public List<DateOnly> Refreshes { get; } = [];

        public List<string> Conflicts { get; } = [];

        public EarlyRideTimeEditorViewModel Editor { get; }

        private GatedErtService? _service;
    }

    private static MonthlyErtConfiguration AugustMonthly(params string[] rides) =>
        new(2026, 8, ErtConfigurationState.Configured, null, rides, "Monthly note", FixedNow);

    [Fact]
    public void Constructor_RejectsMissingCollaborators()
    {
        var service = new FakeErtConfigurationService();
        Func<DateOnly> date = () => August21;
        Func<DateOnly, Task> refresh = _ => Task.CompletedTask;
        Action<string> conflict = _ => { };

        Assert.Throws<ArgumentNullException>(() => new EarlyRideTimeEditorViewModel(null!, date, refresh, conflict));
        Assert.Throws<ArgumentNullException>(() => new EarlyRideTimeEditorViewModel(service, null!, refresh, conflict));
        Assert.Throws<ArgumentNullException>(() => new EarlyRideTimeEditorViewModel(service, date, null!, conflict));
        Assert.Throws<ArgumentNullException>(() => new EarlyRideTimeEditorViewModel(service, date, refresh, null!));
    }

    [Fact]
    public void InitialState_IsClosedEmptyAndClean()
    {
        var h = new Harness();

        Assert.False(h.Editor.IsErtEditorOpen);
        Assert.Null(h.Editor.MonthlyErtRidesText);
        Assert.False(h.Editor.MonthlyErtNoErt);
        Assert.False(h.Editor.ErtUseOverrideForDate);
        Assert.Null(h.Editor.MonthlyErtSaveError);
        Assert.Null(h.Editor.OverrideSaveError);
        Assert.False(h.Editor.IsDirty);
        Assert.Empty(h.Service.MonthlyReads);
    }

    [Fact]
    public async Task Open_ReadsTheCurrentDatesMonthAndOverride_AndFillsThePanel()
    {
        var h = new Harness(x =>
        {
            x.CurrentDate = September3;
            x.Store.SeedMonthly(new MonthlyErtConfiguration(2026, 9, ErtConfigurationState.Configured, null, ["Fury 325", "Copperhead Strike"], "Sept note", FixedNow));
            x.Store.SeedOverride(new ErtDateOverride(September3, ErtConfigurationState.NoErt, null, [], "Storm", FixedNow));
        });

        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.Equal([(2026, 9)], h.Service.MonthlyReads);
        Assert.Equal([September3], h.Service.OverrideReads);
        Assert.True(h.Editor.IsErtEditorOpen);
        Assert.Equal($"Fury 325{Environment.NewLine}Copperhead Strike", h.Editor.MonthlyErtRidesText);
        Assert.Equal("Sept note", h.Editor.MonthlyErtNoteText);
        Assert.True(h.Editor.ErtUseOverrideForDate);
        Assert.True(h.Editor.OverrideNoErt);
        Assert.Equal("Storm", h.Editor.OverrideReasonText);
        Assert.False(h.Editor.IsDirty);
        Assert.Empty(h.Refreshes); // opening never reloads the workspace
    }

    [Fact]
    public async Task Open_SupersededByADateSwitchWhileReading_NeitherOpensNorFillsThePanel()
    {
        var h = new Harness(x => x.Store.SeedMonthly(AugustMonthly("Fury 325")));
        h.Service.ReadGate = new TaskCompletionSource();

        var open = h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.CurrentDate = September3; // the user moved on before the read finished
        h.Service.ReadGate.SetResult();
        await open;

        Assert.False(h.Editor.IsErtEditorOpen);
        Assert.Null(h.Editor.MonthlyErtRidesText);
        Assert.False(await h.Editor.LoadErtEditorFieldsAsync(August21)); // a superseded load reports false
    }

    [Fact]
    public async Task EditingTheRides_MakesThePanelDirty_AndCancelClosesWithoutSavingOrRefreshing()
    {
        var h = new Harness(x => x.Store.SeedMonthly(AugustMonthly("Fury 325")));
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);

        h.Editor.MonthlyErtRidesText = "Fury 325\nIntimidator";
        Assert.True(h.Editor.IsDirty);

        h.Editor.CancelErtEditorCommand.Execute(null);

        Assert.False(h.Editor.IsErtEditorOpen);
        Assert.False(h.Editor.IsDirty); // a closed panel is never "dirty"
        Assert.Empty(h.Store.SaveMonthlyCalls);
        Assert.Empty(h.Refreshes);
    }

    [Fact]
    public async Task Reopening_AfterCancel_ShowsThePersistedValuesAgain()
    {
        var h = new Harness(x => x.Store.SeedMonthly(AugustMonthly("Fury 325")));
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "Unsaved text";
        h.Editor.CancelErtEditorCommand.Execute(null);

        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.True(h.Editor.IsErtEditorOpen);
        Assert.Equal("Fury 325", h.Editor.MonthlyErtRidesText);
        Assert.False(h.Editor.IsDirty);
    }

    [Fact]
    public async Task SaveMonthly_SavesTheShownMonth_RefreshesThatDate_ReopensClean()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "  Fury 325 \r\n\r\nCopperhead Strike  ";
        h.Editor.MonthlyErtNoteText = "  Seasonal  ";

        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        var saved = Assert.Single(h.Store.SaveMonthlyCalls);
        Assert.Equal((2026, 8), (saved.Year, saved.Month));
        Assert.Equal(ErtConfigurationState.Configured, saved.State);
        Assert.Null(saved.StartTime); // Phase 17D §1: no routine start time is ever sent
        Assert.Equal(["Fury 325", "Copperhead Strike"], saved.Rides);
        Assert.Equal("Seasonal", saved.Note);
        Assert.Equal([August21], h.Refreshes);
        Assert.True(h.Editor.IsErtEditorOpen); // Phase 17D §3: the shared panel stays open across Save
        Assert.False(h.Editor.IsDirty);
        Assert.Empty(h.Conflicts);
    }

    [Fact]
    public async Task SaveMonthly_NoErt_ClearsTheRidesItSends()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "Fury 325";
        h.Editor.MonthlyErtNoErt = true;

        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        var saved = Assert.Single(h.Store.SaveMonthlyCalls);
        Assert.Equal(ErtConfigurationState.NoErt, saved.State);
        Assert.Empty(saved.Rides);
    }

    [Fact]
    public async Task SaveMonthly_DateSwitchedWhileSaving_WritesTheMonthItShowed_AndNeverRefreshesTheNewDate()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "Fury 325";
        h.Service.SaveGate = new TaskCompletionSource();

        var save = h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        h.CurrentDate = September3;
        h.Editor.CloseForWorkspaceReplacement(); // the date switch replaced the workspace
        h.Service.SaveGate.SetResult();
        await save;

        var saved = Assert.Single(h.Store.SaveMonthlyCalls);
        Assert.Equal((2026, 8), (saved.Year, saved.Month)); // the values belonged to August, so August is written
        Assert.Empty(h.Refreshes);                           // September's workspace is not reloaded with August's result
        Assert.False(h.Editor.IsErtEditorOpen);              // and the panel is not reopened over the new date
    }

    [Fact]
    public async Task SaveOverride_SavesTheCurrentDate_AndRemoveOverride_ClearsTheOverrideFields()
    {
        var h = new Harness(x => x.CurrentDate = September3);
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.ErtUseOverrideForDate = true;
        h.Editor.OverrideRidesText = "Copperhead Strike";
        h.Editor.OverrideReasonText = " Fury maintenance ";

        await h.Editor.SaveOverrideCommand.ExecuteAsync(null);

        var saved = Assert.Single(h.Store.SaveOverrideCalls);
        Assert.Equal(September3, saved.OperatingDate);
        Assert.Equal(["Copperhead Strike"], saved.Rides);
        Assert.Equal("Fury maintenance", saved.Reason);
        Assert.Null(saved.StartTime);
        Assert.True(h.Editor.ErtUseOverrideForDate);

        await h.Editor.RemoveOverrideCommand.ExecuteAsync(null);

        Assert.Equal([September3], h.Store.RemoveOverrideCalls);
        Assert.False(h.Editor.ErtUseOverrideForDate);
        Assert.False(h.Editor.OverrideNoErt);
        Assert.Null(h.Editor.OverrideRidesText);
        Assert.Null(h.Editor.OverrideReasonText);
        Assert.Equal([September3, September3], h.Refreshes);
        Assert.True(h.Editor.IsErtEditorOpen);
        Assert.False(h.Editor.IsDirty);
    }

    [Fact]
    public async Task ServiceFailure_ShowsTheExistingMessage_KeepsTheValues_AndDoesNotRefresh_ThenARetrySucceeds()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "Fury 325";
        h.Editor.ErtUseOverrideForDate = true;
        h.Editor.OverrideRidesText = "Intimidator";
        h.Service.SaveException = new IOException("disk");

        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        await h.Editor.SaveOverrideCommand.ExecuteAsync(null);
        await h.Editor.RemoveOverrideCommand.ExecuteAsync(null);

        Assert.Equal("Could not save. Try again.", h.Editor.MonthlyErtSaveError);
        Assert.Equal("Could not remove. Try again.", h.Editor.OverrideSaveError);
        Assert.Equal("Fury 325", h.Editor.MonthlyErtRidesText);
        Assert.True(h.Editor.IsErtEditorOpen);
        Assert.Empty(h.Refreshes);

        h.Service.SaveException = null;
        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        Assert.Null(h.Editor.MonthlyErtSaveError);
        Assert.Single(h.Store.SaveMonthlyCalls);
        Assert.Equal([August21], h.Refreshes);
    }

    [Fact]
    public async Task ChangedUnderneathTheOpenPanel_SaveReportsTheExistingConflictOnce_AndTheNextSaveIsClean()
    {
        var h = new Harness(x => x.Store.SeedMonthly(AugustMonthly("Fury 325")));
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Store.SeedMonthly(AugustMonthly("Changed on another device")); // Personal Sync replaced the record
        h.Editor.MonthlyErtRidesText = "Intimidator";

        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        Assert.Equal([EarlyRideTimeEditorViewModel.ErtSyncConflictMessage], h.Conflicts);
        Assert.Single(h.Store.EditorConflicts);

        h.Editor.MonthlyErtRidesText = "Intimidator\nFury 325";
        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);

        Assert.Single(h.Conflicts); // the base hash advanced with the first save
    }

    [Fact]
    public async Task CloseForWorkspaceReplacement_ClosesAndClearsErrors_ButLeavesTheValues()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Editor.MonthlyErtRidesText = "Fury 325";
        h.Service.SaveException = new IOException("disk");
        await h.Editor.SaveMonthlyErtCommand.ExecuteAsync(null);
        Assert.NotNull(h.Editor.MonthlyErtSaveError);

        h.Editor.CloseForWorkspaceReplacement();

        Assert.False(h.Editor.IsErtEditorOpen);
        Assert.Null(h.Editor.MonthlyErtSaveError);
        Assert.Null(h.Editor.OverrideSaveError);
        Assert.Equal("Fury 325", h.Editor.MonthlyErtRidesText);
    }

    [Fact]
    public async Task Open_ClearsAPreviousSaveError()
    {
        var h = new Harness();
        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);
        h.Service.SaveException = new IOException("disk");
        await h.Editor.SaveOverrideCommand.ExecuteAsync(null);
        Assert.NotNull(h.Editor.OverrideSaveError);
        h.Service.SaveException = null;

        await h.Editor.OpenErtEditorCommand.ExecuteAsync(null);

        Assert.Null(h.Editor.OverrideSaveError);
        Assert.Null(h.Editor.MonthlyErtSaveError);
    }
}
