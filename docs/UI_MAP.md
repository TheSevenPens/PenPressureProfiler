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
│ ┌───────────┬─────────────────┬────────────────────┬──────────────────────────┐ │
│ │ PEN       │ BUTTONS         │ PRESSURE           │ ORIENTATION              │ │
│ │ ProximityDot   TipDot   B1   RibbonRawLabel     RibbonAzLabel               │ │
│ │ ProximityLabel Barrel1Dot   RibbonNormLabel    RibbonAltLabel               │ │
│ │                Barrel2Dot   RibbonSmoothLabel  RibbonTxLabel  RibbonTyLabel │ │
│ └───────────┴─────────────────┴────────────────────┴──────────────────────────┘ │
├──────────────────┬──────────────────────────────────┬───────────────────────────┤
│ LEFT (310px)     │ CENTRE (*)                       │ RIGHT (340px)             │
│ ScrollViewer     │ Grid (chart tabs + chart area)   │ Grid (panel tabs + body)  │
│                  │                                  │                           │
│ ── Tablet ──     │                                  │ ┌─ Tab buttons ─────────┐ │
│ dot_pen          │                                  │ │ btn_right_recording   │ │
│ ApiCombo         │                                  │ │ btn_right_sweep       │ │
│                  │                                  │ └───────────────────────┘ │
│ ── Pressure ──   │ ┌─ Chart area Grid ──────────┐   │                           │
│ reading_pressure_│ │ plotView      (AvaPlot)    │   │ panel_right_recording     │
│   raw            │ │ sweepPlotView (AvaPlot)    │   │ ScrollViewer              │
│   norm           │ │ PenInputSurface (Border)   │   │ ┌─ Recording card ─────┐ │
│   smooth         │ │   transparent overlay,     │   │ │ Record / -Last /     │ │
│ reading_pen_rate │ │   intercepts wheel/move/   │   │ │ Clear All buttons    │ │
│ pressureBar      │ │   right-click + receives   │   │ │ txt_record_count     │ │
│ ─── separator ── │ │   AvaloniaPointerSession   │   │ │ comboBox_axis_range  │ │
│ reading_phys_    │ └────────────────────────────┘   │ │ listBox_records      │ │
│   pressure       │  (plot/sweep visibility is       │ │ Metadata… (opens     │ │
│ reading_scale_   │   driven by the right-panel      │ │   MetadataEditWindow)│ │
│   rate           │   Manual / Auto tab)             │ │ Save… / Load…        │ │
│                  │                                  │ │ txt_file_status      │ │
│ ── Scale ──      │                                  │ └──────────────────────┘ │
│ comboBox_comport │                                  │                           │
│ btn_scale_record │                                  │ panel_right_sweep         │
│ ── Logging ──    │                                  │ (IsVisible=False)         │
│ btn_log_toggle   │                                  │                           │
│ btn_open_log_    │                                  │                           │
│   folder         │                                  │                           │
│                  │                                  │ ┌─ Auto Detection ─────┐ │
│                  │                                  │ │ btn_sweep_enable     │ │
│                  │                                  │ │ reading_sweep_       │ │
│                  │                                  │ │   captures           │ │
│                  │                                  │ │ comboBox_sweep_      │ │
│                  │                                  │ │   axis_range         │ │
│                  │                                  │ │ Clear/Save…/Load…    │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Parameters ─────────┐ │
│                  │                                  │ │ slider_penTolerance  │ │
│                  │                                  │ │   label_penTolerance │ │
│                  │                                  │ │ slider_scaleTolerance│ │
│                  │                                  │ │   label_scaleTolerance│ │
│                  │                                  │ │ slider_stableDuration│ │
│                  │                                  │ │   label_stableDuration│ │
│                  │                                  │ │ slider_minGap        │ │
│                  │                                  │ │   label_minGap       │ │
│                  │                                  │ └──────────────────────┘ │
│                  │                                  │ ┌─ Captures ───────────┐ │
│                  │                                  │ │ btn_sweep_sort       │ │
│                  │                                  │ │ Edit… (no name)      │ │
│                  │                                  │ │ listBox_sweep_       │ │
│                  │                                  │ │   captures           │ │
│                  │                                  │ └──────────────────────┘ │
└──────────────────┴──────────────────────────────────┴───────────────────────────┘
```

The **Pressure card** groups all live values: pen-side (raw / norm / smooth / pen rate + visual bar) above the separator, scale-side (phys pressure / scale rate) below. The **Scale card** holds only the connection controls (COM port + Read button).

### Name → role

| `x:Name` | Type | Role |
|---|---|---|
| `ProximityDot` / `ProximityLabel` | Ellipse + TextBlock | Tip down / Proximity / Out indicator |
| `TipDot`, `Barrel1Dot`, `Barrel2Dot` | Ellipse | Live button-state dots |
| `RibbonRaw/Norm/Smooth/Az/Alt/Tx/TyLabel` | TextBlock | Live pen-state readouts in the ribbon |
| `dot_pen` | Ellipse | Session-running indicator next to the API picker |
| `ApiCombo` | ComboBox | Selects `InputApi` backend; change starts a new session |
| `reading_pressure_raw/norm/smooth` | LabeledReading | Pressure card live readings (pen-side, above separator) |
| `reading_pen_rate` | LabeledReading | Pen packets/s (above separator) |
| `pressureBar` | ProgressBar | Visual bar of `NormalizedPressure * 100` (above separator) |
| `reading_phys_pressure` | LabeledReading | Latest scale gf (below separator) |
| `reading_scale_rate` | LabeledReading | Scale readings/s (below separator) |
| `comboBox_comport` | ComboBox | Available `SerialPort.GetPortNames()` |
| `btn_scale_record` | Button | Toggle scale read; Ctrl+T |
| `btn_log_toggle` | Button | Toggle CSV logging; Ctrl+L / Ctrl+G |
| `btn_open_log_folder` | Button | Opens `Documents\PenPressureProfiler\Logs\` |
| `plotView` / `sweepPlotView` | `sp:AvaPlot` | Pressure and Sweep charts. Stacked in the same `Grid` cell; visibility is driven by the right-panel tab handlers (Manual → `plotView`, Auto → `sweepPlotView`). |
| `PenInputSurface` | Border | Transparent overlay — see [`ARCHITECTURE.md`](ARCHITECTURE.md#peninputsurface) |
| `btn_right_recording` / `btn_right_sweep` | Button (`tab-active` class) | Right-panel tab buttons — also toggle which chart is visible |
| `panel_right_recording` / `panel_right_sweep` | ScrollViewer | Right-panel contents (visibility-toggled) |
| **Metadata…** button | Button (no x:Name) | Opens [`MetadataEditWindow`](#metadataeditwindow); on Done, replaces `MainWindow._metadata` |
| `txt_record_count` / `txt_file_status` | TextBlock | Status text |
| `comboBox_axis_range` / `comboBox_sweep_axis_range` | ComboBox | Default / Full / IAF / IAF Large / Max |
| `listBox_records` | ListBox | Bound to `PressureRecordCollection.Items` |
| `btn_sweep_enable` | Button | Toggles `_sweepEnabled` (gates feeding the controller) |
| `reading_sweep_captures` | LabeledReading | Total capture count |
| `slider_*` + `label_*` | Slider + TextBlock | Stability params; OnSweepSliderChanged updates controller + label |
| `btn_sweep_sort` | Button | Toggles `_sweepSortAscending`, re-renders `UpdateSweepData` |
| `listBox_sweep_captures` | ListBox (Multiple) | Bound to `SweepCaptureRow` list |

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
