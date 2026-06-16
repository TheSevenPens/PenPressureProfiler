# Glossary

Short definitions for terms that show up across the code, UI, and other docs.
When in doubt, link to a term from another doc with `[name](GLOSSARY.md#anchor)`.

---

## Pressure

**Physical pressure** — Force applied to the pen tip, in **grams-force (gf)**, read from the digital scale via serial. The "ground truth" the app correlates against.

**Logical pressure** — The value the tablet driver *reports* for the same press. Has three forms in this app:

| Form | Range | Source |
|---|---|---|
| **Raw** | `0 … session.MaxPressure` (driver-defined integer) | `PenPoint.Pressure` from WinPenKit |
| **Normalized** | `0.0 … 1.0` | `raw / MaxPressure` |
| **Smoothed** | `0.0 … 1.0` | 200-sample moving average of normalized, in `MovingAverage` |

Captures are stored on disk via [`StabilitySnapshotFile`](../PenPressureProfiler/Model/StabilitySnapshotFile.cs); logical norm is held as a **fraction** (0.0–1.0) in memory.

---

## Curve regions

**IAF (Initial Activation Force)** — The lowest physical force at which the pen reports a non-zero logical value. The bottom of the curve. Estimated statistically via the **Accumulator** mode.

**Saturation** — Where logical pressure reaches 100%. Beyond this, the driver clips all higher forces to the same value, so the (physical, logical) mapping becomes ambiguous. Saturated windows *are* captured (used to be excluded) — the user can decide whether to keep them via the Edit dialog.

**Activation-threshold bounce** — Samples that fluctuate between zero and non-zero near IAF. Zero-raw windows *are* captured (used to be excluded) so the activation region shows up in the curve; the Edit dialog flags monotonic violations these tend to produce.

---

## Modes

**Mode** — The ribbon **MODE** dropdown selects what the centre chart and right [captures pane](#captures) do. There are exactly three modes:

| Mode | Purpose |
|---|---|
| **Curve** | Record `(physical gf → logical %)` points across the whole range — the pressure response curve, drawn as a scatter plot. |
| **Time series** | Watch live scrolling pen + scale traces over a ~10 s window, EKG-style. Absorbs the old "Monitor" view. |
| **Accumulator** | Estimate **IAF** (initial activation force) statistically by bucketing physical force and counting pen-off vs pen-on samples per bucket. |

**Curve mode** — A scatter plot of gf (x) vs logical % (y): raw-pair stream, stable captures, live tolerance box + crosshair. The UI name for what the code calls **Stability** (controller `StabilityController`, plots `stabilityPlotView` / `StabilitySnapshotFile`). Records stable `(gf, %)` pairs automatically when both signals hold steady, or manually with **Record**. See [Stability terms](#stability-curve-terms).

**Time series mode** — Live scrolling traces (pen norm + scale gf) over a ~10 s window, EKG-style. Each stability capture is marked with a red dot on the traces. Previously a Curve chart-type rather than a top-level mode.

**Follow live** — A Curve-mode option that keeps the scatter plot scrolled/scaled to follow the incoming live data.

**Overlay traces** — A Time-series-mode option that overlays the pen and scale traces on a shared axis rather than stacking them.

> **Manual mode** and **Monitor mode** no longer exist. Manual recording was removed entirely; Monitor is now the top-level **Time series** mode. The former Curve-only chart-type picker (Scatter Plot vs Time series) has been removed — Time series is its own mode.

**Captures pane** — The right-hand panel shared by **Curve** and **Time series**, listing stable `(gf, %)` captures; in **Accumulator** mode it shows the per-bucket table and estimated IAF instead. Holds the per-mode Record / Edit / Clear / Save / Load / Copy controls and the summary statistics, including the **Count** readout (number of captures; formerly "Unique"). The auto-capture ribbon group that drives it is labelled **AUTO-CAPTURE**.

---

## <a name="stability-curve-terms"></a>Stability (Curve) terms

**Stability window** — A sliding `Queue` of recent samples. Depth scales with `MinStableMs`:

```
penWindowDepth   = max(5, MinStableMs / 21)        ← ~48 Hz pen ticks
scaleWindowDepth = max(2, MinStableMs / 115 + 1)   ← ~8.7 Hz scale readings
```

**Tolerance** — Maximum allowed spread (max − min) within a stability window:
- **Pen tolerance** — fraction of normalized pressure, e.g. `0.03` = 3%.
- **Scale tolerance** — gram-force, e.g. `5.0 gf`.

**Stability gate** — Two timing conditions that must both pass before a capture fires:
- **`MinStableMs`** — both signals continuously eligible for at least this long.
- **`MinGapMs`** — wall-clock gap since the *previous* capture.

**Stable capture** — One (averaged physical gf, averaged logical norm) pair recorded automatically when all stability conditions hold. Stored as a `StabilityCapture` with the raw sample lists that produced it.

**Dedup count** (`StabilityCapture.Count`, shown as `×N` in the list) — When a new stable capture lands within tolerance of an existing one, that existing capture's `Count` is incremented instead of adding a duplicate row. So `×3` means "this point was independently re-confirmed twice."

**Monotonic violation** — In `StabilityEditWindow`: a capture whose logical norm is *below* the running maximum of all captures with lower physical force. A correctly-functioning pen curve should be monotonically increasing, so violators are surfaced with an orange `⚠` for review/deletion. The check is in [`StabilityEditWindow.ComputeViolators`](../PenPressureProfiler/Views/StabilityEditWindow.axaml.cs).

---

## Accumulator mode

**Accumulator** — The top-level mode that estimates **IAF** (initial activation force) statistically rather than from individual sweeps. On each scale sample it buckets the physical force and counts whether the pen reads **0%** (off) or **>0%** (on); IAF is the force where the *on* count overtakes the *off* count. Controller [`AccumulatorController`](../PenPressureProfiler/Detection/AccumulatorController.cs).

**Bucket** — A fixed physical-force bin. The accumulator covers a `[min, max)` range (default **0–10 gf**) divided into bins of a selectable width — **1 / 0.5 / 0.25 / 0.1 gf** (default **0.5 gf**). Samples outside the range are tallied in the **below** (`< min`) and **above** (`≥ max`) rows.

**Activation fraction / %ON** — Per bucket, `>0% / (0% + >0%)`, expressed as **0–100%**. The share of samples in that bucket for which the pen was on.

**Logistic fit / Est. IAF** — A count-weighted logistic regression over the buckets' activation fractions. The reported **IAF** is the fit's 50% point (`F0`). **CrossoverGf** is a simple fallback: the lowest bucket where the *on* count is ≥ the *off* count.

**Scale-lag compensation** — Time-aligns the pen feed to the lagging scale by shifting it by the measured response lag, `ScaleSessionManager.ResponseLagMs` = **245 ms** (measured with the **Measure Scale Lag** tool). Keeps each pen sample matched to the scale reading it actually corresponds to.

**Measure Scale Lag** — A Tools-menu dialog: tap the pen on the scale ~10 times, and it reports the **Min / Max / Avg / Median** pen→scale delay for **Onset**, **Peak**, and **Release**. Used to set the response-lag value above.

---

## Captures

The [captures pane](#modes) holds the active mode's results. The stability captures (shared by **Curve** and **Time series**) are the only kind with on-disk persistence.

| | Curve / Time series (Stability) | Accumulator |
|---|---|---|
| In-memory type | `StabilityCapture` | per-bucket off/on counts |
| Collection | `StabilityController.Captures` | `AccumulatorController` buckets |
| Contents | `(physGf, logicalNorm)` plus raw `PenSample[]` (+ legacy `ScaleSample[]`) | per-bucket 0% / >0% counts, activation fraction, fitted IAF |
| Persistence | `StabilitySnapshotFile` (JSON) — Save / Load, drag-drop | none (in-session only; `Copy` to Markdown) |

---

## Pen plumbing

**Backend / InputApi** — Which underlying driver the app talks to for pen data. Selected via the `ApiCombo` dropdown.

| `InputApi` value | UI label | Notes |
|---|---|---|
| `WintabSystem` | WinTab | Default. Most Wacom/XP-Pen/Huion drivers in WinTab mode. |
| `WintabDigitizer` | WinTab (high-res) | High-resolution digitizer context. |
| `AvaloniaPointer` | Avalonia Pointer | For drivers in Windows Ink mode. Receives events only when pen is over `PenInputSurface`. |
| `WmPointer` | *(excluded)* | Available in WinPenKit but skipped — WM_POINTER subclassing doesn't receive events under Avalonia. |

**PenInputSurface** — A transparent `Border` overlaying both charts. Serves two roles: (1) the pointer attachment surface for `AvaloniaPointerSession`, and (2) the chart navigation overlay that intercepts wheel/space-pan/right-click and forwards to the active chart underneath. Must remain a childless `Border` — see [`ARCHITECTURE.md`](ARCHITECTURE.md#peninputsurface).

**Proximity state** — The ribbon dot shows one of three:

| State | Color | Condition |
|---|---|---|
| **Tip down** | green | `TipDown == true` |
| **Proximity** | orange | last packet within 300 ms but tip not down |
| **Out** | gray | no packets for ≥300 ms |

**Scale dot state** — The Device Inputs card's Scale-row dot shows one of three:

| State | Color | Condition |
|---|---|---|
| **Error** | red | no COM ports available, or last `StartAsync` raised |
| **Idle** | yellow | COM port available but `IsReading == false` |
| **Active** | green | `IsReading == true` and data is arriving |

**Drain tick** — One iteration of `PenSessionManager`'s 60 fps `DispatcherTimer`. Calls `session.DrainPoints()` to get all packets queued since the last tick, folds them into the moving average + button tracker, and emits one `PenReadingData`.

**No-packet tick** — A drain tick where `DrainPoints` returned zero items. The manager re-emits the last reading with `PacketCount = 0`, preserving the previous pressure if `TipDown` is true. This is what keeps the UI from flickering with `AvaloniaPointerSession`, which only fires on `PointerMoved`.

---

## Files written by the app

**Pen log** — `Documents\PenPressureProfiler\Logs\pen_YYYY-MM-DD_HHmmss.csv`. ~60 Hz stream of pen state. Rows with `PacketCount == 0` are idle ticks.

**Scale log** — `Documents\PenPressureProfiler\Logs\scale_YYYY-MM-DD_HHmmss.csv`. One row per parsed serial reading (~8–10 Hz).

**Capture snapshot file** — JSON, schema = `StabilitySnapshotFile`. The Curve-mode Save / Load format. Includes every raw pen sample inside each capture's stability window (scale samples are still *read* from older files but no longer written).
