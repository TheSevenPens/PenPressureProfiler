# PenPressureProfiler — Architecture

## Source Files

```
PenPressureProfiler/
├── Program.cs                   # Avalonia entry point
├── App.axaml / App.axaml.cs     # Application definition, theme, DataGrid styles
├── MainWindow.axaml             # Window markup (Avalonia XAML)
├── MainWindow.axaml.cs          # Window logic (code-behind)
│
├── AppState.cs                  # Central mutable state container
├── ScaleSession.cs              # Holds the moving-average for logical pressure
├── MovingAverage.cs             # Simple windowed moving average (inlined)
│
├── PressureRecord.cs            # Immutable paired measurement (physical, logical)
├── PressureRecordCollection.cs  # Ordered list of PressureRecords + JSON serialization
├── ScaleRecord.cs               # Parsed value from a single scale line
├── ScaleParsedLine.cs           # Result record from ParseScaleLine()
│
├── app.manifest                 # Windows DPI manifest
└── docs/                        # This folder
```

---

## Layers

### Presentation (Avalonia)
`MainWindow.axaml` + `MainWindow.axaml.cs`

- Three-column layout: tablet readings (left), chart (center), data recording + metadata (right).
- All UI updates happen on the Avalonia UI thread. The pen poll timer runs on the UI thread via `DispatcherTimer`. Serial port reads are async and marshal back to the UI thread naturally via `await` continuation.

### Business Logic
`MainWindow.axaml.cs` (event handlers and session management)

- Pen session lifecycle: `StartPenSession` / `StopPenSession`.
- Scale session lifecycle: `StartScaleSession` / `StopScaleSession` / `ReadSerialPortAsync`.
- Data capture: `button_record_Click` captures the current `AppState.PhysicalPressure` and moving-average logical pressure.
- Chart management: `InitializePlot`, `updatedata`, `ApplyAxisRange`, `UpdateCharTitle`.

### Data / State
`AppState`, `ScaleSession`, `PressureRecordCollection`, `PressureRecord`

- `AppState` is the single shared state object. It holds both session handles and the latest sensor readings.
- `ScaleSession` owns the `MovingAverage` that smooths the 200-sample window of logical pressure.
- `PressureRecordCollection` is the recorded dataset; also handles JSON text generation.

---

## Key Classes

### AppState
Central state container. Lives for the lifetime of the window.

```csharp
public class AppState
{
    public IPenSession?  PenSession     { get; set; }   // WinPenKit session
    public PenButtonTracker PenButtons  { get; }        // Tracks tip/barrel state
    public ScaleSession? ScaleSession   { get; set; }
    public SerialPort?   SerialPort     { get; set; }
    public CancellationTokenSource? ScaleCts { get; set; }
    public bool          ScaleIsReading { get; set; }
    public double        PhysicalPressure { get; set; } // Latest scale reading (gf)
    public double        LogicalPressure  { get; set; } // Latest normalized pen pressure
    public PressureRecordCollection? RecordCollection { get; set; }
}
```

### MovingAverage
Windowed running average over the last N samples. Window size = 200 samples. Used to smooth the logical pressure reading before recording, since raw WinTab pressure is noisy under sustained force.

### ScaleParsedLine (sealed record)
Immutable result of `ParseScaleLine()`. Carries the input string, a success flag, the parsed `ScaleRecord`, and an error message.

---

## Data Flow

### Pen Input
```
Tablet hardware
  → WinTab driver
  → WinPenKit (WintabSystem factory session, background thread)
  → session.DrainPoints()            called on DispatcherTimer tick (~60 fps)
  → PenButtonTracker.Update(pt)      button state tracking
  → MovingAverage.AddSample(...)     per point, preserving WinTab packet rate
  → UI labels updated
```

### Scale Input
```
Digital scale (serial)
  → SerialPort.ReadLine()            blocking, on Task.Run thread
  → ParseScaleLine(line)
  → AppState.PhysicalPressure = ...
  → label_force.Text updated         (on UI thread via await continuation)
```

### Recording
```
User: Ctrl+R / Record button
  → AppState.PhysicalPressure       (latest scale reading)
  → ScaleSession.LogicalPressure    MA.GetAverage()
  → PressureRecordCollection.Add()
  → updatedata()                    refreshes chart and DataGrid
```

---

## Threading Model

| Thread | Work |
|---|---|
| UI thread | All Avalonia controls, `DispatcherTimer` tick (pen poll), `await` continuations from serial reads |
| WinPenKit background thread | Packet capture into an internal queue; `DrainPoints()` is the thread-safe handoff |
| ThreadPool (Task.Run) | `SerialPort.ReadLine()` blocking read |

There is no explicit `Dispatcher.UIThread.Invoke` needed in `PenPollTimer_Tick` because `DispatcherTimer` already fires on the UI thread. The serial path's `await Task.Run(...)` continuation returns to the UI thread's `SynchronizationContext`.

---

## External Dependencies

| Package | Role | Notes |
|---|---|---|
| `WinPenKit` v0.2.0 | WinTab pen input | Vendored DLL at `libs/WinPenKit/v0.2.0/`. Using `InputApi.WintabSystem`. |
| `Avalonia` 11.3.x | UI framework | net10.0-windows target; same TF as WinPenKit |
| `ScottPlot.Avalonia` 5.1.x | Chart rendering | OxyPlot.Avalonia was tried first and confirmed broken on Avalonia 11.3.x |
| `Avalonia.Controls.DataGrid` 11.3.x | DataGrid control | Requires DataGrid theme include in App.axaml |
| `MessageBox.Avalonia` 3.3.x | Modal dialogs | Avalonia has no built-in MessageBox |
| `System.IO.Ports` | Serial communication | Scale reading |

---

## JSON Serialization

Serialization is hand-rolled in `PressureRecordCollection.GetRecordsJSON()` and `MainWindow.CreateJSONContent()`. Deserialization uses `System.Text.Json` into `PressureTestData` (a private class in MainWindow.axaml.cs).

Logical pressure is stored as a **percentage** (0–100) in the JSON file, but is held internally as a fraction (0–1) in `PressureRecord.LogicalPressure`. The conversion happens in `Add()` (÷100 on load, ×100 on write).
