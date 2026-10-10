using Carowinds.Passdown.Domain.Ert;

namespace Carowinds.Passdown.Application.Ert;

/// <summary>
/// Resolves the one effective Early Ride Time value for an operating date (Phase 17B spec Part 4) —
/// the only place a date override and a monthly configuration are ever loaded together. Every
/// consumer (Associate screen, Dashboard, PDF, finalized snapshot) reads the result of this call —
/// via <c>Associate.AssociateWorkspace.EarlyRideTime</c>, resolved once in <c>AssociatePassdownService.ComposeAsync</c>
/// — rather than calling this a second time or re-deriving precedence itself (spec Part 43: at most
/// one monthly lookup and one override lookup per resolution).
/// </summary>
public interface IEffectiveErtResolver
{
    Task<EffectiveErt> ResolveAsync(DateOnly operatingDate, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEffectiveErtResolver"/>
public sealed class EffectiveErtResolver(IMonthlyErtRepository monthlyRepository, IErtDateOverrideRepository overrideRepository) : IEffectiveErtResolver
{
    public async Task<EffectiveErt> ResolveAsync(DateOnly operatingDate, CancellationToken cancellationToken = default)
    {
        var dateOverride = await overrideRepository.GetAsync(operatingDate, cancellationToken).ConfigureAwait(false);
        var monthly = await monthlyRepository.GetAsync(operatingDate.Year, operatingDate.Month, cancellationToken).ConfigureAwait(false);

        return ErtPrecedenceResolver.Resolve(monthly?.ToSnapshot(), dateOverride?.ToSnapshot());
    }
}
