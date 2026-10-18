using Carowinds.Passdown.Application.Updates;

namespace Carowinds.Passdown.Desktop.Services;

/// <summary>
/// Phase 23D: the two things Install Update needs from the running app shell — save the Associate draft
/// through the existing Phase 23A path, and close the app once the external updater is running.
/// </summary>
public interface IUpdateRestartHost
{
    /// <summary>True only when the edits on screen are confirmed saved (same rule as closing the window).</summary>
    Task<bool> SaveWorkBeforeRestartAsync();

    /// <summary>Closes the app through the normal shutdown path (which flushes again and stops background work).</summary>
    void CloseForUpdate();
}

/// <summary>
/// Phase 23D: filled in by <c>App.OnFrameworkInitializationCompleted</c> once the window and desktop lifetime
/// exist. Until then (and in tests without a shell) saving reports failure, so an install can never proceed
/// without a confirmed save.
/// </summary>
public sealed class DesktopUpdateRestartHost : IUpdateRestartHost
{
    public Func<Task<bool>>? SaveWork { get; set; }

    public Action? Close { get; set; }

    public Task<bool> SaveWorkBeforeRestartAsync() => SaveWork?.Invoke() ?? Task.FromResult(false);

    public void CloseForUpdate() => Close?.Invoke();
}

/// <summary>Phase 23D: the release notes bundled in this build (ReleaseNotes/release-notes.md, embedded) — available offline.</summary>
public sealed class EmbeddedReleaseNotesSource : IBundledReleaseNotesSource
{
    public const string ResourceName = "Carowinds.Passdown.Desktop.ReleaseNotes.release-notes.md";

    public ReleaseNotesContent? LoadCurrent()
    {
        using var stream = typeof(EmbeddedReleaseNotesSource).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return BundledReleaseNotesParser.Parse(reader.ReadToEnd());
    }
}
