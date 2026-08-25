using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// <see cref="IFinalizedPassdownSnapshotBuilder"/> built on the existing
/// <see cref="AssociatePassdownDocumentModelBuilder"/> (spec Phase 9 Part K) — the same print-ready
/// projection <see cref="AssociatePassdownPdfGenerator"/> renders, so a finalized snapshot can never
/// disagree with what the PDF actually showed. This class does no independent interpretation of the
/// workspace; it only 1:1-maps <see cref="AssociatePassdownDocumentModel"/>'s already-frozen display
/// values onto the Application-layer <see cref="FinalizedPassdownSnapshot"/> shape, so Application/
/// Infrastructure code that persists/serializes snapshots never needs to reference this project.
/// </summary>
public sealed class FinalizedPassdownSnapshotBuilder : IFinalizedPassdownSnapshotBuilder
{
    public FinalizedPassdownSnapshot Build(AssociateWorkspace workspace, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, asOf);

        if (model.OperatingDate is not { } passdownDate)
        {
            throw new InvalidOperationException(
                $"Cannot finalize a passdown whose document model has no {nameof(model.OperatingDate)}.");
        }

        return new FinalizedPassdownSnapshot(
            FinalizedPassdownSnapshot.CurrentSchemaVersion,
            passdownDate,
            model.OperatingDateText,
            MapParkHours(model.ParkHours),
            new FinalizedAttendanceSnapshot(model.Attendance.ProjectedAttendance, model.Attendance.PreSolds),
            MapEarlyRideTime(model.EarlyRideTime),
            new FinalizedDailyFeatureSnapshot(model.DailyFeature.TypeLabel, model.DailyFeature.Text),
            model.OperationalNotes.ToList(),
            model.ProductCategories.Select(MapCategory).ToList(),
            model.HasUnverifiedProductPricing,
            model.GeneratedAtText);
    }

    private static FinalizedParkHoursSnapshot MapParkHours(PdfParkHours hours) => new(
        MapStatus(hours.Status),
        hours.HeadlineText,
        hours.BadgeText,
        hours.NorthGateText,
        hours.AvenueText,
        hours.VerificationText,
        hours.PrivateBuyoutDetails);

    private static FinalizedParkHoursStatus MapStatus(PdfParkHoursStatus status) => status switch
    {
        PdfParkHoursStatus.Open => FinalizedParkHoursStatus.Open,
        PdfParkHoursStatus.Closed => FinalizedParkHoursStatus.Closed,
        _ => FinalizedParkHoursStatus.NotCheckedYet,
    };

    private static FinalizedEarlyRideTimeSnapshot MapEarlyRideTime(PdfEarlyRideTime ert) =>
        new(ert.Enabled, ert.Details, ert.ParticipatingAttractions.ToList());

    private static FinalizedProductCategorySnapshot MapCategory(PdfProductCategory category) => new(
        category.CategoryName,
        MapTier(category.Tier),
        category.Products.Select(MapProduct).ToList());

    private static FinalizedProductSectionTier MapTier(PdfProductSectionTier tier) => tier switch
    {
        PdfProductSectionTier.KeySelling => FinalizedProductSectionTier.KeySelling,
        PdfProductSectionTier.Promotional => FinalizedProductSectionTier.Promotional,
        _ => FinalizedProductSectionTier.Reference,
    };

    private static FinalizedProductRowSnapshot MapProduct(PdfProductRow product) => new(
        product.DisplayName,
        product.EffectivePriceText,
        product.ComparisonPriceText,
        product.Qualifier,
        product.Notes);
}
