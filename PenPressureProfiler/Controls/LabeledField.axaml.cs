using Avalonia;
using Avalonia.Controls;

namespace PenPressureProfiler.Controls;

public partial class LabeledField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Label), defaultValue: string.Empty);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Text), defaultValue: string.Empty);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Raised when the inner TextBox text changes.</summary>
    public event EventHandler<Avalonia.Controls.TextChangedEventArgs>? TextChanged;

    public LabeledField()
    {
        InitializeComponent();

        // Keep the StyledProperty in sync when the user types.
        FieldBox.TextChanged += (s, e) =>
        {
            string current = FieldBox.Text ?? string.Empty;
            if (GetValue(TextProperty) != current)
                SetValue(TextProperty, current);
            TextChanged?.Invoke(this, e);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelProperty)
            LabelBlock.Text = (string)change.NewValue!;

        if (change.Property == TextProperty)
        {
            string newVal = (string)change.NewValue!;
            if (FieldBox.Text != newVal)
                FieldBox.Text = newVal;
        }
    }
}
