using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Infrastructure.PersonalSync;

namespace Carowinds.Passdown.Infrastructure.Tests.PersonalSync;

/// <summary>Phase 16B §6: folder validation before Personal Sync is allowed to report Ready.</summary>
public class SyncFolderValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"carowinds-passdown-folder-validator-tests-{Guid.NewGuid():N}");
    private readonly SyncFolderValidator _validator = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory) && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_EmptyPath_ReturnsInvalid()
    {
        var result = await _validator.ValidateAsync("");

        Assert.Equal(SyncFolderValidationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_NewFolder_CreatesOwnedSubfolder_AndReturnsValid()
    {
        var result = await _validator.ValidateAsync(_directory);

        Assert.Equal(SyncFolderValidationOutcome.Valid, result.Outcome);
        Assert.True(Directory.Exists(SyncPathService.GetSyncFolderPath(_directory)));
    }

    [Fact]
    public async Task ValidateAsync_ExistingWritableFolder_ReturnsValid_AndLeavesNoProbeFileBehind()
    {
        Directory.CreateDirectory(_directory);

        var result = await _validator.ValidateAsync(_directory);

        Assert.Equal(SyncFolderValidationOutcome.Valid, result.Outcome);
        Assert.Empty(Directory.GetFiles(SyncPathService.GetSyncFolderPath(_directory)));
    }

    [Fact]
    public async Task ValidateAsync_ExactLocalAppDataDirectory_ReturnsInvalid()
    {
        var result = await _validator.ValidateAsync(AppPaths.LocalApplicationDataDirectory);

        Assert.Equal(SyncFolderValidationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_SubfolderOfLocalAppData_ReturnsInvalid()
    {
        var candidate = Path.Combine(AppPaths.LocalApplicationDataDirectory, "some-subfolder");

        var result = await _validator.ValidateAsync(candidate);

        Assert.Equal(SyncFolderValidationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_AncestorOfLocalAppData_ReturnsInvalid()
    {
        var ancestor = Directory.GetParent(AppPaths.LocalApplicationDataDirectory)!.FullName;

        var result = await _validator.ValidateAsync(ancestor);

        Assert.Equal(SyncFolderValidationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_UnwritableFolder_ReturnsUnavailable_NotInvalid()
    {
        // Unix permission bits don't apply on Windows; this app targets Windows release but
        // tests/develops on Unix hosts (mirrors ScheduledTaskStatusProviderTests' own platform guard).
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var result = await _validator.ValidateAsync(_directory);

        Assert.Equal(SyncFolderValidationOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_UnrelatedFolder_DoesNotReportInvalidJustBecauseItExists()
    {
        Directory.CreateDirectory(_directory);

        var result = await _validator.ValidateAsync(_directory);

        Assert.NotEqual(SyncFolderValidationOutcome.Invalid, result.Outcome);
    }
}
