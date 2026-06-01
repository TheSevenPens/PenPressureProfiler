using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A device-inputs / status row: optional left label, a coloured state dot,
/// and a content slot for the action controls. Driven by <see cref="State"/>
/// (mapped to a brush via <see cref="StatusDotBrushConverter"/>).
/// </summary>
public sealed class StatusDotRow : ContentControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatusDotRow, string>(nameof(Label), defaultValue: string.Empty);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly StyledProperty<DotState> StateProperty =
        AvaloniaProperty.Register<StatusDotRow, DotState>(nameof(State), defaultValue: DotState.Inactive);

    public DotState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public enum DotState { Inactive, Active, Warning, Error }
}

/// <summary>
/// Maps <see cref="StatusDotRow.DotState"/> to an <see cref="IBrush"/> for
/// the dot fill. Kept colour-by-colour aligned with the brushes referenced
/// directly from MainWindow code (ribbon ellipses still set Fill manually).
/// </summary>
public sealed class StatusDotBrushConverter : IValueConverter
{
    public static readonly StatusDotBrushConverter Instance = new();

    private static readonly IBrush ActiveBrush   = new SolidColorBrush(Color.FromRgb(34,  197, 94));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));
    private static readonly IBrush WarningBrush  = new SolidColorBrush(Color.FromRgb(234, 179, 8));
    private static readonly IBrush ErrorBrush    = new SolidColorBrush(Color.FromRgb(239, 68, 68));

    public object Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusDotRow.DotState s
            ? s switch
            {
                StatusDotRow.DotState.Active   => ActiveBrush,
                StatusDotRow.DotState.Warning  => WarningBrush,
                StatusDotRow.DotState.Error    => ErrorBrush,
                _                              => InactiveBrush,
            }
            : InactiveBrush;

    public object ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture) =>
        throw new System.NotSupportedException();
}
