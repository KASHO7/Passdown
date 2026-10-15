using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Runs posted UI work inline — this test assembly has no running Avalonia dispatcher loop (see <c>AssemblyInfo.cs</c>).</summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}
