using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Application.MorningMessage.Sync;
using Carowinds.Passdown.Documents.Teams;

namespace Carowinds.Passdown.Desktop.Presentation;

/// <summary>Phase 30: the words Settings → Personal Sync uses for Sync Morning Messages.</summary>
public static class MorningMessageSyncPresentation
{
    public const string Description =
        "Synchronize morning-message drafts, reminders, break plans, OD Notes, and layout preferences across your computers.";

    public const string DisableExplanation =
        "Turning this off stops future synchronization. Messages already saved on this computer will remain available.";

    public const string NeedsFolderText =
        "Turn on Personal Sync and choose a sync folder first. Morning messages then use the same folder.";

    public const string ScopeNote =
        "Each computer decides for itself. Hours, attendance, pre-solds, prices and the Daily Feature are never copied — each computer builds the message from its own current data. The DAP image stays on the computer that chose it. The default copy mode (formatted or plain text) also follows you.";

    public static (string Text, bool IsProblem) Status(MorningMessageSyncStatus status) => status switch
    {
        MorningMessageSyncStatus.NotSyncing => ("Morning Messages: Not syncing", false),
        MorningMessageSyncStatus.NeedsSyncFolder => ("Morning Messages: Not syncing — no sync folder", true),
        MorningMessageSyncStatus.FolderUnavailable => ("Morning Messages: Shared folder unavailable", true),
        MorningMessageSyncStatus.ConflictNeedsReview => ("Morning Messages: Conflict needs review", true),
        MorningMessageSyncStatus.ChangesWaiting => ("Morning Messages: Changes waiting to sync", false),
        MorningMessageSyncStatus.Synced => ("Morning Messages: Synced", false),
        _ => ("Morning Messages: Not syncing", false),
    };

    /// <summary>One field's synchronized text made readable: blank is "(empty)", flags are Yes/No, section lists are spelled out.</summary>
    public static string Display(string field, string value)
    {
        if (value.Length == 0)
        {
            return "(empty)";
        }

        return field switch
        {
            MorningMessageSyncFields.DapImageReminder or MorningMessageSyncFields.RemindersAsBullets => value == "true" ? "Yes" : "No",
            MorningMessageSyncFields.DefaultCopyMode => value == nameof(TeamsCopyMode.Formatted) ? "Formatted" : "Plain text",
            MorningMessageSyncFields.Sections => string.Join(", ", value.Split(',').Select(SectionText)),
            MorningMessageSyncFields.PriceSections => string.Join(", ", value.Split(',').Select(token =>
                Enum.TryParse<ProductDisplaySection>(token, out var section) ? MorningMessageComposer.PriceSectionName(section) : token)),
            _ => value,
        };
    }

    private static string SectionText(string token)
    {
        var excluded = token.StartsWith('-');
        var name = excluded ? token[1..] : token;
        var label = Enum.TryParse<MorningMessageSectionId>(name, out var id) ? MorningMessageComposer.SectionName(id) : name;
        return excluded ? label + " (not included)" : label;
    }
}

/// <summary>One row of a Morning Message conflict comparison.</summary>
public sealed record MorningMessageConflictFieldPresentation(string Label, string ThisDevice, string OtherDevice);

/// <summary>Phase 30: a conflicted date (or the preferences) as the review panel shows it.</summary>
public sealed class MorningMessageConflictPresentation(MorningMessageConflict conflict)
{
    public MorningMessageConflict Conflict { get; } = conflict;

    public DateOnly? OperatingDate => Conflict.OperatingDate;

    public string Title => Conflict.Title;

    public string Summary => Conflict.Fields.Count == 1
        ? Conflict.Fields[0].Label + " was changed differently on two computers."
        : $"{Conflict.Fields.Count} parts were changed differently on two computers.";

    public IReadOnlyList<MorningMessageConflictFieldPresentation> Fields { get; } = conflict.Fields
        .Select(field => new MorningMessageConflictFieldPresentation(
            field.Label,
            MorningMessageSyncPresentation.Display(field.Field, field.ThisDevice),
            MorningMessageSyncPresentation.Display(field.Field, field.OtherDevice)))
        .ToList();

    public string Explanation =>
        "Both computers changed this without seeing the other's change. Nothing has been overwritten: this computer still shows its own text, and the other computer's text is kept here until you choose. Parts changed on only one computer were already combined.";
}
