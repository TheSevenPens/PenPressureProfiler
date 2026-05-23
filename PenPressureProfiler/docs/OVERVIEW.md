# PenPressureProfiler — Overview

## What It Is

PenPressureProfiler is a Windows desktop tool for characterising the pressure response of drawing tablet pens. It records paired measurements of **physical pressure** (grams-force, read from a digital scale over serial port) and **logical pressure** (normalised 0–1 value from the tablet driver via WinTab), then plots them as a response curve.

The output is a JSON file containing the curve data plus metadata about the pen, tablet, driver, and test conditions. These files are used to compare pens, document pressure curves, and analyse initial activation force (IAF) and saturation behaviour.

The tool has two recording modes:

- **Manual** — user presses at a chosen force level, reads both instruments, and clicks Record to save the pair.
- **Sweep** — user sweeps the pen through a range of pressures while the app automatically detects stable (physical, logical) pairs and captures them without clicking.

## What It Is Not

- Not a drawing application.
- Not a real-time pressure monitor (though it displays live pen data).
- Not cross-platform — it is Windows-only because WinTab is Windows-only.

## Hardware Setup

1. A drawing tablet connected with its driver installed.
2. A digital scale with serial (COM port) output, with the tablet placed flat on top.
3. The pen presses onto the tablet surface; the tablet transmits the force to the scale below.

## Workflow — Manual Mode

1. Launch the app. Pen pressure and tilt readings appear in the left panel immediately.
2. Select the COM port for the scale and press **Ctrl+T** to start reading scale data.
3. Apply pressure with the pen. The smoothed logical pressure and the scale reading stabilise.
4. Press **Ctrl+R** (or click **Record**) to save the current (physical gf, logical %) pair.
5. Repeat across the full pressure range.
6. Fill in metadata (brand, pen model, tablet, driver, OS, date).
7. Press **Export** to save a JSON file, or **Copy** to copy JSON to clipboard.

## Workflow — Sweep Mode

1. Start the scale (Ctrl+T).
2. Switch to the **Sweep** tab.
3. Press the pen onto the tablet at various pressures, dwelling briefly at each level.
4. Grey dots stream onto the scatter chart (live raw pairs); red dots appear as stable captures are auto-recorded.
5. Adjust the stability sliders as needed.
6. Switch to **Sweep Data** to review, inspect raw samples, and save the snapshot.

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+R | Record current reading (Manual) |
| Ctrl+L | Load sample data |
| Ctrl+C | Remove last record |
| Ctrl+A | Clear all records |
| Ctrl+S | Save to loaded file |
| Ctrl+T | Toggle scale start / stop |
| Ctrl+G | Toggle logging start / stop |
| Ctrl+W | Clear stable sweep captures |

## Axis Range Modes

Available in both Manual and Sweep tabs:

| Mode | Description |
|---|---|
| Default | 0–1000 gf × 0–100% |
| Full | Auto-scales X to data extent; Y stays 0–100% |
| IAF | Zooms in to the initial activation region (~2 gf wide, 0–5%) |
| IAF Large | Like IAF but 3× wider (~6 gf, 0–5%) — useful for reviewing the activation region with more context |
| Max | Zooms in on the saturation region (95–100%) |

## JSON Output Format

```json
{
  "brand": "WACOM",
  "pen": "PRO PEN 3",
  "penfamily": "...",
  "inventoryid": "--P.0042",
  "date": "2026-05-22",
  "user": "SEVEN",
  "tablet": "PTH-860",
  "driver": "6.4.2",
  "os": "WINDOWS",
  "tags": "",
  "notes": "",
  "records": [
    [ 10.0, 1.2345 ],
    [ 50.0, 15.6789 ]
  ]
}
```

Records are `[physical_gf, logical_percent]` pairs. Logical pressure is stored as a **percentage** (0–100), not a fraction.

## Technology Stack

| Component | Technology |
|---|---|
| Framework | .NET 10, Windows (`net10.0-windows`) |
| UI | Avalonia 11.3.x with Fluent theme |
| Pen input | WinPenKit v0.2.0 (WinTab, `WintabSystem` context), vendored DLL |
| Chart | ScottPlot.Avalonia 5.x |
| Dialogs | MessageBox.Avalonia (NuGet: `MessageBox.Avalonia`; namespace: `MsBox.Avalonia`) |
| Serial I/O | System.IO.Ports |

## Repository

Standalone repository: **https://github.com/TheSevenPens/PenPressureProfiler**
