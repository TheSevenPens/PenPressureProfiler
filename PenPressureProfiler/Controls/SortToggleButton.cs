using Avalonia;
using Avalonia.Controls;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A two-state sort-direction button. Clicking flips <see cref="Ascending"/>
/// and updates its own glyph (<c>↑</c> / <c>↓</c>) + label. Consumers wire
/// the existing <c>Click</c> event and read <see cref="Ascending"/> to drive
/// their refresh — no manual content toggling required.
/// </summary>
public sealed class SortToggleButton : Button
{
    // Avalonia resolves a control's default ControlTheme by its exact type.
    // Without this, a SortToggleButton finds no theme and renders as bare
    // text (no button chrome). Point the style key at Button so it picks up
    // the standard Button theme + our `Selector="Button"` styles.
    protected override System.Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<bool> AscendingProperty =
        AvaloniaProperty.Register<SortToggleButton, bool>(nameof(Ascending), defaultValue: true);

    public bool Ascending
    {
        get => GetValue(AscendingProperty);
        set => SetValue(AscendingProperty, value);
    }

    public static readonly StyledProperty<string> AxisLabelProperty =
        AvaloniaProperty.Register<SortToggleButton, string>(nameof(AxisLabel), defaultValue: "Force");

    /// <summary>The trailing label (e.g. "Force"). The glyph is generated from <see cref="Ascending"/>.</summary>
    public string AxisLabel
    {
        get => GetValue(AxisLabelProperty);
        set => SetValue(AxisLabelProperty, value);
    }

    public SortToggleButton() => UpdateGlyph();

    protected override void OnClick()
    {
        // Flip first so any Click subscribers see the new value.
        Ascending = !Ascending;
        base.OnClick();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AscendingProperty || change.Property == AxisLabelProperty)
            UpdateGlyph();
    }

    private void UpdateGlyph() => Content = (Ascending ? "↑ " : "↓ ") + AxisLabel;
}
