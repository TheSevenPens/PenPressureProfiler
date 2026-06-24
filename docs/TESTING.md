# Testing

The test project (`PenPressureProfiler.Tests`) uses **xUnit** and covers only the
pure-logic classes. UI, session managers, and file I/O are not tested.

```
dotnet test PenPressureProfiler.Tests/PenPressureProfiler.Tests.csproj
```

CI runs this on every release-tag push ([.github/workflows/release.yml](../.github/workflows/release.yml)).
**There is no PR-time CI**, so I should run tests locally before claiming a change is safe.

---

## What's covered

| Test file | Subject | Notable cases |
|---|---|---|
| [`MovingAverageTests`](../PenPressureProfiler.Tests/MovingAverageTests.cs) | `MovingAverage` | Empty average is 0; window slides correctly when full; `Clear` resets both count and average |
| [`ScaleLineParserTests`](../PenPressureProfiler.Tests/ScaleLineParserTests.cs) | `ScaleLineParser.Parse` | Plain number; `g` suffix; `M` suffix; multi-token `"ST,GS   50.00g"`; null; empty; non-numeric (`"OVER"`); original line is preserved; decimal-place (resolution) detection |
| [`AccumulatorControllerTests`](../PenPressureProfiler.Tests/AccumulatorControllerTests.cs) | `AccumulatorController` clear ops | `ClearBucket` zeroes only that bucket; out-of-range index returns false; `ClearBelow` / `ClearAbove` zero the out-of-range counts |

> Removed modes took their tests with them — `PressureRecordCollectionTests` /
> `PressureTestFileTests` went with the old Manual mode, and `IafBracketTests`
> went with the old Threshold (IAF/MAX sweep) mode.

---

## What's not covered

These are intentionally untested because they're hard to test without a UI thread,
a serial port, or a tablet — but the gap is worth knowing about.

| Class | Why testing is hard | What I'd want covered |
|---|---|---|
| `StabilityController` | Uses `DateTime.UtcNow` directly — no clock injection | Stability gating; dedup count increments within tolerances; window-depth math at edge `MinStableMs` values |
| `StabilityEditWindow.ComputeViolators` | Lives inside a `Window` partial class | Pure function — could be extracted to a static method and unit-tested with a `List<StabilityCapture>` |
| `PenSessionManager` | Needs `DispatcherTimer` + a fake `IPenSession` | Idle-tick pressure preservation when `TipDown`; MA clear on button release |
| `ScaleSessionManager` | Needs a real `SerialPort` | n/a — better tested via integration with a loopback serial mock |
| `SessionLogger` | Touches disk + uses `DateTime.Now` | CSV header presence; row format; safe stop when not started |
| Stability file I/O | `JsonSerializer` round-trip | Round-trip of `StabilitySnapshotFile` including raw sample lists |
| `MainWindow` | god class, all UI | nothing realistically — refactor for testability instead |

---

## Recommended next tests (in priority order)

1. **`StabilityController` stability gating.** This is the most behavior-rich piece of pure logic in the app, and bugs here are silent (a missing capture, a duplicate capture). To make this testable, give `StabilityController` an injectable clock (`Func<DateTime>` defaulting to `() => DateTime.UtcNow`) and feed it synthetic pen+scale sequences.
2. **Extract `ComputeViolators` from `StabilityEditWindow`** into a static helper, then test with a hand-built list including: empty list, all-monotonic, single dip, multiple dips, plateaus (equal logical norms — current code does *not* flag a plateau as a violator because of `<`, not `<=`).
3. **`StabilitySnapshotFile` round-trip** — currently the only "this still loads" guarantee comes from running the app (and Curve save/load is the only persisted format now).
4. **`ScaleLineParser` fuzzing** — add a single property-style test that feeds a few hundred randomly-generated tokens and asserts the parser never throws (only returns `Parsed=false`).

---

## Manual smoke checklist

For changes that touch UI or the live pipeline, the things to actually verify before merging:

- Start the app, switch between **WinTab** and **WM_POINTER (Avalonia)** backends (DEVICES → Tablet) — the Tablet status dot should go green for both (assuming a tablet is connected).
- Press the pen — the ribbon **PEN** / **PEN PRESSURE** readouts update; the pressure gauge moves. Proximity shows **Proximity** (orange) in range and **Out** when lifted away; the Tip dot lights only while the tip is down. On WinTab, **Hover Z** shows a value when hovering ("-" on Avalonia). Lift the pen away and confirm the PEN / PEN PRESSURE readouts blank to `--` (no stale values); hold a still press (Avalonia) and confirm pressure stays live.
- Connect the scale, click **Start** (DEVICES → Scale) — **Scale rate** (SCALE PRESSURE) shows non-zero.
- **Curve** mode (scatter plot): click **Start**, dwell at three forces — grey raw dots stream, blue stable dots appear, **Count** increments. **Record** force-captures. **Clear Dots** clears the grey raw dots but keeps recorded captures. **Save…** → reload via drag-drop / **Load…** → captures restore. Toggle **Follow live**.
- **Time series** mode: pen + scale traces scroll live; toggle **Overlay traces**. A stability capture drops a red dot on the traces.
- **Accumulator** mode: pick the **Measure** target (**IAF** default, or **Max pressure**), set the force **Range** and **Bucket** size (defaults: IAF 0–10 gf / 0.5 gf, Max 0–500 gf / 25 gf — edit by typing, arrows, or wheel with **Shift = ×5**; the step is per-target), click **Start**, and sweep force up and down across the range a few times. The **BUCKETS** table fills in per-bucket counts under the fixed **UNDER / OVER / %** headers (target-agnostic; the per-target meaning is in the description line above the table), plus the `< min` / `≥ max` out-of-range rows; the centre chart's markers (sized by sample count) settle around the dotted 50% line, a **live vertical force line** tracks the scale, and the **Samples** readout updates. Once a bucket has ≥ 50 samples its row tints (≤20% → very light blue, ≥80% → very light purple); the active cell flashes orange. Switch **Measure** and confirm each target keeps its own range/buckets/data. Change the **Bucket** size mid-run and confirm the data is preserved; change the **Range** and confirm that resets the active target's data. Right-click a chart node or a BUCKETS row to erase that bucket. Confirm **Clear** resets the counts and chart.
- **Tools ▸ Measure Scale Lag**: open the dialog, tap the pen on the scale ~10× — **Min / Max / Avg / Median** delay readouts populate.
- **Tools ▸ Options**: toggle **Apply scale-lag comp** and **Only record while pen is in proximity** (off by default), click **Done**, and confirm the Accumulator honours the new settings — with proximity on, lifting the pen away stops the buckets filling; **Cancel** / `Esc` discards changes.
- **Edit dialog** (Curve): open with at least one monotonic violator (capture points out of order); confirm orange `⚠` shows and Delete Selected removes them.
- **Logging**: start → press the pen, read the scale → stop → confirm the two CSVs exist in `Documents\PenPressureProfiler\Logs\` with non-zero rows.
- **Chart nav** on the scatter / accumulator charts: scroll-wheel zoom, right-click reset.
