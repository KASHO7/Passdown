namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// The application shell's state (Phase 4B Part A). Associate Passdown is the only workflow for
/// this release (CLAUDE.md "current scope decision" — Manager is deferred), so this simply hosts
/// <see cref="AssociateViewModel"/> rather than any navigation/mode-selection state; a Manager
/// workflow view model is intentionally not added here as a placeholder.
/// </summary>
public sealed class MainViewModel(AssociateViewModel associate) : ViewModelBase
{
    public AssociateViewModel Associate { get; } = associate;
}
