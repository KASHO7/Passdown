using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Domain;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>"Saved on this computer at 2:14 PM" (spec §10.3) — a home for Phase 4A's <see cref="AssociateSaveStatus"/> that later editable-field autosave work can reuse without changing this screen's layout.</summary>
public sealed record SaveStatusPresentation(string Text)
{
    public static SaveStatusPresentation Build(AssociateSaveStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status.State switch
        {
            AssociateSaveState.NeverSaved => new SaveStatusPresentation("Not saved yet"),
            _ => new SaveStatusPresentation($"Saved on this computer at {FormatTimestamp(status.LastSavedAt!.Value)}"),
        };
    }

    private static string FormatTimestamp(DateTimeOffset value) => EasternTimestampFormat.Time(value);
}
