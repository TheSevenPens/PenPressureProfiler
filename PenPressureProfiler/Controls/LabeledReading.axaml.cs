using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PenPressureProfiler.Controls;

public partial class LabeledReading : UserControl
{
    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Caption), defaultValue: string.Empty);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Value), defaultValue: "-----");

    public static readonly StyledProperty<IBrush?> ValueBackgroundProperty =
        AvaloniaProperty.Register<LabeledReading, IBrush?>(nameof(ValueBackground));

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

    /// <summary>Background brush behind the value text. <c>null</c> (default)
    /// leaves it transparent; set a brush to tint the value slot (e.g. the
    /// Accumulator under/at-or-over threshold tint).</summary>
    public IBrush? ValueBackground
    {
        get => GetValue(ValueBackgroundProperty);
        set => SetValue(ValueBackgroundProperty, value);
    }

    // Dark value text for use over a light tint (the tints are pale pastels, so a
    // theme-light foreground would be unreadable in dark mode). Matches the
    // BUCKETS table, which forces #222 text on the same backgrounds.
    private static readonly IBrush TintedForeground =
        new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));

    public LabeledReading() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CaptionProperty) CaptionBlock.Text = (string)change.NewValue!;
        if (change.Property == ValueProperty)   ValueBlock.Text   = (string)change.NewValue!;
        if (change.Property == ValueBackgroundProperty)
        {
            var bg = (IBrush?)change.NewValue;
            ValueBlock.Background = bg;
            // Force readable dark text over the tint; revert to the themed colour
            // when the tint is cleared.
            if (bg is null) ValueBlock.ClearValue(TextBlock.ForegroundProperty);
            else            ValueBlock.Foreground = TintedForeground;
        }
    }
}
