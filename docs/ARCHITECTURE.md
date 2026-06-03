# PenPressureProfiler — Architecture

For terminology see [GLOSSARY.md](GLOSSARY.md). For per-flow sequencing see [CONTROL_FLOW.md](CONTROL_FLOW.md).
For named UI controls see [UI_MAP.md](UI_MAP.md). For the vendored input library see [WINPENKIT.md](WINPENKIT.md).

---

## Solution layout

```
PenPressureProfiler.slnx
├── PenPressureProfiler/             — the app (WinExe, net10.0-windows)
├── PenPressureProfiler.Tests/       — xUnit (pure-logic only)
└── Scribble.Avalonia/               — sibling demo: a Skia inking canvas using
                                       the same WinPenKit DLLs. Reference for
                                       PenPoint position fields. Not shipped.

libs/WinPenKit/v0.2.0/               — vendored WinPenKit.dll + .Avalonia.dll
.github/workflows/release.yml        — runs on v* tags only. No PR CI.
```

---

## Source files (main project)

```
PenPressureProfiler/
│
├── Program.cs                    # Avalonia entry point (BuildAvaloniaApp)
├── App.axaml / App.axaml.cs      # FluentTheme + shared styles
│                                 # (cards, typography, tab-active button)
│
├── MainWindow.axaml              # Ribbon (DockPanel.Top) + 3-column grid
├── MainWindow.axaml.cs           # God class (~970 LOC):
│                                 # session wiring, tabs, chart mgmt, file I/O,
│                                 # keyboard shortcuts, drag-and-drop, chart nav
├── SweepEditWindow.axaml         # Modal review/delete dialog
├── SweepEditWindow.axaml.cs      # Violation detection, list ↔ chart selection
├── EditCaptureRow.cs             # View-model row for the edit dialog
├── MetadataEditWindow.axaml      # Modal metadata editor (brand, pen, tablet, …)
├── MetadataEditWindow.axaml.cs   # Returns edited PressureTestFile, or null on cancel
├── AboutWindow.axaml(.cs)        # Modal About dialog: version + repo/README links
│
├── ── Input / sessions ──
├── PenSessionManager.cs          # WinPenKit session owner + 60fps poll loop;
│                                 # always attaches AvaloniaPointerSession to
│                                 # PenInputSurface
├── ScaleSessionManager.cs        # Serial read loop on threadpool, marshals
│                                 # parsed readings onto UI thread
├── PenReadingData.cs             # Snapshot emitted each poll tick
│
├── ── Domain ──
├── MovingAverage.cs              # Windowed mean (200 samples)
├── PressureRecord.cs             # Immutable (physical gf, logical fraction)
├── PressureRecordCollection.cs   # Ordered list of manual records
├── PressureTestFile.cs           # JSON model for manual-mode files
├── ScaleRecord.cs                # Parsed value from one scale serial line
├── ScaleParsedLine.cs            # Result of ScaleLineParser.Parse()
├── ScaleLineParser.cs            # Pure static parser for scale serial output
│
├── ── Sweep mode ──
├── SweepController.cs            # Stability detection + dedup; pure UI-thread
│                                 # in-memory component
├── SweepCapture.cs               # One captured pair + raw sample lists + Count
├── SweepCaptureCard.cs           # Card view-model for the right-panel ListBox
├── ManualRecordCard.cs           # Card view-model for the Manual record list
├── PenSample.cs                  # Timestamped pen reading inside a window
├── ScaleSample.cs                # Timestamped scale reading inside a window
├── SweepSnapshotFile.cs          # JSON model for sweep save/load
│
├── ── Threshold mode (IAF + MAX) ──
├── IafController.cs              # Release-sweep IAF estimator (from above);
│                                 # extrapolates last two nonzero (gf, raw)
│                                 # samples forward to raw = 0.
├── IafBelowController.cs         # Push-sweep IAF estimator (from below);
│                                 # arms when scale dips below 0.1 gf, then
│                                 # extrapolates first two nonzero samples
│                                 # backward to raw = 0.
├── MaxController.cs              # Push-sweep saturation estimator; triggers
│                                 # on the sub-saturation → saturation
│                                 # transition. Picked via the Threshold
│                                 # tab's sub-mode ComboBox in MainWindow.
│
├── ── Logging ──
├── SessionLogger.cs              # Two timestamped CSV files; UI-thread writes
│
└── Controls/
    └── LabeledReading.axaml(.cs) # UserControl: caption + value row
```

---

## Layout

`DockPanel` with a ribbon docked to the top and a 3-column `Grid` below.

| Region | Width | Contents |
|---|---|---|
| **Ribbon** (top) | full | PEN proximity · BUTTONS · ORIENTATION live readouts · **MODE** selector (Manual / Auto / Threshold / Monitor) · **HELP** About button |
| **Left** | 310 px | Pen card, Scale card, Device Inputs card (tablet + scale + logging rows) |
| **Centre** | `*` | Single chart area (Pressure, Sweep, Threshold, *or* the stacked Monitor pair), with `PenInputSurface` overlay. Chart visibility is driven by the ribbon MODE selector — there are no separate centre tabs. |
| **Right** | 580 px | The four panels (Manual / Auto / Threshold / Monitor) stack in the same cell, visibility-toggled by MODE. Threshold's sub-mode picker (*IAF from above* / *IAF from below* / *MAX from below*) lives inside that panel. Monitor's panel is a single help/clear card — the view itself is observation-only. |

No MVVM, no DI — the window owns all state. Session managers receive callbacks via constructor delegates; `SweepController` exposes C# events. See [UI_MAP.md](UI_MAP.md) for every named control.

---

## PenInputSurface — key architectural invariant

> **`AvaloniaPointerSession` must be attached to `PenInputSurface` — a plain
> `Border` with `Background="Transparent"` and no interactive children.**

`PenInputSurface` is the topmost layer in the centre column's chart `Grid`, covering all three `AvaPlot` controls. It has two roles:

### Role 1 — pointer attachment for the Avalonia backend

When `AvaloniaPointerSession` subscribes to `PointerMoved` on a *container* with interactive children (TextBoxes, Buttons, ScottPlot's own pan/zoom processor), those children can mark events `Handled` before the session's handler fires. By attaching to a childless `Border`, the session always receives `PointerType.Pen` events first.

| Backend | Pen event delivery |
|---|---|
| `WintabSystem` / `WintabDigitizer` | Global hook — delivers regardless of pointer position |
| `AvaloniaPointer` | Local — only fires when the pen is physically over `PenInputSurface` |

### Role 2 — chart navigation overlay

The same surface intercepts:

| Event | Handler | Effect |
|---|---|---|
| `PointerWheelChanged` | `OnChartAreaWheel` | Zoom in/out, centred on cursor, on the active chart |
| `PointerMoved` (when Space held) | `OnChartAreaPointerMoved` | Pan the active chart by pixel delta |
| `PointerPressed` (right button) | `OnChartAreaPointerPressed` | Reset axes to the currently selected range mode |

Because `PenInputSurface` and the charts share the same `Grid` cell, pixel coordinates are interchangeable — the overlay translates clicks/wheels through `Plot.GetCoordinates` to data coordinates and `Plot.Axes.SetLimits` to apply.

The plots themselves have `UserInputProcessor.IsEnabled = true`, but `PenInputSurface` sits on top and absorbs pointer events before the plot's own handlers run — so navigation is effectively driven entirely by our overlay handlers.

---

## Layers

### Presentation
`MainWindow.axaml` + `MainWindow.axaml.cs` — all UI writes are on the Avalonia UI thread.

#### Theming (light / dark)
The app follows the OS theme (`RequestedThemeVariant="Default"`). Colours come from two places:

- **XAML** uses app-defined brushes declared in `App.axaml` under `ResourceDictionary.ThemeDictionaries` (`Light` / `Dark` keys): `RibbonBackgroundBrush`, `RibbonBorderBrush`, `DividerBrush`, `CardBackgroundBrush`, `SecondaryTextBrush`, `TertiaryTextBrush`, `SubtleHoverBrush`, `CardDeleteBrush`. Everything references them via `{DynamicResource …}`, so they flip automatically. These are app-owned (not Fluent's built-in keys) to avoid depending on exact resource-key spellings.
- **ScottPlot charts** can't read XAML brushes, so [`ChartTheme.Apply`](../PenPressureProfiler/Controls/ChartTheme.cs) reads `Application.Current.ActualThemeVariant` and sets figure/data backgrounds + axis/grid colours in code. `MainWindow` subscribes to `ActualThemeVariantChanged` and calls `ReapplyChartThemes()` (colours only — axis limits preserved) so charts re-skin live when the theme flips. The scatter data colours (blue/orange/red/grey) are saturated enough to read on both themes and are left fixed.

The Fluent **accent colour** is pinned in `App.axaml` (`SystemAccentColor` + its `Light1–3` / `Dark1–3` shade ramp, all `#2563EB`-based) rather than inherited from the user's Windows accent — so accented controls (selection highlight, focus, slider fill, the About-dialog links) look identical on every machine and match the chart-data blue.

### Session managers
`PenSessionManager` owns the WinPenKit session, a `DispatcherTimer` (~60 fps), a `PenButtonTracker`, and a `MovingAverage` (200 samples). `Start(InputApi api)` handles both backend kinds:

- `AvaloniaPointer` → `new AvaloniaPointerSession(_penInputSurface)`
- All others → `PenSessionFactory.Create(api)` + HWND from `TopLevel.GetTopLevel(_penInputSurface).TryGetPlatformHandle()`

`ScaleSessionManager` reads serial lines on the threadpool, parses with `ScaleLineParser`, and marshals each parsed reading onto the UI thread via `Dispatcher.UIThread.Post`. Errors are surfaced through a `Func<string, string, Task>` injected at construction.

### Sweep logic
`SweepController` is a pure in-memory component fed by the same `OnPenDataReceived` and `OnScaleReading` callbacks that drive the live display. It has no Avalonia dependency, but is **not** thread-safe — all calls must be on the UI thread (the contract is met because both feeders are already UI-thread-marshalled). See [stable capture logic](#stable-capture-logic) below.

### Threshold logic (IAF + MAX)
The Threshold tab wraps three sibling controllers — one chart, one panel, one ComboBox sub-mode picker. All controllers share the same threading model and two feeders as `SweepController`; only the currently-selected one is fed (the others' estimates persist independently across mode switches).

- `IafController` — **IAF from above** (release sweep). Tracks the last two non-zero pen samples and the peak gf of the current press; on a raw nonzero→zero transition it linearly extrapolates `(gf, raw)` forward, solving for `gf` where `raw = 0`. A sweep only produces an estimate when the peak gf reached at least `MinPeakGf` (30 gf default). Stops at 10; final IAF is the median.
- `IafBelowController` — **IAF from below** (push sweep). Arms when the scale dips below `MaxRestingGf` (0.1 gf — the "rest" floor). On activation, collects the first two non-zero pen samples and linearly extrapolates `(gf, raw)` *backward*, solving for `gf` where `raw = 0`. Each cycle is consumed on the second sample and re-arms only when the scale dips below 0.1 gf again. Pressing without first lifting fires `SweepRejected`. Stops at 10; final IAF is the median.
- `MaxController` — **MAX from below** (push sweep). Tracks the last two **sub-saturated** non-zero samples and fires on the sub-saturated→saturated transition (`NormalizedPressure ≥ 1.0`). Extrapolation in `(gf, norm)` space solves for `gf` where `norm = 1.0`. Each cycle is consumed on a saturation hit and re-armed by a full lift (`RawPressure == 0`), so a held-at-MAX press still only produces one estimate. Stops at 10; final MAX is the median.

Switching the ComboBox stops any active capture (`_thresholdEnabled = false`) and refreshes the chart + list against the new controller's data. Estimates accumulated for each sub-mode survive the switch.

### Edit dialogs
- `SweepEditWindow` — opened via `ShowDialog<List<SweepCapture>?>(parent)`. Works on a local sorted copy of the captures; **Done** returns the survivors, **Cancel** returns null. Includes monotonic-violation detection (`ComputeViolators`) for UI highlighting.
- `MetadataEditWindow` — opened via `ShowDialog<PressureTestFile?>(parent)`. Edits a local copy of the session metadata; **Done** returns the edited `PressureTestFile`, **Cancel** (or `Esc`) returns null. `MainWindow` holds the canonical metadata as `_metadata` and only replaces it on a non-null result.

### Data / files
`PressureTestFile`, `SweepSnapshotFile`, `SessionLogger` are independent of UI.

---

## Key Classes

### PenSessionManager

Each timer tick:

1. `session.DrainPoints()` — all packets since last tick.
2. Each point fed into `PenButtonTracker` and `MovingAverage`.
3. Button-state transitions detected by comparing to last tick; MA cleared on **any** release.
4. One `PenReadingData` emitted (last point + MA average + packet count).
5. **No-packet ticks** preserve last pressure when `TipDown == true`. This avoids flicker with `AvaloniaPointerSession`, which only fires on `PointerMoved`.

### SweepController

Two feeders + two events:

```csharp
void OnPenData(PenReadingData d);    // checks stability, possibly fires StableCaptured
void OnScaleData(double gf);          // updates window, fires RawPairAvailable

event Action<double, double> RawPairAvailable;    // (physGf, penNormAvg) — drives grey dots
event Action<SweepCapture>   StableCaptured;      // either a new or re-confirmed capture
```

`OnPenData` ignores idle ticks (`PacketCount == 0`) so the window isn't disturbed by no-op frames. Captures are bounded by `MaxCaptures = 2000`.

#### Dedup count

When a new stable capture lands within (`ScaleTolerance`, `PenTolerance`) of an existing capture, the existing one's `Count` is incremented and `StableCaptured` is fired with the existing instance (not a new one). The right-panel ListBox shows this as `×N`. So adding a new capture only happens when the point is genuinely new under the current tolerances.

### SweepEditWindow

Modal dialog over a local `List<SweepCapture>`. Three view layers stay in sync:

- `_captures` (sorted by `PhysicalGf`) — the truth.
- `listBox_edit.ItemsSource` — `EditCaptureRow` view-models with `IsViolator` flag.
- `editPlotView` — three scatter series (clean / violator / selected), redrawn without resetting axis limits so user zoom persists.

`ComputeViolators` runs the captures in physical-force order and flags any whose `LogicalNorm` is strictly less than the running max — i.e. the curve dips backwards. Plateaus (equal values) are **not** flagged.

---

## Stable Capture Logic

`SweepController` auto-records a `(physGf, logical-norm)` pair when both signals are steady.

### Sliding windows

```
penWindowDepth   = max(5, MinStableMs / 21)        ← ~48 Hz pen tick
scaleWindowDepth = max(2, MinStableMs / 115 + 1)   ← ~8.7 Hz serial
```

### Stability checks (every pen tick with packets)

```
penStable    = (penMax − penMin) ≤ PenTolerance
scaleStable  = (scaleMax − scaleMin) ≤ ScaleTolerance
```

Capture fires when both stability checks hold **and** both timing gates pass.
Earlier versions also excluded saturated windows (`penMax ≥ 1.0`) and zero-raw
windows (`any RawPressure == 0`); those guards have been removed so the full
response curve — including the saturation plateau and activation-threshold
region — is captured.

| Gate | Purpose |
|---|---|
| `MinStableMs` | Both signals continuously eligible for at least this duration |
| `MinGapMs` | Minimum wall-clock gap between successive captures |

Any disturbance (`penStable` or `scaleStable` becomes false) resets `_stableStart`.

---

## Data Flow

### Pen input
```
Tablet → WinTab driver → WinPenKit (background thread)
  → DrainPoints()               DispatcherTimer tick, UI thread
  → PenButtonTracker, MovingAverage
  → PenReadingData emitted
  → OnPenDataReceived()         UI update + sweep feed (if enabled)
```

### Scale input
```
Scale → SerialPort.ReadLine()   Task.Run, thread-pool
  → ScaleLineParser.Parse()
  → Dispatcher.UIThread.Post()  marshal to UI thread
  → OnScaleReading()            UI update + sweep feed (if enabled)
```

---

## Threading Model

| Thread | Work |
|---|---|
| **UI thread** | All Avalonia controls; DispatcherTimer tick; `await` continuations; `SweepController`; `SessionLogger` writes |
| **WinPenKit background** | Packet capture queue; `DrainPoints()` is the handoff point |
| **ThreadPool** | `SerialPort.ReadLine()` inside `Task.Run` |

`SweepController` and `SessionLogger` rely on the UI-thread guarantee — they're not safe to call from elsewhere.

---

## External Dependencies

| Package | Role |
|---|---|
| `WinPenKit` v0.2.0 + `WinPenKit.Avalonia` | WinTab + Avalonia Pointer backends. Vendored at `libs/WinPenKit/v0.2.0/`. See [WINPENKIT.md](WINPENKIT.md). |
| `Avalonia` 11.3.x | UI framework. Mica via `TransparencyLevelHint` set in code (XAML parser cannot assign `IReadOnlyList<WindowTransparencyLevel>`). |
| `ScottPlot.Avalonia` 5.1.x | Pressure + sweep charts. Pointer input is captured by `PenInputSurface` first, so ScottPlot's own input processor is effectively bypassed. |
| `System.IO.Ports` | Scale serial reading. |

---

## File Formats

### Manual session JSON
```json
{
  "brand": "WACOM", "pen": "PRO PEN 3", "penfamily": "PRO",
  "inventoryid": "--P.0042", "date": "2026-05-22", "user": "SEVEN",
  "tablet": "PTH-860", "driver": "6.4.2", "os": "WINDOWS",
  "tags": "", "notes": "",
  "records": [ [10.0, 5.23], [100.0, 48.71] ]
}
```
Records are `[physical_gf, logical_percent]`. The model is `PressureTestFile`; `ToRecordCollection` rescales percent → fraction on load.

### Sweep snapshot JSON
```json
{
  "captures": [{
    "count": 3,
    "physicalGf": 45.2, "logicalNorm": 0.235,
    "penSamples":   [{ "timestamp": "…", "rawPressure": 8192,
                       "normalizedPressure": 0.25, "altitude": 82.4 }],
    "scaleSamples": [{ "timestamp": "…", "forceGf": 45.3 }]
  }]
}
```
Includes every raw pen + scale sample inside each capture's stability window, plus the dedup `count`. Each pen sample also carries the pen `altitude` (degrees from the tablet surface, 0–90); preserved in the snapshot for diagnostics even though the live Sweep chart no longer uses it. Snapshots written by older versions of the app omit `altitude` and round-trip with `0.0`.

### CSV logs (`Documents\PenPressureProfiler\Logs\`)
- `pen_YYYY-MM-DD_HHmmss.csv` — ~60 Hz stream of pen state.
- `scale_YYYY-MM-DD_HHmmss.csv` — one row per parsed serial reading (~8–10 Hz).

---

## What's not here yet

- **No PR CI** — only the release-tag workflow runs tests. Run `dotnet test` locally.
- **`SweepController` has no clock injection** — uses `DateTime.UtcNow` directly, so it's not easily unit-testable. See [TESTING.md](TESTING.md) for what's worth refactoring.
- **`MainWindow` is a god class** — ~970 LOC, no view-model split. Anything UI-related lives there.
