using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Records every <see cref="Build"/> call (spec Phase 9 Part U's "no network source is invoked during snapshot creation" is provable by construction here — this fake never touches HTTP/Playwright/filesystem, only the in-memory <see cref="AssociateWorkspace"/> it is handed) so a finalization-service test can assert how many times, and with what workspace, building actually happened.</summary>
internal sealed class FakeFinalizedPassdownSnapshotBuilder : IFinalizedPassdownSnapshotBuilder
{
    public int CallCount { get; private set; }

    public List<AssociateWorkspace> ReceivedWorkspaces { get; } = [];

    public FinalizedPassdownSnapshot Build(AssociateWorkspace workspace, DateTimeOffset asOf)
    {
        CallCount++;
        ReceivedWorkspaces.Add(workspace);

        return new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion,
            workspace.OperatingDate,
            workspace.OperatingDate.ToString("yyyy-MM-dd"),
            new FinalizedParkHoursSnapshot(FinalizedParkHoursStatus.NotCheckedYet, "Hours not checked yet", null, null, null, null, null),
            new FinalizedAttendanceSnapshot(workspace.Draft.ProjectedAttendance, workspace.Draft.PreSolds),
            new FinalizedEarlyRideTimeSnapshot(false, null, []),
            new FinalizedDailyFeatureSnapshot(null, null),
            workspace.Draft.OperationalNotes.ToList(),
            [],
            HasUnverifiedProductPricing: false,
            GeneratedAtText: asOf.ToString("O"));
    }
}
