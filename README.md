# Pen Pressure Profiler

A Windows tool for measuring and recording the **pressure response curve** of a drawing tablet pen — the relationship between the physical force applied to the pen and the logical pressure value reported by the tablet driver.

---

## What it does

Place a drawing tablet flat on a digital scale, press the pen with varying force, and PenPressureProfiler records pairs of *(physical force in gf, logical pressure %)* readings. The result is a pressure curve you can inspect, annotate, save as JSON, and compare across pens or driver configurations.

Modes (pick one from the **MODE** dropdown in the ribbon):

| Mode | How it works |
|---|---|
| **Manual** | You control when each data point is captured. Press the pen to the desired force, read the scale, click **Record**. |
| **Auto (Sweep)** | Automatic stability detection. Hold a steady force — when both the pen and scale readings are stable for long enough, a capture is recorded automatically. |
| **Threshold** | Automated edge detection — finds the activation (IAF) or saturation (MAX) force by sweeping. See [Threshold detection](#threshold-detection) below. |
| **Monitor** | Observation only — two live-scrolling EKG-style traces (pen normalized pressure + scale gf) over a 10-second window. No recording. |

---

## Threshold detection

Threshold mode estimates the physical force at which the driver crosses a logical-pressure boundary. Pick a sub-mode from the **Mode** dropdown in the Threshold panel, click **Start**, and perform the sweep 10 times — the final value is the median of the 10 estimates. An armed-status dot shows when the next sweep is ready to record.

| Sub-mode | What to do | What it measures |
|---|---|---|
| **IAF from above** | Press the pen until at least **30 gf**, then release fully to zero. Repeat 10 times. | Activation force, by extrapolating the falling raw signal to raw = 0. |
| **IAF from below** | Lift the pen so the scale reads **≤ 0.1 gf**, then press down gently until raw pressure becomes nonzero. Repeat 10 times. | Activation force, by extrapolating the rising raw signal back to raw = 1. |
| **MAX from below** | Press the pen until logical pressure reaches **100 %** (saturation), then lift fully off. Repeat 10 times. | Saturation force, by extrapolating the rising signal to 100 %. |

Each sub-mode keeps its own set of estimates; switching modes stops capture but preserves what you've collected. The orange line on the chart tracks live pressure so you can gauge how fast you're sweeping.

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
| [Architecture](docs/ARCHITECTURE.md) | Source layout, key classes, threading model, file formats |
| [Glossary](docs/GLOSSARY.md) | Terms used across the code, UI, and other docs |
| [Control flow](docs/CONTROL_FLOW.md) | Sequence diagrams for the main runtime scenarios |
| [UI map](docs/UI_MAP.md) | Labeled layout of every named control |
| [Testing](docs/TESTING.md) | What's covered, what isn't, smoke checklist |
| [WinPenKit surface](docs/WINPENKIT.md) | The vendored input library's API as we use it |

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
4. Launch **Pen Pressure Profiler**, select the COM port, click **Start**.
5. Press the pen at various forces and record data points.

---

## Chart navigation (all charts)

| Input | Action |
|---|---|
| Scroll wheel | Zoom in / out centred on cursor |
| Right-click | Reset to the chart's default range |

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

---

## Scale

This app is designed to work with the serial port output of a US Solid scale.

The specifica scale I use is: US SOlid 0.1 Precision Balance - 5kg. SKU JFDBS00086-5


