using System.Text;
using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>
/// Phase 18B REL-003: fault injection at every stage of <see cref="AtomicFileWriter"/>'s
/// temp-write → flush-to-disk → rename sequence. No test cuts power; each proves the property power-
/// loss safety rests on — the destination only ever holds a complete old or complete new version, and
/// a temp file never becomes (or is mistaken for) the authoritative file.
/// </summary>
public class AtomicFileWriterFaultTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-atomic-fault-tests-{Guid.NewGuid():N}");

    public AtomicFileWriterFaultTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FailAt(AtomicWriteStage stage, Action<string>? observeTemp = null) : IAtomicWriteFaults
    {
        public void At(AtomicWriteStage current, string tempPath)
        {
            if (current == stage)
            {
                observeTemp?.Invoke(tempPath);
                throw new IOException($"Injected failure at {stage}.");
            }
        }
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("BeforeTempCreated")]
    [InlineData("DuringWrite")]
    [InlineData("BeforeReplace")]
    public async Task FailureAtAnyStage_LeavesThePreviousCompleteVersion_AndNoTempFile(string stageName)
    {
        var stage = Enum.Parse<AtomicWriteStage>(stageName);
        var path = Path.Combine(_directory, "manifest.json");
        await AtomicFileWriter.WriteTextAsync(path, "{\"v\":1}");

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8("{\"v\":2,\"padding\":\"xxxxxxxx\"}"), new FailAt(stage), CancellationToken.None));

        Assert.Equal("{\"v\":1}", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("BeforeTempCreated")]
    [InlineData("DuringWrite")]
    [InlineData("BeforeReplace")]
    public async Task FailureOnFirstEverWrite_NeverCreatesTheDestination(string stageName)
    {
        var stage = Enum.Parse<AtomicWriteStage>(stageName);
        var path = Path.Combine(_directory, "device-id.txt");

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8(Guid.NewGuid().ToString("D")), new FailAt(stage), CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task DuringWrite_TempHoldsOnlyAPartialWrite_WhileTheDestinationIsStillTheOldVersion()
    {
        var path = Path.Combine(_directory, "file.json");
        await AtomicFileWriter.WriteTextAsync(path, "old");
        string? observedTemp = null;
        long observedTempLength = -1;
        string? destinationDuringWrite = null;

        var faults = new FailAt(AtomicWriteStage.DuringWrite, temp =>
        {
            observedTemp = temp;
            observedTempLength = new FileInfo(temp).Length;
            destinationDuringWrite = File.ReadAllText(path);
        });

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8("0123456789"), faults, CancellationToken.None));

        Assert.Equal(5, observedTempLength);                 // genuinely partial
        Assert.Equal("old", destinationDuringWrite);         // never authoritative before the rename
        Assert.NotNull(observedTemp);
        Assert.Equal(_directory, Path.GetDirectoryName(observedTemp)); // same directory → same-volume rename
        Assert.True(AtomicFileWriter.TryParseTempFileName(Path.GetFileName(observedTemp)!, out var finalName));
        Assert.Equal("file.json", finalName);
        Assert.False(File.Exists(observedTemp));             // cleaned up after the failure
    }

    [Fact]
    public async Task BeforeReplace_TempIsCompleteAndFlushed_ButDestinationIsUnchangedUntilTheRename()
    {
        var path = Path.Combine(_directory, "file.json");
        await AtomicFileWriter.WriteTextAsync(path, "old");
        string? tempContent = null;
        string? destinationContent = null;

        var faults = new FailAt(AtomicWriteStage.BeforeReplace, temp =>
        {
            tempContent = File.ReadAllText(temp);
            destinationContent = File.ReadAllText(path);
        });

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8("new complete content"), faults, CancellationToken.None));

        Assert.Equal("new complete content", tempContent);
        Assert.Equal("old", destinationContent);
        Assert.Equal("old", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ReplacementItselfFails_DestinationSurvives_AndTheTempIsRemoved()
    {
        // A directory at the destination path makes the final rename fail on every OS.
        var path = Path.Combine(_directory, "blocked.json");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "inside.txt"), "keep");

        await Assert.ThrowsAnyAsync<Exception>(() => AtomicFileWriter.WriteTextAsync(path, "new"));

        Assert.True(Directory.Exists(path));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "inside.txt")));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SuccessfulWrite_ReplacesTheDestination_WithExactBytes()
    {
        var path = Path.Combine(_directory, "file.pdf");
        await AtomicFileWriter.WriteBytesAsync(path, [1, 2, 3]);
        byte[] replacement = [.. Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251))];

        await AtomicFileWriter.WriteBytesAsync(path, replacement);

        Assert.Equal(replacement, await File.ReadAllBytesAsync(path));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Retry_AfterALeftoverTempFromAnEarlierCrash_SucceedsAndLeavesTheLeftoverUntouched()
    {
        var path = Path.Combine(_directory, "file.json");
        await AtomicFileWriter.WriteTextAsync(path, "old");
        var leftover = Path.Combine(_directory, $"file.json{AtomicFileWriter.TempMarker}{Guid.NewGuid():N}");
        File.WriteAllText(leftover, "partial garbage from a crash");

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8("attempt 1"), new FailAt(AtomicWriteStage.BeforeReplace), CancellationToken.None));
        await AtomicFileWriter.WriteTextAsync(path, "attempt 2");

        Assert.Equal("attempt 2", await File.ReadAllTextAsync(path));
        Assert.Equal("partial garbage from a crash", File.ReadAllText(leftover)); // never read, renamed, or adopted
    }

    [Fact]
    public async Task TextWrites_AreUtf8WithoutBom_SameBytesAsBefore18B()
    {
        var path = Path.Combine(_directory, "file.json");

        await AtomicFileWriter.WriteTextAsync(path, "café");

        Assert.Equal(Encoding.UTF8.GetBytes("café"), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void SynchronousWriteText_IsAtomicAndDurableToo()
    {
        var path = Path.Combine(_directory, "device-id.txt");

        AtomicFileWriter.WriteText(path, "a");
        AtomicFileWriter.WriteText(path, "b");

        Assert.Equal("b", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("2026-09-19.json.tmp-0123456789abcdef0123456789abcdef", true, "2026-09-19.json")]
    [InlineData("manifest.json.tmp-0123456789abcdef0123456789abcdef", true, "manifest.json")]
    [InlineData("2026-09-19.json", false, "")]
    [InlineData("2026-09-19.json.tmp-0123", false, "")]                                       // too short
    [InlineData("2026-09-19.json.tmp-0123456789ABCDEF0123456789ABCDEF", false, "")]           // never uppercase
    [InlineData("2026-09-19.json.tmp-0123456789abcdef0123456789abcdef.pdf", false, "")]       // suffix after
    [InlineData(".tmp-0123456789abcdef0123456789abcdef", false, "")]                          // no final name
    [InlineData("../x.json.tmp-0123456789abcdef0123456789abcdef", false, "")]                 // separator
    [InlineData("..\\x.json.tmp-0123456789abcdef0123456789abcdef", false, "")]
    public void TryParseTempFileName_RecognizesOnlyTheExactShape(string fileName, bool expected, string expectedFinal)
    {
        Assert.Equal(expected, AtomicFileWriter.TryParseTempFileName(fileName, out var finalName));
        Assert.Equal(expectedFinal, finalName);
    }

    [Theory]
    [InlineData("DuringWrite")]
    [InlineData("BeforeReplace")]
    public async Task SyncEnvelope_LastValidVersionSurvivesAFailedReplacement_AndStillReadsAsValid(string stageName)
    {
        var stage = Enum.Parse<AtomicWriteStage>(stageName);
        var path = Path.Combine(_directory, "2026-09-19.json");
        var deviceId = Guid.NewGuid();
        SyncEnvelope Envelope(int revision) => new()
        {
            SyncSchemaVersion = SyncEnvelopeSerializer.CurrentSyncSchemaVersion,
            EntityType = SyncEntityType.AssociateDraft,
            EntityId = "2026-09-19",
            DeviceId = deviceId,
            Revision = revision,
            ModifiedUtc = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
            IsDeleted = false,
            PayloadSchemaVersion = 4,
            Payload = System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement.Clone(),
        };
        await AtomicFileWriter.WriteTextAsync(path, SyncEnvelopeSerializer.Serialize(Envelope(1)));

        await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.WriteAsync(path, Utf8(SyncEnvelopeSerializer.Serialize(Envelope(2))), new FailAt(stage), CancellationToken.None));

        var read = await SyncFileReader.ReadJsonAsync<SyncEnvelope>(path, SyncEnvelopeSerializer.Options, _ => true);
        Assert.Equal(SyncFileReadStatus.Success, read.Status);
        Assert.Equal(1, read.Value!.Revision);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SyncConfiguration_SaveIsAtomic_AndLeavesNoTempFile()
    {
        var path = Path.Combine(_directory, "sync-config.json");
        var store = new FileSyncConfigurationStore(path);
        var first = await store.LoadAsync();

        await store.SaveAsync(first with { PersonalSyncEnabled = true });
        await store.SaveAsync(first with { PersonalSyncEnabled = false });

        Assert.False((await store.LoadAsync()).PersonalSyncEnabled);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }
}
