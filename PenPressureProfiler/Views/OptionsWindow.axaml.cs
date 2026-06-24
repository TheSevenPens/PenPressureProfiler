using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PenPressureProfiler.Views;

/// <summary>
/// Modal <b>Tools ▸ Options</b> dialog. Edits a copy of the app options;
/// <b>Done</b> returns the edited <see cref="AppOptions"/>, <b>Cancel</b> / Esc
/// returns null.
/// </summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow(AppOptions current)
    {
        InitializeComponent();

        // Label carries the measured lag so it stays in sync with the source.
        chk_scale_lag.Content   = $"Apply scale-lag comp ({ScaleSessionManager.ResponseLagMs:0} ms)";
        chk_scale_lag.IsChecked = current.ScaleLagComp;

        chk_require_proximity.IsChecked = current.AccumulatorRequirePenProximity;

        AddHandler(KeyDownEvent, OnKeyDown);
    }

    private void Done_Click(object? sender, RoutedEventArgs e) =>
        Close(new AppOptions
        {
            ScaleLagComp                   = chk_scale_lag.IsChecked == true,
            AccumulatorRequirePenProximity = chk_require_proximity.IsChecked == true,
        });

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
