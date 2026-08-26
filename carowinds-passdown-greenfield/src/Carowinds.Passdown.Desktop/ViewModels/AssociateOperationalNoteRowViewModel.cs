using Carowinds.Passdown.Desktop.Presentation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One "available to select" persistent-note suggestion row in Associate → Notes (Phase 11D Part
/// U/N). Wraps an immutable <see cref="AssociateOperationalNoteSuggestionPresentation"/> with an
/// <see cref="AddCommand"/> that calls back into <see cref="AssociateViewModel"/> — the same
/// callback-delegate shape <see cref="CustomProductRowViewModel"/>/<see cref="ProductRowViewModel"/>
/// already use, except the callback here is async (selection requires an
/// <see cref="Application.Associate.IAssociatePassdownService.SelectOperationalNoteAsync"/> round
/// trip, not an in-memory field edit).
/// </summary>
public partial class AssociateOperationalNoteSuggestionRowViewModel : ObservableObject
{
    private readonly Func<Guid, Task> _onAdd;

    public Guid Id { get; }

    public string Title { get; }

    public string CategoryLabel { get; }

    public string PriorityLabel { get; }

    public bool IsImportant { get; }

    public string DateRangeText { get; }

    /// <summary>Short relative "date relevance" hint (Phase 11E Part T), e.g. "Expires today"/"Through Aug 30" — <see langword="null"/> only in practice-unreachable states, since every suggestion here is Active by construction.</summary>
    public string? CarryForwardText { get; }

    public bool HasCarryForwardText => !string.IsNullOrEmpty(CarryForwardText);

    public string AccessibleSummary { get; }

    public AssociateOperationalNoteSuggestionRowViewModel(AssociateOperationalNoteSuggestionPresentation presentation, Func<Guid, Task> onAdd)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(onAdd);
        _onAdd = onAdd;

        Id = presentation.Id;
        Title = presentation.Title;
        CategoryLabel = presentation.CategoryLabel;
        PriorityLabel = presentation.PriorityLabel;
        IsImportant = presentation.IsImportant;
        DateRangeText = presentation.DateRangeText;
        CarryForwardText = presentation.CarryForwardText;
        AccessibleSummary = presentation.AccessibleSummary;
    }

    [RelayCommand]
    private Task Add() => _onAdd(Id);
}

/// <summary>
/// One already-selected persistent note's frozen copy, as shown in Associate → Notes (Phase 11D
/// Part V). Wraps an immutable <see cref="AssociateSelectedOperationalNotePresentation"/> — never a
/// live <see cref="Application.OperationalNotes.OperationalNote"/> — with a <see cref="RemoveCommand"/>
/// that calls back into <see cref="AssociateViewModel"/>.
/// </summary>
public partial class AssociateSelectedOperationalNoteRowViewModel : ObservableObject
{
    private readonly Func<Guid, Task> _onRemove;
    private readonly Func<Guid, Task> _onReplace;

    public Guid SourceOperationalNoteId { get; }

    public string Title { get; }

    public string? Body { get; }

    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    public string CategoryLabel { get; }

    public string PriorityLabel { get; }

    public bool IsImportant { get; }

    public string DateRangeText { get; }

    /// <summary>Informational only (Phase 11E Parts G-L) — <see langword="null"/> when the source is unchanged; never affects <see cref="Title"/>/<see cref="Body"/>/etc. above, which stay frozen regardless.</summary>
    public string? SourceStatusText { get; }

    public bool HasSourceStatus => !string.IsNullOrEmpty(SourceStatusText);

    /// <summary>Whether "Replace with Current Version" (Phase 11E Part I) should be offered — only when the source's content actually changed, never for Resolved/NoLongerApplicable/Missing (Parts J/K/L leave those to Remove-or-keep).</summary>
    public bool ShowReplaceAction { get; }

    public string AccessibleSummary { get; }

    public AssociateSelectedOperationalNoteRowViewModel(AssociateSelectedOperationalNotePresentation presentation, Func<Guid, Task> onRemove, Func<Guid, Task> onReplace)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(onRemove);
        ArgumentNullException.ThrowIfNull(onReplace);
        _onRemove = onRemove;
        _onReplace = onReplace;

        SourceOperationalNoteId = presentation.SourceOperationalNoteId;
        Title = presentation.Title;
        Body = presentation.Body;
        CategoryLabel = presentation.CategoryLabel;
        PriorityLabel = presentation.PriorityLabel;
        IsImportant = presentation.IsImportant;
        DateRangeText = presentation.DateRangeText;
        SourceStatusText = presentation.SourceStatusText;
        ShowReplaceAction = presentation.ShowReplaceAction;
        AccessibleSummary = presentation.AccessibleSummary;
    }

    [RelayCommand]
    private Task Remove() => _onRemove(SourceOperationalNoteId);

    [RelayCommand]
    private Task Replace() => _onReplace(SourceOperationalNoteId);
}
