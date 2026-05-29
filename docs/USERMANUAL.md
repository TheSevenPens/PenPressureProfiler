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
┌─ Ribbon (top): PEN proximity | BUTTONS | ORIENTATION ────────────────────┐
├──────────────┬──────────────────────────────┬────────────────────────────┤
│ Left panel   │ Centre panel                 │ Right panel                │
│ Live sensor  │ Chart (Pressure / Sweep /    │ Tabs: Manual |             │
│ cards        │ IAF — follows the right tab) │       Auto | IAF           │
└──────────────┴──────────────────────────────┴────────────────────────────┘
```

The centre chart is paired with the right-panel tab: choose **Manual** to see the Pressure chart, **Auto** to see the Sweep chart, **IAF** to see the IAF estimates chart.

See [UI_MAP.md](UI_MAP.md) for the named control inventory.

---

## Ribbon (top bar)

Always-visible live pen state. Survives any tab switch in the panels below. Pressure values are shown in the left-panel **Pressure card**, not the ribbon.

| Field | Meaning |
|---|---|
| **PEN** proximity dot | **Tip down** (green) · **Proximity** (orange, packet within 300ms) · **Out** (gray) |
| **BUTTONS** | Tip / B1 / B2 dots; green when pressed |
| **ORIENTATION** | Azimuth · Altitude · TiltX · TiltY (degrees) |

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
| **Read / Stop** (`Ctrl+T`) | Starts or stops reading from the scale |

**Logging row:**

| Control | Description |
|---|---|
| Status dot | Green when CSV logging is active, gray when idle |
| **Start / Stop Logging** (`Ctrl+L` or `Ctrl+G`) | Toggles CSV logging |
| 📁 | Opens `Documents\PenPressureProfiler\Logs\` in Explorer |

While logging is active, two timestamped CSV files are appended to:
- `pen_YYYY-MM-DD_HHmmss.csv` — pen state at ~60 Hz
- `scale_YYYY-MM-DD_HHmmss.csv` — scale readings as they arrive

---

## Centre Panel — Charts

The centre shows one of two charts. Which one is visible is controlled by the **right-panel tab**:

| Right-panel tab | Centre chart |
|---|---|
| **Manual** | Pressure chart (your recorded points) |
| **Auto** | Sweep chart (raw stream + stable captures) |
| **IAF** | IAF estimates chart (one dot per release, median line) |
| **MAX** | MAX estimates chart (one dot per saturation, median line) |

### Chart navigation (works on both charts and the edit dialog)

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out, centred on the cursor |
| Hold `Space` + drag mouse | Pan |
| Right-click | Reset the axes to the currently selected range mode |

### Axis range (left panel → Chart card)

A single dropdown applies to whichever chart is currently visible (driven by the Manual / Auto tab).

| Mode | What it shows |
|---|---|
| **Default** | 0–1000 gf × 0–100% |
| **Full** | Auto-scales X to the data extent |
| **IAF** | Zooms to ~2 gf wide × 0–5% to inspect activation |
| **IAF Large** | Like IAF but ~6 gf wide × 0–5% |
| **Max** *(pressure chart only)* | Zooms into the saturation region (95–100% logical). On the sweep chart, falls back to **Default**. |

---

## Right Panel — Recording

Four tabs: **Manual**, **Auto**, **IAF**, **MAX**.

### Manual tab

The fixed-point workflow:

1. Press the pen onto the tablet (which rests on the scale).
2. Watch **Phys pressure** (gf) and **Log Pressure (smooth)** in the left panel.
3. Click **Record** (`Ctrl+R`) to save the current pair.
4. Repeat at different force levels across the range.

Records appear in `listBox_records` and on the Pressure chart.

#### Buttons

Header row:

| Button | Action |
|---|---|
| **↑ Force / ↓ Force** | Toggle list sort direction (display only — does not affect insertion order, so **− Last** still removes the most recently added record regardless of sort) |
| **Metadata…** | Open the [metadata dialog](#metadata-dialog) |

Primary actions:

| Button | Shortcut | Action |
|---|---|---|
| **Record** | `Ctrl+R` | Save the current `(physGf, logical)` pair |
| **− Last** | `Ctrl+C` | Remove the most recent point |

File ops (bottom row):

| Button | Shortcut | Action |
|---|---|---|
| **Clear All** | `Ctrl+A` | Remove all points |
| **Save…** | `Ctrl+S` | Save the session as JSON |
| **Load…** | — | Load a previously saved JSON file |

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

### Auto tab

Sweep mode automatically detects stable moments during free-form pressing and records them — no manual clicking required.

**Workflow:**

1. Start the scale (`Ctrl+T`).
2. Open the **Auto** tab, click **Start Auto-Capture**.
3. The centre chart automatically switches to the Sweep chart when you select the **Auto** tab.
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
| **Color dots by altitude** | When checked, each stable-capture dot on the Sweep chart is colored by its average pen altitude: 90° (pen vertical) → black, 60° (or lower) → cornflower blue, linearly interpolated. The connecting line stays default blue. |

**Auto captures card:**

Header row:

| Button | Action |
|---|---|
| **↑ Force / ↓ Force** | Toggle list sort direction |
| **Edit…** | Open the [edit dialog](#edit-dialog) for review and deletion |

Count display:

| Field | Description |
|---|---|
| **Unique:** N | Distinct capture points (after dedup within tolerance) |
| **Total:** M | All confirmations including duplicates (sum of `×N` counts) |

File ops (bottom row):

| Button | Shortcut | Action |
|---|---|---|
| **Clear All** | `Ctrl+W` | Remove all stable captures + raw scatter |
| **Save…** | — | Save captures (incl. raw samples) as JSON |
| **Load…** | — | Load a saved snapshot, replacing the current captures |

Each list row shows: `#NNN  PHYS gf  →  LOG%  ×count  pen:±range%  scale:±range gf`

The pen/scale ranges are quality indicators — smaller is steadier.

---

### IAF tab

Auto-IAF mode estimates [IAF](GLOSSARY.md#curve-regions) from a series of **release sweeps**: press the pen above a threshold, then let go all the way to zero. Each nonzero→zero transition in the raw pen pressure produces one IAF estimate.

**Workflow:**

1. Start the scale (`Ctrl+T`).
2. Open the **IAF** tab, click **Start Auto-IAF**.
3. Press the pen onto the tablet until **Phys pressure** reads at least **30 gf**, then release fully (raw pressure goes to 0).
4. Repeat **10 times**. Each successful release adds a row to the list and a dot to the chart.
5. After the 10th estimate, capture stops automatically and the **median** is highlighted as a red dashed line.

A release that never reached 30 gf is silently ignored — the status line tells you. To re-run, click **Start Auto-IAF** again; if the list was full it clears first.

A thick orange **live line** on the chart tracks the current scale reading in real time, helping you see how fast your pressure is moving — keep the release slow enough that the bracket markers separate visibly.

Click **− Last** to discard the most recent estimate (e.g. if you released too fast or had a poor stroke); the slot frees up and you can resume by pressing **Start Auto-IAF** again.

**How the estimate is computed.** When raw pressure drops from a nonzero value to zero between two consecutive pen ticks, the controller takes the two most recent nonzero samples and linearly extrapolates the trend in `(gf, raw)` space, solving for the gf where raw would equal 0. This is more robust at low forces than just reading the last-nonzero gf, because the raw signal often jumps in coarse driver steps near IAF.

**Cards:**

| Field | Description |
|---|---|
| **Progress** | `N / 10` — how many estimates have been recorded |
| **Median** | Running median of all recorded estimates, in gf |
| Estimate list | One row per estimate: `#NN  IAF: xx.xx gf   peak: yyy.y gf   bracket: raw R@Agf → 0@Bgf`. *Peak* is the highest gf reached during that sweep. *Bracket* is the last nonzero pen reading (`raw=R` at `gf=A`) and the first zero pen reading (at `gf=B`) — the two samples that straddle the zero crossing. The IAF estimate lies on the line through the last two nonzero samples extrapolated to raw=0; the bracket lets you sanity-check that the trend was clean. |
| **Clear All** | Wipes all estimates and resets to 0 / 10 |

The IAF chart plots estimate index on X, IAF in gf on Y. The red dashed horizontal line is the running median.

---

### MAX tab

Auto-MAX mode estimates the **saturation force** — the physical force at which the driver clips logical pressure to 100%. Each push sweep from below saturation up into saturation produces one estimate.

**Workflow:**

1. Start the scale (`Ctrl+T`).
2. Open the **MAX** tab, click **Start Auto-MAX**.
3. Press the pen onto the tablet until **Log Pressure (norm)** reads **100.00 %** (saturation), then lift the pen fully off (raw pressure goes to 0).
4. Repeat **10 times**. Each transition into saturation adds a row and a dot.
5. After the 10th estimate, capture stops and the **median** is highlighted as a red dashed line.

**How the estimate is computed.** When normalized pen pressure crosses from below 1.0 to ≥ 1.0 between two consecutive pen ticks, the controller takes the last two sub-saturated samples and linearly extrapolates the `(gf, norm)` trend, solving for the gf where norm would equal 1.0. This models the linear pressure-rise into saturation, which is more accurate than just reading the gf at the first-saturated tick (which tends to overshoot).

**Cycle rule.** Each push cycle produces at most one estimate. Once a saturation hit fires, you must fully **lift the pen** (raw → 0) before the next push can register. A dip back into sub-saturation while still in contact does *not* re-arm.

**Cards:**

| Field | Description |
|---|---|
| **Progress** | `N / 10` |
| **Median** | Running median of all estimates, in gf |
| Estimate list | `#NN  MAX: xx.xx gf   start: yy.y gf   bracket: norm L%@AAgf → 100%@BBgf`. *Start* is the lowest gf seen during the approach. *Bracket* is the last sub-saturated pen reading (`norm=L%` at `gf=A`) and the first saturated reading (at `gf=B`). |
| **− Last** | Drop the most recent estimate |
| **Clear All** | Wipes all estimates |

The MAX chart works the same as the IAF chart — brackets, blue dots, red dashed median line, orange live-pressure indicator.

---

## Edit dialog

Open with **Edit…** in the Sweep tab. Modal — you can't return to the main window until you close it.

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
| `Delete` key | Delete all currently selected rows |
| **Delete Selected** button | Same as Delete key |
| **Done** | Apply changes — return surviving captures to the main window |
| **Cancel** | Discard all changes |

Edits inside the dialog modify a local copy; the main window's captures only change on **Done**.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+R` | Record manual `(physical, logical)` pair |
| `Ctrl+C` | Remove last manual record (when not typing in a TextBox) |
| `Ctrl+A` | Remove all manual records (when not typing in a TextBox) |
| `Ctrl+S` | Save manual session JSON (when not typing in a TextBox) |
| `Ctrl+T` | Toggle scale read / stop |
| `Ctrl+L` *or* `Ctrl+G` | Toggle logging |
| `Ctrl+W` | Clear stable sweep captures |
| `Space` (hold) | Engage chart pan — drag the mouse to pan |

Text-edit shortcuts (`Ctrl+C/A/S`) are not intercepted while the cursor is in a TextBox.

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
- **IAF (Initial Activation Force)** is the minimum physical force needed for the pen to register any pressure. Use the **IAF** or **IAF Large** axis mode to zoom in.
- The **moving average clears** on any pen-button release, giving a fresh reading on the next press.
