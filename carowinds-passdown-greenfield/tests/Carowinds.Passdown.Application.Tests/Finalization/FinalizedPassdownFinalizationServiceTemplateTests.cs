using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Application.Tests.Finalization;

/// <summary>
/// Phase 17C §18/§31: finalization must freeze whatever Associate Passdown template was selected at
/// the exact moment it runs, and never re-derive or change that choice later — this is the
/// "finalize under Compact, then switch the preference to Modern, historical PDF stays Compact"
/// guarantee spec §31 calls "critical".
/// </summary>
public class FinalizedPassdownFinalizationServiceTemplateTests
{
    private static readonly DateOnly Date = new(2026, 8, 25);
    private static readonly DateTimeOffset FinalizedAt = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    private static AssociateWorkspace Workspace(DateOnly date) => new(
        date,
        AssociateDraftPayload.CreateDefault() with { ProjectedAttendance = 18500 },
        new AssociateParkHoursView(
            ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(21, 0), false, null, null, false, null, null),
            AssociateVerificationInfo.NotCheckedYet,
            OfficialIsClosed: false,
            OfficialOpening: new TimeOnly(10, 0),
            OfficialClosing: new TimeOnly(21, 0)),
        Products: [],
        CategoryStatuses: [],
        AssociateSaveStatus.NeverSaved);

    [Fact]
    public async Task FinalizeAsync_NoTemplateStoreRegistered_UsesClassic()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        var builder = new FakeFinalizedPassdownSnapshotBuilder();
        var service = new FinalizedPassdownFinalizationService(repository, builder);

        await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Scheduled, FinalizedAt);

        Assert.Equal([AssociatePassdownTemplate.Classic], builder.ReceivedTemplates);
    }

    [Fact]
    public async Task FinalizeAsync_ReadsTheCurrentTemplatePreferenceAtFinalizationTime()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        var builder = new FakeFinalizedPassdownSnapshotBuilder();
        var templateStore = new FakeAssociatePassdownTemplatePreferenceStore { Current = new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Compact) };
        var service = new FinalizedPassdownFinalizationService(repository, builder, templateStore);

        await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Scheduled, FinalizedAt);

        Assert.Equal([AssociatePassdownTemplate.Compact], builder.ReceivedTemplates);
    }

    [Fact]
    public async Task FinalizeAsync_LaterPreferenceChange_NeverAltersTheAlreadyFinalizedRecord()
    {
        var repository = new InMemoryFinalizedPassdownRepository();
        var builder = new FakeFinalizedPassdownSnapshotBuilder();
        var templateStore = new FakeAssociatePassdownTemplatePreferenceStore { Current = new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Compact) };
        var service = new FinalizedPassdownFinalizationService(repository, builder, templateStore);

        var firstResult = await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Scheduled, FinalizedAt);
        var frozenPdfBytes = firstResult.FinalizedPassdown.PdfBytes;

        // Switch the live preference to Modern after finalization — spec §31's step 4.
        templateStore.Current = new AssociatePassdownTemplatePreference(AssociatePassdownTemplate.Modern);

        // A second finalization attempt for the same date is idempotent (existing Phase 9 behavior) —
        // it must not rebuild under the new preference, and the stored record must be untouched.
        var secondResult = await service.FinalizeAsync(Date, Workspace(Date), FinalizationOrigin.Scheduled, FinalizedAt);

        Assert.Equal(FinalizationOutcome.AlreadyFinalized, secondResult.Outcome);
        Assert.Equal([AssociatePassdownTemplate.Compact], builder.ReceivedTemplates);
        Assert.Equal(frozenPdfBytes, (await repository.GetByDateAsync(Date))!.PdfBytes);
    }
}
