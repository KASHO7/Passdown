using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.ScheduledRefresh;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Infrastructure;
using Carowinds.Passdown.Infrastructure.Diagnostics;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 18D launch-identity audit (docs/support-troubleshooting.md "Which copy is running?"):
/// the running build is identifiable from Settings → About this build, and the intentionally
/// legacy internal identities stay stable — renaming any of them would orphan existing data,
/// scheduled tasks, or sync folders (docs/release-strategy.md "Launch identity").
/// </summary>
public class LaunchIdentityTests
{
    private static PersonalSyncSettingsViewModel Settings(BuildIdentity identity) =>
        new(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator(),
            buildIdentity: identity);

    [Fact]
    public void DesktopAssembly_IdentifiesAsTheVisibleProduct_WithAReleaseVersion()
    {
        var identity = BuildIdentity.FromAssembly(typeof(Desktop.App).Assembly, null);

        Assert.Equal("Carowinds Operations", identity.ProductName);
        // A development version or a numbered release candidate (docs/release-strategy.md "Release
        // candidate versioning"); the "+<commit>" suffix is split off into SourceRevision.
        Assert.Matches(@"^\d+\.\d+\.\d+-(dev|rc\.\d+)$", identity.Version);
    }

    [Fact]
    public void AboutThisBuild_ShowsNameVersionCommitAndTheRunningFolder_WithoutTheUserName()
    {
        var settings = Settings(new BuildIdentity("Carowinds Operations", "0.1.0-dev", "b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2",
            @"C:\Users\Kim\Desktop\Carowinds-Operations-0.1.0-dev-win-x64\Carowinds.Passdown.Desktop.exe"));

        Assert.Equal("Carowinds Operations", settings.BuildProductName);
        Assert.Equal("0.1.0-dev (b2db3ed)", settings.BuildVersionText);
        Assert.Equal(@"%USERPROFILE%\Desktop\Carowinds-Operations-0.1.0-dev-win-x64\Carowinds.Passdown.Desktop.exe", settings.BuildExecutablePathText);
    }

    [Fact]
    public void AboutThisBuild_UnknownExecutablePath_SaysSo()
    {
        Assert.Equal("Unknown", Settings(new BuildIdentity("Carowinds Operations", "0.1.0-dev", null, null)).BuildExecutablePathText);
    }

    [Fact]
    public void AboutThisBuild_DefaultsToTheActuallyRunningProcess()
    {
        var settings = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), new FakePersonalSyncConflictResolutionService(), new FakePersonalSyncCoordinator());

        Assert.Equal(Environment.ProcessPath, settings.Build.ExecutablePath);
    }

    [Fact]
    public void LegacyInternalIdentities_RemainIntentionallyStable()
    {
        Assert.Equal("Carowinds.Passdown.Desktop", typeof(Desktop.App).Assembly.GetName().Name); // executable file name
        Assert.Equal("Carowinds Passdown", Path.GetFileName(AppPaths.LocalApplicationDataDirectory)); // %LOCALAPPDATA% data folder
        Assert.Equal("Carowinds Passdown Daily Refresh", ScheduledTaskPlan.TaskName);
        Assert.Equal(ScheduledTaskPlan.TaskName, ScheduledTaskStatusProvider.TaskName);
        Assert.Equal("Carowinds Passdown Personal Sync", SyncManifest.ProductName);
    }
}
