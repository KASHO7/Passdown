using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;

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
    public void Build_NormalDay_PreservesEarlyRideTimeDetailsAndAttractions()
    {
        var snapshot = Builder.Build(AssociateWorkspaceFixtures.NormalDay(), AsOf);

        Assert.True(snapshot.EarlyRideTime.Enabled);
        Assert.Contains("8:30 AM", snapshot.EarlyRideTime.Details);
        Assert.Equal(["Fury 325", "Intimidator", "Copperhead Strike"], snapshot.EarlyRideTime.ParticipatingAttractions);
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
    // existing generic OperationalNotes string list — no new structured field, so
    // FinalizedPassdownSnapshot.CurrentSchemaVersion correctly stays at 1 (asserted below).
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
        Assert.Equal(1, FinalizedPassdownSnapshot.CurrentSchemaVersion);
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
        var originalAttractions = snapshot.EarlyRideTime.ParticipatingAttractions.ToList();

        // A record `with` expression never mutates the original `workspace`/`draft` in place — this
        // simulates the next save producing a brand-new draft object, exactly as
        // AssociatePassdownService.SaveDraftAsync does, and rebuilds from that new state.
        var laterWorkspace = workspace with
        {
            Draft = workspace.Draft with
            {
                OperationalNotes = ["Completely different note."],
                EarlyRideTime = new EarlyRideTimeDetails("Different ERT details.", ["Nighthawk"]),
            },
        };
        Builder.Build(laterWorkspace, AsOf);

        Assert.Equal(originalNotes, snapshot.OperationalNotes);
        Assert.Equal(originalAttractions, snapshot.EarlyRideTime.ParticipatingAttractions);
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
