using Avalonia;
using Avalonia.Controls.Primitives;

namespace PenPressureProfiler.Controls;

/// <summary>
/// The recurring "captures/estimates card" wrapper used by Manual, Auto, and
/// Threshold panels. Provides a standard card frame with:
/// <list type="bullet">
///   <item>a bold title row (<see cref="Header"/>) + optional right-aligned
///         action buttons (<see cref="HeaderActions"/>)</item>
///   <item>a body slot (<see cref="Body"/>) for the count display + list</item>
///   <item>a footer slot (<see cref="Footer"/>) for the Clear/Save/Load row</item>
///   <item>an optional status line (<see cref="Status"/>) that auto-hides
///         when empty</item>
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

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(HeaderActions));

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(Body));

    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<CaptureListSection, object?>(nameof(Footer));

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<CaptureListSection, string>(nameof(Status), defaultValue: string.Empty);

    public string Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
}
