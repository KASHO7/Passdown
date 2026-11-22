using System.Net;
using Carowinds.Passdown.Application.PassPerks;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Application.Refresh;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Domain.PassPerks;
using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Preferences;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.PassPerks;

/// <summary>
/// Phase 32: Pass Perks need no schema change — the official fixtures go through the real source and
/// registration into the existing <c>official_reference_documents</c> table (schema 18), a failed check keeps
/// the last-known-good document, and the "Show Pass Perks on Calendar" preference lives in its own local file.
/// </summary>
public sealed class Phase32PassPerksStorageTests : IDisposable
{
    private static readonly DateTimeOffset Nov21Utc = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);
    private readonly SqliteRepositoryTestFixture _fixture = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-calendar-display-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static RetrievalHttpResponse Fixture(string name) => new(HttpStatusCode.OK, "text/html", FixtureFile.ReadText("phase32/" + name));

    private PassPerksSourceRegistration Registration(SqliteOfficialReferenceDocumentRepository repository, StubHttpClient client, DateTimeOffset now) =>
        new(new PassPerksSource(client, new FakeClock(now)), repository);

    [Fact]
    public async Task OfficialFixtures_AreStoredInTheExistingReferenceTable_AndAFailedCheckKeepsThem()
    {
        var repository = new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory);

        var ok = await Registration(repository, new StubHttpClient([Fixture("carowinds-perks-and-play.html"), Fixture("pass-perks-terms.html"), Fixture("perks-and-play-faq.html")]), Nov21Utc)
            .ExecuteAsync(null, CancellationToken.None);
        var failed = await Registration(repository, new StubHttpClient([new RetrievalHttpResponse(HttpStatusCode.BadGateway, "text/html", "")]), Nov21Utc.AddDays(1))
            .ExecuteAsync(null, CancellationToken.None);

        Assert.Equal(SourceOperationOutcomeKind.Success, ok.Outcome);
        Assert.Equal(SourceOperationOutcomeKind.Failure, failed.Outcome);
        var service = new PassPerksService(repository, new FakeClock(Nov21Utc.AddDays(1)));
        var overview = await service.GetOverviewAsync();
        Assert.Equal(OfficialDocumentFreshness.LastCheckFailed, overview.Status.Freshness);
        Assert.Equal(7, overview.ForMonth(2026, 11).Entries.Count);
        Assert.Equal("Updated July 30, 2026", overview.ProgramTerms!.TermsUpdatedText);
        Assert.Single(overview.ProgramTerms.Discrepancies);
    }

    [Fact]
    public async Task ChangedOfficialTerms_KeepThePreviousWordingThroughSqlite()
    {
        var repository = new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory);
        var park = FixtureFile.ReadText("phase32/carowinds-perks-and-play.html");
        StubHttpClient Client(string body) => new([new RetrievalHttpResponse(HttpStatusCode.OK, "text/html", body), Fixture("pass-perks-terms.html"), Fixture("perks-and-play-faq.html")]);

        await Registration(repository, Client(park), Nov21Utc).ExecuteAsync(null, CancellationToken.None);
        await Registration(repository, Client(park.Replace("November 27 - January 3", "November 27 - January 10", StringComparison.Ordinal)), Nov21Utc.AddDays(1))
            .ExecuteAsync(null, CancellationToken.None);

        var overview = await new PassPerksService(repository, new FakeClock(Nov21Utc.AddDays(1))).GetOverviewAsync();
        var mug = overview.ForMonth(2026, 11).Entries.Single(e => e.Perk.Title.Contains("Hot Chocolate", StringComparison.Ordinal)).Perk;
        Assert.Equal(new DateOnly(2027, 1, 10), mug.Redemption!.End);
        Assert.Equal("November 27 - January 3", Assert.Single(mug.PreviousVersions).DateText);
    }

    [Fact]
    public async Task CalendarDisplayFile_DefaultsOn_RoundTrips_AndToleratesDamage()
    {
        var path = Path.Combine(_directory, "calendar-display.json");
        var store = new FileCalendarDisplayPreferenceStore(path);

        Assert.Equal(CalendarDisplayPreferences.BuiltIn, await store.LoadAsync());
        Assert.False(File.Exists(path)); // loading creates nothing

        await store.SaveAsync(new CalendarDisplayPreferences(ShowPassPerks: false));
        Assert.False((await new FileCalendarDisplayPreferenceStore(path).LoadAsync()).ShowPassPerks); // survives a restart

        await File.WriteAllTextAsync(path, "{ not json");
        Assert.True((await store.LoadAsync()).ShowPassPerks);

        await File.WriteAllTextAsync(path, "{}");
        Assert.True((await store.LoadAsync()).ShowPassPerks); // a missing value never means "off"
    }

    [Fact]
    public void CalendarDisplayFile_IsLocalAppData_NotTheDatabaseOrSyncFolder()
    {
        Assert.Equal("calendar-display.json", Path.GetFileName(FileCalendarDisplayPreferenceStore.DefaultPath));
        Assert.Equal(Path.GetFullPath(AppPaths.LocalApplicationDataDirectory), Path.GetDirectoryName(Path.GetFullPath(FileCalendarDisplayPreferenceStore.DefaultPath)));
    }
}
