using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class OperationalNotesView : UserControl
{
    private OperationalNotesViewModel? _viewModel;
    private IInputElement? _focusBeforeEditor;

    public OperationalNotesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 35F: presentation only. When the editor dialog opens, focus moves to its Title box; when it closes
    /// (Save, Cancel, × or Escape), focus returns to the control that opened it (New Note or Edit), or to New Note
    /// if that control is gone. Nothing here reads or writes a note.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as OperationalNotesViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationalNotesViewModel.ShowEditor) || _viewModel is null || VisualRoot is null)
        {
            return;
        }

        if (_viewModel.ShowEditor)
        {
            _focusBeforeEditor = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() => EditorTitleBox.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
        }
        else
        {
            var target = _focusBeforeEditor is Visual { IsEffectivelyVisible: true } ? _focusBeforeEditor : NewNoteButton;
            _focusBeforeEditor = null;
            Dispatcher.UIThread.Post(() => target.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
        }
    }
}
