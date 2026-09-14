using System.Text;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>Records every <see cref="Build"/>/<see cref="BuildProjection"/> call (spec Phase 9 Part U's "no network source is invoked during snapshot creation" is provable by construction here — this fake never touches HTTP/Playwright/filesystem, only the in-memory <see cref="AssociateWorkspace"/> it is handed) so a finalization-service test can assert how many times, and with what workspace, building actually happened. <see cref="ThrowOnBuildProjection"/> lets a test simulate a Phase 9C frozen-PDF generation failure (spec Part I) without depending on the Documents project's real PDF generator. <see cref="ReceivedTemplates"/> (Phase 17C) records which template each <see cref="BuildProjection"/> call was asked to freeze with.</summary>
internal sealed class FakeFinalizedPassdownSnapshotBuilder : IFinalizedPassdownSnapshotBuilder
{
    public int CallCount { get; private set; }

    public List<AssociateWorkspace> ReceivedWorkspaces { get; } = [];

    public List<AssociatePassdownTemplate> ReceivedTemplates { get; } = [];

    /// <summary>When set, <see cref="BuildProjection"/> throws instead of returning — simulates a frozen-PDF generation failure (spec Phase 9C Part I).</summary>
    public bool ThrowOnBuildProjection { get; set; }

    public FinalizedPassdownSnapshot Build(AssociateWorkspace workspace, DateTimeOffset asOf)
    {
        CallCount++;
        ReceivedWorkspaces.Add(workspace);

        return BuildSnapshot(workspace, asOf);
    }

    public FinalizedPassdownProjection BuildProjection(AssociateWorkspace workspace, DateTimeOffset asOf, AssociatePassdownTemplate template = AssociatePassdownTemplate.Classic)
    {
        CallCount++;
        ReceivedWorkspaces.Add(workspace);
        ReceivedTemplates.Add(template);

        if (ThrowOnBuildProjection)
        {
            throw new InvalidOperationException("Simulated frozen PDF generation failure.");
        }

        var snapshot = BuildSnapshot(workspace, asOf);
        var fakePdfBytes = Encoding.UTF8.GetBytes($"FAKE-PDF:{workspace.OperatingDate:yyyy-MM-dd}:{asOf:O}:{template}");
        return new FinalizedPassdownProjection(snapshot, fakePdfBytes);
    }

    private static FinalizedPassdownSnapshot BuildSnapshot(AssociateWorkspace workspace, DateTimeOffset asOf) =>
        new(
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
