using System.ComponentModel;
using Avalonia.Media;

namespace PenPressureProfiler.ViewModels;

/// <summary>
/// One row of the Accumulator data table — a fixed force bucket. The
/// row set is built once for the configured span; counts and the per-cell
/// backgrounds are updated in place (via change notification) each refresh, so
/// rows never shift and scroll position is preserved.
/// <para>"Under" / "AtOrOver" are the threshold counters (samples below vs at-or-over
/// the active target's pressure threshold).</para>
/// </summary>
public sealed class AccumulatorRow : INotifyPropertyChanged
{
    /// <summary>The bucket's force range, e.g. "0.50 &lt; 1.00".</summary>
    public string Phys   { get; }
    /// <summary>The row's fixed zebra background (the base when no other tint applies).</summary>
    public IBrush RowBg  { get; }

    public AccumulatorRow(string phys, IBrush rowBg)
    {
        Phys        = phys;
        RowBg       = rowBg;
        _physBg     = rowBg;
        _underBg    = rowBg;
        _atOrOverBg = rowBg;
    }

    /// <summary>Background for the PHYS / % cells — the row's effective base
    /// (zebra, or a %-based tint once enough samples land).</summary>
    private IBrush _physBg;
    public IBrush PhysBg { get => _physBg; set => Set(ref _physBg, value, nameof(PhysBg)); }

    private string _underCnt = "0";
    public string UnderCnt { get => _underCnt; set => Set(ref _underCnt, value, nameof(UnderCnt)); }

    private string _atOrOverCnt = "0";
    public string AtOrOverCnt { get => _atOrOverCnt; set => Set(ref _atOrOverCnt, value, nameof(AtOrOverCnt)); }

    /// <summary>"at-or-over" as a percentage of this row's total samples ("—" when empty).</summary>
    private string _atOrOverPct = "—";
    public string AtOrOverPct { get => _atOrOverPct; set => Set(ref _atOrOverPct, value, nameof(AtOrOverPct)); }

    private IBrush _underBg;
    public IBrush UnderBg { get => _underBg; set => Set(ref _underBg, value, nameof(UnderBg)); }

    private IBrush _atOrOverBg;
    public IBrush AtOrOverBg { get => _atOrOverBg; set => Set(ref _atOrOverBg, value, nameof(AtOrOverBg)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string name)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
