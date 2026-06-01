using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using System.Collections;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A single row of an estimates list: a bold <c>#N</c> number, a horizontal
/// strip of label/value <see cref="EstimateField"/>s, and a ✕ delete button.
/// Unifies the cards used by the Manual, Auto, and Threshold lists.
/// </summary>
public sealed class EstimateCard : TemplatedControl
{
    public static readonly StyledProperty<string> NumberProperty =
        AvaloniaProperty.Register<EstimateCard, string>(nameof(Number), defaultValue: string.Empty);

    public string Number
    {
        get => GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public static readonly StyledProperty<IEnumerable?> FieldsProperty =
        AvaloniaProperty.Register<EstimateCard, IEnumerable?>(nameof(Fields));

    public IEnumerable? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> DeleteClickedEvent =
        RoutedEvent.Register<EstimateCard, RoutedEventArgs>(nameof(DeleteClicked), RoutingStrategies.Bubble);

    /// <summary>Fired when the user clicks the ✕ button. Bubbles up.</summary>
    public event System.EventHandler<RoutedEventArgs> DeleteClicked
    {
        add    => AddHandler(DeleteClickedEvent, value);
        remove => RemoveHandler(DeleteClickedEvent, value);
    }

    private Button? _deleteButton;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_deleteButton is not null) _deleteButton.Click -= OnDeleteClick;
        _deleteButton = e.NameScope.Find<Button>("PART_Delete");
        if (_deleteButton is not null) _deleteButton.Click += OnDeleteClick;
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(DeleteClickedEvent));
}
