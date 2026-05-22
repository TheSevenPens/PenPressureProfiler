# PenPressureProfiler — Overview

## What It Is

PenPressureProfiler is a Windows desktop tool for characterizing the pressure response of drawing tablet pens. It records paired measurements of **physical pressure** (grams-force, read from a digital scale over serial port) and **logical pressure** (normalized 0–1 value from the tablet driver via WinTab), then plots them as a response curve.

The output is a JSON file containing the curve data plus metadata about the pen, tablet, driver, and test conditions. These files are used to compare pens, document pressure curves, and analyze initial activation force (IAF) and saturation behavior.

## What It Is Not

- Not a drawing application.
- Not a real-time pressure monitor (though it displays live pen data).
- Not cross-platform — it is Windows-only because WinTab is Windows-only.

## Hardware Setup

1. A drawing tablet connected and its driver installed.
2. A digital scale with serial (COM port) output, positioned so the pen tip presses directly onto its surface.
3. The pen is pressed onto the scale while the scale reading and tablet pressure value are captured simultaneously.

## Workflow

1. Launch the app. Pen pressure and tilt readings appear in the left panel as soon as the pen is on the tablet.
2. Select the COM port for the scale and click **Start** to begin reading scale data.
3. Apply pressure with the pen on the scale. The moving-average logical pressure and the scale reading stabilize.
4. Click **Record** (or Ctrl+R) to save the current (physical, logical) pair.
5. Repeat across the full pressure range.
6. Fill in metadata (brand, pen model, tablet, driver, OS, date).
7. Click **Export** to save a JSON file, or **Copy** to copy JSON to clipboard.

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+R | Record current reading |
| Ctrl+L | Load sample data |
| Ctrl+C | Remove last record |
| Ctrl+A | Clear all records |
| Ctrl+S | Save to loaded file |
| Ctrl+T | Stop scale reading |

## Axis Range Modes

| Mode | Description |
|---|---|
| Default | 0–1000 gf × 0–100% |
| Full | Auto-scales X to data; Y stays 0–100% |
| IAF | Zooms in on the initial activation region |
| max | Zooms in on the saturation region (95–100%) |

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
  "os": "WINDOWS 11",
  "tags": "",
  "notes": "",
  "records": [
    [ 10.0 , 1.2345 ],
    [ 50.0 , 15.6789 ],
    ...
  ]
}
```

Records are `[physical_gf, logical_percent]` pairs. Logical pressure is stored as a percentage (0–100), not a fraction.

## Technology Stack

| Component | Technology |
|---|---|
| Framework | .NET 10, Windows |
| UI | Avalonia 11.3.x |
| Pen input | WinPenKit (WinTab, `WintabSystem` context) |
| Chart | ScottPlot.Avalonia 5.x |
| Dialogs | MessageBox.Avalonia |
| Serial I/O | System.IO.Ports |

## Project Location

Currently lives inside the `WinTabPainter` solution. Intended to be extracted into its own standalone repository. See `docs/FUTURES.md`.
