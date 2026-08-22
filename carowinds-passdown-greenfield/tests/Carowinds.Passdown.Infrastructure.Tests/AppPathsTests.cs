namespace Carowinds.Passdown.Infrastructure.Tests;

/// <summary>
/// Phase 7A Part N/M: the release executable may run from a location ordinary users should not
/// write to (e.g. Program Files), so <see cref="AppPaths"/> must resolve mutable app-data storage
/// from the OS special-folder API, never from the process's current working directory or any
/// path relative to the repo/install location.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void LocalApplicationDataDirectory_IsDerivedFromSpecialFolder_NotCurrentDirectory()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Carowinds Passdown");

        Assert.Equal(expected, AppPaths.LocalApplicationDataDirectory);
    }

    [Fact]
    public void LocalApplicationDataDirectory_IsAbsolute()
    {
        Assert.True(Path.IsPathRooted(AppPaths.LocalApplicationDataDirectory));
    }

    [Fact]
    public void LocalApplicationDataDirectory_IsUnaffectedByCurrentDirectoryChanges()
    {
        var before = AppPaths.LocalApplicationDataDirectory;
        var originalDirectory = Environment.CurrentDirectory;
        var temporaryDirectory = Path.GetTempPath();

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            Assert.Equal(before, AppPaths.LocalApplicationDataDirectory);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }
}
