# PenPressureProfiler — User Manual

PenPressureProfiler measures and records the relationship between the **physical force** applied to a drawing tablet pen (measured by a digital scale) and the **logical pressure** reported by the tablet driver. The result is a pressure response profile — a curve showing how the driver maps physical force to a 0–100% pressure value.

Terms used here are defined in [GLOSSARY.md](GLOSSARY.md).

---

## Hardware Setup

```
  Drawing Tablet
  ┌─────────────┐
  │             │  ← place flat on the scale
  └─────────────┘
  ┌─────────────┐
  │  Digital    │  ← scale connected to PC via serial/USB
  │  Scale      │
  └─────────────┘
```

1. Place the **drawing tablet flat on the digital scale**.
2. Connect the scale to the PC via its serial/USB cable.
3. Connect the tablet to the PC normally (driver installed).
4. Launch PenPressureProfiler.

---

## Interface Overview

```
┌─ Ribbon (top): PEN | BUTTONS | ORIENTATION | MODE | HELP ─────────────────┐
├──────────────┬──────────────────────────────┬────────────────────────────┤
│ Left panel   │ Centre panel                 │ Right panel                │
│ Live sensor  │ Chart (follows the ribbon    │ Controls for the current   │
│ cards        │ MODE selection)              │ MODE                       │
└──────────────┴──────────────────────────────┴────────────────────────────┘
```

The ribbon's **MODE** dropdown drives both the centre chart and the right panel: **Manual** → Pressure chart, **Auto** → Sweep chart, **Threshold** → Threshold chart (IAF or MAX, per the sub-mode picker), **Monitor** → live traces.

See [UI_MAP.md](UI_MAP.md) for the named control inventory.

---

## Ribbon (top bar)

Always-visible live pen state. Survives any MODE switch in the panels below. Pressure values are shown in the left-panel **Pressure card**, not the ribbon.

| Field | Meaning |
|---|---|
| **PEN** proximity dot | **Tip down** (green) · **Proximity** (orange, packet within 300ms) · **Out** (gray) |
| **BUTTONS** | Tip / B1 / B2 dots; green when pressed |
| **ORIENTATION** | Azimuth · Altitude · TiltX · TiltY (degrees) |
| **MODE** | Dropdown (**Manual** / **Auto** / **Threshold** / **Monitor**) — picks which right-panel and centre chart are shown |
| **HELP** | **About** button — opens a dialog with the version and links to the GitHub repo and README |

---

## Left Panel

### Pen card

Live pen readings.

| Field | Description |
|---|---|
| Log Pressure (raw) | Raw integer value from the driver |
| Log Pressure (norm) | Normalised to 0–100% |
| Log Pressure (smooth) | 200-sample moving average of normalised pressure |
| Pen rate | Pen packets per second (averaged over a rolling 1 s window) |
| Progress bar | Visual indicator of normalised pressure |

### Scale card

Live scale readings.

| Field | Description |
|---|---|
| Phys pressure (gf) | Latest force reading from the scale |
| Scale rate | Scale readings per second |

### Device Inputs card

Both data sources in one place.

**Tablet row:**

| Control | Description |
|---|---|
| Status dot | Green when a pen session is running, gray otherwise |
| Backend dropdown (`ApiCombo`) | Picks the input backend — changing it immediately stops the current session and starts a new one |

| Backend | When to use |
|---|---|
| **WinTab** | Default. Tablets in WinTab mode (most Wacom/XP-Pen/Huion). |
| **WinTab (high-res)** | High-resolution digitizer context, if your driver exposes it. |
| **Avalonia Pointer** | For tablets in Windows Ink mode. The pen must be physically over the centre chart area for the app to receive events. |

**Scale row:**

| Control | Description |
|---|---|
| Status dot | **Red** = no COM port available, or last attempt failed · **Yellow** = COM port available but not reading · **Green** = actively reading |
| COM port dropdown | Serial port the scale is connected to |
| **Start / Stop** | Starts or stops reading from the scale |

**Logging row:**

| Control | Description |
|---|---|
| Status dot | Green when CSV logging is active, gray when idle |
| **Start / Stop Logging** | Toggles CSV logging |
| 📁 | Opens `Documents\PenPressureProfiler\Logs\` in Explorer |

While logging is active, two timestamped CSV files are appended to:
- `pen_YYYY-MM-DD_HHmmss.csv` — pen state at ~60 Hz
- `scale_YYYY-MM-DD_HHmmss.csv` — scale readings as they arrive

---

## Centre Panel — Charts

The centre chart is controlled by the ribbon's **MODE** dropdown:

| MODE | Centre chart |
|---|---|
| **Manual** | Pressure chart (your recorded points) |
| **Auto** | Sweep chart (raw stream + stable captures) |
| **Threshold** | Threshold chart (IAF or MAX estimates — picked via the sub-mode ComboBox) |
| **Monitor** | Two stacked live-scrolling EKG-style traces (pen normalized + scale gf), 10-second window |

### Chart navigation (works on all charts and the edit dialog)

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out, centred on the cursor |
| Right-click | Reset the axes to the chart's default range |

The Pressure and Sweep charts open at a fixed default range (0–1000 gf × 0–100%); use the scroll wheel to zoom into the activation or saturation regions, and right-click to snap back.

---

## Right Panel — Recording

The ribbon's **MODE** dropdown selects the panel: **Manual**, **Auto**, **Threshold**, or **Monitor**.

### Manual mode

The fixed-point workflow:

1. Press the pen onto the tablet (which rests on the scale).
2. Watch **Phys pressure** (gf) and **Log Pressure (smooth)** in the left panel.
3. Click **Record** to save the current pair.
4. Repeat at different force levels across the range.

Records appear in `listBox_records` and on the Pressure chart.

#### Buttons

Header row:

| Button | Action |
|---|---|
| **↑ Force / ↓ Force** | Toggle list sort direction (display only — does not affect insertion order; the per-card ✕ deletes the correct record regardless of sort) |
| **Metadata…** | Open the [metadata dialog](#metadata-dialog) |

Primary actions:

| Button | Action |
|---|---|
| **Record** | Save the current `(physGf, logical)` pair |

File ops (bottom row):

| Button | Action |
|---|---|
| **Clear All** | Remove all points |
| **Save…** | Save the session as JSON |
| **Load…** | Load a previously saved JSON file |

Drag-and-drop a `.json` file onto the window to load it without using the file picker.

#### Metadata dialog

Pen/tablet details for the session live in a modal dialog (the **Metadata…** button). **Brand**, **Inventory ID**, and **Date** appear in the chart title; all fields are written to the JSON on save.

| Field | Contents |
|---|---|
| Brand | Pen manufacturer |
| Pen | Pen model name |
| Pen family | Pen family/series |
| Inventory ID | Pen inventory identifier |
| Date | Test date (defaults to today) |
| User | Tester (defaults to current Windows user) |
| Tablet | Tablet model |
| Driver | Driver version |
| OS | Operating system (defaults to `WINDOWS`) |
| Tags | Free-form |
| Notes | Free-form, multi-line |

**Done** applies changes and updates the chart title. **Cancel** (or `Esc`) discards them. Loading a JSON file replaces all metadata from disk.

#### JSON file format

```json
{
  "brand": "WACOM", "pen": "PRO PEN 3", "penfamily": "PRO",
  "inventoryid": "--P.0042", "date": "2026-05-22", "user": "SEVEN",
  "tablet": "PTH-860", "driver": "6.4.2", "os": "WINDOWS",
  "tags": "", "notes": "",
  "records": [
    [10.0, 5.23],
    [100.0, 48.71]
  ]
}
```
Each record is `[physical_gf, logical_percent]`.

---

### Auto mode

Sweep mode automatically detects stable moments during free-form pressing and records them — no manual clicking required.

**Workflow:**

1. Make sure a COM port is selected (the scale row's dropdown). The Start button below will start the scale automatically if it isn't already running.
2. Select **Auto** from the ribbon **MODE** dropdown, then click **Start Auto-Capture**.
3. The centre chart switches to the Sweep chart automatically.
4. Press the pen onto the tablet at various pressures, dwelling briefly at each level.
5. Grey dots stream onto the chart (raw pairs); blue dots appear when a stable point is captured.
6. Adjust sliders to tune detection sensitivity.

**What is a stable capture?**

A pair is captured when **all** of these hold:

- Pen normalised pressure has varied by ≤ **Pen tolerance** within the recent window.
- Scale force has varied by ≤ **Scale tolerance** within the recent window.
- Both signals have been continuously eligible for at least **Stable duration** ms.
- At least **Min capture gap** ms have passed since the previous capture.

> Earlier versions excluded saturated windows (pen at 100%) and zero-raw windows
> (activation-threshold bounce). Those guards have been removed so the full
> curve — including the saturation plateau and the activation region — is captured.

**Dedup count.** If a new stable capture falls within tolerance of an existing one, that existing capture is **not** duplicated — its **count** is incremented and shown as `×N` in the list. So `×3` means a point was independently re-confirmed twice.

**Auto Parameters card (collapsible — collapsed by default):**

Click the header to expand. Sliders:

| Slider | Range | Default | Controls |
|---|---|---|---|
| Pen tolerance | 0.1 – 10% | 0.5% | Spread of normalised pen pressure within the window |
| Scale tolerance | 0.1 – 30 gf | 0.25 gf | Spread of scale force within the window |
| Stable duration | 100 – 2000 ms | 500 ms | How long both signals must be steady |
| Min capture gap | 200 – 3000 ms | 500 ms | Minimum gap between successive captures |

**Auto Detection card:**

| Control | Action |
|---|---|
| **Start / Stop Auto-Capture** | Gates whether new pen/scale data feeds the detector |

**Auto captures card:**

Header row:

| Button | Action |
|---|---|
| **↑ Force / ↓ Force** | Toggle list sort direction |
| **Edit…** | Open the [edit dialog](#edit-dialog) for review and deletion |

Inside the card:

| Button | Action |
|---|---|
| **Record** | Force-captures the current `(physGf, smoothed logical)` pair, bypassing stability detection. Useful when you see a value you want but the detector won't fire (noisy signal, tolerances too tight). |

Count display:

| Field | Description |
|---|---|
| **Unique:** N | Distinct capture points (after dedup within tolerance) |
| **Total:** M | All confirmations including duplicates (sum of `×N` counts) |

File ops (bottom row):

| Button | Action |
|---|---|
| **Clear All** | Remove all stable captures + raw scatter |
| **Save…** | Save captures (incl. raw samples) as JSON |
| **Load…** | Load a saved snapshot, replacing the current captures |

Each list row shows: `#NNN  PHYS gf  →  LOG%  ×count  pen:±range%  scale:±range gf`

The pen/scale ranges are quality indicators — smaller is steadier.

---

### Threshold mode

Threshold mode estimates the gf at which the driver crosses a logical-pressure boundary. The sub-mode picker at the top of the panel chooses which boundary and direction:

| Sub-mode | Sweep direction | Boundary |
|---|---|---|
| **IAF from above** | Release: high pressure → 0% | Activation force (gf where raw becomes 0), extrapolated from the falling raw signal |
| **IAF from below** | Lift fully (<0.1 gf), then press up into activation | Activation force, extrapolated *backward* from the first two nonzero samples |
| **MAX from below** | Push: low pressure → 100% | Saturation force (gf where logical hits 100%) |

Both sub-modes use the same UI controls and chart layout. **Estimates from each sub-mode persist independently** — switching the ComboBox stops the active capture and swaps the view, but neither slate of 10 is wiped.

**Workflow:**

1. Make sure a COM port is selected. **Start Auto-IAF / Start Auto-MAX** will start the scale automatically if it isn't already reading.
2. Select **Threshold** from the ribbon **MODE** dropdown and pick a sub-mode in the ComboBox.
3. Click **Start Auto-IAF** / **Start Auto-MAX**.
4. Perform the sweep:
   - **IAF from above:** press past 30 gf, then release fully.
   - **IAF from below:** lift the pen so the scale reads below 0.1 gf, then press down gently until logical pressure becomes nonzero.
   - **MAX from below:** press until logical pressure reads 100%, then lift fully.
5. Repeat **10 times**. Each valid sweep adds a card and a chart dot.
6. After the 10th estimate, capture stops and the **median** is highlighted as a red dashed line.

A thick **orange live line** on the chart tracks the current scale reading in real time, helping you see how fast your pressure is moving.

Click the **✕** on any estimate card to drop that estimate (e.g. if you swept too fast or had a poor stroke); a slot frees up and you can resume by pressing Start again.

**How the estimate is computed.**

- **IAF from above.** When raw pressure drops from a nonzero value to zero between two consecutive pen ticks, the controller takes the last two nonzero samples and linearly extrapolates the `(gf, raw)` trend *forward* to find the gf where raw would equal 0.
- **IAF from below.** When raw pressure first transitions from 0 to nonzero (with the controller armed by a prior scale reading ≤ 0.1 gf), the controller collects the first two nonzero pen samples and linearly extrapolates the rising trend *backward* to find the gf where raw would equal **1** (the smallest meaningful driver value). The estimate fires once the second nonzero sample is in hand. An armed-status dot above the Start button turns green once the scale has reached the resting floor.
- **MAX.** When normalized pen pressure crosses from below 1.0 to ≥ 1.0, the controller takes the last two sub-saturated samples and extrapolates `(gf, norm)` to find the gf where norm would equal 1.0.

**IAF-from-above rule.** A release that never reached 30 gf is silently ignored (no estimate is added). Press harder before lifting.

**IAF-from-below rule.** A press that starts without first lifting below 0.1 gf is silently ignored. Lift the pen fully off the tablet before pressing again. Once an estimate fires, the controller must see another sub-0.1-gf reading before the next sweep can register.

**MAX cycle rule.** Each push cycle produces at most one MAX estimate. Once a saturation hit fires, you must fully **lift the pen** (raw → 0) before the next push can register. A dip back into sub-saturation while still in contact does *not* re-arm.

**Cards:**

| Field | Description |
|---|---|
| **Mode** | ComboBox: IAF from above / IAF from below / MAX from below. Switching stops capture but preserves each mode's estimates independently. |
| **Progress** | `N / 10` for the currently-selected mode |
| **Median** | Running median for the currently-selected mode, in gf |
| Estimate list | One small card per estimate showing `#N`, **Physical** (extrapolated gf), **Raw** (driver pressure integer at the boundary — `0` for IAF, the driver's MaxPressure for MAX), **Logical** (`0%` or `100%` — the boundary percent), and a **✕** delete icon. Clicking ✕ removes that single estimate and renumbers the rest. |
| **Clear All** | Wipes all estimates for the currently-selected mode (per-estimate deletion is via the ✕ on each card). |

The Threshold chart plots estimate index on X, gf on Y. The IAF/MAX dots are blue, the median is a red dashed line, and the orange line tracks live pressure.

---

### Monitor mode

Two stacked live-scrolling charts — pen normalized pressure on top, scale physical force (gf) on the bottom. A 10-second rolling window scrolls leftward, EKG-style. Refreshes ~20 times per second.

Monitor mode does **not** record, estimate, or save anything — it's purely observational. Useful for sanity-checking that pen + scale streams are flowing, diagnosing noise / dropouts, or simply watching the response while you press.

**Behavior:**

- Switching to Monitor resets the traces (buffers cleared, time axis starts from 0).
- **Clear traces** button does the same on demand.
- **Overlay both traces on one chart** checkbox — when on, the pen trace (blue, left y-axis 0–1) and scale trace (orange, right y-axis gf) share a single chart with dual y-axes. When off, the two traces live on separate stacked charts.
- Pan / zoom on the charts are disabled — the rolling window is the view.
- The scale's y-axis auto-scales upward to fit the tallest recent sample, but never below a 5 gf ceiling — so light touches stay readable rather than being flattened against the axis.

---

## Edit dialog

Open with **Edit…** in Auto mode. Modal — you can't return to the main window until you close it.

**What you see:**
- Left: a scatter chart of all captures.
- Right: a multi-select list.

**Colours:**
- **Blue** dots / clean rows — monotonic.
- **Orange ⚠** dots / orange-tinted rows — *monotonic violators*. A capture is a violator when its logical % drops below the running maximum of all captures with lower physical force. A correctly-shaped pen curve should never go backwards.
- **Red ◆** — currently selected.

**Interactions:**

| Input | Action |
|---|---|
| Click a chart dot (within 15 px) | Select the matching row |
| Ctrl+click a chart dot | Toggle row selection |
| Right-click a list row | Delete that row immediately |
| **Delete Selected** button | Delete all currently selected rows |
| **Done** | Apply changes — return surviving captures to the main window |
| **Cancel** | Discard all changes |

Edits inside the dialog modify a local copy; the main window's captures only change on **Done**.

---

## Log Files

All log files are stored in `Documents\PenPressureProfiler\Logs\`.

### Pen log (`pen_YYYY-MM-DD_HHmmss.csv`)

Continuous ~60 Hz stream. Rows on idle ticks still appear; their `PacketCount` is reported per-tick in memory but the CSV omits that field — they show up as zeros across the dynamic columns when no contact.

| Column | Description |
|---|---|
| Timestamp | Local time (ms precision) |
| RawPressure | Raw driver integer |
| NormalizedPressure | 0.0–1.0 fraction |
| SmoothedPressure | Moving-average of normalised pressure |
| Azimuth | Pen compass direction (degrees) |
| Altitude | Pen angle from surface (degrees) |
| TiltX | Left/right tilt (degrees) |
| TiltY | Forward/back tilt (degrees) |
| TipDown | True/False |
| Barrel1Down | True/False |
| Barrel2Down | True/False |

### Scale log (`scale_YYYY-MM-DD_HHmmss.csv`)

| Column | Description |
|---|---|
| Timestamp | Local time (ms precision) |
| Force_gf | Force reading in gram-force |

---

## Tips and Notes

- **Noise grows at low forces.** Use tight pen/scale tolerances and a longer stable duration when profiling the activation region.
- **Don't profile at 100% logical pressure.** The driver clips everything above the maximum to 100%, so those readings are ambiguous. Sweep mode excludes them automatically.
- **Scale sample rate (~8–10 Hz)** is the limiting factor for timing measurements. A sharp impact's peak may be undersampled.
- **IAF (Initial Activation Force)** is the minimum physical force needed for the pen to register any pressure. Scroll-wheel zoom into the low-force corner of the chart to inspect it, or use the **Threshold** view for automated IAF estimation.
- The **moving average clears** on any pen-button release, giving a fresh reading on the next press.
