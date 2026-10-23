using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Hours;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Morning Message rollback (docs/morning-message-rollback.md): the Phase 29–30 Morning Message Builder,
/// its formatted copy and its sync are retired. Nothing of it may be reachable, and the three original Copy
/// options keep their pre-Phase-29 plain-text output byte for byte.
/// </summary>
public class MorningMessageRetiredTests
{
    private static readonly string[] RetiredNames = ["MorningMessage", "TeamsClipboard", "CfHtml", "TeamsHtml", "WindowsNativeClipboard", "ImageFilePicker"];

    /// <summary>
    /// The RC.9 (pre-Phase-29) output for the fixture in <see cref="AssociateViewModelCopyForTeamsTests"/>:
    /// official hours 11:00 AM – 8:00 PM, default gates, no ERT, default PDF price selection.
    /// </summary>
    [Theory]
    [InlineData(AssociateTeamsCopyKind.OperationsSummary,
        "Park Hours: 11:00 AM - 8:00 PM\r\nAvenue: 10:15 AM\r\nNorth Gate: 10:30 AM\r\nERT: Not configured")]
    [InlineData(AssociateTeamsCopyKind.TodaysPricing,
        "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\nFast Lane Ultimate: $109.00")]
    [InlineData(AssociateTeamsCopyKind.OperationsAndPricing,
        "Park Hours: 11:00 AM - 8:00 PM\r\nAvenue: 10:15 AM\r\nNorth Gate: 10:30 AM\r\nERT: Not configured\r\n\r\n"
        + "Ticket Prices:\r\n\r\nDaily Admission\r\nPark Ticket: $59.00\r\n\r\nFast Lane\r\nFast Lane Reserve: $65.00\r\nFast Lane Ultimate: $109.00")]
    public async Task OriginalCopyOptions_CopyExactPreMorningMessagePlainText(AssociateTeamsCopyKind kind, string expected)
    {
        var (viewModel, clipboard) = CreateViewModel();
        await viewModel.PendingLoad;

        await viewModel.CopyForTeamsCommand.ExecuteAsync(kind);

        Assert.Equal(expected, Assert.Single(clipboard.Texts));
    }

    [Fact]
    public void ClipboardSeam_IsPlainTextOnly_AsBeforePhase29()
    {
        Assert.Equal(["SetTextAsync"], typeof(IClipboardService).GetMethods().Select(method => method.Name));
    }

    [Theory]
    [InlineData("Carowinds.Passdown.Desktop")]
    [InlineData("Carowinds.Passdown.Documents")]
    [InlineData("Carowinds.Passdown.Application")]
    [InlineData("Carowinds.Passdown.Infrastructure")]
    public void NoAssembly_DefinesARetiredMorningMessageType(string assemblyName)
    {
        var offending = Assembly.Load(assemblyName).GetTypes()
            .Where(type => RetiredNames.Any(name => type.FullName!.Contains(name, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(offending.Length == 0, $"Retired Morning Message types still present: {string.Join(", ", offending)}");
    }

    [Fact]
    public void PersonalSync_HasNoMorningMessageSettingOrFolder()
    {
        Assert.DoesNotContain(typeof(SyncConfiguration).GetProperties(), property => property.Name.Contains("Morning", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(SyncPathService).GetMethods(), method => method.Name.Contains("Morning", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(PersonalSyncResult).GetProperties(), property => property.Name.Contains("Morning", StringComparison.Ordinal));
    }

    [Fact]
    public void AssociateView_ShowsNoMorningMessageControl()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(DateTimeOffset.UtcNow));
        var view = new AssociateView { DataContext = viewModel };

        var texts = view.GetLogicalDescendants().SelectMany(node => node switch
        {
            TextBlock { Text: { } text } => [text],
            Button { Content: string content } => [content],
            MenuItem { Header: string header } => [header],
            _ => Array.Empty<string>(),
        }).ToArray();

        Assert.DoesNotContain(texts, text => text.Contains("Morning", StringComparison.OrdinalIgnoreCase) || text.Contains("OD Notes", StringComparison.Ordinal));
    }

    /// <summary>Every Desktop view's markup, comments stripped: no composer, no Sync Morning Messages toggle, no formatted-copy controls.</summary>
    [Fact]
    public void DesktopMarkup_HasNoMorningMessageUi()
    {
        var views = Path.GetDirectoryName(FindRepoFile("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml"))!;
        foreach (var file in Directory.GetFiles(views, "*.axaml"))
        {
            var markup = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            Assert.DoesNotContain("Morning", markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Copy Formatted", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("OD Notes", markup, StringComparison.Ordinal);
        }
    }

    private static readonly DateOnly Today = new(2026, 8, 21);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 21, 15, 0, 0, TimeSpan.Zero);

    /// <summary>The same fixture as <see cref="AssociateViewModelCopyForTeamsTests"/> (kept separate so that file stays as it was before Phase 29).</summary>
    private static (AssociateViewModel ViewModel, FakeClipboardService Clipboard) CreateViewModel()
    {
        var service = new FakeAssociatePassdownService { OfficialHours = (new TimeOnly(11, 0), new TimeOnly(20, 0)) };
        service.WorkspaceFactory = date => FakeAssociatePassdownService.DefaultWorkspace(date) with
        {
            Draft = service.StoredDraft(date) ?? AssociateDraftPayload.CreateDefault(),
            ParkHours = new AssociateParkHoursView(
                ParkHoursCalculator.Calculate(false, new TimeOnly(11, 0), new TimeOnly(20, 0), false, null, null, false, null, null),
                AssociateVerificationInfo.NotCheckedYet, false, new TimeOnly(11, 0), new TimeOnly(20, 0)),
            Products =
            [
                Product("reg", "Park Ticket", SourceCategory.DatedAdmissionTickets, 59m),
                Product("reserve", "Fast Lane Reserve", SourceCategory.DatedFastLane, 65m),
                Product("ultimate", "Fast Lane Ultimate", SourceCategory.DatedFastLane, 109m),
                Product("gold", "Gold Season Pass", SourceCategory.SeasonPasses, 129m),
            ],
        };

        var clipboard = new FakeClipboardService();
        var viewModel = new AssociateViewModel(service, new FakeClock(FixedNow), autosaveDelay: new ManualDebounceScheduler().Delay, clipboardService: clipboard);
        return (viewModel, clipboard);
    }

    private static AssociateProduct Product(string suffix, string name, SourceCategory category, decimal price) => new(
        AssociateProductIdentity.ForDatedProduct(category, suffix), name, category, IsCustom: false, price,
        OfficialDisplayPrice: null, ComparisonPriceAmount: null, PriceQualifier: null, DateSpecific: true, Today,
        new AssociateVerificationInfo(AssociateVerificationState.Verified, FixedNow, null, null), IncludeOnPdf: true, DisplayPriceOverride: null);

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' above {AppContext.BaseDirectory}.");
    }
}
