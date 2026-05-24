# Pen Pressure Profiler

A Windows tool for measuring and recording the **pressure response curve** of a drawing tablet pen — the relationship between the physical force applied to the pen and the logical pressure value reported by the tablet driver.

---

## What it does

Place a drawing tablet flat on a digital scale, press the pen with varying force, and PenPressureProfiler records pairs of *(physical force in gf, logical pressure %)* readings. The result is a pressure curve you can inspect, annotate, save as JSON, and compare across pens or driver configurations.

Two recording modes:

| Mode | How it works |
|---|---|
| **Manual** | You control when each data point is captured. Press the pen to the desired force, read the scale, click **Record** (or `Ctrl+R`). |
| **Auto (Sweep)** | Automatic stability detection. Hold a steady force — when both the pen and scale readings are stable for long enough, a capture is recorded automatically. |

---

## Requirements

- Windows 10 or 11
- .NET 10 runtime ([download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0))
- A drawing tablet with a WinTab-compatible driver (or Windows Ink mode)
- A digital scale with a serial/USB output (optional — needed for physical force readings)

---

## Download

Grab the latest release from the [Releases page](https://github.com/TheSevenPens/PenPressureProfiler/releases/latest).

Extract the zip and run `PenPressureProfiler.exe`. No installer needed.

---

## Documentation

| Document | Contents |
|---|---|
| [User Manual](docs/USERMANUAL.md) | Hardware setup, interface overview, keyboard shortcuts, file formats |
| [Architecture](docs/ARCHITECTURE.md) | Source layout, key classes, data flow, threading model, file formats |

---

## Hardware setup

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

1. Place the drawing tablet **flat on the scale**.
2. Connect the scale to the PC via its serial/USB cable.
3. Connect the tablet normally (driver installed).
4. Launch **Pen Pressure Profiler**, select the COM port, click **Read**.
5. Press the pen at various forces and record data points.

---

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+R` | Record current data point |
| `Ctrl+S` | Save JSON |
| `Ctrl+C` | Remove last record |
| `Ctrl+A` | Clear all records |
| `Ctrl+T` | Toggle scale reading |
| `Ctrl+L` / `Ctrl+G` | Toggle logging |
| `Ctrl+W` | Clear sweep captures |

### Chart navigation (all charts)

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out centred on cursor |
| Hold `Space` + move mouse | Pan |
| Right-click | Reset to current axis range |

---

## Building from source

```
git clone https://github.com/TheSevenPens/PenPressureProfiler.git
cd PenPressureProfiler
dotnet build PenPressureProfiler/PenPressureProfiler.csproj -c Release
```

Requires .NET 10 SDK and Windows (the project targets `net10.0-windows`).

---

## Pen input backends

| Backend | When to use |
|---|---|
| **WinTab** | Default. Works with tablets whose driver is in WinTab mode (most Wacom, XP-Pen, Huion drivers). |
| **Avalonia Pointer** | For tablets configured for Windows Ink / WM_POINTER mode. Enable "Use Windows Ink" in your driver settings first. |

Switch between backends with the dropdown in the **Tablet** section of the left panel.
