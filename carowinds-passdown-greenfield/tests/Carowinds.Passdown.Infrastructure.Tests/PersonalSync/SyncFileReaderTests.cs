using System.Text.Json;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §11: the safe reader must distinguish success/missing/malformed/unsupported-version/I-O without ever throwing a raw filesystem/JSON exception.</summary>
public class SyncFileReaderTests : IDisposable
{
    private sealed record Versioned(int SyncSchemaVersion, string Value);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-sync-file-reader-tests-{Guid.NewGuid():N}");
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = null };

    public SyncFileReaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadJsonAsync_MissingFile_ReturnsMissing()
    {
        var path = Path.Combine(_directory, "missing.json");

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, _ => true);

        Assert.Equal(SyncFileReadStatus.Missing, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadJsonAsync_ValidSupportedFile_ReturnsSuccess()
    {
        var path = Path.Combine(_directory, "valid.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Versioned(1, "hello"), Options));

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, v => v.SyncSchemaVersion <= 1);

        Assert.Equal(SyncFileReadStatus.Success, result.Status);
        Assert.Equal("hello", result.Value!.Value);
    }

    [Fact]
    public async Task ReadJsonAsync_MalformedJson_ReturnsMalformed()
    {
        var path = Path.Combine(_directory, "malformed.json");
        await File.WriteAllTextAsync(path, "{ not valid json");

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, _ => true);

        Assert.Equal(SyncFileReadStatus.Malformed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ReadJsonAsync_NullJson_ReturnsMalformed()
    {
        var path = Path.Combine(_directory, "null.json");
        await File.WriteAllTextAsync(path, "null");

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, _ => true);

        Assert.Equal(SyncFileReadStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task ReadJsonAsync_NewerUnsupportedVersion_ReturnsUnsupportedVersion()
    {
        var path = Path.Combine(_directory, "future.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Versioned(99, "future"), Options));

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, v => v.SyncSchemaVersion <= 1);

        Assert.Equal(SyncFileReadStatus.UnsupportedVersion, result.Status);
    }

    [Fact]
    public async Task ReadJsonAsync_FileExclusivelyLockedByAnotherHandle_ReturnsIoError()
    {
        var path = Path.Combine(_directory, "locked.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Versioned(1, "hello"), Options));

        using var exclusiveHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await SyncFileReader.ReadJsonAsync<Versioned>(path, Options, _ => true);

        Assert.Equal(SyncFileReadStatus.IoError, result.Status);
    }
}
