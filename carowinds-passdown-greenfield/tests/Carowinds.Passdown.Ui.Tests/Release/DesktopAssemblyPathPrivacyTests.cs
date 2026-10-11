using System.Reflection.PortableExecutable;
using System.Text;
using Carowinds.Passdown.Desktop;

namespace Carowinds.Passdown.Ui.Tests.Release;

/// <summary>
/// Phase 18C (PKG-001 follow-up): the shipped exe's main assembly must not carry the build machine's
/// path. Avalonia's XAML compiler rewrites Carowinds.Passdown.Desktop.dll after csc and used to
/// record the absolute obj/…/Carowinds.Passdown.Desktop.pdb path, bypassing Directory.Build.props'
/// PathMap. Infrastructure.Tests' own-assembly scan never saw this file (it is not in that test
/// project's output), so the leak survived 18A/18B until Test-ReleasePackage.ps1 ran on a real
/// release build. Release-only, like the PathMap it backs up.
/// </summary>
public class DesktopAssemblyPathPrivacyTests
{
#if !DEBUG
    [Fact]
    public void ReleaseDesktopAssembly_HasNoAbsoluteDebugPath_AndNoDeveloperOrCheckoutPath()
    {
        var assemblyPath = typeof(App).Assembly.Location;
        var bytes = File.ReadAllBytes(assemblyPath);

        using (var reader = new PEReader(new MemoryStream(bytes)))
        {
            foreach (var codeView in reader.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView))
            {
                Assert.StartsWith("/_/", reader.ReadCodeViewDebugDirectoryData(codeView).Path, StringComparison.Ordinal);
            }
        }

        var repositoryRoot = FindRepoRoot();
        string[] tokens = ["/Users/", "/home/", @"C:\Users\", repositoryRoot];
        foreach (var token in tokens)
        {
            foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.Unicode })
            {
                Assert.False(
                    bytes.AsSpan().IndexOf(encoding.GetBytes(token)) >= 0,
                    $"Carowinds.Passdown.Desktop.dll contains '{token}' ({encoding.WebName}).");
            }
        }
    }
#endif

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
