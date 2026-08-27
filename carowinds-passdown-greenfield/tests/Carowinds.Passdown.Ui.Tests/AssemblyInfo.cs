using Xunit;

/// <summary>
/// Phase 15B: this assembly has no <c>Avalonia.Headless.XUnit</c> package (xunit-v2/v3 version
/// conflict — see <see cref="Carowinds.Passdown.Ui.Tests.HeadlessTestSetup"/>'s own doc comment), so
/// there is no running Avalonia dispatcher loop for a real <c>Window</c>-based test to marshal onto
/// from a different thread — confirmed empirically to deadlock. Disabling xUnit's default
/// cross-collection parallelization is what keeps every test in this assembly on the one thread
/// <see cref="Carowinds.Passdown.Ui.Tests.HeadlessTestSetup"/>'s module initializer ran on, which the
/// real-window Operations Calendar layout tests (<c>OperationsCalendarViewTests</c>) require.
/// </summary>
[assembly: CollectionBehavior(DisableTestParallelization = true)]
