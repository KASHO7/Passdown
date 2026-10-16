using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class PriceHistoryView : UserControl
{
    private PriceHistoryViewModel? _viewModel;

    public PriceHistoryView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Phase 20A keyboard support: when the screen opens, focus moves into the product list so arrow
    /// keys work immediately and Escape (this view's key binding) closes it. Focus plumbing only —
    /// no data access here.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as PriceHistoryViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PriceHistoryViewModel.IsOpen) && _viewModel?.IsOpen == true)
        {
            Dispatcher.UIThread.Post(() => ProductList.Focus(), DispatcherPriority.Input);
        }
    }
}
