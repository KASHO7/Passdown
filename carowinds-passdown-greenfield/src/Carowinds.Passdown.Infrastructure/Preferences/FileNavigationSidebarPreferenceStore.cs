using System.Text.Json;
using System.Text.Json.Serialization;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Infrastructure.PersonalSync;
using Microsoft.Extensions.Logging;

namespace Carowinds.Passdown.Infrastructure.Preferences;

/// <summary>
/// Phase 35A: <see cref="INavigationSidebarPreferenceStore"/> backed by <c>navigation-sidebar.json</c>
/// (e.g. <c>{ "Sidebar": "Collapsed" }</c>) beside the other local preference files — atomic writes, no SQLite,
/// outside the Personal Sync folder, never exported. Missing, malformed or unrecognized = Expanded.
/// </summary>
public sealed class FileNavigationSidebarPreferenceStore(string configFilePath, ILogger<FileNavigationSidebarPreferenceStore>? logger = null) : INavigationSidebarPreferenceStore
{
    public static string DefaultPath { get; } = Path.Combine(AppPaths.LocalApplicationDataDirectory, "navigation-sidebar.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    public string ConfigFilePath { get; } = configFilePath;

    public NavigationSidebarState Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return NavigationSidebarState.Expanded;
        }

        try
        {
            var file = JsonSerializer.Deserialize<SidebarFile>(File.ReadAllText(ConfigFilePath), Options);
            if (file is null || !Enum.IsDefined(file.Sidebar))
            {
                logger?.LogWarning("Navigation sidebar preference is empty or not recognized; using Expanded.");
                return NavigationSidebarState.Expanded;
            }

            return file.Sidebar;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning("Navigation sidebar preference could not be read ({ExceptionType}); using Expanded.", ex.GetType().Name);
            return NavigationSidebarState.Expanded;
        }
    }

    public async Task SaveAsync(NavigationSidebarState state, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown navigation sidebar state.");
        }

        var json = JsonSerializer.Serialize(new SidebarFile(state), Options);
        await AtomicFileWriter.WriteTextAsync(ConfigFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private sealed record SidebarFile(NavigationSidebarState Sidebar);
}
