using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Records clipboard text instead of touching a real desktop clipboard; <see cref="FailWith"/> makes the next calls throw like a locked/unavailable clipboard.</summary>
public sealed class FakeClipboardService : IClipboardService
{
    /// <summary>The plain text of every copy, formatted or not, in order.</summary>
    public List<string> Texts { get; } = [];

    /// <summary>Phase 29: the HTML fragment of each formatted copy (null for a plain-text-only copy), parallel to <see cref="Texts"/>.</summary>
    public List<string?> Htmls { get; } = [];

    public List<string> ImageFiles { get; } = [];

    public Exception? FailWith { get; set; }

    /// <summary>Phase 29: fail only formatted copies, as if the HTML format were rejected while plain text still works.</summary>
    public Exception? FailFormattedWith { get; set; }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        Texts.Add(text);
        Htmls.Add(null);
        return Task.CompletedTask;
    }

    public Task SetTextAndHtmlAsync(string text, string htmlFragment, CancellationToken cancellationToken = default)
    {
        if ((FailFormattedWith ?? FailWith) is { } failure)
        {
            throw failure;
        }

        Texts.Add(text);
        Htmls.Add(htmlFragment);
        return Task.CompletedTask;
    }

    public Task SetImageFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        ImageFiles.Add(filePath);
        return Task.CompletedTask;
    }
}
