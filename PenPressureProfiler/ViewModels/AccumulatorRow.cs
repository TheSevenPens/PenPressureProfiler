using System.ComponentModel;
using Avalonia.Media;

namespace PenPressureProfiler.ViewModels;

/// <summary>
/// One row of the Threshold Accumulator data table — a fixed force bucket. The
/// row set is built once for the configured span; counts and the per-cell
/// backgrounds are updated in place (via change notification) each refresh, so
/// rows never shift and scroll position is preserved.
/// </summary>
public sealed class AccumulatorRow : INotifyPropertyChanged
{
    /// <summary>The bucket's force range, e.g. "0.50 &lt; 1.00".</summary>
    public string Phys   { get; }
    /// <summary>Zebra background for the PHYS cell (fixed per row).</summary>
    public IBrush PhysBg { get; }
    /// <summary>The row's base zebra background (used to clear the change tint).</summary>
    public IBrush RowBg  { get; }

    public AccumulatorRow(string phys, IBrush rowBg)
    {
        Phys      = phys;
        PhysBg    = rowBg;
        RowBg     = rowBg;
        _zeroBg   = rowBg;
        _nonZeroBg = rowBg;
    }

    private string _zeroCnt = "0";
    public string ZeroCnt { get => _zeroCnt; set => Set(ref _zeroCnt, value, nameof(ZeroCnt)); }

    private string _nonZeroCnt = "0";
    public string NonZeroCnt { get => _nonZeroCnt; set => Set(ref _nonZeroCnt, value, nameof(NonZeroCnt)); }

    /// <summary>">0%" as a percentage of this row's total samples ("—" when empty).</summary>
    private string _onPct = "—";
    public string OnPct { get => _onPct; set => Set(ref _onPct, value, nameof(OnPct)); }

    private IBrush _zeroBg;
    public IBrush ZeroBg { get => _zeroBg; set => Set(ref _zeroBg, value, nameof(ZeroBg)); }

    private IBrush _nonZeroBg;
    public IBrush NonZeroBg { get => _nonZeroBg; set => Set(ref _nonZeroBg, value, nameof(NonZeroBg)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string name)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
