using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Documents.Associate;

/// <summary>
/// Phase 34B-2: a small, obviously synthetic Passdown used only to warm up the PDF engine in the background
/// after the window opens. It exercises the same production path as a real document (font registration,
/// the selected template's renderer, QuestPDF layout) so the first real preview, Create PDF or Print does
/// not pay that one-time cost. It contains no operational data, is generated in memory only, and its bytes
/// are discarded: never displayed, saved, printed, synced or written anywhere.
/// </summary>
public static class AssociatePassdownPdfWarmUpDocument
{
    private const string Placeholder = "Warm-up";

    public static AssociatePassdownDocumentModel Create()
    {
        PdfProductRow[] products =
        [
            new(Placeholder, "$1.00", "$2.00", null, null),
            new(Placeholder, "$1.00", null, Placeholder, Placeholder),
        ];

        return new AssociatePassdownDocumentModel(
            OperatingDate: new DateOnly(2000, 1, 1),
            OperatingDateText: Placeholder,
            ParkHours: new PdfParkHours(PdfParkHoursStatus.Open, Placeholder, Placeholder, Placeholder, Placeholder, Placeholder, PrivateBuyoutDetails: null),
            Attendance: new PdfAttendance(ProjectedAttendance: 1, PreSolds: 1),
            EarlyRideTime: new PdfEarlyRideTime(EffectiveErtKind.NotConfigured, Source: null, StartTimeText: null, Rides: [], Note: null),
            DailyFeature: new PdfDailyFeature(Placeholder, Placeholder),
            OperationalNotes: [Placeholder],
            ProductCategories:
            [
                new PdfProductCategory(Placeholder, ProductDisplaySection.DailyAdmission, PdfProductSectionTier.KeySelling, products),
                new PdfProductCategory(Placeholder, Section: null, PdfProductSectionTier.Reference, products),
            ],
            HasUnverifiedProductPricing: false,
            GeneratedAtText: Placeholder)
        {
            Shows = [new PdfShow(Placeholder, Placeholder, Placeholder, Placeholder)],
        };
    }
}
