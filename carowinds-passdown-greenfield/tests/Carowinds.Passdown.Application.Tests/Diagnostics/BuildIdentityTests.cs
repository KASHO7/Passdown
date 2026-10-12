using System.Reflection;
using System.Reflection.Emit;
using Carowinds.Passdown.Application.Diagnostics;
using Carowinds.Passdown.Application.Tests.TestSupport;

namespace Carowinds.Passdown.Application.Tests.Diagnostics;

/// <summary>
/// Phase 18D: <see cref="BuildIdentity"/> — the running-build identification used by Settings'
/// "About this build", the exported diagnostics, and the startup/scheduled-refresh log lines.
/// </summary>
public class BuildIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 14, 0, 0, TimeSpan.Zero);

    private static Assembly AssemblyWith(string? product, string? informationalVersion)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BuildIdentityProbe") { Version = new Version(0, 1, 0, 0) }, AssemblyBuilderAccess.Run);
        if (product is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyProductAttribute).GetConstructor([typeof(string)])!, [product]));
        }

        if (informationalVersion is not null)
        {
            builder.SetCustomAttribute(new CustomAttributeBuilder(typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!, [informationalVersion]));
        }

        return builder;
    }

    [Fact]
    public void FromAssembly_SplitsTheReleaseVersionFromTheSourceCommit()
    {
        var identity = BuildIdentity.FromAssembly(
            AssemblyWith("Carowinds Operations", "0.1.0-dev+b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2"),
            @"C:\Users\Kim\Downloads\Carowinds-Operations-0.1.0-dev-win-x64\Carowinds.Passdown.Desktop.exe");

        Assert.Equal("Carowinds Operations", identity.ProductName);
        Assert.Equal("0.1.0-dev", identity.Version);
        Assert.Equal("b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2", identity.SourceRevision);
        Assert.Equal("0.1.0-dev (b2db3ed)", identity.DisplayVersion);
        Assert.Equal("0.1.0-dev+b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2", identity.FullVersion);
        Assert.EndsWith(@"\Carowinds.Passdown.Desktop.exe", identity.ExecutablePath);
    }

    [Fact]
    public void FromAssembly_WithoutACommitSuffix_ShowsTheVersionAlone()
    {
        var identity = BuildIdentity.FromAssembly(AssemblyWith("Carowinds Operations", "0.1.0-dev"), null);

        Assert.Null(identity.SourceRevision);
        Assert.Equal("0.1.0-dev", identity.DisplayVersion);
        Assert.Null(identity.ExecutablePath);
    }

    [Fact]
    public void FromAssembly_MissingAttributes_FallsBackWithoutFabricating()
    {
        var bare = BuildIdentity.FromAssembly(AssemblyWith(null, null), "  ");
        var none = BuildIdentity.FromAssembly(null, null);

        Assert.Equal(BuildIdentity.DefaultProductName, bare.ProductName);
        Assert.Equal("0.1.0.0", bare.Version);
        Assert.Null(bare.ExecutablePath);
        Assert.Equal("unknown", none.Version);
    }

    [Fact]
    public async Task DiagnosticReport_CarriesTheFullVersion_AndTheExportOnlyPrintsTheExecutablePathRedacted()
    {
        var identity = new BuildIdentity("Carowinds Operations", "0.1.0-dev", "b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2",
            @"C:\Users\Kim Lee\Downloads\Carowinds-Operations-0.1.0-dev-win-x64\Carowinds.Passdown.Desktop.exe");
        var builder = new DiagnosticReportBuilder(
            [],
            new InMemorySourceStateRepository(),
            new FakeDatabaseDiagnosticsProvider(new DatabaseDiagnostics(false, null, null, false, null)),
            new FakeScheduledTaskStatusProvider(new ScheduledTaskDiagnostics(ScheduledTaskDiagnosticStatus.NotFound, null)),
            new FakeScheduledRefreshLogProvider([]),
            new FakeClock(Now),
            buildIdentity: identity);

        var report = await builder.BuildAsync();
        var summary = DiagnosticReportFormatter.BuildSummaryText(report);
        var system = DiagnosticReportFormatter.BuildSystemText(report);

        Assert.Equal("0.1.0-dev+b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2", report.AppVersion);
        Assert.Contains("Application version: 0.1.0-dev+b2db3ed523fab5115dcd986c2dfbd3f5ad876eb2", summary);
        Assert.Contains(@"Executable (sanitized): %USERPROFILE%\Downloads\Carowinds-Operations-0.1.0-dev-win-x64\Carowinds.Passdown.Desktop.exe", summary);
        Assert.Contains(@"%USERPROFILE%\Downloads\Carowinds-Operations-0.1.0-dev-win-x64", system);
        Assert.DoesNotContain("Kim", summary);
        Assert.DoesNotContain("Kim", system);
    }
}
