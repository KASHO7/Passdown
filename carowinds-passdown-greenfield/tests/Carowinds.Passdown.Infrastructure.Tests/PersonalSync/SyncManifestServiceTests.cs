using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §12: manifest.json bootstrap — created only when missing, never silently repaired if present but malformed/newer.</summary>
public class SyncManifestServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-manifest-tests-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureManifestAsync_NoManifestYet_WritesOne_AndReportsReady()
    {
        var result = await SyncManifestService.EnsureManifestAsync(_root, Now);

        Assert.Equal(SyncManifestEnsureOutcome.Ready, result.Outcome);
        Assert.True(File.Exists(SyncPathService.GetManifestFilePath(_root)));
    }

    [Fact]
    public async Task EnsureManifestAsync_WrittenManifest_HasCurrentSchemaVersionAndProductName()
    {
        await SyncManifestService.EnsureManifestAsync(_root, Now);

        var json = await File.ReadAllTextAsync(SyncPathService.GetManifestFilePath(_root));

        Assert.Contains($"\"SyncSchemaVersion\": {SyncManifestSerializer.CurrentSyncSchemaVersion}", json);
        Assert.Contains(SyncManifest.ProductName, json);
    }

    [Fact]
    public async Task EnsureManifestAsync_ExistingValidManifest_ReportsReady_AndDoesNotRewriteIt()
    {
        await SyncManifestService.EnsureManifestAsync(_root, Now);
        var manifestPath = SyncPathService.GetManifestFilePath(_root);
        var originalJson = await File.ReadAllTextAsync(manifestPath);

        var result = await SyncManifestService.EnsureManifestAsync(_root, Now.AddDays(1));

        Assert.Equal(SyncManifestEnsureOutcome.Ready, result.Outcome);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task EnsureManifestAsync_MalformedManifest_ReportsMalformed_AndDoesNotOverwriteIt()
    {
        var manifestPath = SyncPathService.GetManifestFilePath(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, "{ not valid json");

        var result = await SyncManifestService.EnsureManifestAsync(_root, Now);

        Assert.Equal(SyncManifestEnsureOutcome.Malformed, result.Outcome);
        Assert.Equal("{ not valid json", await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task EnsureManifestAsync_NewerUnsupportedSchemaVersion_ReportsUnsupportedVersion()
    {
        var manifestPath = SyncPathService.GetManifestFilePath(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var futureManifest = new SyncManifest(SyncManifestSerializer.CurrentSyncSchemaVersion + 1, SyncManifest.ProductName, Now);
        await File.WriteAllTextAsync(manifestPath, SyncManifestSerializer.Serialize(futureManifest));

        var result = await SyncManifestService.EnsureManifestAsync(_root, Now);

        Assert.Equal(SyncManifestEnsureOutcome.UnsupportedVersion, result.Outcome);
    }
}
