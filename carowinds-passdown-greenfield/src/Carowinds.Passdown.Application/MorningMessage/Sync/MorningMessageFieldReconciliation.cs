namespace Carowinds.Passdown.Application.MorningMessage.Sync;

/// <summary>Phase 30: one field's value as some computer last published it.</summary>
public sealed record MorningMessageFieldVersion(string Value, MorningMessageVersion Version);

/// <summary>
/// Phase 30: what this computer remembers about one synchronized field after its last pass — a hash of the
/// value it had then (so a later local edit is detected even if it was made while sync was off) and that
/// value's version.
/// </summary>
public sealed record MorningMessageFieldState(string Hash, MorningMessageVersion Version);

public enum MorningMessageFieldOutcome
{
    /// <summary>Nothing new on either side.</summary>
    Unchanged,

    /// <summary>This computer's value changed and is now published with a new version.</summary>
    LocalChange,

    /// <summary>Another computer's newer value replaces this computer's (it was edited with knowledge of this one).</summary>
    Imported,

    /// <summary>Both computers independently arrived at the same text — recorded as one version, no conflict.</summary>
    Merged,

    /// <summary>Both computers changed the field differently without seeing each other's edit. The local value is kept until the user chooses.</summary>
    Conflict,
}

/// <param name="Value">The value this computer should now hold; null only when it still has no value at all.</param>
/// <param name="State">What to remember for the next pass; null only when there is still nothing to remember.</param>
/// <param name="Other">For <see cref="MorningMessageFieldOutcome.Conflict"/>: the other computer's version, preserved until resolved.</param>
public sealed record MorningMessageFieldDecision(
    MorningMessageFieldOutcome Outcome,
    string? Value,
    MorningMessageFieldState? State,
    MorningMessageFieldVersion? Other = null);

/// <summary>
/// Phase 30: the pure, field-level merge rule for Morning Message sync. A field is replaced only when the
/// other computer's version provably descends from this computer's (its version vector covers ours); two
/// independent edits with different text are a conflict that keeps the local value and preserves the other
/// one for an explicit Keep This Device / Use Other Device choice. Never decided by clocks, file times, or
/// which computer synchronized last. Independent fields are reconciled independently, so reminders edited on
/// one computer and the break plan on another both survive.
/// </summary>
public static class MorningMessageFieldReconciliation
{
    /// <param name="localValue">This computer's current value, or null when it has none to offer (never typed, or still blank/default before the first sync of this field).</param>
    /// <param name="state">What this computer remembered after its last pass, or null if it never synchronized this field.</param>
    /// <param name="remotes">The field as published by other computers (any order; duplicates and stale copies are fine).</param>
    /// <param name="self">This computer's device id.</param>
    /// <param name="nextCounter">Issues this computer's next edit counter (strictly increasing across every field).</param>
    public static MorningMessageFieldDecision Decide(
        string? localValue,
        MorningMessageFieldState? state,
        IReadOnlyList<MorningMessageFieldVersion> remotes,
        Guid self,
        Func<long> nextCounter)
    {
        ArgumentNullException.ThrowIfNull(remotes);
        ArgumentNullException.ThrowIfNull(nextCounter);

        // 1. This computer's own version of the field, including any edit made since the last pass.
        MorningMessageFieldVersion? local;
        var outcome = MorningMessageFieldOutcome.Unchanged;
        if (localValue is null)
        {
            if (state is not null)
            {
                // A remembered field that now reads as "no value" is left alone; callers pass null with a
                // state only for values they cannot see. Absence is never treated as a deletion.
                return new MorningMessageFieldDecision(MorningMessageFieldOutcome.Unchanged, null, state);
            }

            local = null;
        }
        else if (state is null)
        {
            local = new MorningMessageFieldVersion(localValue, MorningMessageVersion.Empty.With(self, nextCounter()));
            outcome = MorningMessageFieldOutcome.LocalChange;
        }
        else if (MorningMessageSyncFields.Hash(localValue) != state.Hash)
        {
            local = new MorningMessageFieldVersion(localValue, state.Version.With(self, nextCounter()));
            outcome = MorningMessageFieldOutcome.LocalChange;
        }
        else
        {
            local = new MorningMessageFieldVersion(localValue, state.Version);
        }

        var localVersion = local?.Version ?? MorningMessageVersion.Empty;

        // 2. Other computers' versions this computer has not already seen, reduced to the newest ones.
        var unseen = remotes.Where(remote => !localVersion.Covers(remote.Version)).ToList();
        var frontier = unseen
            .Where(candidate => !unseen.Any(other => other.Version.CompareTo(candidate.Version) == MorningMessageVersionOrder.Newer))
            .GroupBy(candidate => candidate.Value, StringComparer.Ordinal)
            .Select(group => new MorningMessageFieldVersion(group.Key, group.Select(g => g.Version).Aggregate((a, b) => a.Merge(b))))
            .OrderBy(candidate => candidate.Version.ToString(), StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Value, StringComparer.Ordinal)
            .ToList();

        if (frontier.Count == 0)
        {
            return local is null
                ? new MorningMessageFieldDecision(MorningMessageFieldOutcome.Unchanged, null, null)
                : new MorningMessageFieldDecision(outcome, local.Value, new MorningMessageFieldState(MorningMessageSyncFields.Hash(local.Value), local.Version));
        }

        // 3. A newer version that descends from this computer's replaces it; equal text merges.
        var keep = local is null
            ? frontier[0]
            : frontier.FirstOrDefault(candidate => candidate.Version.Covers(localVersion)) ?? local;
        var keepVersion = frontier
            .Where(candidate => !ReferenceEquals(candidate, keep) && string.Equals(candidate.Value, keep.Value, StringComparison.Ordinal))
            .Aggregate(keep.Version, (version, candidate) => version.Merge(candidate.Version));
        var keepState = new MorningMessageFieldState(MorningMessageSyncFields.Hash(keep.Value), keepVersion);
        var other = frontier.FirstOrDefault(candidate => !string.Equals(candidate.Value, keep.Value, StringComparison.Ordinal));

        if (other is null)
        {
            var settled = ReferenceEquals(keep, local) ? MorningMessageFieldOutcome.Merged : MorningMessageFieldOutcome.Imported;
            return new MorningMessageFieldDecision(settled, keep.Value, keepState);
        }

        // 4. Independent, different edits (with this computer, or between two other computers): hold the kept
        //    value and preserve one other version for review. Any further candidate surfaces as its own
        //    conflict after this one is resolved, so none is ever dropped.
        return new MorningMessageFieldDecision(MorningMessageFieldOutcome.Conflict, keep.Value, keepState, other);
    }

    /// <summary>"Keep This Device": the local value, with a version that supersedes both sides so every computer adopts it.</summary>
    public static MorningMessageFieldState KeepLocal(string localValue, MorningMessageFieldState? state, MorningMessageFieldVersion other, Guid self, long counter)
    {
        ArgumentNullException.ThrowIfNull(localValue);
        ArgumentNullException.ThrowIfNull(other);
        var version = (state?.Version ?? MorningMessageVersion.Empty).Merge(other.Version).With(self, counter);
        return new MorningMessageFieldState(MorningMessageSyncFields.Hash(localValue), version);
    }

    /// <summary>"Use Other Device": the other value, with a version that supersedes both sides.</summary>
    public static MorningMessageFieldState UseOther(MorningMessageFieldState? state, MorningMessageFieldVersion other, Guid self, long counter)
    {
        ArgumentNullException.ThrowIfNull(other);
        var version = (state?.Version ?? MorningMessageVersion.Empty).Merge(other.Version).With(self, counter);
        return new MorningMessageFieldState(MorningMessageSyncFields.Hash(other.Value), version);
    }
}
