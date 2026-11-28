using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: Settings → Passdown Defaults → "Include Weather Outlook by default" and the Passdown's "Weather Outlook"
/// checkbox are one authoritative device preference (the Phase 37F <c>passdown-weather-outlook.json</c>): either surface
/// changes the other at once, each change is saved once, the preview follows, and nothing becomes draft, sync,
/// database or snapshot data. Synthetic data and temporary folders only.
/// </summary>
public partial class Phase37FPassdownWeatherIntegrationTests
{
    private sealed class FailingPreferenceStore : IPassdownWeatherOutlookPreferenceStore
    {
        public bool Fail { get; set; } = true;

        public PassdownWeatherOutlookPreferences Current { get; private set; } = PassdownWeatherOutlookPreferences.BuiltIn;

        public Task<PassdownWeatherOutlookPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(PassdownWeatherOutlookPreferences preferences, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new IOException("Synthetic write failure.");
            }

            Current = preferences;
            return Task.CompletedTask;
        }
    }

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "carowinds-phase37d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task<(Rig Rig, AssociateViewModel ViewModel, PassdownWeatherOutlookSettingsViewModel Shared)> SharedAsync(IPassdownWeatherOutlookPreferenceStore? store = null)
    {
        var rig = new Rig();
        var shared = new PassdownWeatherOutlookSettingsViewModel(store ?? rig.Preferences);
        await shared.PendingLoad;
        rig.Shared = shared;
        rig.Monitor.Current = Snapshot(71);
        var viewModel = rig.Create();
        await viewModel.PendingLoad;
        await viewModel.PendingTemplatePreferenceLoad;
        await viewModel.PendingWeatherOutlookPreferenceLoad;
        await rig.SettleAsync(viewModel);
        return (rig, viewModel, shared);
    }

    [Fact]
    public async Task Phase37D_Settings_IsOnByDefault_WithNoSavedPreference_AndWritesNothing()
    {
        var folder = NewTempFolder();
        try
        {
            var store = new FilePassdownWeatherOutlookPreferenceStore(Path.Combine(folder, "passdown-weather-outlook.json"));
            var settings = new PassdownWeatherOutlookSettingsViewModel(store);
            await settings.PendingLoad;

            Assert.True(settings.IsLoaded);
            Assert.True(settings.IncludeWeatherOutlook);
            Assert.Null(settings.SaveMessage);
            Assert.Empty(Directory.GetFiles(folder)); // loading the default writes nothing

            var (rig, viewModel, shared) = await SharedAsync();
            Assert.True(shared.IncludeWeatherOutlook);
            Assert.True(viewModel.IncludeWeatherOutlook);
            Assert.Equal(0, rig.Preferences.SaveCount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Phase37D_Settings_ReadsTheExistingPhase37FPreferenceFile()
    {
        var folder = NewTempFolder();
        try
        {
            var path = Path.Combine(folder, "passdown-weather-outlook.json");
            await File.WriteAllTextAsync(path, "{\n  \"IncludeWeatherOutlook\": false\n}"); // the Phase 37F file shape
            var (_, viewModel, shared) = await SharedAsync(new FilePassdownWeatherOutlookPreferenceStore(path));

            Assert.False(shared.IncludeWeatherOutlook);
            Assert.False(viewModel.IncludeWeatherOutlook);
            Assert.Null(viewModel.Preview.CurrentDocument!.Weather); // the first preview already used the saved choice
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Phase37D_ChangingSettings_UpdatesThePassdown_RefreshesThePreview_AndSavesOnce()
    {
        var (rig, viewModel, shared) = await SharedAsync();
        var generations = rig.Export.Calls.Count;
        var leading = viewModel.Preview.LeadingRefreshCount;

        shared.IncludeWeatherOutlook = false;

        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.Equal(leading + 1, viewModel.Preview.LeadingRefreshCount); // the existing leading-edge refresh
        Assert.Equal(generations + 1, rig.Export.Calls.Count);
        Assert.Null(rig.Export.Calls[^1].Weather);
        Assert.True(await shared.PendingSave);
        Assert.Equal(1, rig.Preferences.SaveCount); // one save, not one per surface
        Assert.False(rig.Preferences.Current.IncludeWeatherOutlook);

        shared.IncludeWeatherOutlook = true;
        await rig.SettleAsync(viewModel);
        Assert.True(viewModel.IncludeWeatherOutlook);
        Assert.NotNull(viewModel.Preview.CurrentDocument!.Weather);
        await shared.PendingSave;
        Assert.Equal(2, rig.Preferences.SaveCount);

        // A device preference, never draft data: no draft save, no unsaved state.
        Assert.Empty(rig.Service.SaveCalls);
        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
        Assert.Equal(0, rig.Monitor.RefreshNowCount);
    }

    [Fact]
    public async Task Phase37D_ChangingThePassdown_UpdatesSettings_AndSavesOnce()
    {
        var (rig, viewModel, shared) = await SharedAsync();

        viewModel.IncludeWeatherOutlook = false;

        Assert.False(shared.IncludeWeatherOutlook);
        await viewModel.PendingWeatherOutlookPreferenceSave;
        Assert.Equal(1, rig.Preferences.SaveCount);
        Assert.False(rig.Preferences.Current.IncludeWeatherOutlook);
        Assert.Empty(rig.Service.SaveCalls);
        Assert.Equal(AssociateAutosaveState.Saved, viewModel.AutosaveState);
    }

    [Fact]
    public async Task Phase37D_RapidToggles_EndWithTheLatestChoiceSaved()
    {
        var (rig, viewModel, shared) = await SharedAsync();

        for (var i = 0; i < 5; i++)
        {
            shared.IncludeWeatherOutlook = !shared.IncludeWeatherOutlook;
            viewModel.IncludeWeatherOutlook = !viewModel.IncludeWeatherOutlook;
        }

        shared.IncludeWeatherOutlook = false;
        await shared.PendingSave;

        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.False(rig.Preferences.Current.IncludeWeatherOutlook);
        Assert.Equal(11, rig.Preferences.SaveCount); // exactly one save per change, none echoed back
    }

    [Fact]
    public async Task Phase37D_Preference_IsRetainedAfterRestart_InTheOneExistingFile()
    {
        var folder = NewTempFolder();
        try
        {
            var path = Path.Combine(folder, "passdown-weather-outlook.json");
            var (_, _, shared) = await SharedAsync(new FilePassdownWeatherOutlookPreferenceStore(path));
            shared.IncludeWeatherOutlook = false;
            Assert.True(await shared.PendingSave);

            // "Restart": a new shared instance and Passdown over a new store for the same file.
            var (_, viewModel, restarted) = await SharedAsync(new FilePassdownWeatherOutlookPreferenceStore(path));
            Assert.False(restarted.IncludeWeatherOutlook);
            Assert.False(viewModel.IncludeWeatherOutlook);

            // No second, competing preference file.
            Assert.Equal(new[] { "passdown-weather-outlook.json" }, Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Phase37D_SaveFailure_IsReportedHonestly_AndTheChoiceStillAppliesThisSession()
    {
        var store = new FailingPreferenceStore();
        var (_, viewModel, shared) = await SharedAsync(store);

        shared.IncludeWeatherOutlook = false;
        Assert.False(await shared.PendingSave);

        Assert.Equal(PassdownWeatherOutlookSettingsViewModel.SaveFailedMessage, shared.SaveMessage);
        Assert.False(shared.IncludeWeatherOutlook); // never shown as saved, never silently reverted
        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.True(store.Current.IncludeWeatherOutlook);

        store.Fail = false;
        viewModel.IncludeWeatherOutlook = true;
        Assert.True(await shared.PendingSave);
        Assert.Null(shared.SaveMessage);
        Assert.True(store.Current.IncludeWeatherOutlook);
    }

    [Fact]
    public async Task Phase37D_AChoiceMadeBeforeTheFileIsRead_WinsOverTheOlderSavedValue()
    {
        var store = new HeldPreferenceStore(saved: true);
        var shared = new PassdownWeatherOutlookSettingsViewModel(store);
        Assert.False(shared.IsLoaded);

        shared.IncludeWeatherOutlook = false;
        store.Gate.SetResult();
        await shared.PendingLoad;

        Assert.True(shared.IsLoaded);
        Assert.False(shared.IncludeWeatherOutlook);
    }

    [Fact]
    public async Task Phase37D_SharedPreferenceLoad_IsAwaitedBeforeTheFirstPreview()
    {
        var store = new HeldPreferenceStore(saved: false);
        var rig = new Rig { Shared = new PassdownWeatherOutlookSettingsViewModel(store) };
        rig.Monitor.Current = Snapshot(71);
        var viewModel = rig.Create();
        await viewModel.PendingLoad;
        await rig.SettleAsync(viewModel);

        Assert.Empty(rig.Export.Calls); // nothing generated with the default while the saved choice is unknown

        store.Gate.SetResult();
        await viewModel.PendingWeatherOutlookPreferenceLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await rig.SettleAsync(viewModel);

        Assert.False(viewModel.IncludeWeatherOutlook);
        Assert.Single(rig.Export.Calls);
        Assert.Null(rig.Export.Calls[0].Weather);
    }

    [Fact]
    public void Phase37D_Preference_IsNeverDraftOrSnapshotData()
    {
        foreach (var type in new[] { typeof(AssociateDraftPayload), typeof(Carowinds.Passdown.Application.Finalization.FinalizedPassdownSnapshot) })
        {
            Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Weather", StringComparison.OrdinalIgnoreCase));
        }
    }
}
