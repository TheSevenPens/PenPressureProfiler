using Avalonia;
using Avalonia.Controls;

namespace PenPressureProfiler.Controls;

public partial class LabeledReading : UserControl
{
    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Caption), defaultValue: string.Empty);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<LabeledReading, string>(nameof(Value), defaultValue: "-----");

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

    public LabeledReading()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CaptionProperty) CaptionBlock.Text = (string)change.NewValue!;
        if (change.Property == ValueProperty)   ValueBlock.Text   = (string)change.NewValue!;
    }
}
