using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Tests.Updates;

/// <summary>Phase 23D: the new version confirms its launch and only a recorded success counts.</summary>
public class PostUpdateLaunchTests
{
    private sealed class FakeOutcomeStore : IUpdateOutcomeStore
    {
        public UpdateInstallOutcome? Latest { get; set; }

        public List<Guid> Confirmed { get; } = [];

        public Func<UpdateInstallOutcome, UpdateInstallOutcome>? AfterConfirm { get; set; }

        public UpdateInstallOutcome? LoadLatest() => Latest;

        public bool ConfirmLaunch(Guid requestId, SemanticVersion runningVersion)
        {
            Confirmed.Add(requestId);
            if (AfterConfirm is not null && Latest is not null)
            {
                Latest = AfterConfirm(Latest);
            }

            return true;
        }
    }

    private static readonly SemanticVersion Running = SemanticVersion.Parse("0.1.0-rc.8");

    private static UpdateInstallOutcome Awaiting(string to = "0.1.0-rc.8") =>
        new(Guid.NewGuid(), "0.1.0-rc.7", to, UpdateInstallStatus.AwaitingLaunchConfirmation, null, DateTimeOffset.UtcNow, "backup");

    [Fact]
    public async Task AwaitingConfirmation_IsConfirmed_AndTheRecordedSuccessIsReturned()
    {
        var store = new FakeOutcomeStore { Latest = Awaiting() };
        store.AfterConfirm = o => o with { Status = UpdateInstallStatus.Succeeded };
        var launch = new PostUpdateLaunch(store, Running, (_, _) => Task.CompletedTask);

        var outcome = await launch.ConfirmAsync();

        Assert.Single(store.Confirmed);
        Assert.Equal(UpdateInstallStatus.Succeeded, outcome!.Status);
        Assert.True(launch.ShouldShowWhatsNew(outcome, null));
    }

    [Fact]
    public async Task UpdaterNeverRecordsSuccess_WhatsNewDoesNotOpen()
    {
        var store = new FakeOutcomeStore { Latest = Awaiting() };
        var launch = new PostUpdateLaunch(store, Running, (_, _) => Task.CompletedTask);

        var outcome = await launch.ConfirmAsync();

        Assert.Equal(UpdateInstallStatus.AwaitingLaunchConfirmation, outcome!.Status);
        Assert.False(launch.ShouldShowWhatsNew(outcome, null));
    }

    [Fact]
    public async Task OrdinaryRestart_ConfirmsNothing()
    {
        var store = new FakeOutcomeStore { Latest = Awaiting() with { Status = UpdateInstallStatus.Succeeded } };
        var launch = new PostUpdateLaunch(store, Running, (_, _) => Task.CompletedTask);
        await launch.ConfirmAsync();
        Assert.Empty(store.Confirmed);
    }

    [Fact]
    public async Task OldVersionRunningAfterRollback_NeverConfirmsTheNewVersion()
    {
        var store = new FakeOutcomeStore { Latest = Awaiting("0.1.0-rc.9") };
        var launch = new PostUpdateLaunch(store, Running, (_, _) => Task.CompletedTask);
        await launch.ConfirmAsync();
        Assert.Empty(store.Confirmed);
    }
}
