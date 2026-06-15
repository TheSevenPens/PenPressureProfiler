# PenPressureProfiler — User Manual

PenPressureProfiler measures and records the relationship between the **physical
force** applied to a drawing-tablet pen (measured by a digital scale) and the
**logical pressure** reported by the tablet driver. The result is a pressure
response **curve** — how the driver maps physical force to a 0–100% value — plus
the **activation force** (IAF), the force at which the pen first turns on.

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
2. Connect the scale to the PC via its serial/USB cable (9600 baud).
3. Connect the tablet to the PC normally (driver installed).
4. Launch PenPressureProfiler.

---

## Interface Overview

```
┌─ Menu bar:  Edit | Help ───────────────────────────────────────────────────┐
├─ Ribbon:  DEVICES | PEN | PEN PRESSURE | SCALE PRESSURE | MODE | <mode> ─────┤
├──────────────────────────────────────────────┬─────────────────────────────┤
│ Centre: chart (follows MODE + chart type)     │ Right: captures pane         │
└──────────────────────────────────────────────┴─────────────────────────────┘
```

There is no longer a left sidebar — all live readouts and controls live in the
ribbon. The window has two working areas: the **centre chart** and the **right
captures pane**, both driven by the ribbon **MODE** dropdown.

See [UI_MAP.md](UI_MAP.md) for the named-control inventory.

---

## Menu bar

| Menu | Item | Action |
|---|---|---|
| **Edit** | **Metadata…** | Open the [metadata dialog](#metadata-dialog) (pen/tablet/session details) |
| **Help** | **About** | Version + links to the GitHub repo and README |

---

## Ribbon

Always-visible live state plus the mode controls. Left to right:

| Section | Contents |
|---|---|
| **DEVICES** | **Tablet** backend picker (`ApiCombo`) · **Scale** COM-port picker + **Start/Stop** · **Logging** Start/Stop + 📁 (open log folder) |
| **PEN** | Proximity dot (Tip down / Proximity / Out) and Tip / B1 / B2 button dots; Azimuth · Altitude · TiltX · TiltY |
| **PEN PRESSURE** | **Raw** (driver integer) · **Smoothed** (200-sample moving average) · **Pen rate** (packets/s) · **Norm** (0–100%) · a pressure gauge |
| **SCALE PRESSURE** | **Phys pressure (gf)** · **Scale rate** (readings/s) |
| **MODE** | The mode dropdown (**Curve** / **Threshold Accumulator**); for Curve, a second row adds the chart-type picker and its option (see below) |
| **CURVE AUTO-CAPTURE** *(Curve only)* | Start/Stop, an **Edit…** flyout of detection parameters, and a one-line settings summary |
| **THRESHOLD ACCUMULATOR** *(Threshold Accumulator only)* | **Range (gf)** min/max number boxes, a **Bucket size** picker, an **Apply scale-lag comp (245 ms)** checkbox, and **Start/Stop** + **Clear** |

### Backends (Tablet picker)

| Backend | When to use |
|---|---|
| **WinTab** | Tablets in WinTab mode (most Wacom / XP-Pen / Huion). |
| **Avalonia Pointer** | Tablets in Windows Ink mode. The pen must be over the centre chart for the app to receive events. |

### Logging

The Logging Start/Stop appends two timestamped CSVs to
`Documents\PenPressureProfiler\Logs\`:

- `pen_YYYY-MM-DD_HHmmss.csv` — pen state (~60 Hz)
- `scale_YYYY-MM-DD_HHmmss.csv` — scale readings as they arrive

---

## Modes

The **MODE** dropdown selects what the centre chart and right pane do:

| MODE | Purpose |
|---|---|
| **Curve** | Record `(physical gf → logical %)` points across the whole range — the pressure response curve. |
| **Threshold Accumulator** | Estimate the **IAF** (initial activation force) by bucketing scale samples and finding where the pen turns on. |

---

## Curve mode

Curve mode records stable `(gf, %)` pairs — automatically when both signals hold
steady, or manually with **Record**.

### Chart types (MODE section, second row)

| Chart type | Centre chart | Option shown |
|---|---|---|
| **Scatter Plot** | gf (x) vs logical % (y): grey raw-pair stream, blue stable captures, a red tolerance box around the live point, and a live crosshair. | **Follow live** — auto zoom/pan to keep the last ~1 s of live points in view. |
| **Time series** | Live scrolling traces (pen normalized + scale gf) over a 10 s window, EKG-style, with horizontal tolerance bands. | **Overlay traces** — one chart with dual y-axes (on) vs two stacked charts (off). |

The chart type only changes the *centre* view; the captures pane is shared, so
you can record while watching either view.

### Auto-capture (CURVE AUTO-CAPTURE ribbon section)

1. Select a COM port (DEVICES → Scale). **Start** below also starts the scale if
   it isn't already reading.
2. Click **Start** to feed pen/scale data to the stability detector.
3. Press the pen at various pressures, dwelling briefly at each level.
4. A pair is captured when **all** hold:
   - pen normalized pressure varied by ≤ **Pen tolerance** in the recent window,
   - scale force varied by ≤ **Scale tolerance** in the recent window,
   - both were steady for at least **Stable duration** ms, and
   - at least **Min capture gap** ms passed since the last capture.

**Dedup count.** A new capture within tolerance of an existing one increments
that capture's **count** (shown as `×N`) instead of duplicating it.

**Parameters (Edit… flyout).** The **Edit…** button opens a drop-down with:

| Control | Default | Effect |
|---|---|---|
| Tolerance preset | LOW | LOW / MEDIUM / HIGH — sets pen+scale tolerances together (MEDIUM = 1.25% / 5 gf, HIGH = 2.5% / 10 gf) |
| Pen tolerance | 0.5% | Allowed spread of normalized pen pressure |
| Scale tolerance | 0.25 gf | Allowed spread of scale force |
| Stable duration | 500 ms | How long both signals must be steady |
| Min capture gap | 500 ms | Minimum gap between captures |

The current values are summarized on two lines below the Start/Edit row.

### Captures pane (Curve)

| Control | Action |
|---|---|
| **Record** | Force-capture the current `(gf, smoothed %)` pair, bypassing detection. Also starts the scale if idle. |
| **↑/↓ sort** | Toggle list sort direction (display only). |
| **Edit…** | Open the [edit dialog](#edit-dialog) to review / delete captures. |
| **Clear All** | Remove all captures + raw scatter. |
| **Save… / Load…** | Save / load a capture snapshot as JSON (drag-drop a `.json` onto the window to load). |
| **Unique:** | Count of distinct capture points. |

Each row shows `#N   <gf> gf → <%>%   ×count`.

---

## Threshold Accumulator mode

Threshold Accumulator estimates the **IAF** (initial activation force) — the
physical force at which the pen first turns on. Instead of capturing individual
sweeps, it accumulates statistics over many samples: while running, each scale
reading is sorted into a **force bucket**, and that bucket's **pen 0%** (off) or
**pen >0%** (on) counter is incremented depending on whether the pen reports any
logical pressure at that instant. The force where "on" overtakes "off" is the
IAF.

A count-weighted **logistic fit** through the per-bucket activation fractions
gives the IAF as the curve's **50% point**, shown as **Est. IAF**. Sweeping the
pen force up and down across the range repeatedly fills the buckets and lets the
fit settle.

### Configuration (THRESHOLD ACCUMULATOR ribbon section)

| Control | Default | Effect |
|---|---|---|
| **Range (gf)** min / max | 0 / 10 | The `[min, max)` force window that is split into buckets. |
| **Bucket size** | 0.5 gf | Bucket width: **1 / 0.5 / 0.25 / 0.1** gf. Finer buckets need more samples to fill. |
| **Apply scale-lag comp (245 ms)** | on | Time-aligns the faster pen feed to the slower/lagging scale by the measured response lag, so on/off counts land in the correct bucket. |
| **Start / Stop** | — | Begin / pause accumulation (also starts the scale if idle). |
| **Clear** | — | Reset all bucket counts and the fit. |

Samples outside the range are not discarded — they are counted in dedicated
**below** (`< min`) and **above** (`≥ max`) buckets.

### Scale-lag compensation

The scale responds more slowly than the tablet, so a raw sample can pair a fresh
pen state with a stale force. With **Apply scale-lag comp** on, the pen feed is
time-shifted to the scale by the measured response lag (**245 ms**). Measure your
own lag with **Tools ▸ Measure Scale Lag** (see below). Turn the checkbox off to
compare uncompensated results.

### Workflow

1. Select a COM port. **Start** starts the scale if needed.
2. MODE → **Threshold Accumulator**.
3. Set the **Range (gf)** and **Bucket size** for the region you're profiling.
4. Click **Start**.
5. Slowly sweep the pen force **up and down across the range repeatedly**, so
   each bucket collects both off and on samples.
6. Watch the fit curve and **Est. IAF** settle, then click **Stop**.
7. Use **Clear** to start a fresh run.

### Centre chart (Threshold Accumulator)

The chart plots **physical force (gf)** on the x-axis and **pen-on %** on the
y-axis:

- one **marker per bucket** at its activation fraction (0–100%), sized by how
  many samples fell in that bucket,
- the overlaid **logistic fit** curve,
- a dotted **50%** line, and
- a dashed **red IAF line** at the fit's 50% point.

### Right pane (Threshold Accumulator)

Two readouts plus a per-bucket table:

| Readout | Meaning |
|---|---|
| **Samples** | Total scale samples accumulated this run. |
| **Est. IAF** | The fit's 50% point — the estimated activation force. |

The **BUCKETS** table lists one row per bucket:

| Column | Meaning |
|---|---|
| **PHYS** | The bucket's force range (e.g. `0.50 < 1.00`). |
| **0%** | Off count — samples where the pen read 0% in this bucket. |
| **>0%** | On count — samples where the pen read any pressure. |
| **%ON** | On fraction for the bucket. |

Out-of-range samples appear in dedicated **`< min`** and **`≥ max`** rows.

---

## Chart navigation

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out, centred on the cursor |
| Right-click | Reset axes to the chart's default range |

The scatter and Threshold Accumulator charts open at a fixed default range; the
time-series view is a fixed rolling window (pan/zoom disabled).

---

## Edit dialog

Open with **Edit…** in Curve mode. Modal.

- Left: a scatter chart of all captures. Right: a multi-select list.
- **Blue** = monotonic; **orange ⚠** = *monotonic violator* (its % dips below the
  running max of all lower-force captures — a good curve never goes backwards);
  **red ◆** = selected.

| Input | Action |
|---|---|
| Click a chart dot (≤ 15 px) | Select the matching row |
| Ctrl+click a dot | Toggle row selection |
| Right-click a row | Delete it immediately |
| **Delete Selected** | Delete all selected rows |
| **Done** | Apply — return surviving captures |
| **Cancel** | Discard changes |

---

## Metadata dialog

Pen/tablet/session details (**Edit → Metadata…**). **Brand**, **Inventory ID**,
and **Date** appear in the chart title; all fields are written to the JSON on
save. Complete metadata is required before a save (you'll be prompted if a field
is missing).

| Field | Contents |
|---|---|
| Brand / Pen / Pen family | Pen identity |
| Inventory ID | Pen inventory identifier |
| Date / User | Test date (today) / tester (current Windows user) |
| Tablet / Driver / OS | Device + driver + OS (`WINDOWS`) |
| Tags / Notes | Free-form |

---

## Measure Scale Lag dialog

Open with **Tools ▸ Measure Scale Lag**. The scale responds more slowly than the
tablet; this dialog measures that delay so Threshold Accumulator can compensate
for it.

1. Tap the pen on the scale roughly **10×**.
2. The dialog reports the **Min / Max / Avg / Median** pen→scale delay (ms) for
   each of three events: **Onset**, **Peak**, and **Release**.

Use the resulting figure as the scale-lag value; the app's built-in compensation
default is **245 ms**.

---

## Log Files

In `Documents\PenPressureProfiler\Logs\`.

**Pen log** (`pen_*.csv`): Timestamp, RawPressure, NormalizedPressure,
SmoothedPressure, Azimuth, Altitude, TiltX, TiltY, TipDown, Barrel1Down,
Barrel2Down.

**Scale log** (`scale_*.csv`): Timestamp, Force_gf (formatted to the scale's
reported resolution), RawLine (the verbatim serial line).

---

## Tips and Notes

- **Sweep slowly and repeatedly** in Threshold Accumulator — passing the pen
  force up and down through the range many times fills each bucket with both off
  and on samples and lets the logistic fit settle.
- **Scale sample rate (~8–15 Hz)** is the limiting factor for timing; a sharp
  impact's peak may be undersampled.
- **Noise grows at low forces** — tighten tolerances and lengthen the stable
  duration when profiling the activation region.
- The **moving average clears** on any pen-button release, giving a fresh reading
  on the next press.
- A new, finer scale's resolution is preserved end-to-end — readings show as many
  decimals as the device reports.
