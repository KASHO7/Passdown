using Carowinds.Passdown.Domain.PassPerks;

namespace Carowinds.Passdown.Application.PassPerks;

/// <summary>Phase 32: the reference-document id of the cached Pass Perks (a row in <c>official_reference_documents</c>).</summary>
public static class PassPerksDocumentIds
{
    public const string PassPerks = "pass-perks.official";
}

/// <summary>
/// Phase 32: what one successful official Pass Perks check observed. <see cref="IsComplete"/> is true only
/// when the park page's month list was found and every listed reward on it was readable; only a complete
/// observation may mark a previously listed perk as removed. A check that read zero rewards never reaches
/// this type — the source reports it as an error, so it can never be mistaken for a confirmed empty month.
/// </summary>
public sealed record PassPerksObservation(PassPerkCampaign Campaign, IReadOnlyList<OfficialPassPerk> Perks)
{
    /// <summary>Listed rewards whose entry could not be read (no title).</summary>
    public int UnreadableCount { get; init; }

    public bool IsComplete => UnreadableCount == 0;

    /// <summary>Program terms read this time (any page that failed leaves its fields null; they are filled from the last verified terms).</summary>
    public PassPerksProgramTerms? ProgramTerms { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Phase 32: one month campaign as observed on the official page.</summary>
public sealed record PassPerkCampaignRecord(PassPerkCampaign Campaign, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

/// <summary>
/// Phase 32: the stored last-known-good Pass Perks document — every perk ever verified (current and
/// historical), the campaigns observed, and the program terms. Historical perks are kept with their
/// provenance (first seen, last verified, removal, earlier wordings) for <see cref="HistoryRetention"/>.
/// </summary>
public sealed record PassPerksDocument(IReadOnlyList<OfficialPassPerk> Perks)
{
    public static PassPerksDocument Empty { get; } = new([]);

    /// <summary>How long a no-longer-listed perk is kept for the month history.</summary>
    public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(400);

    public IReadOnlyList<PassPerkCampaignRecord> Campaigns { get; init; } = [];

    /// <summary>The campaign the latest successful check found on the official page.</summary>
    public string? LatestCampaignKey { get; init; }

    public PassPerksProgramTerms? ProgramTerms { get; init; }

    /// <summary>Notes from the latest successful check (e.g. the terms page could not be read).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Merges a successful observation:
    /// <list type="bullet">
    /// <item>an observed perk replaces its previous version, keeping its first-seen time; if its official
    /// wording changed, the previous wording is appended to <see cref="OfficialPassPerk.PreviousVersions"/>;
    /// a perk that had been marked removed and is listed again is current again;</item>
    /// <item>a previous perk not observed is marked <see cref="OfficialPassPerk.NoLongerListedSince"/> only
    /// when the observation is complete; otherwise it is kept as last verified, flagged incomplete;</item>
    /// <item>perks last verified more than <see cref="HistoryRetention"/> ago are dropped.</item>
    /// </list>
    /// </summary>
    public PassPerksDocument Merge(PassPerksObservation observation, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var previousById = Perks.ToDictionary(p => p.PerkId, StringComparer.Ordinal);
        var merged = new Dictionary<string, OfficialPassPerk>(StringComparer.Ordinal);

        foreach (var fresh in observation.Perks)
        {
            if (!previousById.TryGetValue(fresh.PerkId, out var previous))
            {
                merged[fresh.PerkId] = fresh with { FirstSeenAt = checkedAt, LastVerifiedAt = checkedAt };
                continue;
            }

            var versions = previous.PreviousVersions;
            if (!previous.HasSameTermsAs(fresh))
            {
                versions = [.. versions, new PassPerkTermsVersion(previous.FirstSeenAtOfCurrentTerms(), previous.LastVerifiedAt, previous.Title, previous.OfficialParagraphs, previous.DateText)];
            }

            merged[fresh.PerkId] = fresh with
            {
                FirstSeenAt = previous.FirstSeenAt,
                LastVerifiedAt = checkedAt,
                PreviousVersions = versions,
                NoLongerListedSince = null,
                LastCheckIncomplete = false,
            };
        }

        foreach (var previous in Perks)
        {
            if (merged.ContainsKey(previous.PerkId) || checkedAt - previous.LastVerifiedAt > HistoryRetention)
            {
                continue;
            }

            merged[previous.PerkId] = observation.IsComplete
                ? previous with { NoLongerListedSince = previous.NoLongerListedSince ?? checkedAt, LastCheckIncomplete = false }
                : previous with { LastCheckIncomplete = previous.NoLongerListedSince is null };
        }

        var campaigns = Campaigns.ToDictionary(c => c.Campaign.Key, StringComparer.Ordinal);
        campaigns[observation.Campaign.Key] = campaigns.TryGetValue(observation.Campaign.Key, out var known)
            ? known with { Campaign = observation.Campaign, LastSeenAt = checkedAt }
            : new PassPerkCampaignRecord(observation.Campaign, checkedAt, checkedAt);

        var terms = observation.ProgramTerms is { } freshTerms
            ? freshTerms.FillFrom(ProgramTerms)
            : ProgramTerms;

        return new PassPerksDocument(merged.Values
            .OrderBy(p => p.Campaign.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList())
        {
            Campaigns = campaigns.Values.OrderBy(c => c.Campaign.Key, StringComparer.Ordinal).ToList(),
            LatestCampaignKey = observation.Campaign.Key,
            ProgramTerms = terms,
            Warnings = observation.Warnings,
        };
    }
}

/// <summary>Phase 32: small helpers over a perk's version history.</summary>
public static class OfficialPassPerkHistory
{
    /// <summary>When the perk's current wording was first seen (after its last recorded earlier version).</summary>
    public static DateTimeOffset FirstSeenAtOfCurrentTerms(this OfficialPassPerk perk)
    {
        ArgumentNullException.ThrowIfNull(perk);
        return perk.PreviousVersions.Count == 0 ? perk.FirstSeenAt : perk.PreviousVersions[^1].LastVerifiedAt;
    }
}
