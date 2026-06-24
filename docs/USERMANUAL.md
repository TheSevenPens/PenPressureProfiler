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
│ Centre: chart (follows MODE)                  │ Right: captures pane         │
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
| **PEN** | Proximity dot (in-range: **Proximity / Out**) · Tip / B1 / B2 button dots · Azimuth · Altitude · TiltX · TiltY · **Hover Z** (WinTab hover height; "-" on other backends) |
| **PEN PRESSURE** | **Raw** (driver integer) · **Norm** (0–100%) · **Smoothed** (200-sample moving average) · **Pen rate** (packets/s) · a pressure gauge. *(All blank to `--` when the pen is lifted away.)* |
| **SCALE PRESSURE** | **Phys pressure (gf)** · **Scale rate** (readings/s) |
| **MODE** | The mode dropdown (**Curve** / **Time series** / **Accumulator**), plus the active mode's primary controls below it: **Curve / Time series** → the auto-capture **Start/Stop** toggle + the per-mode option (Follow live / Overlay traces); **Accumulator** → the **Measure** target picker (**IAF** / **Max pressure**) + **Start/Stop** + **Clear**. (Accumulator's scale-lag compensation and proximity gate live in **Tools ▸ Options**.) |
| **AUTO-CAPTURE** *(Curve and Time series)* | An **Edit…** flyout of detection parameters and a one-line settings summary. (The auto-capture **Start/Stop** toggle moved to the MODE group.) |
| **ACCUMULATOR SETTINGS** *(Accumulator only)* | **Range (gf)** min/max number boxes and a **Bucket size** picker. |

### Backends (Tablet picker)

| Backend | When to use |
|---|---|
| **WinTab** | Tablets in WinTab mode (most Wacom / XP-Pen / Huion). |
| **WM_POINTER (Avalonia)** | Tablets in Windows Ink mode. The pen must be over the centre chart for the app to receive events. |

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
| **Curve** | Record `(physical gf → logical %)` points across the whole range — the pressure response curve, shown as a scatter plot. |
| **Time series** | Watch live scrolling pen and scale traces over a rolling window while recording the same stability captures as Curve. |
| **Accumulator** | Estimate a force threshold by bucketing scale samples and finding where the pen crosses it. A **Measure** picker selects **IAF** (force to first turn on) or **Max pressure** (force to reach 100%). |

**Curve** and **Time series** are two views of the same recording workflow: they
share the captures pane and the AUTO-CAPTURE controls, and differ only in what
the centre chart shows. **Accumulator** is a separate workflow.

---

## Curve mode

Curve mode records stable `(gf, %)` pairs — automatically when both signals hold
steady, or manually with **Record**.

### Centre chart (Curve)

The centre chart is a scatter plot of physical force gf (x) vs logical % (y):
grey raw-pair stream, blue stable captures, a red tolerance box around the live
point, and a live crosshair.

**Per-mode option (MODE section, second row).**

| Option | Effect |
|---|---|
| **Follow live** | Auto zoom/pan to keep the last ~1 s of live points in view. |

### Auto-capture (MODE Start + AUTO-CAPTURE ribbon section)

The **Start/Stop** toggle is in the **MODE** group; the **Edit…** parameter flyout
and settings summary are in the **AUTO-CAPTURE** group.

1. Select a COM port (DEVICES → Scale). **Start** also starts the scale if it
   isn't already reading.
2. Click **Start** (MODE group) to feed pen/scale data to the stability detector.
3. Press the pen at various pressures, dwelling briefly at each level.
4. A pair is captured when **all** hold:
   - pen normalized pressure varied by ≤ **Pen tolerance** in the recent window,
   - scale force varied by ≤ **Scale tolerance** in the recent window,
   - both were steady for at least **Stable duration** ms, and
   - at least **Min capture gap** ms passed since the last capture.

The recorded logical value is the **smoothed** pen pressure (the Smoothed
readout) at the moment of capture — the same value **Record** stores and the live
crosshair shows, so auto- and manual captures are consistent. (Detection still
uses the raw normalized window; only the stored value is smoothed.)

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

### Captures pane (Curve and Time series)

The captures pane is shared by Curve and Time series modes.

| Control | Action |
|---|---|
| **Record** | Force-capture the current `(gf, smoothed %)` pair, bypassing detection. Also starts the scale if idle. |
| **↑/↓ sort** | Toggle list sort direction (display only). |
| **Edit…** | Open the [edit dialog](#edit-dialog) to review / delete captures. |
| **Clear Dots** *(Curve)* | Clear the temporary grey raw dots from the chart, keeping recorded captures (and the current zoom). |
| **Clear All** | Remove all captures + raw scatter. |
| **Save… / Load…** | Save / load a capture snapshot as JSON (drag-drop a `.json` onto the window to load). |
| **Count:** | Count of distinct capture points. |

Each row shows `#N   <gf> gf → <%>%   ×count`.

---

## Time series mode

Time series mode shares the recording workflow and captures pane with Curve mode
— the same stability captures, **Record** button, and AUTO-CAPTURE controls — but
shows live scrolling traces instead of a scatter plot.

### Centre chart (Time series)

Live scrolling traces (pen normalized + scale gf) over a 10 s window, EKG-style,
with horizontal tolerance bands. Each stability capture drops a **red dot** on
both the pen and scale traces, marking where it happened.

**Per-mode option (MODE section, second row).**

| Option | Effect |
|---|---|
| **Overlay traces** | One chart with dual y-axes (on) vs two stacked charts (off). |

### Auto-capture and captures pane

Identical to Curve mode — see [Auto-capture](#auto-capture-auto-capture-ribbon-section)
and [Captures pane](#captures-pane-curve-and-time-series) above.

---

## Accumulator mode

Accumulator locates a **force threshold** statistically. Instead of capturing
individual sweeps, it accumulates statistics over many samples: while running,
each scale reading is sorted into a **force bucket**, and that bucket's **under**
or **at-or-over** counter is incremented depending on whether the pen is below or
at/over the target's threshold at that instant. The force where "at-or-over"
overtakes "under" is the threshold. By default every scale sample is recorded;
enabling **Only record while pen is in proximity** in **Tools ▸ Options** drops
readings taken with the pen lifted away, so the tablet's resting weight on the
scale doesn't pollute the buckets.

A **Measure** picker chooses the target (each remembers its own range, buckets,
and data):

| Target | Measures | "at-or-over" means |
|---|---|---|
| **IAF** | initial activation force — where the pen first turns on | pen **> 0%** |
| **Max pressure** | the force at which the pen reaches 100% | pen **at 100%** (raw = driver max) |

Read the threshold off the **BUCKETS** table's **%** column: it climbs from 0%
to 100% across the force range, and the bucket where it crosses **~50%** is the
threshold force. Sweeping the pen force up and down across the range repeatedly
fills the buckets and sharpens the transition. (Max pressure won't show a
transition for a pen that never reaches 100%.)

### Configuration (MODE / ACCUMULATOR SETTINGS ribbon sections)

| Control | Default | Effect |
|---|---|---|
| **Measure** | IAF | Target: **IAF** or **Max pressure**. Each target keeps its own range, buckets, and accumulated data — switching just shows the other. |
| **Range (gf)** min / max | IAF 0 / 10, Max 0 / 500 | The `[min, max)` force window split into buckets. Edit by typing, the arrows, or the **mouse-wheel** (hold **Shift** for ×5). The arrow/wheel step scales with the target (1 gf for IAF, 50 gf for Max). |
| **Bucket size** | IAF 0.5 gf, Max 25 gf | Bucket width, from the target's set (IAF **1 / 0.5 / 0.25 / 0.2 / 0.1**; Max **50 / 25 / 10 / 5**). Finer buckets need more samples to fill. |
| **Start / Stop** | — | Begin / pause accumulation (also starts the scale if idle). |
| **Clear** | — | Reset the active target's bucket counts and fit. |

Samples outside the range are not discarded — they are counted in dedicated
**below** (`< min`) and **above** (`≥ max`) buckets.

All of a target's bucket widths accumulate at once, so changing the **Bucket
size** does not clear anything — it just re-displays the same samples at the new
width. Only changing the **range (min/max)** resets that target's accumulated
data. Save / Load stores both targets and all their width layouts.

### Scale-lag compensation

The scale responds more slowly than the tablet, so a raw sample can pair a fresh
pen state with a stale force. With **Apply scale-lag comp** on (the default), the
pen feed is time-shifted to the scale by the measured response lag (**245 ms**).
The toggle lives in **Tools ▸ Options**. Measure your own lag with **Tools ▸
Measure Scale Lag** (see below). Turn it off to compare uncompensated results.

### Workflow

1. Select a COM port. **Start** starts the scale if needed.
2. MODE → **Accumulator**.
3. Pick the **Measure** target (IAF or Max pressure).
4. Set the **Range (gf)** and **Bucket size** for the region you're profiling.
   The live vertical force line on the chart (see below) shows where the pen
   currently is — handy for centring the range before you start.
5. Click **Start**.
6. Slowly sweep the pen force **up and down across the range repeatedly**, so
   each bucket collects both under and at-or-over samples.
7. Watch the markers and the **%** column settle, then click **Stop**.
8. Use **Clear** to start a fresh run.

### Centre chart (Accumulator)

The chart plots **physical force (gf)** on the x-axis and **at-or-over %** on the
y-axis:

- one **marker per bucket** at its at-or-over fraction (0–100%), sized by how
  many samples fell in that bucket,
- a dotted **50%** reference line, and
- a **live vertical force line** at the current scale reading (like Curve mode's
  pressure line) — it tracks the scale whether or not accumulation is running.

No fitted curve or estimate line is drawn — read the threshold off the BUCKETS
**%** column (below).

### Right pane (Accumulator)

A **Samples** readout plus a per-bucket table:

| Readout | Meaning |
|---|---|
| **Samples** | Total scale samples accumulated this run. |

The **BUCKETS** table lists one row per bucket, with four fixed columns (the
per-target meaning of the threshold is in the description line above the table):

| Column | Meaning |
|---|---|
| **PHYS** | The bucket's force range (e.g. `0.50 < 1.00`). |
| **UNDER** | Samples where the pen was below the threshold (pen off for IAF, below 100% for Max pressure). |
| **OVER** | Samples where the pen was at or over the threshold (pen on / at 100%). |
| **%** | At-or-over fraction for the bucket — the threshold is the force where this crosses ~50%. |

Out-of-range samples appear in dedicated **`< min`** and **`≥ max`** rows.

Rows with **≥ 50** total samples are tinted by their **%** — **≤ 20%** (mostly
under) shows a very light blue, **≥ 80%** (mostly at-or-over) a very light
purple; other rows use plain zebra striping. The cell that just changed is
highlighted orange.

### Removing noisy buckets

To clean up a stray reading, **delete a bucket's data**:

- **Right-click a node** on the chart to delete that bucket, or
- **Right-click a row** in the BUCKETS table to erase it (including the
  `< min` / `≥ max` out-of-range rows).

Clearing applies to the **currently displayed bucket width** only. Because every
width accumulates independently from the same samples, the other widths keep
their counts — switch to a different bucket size and you'll see the un-cleared
data there. (Per-sample force isn't retained, so a deletion can't be propagated
across widths precisely; clear at each width you care about.) There's no undo, so
deleted counts are gone — but you can always **Start** again to re-accumulate.

### Live threshold tint (ribbon)

While Accumulator mode is active, the ribbon's **PEN PRESSURE** and **SCALE
PRESSURE** value readouts are tinted by the current under/at-or-over
classification — **very light blue** when the pen reading is **under** the active
target's threshold, **very light purple** when **at or over** it (the same colours
as the BUCKETS rows). It tracks live as you press, so you can watch the readouts
flip the instant the pen crosses the threshold. The tint clears when you leave
Accumulator mode or lift the pen.

---

## Chart navigation

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out, centred on the cursor |
| Right-click | Reset axes to the chart's default range |

The scatter and Accumulator charts open at a fixed default range; the
time-series view is a fixed rolling window (pan/zoom disabled).

---

## Edit dialog

Open with **Edit…** in the captures pane (Curve or Time series mode). Modal.

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
tablet; this dialog measures that delay so Accumulator can compensate
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

- **Sweep slowly and repeatedly** in Accumulator — passing the pen
  force up and down through the range many times fills each bucket with both off
  and on samples and sharpens the **%**-column transition.
- **Scale sample rate (~8–15 Hz)** is the limiting factor for timing; a sharp
  impact's peak may be undersampled.
- **Noise grows at low forces** — tighten tolerances and lengthen the stable
  duration when profiling the activation region.
- The **moving average clears** on any pen-button release, giving a fresh reading
  on the next press.
- A new, finer scale's resolution is preserved end-to-end — readings show as many
  decimals as the device reports.
