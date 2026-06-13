# Threshold Auto-Capture — Methods & Theory (Developer Reference)

This document explains how **Threshold mode** turns pen sweeps into force
estimates, with detailed theory, operation, and trade-offs for each method.
For the end-user summary see [USERMANUAL.md](USERMANUAL.md#threshold-methods).

Code lives in `PenPressureProfiler/Detection/`:

| Controller | Sub-mode | Measures |
|---|---|---|
| `IafBelowController` | **IAF from below** (default) | Initial Activation Force, by pressing **up** into activation |
| `IafController` | **IAF from above** | Initial Activation Force, by **releasing** down through deactivation |
| `MaxController` | **MAX from below** | Saturation force (where logical pressure reaches 100%) |

The UI (`MainWindow.axaml.cs`) feeds both streams to the active controller via
`OnScaleData(double gf)` and `OnPenData(PenReadingData)`, dispatched by
`_thresholdMode`. Each controller raises `EstimateAdded` / `SweepRejected`.

---

## 1. What is being measured

- **IAF (Initial Activation Force)** — the physical force (grams-force) at which
  the pen first reports a non-zero raw value (`raw` 0 → 1). It is the bottom of
  the pressure curve.
- **MAX** — the force at which logical pressure saturates at 100% (`raw` reaches
  the driver's `MaxPressure`). It is the top of the curve.

Together with the **Curve** mode (which samples the middle of the curve),
Threshold mode pins the two endpoints.

---

## 2. The core problem: the scale is the bottleneck

| Stream | Typical rate |
|---|---|
| Pen (`OnPenData`) | ~130–200 Hz |
| Scale (`OnScaleData`) | ~8–15 Hz (one sample every ~70–115 ms) |

The pen's `raw` transition from 0→1 is effectively instantaneous in pen time,
but the scale only reports a new force every ~100 ms. So the activation event is
**bracketed** by two scale readings:

```
   force ↑
        |                         ● first non-zero reading  (upper bracket, C)
        |                   ╱
        |   true IAF  ·····╳·····        (somewhere in here)
        |             ╱
        | ● last 0% reading              (lower bracket, A)
        +───────────────────────────────→ time
```

`DeltaPhys = upper − lower` is the **width of the bracket** — i.e. the
measurement uncertainty. A useful (non-zero) bracket requires the force to be
**changing** as the pen crosses activation. If the user presses to a level and
**holds**, both bracketing scale samples read the same force and `DeltaPhys → 0`.

**Every IAF-from-below method below is a different strategy for turning that
bracket into an estimate.** They all share:

- **Scale-aligned sampling.** Capture decisions happen on *scale* samples, not
  pen ticks, so the two bracket points are a genuine scale interval apart.
- **Midpoint estimate** — `IAF = (lower + upper) / 2` — except **Regression**,
  which uses a fitted intercept.
- **Rejections.** A sweep is dropped (fires `SweepRejected`) when:
  - `lower ≤ 0` — no real 0%-reading force was captured (press happened entirely
    between two scale samples);
  - `upper < lower` — a *downstroke* (force fell instead of rose); or
  - `IAF ≤ 0`.

---

## 3. IAF from below — shared mechanics

State machine in `IafBelowController`:

1. **Arm.** While the pen reads `raw == 0`, every scale sample updates
   `_zeroForce` (the latest 0%-side force). Dipping to `gf ≤ MaxRestingGf`
   (default **2.0 gf**) sets `_armed`. Arming only while lifted (`raw == 0`)
   prevents a light *active* press (< 2 gf) from re-arming mid-stroke.
2. **Activate.** When the pen registers and the first scale sample arrives while
   active, the "active press" opens: `_activationZeroForce` = the last 0% force,
   and `(raw, gf)` samples start accumulating.
3. **Commit.** The active **method** decides when enough is collected
   (`IsComplete`) and computes the bracket (`CommitSweep`).
4. **Consume.** After committing, `_armed = false` — the user must lift below the
   rest floor again (or press the **Arm** button) to start the next sweep.

`Method` (enum `IafBelowMethod`) selects the estimator. It affects **new
captures only**, so methods can be compared on the same list.

---

## 4. IAF-from-below methods

> Notation: **A** = lower (0%-side) force, **C** = upper (non-zero-side) force,
> **B** = reported IAF. `DeltaPhys = C − A`.

### `Current` — immediate bracket (default)

- **Operation.** Commits on the *first* scale sample after activation.
  `A = _zeroForce` (last 0% force), `C` = that sample's force, `B = (A+C)/2`.
- **Theory.** Lowest latency; the activation lies between the last-0% and
  first-non-zero scale samples, so their midpoint is the best zero-extra-work
  estimate.
- **Pros.** Simplest; immediate; no dependence on press technique past the first
  post-activation sample.
- **Cons.** `DeltaPhys` collapses to ~0 whenever the force is steady across the
  ~100 ms activation boundary (press-and-hold, or a press slower than the
  scale's per-sample resolution). Then `B ≈ C` and the bracket conveys nothing.
- **DeltaPhys.** ~0 unless the force happened to change between those two
  consecutive scale samples.

### `A: Press-through`

- **Operation.** Hold `A = _zeroForce`, but keep extending `C` until the pen
  presses through to `raw ≥ PressThroughLevels` (default **5**) — or the pen is
  released. `B = (A+C)/2`.
- **Theory.** Force the press to continue so the upper bound is a genuinely
  higher, *distinct* force, guaranteeing a non-zero span.
- **Pros.** Always yields a non-zero `DeltaPhys`; conceptually simple.
- **Cons.** **Biases the IAF upward** — the midpoint of a wide bracket whose
  upper edge is well past activation sits above the true onset. Requires pressing
  firmly past activation; sensitive to how far/fast you press.
- **DeltaPhys.** Always > 0 on a real press (≈ force gained over levels 1→5).

### `B: Regression` — *recommended*

- **Operation.** Accumulate `(gf, raw)` over the rise until `raw ≥
  PressThroughLevels`. Least-squares fit `gf = m·raw + b`; the IAF is **`b`**
  (the force at `raw = 0`, i.e. the activation onset just *below* first
  detection). Bracket reported as `[B = b, C = first non-zero force]`. Falls back
  to `Current` if there are < 2 samples or the slope is non-positive.
- **Theory.** The pen's `raw` levels (1, 2, 3 …) are a far finer signal than the
  scale's *time* resolution. Several `(gf, raw)` points taken across *distinct*
  scale samples let us fit the local slope of the force→level relationship and
  extrapolate down to the true onset. This is the only method that estimates IAF
  *below* the first detected force rather than at/above it.
- **Pros.** Most accurate; exploits the finest available signal; `DeltaPhys` is
  meaningful (distance from first detection down to the extrapolated onset);
  robust to exactly where activation fell between two scale samples.
- **Cons.** Needs a **slow, continuous** press through the first several levels
  at *different* forces. A fast jab gives only one or two scale samples at
  `raw ≥ 5` → degenerate fit → falls back to `Current`. Assumes the force→level
  relationship is locally linear near activation.
- **DeltaPhys.** `C − b` (> 0 when the fit extrapolates below first detection).

### `C: Time window`

- **Operation.** From the rolling scale history: `A` = force at
  `activation − TimeWindowMs` (default **200 ms**), `C` = force at
  `activation + TimeWindowMs`. Commits once the window has elapsed (or on
  release). `B = (A+C)/2`.
- **Theory.** Bracket a fixed *time* interval centred on the crossing; if the
  force is moving, the two ends differ by (press speed × 2 · window).
- **Pros.** Simple; symmetric in time; independent of raw levels.
- **Cons.** The window is arbitrary. Collapses toward 0 if the force is held.
  A slow press can reach back to rest on the lower side (`A ≈ 0` → **rejected**);
  adds up to one window of latency.
- **DeltaPhys.** ∝ press speed × 2 · window.

### `D: Min-delta`

- **Operation.** Start from the `Current` bracket; if `C − A < MinDeltaGf`
  (default **0.5 gf**), walk `A` backward through the scale history to an earlier
  (lower) force until the span reaches the floor (or history runs out).
  `B = (A+C)/2`.
- **Theory.** Cosmetically guarantee a minimum bracket width by reaching back to
  an earlier force; the upper (measured first-non-zero) edge is untouched.
- **Pros.** Guarantees `DeltaPhys ≥ MinDeltaGf`; preserves the measured `C`.
- **Cons.** The lower bound becomes "force *N* ms before activation," not a real
  0% boundary, so `B` is biased **low** and the value is less principled. Does
  **not** improve accuracy — only the displayed span.
- **DeltaPhys.** ≥ `MinDeltaGf` when history allows.

### Choosing a method

| Goal | Method |
|---|---|
| Most accurate IAF | **B: Regression** (with a slow continuous press) |
| Raw, lowest-latency reading | **Current** |
| Just want a visible non-zero span | **A: Press-through** or **D: Min-delta** |
| Time-based bracketing experiment | **C: Time window** |

All methods need a **slow, continuous press through the threshold** to produce a
meaningful bracket; press-and-hold defeats the bracket regardless of method.

---

## 5. IAF from above (`IafController`)

- **Gesture.** Press above `MinPeakGf` (default **30 gf**), then **release**
  slowly to zero.
- **Bracket (scale-aligned, on release).** `C` = the last on-force scale sample
  (with its `raw`), `A` = the first 0%-reading scale sample after release.
  `IAF = (A+C)/2`.
- **Arming.** Set once the peak scale force during the press reaches `MinPeakGf`.
- **Rejections.** A release while still under load (last on-force ≥ `MinPeakGf`,
  i.e. the pen jumped to zero rather than gliding off) is rejected as a *jump*;
  non-positive or inverted brackets are rejected.
- **Theory / trade-off.** Approaching from above catches **deactivation** rather
  than first detection, sidestepping the "first-detection overshoot." It needs a
  controlled glide-off rather than a lift. Single algorithm — not user-selectable
  (the method picker applies only to IAF-from-below).

---

## 6. MAX from below (`MaxController`)

- **Gesture.** Push until logical pressure reads 100% (`raw` → driver
  `MaxPressure`), then lift.
- **Estimate.** As normalized pressure crosses from < 1.0 to ≥ 1.0
  (`SaturationNorm`), extrapolate the `(gf, norm)` trend to `norm = 1.0` for the
  saturation force.
- **Arming.** Re-arms on a full lift (`raw → 0`); at most one estimate per push
  cycle (a dip back into sub-saturation while still in contact does not re-arm).

---

## 7. Manual arming

Each controller exposes `bool Armed` and `void Arm()`. The **Arm** button in the
THRESHOLD AUTO-CAPTURE ribbon section force-arms the active mode, bypassing its
auto-arm condition — useful when that condition is awkward to hit (e.g. the
from-below rest floor, or the from-above peak force).

---

## 8. How a capture is displayed

IAF cards render the three-point progression plus the bracket width:

```
(A gf, 0%)  →  (B gf, IAF)  →  (C gf, X%)   ·   DeltaPhys D gf
```

- **A** — lower (0%-reading) force (`IafEstimate.FirstZeroGf`)
- **B** — the IAF estimate (`IafEstimate.IafGf`)
- **C** — upper (non-zero-reading) force (`IafEstimate.LastNonZeroGf`)
- **X%** — the logical reading at C (`LastNonZeroRaw / MaxPressure × 100`, shown
  with enough precision never to round a non-zero level to `0.00`)
- **D** — `C − A`, the bracket width

MAX estimates and manually-recorded estimates (no sweep bracket) fall back to a
plain `gf → %` line. The **Copy** button exports the list as a Markdown table,
including the bracket columns for IAF.

---

## 9. Tunable constants

| Constant | Location | Default | Meaning |
|---|---|---|---|
| `MaxRestingGf` | `IafBelowController` | 2.0 gf | Rest floor that (re-)arms a from-below sweep |
| `ActivationRaw` | `IafBelowController` | 1 | Smallest meaningful non-zero `raw`; used for boundary labels |
| `PressThroughLevels` | `IafBelowController` | 5 | Target `raw` for the Press-through / Regression methods |
| `TimeWindowMs` | `IafBelowController` | 200 ms | Half-window for the Time-window method |
| `MinDeltaGf` | `IafBelowController` | 0.5 gf | Minimum bracket span for the Min-delta method |
| `MinPeakGf` | `IafController` | 30 gf | Peak force that arms a from-above release sweep |
| `MaxEstimates` | all three controllers | 20 | Estimates collected before capture stops |
| `ThresholdMaxEstimates` | `MainWindow.axaml.cs` | 20 | UI mirror (progress readout + chart x-axis) |

---

## 10. Tests

`PenPressureProfiler.Tests/IafBracketTests.cs` covers, per direction:

- **Current**: slow-press midpoint bracketing; capture timing (waits for the
  post-activation scale sample); arm consumption.
- **Press-through** and **Regression**: widened bracket and raw-0 extrapolation.
- Rejections: downstroke (negative `DeltaPhys`), zero lower bracket,
  press-without-arming.
- **From above**: midpoint bracket, not-armed, release-under-load rejection.
