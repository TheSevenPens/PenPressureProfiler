# Pen Pressure Profiler

A Windows tool for measuring and recording the **pressure response curve** of a drawing tablet pen — the relationship between the physical force applied to the pen and the logical pressure value reported by the tablet driver.

---

## What it does

Place a drawing tablet flat on a digital scale, press the pen with varying force, and PenPressureProfiler records pairs of *(physical force in gf, logical pressure %)* readings. The result is a pressure curve you can inspect, annotate, save as JSON, and compare across pens or driver configurations.

Modes (pick one from the **MODE** dropdown in the ribbon):

| Mode | How it works |
|---|---|
| **Curve** | A scatter plot of `(physical gf → logical %)` points across the whole range. Auto-captures when both signals hold steady (configurable tolerances), or press **Record** to capture manually. Per-mode option **Follow live**. |
| **Time series** | Live scrolling pen + scale traces. Per-mode option **Overlay traces**; each stability capture drops a **red dot** on the traces where it happened. |
| **Accumulator** | Estimates the activation force (IAF) by bucketing every scale sample by force and tracking how often the pen is on vs. off in each bucket. See [Accumulator](#accumulator) below. |

**Curve** and **Time series** share the stability captures pane (**Record** / **Edit** / **Clear** / **Save** / **Load** plus the captures list and summary) and the **AUTO-CAPTURE** ribbon group. The captures-count readout is labelled **Count**.

---

## Accumulator

Accumulator mode estimates the **initial activation force (IAF)** — the force at which the pen first registers logical pressure. While running, it buckets each scale sample by physical force and increments a **pen 0%** (off) or **pen >0%** (on) counter for that bucket. The force where *on* overtakes *off* is the IAF, refined by a count-weighted logistic fit whose 50% point is the reported estimate.

Set up the capture in the **ACCUMULATOR** ribbon section, then **Start** / **Stop**; **Clear** resets the counters.

| Control | What it does |
|---|---|
| **Range (gf)** | The force window to bucket, min/max (default **0–10 gf**, half-open `[min, max)`). Samples below `min` and at/above `max` are counted in dedicated **below** / **above** buckets. |
| **Bucket size** | Bucket width: **1 / 0.5 / 0.25 / 0.1 gf** (default **0.5**). |
| **Apply scale-lag comp (245 ms)** | Time-aligns the pen feed to the slower/lagging scale by the measured response lag (`ScaleSessionManager.ResponseLagMs = 245 ms`, from **Tools ▸ Measure Scale Lag**). |

The **centre chart** plots each bucket's activation fraction (0–100%) as markers sized by sample count, overlaid with the logistic fit curve, a dotted 50% line, and a dashed red IAF line. X = force (gf), Y = pen-on %. The **right pane** shows **Samples** and **Est. IAF** readouts plus a **BUCKETS** table:

| Column | Contents |
|---|---|
| **PHYS** | Bucket range, e.g. `0.50 < 1.00`; out-of-range rows are `< min` and `≥ max`. |
| **0%** | Samples in this bucket with the pen off. |
| **>0%** | Samples in this bucket with the pen on. |
| **%ON** | Activation fraction for the bucket. |

Detection logic lives in `AccumulatorController` (`PenPressureProfiler/Detection/AccumulatorController.cs`).

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


