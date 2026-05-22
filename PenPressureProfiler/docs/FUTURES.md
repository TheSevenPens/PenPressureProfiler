# PenPressureProfiler — Future Work

## Immediate: Extract to Own Repository

The project has no remaining in-solution dependencies. It is ready to move out of `WinTabPainter` into its own repo.

**What's needed:**
- Create a new git repo (e.g., `PenPressureProfiler`).
- Move `PenPressureProfiler/` and `libs/WinPenKit/` into it.
- Update the `HintPath` in the csproj from `..\libs\...` to `.\libs\...` (or wherever libs lands).
- Confirm solution file (`WinTabPainter.slnx`) still builds the remaining projects without this one.

---

## Near-Term

### Code quality
- Fix the 4 pre-existing nullable warnings in `ScaleRecord.cs` and `ScaleParsedLine.cs` (add `required` modifier or make properties nullable).
- Replace hand-rolled JSON serialization in `CreateJSONContent()` with `System.Text.Json` source generation — consistent with how deserialization already works.
- `PressureTestData` class is `internal` and buried at the bottom of `MainWindow.axaml.cs` — move to its own file.

### MVVM
*Considered and deliberately skipped.* The logic layer (`ScaleLineParser`, `MovingAverage`, `PressureRecordCollection`, `PressureTestFile`) is already clean and independently testable — the main MVVM benefit is already achieved. For this app, MVVM would add INotifyPropertyChanged/RelayCommand ceremony without meaningful payoff: the 60fps pen poll loop is wasteful to route through property-change notifications, session lifecycle is entangled with async error dialogs (which ViewModels handle awkwardly), and the chart is a pure View concern regardless.

Revisit if the app grows to multiple windows, requires live data bindings, or needs a headless test harness for UI state. The chosen alternative was **session manager extraction** — `PenSessionManager` and `ScaleSessionManager` reduce `MainWindow.axaml.cs` to a coordinator without adding binding infrastructure.

### Timestamps
Add a timestamp to each `PressureRecord` so the JSON export can include when each point was captured.

### CSV export
Export records as CSV in addition to JSON for compatibility with spreadsheet tools.

---

## Medium-Term

### Configuration persistence
Save and restore metadata field values (user name, tablet, driver, OS) between sessions so they don't need to be re-entered on every run.

### Multiple pen inputs
Support `InputApi.WmPointer` as an alternative input path (for tablets that don't use WinTab). WinPenKit already supports it — just needs a UI selector and a session swap.

### Graph interaction
ScottPlot supports pan/zoom — currently disabled (`UserInputProcessor.IsEnabled = false`). Could be re-enabled optionally, e.g., via a toggle button, for more detailed inspection.

### Overlay / comparison
Load two JSON files and overlay their curves on the same chart for side-by-side pen comparison.

---

## Long-Term

### WinPenKit updates
WinPenKit is vendored at v0.2.0 as a DLL. When WinPenKit publishes to NuGet, switch to a `PackageReference`. Track upstream releases and update as new input paths or fixes land.

### Non-serial scale support
Some scales use USB HID instead of serial COM port. Abstract the scale input behind an interface so different scale backends can be plugged in.

### Report generation
Generate a formatted PDF or HTML report from a JSON file including the chart image, metadata table, and key statistics (IAF, saturation pressure, mid-range linearity).
