using System.Reflection.PortableExecutable;
using Carowinds.Passdown.Infrastructure.Storage;

namespace Carowinds.Passdown.Infrastructure.Tests.Packaging;

/// <summary>
/// Phase 18A PKG-001: a Release build of this repository's own assemblies must not embed the build
/// machine's absolute path in the PE debug directory (Directory.Build.props PathMap). Pre-18A every
/// shipped Carowinds.Passdown.*.dll recorded e.g. "/Users/&lt;developer&gt;/Documents/Passdown/...pdb".
/// Only meaningful for a Release build (the documented deterministic partition runs -c Release); a
/// Debug build deliberately keeps real paths for local debugging.
/// </summary>
public class ReleaseBinaryPathPrivacyTests
{
#if !DEBUG
    [Fact]
    public void ReleaseAssembly_CodeViewPdbPath_IsRepositoryRelative_NeverTheBuildMachinePath()
    {
        var assemblyPath = typeof(SqliteConnectionFactory).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);

        var codeView = reader.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        var pdbPath = reader.ReadCodeViewDebugDirectoryData(codeView).Path;

        Assert.StartsWith("/_/", pdbPath, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, pdbPath, StringComparison.OrdinalIgnoreCase);
    }
#endif
}
