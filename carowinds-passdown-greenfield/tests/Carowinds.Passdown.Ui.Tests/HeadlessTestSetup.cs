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
///
/// Phase 15E note: a dedicated-background-thread variant of this setup was tried (to fully eliminate
/// the intermittent real-<c>Window</c> "different thread owns it" race described on
/// <c>OperationsCalendarViewTests.RealizeCalendarLayoutAsync</c>) and reverted — blocking this
/// <see cref="ModuleInitializerAttribute"/> method on that thread's setup deadlocked outright (the CLR
/// holds a loader lock across module initialization; the new thread's attempt to JIT/load Avalonia's
/// types requires that same lock). A hang is strictly worse than the occasional fast test failure it
/// was meant to fix, so this file is back to the original, direct, synchronous call. See the hardening
/// doc's Phase 15E section for the full investigation and why the underlying flake is left as a
/// documented, pre-existing, known limitation rather than "fixed" by a riskier mechanism.
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
