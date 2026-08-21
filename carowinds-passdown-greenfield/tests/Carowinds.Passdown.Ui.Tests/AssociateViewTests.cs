using Carowinds.Passdown.Desktop.ViewModels;
using Carowinds.Passdown.Desktop.Views;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 4B Part I #1: proves the Associate screen actually resolves/constructs through Avalonia's
/// real XAML loader (compiled-binding checks at build time only prove the binding paths exist —
/// this proves <c>InitializeComponent</c> itself, including every <c>StaticResource</c>/converter
/// lookup, succeeds at runtime). Uses the headless platform set up once in
/// <see cref="HeadlessTestSetup"/>. Deliberately stays on plain <c>UserControl</c>
/// construction (not a <c>Window</c>): a <c>Window</c> attaches to Avalonia's compositor
/// immediately on construction, which requires running on whichever thread the headless platform's
/// dispatcher is bound to — a real requirement the <c>Avalonia.Headless.XUnit</c> package's test
/// attribute satisfies, but that package pulls in xunit v3, which collides with this project's
/// existing xunit v2 packages. A <c>UserControl</c> has no such requirement, so it stays a safe,
/// hermetic construction check without needing that second test framework.
/// </summary>
public class AssociateViewTests
{
    [Fact]
    public void AssociateView_Constructs_AndAcceptsAssociateViewModelAsDataContext()
    {
        var viewModel = new AssociateViewModel(new FakeAssociatePassdownService(), new FakeClock(DateTimeOffset.UtcNow));

        var view = new AssociateView { DataContext = viewModel };

        Assert.Same(viewModel, view.DataContext);
    }
}
