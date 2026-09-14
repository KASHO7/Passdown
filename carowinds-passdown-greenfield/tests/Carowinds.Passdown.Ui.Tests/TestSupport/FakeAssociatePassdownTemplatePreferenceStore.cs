using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>In-memory <see cref="IAssociatePassdownTemplatePreferenceStore"/> for <see cref="Carowinds.Passdown.Desktop.ViewModels.AssociateViewModel"/> tests — records every <see cref="SaveAsync"/> call so a test can prove the ViewModel persists a template change immediately.</summary>
internal sealed class FakeAssociatePassdownTemplatePreferenceStore : IAssociatePassdownTemplatePreferenceStore
{
    public AssociatePassdownTemplatePreference Current { get; set; } = AssociatePassdownTemplatePreference.Default;

    public List<AssociatePassdownTemplatePreference> SaveCalls { get; } = [];

    public Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default)
    {
        Current = preference;
        SaveCalls.Add(preference);
        return Task.CompletedTask;
    }
}
