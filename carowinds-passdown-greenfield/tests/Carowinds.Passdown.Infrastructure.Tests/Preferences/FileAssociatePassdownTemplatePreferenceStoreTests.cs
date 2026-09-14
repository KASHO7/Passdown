using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.Preferences;

namespace Carowinds.Passdown.Infrastructure.Tests.Preferences;

/// <summary>Phase 17C §15/§39: the local Associate Passdown template preference — Classic is always the safe fallback.</summary>
public class FileAssociatePassdownTemplatePreferenceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-template-preference-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsClassicDefault()
    {
        var store = new FileAssociatePassdownTemplatePreferenceStore(PathFor("associate-passdown-template.json"));

        var preference = await store.LoadAsync();

        Assert.Equal(AssociatePassdownTemplatePreference.Default, preference);
        Assert.Equal(AssociatePassdownTemplate.Classic, preference.Template);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips(AssociatePassdownTemplate template)
    {
        var store = new FileAssociatePassdownTemplatePreferenceStore(PathFor("associate-passdown-template.json"));
        var saved = new AssociatePassdownTemplatePreference(template);

        await store.SaveAsync(saved);
        var loaded = await store.LoadAsync();

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task LoadAsync_MalformedFile_ReturnsClassicDefault_NeverThrows()
    {
        var path = PathFor("associate-passdown-template.json");
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{ not valid json");

        var preference = await new FileAssociatePassdownTemplatePreferenceStore(path).LoadAsync();

        Assert.Equal(AssociatePassdownTemplatePreference.Default, preference);
    }

    [Fact]
    public void DefaultPath_IsUnderAppPathsLocalApplicationDataDirectory()
    {
        Assert.StartsWith(AppPaths.LocalApplicationDataDirectory, FileAssociatePassdownTemplatePreferenceStore.DefaultPath);
    }
}
