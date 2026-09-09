using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Deterministic <see cref="ISyncFolderPicker"/> — never opens a real native dialog.</summary>
internal sealed class FakeSyncFolderPicker : ISyncFolderPicker
{
    public string? NextResult { get; set; }

    public int CallCount { get; private set; }

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}
