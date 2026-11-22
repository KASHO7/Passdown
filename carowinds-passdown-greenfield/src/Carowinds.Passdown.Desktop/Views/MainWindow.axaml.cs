using Avalonia;
using Avalonia.Controls;
using Carowinds.Passdown.Desktop.ViewModels;

namespace Carowinds.Passdown.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Phase 35A: presentation-only forwarding. Activation re-reads Windows reduced motion (changing that setting
        // means leaving the app); the client width lets the sidebar show icons only in narrow windows.
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty || change.Property == DataContextProperty)
        {
            (DataContext as MainViewModel)?.UpdateShellWidth(ClientSize.Width);
        }
    }
}
