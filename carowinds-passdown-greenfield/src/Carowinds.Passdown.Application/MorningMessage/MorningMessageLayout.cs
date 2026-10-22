namespace Carowinds.Passdown.Application.MorningMessage;

/// <summary>
/// Phase 29: the sections of the Teams morning message. Persisted by name in the local preference file,
/// so values may be appended but never renamed. OD Notes are deliberately not a member: they are always a
/// separate copy, never part of the main message.
/// </summary>
public enum MorningMessageSectionId
{
    Greeting,
    Operations,
    DapPlacement,
    Reminders,
    TicketPrices,
    BreakPlan,
    DailyFeature,
}

/// <summary>Phase 29: what a Teams copy action places on the clipboard.</summary>
public enum TeamsCopyMode
{
    /// <summary>Plain text only (the Phase 18F behavior).</summary>
    PlainText,

    /// <summary>HTML (Windows "HTML Format") plus the identical plain text alongside it.</summary>
    Formatted,
}

/// <summary>One section's position-independent setting; its position is its index in <see cref="MorningMessageLayout.Sections"/>.</summary>
public sealed record MorningMessageSectionSetting(MorningMessageSectionId Id, bool Enabled);

/// <summary>
/// Phase 29: the ordered, individually enabled sections of the morning message. Pure — every operation
/// returns a new layout, and <see cref="Normalize"/> turns any stored list (old, partial, duplicated or
/// containing unknown values) into a complete, valid layout, so a preference file written by another
/// version can never drop or duplicate a section.
/// </summary>
public sealed record MorningMessageLayout
{
    private MorningMessageLayout(IReadOnlyList<MorningMessageSectionSetting> sections) => Sections = sections;

    /// <summary>Always contains every <see cref="MorningMessageSectionId"/> exactly once.</summary>
    public IReadOnlyList<MorningMessageSectionSetting> Sections { get; }

    /// <summary>The user's preferred workflow: greeting, operations, DAP, reminders, prices, break plan, then Daily Feature last.</summary>
    public static IReadOnlyList<MorningMessageSectionId> DefaultOrder { get; } =
    [
        MorningMessageSectionId.Greeting,
        MorningMessageSectionId.Operations,
        MorningMessageSectionId.DapPlacement,
        MorningMessageSectionId.Reminders,
        MorningMessageSectionId.TicketPrices,
        MorningMessageSectionId.BreakPlan,
        MorningMessageSectionId.DailyFeature,
    ];

    public static MorningMessageLayout Default { get; } = new(DefaultOrder.Select(id => new MorningMessageSectionSetting(id, true)).ToList());

    /// <summary>
    /// Keeps the first occurrence of each defined section in stored order and inserts any missing section
    /// (enabled) at its default position relative to the sections before it — a section added by a future
    /// version lands where the default order puts it rather than at the very end.
    /// </summary>
    public static MorningMessageLayout Normalize(IEnumerable<MorningMessageSectionSetting>? stored)
    {
        var result = new List<MorningMessageSectionSetting>();
        foreach (var setting in stored ?? [])
        {
            if (setting is not null && Enum.IsDefined(setting.Id) && result.All(existing => existing.Id != setting.Id))
            {
                result.Add(setting);
            }
        }

        foreach (var id in DefaultOrder)
        {
            if (result.Any(existing => existing.Id == id))
            {
                continue;
            }

            var defaultIndex = DefaultOrder.ToList().IndexOf(id);
            var insertAt = 0;
            for (var i = 0; i < result.Count; i++)
            {
                if (DefaultOrder.ToList().IndexOf(result[i].Id) < defaultIndex)
                {
                    insertAt = i + 1;
                }
            }

            result.Insert(insertAt, new MorningMessageSectionSetting(id, true));
        }

        return new MorningMessageLayout(result);
    }

    public int IndexOf(MorningMessageSectionId id) => Sections.ToList().FindIndex(setting => setting.Id == id);

    public bool IsEnabled(MorningMessageSectionId id) => Sections.First(setting => setting.Id == id).Enabled;

    public bool CanMoveUp(MorningMessageSectionId id) => IndexOf(id) > 0;

    public bool CanMoveDown(MorningMessageSectionId id) => IndexOf(id) < Sections.Count - 1;

    public MorningMessageLayout MoveUp(MorningMessageSectionId id) => Swap(IndexOf(id), IndexOf(id) - 1);

    public MorningMessageLayout MoveDown(MorningMessageSectionId id) => Swap(IndexOf(id), IndexOf(id) + 1);

    public MorningMessageLayout WithEnabled(MorningMessageSectionId id, bool enabled) =>
        new(Sections.Select(setting => setting.Id == id ? setting with { Enabled = enabled } : setting).ToList());

    /// <summary>The enabled sections, in message order.</summary>
    public IReadOnlyList<MorningMessageSectionId> EnabledInOrder() =>
        Sections.Where(setting => setting.Enabled).Select(setting => setting.Id).ToList();

    public bool Equals(MorningMessageLayout? other) => other is not null && Sections.SequenceEqual(other.Sections);

    public override int GetHashCode() => Sections.Aggregate(17, (hash, setting) => hash * 31 + setting.GetHashCode());

    private MorningMessageLayout Swap(int from, int to)
    {
        if (from < 0 || to < 0 || to >= Sections.Count)
        {
            return this;
        }

        var list = Sections.ToList();
        (list[from], list[to]) = (list[to], list[from]);
        return new MorningMessageLayout(list);
    }
}
