using Avalonia.Media;

namespace PenPressureProfiler;

/// <summary>
/// View-model row used by <see cref="SweepEditWindow"/>.
/// Carries violation state so the list can show non-monotonic
/// points with a distinct background.
/// </summary>
internal sealed class EditCaptureRow
{
    // Orange tint for violation rows, transparent for clean rows.
    private static readonly IBrush ViolatorBrush =
        new SolidColorBrush(Color.FromArgb(55, 255, 140, 0));

    public SweepCapture Capture     { get; }
    public string       DisplayText { get; }
    public bool         IsViolator  { get; }
    public IBrush       RowBackground => IsViolator ? ViolatorBrush : Brushes.Transparent;

    public EditCaptureRow(int index, SweepCapture capture, bool isViolator)
    {
        Capture    = capture;
        IsViolator = isViolator;

        string flag = isViolator ? "⚠ " : "  ";
        DisplayText =
            $"{flag}#{index:D3}  {capture.PhysicalGf,8:F2} gf  →  " +
            $"{capture.LogicalNorm * 100,7:F2}%  ×{capture.Count}";
    }
}
