# PenPressureProfiler — Architecture

## Source Files

```
PenPressureProfiler/
│
├── Program.cs                    # Avalonia entry point (BuildAvaloniaApp)
├── App.axaml / App.axaml.cs      # Application definition, FluentTheme, DataGrid styles,
│                                 # shared Avalonia Style rules (buttons, cards, typography)
│
├── MainWindow.axaml              # Window markup — three-column layout, three chart tabs
├── MainWindow.axaml.cs           # Window logic: wiring, tab switching, chart management,
│                                 # file I/O, keyboard shortcuts, drag-and-drop
│
├── ── Input / sessions ──
├── PenSessionManager.cs          # WinTab poll loop (DispatcherTimer), button tracking,
│                                 # moving average, emits PenReadingData each tick
├── ScaleSessionManager.cs        # Serial port read loop, marshals readings to UI thread
├── PenReadingData.cs             # Snapshot struct emitted by PenSessionManager each tick
│
├── ── Domain ──
├── MovingAverage.cs              # Windowed moving average (window = 200 samples)
├── PressureRecord.cs             # Immutable (physical gf, logical fraction) pair
├── PressureRecordCollection.cs   # Ordered list of PressureRecords; ToRecordArrays()
├── PressureTestFile.cs           # System.Text.Json model for manual-mode JSON files
├── ScaleRecord.cs                # Parsed value from one scale serial line
├── ScaleParsedLine.cs            # Result of ScaleLineParser.Parse()
├── ScaleLineParser.cs            # Pure static parser for scale serial output
│
├── ── Sweep mode ──
├── SweepController.cs            # Stability detection, auto-capture logic
├── SweepCapture.cs               # One stable capture: averaged pair + raw sample lists
├── PenSample.cs                  # Timestamped pen reading (Timestamp, RawPressure, Norm)
├── ScaleSample.cs                # Timestamped scale reading (Timestamp, ForceGf)
├── SweepSnapshotFile.cs          # System.Text.Json model for sweep session save/load
│
├── ── Logging ──
├── SessionLogger.cs              # Writes two timestamped CSV files during a session
│
├── ── UI helpers ──
├── Controls/LabeledReading.axaml(.cs)  # UserControl: caption + live value display row
├── Controls/LabeledField.axaml(.cs)    # UserControl: label + editable TextBox
├── SweepCaptureRow.cs            # DataGrid view-model row for the Sweep Data tab
├── StringExtensions.cs           # IfEmpty() extension
│
├── app.manifest                  # Windows Per-Monitor V2 DPI manifest
└── docs/                         # This folder
```

---

## Layers

### Presentation (Avalonia)
`MainWindow.axaml` + `MainWindow.axaml.cs`

Three-column layout:
- **Left** — live tablet readings (Pressure card, Orientation card, Button State card), Scale control, Logging card
- **Centre** — chart area with Manual / Sweep / Sweep Data tab strip
- **Right** — manual recording buttons, record DataGrid, metadata fields, file operations

All UI writes happen on the Avalonia UI thread:
- Pen poll timer fires on the UI thread via `DispatcherTimer`.
- Scale serial reads are async; `ScaleSessionManager` marshals `_onReading` via `Dispatcher.UIThread.Post` before touching any controls.

### Session managers
`PenSessionManager` and `ScaleSessionManager` are injected with callbacks at construction. They own their own lifecycle and call back to `MainWindow` only via the supplied delegates.

### Sweep logic
`SweepController` is a pure in-memory component fed from the same `OnPenDataReceived` and `OnScaleReading` callbacks that drive the live display. It has no Avalonia dependency.

### Data / files
`PressureTestFile` (manual JSON), `SweepSnapshotFile` (sweep JSON), `SessionLogger` (CSV) are all independent of UI.

---

## Key Classes

### PenSessionManager
Owns the WinPenKit session, a `DispatcherTimer` (~60 fps poll), a `PenButtonTracker`, and a `MovingAverage` (200-sample window).

Each tick:
1. `session.DrainPoints()` — collects all WinTab packets since last tick.
2. Feeds each point into the moving average.
3. Detects button-state transitions; clears the MA on any button release.
4. Emits one `PenReadingData` to the `onPenData` callback with the last point's values, the MA average, and the packet count.

### ScaleSessionManager
Owns the `SerialPort` for the duration of one session (opened on `StartAsync`, disposed in `finally`). Uses `Task.Run(() => port.ReadLine())` to avoid blocking the UI, then calls `Dispatcher.UIThread.Post(() => _onReading(...))` so all downstream processing is on the UI thread.

### SweepController
See **Stable Capture Logic** below.

### MovingAverage
Window size 200. Recomputes from `samples.Sum() / samples.Count` on each `GetAverage()` call (O(window)) to avoid floating-point drift from incremental sum accumulation.

---

## Stable Capture Logic

`SweepController` watches the live pen and scale streams and automatically records a **(physical gf, logical %)** pair whenever both signals have been steady long enough to be trusted.

### Inputs

| Source | Rate | What is tracked |
|---|---|---|
| `OnPenData(PenReadingData d)` | ~48 Hz (DispatcherTimer) | `d.NormalizedPressure`, `d.RawPressure` |
| `OnScaleData(double gf)` | ~8–10 Hz (serial) | Force in gram-force |

### Sliding windows

Two queues accumulate recent history. Their depths are derived from `MinStableMs` so both windows cover the same wall-clock duration regardless of sample rate:

```
penWindowDepth   = max(5, MinStableMs / 21)     ← 21 ms ≈ average pen tick interval at ~48 Hz
scaleWindowDepth = max(2, MinStableMs / 115 + 1) ← 115 ms ≈ average scale interval at ~8.7 Hz
```

Each entry in the pen queue is a `PenSample(Timestamp, RawPressure, NormalizedPressure)`.
Each entry in the scale queue is a `ScaleSample(Timestamp, ForceGf)`.

### Stability checks (evaluated every pen tick)

```
penMin    = min NormalizedPressure in pen window
penMax    = max NormalizedPressure in pen window

penStable     = (penMax − penMin) ≤ PenTolerance
scaleStable   = (scaleMax − scaleMin) ≤ ScaleTolerance
                  where min/max are over the scale window's ForceGf values

penSaturated  = penMax ≥ 1.0
                  → pen clips all forces above its hardware maximum to 100%;
                    those readings are ambiguous and must be excluded.

penHasZeroRaw = any sample in pen window has RawPressure == 0
                  → a window bouncing between raw 0 and 1 looks stable by
                    normalised variance but the pen is at the activation
                    threshold where readings are unreliable.
```

All four preconditions must be satisfied simultaneously:

```
capture eligible = penStable
                 ∧ scaleStable
                 ∧ lastScaleGf > 0       ← scale must be actively reading
                 ∧ ¬penSaturated
                 ∧ ¬penHasZeroRaw
```

### Timing gates

Two timers prevent over-capturing during a long stable hold:

| Gate | Purpose |
|---|---|
| `MinStableMs` | Both signals must have been continuously eligible for at least this long before a capture fires. Tracked via `_stableStart` — set when eligibility first becomes true, cleared when it breaks. |
| `MinGapMs` | Minimum wall-clock gap between two successive captures. After a capture, `_stableStart` is reset to `null` so the next capture requires a fresh `MinStableMs` stable run. |

Effective timing: **each capture requires `MinStableMs` of stable signal, with at least `MinGapMs` since the previous capture.** These are independent: a new stable window must accumulate from scratch each time.

### What is captured

When all conditions are met:

```csharp
new SweepCapture(
    PhysicalGf   = average(scaleWindow.ForceGf),
    LogicalNorm  = average(penWindow.NormalizedPressure),
    PenSamples   = snapshot of penWindow  (Timestamp, RawPressure, NormalizedPressure per entry),
    ScaleSamples = snapshot of scaleWindow (Timestamp, ForceGf per entry)
)
```

The averaged pair is the reported measurement. The raw sample lists are retained for quality inspection and future analysis (variance, outlier detection, timestamp-based lag analysis).

### Cap

`MaxCaptures` (default 2000) prevents unbounded memory use during long sessions. The capture list stops growing when the cap is reached; the label shows "(limit reached)".

---

## Data Flow

### Pen input
```
Tablet hardware
  → WinTab driver
  → WinPenKit (WintabSystem session, background thread)
  → session.DrainPoints()            DispatcherTimer tick (~60 fps, UI thread)
  → PenButtonTracker.Update(pt)      button state transitions
  → MovingAverage.AddSample(norm)    per WinTab packet
  → PenReadingData emitted           one per tick (last packet + MA average)
  → OnPenDataReceived()              UI labels, pressure bar, pen rate counter
  → sweepController.OnPenData()      stability window update
```

### Scale input
```
Digital scale (serial port)
  → SerialPort.ReadLine()            blocking, on Task.Run thread pool
  → ScaleLineParser.Parse(line)
  → Dispatcher.UIThread.Post(...)    marshal to UI thread
  → OnScaleReading()                 UI label, rate counter, physicalPressure field
  → sweepController.OnScaleData()    scale window update, RawPairAvailable event
```

### Manual recording
```
User: Ctrl+R / Record button
  → physicalPressure                 (latest scale reading)
  → logicalPressure                  (latest pen MA from PenReadingData.SmoothedPressure)
  → PressureRecordCollection.Add()
  → UpdateData()                     refreshes chart and DataGrid
```

### Sweep auto-capture
```
SweepController.OnPenData() — each pen tick
  → update pen window (PenSample queue)
  → evaluate stability checks
  → if eligible and timing gates satisfied:
      → SweepCapture created with averages + window snapshots
      → StableCaptured event fired
      → MainWindow: label_captureCount updated, sweep plot refreshed
      → (if Sweep Data tab open) sweepDataGrid refreshed
```

---

## Threading Model

| Thread | Work |
|---|---|
| **UI thread** | All Avalonia controls; `DispatcherTimer` tick (pen poll); `await` continuations from serial reads; `SweepController` (all methods); `SessionLogger` writes |
| **WinPenKit background thread** | Packet capture into internal queue; `DrainPoints()` is the thread-safe handoff point |
| **ThreadPool (Task.Run)** | `SerialPort.ReadLine()` blocking call only |

`ScaleSessionManager` is the only class that crosses threads. It uses `Dispatcher.UIThread.Post` to marshal the `_onReading` callback back to the UI thread before any state is touched.

---

## External Dependencies

| Package | Role | Notes |
|---|---|---|
| `WinPenKit` v0.2.0 | WinTab pen input | Vendored DLL at `libs/WinPenKit/v0.2.0/`. `InputApi.WintabSystem` (system context — pen drives cursor). |
| `Avalonia` 11.3.x | UI framework | `net10.0-windows`. Mica transparency via `TransparencyLevelHint` set in code (XAML parser cannot assign `IReadOnlyList<WindowTransparencyLevel>`). |
| `ScottPlot.Avalonia` 5.1.x | Chart rendering | OxyPlot.Avalonia 2.1.0-avalonia11 was tried first and confirmed broken on Avalonia 11.3.x. |
| `Avalonia.Controls.DataGrid` 11.3.x | DataGrid | Requires `StyleInclude` for DataGrid Fluent theme in `App.axaml`. |
| `MessageBox.Avalonia` 3.3.x | Modal dialogs | Avalonia has no built-in MessageBox. |
| `System.IO.Ports` | Serial port | Scale reading. |

---

## File Formats

### Manual session JSON (`Documents\PenPressureProfiler\*.json`)
```json
{
  "brand": "WACOM", "pen": "PRO PEN 3", "inventoryid": "--P.0042",
  "date": "2026-05-22", "user": "SEVEN", "tablet": "PTH-860",
  "driver": "6.4.2", "os": "WINDOWS", "tags": "", "notes": "",
  "records": [ [10.0, 5.23], [100.0, 48.71] ]
}
```
Records are `[physical_gf, logical_percent]`. Logical is stored as **percent** in JSON but held as **fraction** (0–1) in `PressureRecord`.

### Sweep snapshot JSON
```json
{
  "captures": [{
    "physicalGf": 45.2, "logicalNorm": 0.235,
    "penSamples": [
      { "timestamp": "2026-05-22T14:33:30.119Z", "rawPressure": 8192, "normalizedPressure": 0.25 }
    ],
    "scaleSamples": [
      { "timestamp": "2026-05-22T14:33:30.134Z", "forceGf": 45.3 }
    ]
  }]
}
```
`logicalNorm` is a fraction (0–1). Timestamps are UTC ISO 8601.

### CSV logs (`Documents\PenPressureProfiler\Logs\`)
- `pen_YYYY-MM-DD_HHmmss.csv` — continuous ~60 Hz stream; `PacketCount=0` rows are zero-fill ticks (no WinTab contact).
- `scale_YYYY-MM-DD_HHmmss.csv` — one row per serial reading, ~8–10 Hz.
