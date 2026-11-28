using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Pre-RC.15 post-closing pricing retention (docs/pre-rc15-post-closing-pricing-retention.md): the
/// presentation wording for prices verified earlier for the selected date whose sales are no longer
/// listed. View-model and presentation level only — no real Window (mixed Ui partition).
/// </summary>
public class PostClosingPricingRetentionUiTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    /// <summary>3:10 PM EST.</summary>
    private static readonly DateTimeOffset VerifiedAt = new(2026, 11, 27, 20, 10, 0, TimeSpan.Zero);

    /// <summary>10:30 PM EST.</summary>
    private static readonly DateTimeOffset AfterClosing = new(2026, 11, 28, 3, 30, 0, TimeSpan.Zero);

    private static AssociateProduct Product(AssociateVerificationState state, DateTimeOffset? verifiedAt, decimal? displayOverride = null) => new(
        AssociateProductIdentity.ForDatedProduct(SourceCategory.DatedAdmissionTickets, "park-1"),
        "Park Ticket",
        SourceCategory.DatedAdmissionTickets,
        IsCustom: false,
        64.99m,
        null,
        null,
        null,
        DateSpecific: true,
        Today,
        new AssociateVerificationInfo(state, verifiedAt, TimeSpan.FromHours(7), "Not available for this date."),
        IncludeOnPdf: true,
        displayOverride,
        PriceKind: PriceKind.ExactDated);

    [Fact]
    public void ProductRow_PreviouslyVerified_ShowsTheOriginalTime_AndThatAvailabilityIsNotConfirmed()
    {
        var row = new ProductRowViewModel(Product(AssociateVerificationState.PreviouslyVerified, VerifiedAt), () => { }, openPriceEditor: _ => { });

        Assert.Equal("Last verified Nov 27 at 3:10 PM ET — sales availability not confirmed.", row.VerificationNoteText);
        Assert.Equal("$64.99", row.OfficialPriceText);
        Assert.True(row.CanEditPrice);
    }

    [Fact]
    public void ProductRow_PreviouslyVerified_WithAnOverride_KeepsTheOverrideAsTheEffectivePrice()
    {
        var row = new ProductRowViewModel(Product(AssociateVerificationState.PreviouslyVerified, VerifiedAt, displayOverride: 59m), () => { });

        Assert.True(row.HasOverride);
        Assert.Equal("$59.00", row.EffectivePriceText);
        Assert.Equal("$64.99", row.OfficialPriceText);
        Assert.NotNull(row.VerificationNoteText);
    }

    [Theory]
    [InlineData(AssociateVerificationState.Verified, null)]
    [InlineData(AssociateVerificationState.Stale, "Stale — showing the last verified price.")]
    [InlineData(AssociateVerificationState.Error, "Could not verify current pricing.")]
    public void ProductRow_OtherStates_KeepTheirExistingNotes(AssociateVerificationState state, string? expected)
    {
        var row = new ProductRowViewModel(Product(state, VerifiedAt), () => { });

        Assert.Equal(expected, row.VerificationNoteText);
    }

    [Fact]
    public void CategoryStatus_PreviouslyVerified_WithNoRows_SaysNoLongerListed()
    {
        var status = new AssociateProductCategoryStatus(SourceCategory.DatedFastLane, AssociateVerificationState.PreviouslyVerified, AfterClosing, "Not available for this date.");

        var presentation = CategoryStatusPresentation.Build(status, productRowCount: 0);

        Assert.Equal("No longer listed for this date", presentation!.StatusText);
        Assert.Null(CategoryStatusPresentation.Build(status, productRowCount: 2)); // rows carry their own note
    }

    [Fact]
    public void DashboardAdmission_PreviouslyVerified_IsMarked_AndNeverSaysVerifiedAsCurrent()
    {
        var summary = Phase22CDashboardFixtures.ExactAdmission(Today, VerifiedAt) with { PriceState = AssociateVerificationState.PreviouslyVerified };

        var p = DashboardAdmissionPresentation.Build(summary, AfterClosing);

        Assert.True(p.IsStale);
        Assert.True(p.HasPrice);
        Assert.StartsWith("Last verified", p.FreshnessText);
        Assert.EndsWith("— availability not confirmed", p.FreshnessText);
        Assert.Contains("availability not confirmed", p.AutomationText);
    }

    [Fact]
    public void DashboardPricing_PreviouslyVerifiedRows_AreMarked_WithTheirOwnWording()
    {
        var summary = new DashboardPricingSummary(
        [
            new DashboardPriceItem("Fast Lane Reserve", 49m, ProductDisplaySection.FastLane, AssociateVerificationState.PreviouslyVerified, VerifiedAt),
            new DashboardPriceItem("Fast Lane Ultimate", 85m, ProductDisplaySection.FastLane, AssociateVerificationState.PreviouslyVerified, VerifiedAt),
        ], 2);

        var p = DashboardPricingPresentation.Build(summary, AfterClosing);

        Assert.True(p.IsStale);
        Assert.All(p.Rows, r => Assert.True(r.IsStale));
        Assert.Equal("Some prices are no longer listed — availability not confirmed", p.FreshnessText);
    }

    [Fact]
    public void DashboardPricing_AFailedRecheck_KeepsTheExistingWording()
    {
        var summary = new DashboardPricingSummary(
        [
            new DashboardPriceItem("Fast Lane Reserve", 49m, ProductDisplaySection.FastLane, AssociateVerificationState.PreviouslyVerified, VerifiedAt),
            new DashboardPriceItem("Park Ticket", 64.99m, ProductDisplaySection.DailyAdmission, AssociateVerificationState.Stale, VerifiedAt),
        ], 2);

        var p = DashboardPricingPresentation.Build(summary, AfterClosing);

        Assert.Equal("Some prices could not be rechecked — may be out of date", p.FreshnessText);
    }
}
