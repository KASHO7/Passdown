using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

namespace Carowinds.Passdown.Documents.Tests.Associate;

/// <summary>
/// Covers <see cref="FinalizedPassdownSnapshotBuilder"/> (spec Phase 9 Parts C/D/K) against the same
/// <see cref="AssociateWorkspaceFixtures"/> sample days <see cref="AssociatePassdownDocumentModelBuilderTests"/>
/// and the PDF generator tests already use — proving the finalized snapshot reflects exactly what
/// the PDF would render for the same workspace, never a second independent interpretation.
/// </summary>
public class FinalizedPassdownSnapshotBuilderTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly FinalizedPassdownSnapshotBuilder Builder = new();

    [Fact]
    public void Build_NormalDay_PreservesEffectiveHours()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal(FinalizedParkHoursStatus.Open, snapshot.ParkHours.Status);
        Assert.Equal("9:00 AM - 9:00 PM", snapshot.ParkHours.HeadlineText);
    }

    [Fact]
    public void Build_PrivateBuyoutDay_PreservesBuyoutStateAndDetails()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.PrivateBuyoutDay(), AsOf);

        Assert.Equal(FinalizedParkHoursStatus.Open, snapshot.ParkHours.Status);
        Assert.Equal("Private Buyout", snapshot.ParkHours.BadgeText);
        Assert.Contains("Acme Corp", snapshot.ParkHours.PrivateBuyoutDetails);
    }

    [Fact]
    public void Build_NormalDay_PreservesAttendanceAndPreSolds()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal(18500, snapshot.Attendance.ProjectedAttendance);
        Assert.Equal(4200, snapshot.Attendance.PreSolds);
    }

    [Fact]
    public void Build_NormalDay_PreservesEarlyRideTimeStartRidesAndNote()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal(EffectiveErtKind.Configured, snapshot.EarlyRideTime.Kind);
        Assert.Equal(EffectiveErtSource.Monthly, snapshot.EarlyRideTime.Source);
        Assert.Equal("8:30 AM", snapshot.EarlyRideTime.StartTimeText);
        Assert.Equal(["Fury 325", "Intimidator", "Copperhead Strike"], snapshot.EarlyRideTime.Rides);
        Assert.Equal("For pass members with the benefit.", snapshot.EarlyRideTime.Note);

        // Legacy fields (pre-17B) are still populated for any older reader — never left at their
        // uninitialized defaults for a genuinely new-model row.
        Assert.True(snapshot.EarlyRideTime.Enabled);
        Assert.Equal(["Fury 325", "Intimidator", "Copperhead Strike"], snapshot.EarlyRideTime.ParticipatingAttractions);
    }

    // Phase 17B spec Part 14: FinalizedEarlyRideTimeSnapshot's new resolver-driven fields are an
    // additive shape change, so FinalizedPassdownSnapshot.CurrentSchemaVersion bumps 1 -> 2.
    [Fact]
    public void CurrentSchemaVersion_Is2_ForThePhase17BShapeChange()
    {
        Assert.Equal(2, FinalizedPassdownSnapshot.CurrentSchemaVersion);
    }

    // Phase 17B spec Part 31: park hours changing (even to Closed) never alters the frozen ERT.
    [Fact]
    public void Build_NormalDay_ErtStartIsIndependentOfNorthGate()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal("8:30 AM", snapshot.ParkHours.NorthGateText);
        Assert.Equal("8:30 AM", snapshot.EarlyRideTime.StartTimeText); // coincidentally equal in this fixture, not derived from one another.
    }

    // Private Buyout's own opening still drives the frozen North Gate time exactly like every other
    // effective-opening source (park-hours arithmetic, unrelated to Phase 17B's ERT changes).
    [Fact]
    public void Build_PrivateBuyoutDay_FreezesDerivedNorthGateFromBuyoutOpening()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.PrivateBuyoutDay(), AsOf);

        Assert.Equal("5:30 PM", snapshot.ParkHours.NorthGateText);
    }

    // Finalizing freezes park hours (North Gate/Avenue) computed at that moment — a later change to
    // what "live" hours would derive must never alter the already-built (frozen) snapshot. Unrelated
    // to Phase 17B's ERT changes; retained as a park-hours immutability regression test.
    [Fact]
    public void Build_LaterHoursChange_DoesNotAlterAnAlreadyBuiltSnapshot()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay(); // official opening 9:00 AM -> derived 8:30 AM
        var frozenSnapshot = Builder.Build(workspace, AsOf);
        Assert.Equal("8:30 AM", frozenSnapshot.ParkHours.NorthGateText);

        var effectiveOpening = ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(21, 0), false, null, null, false, null, null);
        var laterWorkspace = workspace with
        {
            ParkHours = workspace.ParkHours with { Effective = effectiveOpening },
        };
        var laterSnapshot = Builder.Build(laterWorkspace, AsOf);

        Assert.Equal("10:30 AM", laterSnapshot.ParkHours.NorthGateText);
        Assert.Equal("8:30 AM", frozenSnapshot.ParkHours.NorthGateText); // unchanged — History is immutable.
    }

    [Fact]
    public void Build_NormalDay_PreservesDailyFeature()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal("Question of the Day", snapshot.DailyFeature.TypeLabel);
        Assert.Equal("What year did Carowinds open?", snapshot.DailyFeature.Text);
    }

    [Fact]
    public void Build_NormalDay_PreservesOperationalNotesExactly()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();

        var snapshot = Builder.Build(workspace, AsOf);

        Assert.Equal(workspace.Draft.OperationalNotes, snapshot.OperationalNotes);
    }

    // Phase 11D Part L: the finalized snapshot represents a selected persistent note as part of the
    // existing generic OperationalNotes string list — no new structured field needed for this.
    [Fact]
    public void Build_SelectedPersistentNote_FreezesIntoOperationalNotesList()
    {
        var selection = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
            new DateOnly(2026, 8, 20), null, new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero));

        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [selection] } };

        var snapshot = Builder.Build(workspace, AsOf);

        Assert.Contains("Persistent: South Gate scanner #3 out of service — Direct guests to gate 2.", snapshot.OperationalNotes);
    }

    // Phase 11F Part M's required scenario, made explicit as its own regression test rather than left
    // to be inferred from separate service-level and builder-level tests: select a persistent note
    // (freezing its then-current text), the source note's text changes afterward, the supervisor does
    // NOT explicitly replace — the finalized snapshot must still show the OLD frozen text. The
    // "selection" here is constructed with the exact same field shape/order
    // AssociatePassdownService.SelectOperationalNoteAsync uses, without needing that service's full
    // repository/clock plumbing in this project.
    [Fact]
    public void Build_SelectedNoteSourceEditedWithoutReplace_SnapshotStillShowsFrozenOldText()
    {
        var frozenAtSelection = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "South Gate scanner #3 out of service", "Direct guests to gate 2.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
            new DateOnly(2026, 8, 20), null, new DateTimeOffset(2026, 8, 21, 6, 0, 0, TimeSpan.Zero));

        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [frozenAtSelection] } };

        // The source note's title/body changes after selection (e.g. an edit in Operational Notes
        // Manager) — modeled here simply as "the frozen selection object is never touched," since
        // nothing in this workspace holds a live reference back to a source OperationalNote at all.
        var snapshot = Builder.Build(workspace, AsOf);

        Assert.Contains("Persistent: South Gate scanner #3 out of service — Direct guests to gate 2.", snapshot.OperationalNotes);
        Assert.DoesNotContain(snapshot.OperationalNotes, note => note.Contains("scanner repaired", StringComparison.Ordinal));
    }

    // The explicit-Replace counterpart: once a supervisor does call "Replace with Current Version"
    // (AssociatePassdownService.ReplaceOperationalNoteWithCurrentAsync), the new selection overwrites
    // the old one in the draft, and finalizing from that point on must show the NEW text.
    [Fact]
    public void Build_SelectedNoteAfterExplicitReplace_SnapshotShowsNewText()
    {
        var replacedSelection = new AssociateOperationalNoteSelection(
            Guid.NewGuid(), "South Gate scanner #3 repaired", "Fully operational again.",
            OperationalNoteCategory.Equipment, OperationalNotePriority.Normal,
            new DateOnly(2026, 8, 20), null, new DateTimeOffset(2026, 8, 21, 7, 0, 0, TimeSpan.Zero));

        var workspace = AssociateWorkspaceFixtures.NormalDay();
        workspace = workspace with { Draft = workspace.Draft with { SelectedOperationalNotes = [replacedSelection] } };

        var snapshot = Builder.Build(workspace, AsOf);

        Assert.Contains("Persistent: South Gate scanner #3 repaired — Fully operational again.", snapshot.OperationalNotes);
        Assert.DoesNotContain(snapshot.OperationalNotes, note => note.Contains("out of service", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_NormalDay_FreezesProductDisplayNameAndPrice()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        var admission = snapshot.ProductCategories.Single(c => c.CategoryName == "Daily Admission");
        var oneDay = admission.Products.Single(p => p.DisplayName == "1-Day Ticket");
        Assert.Equal("$64.99", oneDay.EffectivePriceText);
    }

    [Fact]
    public void Build_NormalDay_FreezesComparisonPrice()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        var admission = snapshot.ProductCategories.Single(c => c.CategoryName == "Daily Admission");
        var oneDay = admission.Products.Single(p => p.DisplayName == "1-Day Ticket");
        Assert.Equal("$79.99", oneDay.ComparisonPriceText);
    }

    [Fact]
    public void Build_NormalDay_FreezesCustomProductNameAndNotes()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        var custom = snapshot.ProductCategories.Single(c => c.CategoryName == "Custom / Internal Items");
        var voucher = custom.Products.Single();
        Assert.Equal("Team Member Meal Voucher", voucher.DisplayName);
        Assert.Equal("$8.00", voucher.EffectivePriceText);
        Assert.Equal("Cafeteria only, associates present ID.", voucher.Notes);
    }

    [Fact]
    public void Build_LaterMutationOfSourceDraft_DoesNotMutateAnAlreadyBuiltSnapshot()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var snapshot = Builder.Build(workspace, AsOf);
        var originalNotes = snapshot.OperationalNotes.ToList();
        var originalRides = snapshot.EarlyRideTime.Rides!.ToList();

        // A record `with` expression never mutates the original `workspace`/`draft` in place — this
        // simulates a later monthly-ERT edit (a brand-new resolved EffectiveErt, exactly as
        // AssociatePassdownService.ComposeAsync would produce after IErtConfigurationService.SaveMonthlyAsync)
        // and rebuilds from that new state.
        var laterWorkspace = workspace with
        {
            Draft = workspace.Draft with
            {
                OperationalNotes = ["Completely different note."],
            },
            EarlyRideTime = new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(9, 0), ["Nighthawk"], "Different ERT note."),
        };
        Builder.Build(laterWorkspace, AsOf);

        Assert.Equal(originalNotes, snapshot.OperationalNotes);
        Assert.Equal(originalRides, snapshot.EarlyRideTime.Rides);
    }

    [Fact]
    public void Build_ClosedDayWithNoProducts_ProducesNoProductCategories()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.PrivateBuyoutDay(), AsOf);

        Assert.Empty(snapshot.ProductCategories);
    }

    [Fact]
    public void Build_SchemaVersionMatchesCurrentConstant()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.Equal(FinalizedPassdownSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
    }

    [Fact]
    public void Build_StaleDataDay_FlagsUnverifiedProductPricingFromVerification()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.StaleDataDay(), AsOf);

        Assert.True(snapshot.HasUnverifiedProductPricing);
    }

    [Fact]
    public void Build_ReflectsTheDocumentModelUsedForThePdf()
    {
        var workspace = AssociateWorkspaceFixtures.DenseDay();

        var expectedModel = AssociatePassdownDocumentModelBuilder.Build(workspace, AsOf);
        var snapshot = Builder.Build(workspace, AsOf);

        Assert.Equal(expectedModel.ProductCategories.Sum(c => c.Products.Count), snapshot.ProductCategories.Sum(c => c.Products.Count));
        Assert.Equal(expectedModel.OperatingDateText, snapshot.OperatingDateText);
        Assert.Equal(expectedModel.HasUnverifiedProductPricing, snapshot.HasUnverifiedProductPricing);
    }
}
