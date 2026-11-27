using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PenPressureProfiler.Model;

namespace PenPressureProfiler.Controls;

/// <summary>
/// A two-handle range slider: a thin track, the selected span drawn in the
/// accent colour, and a value label over each handle. Drag a handle (the one
/// nearest the pointer is picked) or scroll the wheel over the track (Shift ×5).
/// Values snap to <see cref="Step"/>.
///
/// The handles move live while dragging, but <see cref="RangeCommitted"/> only
/// fires when the drag ends (or per wheel notch). A range change clears the
/// accumulator, so it must not fire continuously mid-drag.
/// </summary>
public sealed class RangeSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> LowerValueProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(LowerValue), 0);
    public static readonly StyledProperty<double> UpperValueProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(UpperValue), 100);
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Step), 1);
    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<RangeSlider, string>(nameof(Unit), "");

    public double Minimum    { get => GetValue(MinimumProperty);    set => SetValue(MinimumProperty, value); }
    public double Maximum    { get => GetValue(MaximumProperty);    set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
    public double Step       { get => GetValue(StepProperty);       set => SetValue(StepProperty, value); }
    public string Unit       { get => GetValue(UnitProperty);       set => SetValue(UnitProperty, value); }

    /// <summary>Raised when the user finishes changing the range (drag released,
    /// or a wheel notch). Not raised for programmatic changes.</summary>
    public event EventHandler? RangeCommitted;

    static RangeSlider()
    {
        AffectsRender<RangeSlider>(MinimumProperty, MaximumProperty, LowerValueProperty,
                                   UpperValueProperty, UnitProperty);
        FocusableProperty.OverrideDefaultValue<RangeSlider>(false);
    }

    public RangeSlider()
    {
        Height = 44;
        MinWidth = 160;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private const double TrackY       = 32;   // track centre line
    private const double ThumbRadius  = 7;
    private const double SidePadding  = 26;   // room for the end labels
    private const double LabelSize    = 13;

    private bool? _draggingLower;              // null = not dragging
    private (double Lower, double Upper) _dragStart;

    private double TrackLeft  => SidePadding;
    private double TrackWidth => Math.Max(1, Bounds.Width - 2 * SidePadding);
    private double X(double v) => TrackLeft + RangeMath.ValueToX(v, Minimum, Maximum, TrackWidth);

    public override void Render(DrawingContext ctx)
    {
        var track  = Brush("DividerBrush", Brushes.LightGray);
        var accent = Brush("SystemAccentColor", Brushes.RoyalBlue);
        var text   = Brush("PrimaryTextBrush", Brushes.Black);
        var bg     = Brush("RibbonBackgroundBrush", Brushes.White);

        // Transparent hit area across the whole control.
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        double left = TrackLeft, right = TrackLeft + TrackWidth;
        ctx.DrawLine(new Pen(track, 3, lineCap: PenLineCap.Round), new Point(left, TrackY), new Point(right, TrackY));

        double xl = X(LowerValue), xu = X(UpperValue);
        ctx.DrawLine(new Pen(accent, 3, lineCap: PenLineCap.Round), new Point(xl, TrackY), new Point(xu, TrackY));

        foreach (var x in new[] { xl, xu })
            ctx.DrawEllipse(bg, new Pen(accent, 2), new Point(x, TrackY), ThumbRadius, ThumbRadius);

        // Value labels centred over each handle, nudged apart if they would collide.
        var lt = Label(LowerValue, text);
        var ut = Label(UpperValue, text);
        double lx = xl - lt.Width / 2, ux = xu - ut.Width / 2;
        double overlap = lx + lt.Width + 6 - ux;
        if (overlap > 0) { lx -= overlap / 2; ux += overlap / 2; }
        lx = Math.Clamp(lx, 0, Math.Max(0, Bounds.Width - lt.Width));
        ux = Math.Clamp(ux, 0, Math.Max(0, Bounds.Width - ut.Width));
        ctx.DrawText(lt, new Point(lx, 2));
        ctx.DrawText(ut, new Point(ux, 2));
    }

    private FormattedText Label(double v, IBrush brush)
    {
        var s = v.ToString("0.##", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit);
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                 new Typeface(new FontFamily("Segoe UI,sans-serif")), LabelSize, brush);
    }

    private IBrush Brush(string key, IBrush fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var r))
        {
            if (r is IBrush b) return b;
            if (r is Color c) return new SolidColorBrush(c);
        }
        return fallback;
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    private double ValueAt(Point p) => RangeMath.XToValue(p.X - TrackLeft, Minimum, Maximum, TrackWidth);

    private bool NearestIsLower(Point p)
    {
        double dl = Math.Abs(p.X - X(LowerValue)), du = Math.Abs(p.X - X(UpperValue));
        if (Math.Abs(dl - du) < 0.5)   // handles stacked: pick by direction of the click
            return p.X < X(LowerValue);
        return dl < du;
    }

    private void MoveTo(bool lower, double proposed)
    {
        var (lo, hi) = RangeMath.MoveHandle(lower, proposed, LowerValue, UpperValue, Minimum, Maximum, Step);
        LowerValue = lo;
        UpperValue = hi;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        _draggingLower = NearestIsLower(p);
        _dragStart = (LowerValue, UpperValue);
        MoveTo(_draggingLower.Value, ValueAt(p));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_draggingLower is not { } lower) return;
        MoveTo(lower, ValueAt(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingLower is null) return;
        _draggingLower = null;
        e.Pointer.Capture(null);
        if (_dragStart != (LowerValue, UpperValue)) RangeCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_draggingLower is null) return;
        _draggingLower = null;
        if (_dragStart != (LowerValue, UpperValue)) RangeCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        bool lower = NearestIsLower(e.GetPosition(this));
        double mult = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 : 1;
        double delta = Step * mult * (e.Delta.Y >= 0 ? 1 : -1);
        var before = (LowerValue, UpperValue);
        MoveTo(lower, (lower ? LowerValue : UpperValue) + delta);
        if (before != (LowerValue, UpperValue)) RangeCommitted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
