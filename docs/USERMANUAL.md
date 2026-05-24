# PenPressureProfiler — User Manual

PenPressureProfiler measures and records the relationship between the **physical force** applied to a drawing tablet pen (measured by a digital scale) and the **logical pressure** reported by the tablet driver. The result is a pressure response profile — a curve showing how the driver maps physical force to a 0–100% pressure value.

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

The window is divided into three areas:

| Area | Purpose |
|---|---|
| **Left panel** | Live tablet and scale readings |
| **Centre panel** | Chart / visualisation (Manual, Sweep, Sweep Data tabs) |
| **Right panel** | Data recording, metadata, and file operations |

---

## Left Panel

### Pressure card
Real-time readings from the pen:

| Field | Description |
|---|---|
| Log Pressure (raw) | Raw integer value from the WinTab driver |
| Log Pressure (norm) | Normalised to 0–100% |
| Log Pressure (smooth) | Moving average of normalised pressure (200-sample window) |
| Pen rate | WinTab packets received per second |
| Progress bar | Visual indicator of smoothed pressure |
| Phys Pressure | Force reading from the scale (gf) |
| Scale rate | Scale readings received per second |

### Orientation card
Live tilt data from the pen stylus:

| Field | Description |
|---|---|
| Azimuth | Compass direction of pen tilt (0–360°) |
| Altitude | Angle from tablet surface (0°=flat, 90°=perpendicular) |
| Tilt X | Left/right tilt (−90° to +90°) |
| Tilt Y | Toward/away tilt (−90° to +90°) |

### Button State card
Shows which pen buttons are currently pressed (Tip, Button1, Button2).

### Scale control card
| Control | Description |
|---|---|
| COM Port | Serial port the scale is connected to |
| ▶ Start / ■ Stop (Ctrl+T) | Starts or stops reading from the scale |

### Logging card
| Control | Description |
|---|---|
| Status dot | Green = logging active, grey = idle |
| ▶ Start Logging / ■ Stop Logging (Ctrl+G) | Toggles CSV logging |
| Open Folder | Opens `Documents\PenPressureProfiler\Logs\` in Explorer |

When logging is active, two timestamped CSV files are written to `Documents\PenPressureProfiler\Logs\`:
- `pen_YYYY-MM-DD_HHmmss.csv` — pen data at ~60 Hz
- `scale_YYYY-MM-DD_HHmmss.csv` — scale readings as they arrive

---

## Centre Panel

### Axis Range selector
Controls the chart zoom. Present in all chart tabs:

| Mode | What it shows |
|---|---|
| Default | 0–1000 gf × 0–100% (full calibrated range) |
| Full | Auto-scales X to the data extent |
| IAF | Zooms in to the initial activation force region (~2 gf wide, 0–5%) |
| IAF Large | Like IAF but 3× wider (~6 gf, 0–5%) — useful for reviewing low-pressure activation detail |
| Max | Zooms into the saturation region (95–100% logical pressure) |

---

### Manual tab

The standard recording workflow:

1. Press the pen lightly onto the tablet surface (which rests on the scale).
2. Note the **Phys Pressure** reading (gf) and the **Log Pressure (smooth)** value.
3. Click **Record (Ctrl+R)** to save the current pair.
4. Repeat at different force levels across the full pressure range.
5. The chart updates after each recording, showing the emerging response curve.

**Tips:**
- Use **IAF** or **IAF Large** axis mode when recording low-force activation points.
- Use **Max** axis mode when approaching full pen saturation.
- Load sample data (Ctrl+L) to see example curve shape.
- Drag and drop a previously saved `.json` file onto the window to reload it.

#### Data panel buttons (right column)

| Button | Shortcut | Action |
|---|---|---|
| Record | Ctrl+R | Save current (physical, logical) pair |
| Sample | Ctrl+L | Load a set of example data points |
| Clear Last | Ctrl+C | Remove the most recently recorded point |
| Clear All | Ctrl+A | Remove all recorded points |
| Copy | — | Copy the session JSON to the clipboard |
| Save | Ctrl+S | Save to the last loaded file |
| Export | — | Save as a new JSON file in Documents |

#### Metadata fields
Fill in pen/tablet details before exporting. **Brand**, **ID**, and **Date** also appear in the chart title. All fields are included in the exported JSON.

| Field | Contents |
|---|---|
| Brand | Pen manufacturer |
| ID | Pen inventory/model identifier |
| Pen | Pen model name |
| Family | Pen family/series |
| User | Tester name (auto-filled from Windows username) |
| Tablet | Tablet model |
| Driver | Driver version |
| Date | Test date (auto-filled with today) |
| OS | Operating system (auto-filled as WINDOWS) |
| Tags | Free-form tags |
| Notes | Free-form notes |

#### JSON file format
```json
{
  "brand": "WACOM",
  "pen": "PRO PEN 3",
  "inventoryid": "--P.0042",
  "date": "2026-05-22",
  "records": [
    [10.0, 5.23],
    [100.0, 48.71]
  ]
}
```
Each record is `[physical_gf, logical_percent]`.

---

### Sweep tab

Sweep mode automatically detects stable moments during free-form pressing and records them as profile points — no manual clicking required.

**Workflow:**
1. Start the scale (Ctrl+T).
2. Switch to the **Sweep** tab.
3. Press the pen onto the tablet at various pressures, dwelling briefly at each level.
4. Watch grey dots stream onto the chart (raw pairs) and red dots appear when the app captures a stable reading.
5. Adjust sliders to tune detection sensitivity.
6. Switch to **Sweep Data** to review and save the captured points.

**What is a stable capture?**
A pair is captured when:
- Both the pen smoothed pressure and scale readings have been steady for at least **Stable duration** milliseconds
- The pen is not at 100% (saturated) and not at 0% (hovering)
- At least **Min capture gap** ms have elapsed since the last capture

**Stability settings:**

| Slider | Range | What it controls |
|---|---|---|
| Pen tolerance | 0.5–10% | How much normalised pen pressure can vary within the window |
| Scale tolerance | 0.5–30 gf | How much scale force can vary across recent readings |
| Stable duration | 100–2000 ms | How long both signals must be steady before a capture fires |
| Min capture gap | 200–3000 ms | Minimum time between two successive captures |

**Clear (Ctrl+W)** — removes all stable captures and clears the scatter plot.

---

### Sweep Data tab

A table of all stable captures from the current session, sorted by physical pressure.

| Column | Contents |
|---|---|
| # | Row number (sorted by force) |
| Physical gf | Average physical force during the stability window |
| Logical % | Average logical pressure during the stability window |
| Pen range | Spread of pen samples in the window (quality indicator) |
| Scale range | Spread of scale samples in the window (quality indicator) |

**Selecting a row** expands a detail panel showing the full set of raw pen and scale samples that made up the capture, plus min/max/range statistics.

#### Save / Load Snapshots
- **Save Snapshots** — saves all stable captures (including raw samples) to a JSON file via a file picker dialog.
- **Load Snapshots** — loads a previously saved snapshot file, restoring all captures and syncing the Sweep chart.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+R** | Record manual (physical, logical) pair |
| **Ctrl+L** | Load sample data |
| **Ctrl+C** | Clear last recorded point |
| **Ctrl+A** | Clear all recorded points |
| **Ctrl+S** | Save to current file |
| **Ctrl+T** | Toggle scale start / stop |
| **Ctrl+G** | Toggle logging start / stop |
| **Ctrl+W** | Clear stable sweep captures |

---

## Log Files

All log files are stored in `Documents\PenPressureProfiler\Logs\`.

### Pen log (`pen_YYYY-MM-DD_HHmmss.csv`)
Continuous ~60 Hz stream. Rows with `PacketCount=0` are zero-fill ticks (no tablet contact).

| Column | Description |
|---|---|
| Timestamp | Local time (ms precision) |
| RawPressure | Raw WinTab integer value |
| NormalizedPressure | 0.0–1.0 fraction |
| SmoothedPressure | Moving-average of normalised pressure |
| Azimuth | Pen compass direction (degrees) |
| Altitude | Pen angle from surface (degrees) |
| TiltX | Left/right tilt (degrees) |
| TiltY | Forward/back tilt (degrees) |
| TipDown | True/False |
| Barrel1Down | True/False |
| Barrel2Down | True/False |
| PacketCount | WinTab packets in this poll tick (0 = no contact) |

### Scale log (`scale_YYYY-MM-DD_HHmmss.csv`)

| Column | Description |
|---|---|
| Timestamp | Local time (ms precision) |
| Force_gf | Force reading in gram-force |

---

## Tips and Notes

- **Noise increases at low forces.** Use tight pen/scale tolerances and longer stable duration when profiling the activation region.
- **Never record at 100% logical pressure.** The pen clips all forces above its maximum to 100%, so those readings are ambiguous. The sweep mode enforces this automatically.
- **Scale sample rate (~8–10 Hz)** is the limiting factor for timing measurements. The physical peak of a sharp impact may be undersampled.
- **IAF (Initial Activation Force)** is the minimum physical force needed for the pen to register any pressure. Use the IAF or IAF Large axis range to zoom into this region.
- The **moving average clears** when the pen tip is released, giving a clean reading for the next press.
