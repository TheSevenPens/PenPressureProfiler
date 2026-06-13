using Avalonia;
using Avalonia.Controls.Primitives;

namespace PenPressureProfiler.Controls;

/// <summary>
/// The captures pane shared by the Curve and Threshold modes (a flat pane, no
/// card chrome). Lays out four stacked regions in a fixed order:
/// <list type="number">
///   <item><see cref="Header"/> — the card title.</item>
///   <item><see cref="Actions"/> — the action-button row.</item>
///   <item><see cref="Meta"/> — counts / readouts about the captures.</item>
///   <item><see cref="Body"/> — the capture list, which takes all remaining
///         vertical space in the card.</item>
/// </list>
/// </summary>
public sealed class CaptureListSection : TemplatedControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<CaptureListSection, string>(nameof(Header), defaultValue: string.Empty);

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(Actions));

    /// <summary>The action-button row (Record / sort / Clear / Save / Load …).</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public static readonly StyledProperty<object?> MetaProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(Meta));

    /// <summary>Count / readout content shown between the actions and the list.</summary>
    public object? Meta
    {
        get => GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(Body));

    /// <summary>The capture list. Fills the card's remaining vertical space.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
