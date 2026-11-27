using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Carowinds.Passdown.Application.Ert;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace Carowinds.Passdown.Ui.Tests;

/// <summary>
/// Phase 36C real-window checks for the extracted Early Ride Time editor (docs/phase36c-ert-editor-maintainability.md):
/// the retargeted bindings work both ways in the realized Associate view, Save and Close behave as before, the panel's
/// keyboard order is unchanged, and it renders in Light and Dark. A partial of the RUN-ALONE
/// <see cref="Phase33PreviewWindowTests"/> class (RealWindowPartitionGuardTests), so the deterministic partition is unchanged.
/// </summary>
public partial class Phase33PreviewWindowTests
{
    private static FakeErtConfigurationService SeededErt()
    {
        var ert = new FakeErtConfigurationService();
        ert.SeedMonthly(new MonthlyErtConfiguration(FixedDate.Year, FixedDate.Month, ErtConfigurationState.Configured, null, ["Fury 325"], "Monthly note", DateTimeOffset.UnixEpoch));
        return ert;
    }

    private static Border ErtPanel(Window window) => ByName<Border>(window, "Early Ride Time editor");

    [Fact]
    public async Task Phase36C_ErtEditor_BindingsRoundTrip_SaveKeepsItOpen_PreviewUpdates_CloseHidesIt()
    {
        var ert = SeededErt();
        var w = await RealizeWorkspaceAsync(1600, 900, ert: ert);
        var editor = w.ViewModel.EarlyRideTimeEditor;
        Assert.False(ErtPanel(w.Window).IsEffectivelyVisible);

        Execute(ByName<Button>(w.Window, "Edit Attractions for Early Ride Time").Command, () => editor.IsErtEditorOpen);
        Pump();

        Assert.True(ErtPanel(w.Window).IsEffectivelyVisible);
        var rides = ByName<TextBox>(w.Window, "Monthly Early Ride Time rides");
        Assert.Equal("Fury 325", rides.Text);                                                  // child → view
        Assert.Equal("Monthly note", ByName<TextBox>(w.Window, "Monthly Early Ride Time note").Text);

        rides.Text = "Intimidator";
        Pump();
        Assert.Equal("Intimidator", editor.MonthlyErtRidesText);                               // view → child

        var exportsBefore = w.Export.Calls.Count;
        Execute(ByName<Button>(w.Window, "Save Monthly Early Ride Time").Command, () => ert.SaveMonthlyCalls.Count == 1 && editor.IsErtEditorOpen);
        w.PreviewDebounce.ElapseLatest();
        Pump();

        Assert.Equal(["Intimidator"], Assert.Single(ert.SaveMonthlyCalls).Rides);
        Assert.Empty(w.Service.SaveCalls);                                                      // never a draft save
        Assert.True(ErtPanel(w.Window).IsEffectivelyVisible);                                   // stays open after Save, as before
        Assert.Contains(w.Window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Intimidator" && t.IsEffectivelyVisible);
        Assert.True(w.Export.Calls.Count > exportsBefore);                                      // the preview regenerated…
        Assert.Equal(["Intimidator"], w.Export.Calls[^1].Workspace.EarlyRideTime.Rides);         // …from the reloaded workspace

        Execute(ByName<Button>(w.Window, "Close Early Ride Time editor").Command, () => !editor.IsErtEditorOpen);
        Pump();
        Assert.False(ErtPanel(w.Window).IsEffectivelyVisible);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase36C_ErtEditor_OverrideSection_ShowsAndSavesThroughTheChild()
    {
        var ert = SeededErt();
        var w = await RealizeWorkspaceAsync(1600, 900, ert: ert);
        var editor = w.ViewModel.EarlyRideTimeEditor;
        Execute(ByName<Button>(w.Window, "Edit Attractions for Early Ride Time").Command, () => editor.IsErtEditorOpen);
        Pump();
        var overrideRides = ByName<TextBox>(w.Window, "Early Ride Time override rides");
        Assert.False(overrideRides.IsEffectivelyVisible);

        ByName<CheckBox>(w.Window, "Use an Early Ride Time override for this date").IsChecked = true;
        Pump();
        Assert.True(editor.ErtUseOverrideForDate);
        Assert.True(overrideRides.IsEffectivelyVisible);
        overrideRides.Text = "Copperhead Strike";
        Pump();

        Execute(ByName<Button>(w.Window, "Save Early Ride Time override").Command, () => ert.SaveOverrideCalls.Count == 1 && editor.IsErtEditorOpen);
        Pump();

        Assert.Equal(FixedDate, Assert.Single(ert.SaveOverrideCalls).OperatingDate);
        Assert.True(ByName<Button>(w.Window, "Remove Early Ride Time override for this date").IsEffectivelyVisible); // parent's EarlyRideTimeIsOverride
        Assert.Empty(w.Service.SaveCalls);
        w.Window.Close();
    }

    [Fact]
    public async Task Phase36C_ErtEditor_TabOrderInsideThePanel_IsUnchanged()
    {
        var w = await RealizeWorkspaceAsync(1600, 900, ert: SeededErt());
        Execute(ByName<Button>(w.Window, "Edit Attractions for Early Ride Time").Command, () => w.ViewModel.EarlyRideTimeEditor.IsErtEditorOpen);
        Pump();

        var first = ByName<CheckBox>(w.Window, "No Early Ride Time this month");
        string[] expected =
        [
            "No Early Ride Time this month",
            "Monthly Early Ride Time rides",
            "Monthly Early Ride Time note",
            "Save Monthly Early Ride Time",
            "Use an Early Ride Time override for this date",
            "Close Early Ride Time editor",
        ];

        // Real Tab key presses, as Phase 35D's tab-order check does.
        first.Focus(NavigationMethod.Tab);
        Pump();
        var order = new List<string?>();
        for (var i = 0; i < expected.Length; i++)
        {
            order.Add(w.Window.FocusManager?.GetFocusedElement() is Control focused ? AutomationProperties.GetName(focused) : null);
            w.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            w.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Pump();
        }

        Assert.Equal(expected, order);
        w.Window.Close();
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Phase36C_ErtEditor_RendersInBothThemes_WithoutTransitions(string variant)
    {
        var theme = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var w = await RealizeWorkspaceAsync(1600, 900, theme, ert: SeededErt());
        Execute(ByName<Button>(w.Window, "Edit Attractions for Early Ride Time").Command, () => w.ViewModel.EarlyRideTimeEditor.IsErtEditorOpen);
        Pump();

        var panel = ErtPanel(w.Window);
        Assert.Equal(theme, w.Window.ActualThemeVariant);
        Assert.True(panel.IsEffectivelyVisible);
        Assert.NotNull(panel.Background);
        Assert.True(panel.Bounds.Width > 0 && panel.Bounds.Height > 0);
        Assert.True(panel.Transitions is null || panel.Transitions.Count == 0); // opening is instant, with or without reduced motion
        w.Window.Close();
    }
}
