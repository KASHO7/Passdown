using System.Text.Json;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Calendar;
using Carowinds.Passdown.Application.Dashboard;
using Carowinds.Passdown.Application.OperationalNotes;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Shows;
using Carowinds.Passdown.Application.Tests.TestSupport;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Retrieval;

namespace Carowinds.Passdown.Application.Tests.Shows;

/// <summary>Phase 21C: the Dashboard and Operations Calendar read each date's own recorded show schedule, locally.</summary>
public class Phase21CDashboardCalendarShowsTests
{
    /// <summary>2026-10-16 3:00 PM Eastern.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 16);

    private static async Task SeedAsync(InMemoryDatedResultRepository dated, DateOnly date, string name)
    {
        var evidence = new SourceEvidence("Carowinds Show Schedule", new Uri("https://www.sixflags.com/carowinds/shows"), Now, RetrievalStatus.Verified,
            "Verified.", RetrievalStrategy.DirectApi, requestedDate: date, confirmedDate: date);
        await dated.RecordSuccessAsync(new DatedResultKey(SourceCategory.ShowSchedule, date), evidence,
            JsonSerializer.Serialize(new ShowSchedulePayload([new ScheduledShow("id:" + name, name, null, [new TimeOnly(19, 0)])])));
    }

    private static (OperationsDashboardService Dashboard, OperationsCalendarService Calendar, InMemoryDatedResultRepository Dated, FakeRefreshOrchestrator Orchestrator) Create()
    {
        var clock = new FakeClock(Now);
        var drafts = new InMemoryAssociateDraftRepository();
        var dated = new InMemoryDatedResultRepository();
        var orchestrator = new FakeRefreshOrchestrator();
        var notes = new OperationalNotesService(new InMemoryOperationalNoteRepository(), clock);
        var associate = new AssociatePassdownService(drafts, dated, new InMemoryCatalogProductRepository(), orchestrator, notes, clock);
        var finalized = new InMemoryFinalizedPassdownRepository();
        return (new OperationsDashboardService(associate, finalized, notes, clock),
            new OperationsCalendarService(associate, drafts, dated, finalized, notes, clock), dated, orchestrator);
    }

    [Fact]
    public async Task Dashboard_TodayShows_IsTodaysRecordedSchedule()
    {
        var env = Create();
        await SeedAsync(env.Dated, Today, "Wake the Dead");
        await SeedAsync(env.Dated, Today.AddDays(1), "Tomorrow Show");

        var snapshot = await env.Dashboard.GetTodayAsync();

        Assert.Equal(ShowScheduleState.Scheduled, snapshot.TodayShows.State);
        Assert.Equal("Wake the Dead", Assert.Single(snapshot.TodayShows.Shows).Name);
        Assert.Empty(env.Orchestrator.ManualDateRefreshCalls); // local only, never online
    }

    [Fact]
    public async Task Dashboard_NothingRecorded_IsNotChecked_NeverNoShows()
    {
        var snapshot = await Create().Dashboard.GetTodayAsync();

        Assert.Equal(ShowScheduleState.NotChecked, snapshot.TodayShows.State);
    }

    [Fact]
    public async Task Calendar_ChangingTheSelectedDate_NeverCarriesShowsOver()
    {
        var env = Create();
        await SeedAsync(env.Dated, Today.AddDays(1), "Saturday Show");

        var saturday = await env.Calendar.LoadAsync(new DateOnly(2026, 10, 1), Today.AddDays(1));
        var sunday = await env.Calendar.LoadAsync(new DateOnly(2026, 10, 1), Today.AddDays(2));

        Assert.Equal("Saturday Show", Assert.Single(saturday.Selected.Shows.Shows).Name);
        Assert.Equal(ShowScheduleState.NotChecked, sunday.Selected.Shows.State);
        Assert.Empty(sunday.Selected.Shows.Shows);
    }

    [Fact]
    public async Task Calendar_PastDateWithoutRecord_NeverShowsTodaysLineup()
    {
        var env = Create();
        await SeedAsync(env.Dated, Today, "Wake the Dead");

        var past = await env.Calendar.LoadAsync(new DateOnly(2026, 10, 1), Today.AddDays(-5));

        Assert.Equal(ShowScheduleState.HistoricalNotAvailable, past.Selected.Shows.State);
        Assert.Empty(past.Selected.Shows.Shows);
    }
}
