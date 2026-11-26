namespace Carowinds.Passdown.Application.Preferences;

/// <summary>
/// Phase 35A: whether the application sidebar shows labels (Expanded) or icons only (Collapsed). A local device
/// preference — persisted by name, never part of Personal Sync. Purely presentational: it never changes which
/// section is shown or what any page holds.
/// </summary>
public enum NavigationSidebarState
{
    /// <summary>Icons and labels. The default for a fresh profile.</summary>
    Expanded = 0,

    /// <summary>Icons only, with tooltips and accessible names.</summary>
    Collapsed = 1,
}

/// <summary>Phase 35A: local persistence for <see cref="NavigationSidebarState"/> — one small file on this computer. Never synced.</summary>
public interface INavigationSidebarPreferenceStore
{
    /// <summary>Synchronous (read once before the window is built). Never throws: a missing or unreadable value returns <see cref="NavigationSidebarState.Expanded"/>.</summary>
    NavigationSidebarState Load();

    /// <summary>Writes only the sidebar file, atomically.</summary>
    Task SaveAsync(NavigationSidebarState state, CancellationToken cancellationToken = default);
}
