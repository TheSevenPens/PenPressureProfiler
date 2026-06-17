# UI Map

A labeled layout of every named control in `MainWindow.axaml` and `StabilityEditWindow.axaml`.
Each `x:Name` is the handle you'd reach for when changing or referencing UI from code.

For interaction behaviors (keyboard shortcuts, chart nav) see [USERMANUAL.md](USERMANUAL.md).
For wiring see [CONTROL_FLOW.md](CONTROL_FLOW.md).

---

## MainWindow

```
┌───────────────────────────────────────────────────────────────────────────────────┐
│ MENU (DockPanel.Dock=Top):  Edit → Metadata… │ Help → About                         │
├───────────────────────────────────────────────────────────────────────────────────┤
│ RIBBON (DockPanel.Dock=Top — StackPanel of controls:RibbonGroup, left→right)        │
│ ┌─────────┬─────┬──────────────┬────────────────┬──────┬───────────────┬──────────┐ │
│ │ DEVICES │ PEN │ PEN PRESSURE │ SCALE PRESSURE │ MODE │ AUTO-CAPTURE   │ THRESHOLD│ │
│ │ Tablet  │ …   │ raw/smooth/  │ phys pressure  │ view │ (Curve +       │ ACCUMU-  │ │
│ │ Scale   │     │ rate/norm +  │ scale rate     │ mode │  Time series)  │ LATOR    │ │
│ │ Logging │     │ pressureBar  │                │ +    │ Start/Edit…/   │ (Accum   │ │
│ │         │     │              │                │ follow│ summary       │ only)    │ │
│ └─────────┴─────┴──────────────┴────────────────┴──────┴───────────────┴──────────┘ │
├──────────────────────────────────────────────────┬────────────────────────────────┤
│ CENTRE (Grid.Column 0, *)                         │ RIGHT (Grid.Column 1, 580px)   │
│ Grid (overlapping AvaPlots + overlay)             │ Grid (overlapping DockPanels)  │
│                                                   │                                │
│ ┌─ stabilityPlotView (AvaPlot, Curve scatter) ─┐  │ panel_right_stability (Curve)  │
│ │ accumPlotView     (AvaPlot, Accumulator)     │  │ ┌─ CaptureListSection ──────┐  │
│ │ monitorView (Grid, Time series mode):        │  │ │ Actions:                  │  │
│ │   monitorPenPlot   (AvaPlot, row 0)          │  │ │  [Record][↑↓ sort][Edit…] │  │
│ │   monitorScalePlot (AvaPlot, row 1)          │  │ │  [Clear All][Save…][Load…]│  │
│ │ PenInputSurface (Border, transparent overlay,│  │ │ Meta: reading_stability_  │  │
│ │   always on top, AvaloniaPointerSession)     │  │ │       unique              │  │
│ └──────────────────────────────────────────────┘  │ │ Body: listBox_stability_  │  │
│  (chart visibility driven by ribbon MODE          │ │       captures            │  │
│   via SetActiveTab())                              │ └───────────────────────────┘  │
│                                                   │                                │
│                                                   │ panel_right_accumulator (Accum)│
│                                                   │ ┌ reading_accum_samples /   ┐  │
│                                                   │ │ reading_accum_iaf +       │  │
│                                                   │ │ txt_accum_status          │  │
│                                                   │ │ ┌─ CaptureListSection ──┐ │  │
│                                                   │ │ │ "BUCKETS"             │ │  │
│                                                   │ │ │ Body: listBox_accum_  │ │  │
│                                                   │ │ │       table           │ │  │
│                                                   │ │ └───────────────────────┘ │  │
│                                                   │ └───────────────────────────┘  │
└──────────────────────────────────────────────────┴────────────────────────────────┘
```

There is **no left panel** anymore. All live readouts and connection/logging
controls live in the **ribbon** (a `StackPanel` of `controls:RibbonGroup`), and the
former HELP ribbon group has been folded into the top **Menu** bar. The window has
two working areas: the **centre charts** and the **right captures pane**, both driven
by the ribbon **MODE** dropdown. The right pane is flat (no card, no "CAPTURES"
heading wrapper) — it holds two overlapping `DockPanel`s, one per mode, each a single
`CaptureListSection`.

### Menu bar

| Menu | Item | Handler | Role |
|---|---|---|---|
| **Edit** | **Metadata…** | `btn_edit_metadata_Click` | Opens [`MetadataEditWindow`](#metadataeditwindow); on Done, replaces `MainWindow._metadata` |
| **Help** | **About** | `btn_about_Click` | Opens the modal `AboutWindow` (version + GitHub repo / README links). Moved here from the old HELP ribbon group. |

### Ribbon → role

| `x:Name` | Type | Role |
|---|---|---|
| `row_tablet` | StatusDotRow | DEVICES → Tablet row; status dot + label, holds `ApiCombo` |
| `ApiCombo` | ComboBox | Selects `InputApi` backend (WinTab / Avalonia Pointer); change starts a new session |
| `row_scale` | StatusDotRow | DEVICES → Scale row; tri-state dot (red = no port / error, yellow = idle, green = reading) |
| `comboBox_comport` | ComboBox | Available `SerialPort.GetPortNames()` (Scale row) |
| `btn_scale_record` | Button | Toggle scale read (Scale row); label "Start" / "Stop" |
| `btn_scale_tare` | Button | Tare/zero the scale (Scale row). **`IsVisible=False`** — hidden; the current scale ignores the `T\r\n` command, wiring kept for later |
| `row_logging` | StatusDotRow | DEVICES → Logging row; dot green when CSV logging active |
| `btn_log_toggle` | Button | Toggle CSV logging (Logging row); label "Start Logging" / "Stop Logging" |
| *(folder button, no x:Name)* | Button (📁) | `btn_open_log_folder_Click` — opens `Documents\PenPressureProfiler\Logs\` (Logging row) |
| `ProximityDot` / `ProximityLabel` | Ellipse + TextBlock | PEN group — Tip down / Proximity / Out indicator |
| `TipDot`, `Barrel1Dot`, `Barrel2Dot` | Ellipse | PEN group — live button-state dots (Tip / B1 / B2) |
| `RibbonAzLabel` / `RibbonAltLabel` / `RibbonTxLabel` / `RibbonTyLabel` | TextBlock | PEN group — live orientation readouts (Az / Alt / TX / TY) |
| `reading_pressure_raw` / `reading_pressure_smooth` / `reading_pen_rate` | LabeledReading | PEN PRESSURE group — raw driver integer / smoothed (moving avg) / pen packets/s |
| `reading_pressure_norm` | LabeledReading | PEN PRESSURE group — normalized 0–100% |
| `pressureBar` | ProgressBar | PEN PRESSURE group — visual bar of `NormalizedPressure * 100` |
| `reading_phys_pressure` | LabeledReading | SCALE PRESSURE group — latest scale gf |
| `reading_scale_rate` | LabeledReading | SCALE PRESSURE group — scale readings/s |
| `comboBox_view_mode` | ComboBox | MODE group — mode picker (**Curve** / **Time series** / **Accumulator**); selects which centre chart + right panel are visible via `SetActiveTab()` |
| `group_view_follow` | StackPanel | MODE group — second row, visible in both Curve and Time series modes; holds `chk_live_follow` (Curve) and `chk_capture_overlay` (Time series) |
| `chk_live_follow` | CheckBox | MODE group — "Follow live": auto zoom/pan to keep the last ~1 s of live points in view (shown in Curve mode) |
| `chk_capture_overlay` | CheckBox | MODE group — "Overlay traces": dual-y-axis single chart (on) vs two stacked charts (off) for Time series (shown in Time series mode) |
| `group_curve_capture` | RibbonGroup | **AUTO-CAPTURE** — `IsVisible=False`, shown in both Curve and Time series modes |
| `btn_stability_enable` | Button | Curve auto-capture toggle (gates feeding the stability controller); label "Start" / "Stop" |
| *(Edit… button, no x:Name)* | Button + `Button.Flyout` | Opens a flyout of stability detection parameters (below) |
| `comboBox_tolerancePreset` | ComboBox | Flyout — tolerance preset (LOW / MEDIUM / HIGH); sets pen + scale tolerances together |
| `slider_penTolerance` / `slider_scaleTolerance` / `slider_stableDuration` / `slider_minGap` | Slider | Flyout — stability params; `OnStabilitySliderChanged` updates controller + label |
| `label_penTolerance` / `label_scaleTolerance` / `label_stableDuration` / `label_minGap` | TextBlock | Flyout — current value of each slider |
| `txt_curve_settings` | TextBlock | One-line summary of the current curve auto-capture settings |
| `group_accumulator` | RibbonGroup | **ACCUMULATOR** — `IsVisible=False`, shown only in Accumulator mode |
| `numeric_accum_min` / `numeric_accum_max` | NumericUpDown | Force range (gf) over which buckets are accumulated; defaults 0 / 10 |
| `comboBox_accum_bucket` | ComboBox | Bucket width in gf (1 / 0.5 / 0.25 / 0.1); default 0.5 |
| `chk_accum_scale_lag` | CheckBox | "Apply scale-lag comp (245 ms)" — compensates for scale latency when binning samples |
| `btn_accumulator_enable` | Button | Accumulator toggle (gates feeding the accumulator); label "Start" / "Stop" |
| `btn_accumulator_clear` | Button | "Clear" — wipes all accumulated bucket data |

### Centre + right-pane → role

| `x:Name` | Type | Role |
|---|---|---|
| `stabilityPlotView` | `sp:AvaPlot` | Curve scatter chart (shown in Curve mode). Top of the overlap stack; default-visible |
| `accumPlotView` | `sp:AvaPlot` | Accumulator chart (shown in Accumulator mode). `IsVisible=False` until Accumulator mode. Draws **only** activation-% markers (sized by sample count) + a dotted 50% reference line; X = force gf, Y = pen-on %. The logistic fit still computes the Est. IAF readout but is no longer drawn (no fit curve, no dashed IAF line) |
| `monitorView` / `monitorPenPlot` / `monitorScalePlot` | Grid + 2× `sp:AvaPlot` | Time series view (shown in Time series mode) — a 2-row Grid of two stacked live charts (pen normalized on top, scale gf on bottom). `IsVisible=False` until Time series mode. 10-second rolling window; pan/zoom disabled, right-click resets to the rolling window. Stability captures are marked with red dots on the traces |
| `PenInputSurface` | Border | Transparent overlay, always on top; `AvaloniaPointerSession` attaches here. Must stay a plain Border with no interactive children — see [`ARCHITECTURE.md`](ARCHITECTURE.md#peninputsurface) |
| `panel_right_stability` | DockPanel | Right pane — stability captures (shared by Curve and Time series modes; default-visible). Holds one `CaptureListSection` |
| `panel_right_accumulator` | DockPanel | Right pane — Accumulator (`IsVisible=False` until Accumulator mode). Holds the accumulator readouts, `txt_accum_status`, and the "BUCKETS" `CaptureListSection` |
| `CaptureListSection` (unnamed Curve / unnamed "BUCKETS") | Templated control | Shared capture layout: **actions (buttons) → meta (counts) → body (list)**. The `Body` list takes all remaining vertical space |
| `btn_stability_record` | Button | Curve actions — force-capture the current `(gf, smoothed %)` pair, bypassing detection |
| `btn_stability_sort` | SortToggleButton | Curve actions — toggle list sort direction (display only) |
| *(Edit… button, no x:Name)* | Button | Curve actions — `btn_stability_edit_Click`; opens the [edit dialog](#stabilityeditwindow) |
| *(Clear All / Clear Dots / Save… / Load…, no x:Name)* | Button | Curve actions — `btn_stability_clear_Click` (wipe recorded captures) / `btn_stability_clear_raw_Click` ("Clear Dots" — clears the temporary grey raw scatter, keeps recorded captures) / `btn_stability_save_Click` / `btn_stability_load_Click` |
| `reading_stability_unique` | LabeledReading | Curve meta — distinct capture count (after dedup); caption "Count:". (The old "Total:" readout was removed.) |
| `listBox_stability_captures` | ListBox | Curve body — one `EstimateCard` per `StabilityCapture`: `#N`, segments (gf → %, `×Count`), ✕ delete (`btn_stability_card_delete_Click`) |
| `reading_accum_samples` / `reading_accum_iaf` | LabeledReading | Accumulator readouts — total accumulated sample count and current IAF estimate (gf) from the logistic fit |
| `txt_accum_status` | TextBlock | Accumulator status line (current run/accumulation state) |
| `listBox_accum_table` | ListBox | "BUCKETS" body — per-bucket table: columns PHYS range / 0% / >0% / %ON, plus out-of-range "< min" / "≥ max" rows. Rows with ≥ 50 samples are tinted by %ON (≤20% → very light blue, ≥80% → very light purple); otherwise zebra striping. The active cell is still highlighted orange |

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

Done returns the edited `SessionMetadata`; Cancel and `Esc` return null. The metadata is reused by the Curve snapshot save (and shown in the chart title).
