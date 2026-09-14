using Carowinds.Passdown.Application.Preferences;

namespace Carowinds.Passdown.Application.Tests.TestSupport;

/// <summary>In-memory <see cref="IAssociatePassdownTemplatePreferenceStore"/> for finalization tests — lets a test change the "current" template between two <see cref="Carowinds.Passdown.Application.Finalization.FinalizedPassdownFinalizationService.FinalizeAsync"/> calls to prove finalization always reads it fresh at that exact moment (Phase 17C §18/§31).</summary>
internal sealed class FakeAssociatePassdownTemplatePreferenceStore : IAssociatePassdownTemplatePreferenceStore
{
    public AssociatePassdownTemplatePreference Current { get; set; } = AssociatePassdownTemplatePreference.Default;

    public Task<AssociatePassdownTemplatePreference> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task SaveAsync(AssociatePassdownTemplatePreference preference, CancellationToken cancellationToken = default)
    {
        Current = preference;
        return Task.CompletedTask;
    }
}
