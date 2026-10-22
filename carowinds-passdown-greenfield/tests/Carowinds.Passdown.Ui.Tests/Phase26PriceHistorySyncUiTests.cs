using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Catalog;
using Carowinds.Passdown.Application.Changes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 26: Personal Sync wording for official price history, the Price History screen's in-place
/// refresh and provenance labels, and the clickable official source link. View-model/presentation
/// level plus markup-string checks (the mixed partition never asserts bound values from a live view).
/// </summary>
public class Phase26PriceHistorySyncUiTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);
    private static readonly Uri OfficialPage = new("https://www.sixflags.com/carowinds/memberships-and-season-passes");

    private static string Markup(string view) =>
        Regex.Replace(File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml")), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private sealed class RecordingLauncher(bool succeeds = true) : IExternalLinkLauncher
    {
        public List<Uri> Opened { get; } = [];

        public bool Throws { get; init; }

        public Task<bool> OpenAsync(Uri officialPage, CancellationToken cancellationToken = default)
        {
            if (Throws)
            {
                throw new InvalidOperationException("No browser.");
            }

            Opened.Add(officialPage);
            return Task.FromResult(succeeds);
        }
    }

    private sealed class Notifier : IDataChangeNotifier
    {
        public event EventHandler<DataChange>? Changed;

        public void Publish(DataChange change) => Changed?.Invoke(this, change);
    }

    // ----- Personal Sync wording -----

    [Fact]
    public void SyncResult_NamesMergedObservations_NeverPriceChanges()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 1, 2, 3, 0, 0, 0, null, PriceObservationsMergedCount: 28, IncludesOfficialPriceHistory: true);

        var text = PersonalSyncResultPresentation.Format(result);

        Assert.Equal("Synced: 1 imported, 2 exported, 3 unchanged. Official price history: 28 new observations merged.", text);
        Assert.DoesNotContain("price change", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyncResult_WithNothingNew_AddsNoPriceHistoryText_AndSkippedRecordsAreSaidPlainly()
    {
        Assert.Equal("Synced: 0 imported, 0 exported, 4 unchanged.",
            PersonalSyncResultPresentation.Format(new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 4, 0, 0, 0, null, IncludesOfficialPriceHistory: true)));

        var skipped = PersonalSyncResultPresentation.Format(new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null, PriceObservationsSkippedCount: 1));
        Assert.EndsWith("Official price history: 1 observation could not be verified and was skipped.", skipped);
    }

    [Fact]
    public void SettingsMarkup_SaysPersonalSyncIncludesVerifiedOfficialPriceHistory()
    {
        var markup = Markup("PersonalSyncSettingsView");

        Assert.Contains("Associate drafts, Operational Notes, finalized History, and verified official price history", markup);
        Assert.Contains("x:Name=\"OfficialPriceHistoryNote\"", markup);
        Assert.Contains("merge-only", markup);
        Assert.Contains("nothing is ever removed", markup);
        // Still one setup flow: no new folder picker, account or sign-in for price history.
        Assert.Single(Regex.Matches(markup, "ChooseFolderCommand"));
        Assert.DoesNotContain("Sign in", markup, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Price History presentation -----

    private static SeasonPriceHistoryInterval Interval(int sequence, decimal amount, DateTimeOffset first, PriceKind kind = PriceKind.StandardCatalog) =>
        new("gold-membership", sequence, amount, kind, null, null, "Gold Membership", "Carowinds Memberships & Season Passes", first, first.AddDays(30), SeasonPriceHistoryOrigin.Observed);

    [Fact]
    public void ImportedRows_SayWhereTheEvidenceCameFrom_LocalRowsStayUnlabeled()
    {
        var entries = new[]
        {
            new PriceHistoryEntry(Interval(1, 6m, At.AddMonths(-6)), null) { Provenance = PriceHistoryProvenance.ImportedThroughSync },
            new PriceHistoryEntry(Interval(2, 7m, At.AddMonths(-2)), new SeasonPriceChange(1m, 16.7m, true, false))
            {
                Provenance = PriceHistoryProvenance.CollectedHere | PriceHistoryProvenance.ImportedThroughSync,
            },
            new PriceHistoryEntry(Interval(3, 8m, At), new SeasonPriceChange(1m, 14.3m, true, false)),
        };

        var rows = PriceHistoryPresentation.BuildRows(entries, PassPriceUnit.PerMonth);

        Assert.Equal(["$6.00/month", "$7.00/month", "$8.00/month"], rows.Select(r => r.PriceText));
        Assert.Equal("+$1.00/month (+16.7%)", rows[1].ChangeText);
        Assert.Equal(PriceHistoryPresentation.ImportedProvenanceText, rows[0].ProvenanceText);
        Assert.Equal(PriceHistoryPresentation.MixedProvenanceText, rows[1].ProvenanceText);
        Assert.Null(rows[2].ProvenanceText);
        Assert.False(rows[2].HasProvenance);
        Assert.Contains("Imported through Personal Sync", rows[0].AutomationName);
        // Original observation dates, never the sync/arrival time.
        Assert.Equal(PriceHistoryPresentation.FormatDate(At.AddMonths(-6)), rows[0].FirstSeenText);
        Assert.DoesNotContain("checked", rows[0].ProvenanceText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportedOnlyProduct_ReadsImportedHistory_NotRetired()
    {
        var option = new PriceHistoryProductOption(new PriceHistoryProduct("k", "Gold Season Pass — 2026", PriceHistoryFamily.SeasonPass, false, 1) { IsImportedOnly = true });

        Assert.Equal("Season Pass · Imported history", option.DetailText);
        Assert.True(option.IsInactive);
    }

    [Fact]
    public void PriceHistoryMarkup_ShowsProvenanceAndAnAccessibleSourceLink()
    {
        var markup = Markup("PriceHistoryView");

        Assert.Contains("{Binding ProvenanceText}", markup);
        Assert.Contains("IsVisible=\"{Binding HasProvenance}\"", markup);
        AssertAccessibleLink(markup);
    }

    // ----- Price History refresh after a merge -----

    private static FakeSeasonPriceHistoryService Service() => new FakeSeasonPriceHistoryService()
        .With("gold-pass", "Gold Season Pass — 2027", active: true, PriceHistoryFamily.SeasonPass, (99m, PriceKind.StandardCatalog, At))
        .With("gold-membership", "Gold Membership", active: true, PriceHistoryFamily.Membership, (7m, PriceKind.StandardCatalog, At))
        .With("old-pass", "Gold Season Pass — 2026", active: false, PriceHistoryFamily.SeasonPass, (89m, PriceKind.StandardCatalog, At.AddYears(-1)));

    [Fact]
    public async Task MergedObservations_RefreshTheOpenScreen_KeepingSelectionAndFilters()
    {
        var service = Service();
        var notifier = new Notifier();
        var vm = new PriceHistoryViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());
        await vm.OpenAsync("gold-membership");
        vm.SelectedFamilyFilter = vm.FamilyFilters.Single(f => f.Family == PriceHistoryFamily.Membership);
        vm.SearchText = "Gold";
        vm.IncludeInactive = false;
        await vm.PendingHistoryLoad;
        Assert.Equal(["$7.00/month"], vm.Rows.Select(r => r.PriceText));

        // Another computer's older observation arrives through Personal Sync.
        service.Intervals["gold-membership"].Insert(0, new SeasonPriceHistoryInterval(
            "gold-membership", 0, 6m, PriceKind.StandardCatalog, null, null, "Gold Membership", "Carowinds Memberships & Season Passes", At.AddMonths(-6), At.AddMonths(-5), SeasonPriceHistoryOrigin.Observed));
        notifier.Publish(new DataChange(DataChangeKind.OfficialPriceHistory));
        await vm.PendingRefresh;
        await vm.PendingHistoryLoad;

        Assert.Equal("gold-membership", vm.SelectedProduct!.ProductKey);
        Assert.Equal(PriceHistoryFamily.Membership, vm.SelectedFamilyFilter.Family);
        Assert.Equal("Gold", vm.SearchText);
        Assert.False(vm.IncludeInactive);
        Assert.Equal(["$6.00/month", "$7.00/month"], vm.Rows.Select(r => r.PriceText));
        Assert.Equal(2, vm.Rows.Select(r => r.FirstSeenText).Distinct().Count()); // no duplicate rows
    }

    [Fact]
    public async Task OtherChanges_AndAClosedScreen_NeverTriggerARefresh()
    {
        var service = Service();
        var notifier = new Notifier();
        var vm = new PriceHistoryViewModel(service, dataChangeNotifier: notifier, uiDispatcher: new ImmediateUiDispatcher());

        notifier.Publish(new DataChange(DataChangeKind.OfficialPriceHistory));
        await vm.PendingRefresh;
        Assert.False(vm.IsLoaded); // closed: nothing was read

        await vm.OpenAsync(null);
        var before = vm.PendingRefresh;
        notifier.Publish(new DataChange(DataChangeKind.AssociateDraft));
        notifier.Publish(new DataChange(DataChangeKind.PersonalSync));
        Assert.Same(before, vm.PendingRefresh);
    }

    // ----- Clickable official source link -----

    [Fact]
    public async Task SourceLink_OpensTheVerifiedOfficialPage_InTheDefaultBrowser()
    {
        var launcher = new RecordingLauncher();
        var link = OfficialSourceLinkViewModel.TryCreate(OfficialPage, launcher)!;

        Assert.Equal("View Official Memberships & Season Passes Page", link.Label);
        Assert.Equal("Opens www.sixflags.com/carowinds/memberships-and-season-passes in your default web browser.", link.HelpText);

        await link.OpenCommand.ExecuteAsync(null);

        Assert.Equal([OfficialPage], launcher.Opened);
        Assert.False(link.HasMessage);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SourceLink_OpenFailure_ShowsAPlainLanguageMessage(bool succeeds, bool throws)
    {
        var link = OfficialSourceLinkViewModel.TryCreate(OfficialPage, new RecordingLauncher(succeeds) { Throws = throws })!;

        await link.OpenCommand.ExecuteAsync(null);

        Assert.True(link.HasMessage);
        Assert.Equal("The official page couldn't be opened. Open your web browser and go to www.sixflags.com/carowinds/memberships-and-season-passes.", link.Message);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://www.sixflags.com/carowinds/memberships-and-season-passes")]
    [InlineData("https://www.sixflags.com.attacker.example/carowinds")]
    [InlineData("https://www.sixflags.com/magicmountain/memberships-and-season-passes")]
    public void SourceLink_IsNeverOfferedForAnUnsafeAddress(string address)
    {
        var launcher = new RecordingLauncher();

        Assert.Null(OfficialSourceLinkViewModel.TryCreate(new Uri(address), launcher));
        Assert.Empty(launcher.Opened);
    }

    [Fact]
    public void SourceLink_UsesTheProductsRecordedSourceUrl_NotAHardCodedAddress()
    {
        var offer = PassOffer.SeasonPass("Gold", PassPriceType.NewPurchase, 2027);
        var recorded = new Uri("https://www.sixflags.com/carowinds/season-passes-v2");
        var product = new AssociateProduct(
            AssociateProductIdentity.ForCatalogProduct(offer.ToKey().ToString()), offer.DisplayName, SourceCategory.SeasonPasses, IsCustom: false,
            99m, "$99", null, null, DateSpecific: false, ApplicableDate: null,
            new AssociateVerificationInfo(AssociateVerificationState.Verified, At, null, "Verified."), IncludeOnPdf: true, DisplayPriceOverride: null,
            PriceKind: PriceKind.StandardCatalog)
        {
            PassOffer = offer,
            OfficialSourceUrl = recorded,
        };

        Assert.Equal(recorded, PassSourcePresentation.SourceUrl(ProductDisplaySection.SeasonPasses, [product]));
        Assert.Null(PassSourcePresentation.SourceUrl(ProductDisplaySection.AddOns, [product]));
        Assert.Equal("View Official Page", OfficialSourceLinkViewModel.TryCreate(recorded, new RecordingLauncher())!.Label);
        Assert.Null(OfficialSourceLinkViewModel.TryCreate(recorded, launcher: null));
    }

    [Fact]
    public async Task PriceHistory_OffersTheSelectedProductsOfficialPage()
    {
        var service = new FakeSeasonPriceHistoryService();
        service.Products.Add(new PriceHistoryProduct("gold-pass", "Gold Season Pass — 2027", PriceHistoryFamily.SeasonPass, true, 1) { OfficialSourceUrl = OfficialPage });
        service.Products.Add(new PriceHistoryProduct("imported", "Gold Season Pass — 2026", PriceHistoryFamily.SeasonPass, false, 1) { IsImportedOnly = true });
        var vm = new PriceHistoryViewModel(service, linkLauncher: new RecordingLauncher());

        await vm.OpenAsync("gold-pass");
        Assert.True(vm.HasSourceLink);
        Assert.Equal(OfficialPage, vm.SourceLink!.Address);

        vm.IncludeInactive = true;
        vm.SelectedProduct = vm.Products.Single(p => p.ProductKey == "imported");
        await vm.PendingHistoryLoad;
        Assert.False(vm.HasSourceLink); // no verified source on this computer — no link invented
    }

    [Fact]
    public void AssociateMarkup_HasAnAccessibleOfficialSourceLink_UnderThePassSourceNote()
    {
        var markup = Markup("AssociateView");

        Assert.True(markup.IndexOf("{Binding SourceNoteText}", StringComparison.Ordinal) < markup.IndexOf("{Binding SourceLink.Label}", StringComparison.Ordinal));
        Assert.Contains("IsVisible=\"{Binding HasSourceLink}\"", markup);
        AssertAccessibleLink(markup);
    }

    [Fact]
    public void LinkStyle_UsesThemeTokensDefinedForLightAndDark()
    {
        var styles = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml"));
        var tokens = File.ReadAllText(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml"));

        Assert.Contains("Selector=\"Button.textAction, Button.linkAction\"", styles);
        Assert.Contains("{DynamicResource PassdownAccentForegroundBrush}", styles);
        Assert.Equal(2, Regex.Matches(tokens, "x:Key=\"PassdownAccentForegroundBrush\"").Count); // light + dark dictionaries
    }

    private static void AssertAccessibleLink(string markup)
    {
        var start = markup.IndexOf("Content=\"{Binding SourceLink.Label}\"", StringComparison.Ordinal);
        Assert.True(start > 0);
        var buttonStart = markup.LastIndexOf("<Button", start, StringComparison.Ordinal);
        var button = markup[buttonStart..markup.IndexOf("/>", start, StringComparison.Ordinal)];

        Assert.Contains("Classes=\"linkAction\"", button);                        // a real Button: Tab stop, Enter/Space, Fluent focus visual
        Assert.Contains("Command=\"{Binding SourceLink.OpenCommand}\"", button);
        Assert.Contains("AutomationProperties.Name=\"{Binding SourceLink.Label}\"", button);
        Assert.Contains("AutomationProperties.HelpText=\"{Binding SourceLink.HelpText}\"", button);
        Assert.DoesNotContain("IsTabStop=\"False\"", button);
        Assert.DoesNotContain("Focusable=\"False\"", button);
        Assert.DoesNotMatch("(Foreground|Background)=\"#", button);
        Assert.Contains("HorizontalAlignment=\"Left\"", button); // no fixed width: wraps/shrinks on narrow layouts
        Assert.DoesNotContain("Width=\"", button.Replace("MaxWidth", string.Empty, StringComparison.Ordinal));
        Assert.Contains("{Binding SourceLink.Message}", markup[start..]);
    }
}
