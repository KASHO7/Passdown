using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Records clipboard text instead of touching a real desktop clipboard; <see cref="FailWith"/> makes the next calls throw like a locked/unavailable clipboard.</summary>
public sealed class FakeClipboardService : IClipboardService
{
    public List<string> Texts { get; } = [];

    public Exception? FailWith { get; set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        Texts.Add(text);
        return Task.CompletedTask;
    }
}
