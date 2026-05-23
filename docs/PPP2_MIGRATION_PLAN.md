# PenPressureProfiler2 — Phased Migration Plan

## Motivation

`PenPressureProfiler` was built by adding Avalonia layers onto a WinForms/WPF codebase and has a persistent issue: `AvaloniaPointerSession` stops receiving `PointerType.Pen` events after switching away from WinTab. Root cause is that the session is attached to `rootGrid` (a complex container with interactive children) rather than a dedicated input surface.

`Scribble.Avalonia` works reliably because the session is attached to `CanvasArea` — a single `Border` that fills the main content area with no interactive children. PenPressureProfiler2 starts from that working foundation and adds profiling features back in, phase by phase, with a pen-input verification step after every phase.

---

## Architectural Principle

> **The pen input surface must be a dedicated `Border` (`PenInputSurface`) that fills the window's main content area and contains no interactive children.**

`AvaloniaPointerSession` is attached to `PenInputSurface`. All UI panels (readings, charts, buttons) live in a separate side column. The user positions the app window over the tablet area they're testing.

For the WinTab backend this doesn't matter (global hook). For Avalonia Pointer it is essential.

---

## Phase 0 — Clone and sanitise (baseline)

**Goal:** A compilable, runnable app that is just Scribble with the drawing removed and the namespace renamed.

**Steps:**
1. Copy `Scribble.Avalonia/` to `PenPressureProfiler2/`
2. Rename namespace → `PenPressureProfiler2`, assembly name → `PenPressureProfiler2`
3. Keep `CanvasArea` Border in AXAML but rename it `PenInputSurface`; remove the `DrawImage` Image child
4. Remove all Skia bitmap / drawing code from code-behind
5. Remove `SkiaSharp` package reference
6. Add `Avalonia.Themes.Fluent` styles (already present), set window title
7. Add to `PenPressureProfiler.slnx`

**Verification:** Launch → switch between WinTab and Avalonia Pointer → move/press pen → check title-bar telemetry (pressure/tilt) shows non-zero values for both APIs.

---

## Phase 1 — Pen data display

**Goal:** Replace the Scribble header ribbon with the PenPressureProfiler card-based readings UI. No new features — just display the data that is already flowing.

**What to add:**
- `LabeledReading` user control (from existing PPP)
- Left panel with cards: **Pressure** (raw, normalized, smoothed, rate), **Tilt** (azimuth, altitude, tiltX, tiltY), **Button State** (tip, barrel 1/2, eraser)
- `MovingAverage` class (200-sample window)
- `PenButtonTracker` (already in WinPenKit — use it)
- Pen rate counter (~1 s window)
- `PenSessionManager` (adapted from PPP — owns the `DispatcherTimer` + DrainPoints loop)
- API selector ComboBox (WinTab / Avalonia Pointer)
- Status dot (green/grey)

**`PenInputSurface` stays exactly as it is.** `PenSessionManager` is constructed with `PenInputSurface` as its element — not the window, not the root grid.

**Verification:** Same as Phase 0 — both APIs deliver live pressure + tilt readings.

---

## Phase 2 — Scale integration

**Goal:** Add serial-port scale reading (physical pressure in gf). Pen input must remain unaffected.

**What to add:**
- `ScaleSessionManager` (from PPP)
- `ScaleLineParser` (from PPP)
- Scale control card: COM port selector, Record/Stop toggle button
- Phys pressure reading + scale rate reading in the Pressure card

**Verification:** Scale data shows. Switch between WinTab / Avalonia Pointer — both still deliver pen data.

---

## Phase 3 — Session logging

**Goal:** CSV log files for pen and scale.

**What to add:**
- `SessionLogger` (from PPP)
- Logging card: enable/disable toggle, open-folder button
- Hotkey (e.g. `L`) to toggle logging
- Log written to `Documents/PenPressureProfiler/Logs/`

**Verification:** Record a session, inspect CSV. Both APIs still work.

---

## Phase 4 — Manual recording + pressure curve chart

**Goal:** The user can record (phys pressure, logical pressure) pairs and see them on a chart.

**What to add:**
- `PressureRecord` and `PressureRecordCollection` (from PPP)
- Metadata fields right panel: User, Date, Tablet, Driver, OS, Notes
- ScottPlot chart (`AvaPlot`) in the center column — this is a new child of the center column, **not** inside `PenInputSurface`
- Record button, delete selected, clear all
- Axis range selector

**`PenInputSurface` is shrunk / replaced by the chart in the center column once there is something to show there. The session must still be attached to a control that the pen can be over — see note below.**

> **Layout note:** At this point the center column becomes the chart. For Avalonia Pointer to work, the pen must be over the side panel (left column) or over the chart area. The chart `AvaPlot` is non-interactive (`UserInputProcessor.IsEnabled = false`), so it won't intercept events. Attach the session to the left-panel `StackPanel` if needed, or keep a thin `PenInputSurface` alongside the chart.

**Verification:** Record a few points, chart updates, delete works. Both APIs work.

---

## Phase 5 — JSON save / load + drag-drop

**Goal:** Persist and restore pressure curve data.

**What to add:**
- `PressureTestFile` JSON model (from PPP)
- Save / Load buttons
- Drag-drop JSON onto window (`DragDrop.AllowDrop`)
- Chart title update on load

**Verification:** Save → reload → chart matches. Drag-drop works. Both APIs still work.

---

## Phase 6 — Sweep mode

**Goal:** Automatic stable-capture detection: hold pen + scale steady → snapshot recorded automatically.

**What to add:**
- `SweepController` (from PPP — owns stability detection logic)
- Sweep config card: min stable ms, enable toggle
- Sweep chart (second `AvaPlot`) in a tabbed or split view
- Sweep data view (DataGrid of stable captures)
- Sweep snapshot JSON save / load

**Verification:** Connect scale, enable sweep, press steadily — captures record. Switch to Avalonia Pointer — sweep still works.

---

## Phase 7 — Polish + finalization

**Goal:** Match PPP feature parity and visual quality.

**What to add / adjust:**
- Mica / AcrylicBlur window transparency
- `WindowStartupLocation`, `MinWidth`, `MinHeight`
- Tooltips on API selector ComboBox
- `USERMANUAL.md` updates
- `ARCHITECTURE.md` update to document `PenInputSurface` pattern

**When passing all verification:** rename `PenPressureProfiler2` → `PenPressureProfiler` and archive the old project (or keep both in the solution briefly during transition).

---

## Pen-input verification checklist (run after every phase)

After each phase rebuild and run:

- [ ] WinTab selected from startup → press pen → pressure + tilt readings update
- [ ] Switch dropdown to Avalonia Pointer → press pen → pressure + tilt readings update
- [ ] Switch back to WinTab → pen still works
- [ ] No error dialogs on switch

If any checkbox fails, **stop and fix before proceeding** to the next phase.

---

## Files carried over from PenPressureProfiler (reference)

| File | Phase |
|---|---|
| `LabeledReading.axaml` / `.cs` | 1 |
| `MovingAverage.cs` | 1 |
| `PenReadingData.cs` | 1 |
| `PenSessionManager.cs` | 1 |
| `ScaleSessionManager.cs` | 2 |
| `ScaleLineParser.cs` | 2 |
| `ScaleRecord.cs` / `ScaleSample.cs` | 2 |
| `SessionLogger.cs` | 3 |
| `PressureRecord.cs` | 4 |
| `PressureRecordCollection.cs` | 4 |
| `PressureTestFile.cs` | 5 |
| `SweepController.cs` | 6 |
| `SweepSnapshot.cs` | 6 |
