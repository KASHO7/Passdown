using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.PersonalSync;

/// <summary>
/// <see cref="IOneDriveLocationSuggestionProvider"/> reading the environment variables the OneDrive
/// desktop client sets at install time (Phase 16A §K, Phase 16B §3). Never hardcodes
/// <c>C:\Users\&lt;name&gt;\OneDrive</c>; simply reads whichever of these variables are present.
/// <see cref="Environment.GetEnvironmentVariable(string)"/> is itself portable, so this is directly
/// testable on non-Windows hosts by setting/clearing the same variable names — no Windows-only API
/// is involved.
/// </summary>
public sealed class EnvironmentOneDriveLocationSuggestionProvider : IOneDriveLocationSuggestionProvider
{
    private static readonly string[] VariableNames = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    public IReadOnlyList<string> GetSuggestedRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();

        foreach (var name in VariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                results.Add(value);
            }
        }

        return results;
    }
}
