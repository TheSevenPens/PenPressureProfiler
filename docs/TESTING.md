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
| [`IafBracketTests`](../PenPressureProfiler.Tests/IafBracketTests.cs) | `IafBelowController` / `IafController` (scale-bracket IAF capture) | **From below**: slow-press midpoint bracket; capture waits for the post-activation scale sample; arm consumed after a capture; Press-through widens the upper bound; Regression extrapolates `(gf, raw)` to raw 0; rejections for a zero lower bracket, a downstroke (negative DeltaPhys), and a press without arming. **From above**: midpoint bracket; not-armed produces nothing; release-under-load is rejected as a jump. |

> The removed Manual mode took its tests with it — `PressureRecordCollectionTests`
> and `PressureTestFileTests` were deleted along with `PressureRecord` / `PressureTestFile`.

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

- Start the app, switch between **WinTab** and **Avalonia Pointer** backends (DEVICES → Tablet) — the Tablet status dot should go green for both (assuming a tablet is connected).
- Press the pen — the ribbon **PEN** / **PEN PRESSURE** readouts update; the pressure gauge moves.
- Connect the scale, click **Start** (DEVICES → Scale) — **Scale rate** (SCALE PRESSURE) shows non-zero.
- **Curve** mode:
  - *Scatter Plot*: click **Start**, dwell at three forces — grey raw dots stream, blue stable dots appear, **Unique** increments. **Record** force-captures. **Save…** → reload via drag-drop / **Load…** → captures restore.
  - *Time series*: switch the chart-type picker — pen + scale traces scroll; toggle **Overlay traces**.
- **Threshold** mode: pick **IAF from below**, **Start**, and sweep up slowly a few times — estimate cards/dots appear, **Progress / Median / Min / Max / Avg** update. Try the **IAF method** picker and the **Arm** button; **Copy** puts a Markdown table on the clipboard.
- **Edit dialog** (Curve): open with at least one monotonic violator (capture points out of order); confirm orange `⚠` shows and Delete Selected removes them.
- **Logging**: start → press the pen, read the scale → stop → confirm the two CSVs exist in `Documents\PenPressureProfiler\Logs\` with non-zero rows.
- **Chart nav** on the scatter/threshold charts: scroll-wheel zoom, right-click reset.
