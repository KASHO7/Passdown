using Avalonia.Controls;
using Avalonia.Input;

namespace Carowinds.Passdown.Desktop.Controls;

/// <summary>
/// Phase 21D: inserts text into a <see cref="TextBox"/> exactly as if it had been typed there — by
/// raising the TextBox's own text-input event at the given selection — so the edit goes through the
/// control's normal path: a selection is replaced, the caret moves past the insertion, the change is
/// one undoable step, <c>MaxLength</c>/read-only are honored, and the two-way <c>Text</c> binding
/// (dirty state, autosave) sees an ordinary edit. Surrounding text is never touched.
/// </summary>
public static class TextBoxInsertion
{
    /// <summary>
    /// Replaces [<paramref name="selectionStart"/>, <paramref name="selectionEnd"/>) of
    /// <paramref name="target"/> with <paramref name="text"/> and returns focus to it. When
    /// <paramref name="selectOffset"/> is given, the inserted text's placeholder at that offset (of
    /// <paramref name="selectLength"/> characters) is selected so typing replaces it; otherwise the caret
    /// sits right after the insertion. Returns false (and changes nothing) for a read-only or disabled editor.
    /// </summary>
    public static bool Insert(TextBox target, string text, int selectionStart, int selectionEnd, int? selectOffset = null, int selectLength = 0)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);

        if (target.IsReadOnly || !target.IsEffectivelyEnabled || text.Length == 0)
        {
            return false;
        }

        var length = target.Text?.Length ?? 0;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, length);

        target.Focus();
        target.SelectionStart = start;
        target.SelectionEnd = end;
        target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target });

        var inserted = (target.Text?.Length ?? 0) - (length - (end - start));
        if (inserted <= 0)
        {
            return false; // e.g. MaxLength refused it; nothing changed
        }

        if (selectOffset is { } offset && offset >= 0 && offset + selectLength <= inserted)
        {
            target.SelectionStart = start + offset;
            target.SelectionEnd = start + offset + selectLength;
        }
        else
        {
            target.SelectionStart = target.SelectionEnd = start + inserted;
        }

        return true;
    }
}
