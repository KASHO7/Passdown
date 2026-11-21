using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Promotions;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 31B presentation: Price History availability wording, the Products rows' Important Details &amp;
/// Deadlines section, the editable ERT conflict resolution, and the view markup bindings. View-model and
/// markup assertions only (no real Window; bound text is asserted through view models, never the view).
/// </summary>
public class Phase31BProductDetailsAndSyncUiTests
{
    private static readonly DateTimeOffset Verified = new(2026, 11, 21, 16, 0, 0, TimeSpan.FromHours(-5));

    // ----- Price History availability -----

    private static PriceHistoryProductOption Option(ProductAvailability availability, bool isActive, string reason = "reason") =>
        new(new PriceHistoryProduct("MilitaryFirstResponder:id:fallmilitarygoldsp2027", "2027 Military Gold Pass", PriceHistoryFamily.SeasonPass, isActive, 1)
        {
            Availability = new ProductAvailabilityAssessment(availability, reason),
        });

    [Fact]
    public void AnUnconfirmedMilitaryPass_ReadsAvailabilityUnconfirmed_NeverRetired()
    {
        var option = Option(ProductAvailability.Unconfirmed, isActive: true, reason: "Sold through the official ID.me-verified flow.");

        Assert.Equal("Season Pass · Availability unconfirmed", option.DetailText);
        Assert.DoesNotContain("Retired", option.DetailText, StringComparison.Ordinal);
        Assert.True(option.IsAvailabilityNoteVisible);
        Assert.Equal("Sold through the official ID.me-verified flow.", option.AvailabilityReason);
    }

    [Fact]
    public void RetiredExpiredAndAvailable_ReadAsBefore()
    {
        Assert.Equal("Season Pass · Retired", Option(ProductAvailability.Retired, isActive: false).DetailText);
        Assert.Equal("Season Pass · Expired", Option(ProductAvailability.Expired, isActive: false).DetailText);
        Assert.Equal("Season Pass", Option(ProductAvailability.Available, isActive: true).DetailText);
        Assert.False(Option(ProductAvailability.Available, isActive: true).IsAvailabilityNoteVisible);
    }

    [Fact]
    public void TheTimingNote_ExplainsHowFirstSeenAndLastConfirmedAreDerivedAcrossComputers()
    {
        Assert.Contains("on any of your computers", PriceHistoryPresentation.TimingNoteText, StringComparison.Ordinal);
        Assert.Contains("\"Last confirmed\" is the latest check", PriceHistoryPresentation.TimingNoteText, StringComparison.Ordinal);
    }

    [Fact]
    public void AConcurrentObservationRow_IsLabeled_SoItNeverReadsAsAConfirmedChange()
    {
        PriceHistoryEntry Entry(int sequence, decimal amount, PriceKind kind, bool overlaps, SeasonPriceChange? change) =>
            new(new Application.Persistence.SeasonPriceHistoryInterval("k", sequence, amount, kind, null, null, "Gold", "src",
                Verified, Verified.AddDays(1), Application.Persistence.SeasonPriceHistoryOrigin.Observed), change) { OverlapsEarlierPeriod = overlaps };

        var rows = PriceHistoryPresentation.BuildRows(
        [
            Entry(1, 99m, PriceKind.Sale, false, null),
            Entry(2, 99m, PriceKind.StandardCatalog, true, SeasonPriceChange.Between(new SeasonPriceState(99m, PriceKind.Sale), new SeasonPriceState(99m, PriceKind.StandardCatalog))),
        ]);

        Assert.Equal(PriceHistoryPresentation.ConcurrentObservationNoteText, rows[1].NoteText);
    }

    // ----- Important Details & Deadlines -----

    private static readonly OfficialDateStatement BannerEnd = new(new DateOnly(2026, 11, 27), "Hurry! Black Friday Deal Ends 11/27", DeadlineSource.CampaignBanner, YearInferred: true);
    private static readonly OfficialDateStatement UseBy = new(new DateOnly(2027, 1, 3), "Use by 1/3/27.", DeadlineSource.OfferTerms);

    private static ProductImportantDetails GoldDetails(DateOnly today) =>
        new ProductImportantDetailsIndex(
            new Dictionary<OfficialProductChannel, (OfficialProductDetailsDocument, OfficialDocumentStatus)>
            {
                [OfficialProductChannel.SeasonPasses] = (new OfficialProductDetailsDocument(
                [
                    new OfficialProductDetails
                    {
                        DetailsId = "details:SeasonPasses:gold",
                        Title = "Gold Tier",
                        Channel = OfficialProductChannel.SeasonPasses,
                        TierName = "Gold",
                        PrimarySourceUrl = new Uri("https://www.sixflags.com/carowinds/memberships-and-season-passes"),
                        FirstSeenAt = Verified,
                        LastVerifiedAt = Verified,
                        Statements =
                        [
                            new ProductDetailStatement { Kind = ProductDetailKind.Fee, Text = "Plus processing fee of up to $9.99 & applicable taxes." },
                            new ProductDetailStatement
                            {
                                Kind = ProductDetailKind.BringAFriend,
                                Text = "Buy during the Black Friday Deal and get a FREE Bring-A-Friend ticket. Use by 1/3/27.",
                                PurchaseDeadline = BannerEnd,
                                RedemptionDeadline = UseBy,
                            },
                        ],
                    },
                ]), new OfficialDocumentStatus(OfficialDocumentFreshness.Current, Verified, Verified, null, null)),
            },
            today, Verified.AddHours(1))
            .Find(GoldPassProduct())!;

    private static AssociateProduct GoldPassProduct() =>
        new("catalog|" + CatalogProductKey.FromPassOffer(PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027)), "Gold Season Pass", SourceCategory.SeasonPasses,
            false, 99m, "$99", 115m, "Only", false, null, AssociateVerificationInfo.NotCheckedYet, true, null, PriceKind.StandardCatalog)
        { PassOffer = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027) };

    [Fact]
    public void TheSection_SummarizesTheNextDeadline_WithPurchaseAndRedemptionLabeledSeparately()
    {
        var presentation = ProductImportantDetailsPresentation.Build(GoldDetails(new DateOnly(2026, 11, 21)));

        Assert.Equal("Important Details & Deadlines", presentation.Header);
        Assert.Equal("Buy by November 27, 2026 (campaign end date) — year inferred · Use by January 3, 2027", presentation.SummaryText);
        var perk = presentation.Lines[0];
        Assert.Equal("Bring-A-Friend", perk.KindLabel);
        Assert.Equal("Current", perk.StatusLabel);
        Assert.False(perk.IsNotCurrent);
        Assert.Contains("Bring-A-Friend, Current:", perk.AutomationName, StringComparison.Ordinal);
        Assert.Contains("Memberships & Season Passes", presentation.ProvenanceText, StringComparison.Ordinal);
        Assert.Contains("Last verified", presentation.ProvenanceText, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterThePurchaseDeadline_ThePerkIsMarkedExpired_AndTheSummaryNoLongerOffersIt()
    {
        var presentation = ProductImportantDetailsPresentation.Build(GoldDetails(new DateOnly(2026, 11, 28)));

        var perk = presentation.Lines.Single(l => l.KindLabel == "Bring-A-Friend");
        Assert.Equal("Expired", perk.StatusLabel);
        Assert.True(perk.IsExpired);
        Assert.Contains("already-earned benefits are usable through January 3, 2027", perk.StatusReason, StringComparison.Ordinal);
        Assert.Null(presentation.SummaryText);
    }

    [Fact]
    public void AProductRow_ShowsTheSectionOnlyWhenOfficialDetailsMatch_AndChangesNothingElse()
    {
        var row = new ProductRowViewModel(GoldPassProduct(), () => { });
        Assert.False(row.HasImportantDetails);

        row.ApplyImportantDetails(GoldDetails(new DateOnly(2026, 11, 21)), linkLauncher: null);

        Assert.True(row.HasImportantDetails);
        Assert.Equal(2, row.ImportantDetails!.Lines.Count);
        Assert.True(row.IncludeOnPdf);
        Assert.Null(row.DisplayPriceOverride);

        row.ApplyImportantDetails(null, linkLauncher: null);
        Assert.False(row.HasImportantDetails);
        Assert.False(row.HasImportantDetailsSourceLink);
    }

    // ----- ERT conflict editing -----

    private static readonly SyncConflictSummary ErtSummary = new(
        SyncEntityType.MonthlyErt, SyncEntityIdentity.ForMonthlyErt(2026, 11), "November 2026 Early Ride Time",
        false, false, Guid.Parse("bbbbbbbb-3131-0000-0000-000000000002"), 2, Verified, Verified, false);

    private static ErtConflictDetail ErtDetail(TimeOnly remoteStart) => new(
        SyncEntityType.MonthlyErt, ErtSummary.EntityId,
        new ErtSelectionSnapshot(ErtConfigurationState.Configured, new TimeOnly(9, 0), ["Copperhead Strike", "Mountain Gliders"], null),
        new ErtSelectionSnapshot(ErtConfigurationState.Configured, remoteStart, ["Fury 325", "Copperhead Strike"], null),
        false, false, ErtSummary.RemoteDeviceId, Verified, false);

    [Fact]
    public async Task OpeningAnErtConflict_ShowsTheComparison_AndPrefillsTheMergedRideList()
    {
        var service = new FakePersonalSyncConflictResolutionService { NextErtDetail = ErtDetail(new TimeOnly(9, 0)) };
        var viewModel = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), service, new FakePersonalSyncCoordinator());

        await viewModel.OpenConflictCommand.ExecuteAsync(ErtSummary);

        Assert.NotNull(viewModel.SelectedConflictDetail);
        Assert.True(viewModel.SelectedConflictDetail!.IsErtConflict);
        Assert.True(viewModel.SelectedConflictDetail.CanEditErtRides);
        Assert.False(viewModel.SelectedConflictDetail.ShowsErtBaseChoice);
        Assert.Equal(string.Join(Environment.NewLine, ["Copperhead Strike", "Mountain Gliders", "Fury 325"]), viewModel.EditedErtRidesText);
        Assert.True(viewModel.SaveEditedErtRidesCommand.CanExecute(null));
    }

    [Fact]
    public async Task SavingEditedRides_SendsTheEditedListAndChosenBase_ThenClosesTheResolvedConflict()
    {
        var service = new FakePersonalSyncConflictResolutionService { NextErtDetail = ErtDetail(new TimeOnly(9, 30)) };
        var viewModel = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), service, new FakePersonalSyncCoordinator());
        await viewModel.OpenConflictCommand.ExecuteAsync(ErtSummary);
        Assert.True(viewModel.SelectedConflictDetail!.ShowsErtBaseChoice);

        viewModel.EditedErtRidesText = "Fury 325\nCopperhead Strike, Mountain Gliders\n\n";
        viewModel.ErtBaseIsOtherDevice = true;
        await viewModel.SaveEditedErtRidesCommand.ExecuteAsync(null);

        Assert.Equal("EditedErt", service.LastResolutionKind);
        Assert.Equal(ErtResolutionBase.OtherDevice, service.LastErtResolution!.BaseSide);
        Assert.Equal(["Fury 325", "Copperhead Strike", "Mountain Gliders"], service.LastErtResolution.Rides);
        Assert.Null(viewModel.SelectedConflictDetail);
    }

    [Fact]
    public async Task AFailedEditedResolution_KeepsTheConflictOpen_WithTheServicesMessage()
    {
        var service = new FakePersonalSyncConflictResolutionService
        {
            NextErtDetail = ErtDetail(new TimeOnly(9, 0)),
            NextResolution = ConflictResolutionResult.ExportUnavailable(),
        };
        var viewModel = new PersonalSyncSettingsViewModel(new FakePersonalSyncSettingsService(), new FakeSyncFolderPicker(), service, new FakePersonalSyncCoordinator());
        await viewModel.OpenConflictCommand.ExecuteAsync(ErtSummary);

        await viewModel.SaveEditedErtRidesCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedConflictDetail);
        Assert.Equal(ConflictResolutionResult.ExportUnavailable().Message, viewModel.ConflictActionMessage);
    }

    // ----- markup -----

    private static string Markup(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void ProductRows_DeclareTheExpandableImportantDetailsSection()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/AssociateView.axaml");

        Assert.Contains("IsVisible=\"{Binding HasImportantDetails}\"", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding ImportantDetails.SummaryText}", markup, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ImportantDetails.Lines}\"", markup, StringComparison.Ordinal);
        Assert.Contains("<DataTemplate DataType=\"presentation:ProductDetailLinePresentation\"", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding StatusLabel}", markup, StringComparison.Ordinal);
        Assert.Contains("Important details and deadlines for {0}", markup, StringComparison.Ordinal);
        Assert.Contains("{Binding ImportantDetails.ProvenanceText}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConflictPanel_DeclaresTheMergeOrEditRidesControls()
    {
        var markup = Markup("src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml");

        Assert.Contains("{Binding SelectedConflictDetail.CanEditErtRides}", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EditedErtRidesText}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveEditedErtRidesCommand}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Keep This Device\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Use Other Device\"", markup, StringComparison.Ordinal);
    }
}
