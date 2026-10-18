using Carowinds.Passdown.Application.Updates;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 23D: the "What's New in Carowinds Operations" overlay. Opened automatically once after a confirmed
/// in-app update, and any time from Settings → About this build → What's New. Shows the notes bundled in this
/// build (offline), rendered as plain text blocks — never HTML, links or scripts. Closing it records the version
/// as acknowledged on this computer only.
/// </summary>
public partial class WhatsNewViewModel : ViewModelBase
{
    public const string MissingNotesText = "Release notes aren't available for this version.";

    private readonly IBundledReleaseNotesSource? _notesSource;
    private readonly Func<SemanticVersion, Task>? _acknowledge;

    public WhatsNewViewModel(IBundledReleaseNotesSource? notesSource = null, IApplicationUpdateCoordinator? coordinator = null)
    {
        _notesSource = notesSource;
        _acknowledge = coordinator is null ? null : coordinator.AcknowledgeReleaseNotesAsync;
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string VersionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PublishedText { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReleaseNotesBlock> Blocks { get; set; } = [];

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    /// <summary>The acknowledgement started by the last Close; tests await it.</summary>
    public Task PendingAcknowledge { get; private set; } = Task.CompletedTask;

    private SemanticVersion? _openVersion;

    /// <summary>Opens with the bundled notes for the running build. Returns false when the build has none (the overlay still opens and says so).</summary>
    public bool OpenCurrent(string runningVersion)
    {
        var notes = _notesSource?.LoadCurrent();
        // Notes for another version than the one running are never shown as if they were this version's.
        if (notes is not null && notes.Version != runningVersion)
        {
            notes = null;
        }

        Open(notes, runningVersion);
        return notes is not null;
    }

    public void Open(ReleaseNotesContent? notes, string versionFallback)
    {
        VersionText = $"Version {notes?.Version ?? versionFallback}";
        Title = notes?.Title ?? "Carowinds Operations";
        PublishedText = notes?.PublishedOn is { } date ? $"Released {date.ToString("MMMM d, yyyy", PresentationCulture.UnitedStates)}" : null;
        Blocks = notes?.Document.Blocks ?? [];
        HasNotes = Blocks.Count > 0;
        _openVersion = SemanticVersion.TryParse(notes?.Version ?? versionFallback, out var version) ? version : null;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        if (_openVersion is not null && _acknowledge is not null)
        {
            PendingAcknowledge = _acknowledge(_openVersion);
        }
    }
}
