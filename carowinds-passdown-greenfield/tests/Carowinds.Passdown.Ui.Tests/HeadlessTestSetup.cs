using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Carowinds.Passdown.Desktop;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// One-time Avalonia headless platform setup for this test assembly (Phase 4B Part I). Uses the
/// plain <c>Avalonia.Headless</c> package rather than <c>Avalonia.Headless.XUnit</c> — that package
/// pulls in xunit v3, which collides with this project's existing xunit v2 packages (used by
/// <see cref="CompositionRootTests"/>). A <see cref="ModuleInitializerAttribute"/> method gives the
/// same "set the headless platform up once before any test runs" effect without a second test
/// framework's runner.
/// </summary>
internal static class HeadlessTestSetup
{
    [ModuleInitializer]
    public static void Initialize()
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
    }
}
