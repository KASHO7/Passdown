namespace Carowinds.Passdown.Application.MorningMessage.Sync;

/// <summary>How two <see cref="MorningMessageVersion"/>s relate.</summary>
public enum MorningMessageVersionOrder
{
    /// <summary>The same edit history.</summary>
    Equal,

    /// <summary>The left side has seen every edit the right side has, and more.</summary>
    Newer,

    /// <summary>The right side has seen every edit the left side has, and more.</summary>
    Older,

    /// <summary>Each side has an edit the other has not seen — edited independently.</summary>
    Concurrent,
}

/// <summary>
/// Phase 30: a version vector for one synchronized Morning Message field — for each computer (anonymous
/// Personal Sync device id), the highest edit counter of that computer this value has seen. Comparing two
/// vectors proves whether one value was edited with knowledge of the other (<see cref="MorningMessageVersionOrder.Newer"/>
/// / <see cref="MorningMessageVersionOrder.Older"/>) or independently (<see cref="MorningMessageVersionOrder.Concurrent"/>),
/// without trusting any computer's clock or file modification time. Immutable.
/// </summary>
public sealed class MorningMessageVersion : IEquatable<MorningMessageVersion>
{
    /// <summary>Most computers a single version may name — far beyond one person's machines; larger input is rejected.</summary>
    public const int MaxEntries = 64;

    private readonly SortedDictionary<Guid, long> _counters;

    private MorningMessageVersion(SortedDictionary<Guid, long> counters) => _counters = counters;

    public static MorningMessageVersion Empty { get; } = new([]);

    public IReadOnlyDictionary<Guid, long> Counters => _counters;

    public bool IsEmpty => _counters.Count == 0;

    /// <summary>Null when <paramref name="counters"/> is not a valid version: an empty device id, a counter below 1, or too many entries.</summary>
    public static MorningMessageVersion? TryCreate(IEnumerable<KeyValuePair<Guid, long>>? counters)
    {
        var result = new SortedDictionary<Guid, long>();
        foreach (var (device, counter) in counters ?? [])
        {
            if (device == Guid.Empty || counter < 1 || !result.TryAdd(device, counter) || result.Count > MaxEntries)
            {
                return null;
            }
        }

        return new MorningMessageVersion(result);
    }

    public long CounterFor(Guid device) => _counters.TryGetValue(device, out var counter) ? counter : 0;

    /// <summary>This version with <paramref name="device"/>'s counter set to <paramref name="counter"/> (a new local edit).</summary>
    public MorningMessageVersion With(Guid device, long counter)
    {
        var copy = new SortedDictionary<Guid, long>(_counters) { [device] = Math.Max(counter, CounterFor(device)) };
        return new MorningMessageVersion(copy);
    }

    /// <summary>The smallest version that has seen everything both versions have seen.</summary>
    public MorningMessageVersion Merge(MorningMessageVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var copy = new SortedDictionary<Guid, long>(_counters);
        foreach (var (device, counter) in other._counters)
        {
            copy[device] = Math.Max(counter, CounterFor(device));
        }

        return new MorningMessageVersion(copy);
    }

    public MorningMessageVersionOrder CompareTo(MorningMessageVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var thisAhead = false;
        var otherAhead = false;
        foreach (var device in _counters.Keys.Union(other._counters.Keys))
        {
            var mine = CounterFor(device);
            var theirs = other.CounterFor(device);
            thisAhead |= mine > theirs;
            otherAhead |= theirs > mine;
        }

        return (thisAhead, otherAhead) switch
        {
            (false, false) => MorningMessageVersionOrder.Equal,
            (true, false) => MorningMessageVersionOrder.Newer,
            (false, true) => MorningMessageVersionOrder.Older,
            _ => MorningMessageVersionOrder.Concurrent,
        };
    }

    /// <summary>True when this version has seen every edit <paramref name="other"/> has (equal or newer).</summary>
    public bool Covers(MorningMessageVersion other) => CompareTo(other) is MorningMessageVersionOrder.Equal or MorningMessageVersionOrder.Newer;

    public bool Equals(MorningMessageVersion? other) => other is not null && CompareTo(other) == MorningMessageVersionOrder.Equal;

    public override bool Equals(object? obj) => Equals(obj as MorningMessageVersion);

    public override int GetHashCode() => _counters.Aggregate(17, (hash, entry) => HashCode.Combine(hash, entry.Key, entry.Value));

    /// <summary>A stable text form, used only to order candidates deterministically.</summary>
    public override string ToString() => string.Join(",", _counters.Select(entry => $"{entry.Key:N}:{entry.Value}"));
}
