using System.ComponentModel;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 35G: fixes from the cross-screen consistency audit. View-model and markup contracts only (no real Window),
/// so this class runs in the mixed Ui partition.
/// </summary>
public class Phase35GPolishTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);

    private static async Task<OperationalNotesViewModel> NotesViewModel()
    {
        var vm = new OperationalNotesViewModel(new FakeOperationalNotesService { UtcNowFactory = () => Now }, new FakeClock(Now));
        await vm.PendingLoad;
        return vm;
    }

    [Fact]
    public async Task NoteEditor_EffectiveThroughPlaceholder_SaysUntilResolved_ThenAsksForADate()
    {
        var vm = await NotesViewModel();
        vm.NewNoteCommand.Execute(null);
        Assert.True(vm.EditorUntilResolved);
        Assert.Equal("Until resolved", vm.EditorEffectiveThroughPlaceholder);

        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.EditorUntilResolved = false;

        Assert.Equal("Choose a date", vm.EditorEffectiveThroughPlaceholder);
        Assert.Equal(OperationalNotesViewModel.DatePickerPlaceholder, vm.EditorEffectiveThroughPlaceholder);
        Assert.Contains(nameof(OperationalNotesViewModel.EditorEffectiveThroughPlaceholder), changed);
        Assert.Null(vm.EditorEffectiveThrough); // words only: the placeholder never becomes a date
    }

    [Fact]
    public void EveryDatePicker_HasAPlaceholderInWords_NeverTheRawFormatString()
    {
        var views = Path.GetDirectoryName(MarkupFiles.Find("src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml"))!;
        var pickers = 0;
        foreach (var file in Directory.EnumerateFiles(views, "*.axaml"))
        {
            foreach (Match picker in Regex.Matches(File.ReadAllText(file), "<CalendarDatePicker\\b[^>]*>", RegexOptions.Singleline))
            {
                pickers++;
                var placeholder = Regex.Match(picker.Value, "PlaceholderText=\"([^\"]+)\"");
                Assert.True(placeholder.Success, $"{Path.GetFileName(file)}: a CalendarDatePicker has no PlaceholderText, so Avalonia shows \"<MMM d, yyyy>\"");
                Assert.DoesNotContain("<", placeholder.Groups[1].Value);
                Assert.DoesNotContain("yyyy", placeholder.Groups[1].Value);
            }
        }

        Assert.True(pickers >= 2, "the Operational Notes editor's two date pickers were not found");
    }

    [Fact]
    public void SettingsDataRefresh_PointsToWhereCheckOnlineAndSourceStatusActuallyAre()
    {
        // Phase 35B moved the Passdown's Check Online into the Actions menu and removed its source-details strip;
        // the Settings explanation must not send people to a place that no longer exists.
        static string Markup(string view) => File.ReadAllText(MarkupFiles.Find($"src/Carowinds.Passdown.Desktop/Views/{view}.axaml"));
        var settings = Markup("PersonalSyncSettingsView");
        var line = Regex.Match(settings, "Text=\"Status and Check Online\"[^>]*/>\\s*<TextBlock[^>]*Text=\"([^\"]+)\"", RegexOptions.Singleline);
        Assert.True(line.Success);
        Assert.DoesNotContain("official source details", line.Groups[1].Value);
        Assert.Contains("Official Data on the Dashboard", line.Groups[1].Value);
        Assert.Contains("Passdown's Actions menu", line.Groups[1].Value);

        Assert.Contains("Text=\"OFFICIAL DATA\"", Markup("DashboardView"), StringComparison.OrdinalIgnoreCase);
        var associate = Markup("AssociateView");
        Assert.Matches("<MenuItem Header=\"Check Online[^\"]*\"\\s+Command=\"\\{Binding CheckOnlineCommand\\}\"", associate);
    }
}
