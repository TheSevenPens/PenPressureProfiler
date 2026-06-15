using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A metadata field: a bold caption above a single-line editable text box.
/// <see cref="Text"/> is two-way bindable and is also readable/writable directly
/// (e.g. <c>field_brand.Text</c>) like a plain TextBox.
/// </summary>
public partial class LabeledField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledField, string>(nameof(Label), defaultValue: string.Empty);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LabeledField, string?>(
            nameof(Text), defaultValue: "", defaultBindingMode: BindingMode.TwoWay);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public LabeledField() => InitializeComponent();
}
