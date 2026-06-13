# PenPressureProfiler — User Manual

PenPressureProfiler measures and records the relationship between the **physical
force** applied to a drawing-tablet pen (measured by a digital scale) and the
**logical pressure** reported by the tablet driver. The result is a pressure
response **curve** — how the driver maps physical force to a 0–100% value — plus
its endpoints: the **activation force** (IAF) and the **saturation force** (MAX).

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
| **MODE** | The mode dropdown (**Curve** / **Threshold**); for Curve, a second row adds the chart-type picker and its option (see below) |
| **CURVE AUTO-CAPTURE** *(Curve only)* | Start/Stop, an **Edit…** flyout of detection parameters, and a one-line settings summary |
| **THRESHOLD AUTO-CAPTURE** *(Threshold only)* | Sweep-mode picker, IAF-method picker, armed indicator, **Arm**, and Start/Stop |

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
| **Threshold** | Estimate the curve's endpoints: **IAF** (activation force) and **MAX** (saturation force). |

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

## Threshold mode

Threshold mode estimates the curve's endpoints. The **Mode** picker in the
THRESHOLD AUTO-CAPTURE section chooses the boundary and approach direction:

| Sub-mode | Gesture | Boundary |
|---|---|---|
| **IAF from below** *(default)* | Lift to the rest floor (≤ 2 gf), then press **up** slowly through activation | Activation force |
| **IAF from above** | Press past 30 gf, then **release** slowly to zero | Activation force |
| **MAX from below** | Push until logical pressure reads 100%, then lift | Saturation force |

Estimates for each sub-mode persist independently; switching the picker swaps the
view without wiping the others. Up to **20** estimates are collected per mode;
the **median** is then highlighted on the chart.

### Workflow

1. Select a COM port. **Start** starts the scale if needed.
2. MODE → **Threshold**, pick a sub-mode.
3. Click **Start**, then perform the sweep slowly and repeatedly.
4. Each valid sweep adds a card and a chart dot. **✕** on a card drops that
   estimate and frees a slot.

**Arming.** Each mode auto-arms on its precondition (from-below: dip to ≤ 2 gf;
from-above: peak ≥ 30 gf; MAX: a full lift). The armed dot turns green when
ready. The **Arm** button force-arms the active mode immediately, bypassing that
precondition.

**Rejections.** From-below ignores a press that started without arming, a
*downstroke* (force falling, not rising), and a sweep with no real 0%-reading
force. From-above rejects a release "under load" (jumped to zero instead of
gliding off). These keep bogus sweeps out of the list.

### <a name="threshold-methods"></a>IAF-from-below methods

Because the scale samples only ~10×/sec while the pen samples ~150×/sec, the
exact activation force is **bracketed** between two scale readings — the last
that read 0% and the first that read non-zero. The reported IAF sits between
them, and **DeltaPhys** is the width of that bracket (your measurement
uncertainty). A meaningful (non-zero) bracket requires a **slow, continuous
press** through the threshold — pressing to a level and *holding* collapses it.

The **IAF method** picker (shown only for IAF from below) chooses how that
bracket becomes an estimate, so you can compare them on real sweeps:

| Method | What it does | Best for |
|---|---|---|
| **Current** | Midpoint of the immediate bracket (last-0% → first-non-zero). | A raw, low-latency reading. Span is ~0 if you hold. |
| **A: Press-through** | Keeps the lower edge at the last-0% force but extends the upper edge until you press well past activation; reports the midpoint. | Guaranteeing a visible span (biases the estimate a bit high). |
| **B: Regression** *(recommended)* | Fits a line through the rising `(force, level)` points and extrapolates to the activation onset. | The most accurate estimate — press up slowly and smoothly. |
| **C: Time window** | Brackets the force ±200 ms around the crossing; reports the midpoint. | A time-based bracket experiment. |
| **D: Min-delta** | The Current bracket, widened backward until the span reaches 0.5 gf. | Forcing a minimum span (biases the estimate a bit low). |

The picker affects **new captures only**, so you can record a few sweeps with
each method and compare them in the list. Developer-level theory, operation, and
trade-offs are in [THRESHOLD_METHODS.md](THRESHOLD_METHODS.md).

### Captures pane (Threshold)

| Control | Action |
|---|---|
| **Record** | Force-record the current scale force as an estimate, bypassing detection. |
| **Copy** | Copy the captures to the clipboard as a Markdown table. |
| **Clear All** | Wipe all estimates for the current sub-mode. |
| **Progress** | `N / 20` for the current sub-mode. |
| **Median / Min / Max / Avg** | Statistics over the captured forces (gf). |

IAF estimate rows read as a three-point progression with the bracket width:

```
(<A> gf, 0%)  →  (<IAF> gf, IAF)  →  (<C> gf, <X>%)   ·   DeltaPhys <D> gf
```

where A is the last 0%-reading force, C the first non-zero-reading force at
reading X%, and D = C − A. MAX and manual estimates show a plain `gf → %` line.

The Threshold chart plots estimate index (x) vs gf (y): blue dots, a red dashed
median line, and a thick orange line tracking the live scale force.

---

## Chart navigation

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out, centred on the cursor |
| Right-click | Reset axes to the chart's default range |

The scatter and threshold charts open at a fixed default range; the time-series
view is a fixed rolling window (pan/zoom disabled).

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

## Log Files

In `Documents\PenPressureProfiler\Logs\`.

**Pen log** (`pen_*.csv`): Timestamp, RawPressure, NormalizedPressure,
SmoothedPressure, Azimuth, Altitude, TiltX, TiltY, TipDown, Barrel1Down,
Barrel2Down.

**Scale log** (`scale_*.csv`): Timestamp, Force_gf (formatted to the scale's
reported resolution), RawLine (the verbatim serial line).

---

## Tips and Notes

- **Press slowly** for IAF-from-below — a steady glide through activation gives a
  real bracket; a press-and-hold collapses DeltaPhys to ~0 (see
  [threshold methods](#threshold-methods)).
- **Scale sample rate (~8–15 Hz)** is the limiting factor for timing; a sharp
  impact's peak may be undersampled.
- **Noise grows at low forces** — tighten tolerances and lengthen the stable
  duration when profiling the activation region.
- The **moving average clears** on any pen-button release, giving a fresh reading
  on the next press.
- A new, finer scale's resolution is preserved end-to-end — readings show as many
  decimals as the device reports.
