using Carowinds.Passdown.Domain.Retrieval;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Storage.Migrations;
using Carowinds.Passdown.Infrastructure.Storage.Repositories;
using Carowinds.Passdown.Infrastructure.Tests.PersonalSync;
using Carowinds.Passdown.Infrastructure.Tests.Storage.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carowinds.Passdown.Infrastructure.Tests.Storage;

/// <summary>Phase 31: the official reference document store and its additive migration.</summary>
public sealed class Phase31OfficialReferenceDocumentTests : IDisposable
{
    private static readonly Uri Url = new("https://www.sixflags.com/carowinds/events");
    private static readonly DateTimeOffset First = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(-5));
    private readonly SqliteRepositoryTestFixture _fixture = new();
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "phase31-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _fixture.Dispose();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
            // DirectoryNotFoundException included: nothing was created.
        }
    }

    private static SourceEvidence Evidence(DateTimeOffset at, RetrievalStatus status, string detail = "3 seasonal event schedules confirmed.") =>
        new("Carowinds Events", Url, at, status, detail, RetrievalStrategy.Html, technicalDetail: status == RetrievalStatus.Error ? "HTTP 502" : null);

    [Fact]
    public async Task FailedCheck_RecordsItsError_WithoutReplacingLastKnownGood()
    {
        var repository = new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory);
        await repository.SaveSuccessAsync("operating-events.official", Evidence(First, RetrievalStatus.Verified), "{\"Events\":[]}");

        await repository.SaveAttemptAsync("operating-events.official", Evidence(First.AddDays(1), RetrievalStatus.Error, "We couldn't check the official event schedule right now."));

        var record = await repository.GetAsync("operating-events.official");
        Assert.NotNull(record);
        Assert.Equal("{\"Events\":[]}", record!.SuccessPayloadJson);
        Assert.Equal(First, record.SuccessCheckedAt);
        Assert.True(record.LastAttemptFailed);
        Assert.Equal(First.AddDays(1), record.LastAttemptCheckedAt);
        Assert.Equal("HTTP 502", record.LastAttemptTechnicalDetail);
    }

    [Fact]
    public async Task FailureBeforeAnySuccess_HasNoPayload()
    {
        var repository = new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory);

        await repository.SaveAttemptAsync("deals.official", Evidence(First, RetrievalStatus.Error, "We couldn't check promotions right now."));

        var record = await repository.GetAsync("deals.official");
        Assert.Null(record!.SuccessPayloadJson);
        Assert.Null(record.SuccessCheckedAt);
        Assert.True(record.LastAttemptFailed);
    }

    [Fact]
    public async Task UnverifiedEvidence_CanNeverBecomeLastKnownGood()
    {
        var repository = new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.SaveSuccessAsync("deals.official", Evidence(First, RetrievalStatus.Error, "We couldn't check promotions right now."), "{}"));
    }

    [Fact]
    public async Task UnknownSource_IsNull()
    {
        Assert.Null(await new SqliteOfficialReferenceDocumentRepository(_fixture.ConnectionFactory).GetAsync("operating-events.official"));
    }

    [Fact]
    public void Rc8Profile_UpgradesTo18_AdditivelyPreservingEveryRow()
    {
        var profile = Path.Combine(_workspace, "rc8-profile");
        var factory = Rc8ProfileBuilder.Create(profile);
        var database = Path.Combine(profile, "carowinds-passdown.db");
        var before = Rc8ProfileBuilder.Dump(database);

        var migrations = Phase26OfficialPriceHistorySyncTests.AllMigrations().Append(new Migration0018CreateOfficialReferenceDocumentsTable()).ToList();
        Assert.Equal(18, new MigrationRunner(factory, migrations, NullLogger<MigrationRunner>.Instance).Run());

        Assert.Equal(18, Rc8ProfileBuilder.SchemaVersion(database));
        // Every pre-existing row is unchanged; the only differences are the two new tables and the
        // migration log rows that record versions 17 and 18.
        var after = Rc8ProfileBuilder.Dump(database, "synced_price_observations", "official_reference_documents");
        var newLogRows = after.Split('\n').Where(line => line.StartsWith("17|", StringComparison.Ordinal) || line.StartsWith("18|", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, newLogRows.Count);
        Assert.Equal(before, string.Join('\n', after.Split('\n').Where(line => !newLogRows.Contains(line))));
    }
}
