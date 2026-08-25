using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Carowinds.Passdown.Desktop.Views;

public partial class AssociateView : UserControl
{
    public AssociateView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Keeps the section rail's three <see cref="ToggleButton"/>s (spec Parts 2/3 — Overview /
    /// Products / Notes) mutually exclusive. Plain view-layer navigation state, not business
    /// logic (CLAUDE.md's code-behind restriction is about persistence/retrieval/business
    /// rules/PDF generation, not which section is currently shown). Deliberately not
    /// <see cref="RadioButton"/>'s built-in <c>GroupName</c> grouping: that registers each button
    /// into Avalonia's process-global <c>RadioButtonGroupManager</c>, which collides across the
    /// separate <see cref="AssociateView"/> instances different headless tests construct.
    /// </summary>
    private void OnSectionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked)
        {
            return;
        }

        OverviewSectionButton.IsChecked = ReferenceEquals(clicked, OverviewSectionButton);
        ProductsSectionButton.IsChecked = ReferenceEquals(clicked, ProductsSectionButton);
        NotesSectionButton.IsChecked = ReferenceEquals(clicked, NotesSectionButton);
    }
}
