using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;

namespace Carowinds.Passdown.Application.Tests.MorningMessage;

/// <summary>Phase 30: the pure rules behind Morning Message sync — version vectors, field encodings and the field-level merge.</summary>
public sealed class MorningMessageSyncRulesTests
{
    private static readonly Guid A = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = new("cccccccc-0000-0000-0000-000000000003");

    private static MorningMessageVersion V(params (Guid Device, long Counter)[] counters) =>
        MorningMessageVersion.TryCreate(counters.Select(c => new KeyValuePair<Guid, long>(c.Device, c.Counter)))!;

    private static MorningMessageFieldState State(string value, MorningMessageVersion version) => new(MorningMessageSyncFields.Hash(value), version);

    private static MorningMessageFieldVersion Remote(string value, MorningMessageVersion version) => new(value, version);

    private sealed class Counter(long start = 0)
    {
        public long Value { get; private set; } = start;

        public long Next() => ++Value;
    }

    // ----- Version vectors -----

    [Fact]
    public void Version_Compare_DetectsNewerOlderEqualAndConcurrent()
    {
        Assert.Equal(MorningMessageVersionOrder.Equal, V((A, 1)).CompareTo(V((A, 1))));
        Assert.Equal(MorningMessageVersionOrder.Newer, V((A, 2)).CompareTo(V((A, 1))));
        Assert.Equal(MorningMessageVersionOrder.Older, V((A, 1)).CompareTo(V((A, 1), (B, 1))));
        Assert.Equal(MorningMessageVersionOrder.Concurrent, V((A, 2)).CompareTo(V((A, 1), (B, 1))));
        Assert.Equal(MorningMessageVersionOrder.Newer, V((A, 1)).CompareTo(MorningMessageVersion.Empty));
    }

    [Fact]
    public void Version_Merge_TakesTheHighestCounterPerDevice_AndCoversBoth()
    {
        var merged = V((A, 3), (B, 1)).Merge(V((A, 1), (B, 4), (C, 2)));

        Assert.Equal(V((A, 3), (B, 4), (C, 2)), merged);
        Assert.True(merged.Covers(V((A, 3), (B, 1))));
        Assert.True(merged.Covers(V((A, 1), (B, 4), (C, 2))));
    }

    [Fact]
    public void Version_TryCreate_RejectsEmptyDeviceIds_NonPositiveCounters_DuplicatesAndTooManyEntries()
    {
        Assert.Null(MorningMessageVersion.TryCreate([new(Guid.Empty, 1)]));
        Assert.Null(MorningMessageVersion.TryCreate([new(A, 0)]));
        Assert.Null(MorningMessageVersion.TryCreate([new(A, -5)]));
        Assert.Null(MorningMessageVersion.TryCreate([new(A, 1), new(A, 2)]));
        Assert.Null(MorningMessageVersion.TryCreate(Enumerable.Range(0, MorningMessageVersion.MaxEntries + 1).Select(_ => new KeyValuePair<Guid, long>(Guid.NewGuid(), 1))));
        Assert.NotNull(MorningMessageVersion.TryCreate(Enumerable.Range(0, MorningMessageVersion.MaxEntries).Select(_ => new KeyValuePair<Guid, long>(Guid.NewGuid(), 1))));
    }

    // ----- Field encodings -----

    [Fact]
    public void Content_RoundTrips_EveryField_ButNeverTheLocalImagePath()
    {
        var content = new MorningMessageContent("Hydrate\r\n- Smile 🎢", "Avery   10:30\nJordan  11:00", "Lot B", true, @"C:\Users\someone\dap.png", "Ride 4 down <b>&</b>", "Prices \"held\" 'today'");

        var fields = MorningMessageSyncFields.FromContent(content);
        var applied = MorningMessageSyncFields.ApplyToContent(MorningMessageContent.Empty with { DapImagePath = "/local/other.png" }, fields);

        Assert.Equal(MorningMessageSyncFields.ContentFields.Order(), fields.Keys.Order());
        Assert.DoesNotContain(fields.Values, value => value.Contains("dap.png", StringComparison.Ordinal));
        Assert.Equal(content with { DapImagePath = "/local/other.png" }, applied);
        Assert.All(fields, field => Assert.True(MorningMessageSyncFields.IsValidValue(field.Key, field.Value)));
    }

    [Fact]
    public void Preferences_RoundTrip_GreetingOrderVisibilityPricesBulletsAndCopyMode()
    {
        var layout = MorningMessageLayout.Default.MoveUp(MorningMessageSectionId.DailyFeature).WithEnabled(MorningMessageSectionId.DapPlacement, false);
        var preferences = new MorningMessagePreferences(
            "Gooood Morning, team! ☀️", layout, TeamsCopyMode.Formatted, [ProductDisplaySection.DailyAdmission, ProductDisplaySection.SeasonPasses], RemindersAsBullets: true);

        var fields = MorningMessageSyncFields.FromPreferences(preferences);
        var applied = MorningMessageSyncFields.ApplyToPreferences(MorningMessagePreferences.Default, fields);

        Assert.Equal(preferences, applied);
        Assert.Equal("Greeting,Operations,-DapPlacement,Reminders,TicketPrices,DailyFeature,BreakPlan", fields[MorningMessageSyncFields.Sections]);
        Assert.All(fields, field => Assert.True(MorningMessageSyncFields.IsValidValue(field.Key, field.Value)));
    }

    [Theory]
    [InlineData("Greeting,Operations,DapPlacement,Reminders,TicketPrices,BreakPlan")] // a section missing
    [InlineData("Greeting,Operations,DapPlacement,Reminders,TicketPrices,BreakPlan,DailyFeature,Greeting")] // duplicated
    [InlineData("Greeting,Operations,DapPlacement,Reminders,TicketPrices,BreakPlan,DailyFeature,OdNotes")] // unknown — OD Notes are never a section
    [InlineData("greeting,Operations,DapPlacement,Reminders,TicketPrices,BreakPlan,DailyFeature")] // wrong case
    [InlineData("Greeting,Operations,DapPlacement,Reminders,TicketPrices,BreakPlan,7")] // numeric value
    [InlineData("")]
    public void Sections_UnexpectedSectionLists_AreRejected(string value) =>
        Assert.False(MorningMessageSyncFields.IsValidValue(MorningMessageSyncFields.Sections, value));

    [Theory]
    [InlineData(MorningMessageSyncFields.PriceSections, "Memberships", false)] // never selectable
    [InlineData(MorningMessageSyncFields.PriceSections, "FastLane,DailyAdmission", false)] // not canonical order
    [InlineData(MorningMessageSyncFields.PriceSections, "DailyAdmission,DailyAdmission", false)]
    [InlineData(MorningMessageSyncFields.PriceSections, "", true)]
    [InlineData(MorningMessageSyncFields.DapImageReminder, "yes", false)]
    [InlineData(MorningMessageSyncFields.RemindersAsBullets, "true", true)]
    [InlineData(MorningMessageSyncFields.DefaultCopyMode, "1", false)]
    [InlineData(MorningMessageSyncFields.DefaultCopyMode, "Formatted", true)]
    [InlineData(MorningMessageSyncFields.Greeting, " Padded ", false)]
    [InlineData("Shows", "anything", false)] // a field this version does not know
    public void Values_AreValidatedPerField(string field, string value, bool valid) =>
        Assert.Equal(valid, MorningMessageSyncFields.IsValidValue(field, value));

    [Fact]
    public void TextLength_IsBounded()
    {
        Assert.True(MorningMessageSyncFields.IsValidValue(MorningMessageSyncFields.Reminders, new string('x', MorningMessageContent.MaxFieldLength)));
        Assert.False(MorningMessageSyncFields.IsValidValue(MorningMessageSyncFields.Reminders, new string('x', MorningMessageContent.MaxFieldLength + 1)));
        Assert.False(MorningMessageSyncFields.IsValidValue(MorningMessageSyncFields.Greeting, new string('x', MorningMessagePreferences.MaxGreetingLength + 1)));
        Assert.False(MorningMessageSyncFields.IsValidValue(MorningMessageSyncFields.Reminders, null));
    }

    [Fact]
    public void UnsetValues_AreBlankTextAndAnUncheckedReminder_AndDefaultsAreFreshInstallPreferences()
    {
        Assert.True(MorningMessageSyncFields.IsUnsetContentValue(MorningMessageSyncFields.Reminders, "  \n"));
        Assert.True(MorningMessageSyncFields.IsUnsetContentValue(MorningMessageSyncFields.DapImageReminder, "false"));
        Assert.False(MorningMessageSyncFields.IsUnsetContentValue(MorningMessageSyncFields.OdNotes, "x"));
        Assert.All(MorningMessageSyncFields.FromPreferences(MorningMessagePreferences.Default),
            field => Assert.True(MorningMessageSyncFields.IsDefaultPreferenceValue(field.Key, field.Value)));
        Assert.False(MorningMessageSyncFields.IsDefaultPreferenceValue(MorningMessageSyncFields.Greeting, "Hello"));
    }

    // ----- Field-level merge -----

    [Fact]
    public void FirstSync_NoLocalValue_ImportsTheOtherComputersValue()
    {
        var decision = MorningMessageFieldReconciliation.Decide(null, null, [Remote("Parking meeting 9:00", V((B, 1)))], A, new Counter().Next);

        Assert.Equal(MorningMessageFieldOutcome.Imported, decision.Outcome);
        Assert.Equal("Parking meeting 9:00", decision.Value);
        Assert.Equal(V((B, 1)), decision.State!.Version);
    }

    [Fact]
    public void FirstSync_LocalValue_NoRemote_IsPublishedWithANewVersion()
    {
        var counter = new Counter(6);
        var decision = MorningMessageFieldReconciliation.Decide("Mine", null, [], A, counter.Next);

        Assert.Equal(MorningMessageFieldOutcome.LocalChange, decision.Outcome);
        Assert.Equal(V((A, 7)), decision.State!.Version);
    }

    [Fact]
    public void FirstSync_IdenticalTextOnBothComputers_MergesWithoutConflict()
    {
        var decision = MorningMessageFieldReconciliation.Decide("Same", null, [Remote("Same", V((B, 3)))], A, new Counter().Next);

        Assert.Equal(MorningMessageFieldOutcome.Merged, decision.Outcome);
        Assert.Equal(V((A, 1), (B, 3)), decision.State!.Version);
        Assert.Null(decision.Other);
    }

    [Fact]
    public void FirstSync_DifferentTextOnBothComputers_IsAConflict_KeepingLocal_AndPreservingTheOther()
    {
        var decision = MorningMessageFieldReconciliation.Decide("Parking meeting at 9:00.", null, [Remote("Parking meeting at 9:30.", V((B, 1)))], A, new Counter().Next);

        Assert.Equal(MorningMessageFieldOutcome.Conflict, decision.Outcome);
        Assert.Equal("Parking meeting at 9:00.", decision.Value);
        Assert.Equal("Parking meeting at 9:30.", decision.Other!.Value);
        Assert.Equal(MorningMessageSyncFields.Hash("Parking meeting at 9:00."), decision.State!.Hash);
    }

    [Fact]
    public void RemoteEditedAfterSeeingOurs_ReplacesOurs()
    {
        var state = State("Old", V((A, 1)));
        var decision = MorningMessageFieldReconciliation.Decide("Old", state, [Remote("New", V((A, 1), (B, 1)))], A, new Counter(1).Next);

        Assert.Equal(MorningMessageFieldOutcome.Imported, decision.Outcome);
        Assert.Equal("New", decision.Value);
    }

    [Fact]
    public void LocalEditSinceLastSync_IsNeverOverwrittenByAnOlderRemote()
    {
        var state = State("Old", V((A, 1)));
        var decision = MorningMessageFieldReconciliation.Decide("Edited here", state, [Remote("Old", V((A, 1)))], A, new Counter(1).Next);

        Assert.Equal(MorningMessageFieldOutcome.LocalChange, decision.Outcome);
        Assert.Equal("Edited here", decision.Value);
        Assert.Equal(V((A, 2)), decision.State!.Version);
    }

    [Fact]
    public void BothEditedTheSameFieldSinceTheCommonBaseline_IsAConflict_NotDecidedByClocks()
    {
        var baseline = V((A, 1));
        var decision = MorningMessageFieldReconciliation.Decide("A's edit", State("Base", baseline), [Remote("B's edit", V((A, 1), (B, 1)).Merge(V((B, 1))))], A, new Counter(1).Next);

        // B's version covers A:1 but not A's new edit (A:2) — so they are concurrent.
        Assert.Equal(MorningMessageFieldOutcome.Conflict, decision.Outcome);
        Assert.Equal("A's edit", decision.Value);
        Assert.Equal("B's edit", decision.Other!.Value);
    }

    [Fact]
    public void Repeating_AnUnchangedPass_IsUnchanged_AndUsesNoCounter()
    {
        var counter = new Counter(4);
        var state = State("Same", V((A, 4), (B, 2)));
        var decision = MorningMessageFieldReconciliation.Decide("Same", state, [Remote("Same", V((A, 4), (B, 2))), Remote("Older", V((B, 1)))], A, counter.Next);

        Assert.Equal(MorningMessageFieldOutcome.Unchanged, decision.Outcome);
        Assert.Equal(4, counter.Value);
    }

    [Fact]
    public void TwoOtherComputersDisagree_AndThisOneHasNothing_AdoptsOne_AndKeepsTheOtherForReview()
    {
        var decision = MorningMessageFieldReconciliation.Decide(null, null, [Remote("From B", V((B, 1))), Remote("From C", V((C, 1)))], A, new Counter().Next);

        Assert.Equal(MorningMessageFieldOutcome.Conflict, decision.Outcome);
        Assert.NotNull(decision.Value);
        Assert.NotNull(decision.Other);
        Assert.NotEqual(decision.Value, decision.Other!.Value);
        Assert.Equal(["From B", "From C"], new[] { decision.Value!, decision.Other.Value }.Order());
    }

    [Fact]
    public void ANewerDescendantOfOurs_IsAdoptedFirst_ThenAnIndependentThirdValueIsTheConflict()
    {
        var state = State("Base", V((A, 1)));
        var decision = MorningMessageFieldReconciliation.Decide(
            "Base", state, [Remote("B built on ours", V((A, 1), (B, 1))), Remote("C independent", V((C, 1)))], A, new Counter(1).Next);

        Assert.Equal(MorningMessageFieldOutcome.Conflict, decision.Outcome);
        Assert.Equal("B built on ours", decision.Value);
        Assert.Equal("C independent", decision.Other!.Value);
    }

    [Fact]
    public void KeepLocal_AndUseOther_ProduceAVersionThatSupersedesBothSides()
    {
        var mine = State("Mine", V((A, 2)));
        var theirs = Remote("Theirs", V((A, 1), (B, 1)));

        var kept = MorningMessageFieldReconciliation.KeepLocal("Mine", mine, theirs, A, 3);
        var used = MorningMessageFieldReconciliation.UseOther(mine, theirs, A, 3);

        foreach (var resolved in new[] { kept, used })
        {
            Assert.Equal(MorningMessageVersionOrder.Newer, resolved.Version.CompareTo(mine.Version));
            Assert.Equal(MorningMessageVersionOrder.Newer, resolved.Version.CompareTo(theirs.Version));
        }

        Assert.Equal(MorningMessageSyncFields.Hash("Mine"), kept.Hash);
        Assert.Equal(MorningMessageSyncFields.Hash("Theirs"), used.Hash);

        // The other computer, still holding "Theirs", now simply imports the resolution.
        var onB = MorningMessageFieldReconciliation.Decide("Theirs", State("Theirs", theirs.Version), [Remote("Mine", kept.Version)], B, new Counter(1).Next);
        Assert.Equal(MorningMessageFieldOutcome.Imported, onB.Outcome);
        Assert.Equal("Mine", onB.Value);
    }

    [Fact]
    public void ARememberedFieldWithNoVisibleValue_IsLeftAlone_NeverTreatedAsDeletion()
    {
        var state = State("Kept", V((A, 1)));
        var decision = MorningMessageFieldReconciliation.Decide(null, state, [Remote("Newer", V((A, 1), (B, 1)))], A, new Counter(1).Next);

        Assert.Equal(MorningMessageFieldOutcome.Unchanged, decision.Outcome);
        Assert.Null(decision.Value);
        Assert.Same(state, decision.State);
    }

    // ----- Editor session -----

    [Fact]
    public async Task Session_RequestsSyncOnlyWhenEnabled_AndIgnoresEmptyChanges()
    {
        var session = new MorningMessageEditorSession();
        var requests = 0;
        var changes = 0;
        session.SyncRequested += (_, _) => requests++;
        session.Changed += (_, _) => changes++;

        session.RequestSync();
        session.SyncEnabled = true;
        session.RequestSync();
        session.PublishChange(new MorningMessageSyncChange([], false, [], false));
        session.PublishChange(new MorningMessageSyncChange([new DateOnly(2026, 10, 22)], false, [], false));

        Assert.Equal(1, requests);
        Assert.Equal(1, changes);

        using (await session.LockAsync())
        {
            session.MarkOpen(new DateOnly(2026, 10, 22));
        }

        Assert.True(session.IsComposerOpen);
        using (await session.LockAsync())
        {
            session.MarkClosed();
        }

        Assert.False(session.IsComposerOpen);
    }
}
