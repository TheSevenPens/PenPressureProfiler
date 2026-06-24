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

libs/WinPenKit/v0.3.0/               — vendored WinPenKit.dll + .Avalonia.dll
.github/workflows/release.yml        — runs on v* tags only. No PR CI.
```

---

## Source files (main project)

Folder = namespace. The root namespace is `PenPressureProfiler`; each subfolder
is `PenPressureProfiler.<Folder>`. A `GlobalUsings.cs` at the root `global using`s
all the internal namespaces, so files rarely need explicit cross-namespace usings.

```
PenPressureProfiler/
│
├── Program.cs                    # Entry point; sets ScottPlot default font
├── App.axaml / App.axaml.cs      # FluentTheme, theme brushes, shared styles
├── MainWindow.axaml(.cs)         # Menu + ribbon + 2-column grid; the god class
│                                 # (session wiring, mode switching, chart mgmt,
│                                 #  file I/O, drag-drop, live crosshair)
├── GlobalUsings.cs               # global usings for the internal namespaces
│
├── Views/                        # namespace PenPressureProfiler.Views — dialogs
│   ├── AboutWindow.axaml(.cs)        # version + repo/README links
│   ├── MetadataEditWindow.axaml(.cs) # returns edited SessionMetadata or null
│   ├── MeasureScaleLagWindow.axaml(.cs) # measures the scale's response lag (τ)
│   ├── OptionsWindow.axaml(.cs)      # Tools ▸ Options; returns edited AppOptions or null
│   └── StabilityEditWindow.axaml(.cs)# review/delete dialog; violation detection
│
├── Controls/                     # namespace .Controls — reusable widgets
│   ├── LabeledReading.axaml(.cs)     # caption + value row
│   ├── ReadingSegment.cs             # one segment of a multi-part reading line
│   ├── RibbonGroup.cs                # ribbon header + content + separator
│   ├── StatusDotRow.cs               # label + state dot (+ brush converter)
│   ├── EstimateCard.cs               # #N + segment strip + ✕ delete (list rows)
│   ├── CaptureListSection.cs         # title/actions/meta/list card frame
│   ├── SortToggleButton.cs           # ↑/↓ Force toggle
│   └── ChartTheme.cs                 # per-AvaPlot bg + axis/grid colours
│
├── ViewModels/                   # namespace .ViewModels — list-row VMs
│   ├── StabilityCaptureCard.cs
│   ├── AccumulatorRow.cs             # one BUCKETS-table row (PHYS / UNDER / OVER / %)
│   └── EditCaptureRow.cs             # row for the StabilityEditWindow list
│
├── Detection/                    # namespace .Detection — in-memory analysers
│   ├── StabilityController.cs        # Curve (stability) detection + dedup
│   └── AccumulatorController.cs      # Accumulator: buckets physical force,
│                                     #   counts under/at-or-over a per-target
│                                     #   threshold (IAF | Max); % read off buckets
│
├── Sessions/                     # namespace .Sessions — hardware + logging
│   ├── PenSessionManager.cs          # WinPenKit session + 60fps poll loop
│   ├── ScaleSessionManager.cs        # owns the SerialPort; read loop + SendTare()
│   └── SessionLogger.cs              # two timestamped CSV files
│
└── Model/                        # namespace .Model — UI-free data + parsing
    ├── SessionMetadata.cs            # shared metadata block (snapshot "metadata")
    ├── StabilityCapture.cs / StabilitySnapshotFile.cs
    ├── PenSample.cs / ScaleSample.cs / PenReadingData.cs
    ├── ScaleRecord.cs / ScaleParsedLine.cs / ScaleLineParser.cs
    └── MovingAverage.cs
```

The manual-capture data model (`PressureRecord`, `PressureRecordCollection`,
`PressureTestFile`) and the `ManualRecordCard` row VM were removed when Manual
mode was dropped; saved files are now stability snapshots only. The old
Threshold mode (the `IafController` / `IafBelowController` / `MaxController`
sibling controllers, the IAF-from-below/above + MAX-from-below sub-mode picker,
and the per-capture raw recording + `ThresholdReviewWindow`) was removed and
replaced by the single `AccumulatorController`.

---

## Layout

`DockPanel` with a `Menu` and a ribbon docked to the top and a **2-column**
`Grid` below. There is **no left sidebar** — all live readouts and controls live
in the ribbon. The window `Background` is `RibbonBackgroundBrush`.

| Region | Width | Contents |
|---|---|---|
| **Menu** (top) | full | **Edit → Metadata…** (session metadata dialog) · **Tools → Measure Scale Lag… / Options…** (the [`OptionsWindow`](../PenPressureProfiler/Views/OptionsWindow.axaml.cs) settings dialog) · **Chart → Save Image as PNG… / Copy Image to Clipboard** (active chart, via [`ChartImage`](../PenPressureProfiler/Controls/ChartImage.cs)) · **Help → About** |
| **Ribbon** (top) | full | DEVICES (tablet/scale/logging) · PEN proximity + buttons + orientation + hover Z · PEN PRESSURE · SCALE PRESSURE · **MODE** dropdown (**Curve** / **Time series** / **Accumulator**) — the MODE group also hosts the active mode's primary controls (Curve/Time series: the auto-capture **Start** toggle + per-mode option Follow live / Overlay traces, in `group_mode_curve`; Accumulator: **Measure** target + **Start/Clear**, in `group_mode_accumulator`) · the shared `group_curve_capture` AUTO-CAPTURE group (Edit… flyout + settings summary) for Curve and Time series · `group_accumulator_settings` (Range + Bucket) for Accumulator |
| **Centre** | `*` (col 0) | Single chart area (the Curve **scatter** chart `stabilityPlotView`, the **Time series** live-trace pair `monitorView` (`monitorPenPlot`/`monitorScalePlot`), *or* the Accumulator chart `accumPlotView`), with the `PenInputSurface` overlay on top. Chart visibility is driven entirely by the ribbon MODE dropdown — there are no separate centre tabs and no chart-type picker. |
| **Right** | 580 px (col 1) | The two panes (`panel_right_stability` for Curve and Time series, `panel_right_accumulator` for Accumulator) stack in the same cell, visibility-toggled by MODE. The stability captures pane is shared by both Curve and Time series. |

There are three top-level modes: **Curve**, **Time series**, and **Accumulator**
(the ribbon `comboBox_view_mode`, items `"Curve"` / `"Time series"` /
`"Accumulator"`, mapping to the internal tab keys via `SetActiveTab`, which also
derives `_captureTimeSeries` from the active mode). Manual mode, the standalone
Monitor mode, and the old multi-controller Threshold mode were removed; Monitor's
live time-series view is now the top-level **Time series** mode (previously a
chart-type within Curve, selected by the now-removed `comboBox_capture_chart`
picker). Many internals keep their pre-rename names — e.g. `StabilityController`,
`stabilityPlotView`, `panel_right_stability` shared by Curve and Time series, and
`monitorView`, `monitorPenPlot`/`monitorScalePlot`, the `_monitor*` buffers and
`RefreshMonitorPlots` for the Time series mode. In Time series mode each stability
capture is marked with a red dot on the live traces (the capture-marker buffers
in `MainWindow`).

No MVVM, no DI — the window owns all state. Session managers receive callbacks via constructor delegates; `StabilityController` exposes C# events. See [UI_MAP.md](UI_MAP.md) for every named control.

---

## PenInputSurface — key architectural invariant

> **`AvaloniaPointerSession` must be attached to `PenInputSurface` — a plain
> `Border` with `Background="Transparent"` and no interactive children.**

`PenInputSurface` is the topmost layer in the centre column's chart `Grid`, covering every `AvaPlot` control (the Curve scatter chart, the Time series trace pair, and the Accumulator chart). It has two roles:

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
| `PointerPressed` (right button) | `OnChartAreaPointerPressed` | Reset axes to the active chart's default range |

(Space-held pan was removed along with the keyboard hotkeys; only wheel-zoom and
right-click-reset remain.) `ActiveChart()` picks the target: `monitorPenPlot`
in Time series mode, `accumPlotView` for Accumulator, otherwise the Curve
`stabilityPlotView`.

Because `PenInputSurface` and the charts share the same `Grid` cell, pixel coordinates are interchangeable — the overlay translates clicks/wheels through `Plot.GetCoordinates` to data coordinates and `Plot.Axes.SetLimits` to apply.

The plots themselves have `UserInputProcessor.IsEnabled = true`, but `PenInputSurface` sits on top and absorbs pointer events before the plot's own handlers run — so navigation is effectively driven entirely by our overlay handlers.

---

## Layers

### Presentation
`MainWindow.axaml` + `MainWindow.axaml.cs` — all UI writes are on the Avalonia UI thread.

#### Theming (light / dark)
The app follows the OS theme (`RequestedThemeVariant="Default"`). Colours come from two places:

- **XAML** uses app-defined brushes declared in `App.axaml` under `ResourceDictionary.ThemeDictionaries` (`Light` / `Dark` keys): `RibbonBackgroundBrush`, `RibbonBorderBrush`, `DividerBrush`, `CardBackgroundBrush`, `PrimaryTextBrush`, `SecondaryTextBrush`, `TertiaryTextBrush`, `SubtleHoverBrush`, `CardDeleteBrush`. Everything references them via `{DynamicResource …}`, so they flip automatically. These are app-owned (not Fluent's built-in keys) to avoid depending on exact resource-key spellings.
- **ScottPlot charts** can't read XAML brushes, so [`ChartTheme.Apply`](../PenPressureProfiler/Controls/ChartTheme.cs) reads `Application.Current.ActualThemeVariant` and sets figure/data backgrounds + axis/grid colours in code. `MainWindow` subscribes to `ActualThemeVariantChanged` and calls `ReapplyChartThemes()` (colours only — axis limits preserved) so charts re-skin live when the theme flips. The scatter data colours (blue/orange/red/grey) are saturated enough to read on both themes and are left fixed.

The Fluent **accent colour** is pinned in `App.axaml` (`SystemAccentColor` + its `Light1–3` / `Dark1–3` shade ramp, all `#2563EB`-based) rather than inherited from the user's Windows accent — so accented controls (selection highlight, focus, slider fill, the About-dialog links) look identical on every machine and match the chart-data blue.

### Session managers
`PenSessionManager` owns the WinPenKit session, a `DispatcherTimer` (~60 fps), a `PenButtonTracker`, and a `MovingAverage` (200 samples). `Start(InputApi api)` handles both backend kinds:

- `AvaloniaPointer` → `new AvaloniaPointerSession(_penInputSurface)`
- All others → `PenSessionFactory.Create(api)` + HWND from `TopLevel.GetTopLevel(_penInputSurface).TryGetPlatformHandle()`

`ScaleSessionManager` **owns the open `SerialPort`** for the session: it opens
the port, reads serial lines via `Task.Run` on the threadpool, parses with
`ScaleLineParser`, and marshals each parsed `ScaleRecord` onto the UI thread via
`Dispatcher.UIThread.Post`. It also exposes `SendTare()`, which writes the bytes
`"T\r\n"` to the open port (safe to call from the UI thread while the read loop
runs — serial reads and writes are independent). Errors are surfaced through a
`Func<string, string, Task>` injected at construction.

### Curve (stability) logic
`StabilityController` (the "Curve" detector — user-facing rename only; the type,
field `_stabilityController`, and chart `stabilityPlotView` keep the
`stability*` names) is a pure in-memory component fed by the same
`OnPenDataReceived` and `OnScaleReading` callbacks that drive the live display.
It has no Avalonia dependency, but is **not** thread-safe — all calls must be on
the UI thread (the contract is met because both feeders are already
UI-thread-marshalled). See [stable capture logic](#stable-capture-logic) below.

### Accumulator logic
The Accumulator tab wraps one `AccumulatorController` — one chart, one panel. It
shares the same threading model and the same two feeders as `StabilityController`
(UI-thread-only, no Avalonia dependency). Instead of committing discrete capture
brackets, it **histograms physical force** and, per bucket, splits samples by a
per-target raw-pressure **threshold** `T`: a sample is **at-or-over** when
`T > 0 && raw ≥ T`, else **under**. The force where at-or-over overtakes under
(the per-bucket **%** column crossing ~50%) is read directly off the buckets; the
controller computes no estimate of its own.

The controller holds **two target states** (`AccumTarget.Iaf`, `.MaxPressure`),
each with its own range, bucket-width set, selected width and counts; only the
active `Target` accumulates. Thresholds: **IAF** `T = 1` (≡ pen > 0%; range default
**0–10 gf**, widths 1 / 0.5 / 0.25 / 0.2 / 0.1); **Max pressure** `T = MaxRawPressure`
(pen at 100%; range default **0–500 gf**, widths 50 / 25 / 10 / 5). `MaxRawPressure`
is fed from the pen session. Within the active target it maintains **all bucket
widths at once** (one layout each, sharing the range) and fans each lag-aligned
scale sample into every layout, incrementing that bucket's **Under** or
**AtOrOver** counter; samples below `min` / ≥ `max` go to the out-of-range counters.

`SetWidth` selects which layout backs the visible bucket arrays; because every
width is already accumulating, switching width **never loses data**. `Configure`
(range change) rebuilds and **clears the active target's layouts**. `SetTarget`
switches targets without touching either's data. `ExportLayouts(target)` /
`ImportLayouts(target, …)` round-trip every width's counts per target for save/load
(`AccumulatorSnapshotFile` v2 holds both targets).

The controller exposes:

- per-bucket `UnderCounts` / `AtOrOverCounts` arrays plus the scalar
  `BelowUnder` / `BelowAtOrOver` / `AboveUnder` / `AboveAtOrOver` out-of-range
  counts, and `BucketLowerGf` / `BucketCenterGf` for axis and table labelling.
  The chart draws the per-bucket markers, a fixed **50% reference line**, and a
  **live vertical force line** at the current scale reading; there is no fitted
  curve or estimate line. (A count-weighted logistic-fit estimate and a simpler
  crossover fallback were removed — in practice the fit picked the IAF poorly, so
  the user now reads the threshold off the per-bucket **%** column directly);
- `LastChanged` (`None` / `Below` / `Bucket` / `Above`) — which counter the most
  recent sample incremented, so the panel can live-highlight the affected cell;
- `IsAtOrOver(raw)` — classifies a raw pen pressure against the active target's
  threshold. Besides driving accumulation, `MainWindow.ApplyAccumulatorPressureTint`
  uses it each pen tick to tint the ribbon PEN/SCALE PRESSURE readouts (blue =
  under, purple = at-or-over) while Accumulator mode is active;
- `ClearBucket(i)` / `ClearBelow()` / `ClearAbove()` — zero one bucket's (or an
  out-of-range region's) counts in the **selected width layout**, for noise
  cleanup. Wired to right-click on a chart node (`TryDeleteAccumulatorNodeAt`,
  pixel hit-test against the markers) and right-click on a BUCKETS row
  (`accum_row_PointerPressed`). Per-width only: the other layouts bucketed the
  same samples differently and only store tallies, so a deletion can't be
  propagated across widths precisely.

The bucket counters accumulate continuously while capture is enabled; there is no
arming. An **optional pen-proximity gate** (`_accumRequireProximity`, toggled in
**Tools ▸ Options** via `AppOptions.AccumulatorRequirePenProximity`, **off** by
default) makes `MainWindow.OnScaleReading` feed a scale sample to the controller
only when `_penPresent` is true — so a pen lifted away (whose resting tablet
weight the scale still reports) doesn't pile up as "under" samples. With the gate
off (the default) every sample is recorded. The chart refreshes on every scale sample while the accumulator
chart is visible (whether or not accumulation is running), so the live force line
tracks even when nothing is being recorded.

### Scale-lag compensation
The scale lags the pen by a fixed response time τ. To align the fast pen stream
with the slow scale stream before feeding the accumulator, `MainWindow` queues
incoming pen events in `_penLagQueue` and releases each to the controller only
once it is older than τ (= `ScaleSessionManager.ResponseLagMs`, **245 ms**), so
the pen's under/at-or-over state is matched to the scale sample it actually produced.
The compensation is toggled in **Tools ▸ Options** (`OptionsWindow` → `AppOptions.ScaleLagComp`,
applied via `MainWindow.SetScaleLagComp`; on by default). τ itself is measured with
the **Measure Scale Lag** tool (`Views/MeasureScaleLagWindow`).

### Edit dialogs
- `StabilityEditWindow` — opened via `ShowDialog<List<StabilityCapture>?>(parent)`. Works on a local sorted copy of the captures; **Done** returns the survivors, **Cancel** returns null. Includes monotonic-violation detection (`ComputeViolators`) for UI highlighting.
- `MetadataEditWindow` — opened via `ShowDialog<SessionMetadata?>(parent)`. Edits a local copy of the session metadata; **Done** returns the edited `SessionMetadata`, **Cancel** (or `Esc`) returns null. `MainWindow` holds the canonical metadata as `_metadata` (a `SessionMetadata`) and only replaces it on a non-null result. A `requireAll` variant forces all mandatory fields before a save.
- `OptionsWindow` — opened via `ShowDialog<AppOptions?>(parent)` from **Tools ▸ Options**. Edits a copy of [`AppOptions`](../PenPressureProfiler/Model/AppOptions.cs); **Done** returns the edited options (applied by `MainWindow.SetScaleLagComp`), **Cancel**/`Esc` return null. Currently holds the Accumulator scale-lag toggle; the intended home for future app options.

### Data / files
`SessionMetadata`, `StabilitySnapshotFile`, `SessionLogger` are independent of UI.

---

## Key Classes

### PenSessionManager

Each timer tick:

1. `session.DrainPoints()` — all packets since last tick.
2. Each point fed into `PenButtonTracker` and `MovingAverage`.
3. Button-state transitions detected by comparing to last tick; MA cleared on **any** release.
4. One `PenReadingData` emitted (last point + MA average + packet count).
5. **No-packet ticks** preserve last pressure when `TipDown == true`. This avoids flicker with `AvaloniaPointerSession`, which only fires on `PointerMoved`.

### StabilityController

Two feeders + two events:

```csharp
void OnPenData(PenReadingData d);    // checks stability, possibly fires StableCaptured
void OnScaleData(double gf);          // updates window, fires RawPairAvailable

event Action<double, double> RawPairAvailable;    // (physGf, penNormAvg) — drives grey dots
event Action<StabilityCapture>   StableCaptured;      // either a new or re-confirmed capture
```

`OnPenData` ignores idle ticks (`PacketCount == 0`) so the window isn't disturbed by no-op frames. Captures are bounded by `MaxCaptures = 2000`.

#### Dedup count

When a new stable capture lands within (`ScaleTolerance`, `PenTolerance`) of an existing capture, the existing one's `Count` is incremented and `StableCaptured` is fired with the existing instance (not a new one). The right-panel ListBox shows this as `×N`. So adding a new capture only happens when the point is genuinely new under the current tolerances.

### StabilityEditWindow

Modal dialog over a local `List<StabilityCapture>`. Three view layers stay in sync:

- `_captures` (sorted by `PhysicalGf`) — the truth.
- `listBox_edit.ItemsSource` — `EditCaptureRow` view-models with `IsViolator` flag.
- `editPlotView` — three scatter series (clean / violator / selected), redrawn without resetting axis limits so user zoom persists.

`ComputeViolators` runs the captures in physical-force order and flags any whose `LogicalNorm` is strictly less than the running max — i.e. the curve dips backwards. Plateaus (equal values) are **not** flagged.

---

## Stable Capture Logic

`StabilityController` auto-records a `(physGf, logical-norm)` pair when both signals are steady.

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

### Recorded value

The detection above runs on the **raw normalized** pen window, but the value
actually stored as `LogicalNorm` is `d.SmoothedPressure` (the 200-sample moving
average) at the moment of capture — the same value the live crosshair shows and
that manual **Record** stores via `_logicalPressure`. So auto- and manual
captures are consistent and land exactly on the crosshair. (`physGf` is still the
scale-window average.)

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
| **UI thread** | All Avalonia controls; DispatcherTimer tick; `await` continuations; `StabilityController`; `SessionLogger` writes |
| **WinPenKit background** | Packet capture queue; `DrainPoints()` is the handoff point |
| **ThreadPool** | `SerialPort.ReadLine()` inside `Task.Run` |

`StabilityController` and `SessionLogger` rely on the UI-thread guarantee — they're not safe to call from elsewhere.

---

## External Dependencies

| Package | Role |
|---|---|
| `WinPenKit` v0.3.0 + `WinPenKit.Avalonia` | WinTab + Avalonia Pointer backends. Vendored at `libs/WinPenKit/v0.3.0/`. See [WINPENKIT.md](WINPENKIT.md). |
| `Avalonia` 11.3.x | UI framework. Mica via `TransparencyLevelHint` set in code (XAML parser cannot assign `IReadOnlyList<WindowTransparencyLevel>`). |
| `ScottPlot.Avalonia` 5.1.x | Curve (scatter + time-series) and Accumulator charts. Pointer input is captured by `PenInputSurface` first, so ScottPlot's own input processor is effectively bypassed. |
| `System.IO.Ports` | Scale serial reading. |

---

## File Formats

There is a single save/load format — the **stability snapshot** (Curve mode,
via `StabilitySnapshotFile`). The old manual-session JSON (`PressureTestFile`,
`[physical_gf, logical_percent]` record arrays) was removed with Manual mode.

### Stability snapshot JSON
```json
{
  "metadata": {
    "brand": "WACOM", "pen": "PRO PEN 3", "penfamily": "PRO",
    "inventoryid": "--P.0042", "date": "2026-05-22", "user": "SEVEN",
    "tablet": "PTH-860", "driver": "6.4.2", "os": "WINDOWS",
    "tags": "", "notes": ""
  },
  "captures": [{
    "count": 3,
    "physicalGf": 45.2, "logicalNorm": 0.235,
    "penSamples": [{ "timestamp": "…", "rawPressure": 8192,
                     "normalizedPressure": 0.25, "altitude": 82.4 }]
  }]
}
```
A `metadata` block (`SessionMetadata`) plus the captures. Each capture carries
the dedup `count`, the `(physicalGf, logicalNorm)` pair, and every raw `penSample`
inside its stability window (timestamp, raw, normalized, and `altitude` — degrees
from the tablet surface, 0–90 — kept for diagnostics though the live Curve chart
no longer uses it). `scaleSamples` are **no longer written** but are still read
back from older snapshots (`StabilitySnapshotCapture.ScaleSamples` is nullable,
omitted-when-null). Drag-dropping a `.json` onto the window loads a snapshot
(`OnDrop` → `JsonSerializer.DeserializeAsync<StabilitySnapshotFile>`); a loaded
snapshot's `metadata` replaces the in-memory `_metadata`.

### CSV logs (`Documents\PenPressureProfiler\Logs\`)
- `pen_YYYY-MM-DD_HHmmss.csv` — ~60 Hz stream of pen state. Columns: `Timestamp,
  RawPressure, NormalizedPressure, SmoothedPressure, Azimuth, Altitude, TiltX,
  TiltY, TipDown, Barrel1Down, Barrel2Down`.
- `scale_YYYY-MM-DD_HHmmss.csv` — one row per parsed serial reading (~8–10 Hz).
  Columns: `Timestamp, Force_gf, RawLine`. `Force_gf` is formatted to the
  device-reported decimal precision (`ScaleRecord.DecimalPlaces`, floored at 2
  dp in the log); `RawLine` is the verbatim, CSV-quoted serial line.

---

## What's not here yet

- **No PR CI** — only the release-tag workflow runs tests. Run `dotnet test` locally.
- **`StabilityController` has no clock injection** — uses `DateTime.UtcNow` directly, so it's not easily unit-testable. See [TESTING.md](TESTING.md) for what's worth refactoring.
- **`MainWindow` is a god class** — ~2000 LOC, no view-model split. Anything UI-related lives there.
