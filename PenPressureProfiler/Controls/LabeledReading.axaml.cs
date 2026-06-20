using Avalonia;
using Avalonia.Controls;

namespace PenPressureProfiler.Controls;

public partial class LabeledReading : UserControl
{
    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Caption), defaultValue: string.Empty);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Value), defaultValue: "-----");

    public static readonly StyledProperty<GridLength> CaptionWidthProperty =
        AvaloniaProperty.Register<LabeledReading, GridLength>(
            nameof(CaptionWidth), defaultValue: new GridLength(155));

    public static readonly StyledProperty<double> ValueWidthProperty =
        AvaloniaProperty.Register<LabeledReading, double>(
            nameof(ValueWidth), defaultValue: double.NaN);

    /// <summary>Width of the caption column. Defaults to 155 so stacked
    /// readings align; set smaller when placing readings side-by-side.</summary>
    public GridLength CaptionWidth
    {
        get => GetValue(CaptionWidthProperty);
        set => SetValue(CaptionWidthProperty, value);
    }

    /// <summary>Fixed width of the value (right-aligned). Default
    /// <see cref="double.NaN"/> = size to content. Set a fixed value so a
    /// changing reading never reflows neighbouring content.</summary>
    public double ValueWidth
    {
        get => GetValue(ValueWidthProperty);
        set => SetValue(ValueWidthProperty, value);
    }

    public string Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public LabeledReading() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CaptionProperty) CaptionBlock.Text = (string)change.NewValue!;
        if (change.Property == ValueProperty)   ValueBlock.Text   = (string)change.NewValue!;
    }
}
