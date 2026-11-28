using System.Text.RegularExpressions;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 37D: the Settings control's placement and wording, and the composition that makes Settings and the Passdown
/// share one preference instance. Checked from source so no test resolves the app's real preference file; the live
/// binding, theme rendering and keyboard use are checked in a real window (Phase15ERealWindowLayoutTests, run alone).
/// </summary>
public class Phase37DSettingsWeatherOutlookMarkupTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    [Fact]
    public void SettingsCheckbox_IsInPassdownDefaults_WithTheExactLabelAndDescription()
    {
        var markup = Source("src", "Carowinds.Passdown.Desktop", "Views", "PersonalSyncSettingsView.axaml");
        var sectionStart = markup.IndexOf("x:Name=\"PassdownPreferencesSection\"", StringComparison.Ordinal);
        var sectionEnd = markup.IndexOf("x:Name=\"CalendarDisplaySection\"", StringComparison.Ordinal);
        var section = markup[sectionStart..sectionEnd];

        var start = section.IndexOf("<CheckBox x:Name=\"IncludeWeatherOutlookByDefault\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the checkbox belongs to the Passdown Defaults section");
        var element = section[start..section.IndexOf("/>", start, StringComparison.Ordinal)];
        Assert.Contains("Content=\"Include Weather Outlook by default\"", element, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding WeatherOutlook.IncludeWeatherOutlook}\"", element, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Include Weather Outlook by default\"", element, StringComparison.Ordinal);
        Assert.Contains("Text=\"" + PassdownWeatherOutlookSettingsViewModel.Description + "\"", section, StringComparison.Ordinal);
        Assert.Equal("Include the Weather Outlook in Passdown PDFs when a valid forecast is available.", PassdownWeatherOutlookSettingsViewModel.Description);
        Assert.Contains("{Binding WeatherOutlook.SaveMessage}", section, StringComparison.Ordinal);

        // One Settings control for it, and no new Settings section or navigation item for one checkbox.
        Assert.Single(Regex.Matches(markup, Regex.Escape("WeatherOutlook.IncludeWeatherOutlook")));
        Assert.DoesNotContain("Tag=\"WeatherOutlookSection\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionRoot_SharesOnePreferenceInstance_BetweenSettingsAndThePassdown()
    {
        var app = Source("src", "Carowinds.Passdown.Desktop", "App.axaml.cs");
        Assert.Single(Regex.Matches(app, Regex.Escape("services.AddSingleton<PassdownWeatherOutlookSettingsViewModel>();")));
        Assert.Single(Regex.Matches(app, Regex.Escape("new FilePassdownWeatherOutlookPreferenceStore(")));

        // Both consumers take the shared instance from the container.
        Assert.Contains(typeof(AssociateViewModel).GetConstructors(), ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(PassdownWeatherOutlookSettingsViewModel)));
        Assert.Contains(typeof(PersonalSyncSettingsViewModel).GetConstructors(), ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(PassdownWeatherOutlookSettingsViewModel)));
    }
}
