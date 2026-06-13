# Pen Pressure Profiler

A Windows tool for measuring and recording the **pressure response curve** of a drawing tablet pen — the relationship between the physical force applied to the pen and the logical pressure value reported by the tablet driver.

---

## What it does

Place a drawing tablet flat on a digital scale, press the pen with varying force, and PenPressureProfiler records pairs of *(physical force in gf, logical pressure %)* readings. The result is a pressure curve you can inspect, annotate, save as JSON, and compare across pens or driver configurations.

Modes (pick one from the **MODE** dropdown in the ribbon):

| Mode | How it works |
|---|---|
| **Curve** | Records `(physical gf → logical %)` points across the whole range. Auto-captures when both signals hold steady (configurable tolerances), or press **Record** to capture manually. Two chart types: a **Scatter Plot** (gf vs %) and a live **Time series** (scrolling pen + scale traces). |
| **Threshold** | Automated endpoint detection — finds the activation (IAF) or saturation (MAX) force by sweeping. See [Threshold detection](#threshold-detection) below. |

---

## Threshold detection

Threshold mode estimates the physical force at which the driver crosses a logical-pressure boundary. Pick a sub-mode in the THRESHOLD AUTO-CAPTURE ribbon section, click **Start**, and sweep slowly and repeatedly (up to **20** estimates) — the final value is the median. An armed dot shows when the next sweep is ready; the **Arm** button force-arms it.

| Sub-mode | What to do | What it measures |
|---|---|---|
| **IAF from below** *(default)* | Lift to the rest floor (**≤ 2 gf**), then press **up** slowly through activation. | Activation force. |
| **IAF from above** | Press past **30 gf**, then **release** slowly to zero. | Activation force. |
| **MAX from below** | Press until logical pressure reaches **100 %**, then lift fully off. | Saturation force. |

Because the scale samples far slower than the pen, the activation force is **bracketed** between the last 0%-reading and first non-zero-reading scale samples; the estimate sits between them and **DeltaPhys** is the bracket width. For IAF from below you can choose how that bracket becomes an estimate (Current / Press-through / Regression / Time-window / Min-delta) and compare them — see [Threshold methods](docs/THRESHOLD_METHODS.md). Each sub-mode keeps its own estimates; switching modes preserves them. The orange chart line tracks live force so you can gauge your sweep speed.

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
| [User Manual](docs/USERMANUAL.md) | Hardware setup, interface overview, modes, file formats |
| [Threshold methods](docs/THRESHOLD_METHODS.md) | IAF/MAX capture methods — theory, operation, pros/cons |
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

Switch between backends with the dropdown in the **DEVICES** section of the ribbon.

---

## Scale

This app is designed to work with the serial port output of a US Solid scale.

The specifica scale I use is: US SOlid 0.1 Precision Balance - 5kg. SKU JFDBS00086-5


