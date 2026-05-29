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

Stored on disk as a **percent** (0–100), held in memory as a **fraction** (0.0–1.0). See [`PressureTestFile.ToRecordCollection`](../PenPressureProfiler/PressureTestFile.cs).

---

## Curve regions

**IAF (Initial Activation Force)** — The lowest physical force at which the pen reports a non-zero logical value. The bottom of the curve. The `IAF` and `IAF Large` axis-range modes zoom here.

**Saturation** — Where logical pressure reaches 100%. Beyond this, the driver clips all higher forces to the same value, so the (physical, logical) mapping becomes ambiguous. Saturated windows *are* captured (used to be excluded) — the user can decide whether to keep them via the Edit dialog.

**Activation-threshold bounce** — Samples that fluctuate between zero and non-zero near IAF. Zero-raw windows *are* captured (used to be excluded) so the activation region shows up in the curve; the Edit dialog flags monotonic violations these tend to produce.

---

## Sweep mode

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

**Stable capture** — One (averaged physical gf, averaged logical norm) pair recorded automatically when all stability conditions hold. Stored as a `SweepCapture` with the raw sample lists that produced it.

**Dedup count** (`SweepCapture.Count`, shown as `×N` in the list) — When a new stable capture lands within tolerance of an existing one, that existing capture's `Count` is incremented instead of adding a duplicate row. So `×3` means "this point was independently re-confirmed twice."

**Monotonic violation** — In `SweepEditWindow`: a capture whose logical norm is *below* the running maximum of all captures with lower physical force. A correctly-functioning pen curve should be monotonically increasing, so violators are surfaced with an orange `⚠` for review/deletion. The check is in [`SweepEditWindow.ComputeViolators`](../PenPressureProfiler/SweepEditWindow.axaml.cs).

---

## IAF mode

**Release sweep** — A single push-then-release cycle. The user raises the pen pressure to at least `MinPeakGf` (30 gf default) and then lifts off so raw pen pressure transitions back to 0. One sweep produces one [IAF estimate](#iaf-estimate).

**Armed** — Internal state of [`IafController`](../PenPressureProfiler/IafController.cs). A sweep becomes armed the first tick its peak gf reaches `MinPeakGf`; only armed sweeps produce an estimate on release.

**IAF estimate** — One number, in gf, computed from a single release sweep. Method: linear extrapolation across the last two non-zero pen samples `(gf, raw)`, solving for the gf where raw would equal 0. Fall-back to `gf` of the last-nonzero sample if the line is flat or rising. See [`IafController.ExtrapolateIaf`](../PenPressureProfiler/IafController.cs).

**Zero-crossing bracket** — Stored on each [IAF estimate](#iaf-estimate). The two pen samples that bracket the moment raw pressure hit 0: the **last-nonzero** sample (`LastNonZeroRaw`, `LastNonZeroGf`) and the **first-zero** sample (raw = 0, `FirstZeroGf`). The IAF estimate itself comes from extrapolating *backward* from the last two nonzero samples, not from this bracket — the bracket is shown for sanity-checking only.

---

## MAX mode

**Push sweep** — A single press-then-lift cycle. The user raises pen pressure until normalized logical pressure reaches 1.0 (saturation), then lifts so raw pressure transitions back to 0. One sweep produces one [MAX estimate](#max-estimate).

**MAX estimate** — One number, in gf, computed from a single push sweep. Method: linear extrapolation across the last two sub-saturated pen samples in `(gf, norm)` space, solving for the gf where `norm` would equal 1.0. Falls back to the last sub-saturated `gf` when the trend is flat, decreasing, or has identical gf values. See [`MaxController.ExtrapolateMax`](../PenPressureProfiler/MaxController.cs).

**Saturation bracket** — Stored on each [MAX estimate](#max-estimate). The two pen samples that bracket the moment normalized pressure hit 1.0: the **last sub-saturated** sample (`LastSubMaxNorm`, `LastSubMaxGf`) and the **first saturated** sample (norm = 1.0, `FirstAtMaxGf`). Shown on the chart as two open squares at the same x.

**Cycle rule** — `MaxController` consumes a cycle on a saturation hit; the next estimate requires a full lift (`RawPressure == 0`) to re-arm. Prevents brief dips out of saturation while still in contact from double-counting.

**Final MAX** — Median of all collected MAX estimates. Auto-MAX mode stops collection at `MaxEstimates = 10` (the `MaxController.MaxEstimates` constant; distinct from `IafController.MaxEstimates`).

**Final IAF** — Median of all collected IAF estimates. Auto-IAF mode stops collection at `MaxEstimates = 10`.

---

## Records vs captures

The app has two **separate** in-memory record types and two **separate** on-disk formats. They don't share storage; there's no "promote sweep capture to manual record" path.

| | Manual mode | Sweep mode |
|---|---|---|
| In-memory type | `PressureRecord` | `SweepCapture` |
| Collection | `PressureRecordCollection` | `SweepController.Captures` |
| Pair only? | Yes — just `(physGf, logical)` | No — also keeps raw `PenSample[]` + `ScaleSample[]` |
| File format | `PressureTestFile` (JSON) | `SweepSnapshotFile` (JSON) |
| File picker | "Save / Load pressure data" | "Save / Load sweep data" |

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

**Manual session file** — JSON, schema = `PressureTestFile`. Records are `[physical_gf, logical_percent]` pairs.

**Sweep snapshot file** — JSON, schema = `SweepSnapshotFile`. Includes every raw pen + scale sample inside each capture's stability window.
