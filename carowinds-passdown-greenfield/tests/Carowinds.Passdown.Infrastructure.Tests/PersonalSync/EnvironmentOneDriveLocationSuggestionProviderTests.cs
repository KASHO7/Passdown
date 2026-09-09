using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 16B §3: best-effort OneDrive suggestions from environment variables — never an error when
/// absent, never more than what the variables actually say. Saves/restores every variable it touches
/// so this test class must not run its methods in parallel with itself (xUnit's default — methods
/// within one class run sequentially; only other classes run concurrently, and no other test class
/// touches these variable names).
/// </summary>
public class EnvironmentOneDriveLocationSuggestionProviderTests : IDisposable
{
    private const string OneDrive = "OneDrive";
    private const string OneDriveConsumer = "OneDriveConsumer";
    private const string OneDriveCommercial = "OneDriveCommercial";

    private readonly string?[] _originalValues =
    [
        Environment.GetEnvironmentVariable(OneDrive),
        Environment.GetEnvironmentVariable(OneDriveConsumer),
        Environment.GetEnvironmentVariable(OneDriveCommercial),
    ];

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OneDrive, _originalValues[0]);
        Environment.SetEnvironmentVariable(OneDriveConsumer, _originalValues[1]);
        Environment.SetEnvironmentVariable(OneDriveCommercial, _originalValues[2]);
    }

    [Fact]
    public void GetSuggestedRoots_NoVariablesSet_ReturnsEmpty_NotAnError()
    {
        Environment.SetEnvironmentVariable(OneDrive, null);
        Environment.SetEnvironmentVariable(OneDriveConsumer, null);
        Environment.SetEnvironmentVariable(OneDriveCommercial, null);

        var roots = new EnvironmentOneDriveLocationSuggestionProvider().GetSuggestedRoots();

        Assert.Empty(roots);
    }

    [Fact]
    public void GetSuggestedRoots_OneDriveVariableSet_IsSuggested()
    {
        Environment.SetEnvironmentVariable(OneDrive, "/Users/associate/OneDrive");
        Environment.SetEnvironmentVariable(OneDriveConsumer, null);
        Environment.SetEnvironmentVariable(OneDriveCommercial, null);

        var roots = new EnvironmentOneDriveLocationSuggestionProvider().GetSuggestedRoots();

        Assert.Contains("/Users/associate/OneDrive", roots);
    }

    [Fact]
    public void GetSuggestedRoots_DistinctValuesAcrossVariables_AllSuggested()
    {
        Environment.SetEnvironmentVariable(OneDrive, "/Users/associate/OneDrive");
        Environment.SetEnvironmentVariable(OneDriveConsumer, "/Users/associate/OneDrive-Personal");
        Environment.SetEnvironmentVariable(OneDriveCommercial, null);

        var roots = new EnvironmentOneDriveLocationSuggestionProvider().GetSuggestedRoots();

        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void GetSuggestedRoots_SameValueAcrossVariables_IsNotDuplicated()
    {
        Environment.SetEnvironmentVariable(OneDrive, "/Users/associate/OneDrive");
        Environment.SetEnvironmentVariable(OneDriveConsumer, "/Users/associate/OneDrive");
        Environment.SetEnvironmentVariable(OneDriveCommercial, null);

        var roots = new EnvironmentOneDriveLocationSuggestionProvider().GetSuggestedRoots();

        Assert.Single(roots);
    }

    [Fact]
    public void GetSuggestedRoots_NeverHardcodesAWindowsUserProfilePath()
    {
        Environment.SetEnvironmentVariable(OneDrive, null);
        Environment.SetEnvironmentVariable(OneDriveConsumer, null);
        Environment.SetEnvironmentVariable(OneDriveCommercial, null);

        var roots = new EnvironmentOneDriveLocationSuggestionProvider().GetSuggestedRoots();

        Assert.DoesNotContain(roots, r => r.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase));
    }
}
