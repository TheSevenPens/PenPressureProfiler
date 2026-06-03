# UI Map

A labeled layout of every named control in `MainWindow.axaml` and `StabilityEditWindow.axaml`.
Each `x:Name` is the handle you'd reach for when changing or referencing UI from code.

For interaction behaviors (keyboard shortcuts, chart nav) see [USERMANUAL.md](USERMANUAL.md).
For wiring see [CONTROL_FLOW.md](CONTROL_FLOW.md).

---

## MainWindow

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│ RIBBON (DockPanel.Dock=Top)                                                     │
│ ┌─────┬─────────┬───────────────┬─────────────────────────────┐ │
│ │ PEN │ BUTTONS │ ORIENTATION   │ MODE                        │ │
│ │ …   │ …       │ Az / Alt /    │ comboBox_view_mode          │ │
│ │     │         │ TX / TY       │   (Manual / Stability / Threshold│ │
│ │     │         │               │    / Monitor)               │ │
│ └─────┴─────────┴───────────────┴─────────────────────────────┘ │
├──────────────────┬──────────────────────────────────┬───────────────────────────┤
│ LEFT (310px)     │ CENTRE (*)                       │ RIGHT (580px)             │
│ ScrollViewer     │ Grid (stacked AvaPlots)          │ Grid (stacked panels)     │
│                  │                                  │                           │
│ ── Pen ──        │ ┌─ Chart area Grid ──────────┐   │ panel_right_recording     │
│ reading_pressure_│ │ plotView      (AvaPlot)    │   │ ScrollViewer              │
│   raw            │ │ stabilityPlotView (AvaPlot)    │   │ ┌─ Manual captures     │ │
│   norm           │ │ threshPlotView (AvaPlot)   │   │ │   header  [↑ Force]  │ │
│   smooth         │ │ PenInputSurface (Border)   │   │ │           [Metadata…]│ │
│ reading_pen_rate │ │   transparent overlay,     │   │ │ [Record]             │ │
│ pressureBar      │ │   intercepts wheel/move/   │   │ │ txt_record_count     │ │
│                  │ │   right-click + receives   │   │ │ listBox_records      │ │
│ ── Scale ──      │ │   AvaloniaPointerSession   │   │ │ [Clear All] [Save…]  │ │
│ reading_phys_    │ └────────────────────────────┘   │ │            [Load…]   │ │
│   pressure       │  (plot visibility is driven by   │ │ (cards + footer)     │ │
│ reading_scale_   │   the ribbon MODE selector)      │ └──────────────────────┘ │
│   rate           │                                  │                           │
│                  │                                  │ panel_right_stability         │
│ ── Device       ─│                                  │ (IsVisible=False)         │
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
│                  │                                  │ ┌─ Stability Detection ┐ │
│                  │                                  │ │ btn_stability_enable     │ │
│                  │                                  │ │ comboBox_sweep_      │ │
│                  │                                  │ │   axis_range         │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Stability Params ▸ ─┐ │
│                  │                                  │ │ (Expander, collapsed │ │
│                  │                                  │ │  by default — when   │ │
│                  │                                  │ │  expanded, shows the │ │
│                  │                                  │ │  4 slider rows:      │ │
│                  │                                  │ │   pen / scale tol,   │ │
│                  │                                  │ │   stable / min gap)  │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Stability captures  │ │
│                  │                                  │ │   header  [↑ Force]  │ │
│                  │                                  │ │           [Edit…]    │ │
│                  │                                  │ │ reading_stability_unique │ │
│                  │                                  │ │ reading_stability_total  │ │
│                  │                                  │ │ listBox_stability_       │ │
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
| `btn_scale_record` | Button | Toggle scale read (Device Inputs → Scale row) |
| `dot_log` | Ellipse | Logging indicator — green when active, gray when idle (Device Inputs → Logging row) |
| `btn_log_toggle` | Button | Toggle CSV logging (Device Inputs → Logging row) |
| `btn_open_log_folder` | Button | Opens `Documents\PenPressureProfiler\Logs\` (Device Inputs → Logging row) |
| `plotView` / `stabilityPlotView` / `threshPlotView` | `sp:AvaPlot` | Pressure, Stability, and Threshold charts. Stacked in the same `Grid` cell; visibility is driven by the ribbon MODE selector via `SetActiveTab()` (Manual → `plotView`, Stability → `stabilityPlotView`, Threshold → `threshPlotView`). |
| `monitorView` / `monitorPenPlot` / `monitorScalePlot` | Grid + 2× `sp:AvaPlot` | Monitor view — a 2-row Grid containing two stacked live charts (pen normalized on top, scale gf on bottom). 10-second rolling window, ~20 fps refresh. Pan/zoom disabled (`UserInputProcessor.IsEnabled = false`); right-click resets to the rolling window. |
| `PenInputSurface` | Border | Transparent overlay — see [`ARCHITECTURE.md`](ARCHITECTURE.md#peninputsurface) |
| `comboBox_view_mode` | ComboBox | Mode picker in the ribbon's **MODE** group — selects which right-panel and centre chart are visible (Manual / Stability / Threshold / Monitor). Replaced the 4 tab buttons. |
| `btn_about` | Button | Ribbon **HELP** group — opens the modal `AboutWindow` (version + GitHub repo / README links) |
| `panel_right_recording` / `panel_right_stability` / `panel_right_threshold` / `panel_right_monitor` | DockPanel | Right-panel contents (visibility-toggled). Non-capture cards dock to the top; the `CaptureListSection` fills the remaining height so its list grows with the window. |
| `CaptureListSection` (`section_manual` / `section_threshold` / unnamed Stability) | Templated control | Shared capture-card layout: **title → actions (buttons) → meta (counts) → list**. The list (`Body`) takes all remaining vertical space. Same shape across Manual / Stability / Threshold. |
| `check_monitor_overlay` | CheckBox | Off: split into two stacked charts (default). On: pen + scale overlaid on a single chart with dual y-axes (pen left 0–1, scale right gf). Toggling RowSpans the pen plot across both rows and hides `monitorScalePlot` |
| `btn_monitor_clear` | Button | Resets the Monitor traces (clears the buffers and the epoch) |
| **Metadata…** button | Button (no x:Name) | Opens [`MetadataEditWindow`](#metadataeditwindow); on Done, replaces `MainWindow._metadata` |
| `txt_record_count` | TextBlock | "N records" count above the list |
| `listBox_records` | ListBox | One card per `PressureRecord` (`ManualRecordCard` view-model): `#N`, Physical gf, Logical %, ✕ delete. Cards are sorted by the toggle on the header; the source index on each card maps back to the underlying `PressureRecordCollection` regardless of sort |
| `btn_stability_enable` | Button | Toggles `_stabilityEnabled` (gates feeding the controller) |
| `reading_stability_unique` | LabeledReading | Distinct capture count (after dedup); caption "Unique:" |
| `reading_stability_total` | LabeledReading | Total confirmations including duplicates (`Σ Count`); caption "Total:" |
| `slider_*` + `label_*` | Slider + TextBlock | Stability params; OnStabilitySliderChanged updates controller + label |
| `btn_manual_sort` | Button | Toggles `_manualSortAscending` (display order of `listBox_records` only); calls `UpdateChart` |
| `btn_stability_sort` | Button | Toggles `_sweepSortAscending`, re-renders `UpdateStabilityData` |
| `listBox_stability_captures` | ListBox | One card per `StabilityCapture` (`StabilityCaptureCard` view-model): `#N`, Physical gf, Logical %, `×Count`, ✕ delete. The Edit… dialog still offers richer review (chart selection, monotonic-violation highlighting); ✕ here is a quick single-row drop |
| `comboBox_threshold_mode` | ComboBox | Sub-mode picker: "IAF from above" / "IAF from below" / "MAX from below". Switching stops any active capture; each sub-mode's estimates persist independently |
| `panel_threshold_armed` / `dot_threshold_armed` / `txt_threshold_armed` | StackPanel + Ellipse + TextBlock | Armed-status indicator (shown in all three sub-modes). Green when the active controller is ready to record its next estimate; gray otherwise. Label text is mode-dependent — describes what the user needs to do next |
| `txt_threshold_help` | TextBlock | Mode-dependent instructions shown above the Start button |
| `btn_threshold_enable` | Button | Toggles `_thresholdEnabled` (gates feeding the currently-selected controller). Label is "Start" / "Stop" |
| `reading_threshold_count` / `reading_threshold_median` | LabeledReading | "N / 10" and median in gf (or "—") for the active mode. The card header is the static "Captures" (matching Manual/Auto) |
| `listBox_threshold_estimates` | ListBox | One card per estimate (`ThresholdEstimateCard` view-model): `#N`, Physical gf, Raw (driver pressure integer at the boundary — 0 for IAF, `PenSessionManager.MaxPressure` for MAX), Logical % (0% for IAF, 100% for MAX), plus a `card-delete`-classed ✕ button. Rendered via an inline `DataTemplate`. Deleting via the per-card ✕ renumbers the remaining cards automatically (cards are rebuilt from the controller list every refresh) |
| `btn_threshold_remove_last` / `btn_threshold_clear` | Button | Drop last / wipe all for the active mode only |

---

## StabilityEditWindow

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
│   - Ctrl+click to toggle selection           │    (multi-select via     │
│   - Wheel zoom / RMB-reset                   │     button or click)     │
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
