# UI Map

A labeled layout of every named control in `MainWindow.axaml` and `SweepEditWindow.axaml`.
Each `x:Name` is the handle you'd reach for when changing or referencing UI from code.

For interaction behaviors (keyboard shortcuts, chart nav) see [USERMANUAL.md](USERMANUAL.md).
For wiring see [CONTROL_FLOW.md](CONTROL_FLOW.md).

---

## MainWindow

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ RIBBON (DockPanel.Dock=Top)                                                     │
│ ┌───────────┬─────────────────┬─────────────────────────────────────────────┐   │
│ │ PEN       │ BUTTONS         │ ORIENTATION                                 │   │
│ │ ProximityDot   TipDot   B1   RibbonAzLabel  RibbonAltLabel                │   │
│ │ ProximityLabel Barrel1Dot   RibbonTxLabel  RibbonTyLabel                  │   │
│ │                Barrel2Dot                                                  │   │
│ └───────────┴─────────────────┴─────────────────────────────────────────────┘   │
├──────────────────┬──────────────────────────────────┬───────────────────────────┤
│ LEFT (310px)     │ CENTRE (*)                       │ RIGHT (580px) — 4 tabs    │
│ ScrollViewer     │ Grid (chart tabs + chart area)   │ Grid (panel tabs + body)  │
│                  │                                  │                           │
│                  │                                  │ ┌─ Tab buttons ─────────┐ │
│                  │                                  │ │ btn_right_recording   │ │
│                  │                                  │ │ btn_right_sweep       │ │
│                  │                                  │ │ btn_right_iaf         │ │
│                  │                                  │ └───────────────────────┘ │
│ ── Pen ──        │ ┌─ Chart area Grid ──────────┐   │                           │
│ reading_pressure_│ │ plotView      (AvaPlot)    │   │ panel_right_recording     │
│   raw            │ │ sweepPlotView (AvaPlot)    │   │ ScrollViewer              │
│   norm           │ │ PenInputSurface (Border)   │   │ ┌─ Manual captures     │ │
│   smooth         │ │   transparent overlay,     │   │ │   header  [↑ Force]  │ │
│ reading_pen_rate │ │   intercepts wheel/move/   │   │ │           [Metadata…]│ │
│ pressureBar      │ │   right-click + receives   │   │ │ [Record] [− Last]    │ │
│                  │ │   AvaloniaPointerSession   │   │ │ txt_record_count     │ │
│ ── Scale ──      │ └────────────────────────────┘   │ │ listBox_records      │ │
│ reading_phys_    │  (plot/sweep visibility is       │ │ [Clear All] [Save…]  │ │
│   pressure       │   driven by the right-panel      │ │            [Load…]   │ │
│ reading_scale_   │   Manual / Auto tab)             │ │ txt_file_status      │ │
│   rate           │                                  │ └──────────────────────┘ │
│ ── Chart ──      │                                  │                           │
│ Axis range:      │                                  │ panel_right_sweep         │
│  comboBox_chart_ │                                  │ (IsVisible=False)         │
│  axis            │                                  │                           │
│                  │                                  │                           │
│ ── Device       ─│                                  │                           │
│    Inputs ──     │                                  │                           │
│ Tablet:  dot_pen │                                  │ (IsVisible=False)         │
│          ApiCombo│                                  │                           │
│ Scale:   dot_    │                                  │                           │
│          scale   │                                  │                           │
│          comboBox│                                  │                           │
│          _comport│                                  │                           │
│          btn_    │                                  │                           │
│          scale_  │                                  │                           │
│          record  │                                  │                           │
│ Logging: dot_log │                                  │                           │
│          btn_log_│                                  │                           │
│          toggle  │                                  │                           │
│          📁 btn  │                                  │                           │
│                  │                                  │ ┌─ Auto Detection ─────┐ │
│                  │                                  │ │ btn_sweep_enable     │ │
│                  │                                  │ │ comboBox_sweep_      │ │
│                  │                                  │ │   axis_range         │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Auto Parameters ▸ ─┐ │
│                  │                                  │ │ (Expander, collapsed │ │
│                  │                                  │ │  by default — when   │ │
│                  │                                  │ │  expanded, shows the │ │
│                  │                                  │ │  4 slider rows:      │ │
│                  │                                  │ │   pen / scale tol,   │ │
│                  │                                  │ │   stable / min gap)  │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Auto captures       │ │
│                  │                                  │ │   header  [↑ Force]  │ │
│                  │                                  │ │           [Edit…]    │ │
│                  │                                  │ │ reading_sweep_unique │ │
│                  │                                  │ │ reading_sweep_total  │ │
│                  │                                  │ │ listBox_sweep_       │ │
│                  │                                  │ │   captures           │ │
│                  │                                  │ │ [Clear All] [Save…]  │ │
│                  │                                  │ │            [Load…]   │ │
│                  │                                  │ └──────────────────────┘ │
└──────────────────┴──────────────────────────────────┴───────────────────────────┘
```

The left panel stacks three cards: **Pen** (live pen readings + visual pressure bar), **Scale** (live scale readings), and **Device Inputs** (the connection + logging controls — Tablet / Scale / Logging rows, each with a status dot).

### Name → role

| `x:Name` | Type | Role |
|---|---|---|
| `ProximityDot` / `ProximityLabel` | Ellipse + TextBlock | Tip down / Proximity / Out indicator |
| `TipDot`, `Barrel1Dot`, `Barrel2Dot` | Ellipse | Live button-state dots |
| `RibbonAz/Alt/Tx/TyLabel` | TextBlock | Live orientation readouts in the ribbon |
| `dot_pen` | Ellipse | Session-running indicator (Device Inputs → Tablet row) |
| `ApiCombo` | ComboBox | Selects `InputApi` backend; change starts a new session (Device Inputs → Tablet row) |
| `reading_pressure_raw/norm/smooth` | LabeledReading | Pressure card live readings (pen-side, above separator) |
| `reading_pen_rate` | LabeledReading | Pen packets/s (above separator) |
| `pressureBar` | ProgressBar | Visual bar of `NormalizedPressure * 100` (above separator) |
| `reading_phys_pressure` | LabeledReading | Latest scale gf (below separator) |
| `reading_scale_rate` | LabeledReading | Scale readings/s (below separator) |
| `dot_scale` | Ellipse | Scale tri-state indicator — red = no port / error, yellow = idle, green = reading |
| `comboBox_comport` | ComboBox | Available `SerialPort.GetPortNames()` (Device Inputs → Scale row) |
| `btn_scale_record` | Button | Toggle scale read; Ctrl+T (Device Inputs → Scale row) |
| `dot_log` | Ellipse | Logging indicator — green when active, gray when idle (Device Inputs → Logging row) |
| `btn_log_toggle` | Button | Toggle CSV logging; Ctrl+L / Ctrl+G (Device Inputs → Logging row) |
| `btn_open_log_folder` | Button | Opens `Documents\PenPressureProfiler\Logs\` (Device Inputs → Logging row) |
| `plotView` / `sweepPlotView` / `iafPlotView` / `maxPlotView` | `sp:AvaPlot` | Pressure, Sweep, IAF and MAX charts. Stacked in the same `Grid` cell; visibility is driven by the right-panel tab handlers via `SetActiveTab()` (Manual → `plotView`, Auto → `sweepPlotView`, IAF → `iafPlotView`, MAX → `maxPlotView`). |
| `PenInputSurface` | Border | Transparent overlay — see [`ARCHITECTURE.md`](ARCHITECTURE.md#peninputsurface) |
| `btn_right_recording` / `btn_right_sweep` / `btn_right_iaf` / `btn_right_max` | Button (`tab-active` class) | Right-panel tab buttons — also toggle which chart is visible |
| `panel_right_recording` / `panel_right_sweep` / `panel_right_iaf` / `panel_right_max` | ScrollViewer | Right-panel contents (visibility-toggled) |
| **Metadata…** button | Button (no x:Name) | Opens [`MetadataEditWindow`](#metadataeditwindow); on Done, replaces `MainWindow._metadata` |
| `txt_record_count` / `txt_file_status` | TextBlock | Status text |
| `comboBox_chart_axis` | ComboBox | Default / Full / IAF / IAF Large / Max — applies to whichever chart is currently visible (left panel → Chart card) |
| `listBox_records` | ListBox | Bound to `PressureRecordCollection.Items` |
| `btn_sweep_enable` | Button | Toggles `_sweepEnabled` (gates feeding the controller) |
| `check_altitude_color` | CheckBox | Toggles per-dot altitude coloring on the Sweep chart (90° → black, 60° → cornflower blue) |
| `reading_sweep_unique` | LabeledReading | Distinct capture count (after dedup); caption "Unique:" |
| `reading_sweep_total` | LabeledReading | Total confirmations including duplicates (`Σ Count`); caption "Total:" |
| `slider_*` + `label_*` | Slider + TextBlock | Stability params; OnSweepSliderChanged updates controller + label |
| `btn_manual_sort` | Button | Toggles `_manualSortAscending` (display order of `listBox_records` only); calls `UpdateChart` |
| `btn_sweep_sort` | Button | Toggles `_sweepSortAscending`, re-renders `UpdateSweepData` |
| `listBox_sweep_captures` | ListBox (Multiple) | Bound to `SweepCaptureRow` list |
| `btn_iaf_enable` | Button | Toggles `_iafEnabled` (gates feeding `IafController`). Re-starts after the 10th estimate clears prior results. |
| `reading_iaf_count` | LabeledReading | "N / 10" progress |
| `reading_iaf_median` | LabeledReading | Median IAF in gf, or "—" |
| `listBox_iaf_estimates` | ListBox | One row per estimate: `#NN  IAF: xx.xx gf   peak: yyy.y gf   bracket: raw R@Agf → 0@Bgf` — the two pen samples that bracket the zero crossing |
| `btn_iaf_remove_last` | Button | Drops the most recent estimate (frees a slot to re-capture) |
| `btn_iaf_clear` | Button | Wipes estimates and resets controller |
| `txt_iaf_status` | TextBlock | Status line (armed / progress / done / rejected sweep) |
| `btn_max_enable` | Button | Toggles `_maxEnabled` (gates feeding `MaxController`) |
| `reading_max_count` | LabeledReading | "N / 10" progress |
| `reading_max_median` | LabeledReading | Median MAX in gf, or "—" |
| `listBox_max_estimates` | ListBox | `#NN  MAX: xx.xx gf   start: yy.y gf   bracket: norm L%@AAgf → 100%@BBgf` |
| `btn_max_remove_last` / `btn_max_clear` | Button | Drop last / wipe all |
| `txt_max_status` | TextBlock | Status line for the MAX flow |

---

## SweepEditWindow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ txt_status   "N captures | M ⚠ non-monotonic — select items to delete"  │
├──────────────────────────────────────────────┬──────────────────────────┤
│                                              │                          │
│   editPlotView (AvaPlot)                     │  listBox_edit            │
│   - Blue dots  = clean captures              │  - Multiple selection    │
│   - Orange     = monotonic violators         │  - Orange row bg for     │
│   - Red ◆      = currently selected          │    violators             │
│                                              │  - Right-click row =     │
│   - Click within 15px of a dot to select     │    delete immediately    │
│   - Ctrl+click to toggle selection           │  - Delete key = delete   │
│   - Wheel / Space+drag / RMB-reset           │    selected              │
│                                              │                          │
├──────────────────────────────────────────────┴──────────────────────────┤
│ btn_delete_selected ("Delete Selected (N)")  Done   Cancel              │
└─────────────────────────────────────────────────────────────────────────┘
```

Closes with `Close(_captures)` on Done (returns survivors) or `Close(null)` on Cancel.

---

## MetadataEditWindow

```
┌──────────────────────────────────────────────────┐
│                                                  │
│   ScrollViewer                                   │
│     Brand:        [ field_brand        ]         │
│     Pen:          [ field_pen          ]         │
│     Pen family:   [ field_penfamily    ]         │
│     Inventory ID: [ field_inventoryid  ]         │
│     Date:         [ field_date         ]         │
│     User:         [ field_user         ]         │
│     Tablet:       [ field_tablet       ]         │
│     Driver:       [ field_driver       ]         │
│     OS:           [ field_os           ]         │
│     Tags:         [ field_tags         ]         │
│     Notes:        [ textBox_notes      ]         │
│                                                  │
├──────────────────────────────────────────────────┤
│                                Done    Cancel    │
└──────────────────────────────────────────────────┘
```

Done returns a new `PressureTestFile` (with metadata only — Records is left empty; `MainWindow.BuildTestFile` recombines metadata + `_recordCollection` at save time). Cancel and `Esc` return null.
