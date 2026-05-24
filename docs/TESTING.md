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
| [`ScaleLineParserTests`](../PenPressureProfiler.Tests/ScaleLineParserTests.cs) | `ScaleLineParser.Parse` | Plain number; `g` suffix; `M` suffix; multi-token `"ST,GS   50.00g"`; null; empty; non-numeric (`"OVER"`); original line is preserved |
| [`PressureRecordCollectionTests`](../PenPressureProfiler.Tests/PressureRecordCollectionTests.cs) | `PressureRecordCollection` | Add / Clear / RemoveLast; `ToRecordArrays` scales logical fraction → percent |
| [`PressureTestFileTests`](../PenPressureProfiler.Tests/PressureTestFileTests.cs) | `PressureTestFile` JSON | Round-trip preserves all fields; lower-case JSON property names; `ToRecordCollection` scales percent → fraction; skips records with <2 values; reads legacy hand-rolled JSON |

---

## What's not covered

These are intentionally untested because they're hard to test without a UI thread,
a serial port, or a tablet — but the gap is worth knowing about.

| Class | Why testing is hard | What I'd want covered |
|---|---|---|
| `SweepController` | Uses `DateTime.UtcNow` directly — no clock injection | Stability gating; dedup count increments within tolerances; `penSaturated` exclusion; `penHasZeroRaw` exclusion; window-depth math at edge `MinStableMs` values |
| `SweepEditWindow.ComputeViolators` | Lives inside a `Window` partial class | Pure function — could be extracted to a static method and unit-tested with a `List<SweepCapture>` |
| `PenSessionManager` | Needs `DispatcherTimer` + a fake `IPenSession` | Idle-tick pressure preservation when `TipDown`; MA clear on button release |
| `ScaleSessionManager` | Needs a real `SerialPort` | n/a — better tested via integration with a loopback serial mock |
| `SessionLogger` | Touches disk + uses `DateTime.Now` | CSV header presence; row format; safe stop when not started |
| Sweep file I/O | `JsonSerializer` round-trip | Round-trip of `SweepSnapshotFile` including raw sample lists |
| `MainWindow` | god class, all UI | nothing realistically — refactor for testability instead |

---

## Recommended next tests (in priority order)

1. **`SweepController` stability gating.** This is the most behavior-rich piece of pure logic in the app, and bugs here are silent (a missing capture, a duplicate capture). To make this testable, give `SweepController` an injectable clock (`Func<DateTime>` defaulting to `() => DateTime.UtcNow`) and feed it synthetic pen+scale sequences.
2. **Extract `ComputeViolators` from `SweepEditWindow`** into a static helper, then test with a hand-built list including: empty list, all-monotonic, single dip, multiple dips, plateaus (equal logical norms — current code does *not* flag a plateau as a violator because of `<`, not `<=`).
3. **`SweepSnapshotFile` round-trip** — analogous to `PressureTestFileTests`. Currently the only "this still loads" guarantee comes from running the app.
4. **`ScaleLineParser` fuzzing** — add a single property-style test that feeds a few hundred randomly-generated tokens and asserts the parser never throws (only returns `Parsed=false`).

---

## Manual smoke checklist

For changes that touch UI or the live pipeline, the things to actually verify before merging:

- Start the app, switch between **WinTab** and **Avalonia Pointer** backends — `dot_pen` should go green for both (assuming tablet is connected).
- Press the pen — Pressure card readings and ribbon update; pressure bar moves.
- Connect the scale, click **Read** — Scale rate shows non-zero.
- **Manual**: Ctrl+R a few times at different forces, check chart updates and `listBox_records` populates. Ctrl+S → reload via drag-drop or Load… → values restore.
- **Sweep**: Start Auto-Capture, dwell at three different forces — grey raw dots stream onto sweep chart, blue stable dots appear, count increments in the right panel.
- **Edit dialog**: open with at least one obviously-monotonic violator (record points in reverse order); confirm orange `⚠` shows and Delete Selected removes them.
- **Logging**: start → press the pen, write to scale → stop → confirm the two CSVs exist in `Documents\PenPressureProfiler\Logs\` with non-zero rows.
- **Chart nav** on both charts: scroll-wheel zoom, Space+drag pan, right-click reset.
