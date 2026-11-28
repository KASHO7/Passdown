using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.Storage;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard: the schema and format versions stated in the current architecture documentation
/// equal the source constants. This only checks the numbers — it is not evidence that the surrounding
/// prose is accurate (.claude/rules/documentation.md). A schema or sync-format change still needs
/// compatibility analysis and approval; when one is approved, update the documents in the same change.
/// </summary>
public partial class DocumentedSchemaVersionTests
{
    private const string Overview = "docs/architecture.md";
    private const string PersistenceGuide = "docs/architecture/data-and-persistence.md";

    [Fact]
    public void Migrations_AreContiguousFromOne_AndNamedForTheirVersion()
    {
        var migrations = Migrations();

        Assert.Equal(Enumerable.Range(1, migrations.Length), migrations.Select(migration => migration.Version));
        Assert.All(migrations, migration => Assert.StartsWith($"Migration{migration.Version:D4}", migration.GetType().Name, StringComparison.Ordinal));
    }

    [Fact]
    public void DesktopCompositionRoot_RegistersEveryMigration()
    {
        // The production list lives in Desktop (not referenced by this test project), so read the source.
        var app = RepositoryFiles.ReadText("src/Carowinds.Passdown.Desktop/App.axaml.cs");

        Assert.All(Migrations(), migration => Assert.Contains($"new {migration.GetType().Name}()", app, StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureOverview_StatesTheLatestSqliteSchemaAndMigration()
    {
        var latestMigration = Migrations()[^1];
        var latest = latestMigration.Version;
        var latestType = latestMigration.GetType();
        var text = RepositoryFiles.ReadText(Overview);

        Assert.Equal(latest, Stated(text, SqliteSchemaPattern(), Overview, "SQLite schema N"));
        Assert.Contains($"`{latestType.Name}`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceGuide_StatesTheLatestSqliteSchema()
    {
        Assert.Equal(
            Migrations()[^1].Version,
            Stated(RepositoryFiles.ReadText(PersistenceGuide), SqliteSchemaPattern(), PersistenceGuide, "SQLite schema N"));
    }

    [Theory]
    [InlineData("SyncEnvelopeSerializer.CurrentSyncSchemaVersion")]
    [InlineData("AssociateDraftPayload.CurrentSchemaVersion")]
    [InlineData("FinalizedPassdownSnapshot.CurrentSchemaVersion")]
    public void ArchitectureOverview_StatesTheSourceFormatVersion(string constant)
    {
        var expected = constant switch
        {
            "SyncEnvelopeSerializer.CurrentSyncSchemaVersion" => SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
            "AssociateDraftPayload.CurrentSchemaVersion" => AssociateDraftPayload.CurrentSchemaVersion,
            "FinalizedPassdownSnapshot.CurrentSchemaVersion" => FinalizedPassdownSnapshot.CurrentSchemaVersion,
            _ => throw new ArgumentOutOfRangeException(nameof(constant)),
        };
        var pattern = new Regex($"`{Regex.Escape(constant)}`\\s+(\\d+)");

        Assert.Equal(expected, Stated(RepositoryFiles.ReadText(Overview), pattern, Overview, $"`{constant}` N"));
    }

    /// <summary>Every <see cref="IMigration"/> in Infrastructure, ordered by its own <see cref="IMigration.Version"/>.</summary>
    private static IMigration[] Migrations() =>
        typeof(IMigration).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IMigration).IsAssignableFrom(type))
            .Select(type => (IMigration)Activator.CreateInstance(type)!)
            .OrderBy(migration => migration.Version)
            .ToArray();

    private static int Stated(string text, Regex pattern, string document, string form)
    {
        var values = pattern.Matches(text).Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        Assert.True(values.Length == 1, $"{document} must state the value exactly once as \"{form}\" (found: [{string.Join(", ", values)}]).");
        return values[0];
    }

    [GeneratedRegex(@"SQLite schema (\d+)")]
    private static partial Regex SqliteSchemaPattern();
}
