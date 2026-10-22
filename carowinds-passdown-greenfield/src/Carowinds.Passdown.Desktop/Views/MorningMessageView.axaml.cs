using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class MorningMessageView : UserControl
{
    private MorningMessageViewModel? _viewModel;

    public MorningMessageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 29 keyboard support: when the composer opens, focus moves into the greeting so typing and Tab
    /// work immediately and Escape (this view's key binding) closes it. Focus plumbing only — no data access.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MorningMessageViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MorningMessageViewModel.IsOpen) && _viewModel?.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() => GreetingBox.Focus(), DispatcherPriority.Input);
        }
    }
}
