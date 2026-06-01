using Avalonia;
using Avalonia.Controls;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A labeled group inside the top ribbon: a small bold header above its
/// child content, followed by a thin vertical separator. Templated via
/// the style block in <c>App.axaml</c>.
/// </summary>
public sealed class RibbonGroup : ContentControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<RibbonGroup, string>(nameof(Header), defaultValue: string.Empty);

    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<bool> ShowSeparatorProperty =
        AvaloniaProperty.Register<RibbonGroup, bool>(nameof(ShowSeparator), defaultValue: true);

    /// <summary>Whether to render the trailing vertical separator. Default true; set to false on the last group.</summary>
    public bool ShowSeparator
    {
        get => GetValue(ShowSeparatorProperty);
        set => SetValue(ShowSeparatorProperty, value);
    }
}
