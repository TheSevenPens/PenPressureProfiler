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

**IAF (Initial Activation Force)** — The lowest physical force at which the pen reports a non-zero logical value. The bottom of the curve. Measured automatically via the **Threshold** view's IAF sub-modes.

**Saturation** — Where logical pressure reaches 100%. Beyond this, the driver clips all higher forces to the same value, so the (physical, logical) mapping becomes ambiguous. Saturated windows *are* captured (used to be excluded) — the user can decide whether to keep them via the Edit dialog.

**Activation-threshold bounce** — Samples that fluctuate between zero and non-zero near IAF. Zero-raw windows *are* captured (used to be excluded) so the activation region shows up in the curve; the Edit dialog flags monotonic violations these tend to produce.

---

## Modes

**Mode** — The ribbon **MODE** dropdown selects what the centre chart and right [captures pane](#captures) do. There are exactly two modes:

| Mode | Purpose |
|---|---|
| **Curve** | Record `(physical gf → logical %)` points across the whole range — the pressure response curve. |
| **Threshold** | Estimate the curve's endpoints: **IAF** (activation force) and **MAX** (saturation force). |

**Curve mode** — The UI name for what the code calls **Stability** (controller `StabilityController`, plots `stabilityPlotView` / `StabilitySnapshotFile`). Records stable `(gf, %)` pairs automatically when both signals hold steady, or manually with **Record**. See [Stability terms](#stability-curve-terms).

**Chart type** — A Curve-only second-row picker (`comboBox_capture_chart`) that selects only the *centre* view; the captures pane is shared across both:

| Chart type | Centre view |
|---|---|
| **Scatter Plot** | gf (x) vs logical % (y): raw-pair stream, stable captures, live tolerance box + crosshair. |
| **Time series** | Live scrolling traces (pen norm + scale gf) over a ~10 s window, EKG-style. Absorbs the old "Monitor" view. |

> **Manual mode** and **Monitor mode** no longer exist. Manual recording was removed entirely; Monitor is now Curve's **Time series** chart type, not a separate mode.

**Captures pane** — The right-hand panel listing the current mode's results: stable `(gf, %)` captures in Curve mode, IAF/MAX estimates in Threshold mode. Shared across both Curve chart types. Holds the per-mode Record / Edit / Clear / Save / Load / Copy controls and the summary statistics.

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

## Threshold mode

**Sub-mode** — Threshold wraps three sub-modes, picked by a ComboBox in the THRESHOLD AUTO-CAPTURE ribbon section: **IAF from below** (default — push sweep into activation, [`IafBelowController`](../PenPressureProfiler/Detection/IafBelowController.cs)), **IAF from above** (release sweep, [`IafController`](../PenPressureProfiler/Detection/IafController.cs)), and **MAX from below** (push sweep into saturation, [`MaxController`](../PenPressureProfiler/Detection/MaxController.cs)). Estimates for each sub-mode persist independently — switching sub-mode stops any active capture but does not wipe results.

**Release sweep** — A single push-then-release cycle (IAF from above). The user raises the pen pressure to at least `MinPeakGf` (30 gf default) and then releases slowly so raw pen pressure glides back to 0. One sweep produces one [IAF estimate](#iaf-estimate).

**Push-into-activation sweep** — A single lift-then-press cycle (IAF from below). The user lifts so the scale drops to the **rest floor** ([`MaxRestingGf`](#rest-floor)), then presses slowly and continuously until raw pressure becomes nonzero.

**IAF method** — A picker shown only for **IAF from below** (enum `IafBelowMethod` in [`IafBelowController`](../PenPressureProfiler/Detection/IafBelowController.cs)) that selects how the [activation bracket](#bracket-deltaphys) becomes an estimate. Affects **new captures only**, so methods can be compared in one list:

| Method | One-line summary |
|---|---|
| **Current** | Midpoint of the immediate (last-0% → first-non-zero) bracket. |
| **A: Press-through** | Hold the lower edge, extend the upper edge past activation, then take the midpoint. |
| **B: Regression** *(recommended)* | Least-squares fit through the rising `(gf, raw)` points, extrapolated to onset. |
| **C: Time window** | Midpoint of a ±200 ms time bracket around the crossing. |
| **D: Min-delta** | The Current bracket widened backward until its span reaches 0.5 gf. |

Theory and trade-offs are in [THRESHOLD_METHODS.md](THRESHOLD_METHODS.md). IAF-from-above and MAX use a single fixed algorithm and have no method picker.

---

## IAF / MAX estimation

**<a name="bracket-deltaphys"></a>Bracket / DeltaPhys** — Because the scale samples (~8–15 Hz) far slower than the pen (~130–200 Hz), the exact endpoint force is **bracketed** between two scale readings — a lower edge (last 0%-reading force, `FirstZeroGf`) and an upper edge (first non-zero-reading force, `LastNonZeroGf`). **DeltaPhys** = upper − lower is the bracket width, i.e. the measurement uncertainty. A meaningful (non-zero) bracket needs a *slow, continuous* press through the boundary; pressing-and-holding collapses it to ~0.

**IAF estimate** — One number, in gf, computed from a single sweep via the **scale-aligned bracket → midpoint** described above (`IAF = (lower + upper) / 2`), except the **Regression** method, which reports the fitted `raw = 0` intercept instead. Carries the bracket edges (`FirstZeroGf`, `LastNonZeroGf`, `IafGf`) for display and `Copy`. See [`IafBelowController`](../PenPressureProfiler/Detection/IafBelowController.cs) and [`IafController`](../PenPressureProfiler/Detection/IafController.cs).

**Arm / armed** — Per-controller internal state gating whether the next sweep records an estimate. Each sub-mode auto-arms on its precondition; the **Arm** button force-arms the active sub-mode, bypassing the precondition.

| Sub-mode | Auto-arm condition | Re-arm after a hit |
|---|---|---|
| **IAF from below** ([`IafBelowController`](../PenPressureProfiler/Detection/IafBelowController.cs)) | scale dips to ≤ [`MaxRestingGf`](#rest-floor) **while lifted** (`raw == 0`) | lift below the rest floor again |
| **IAF from above** ([`IafController`](../PenPressureProfiler/Detection/IafController.cs)) | peak scale force reaches `MinPeakGf` (30 gf) | press past `MinPeakGf` again |
| **MAX from below** ([`MaxController`](../PenPressureProfiler/Detection/MaxController.cs)) | next saturation hit will record | a full lift (`raw == 0`) |

**Armed indicator** — Dot + label in the THRESHOLD AUTO-CAPTURE section. Green when the active controller is ready to record its next estimate; gray otherwise. The label text describes what to do to (re-)arm — lift to ≤ 2 gf, press past 30 gf, or lift the pen — depending on sub-mode.

**<a name="rest-floor"></a>Rest floor** (`IafBelowController.MaxRestingGf`, **2.0 gf**) — The force the scale must dip to (while lifted) to arm an IAF-from-below sweep. Arming only while `raw == 0` keeps a light *active* press below the floor from re-arming mid-stroke.

---

## MAX mode

**Push sweep** — A single press-then-lift cycle. The user raises pen pressure until normalized logical pressure reaches 1.0 (saturation), then lifts so raw pressure transitions back to 0. One sweep produces one [MAX estimate](#max-estimate).

**MAX estimate** — One number, in gf, computed from a single push sweep. Method: linear extrapolation across the last two sub-saturated pen samples in `(gf, norm)` space, solving for the gf where `norm` would equal 1.0. Falls back to the last sub-saturated `gf` when the trend is flat, decreasing, or has identical gf values. See [`MaxController.ExtrapolateMax`](../PenPressureProfiler/Detection/MaxController.cs).

**Saturation bracket** — The two pen samples that straddle the moment normalized pressure hit 1.0 (`LastSubMaxNorm`, `LastSubMaxGf`, `FirstAtMaxGf`). Captured on each [MAX estimate](#max-estimate) for diagnostics. Not currently rendered in the UI; reserved for future analysis or save formats.

**Cycle rule** — `MaxController` consumes a cycle on a saturation hit; the next estimate requires a full lift (`RawPressure == 0`) to re-arm. Prevents brief dips out of saturation while still in contact from double-counting.

**Final MAX** — Median of all collected MAX estimates. Collection stops at `MaxController.MaxEstimates = 20`.

**Final IAF** — Median of all collected IAF estimates. Collection stops at `MaxEstimates = 20` (same value in `IafBelowController` and `IafController`; the UI mirror is `ThresholdMaxEstimates = 20`).

---

## Captures

The [captures pane](#modes) holds the active mode's results. Curve captures are the only kind with on-disk persistence.

| | Curve (Stability) | Threshold |
|---|---|---|
| In-memory type | `StabilityCapture` | `IafEstimate` / MAX estimate (gf) |
| Collection | `StabilityController.Captures` | per-controller estimate list |
| Contents | `(physGf, logicalNorm)` plus raw `PenSample[]` (+ legacy `ScaleSample[]`) | one gf value (IAF carries its [bracket](#bracket-deltaphys)) |
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
