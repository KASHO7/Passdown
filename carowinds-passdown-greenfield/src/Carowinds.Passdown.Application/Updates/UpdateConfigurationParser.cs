using Carowinds.Passdown.Domain.Updates;

namespace Carowinds.Passdown.Application.Updates;

/// <summary>
/// Phase 23D: turns the raw "Updates" settings into an <see cref="UpdateConfiguration"/>. Anything invalid
/// disables updates rather than guessing: an unparseable repository means no release source, an unknown
/// channel means the conservative Release Candidate default is not assumed — updates stay off — and a key
/// entry missing its id or value is ignored.
/// </summary>
public static class UpdateConfigurationParser
{
    public static UpdateConfiguration Parse(string? repository, string? channel, IEnumerable<(string? KeyId, string? PublicKey)> trustedKeys)
    {
        if (!UpdateRepository.TryParse(repository, out var parsedRepository))
        {
            return UpdateConfiguration.Disabled;
        }

        UpdateChannel parsedChannel;
        switch (channel)
        {
            case nameof(UpdateChannel.ReleaseCandidate):
                parsedChannel = UpdateChannel.ReleaseCandidate;
                break;
            case nameof(UpdateChannel.Stable):
                parsedChannel = UpdateChannel.Stable;
                break;
            default:
                return UpdateConfiguration.Disabled;
        }

        var keys = trustedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key.KeyId) && !string.IsNullOrWhiteSpace(key.PublicKey))
            .Select(key => new TrustedUpdateKey(key.KeyId!.Trim(), key.PublicKey!.Trim()))
            .ToList();

        return new UpdateConfiguration(parsedRepository, parsedChannel, keys);
    }
}
