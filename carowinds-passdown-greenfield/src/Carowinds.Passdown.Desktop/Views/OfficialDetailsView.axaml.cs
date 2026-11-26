using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class OfficialDetailsView : UserControl
{
    private OfficialDetailsDialogViewModel? _viewModel;
    private IInputElement? _returnFocusTo;

    public OfficialDetailsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 31C keyboard and Narrator support: opening moves focus to the dialog's Close button (whose
    /// accessible name includes the item), and closing returns focus to the View details link that opened
    /// it. Focus plumbing only — no data access here.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as OfficialDetailsDialogViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficialDetailsDialogViewModel.IsOpen) || _viewModel is null)
        {
            return;
        }

        if (_viewModel.IsOpen)
        {
            _returnFocusTo = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() => CloseButton.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
        }
        else if (_returnFocusTo is { } target)
        {
            _returnFocusTo = null;
            Dispatcher.UIThread.Post(() => target.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
        }
    }
}
