using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A single row of an estimates list: a bold <c>#N</c> number, an inline
/// reading line (<see cref="Segments"/>, e.g. "3.4 gf → 0.00% (0)" with the
/// numbers bold), and a ✕ delete button. Unifies the cards used by the Manual,
/// Stability, and Threshold lists.
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

    public static readonly StyledProperty<IReadOnlyList<ReadingSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<EstimateCard, IReadOnlyList<ReadingSegment>?>(nameof(Segments));

    /// <summary>The reading runs; numbers are rendered bold, the rest normal.</summary>
    public IReadOnlyList<ReadingSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> DeleteClickedEvent =
        RoutedEvent.Register<EstimateCard, RoutedEventArgs>(nameof(DeleteClicked), RoutingStrategies.Bubble);

    /// <summary>Fired when the user clicks the ✕ button. Bubbles up.</summary>
    public event System.EventHandler<RoutedEventArgs> DeleteClicked
    {
        add    => AddHandler(DeleteClickedEvent, value);
        remove => RemoveHandler(DeleteClickedEvent, value);
    }

    private Button?    _deleteButton;
    private TextBlock? _reading;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_deleteButton is not null) _deleteButton.Click -= OnDeleteClick;
        _deleteButton = e.NameScope.Find<Button>("PART_Delete");
        if (_deleteButton is not null) _deleteButton.Click += OnDeleteClick;

        _reading = e.NameScope.Find<TextBlock>("PART_Reading");
        RenderSegments();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty) RenderSegments();
    }

    private void RenderSegments()
    {
        if (_reading is null) return;

        var inlines = new InlineCollection();
        if (Segments is not null)
            foreach (var s in Segments)
                inlines.Add(new Run(s.Text)
                {
                    FontWeight = s.Bold ? FontWeight.Bold : FontWeight.Normal
                });

        _reading.Inlines = inlines;
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(DeleteClickedEvent));
}
