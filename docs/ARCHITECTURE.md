# PenPressureProfiler — Architecture

## Source Files

```
PenPressureProfiler/
│
├── Program.cs                    # Avalonia entry point (BuildAvaloniaApp)
├── App.axaml / App.axaml.cs      # Application definition, FluentTheme, shared styles
│                                 # (cards, typography, tab-active button)
│
├── MainWindow.axaml              # Window markup — 3-column layout, 2-tab centre chart,
│                                 # 2-tab right panel (Recording / Sweep)
├── MainWindow.axaml.cs           # Window logic: session wiring, tab switching, chart
│                                 # management, file I/O, keyboard shortcuts, drag-and-drop
│
├── ── Input / sessions ──
├── PenSessionManager.cs          # WinPenKit session owner + DispatcherTimer poll loop;
│                                 # always attaches AvaloniaPointerSession to PenInputSurface
├── ScaleSessionManager.cs        # Serial port read loop, marshals readings to UI thread
├── PenReadingData.cs             # Snapshot struct emitted each poll tick
│
├── ── Domain ──
├── MovingAverage.cs              # Windowed moving average (200 samples)
├── PressureRecord.cs             # Immutable (physical gf, logical fraction) pair
├── PressureRecordCollection.cs   # Ordered list; ToRecordArrays()
├── PressureTestFile.cs           # System.Text.Json model for manual-mode JSON
├── ScaleRecord.cs                # Parsed value from one scale serial line
├── ScaleParsedLine.cs            # Result of ScaleLineParser.Parse()
├── ScaleLineParser.cs            # Pure static parser for scale serial output
│
├── ── Sweep mode ──
├── SweepController.cs            # Stability detection, auto-capture logic
├── SweepCapture.cs               # One stable capture: averaged pair + raw sample lists
├── SweepCaptureRow.cs            # ListBox display row for sweep captures
├── PenSample.cs                  # Timestamped pen reading inside a stability window
├── ScaleSample.cs                # Timestamped scale reading inside a stability window
├── SweepSnapshotFile.cs          # System.Text.Json model for sweep session save/load
│
├── ── Logging ──
├── SessionLogger.cs              # Writes two timestamped CSV files during a session
│
├── ── UI helpers ──
├── Controls/LabeledReading.axaml(.cs)  # UserControl: caption + live value display row
│
└── docs/                         # This folder
```

---

## Layout

Three-column layout inside a `DockPanel` (ribbon docked to top):

| Column | Width | Contents |
|---|---|---|
| Left (310 px) | Fixed | Sensor cards: Pressure, Tilt, Button State, Scale, Logging |
| Centre (`*`) | Fills remaining | Two-tab chart area: Pressure Chart / Sweep Chart |
| Right (340 px) | Fixed | Two-tab panel: Recording (metadata + records) / Sweep (controls + captures) |

---

## PenInputSurface — the key architectural invariant

> **`AvaloniaPointerSession` must be attached to `PenInputSurface` — a plain `Border`
> with `Background="Transparent"` and no interactive children.**

`PenInputSurface` sits as the topmost layer in the centre column's chart `Grid`, covering both the Pressure and Sweep `AvaPlot` controls. Because both plots have `UserInputProcessor.IsEnabled = false`, the transparent overlay does not block any useful interaction.

This solves a subtle Avalonia routing issue: when `AvaloniaPointerSession` subscribes to `PointerMoved` on a *container* that has interactive children (TextBoxes, Buttons, etc.), those children can mark events `Handled` and suppress bubbling before the session's handler fires. By attaching to `PenInputSurface` — which has no children at all — the session always receives `PointerType.Pen` events first, regardless of what else is in the window.

### Why this matters for WinTab vs Avalonia Pointer

| Backend | Pen event delivery |
|---|---|
| `WintabSystem` | Global hook — delivers events regardless of pen position over the window |
| `AvaloniaPointerSession` | Local — only fires when the pen is physically over `PenInputSurface` |

For pressure profiling, the user positions the app window so the centre chart area covers the tablet region being tested.

---

## Layers

### Presentation
`MainWindow.axaml` + `MainWindow.axaml.cs`

All UI writes are on the Avalonia UI thread:
- Pen poll timer fires on the UI thread via `DispatcherTimer`.
- Scale serial reads are async; `ScaleSessionManager` marshals callbacks via `Dispatcher.UIThread.Post`.

### Session managers
`PenSessionManager` and `ScaleSessionManager` are injected with callbacks at construction. They own their lifecycle and call back to `MainWindow` only through delegates.

`PenSessionManager` always uses `PenInputSurface` as the Avalonia Pointer attachment point — it receives this control in its constructor, never the Window itself.

### Sweep logic
`SweepController` is a pure in-memory component fed by the same `OnPenDataReceived` and `OnScaleReading` callbacks that drive the live display. It has no Avalonia dependency.

### Data / files
`PressureTestFile`, `SweepSnapshotFile`, `SessionLogger` are independent of UI.

---

## Key Classes

### PenSessionManager
Owns the WinPenKit session, a `DispatcherTimer` (~60 fps poll), a `PenButtonTracker`, and a `MovingAverage` (200-sample window).

`Start(InputApi api)` handles both backends:
- `AvaloniaPointer` → `new AvaloniaPointerSession(_penInputSurface)` (always uses the injected surface)
- All others → `PenSessionFactory.Create(api)` + retrieve HWND via `TopLevel.GetTopLevel(_penInputSurface).TryGetPlatformHandle()`

Each tick:
1. `session.DrainPoints()` — all packets since last tick.
2. Each point fed into the moving average.
3. Button-state transitions detected; MA cleared on any release.
4. One `PenReadingData` emitted (last point + MA average + packet count).
5. No-packet ticks preserve last pressure when `TipDown` is true (avoids flicker with `AvaloniaPointerSession` which only fires on `PointerMoved`).

### SweepController
See **Stable Capture Logic** in the full architecture docs.

---

## Stable Capture Logic

`SweepController` auto-records a **(physical gf, logical %)** pair when both signals are steady.

### Sliding windows

```
penWindowDepth   = max(5, MinStableMs / 21)      ← ~48 Hz pen tick
scaleWindowDepth = max(2, MinStableMs / 115 + 1) ← ~8.7 Hz serial
```

### Stability checks (every pen tick)

```
penStable     = (penMax − penMin) ≤ PenTolerance
scaleStable   = (scaleMax − scaleMin) ≤ ScaleTolerance
penSaturated  = penMax ≥ 1.0         → excluded (pen clips all values above max)
penHasZeroRaw = any RawPressure == 0 → excluded (activation-threshold bounce)
```

Capture fires when all four preconditions hold **and** both timing gates pass:

| Gate | Purpose |
|---|---|
| `MinStableMs` | Both signals continuously eligible for at least this duration |
| `MinGapMs` | Minimum wall-clock gap between successive captures |

---

## Data Flow

### Pen input
```
Tablet → WinTab driver → WinPenKit (background thread)
  → DrainPoints()               DispatcherTimer tick, UI thread
  → PenButtonTracker, MovingAverage
  → PenReadingData emitted
  → OnPenDataReceived()         UI update + sweep feed
```

### Scale input
```
Scale → SerialPort.ReadLine()   Task.Run, thread-pool
  → ScaleLineParser.Parse()
  → Dispatcher.UIThread.Post()  marshal to UI thread
  → OnScaleReading()            UI update + sweep feed
```

---

## Threading Model

| Thread | Work |
|---|---|
| **UI thread** | All Avalonia controls; DispatcherTimer tick; `await` continuations; `SweepController`; `SessionLogger` |
| **WinPenKit background** | Packet capture queue; `DrainPoints()` is the handoff point |
| **ThreadPool** | `SerialPort.ReadLine()` only |

---

## External Dependencies

| Package | Role |
|---|---|
| `WinPenKit` v0.2.0 + `WinPenKit.Avalonia` | WinTab + Avalonia Pointer backends. Vendored at `libs/WinPenKit/v0.2.0/`. |
| `Avalonia` 11.3.x | UI framework. Mica via `TransparencyLevelHint` set in code (XAML parser cannot assign `IReadOnlyList<WindowTransparencyLevel>`). |
| `ScottPlot.Avalonia` 5.1.x | Pressure + sweep charts. `UserInputProcessor.IsEnabled = false` on both plots. |
| `System.IO.Ports` | Scale serial reading. |

---

## File Formats

### Manual session JSON
```json
{
  "brand": "WACOM", "pen": "PRO PEN 3", "inventoryid": "--P.0042",
  "date": "2026-05-22", "user": "SEVEN", "tablet": "PTH-860",
  "driver": "6.4.2", "os": "WINDOWS", "tags": "", "notes": "",
  "records": [ [10.0, 5.23], [100.0, 48.71] ]
}
```
Records are `[physical_gf, logical_percent]`.

### Sweep snapshot JSON
```json
{
  "captures": [{
    "physicalGf": 45.2, "logicalNorm": 0.235,
    "penSamples":   [{ "timestamp": "…", "rawPressure": 8192, "normalizedPressure": 0.25 }],
    "scaleSamples": [{ "timestamp": "…", "forceGf": 45.3 }]
  }]
}
```

### CSV logs (`Documents\PenPressureProfiler\Logs\`)
- `pen_YYYY-MM-DD_HHmmss.csv` — ~60 Hz stream; zero-pressure rows emitted on idle ticks.
- `scale_YYYY-MM-DD_HHmmss.csv` — one row per serial reading (~8–10 Hz).
