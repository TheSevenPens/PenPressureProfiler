# PenPressureProfiler — Future Work

## Near-Term

### MVVM
*Considered and deliberately skipped.* The logic layer (`ScaleLineParser`, `MovingAverage`, `PressureRecordCollection`, `PressureTestFile`) is already clean and independently testable — the main MVVM benefit is already achieved. For this app, MVVM would add INotifyPropertyChanged/RelayCommand ceremony without meaningful payoff: the 60fps pen poll loop is wasteful to route through property-change notifications, session lifecycle is entangled with async error dialogs (which ViewModels handle awkwardly), and the chart is a pure View concern regardless.

Revisit if the app grows to multiple windows, requires live data bindings, or needs a headless test harness for UI state. The chosen alternative was **session manager extraction** — `PenSessionManager` and `ScaleSessionManager` reduce `MainWindow.axaml.cs` to a coordinator without adding binding infrastructure.

### Timestamps on manual records
Add a timestamp to each `PressureRecord` so the JSON export can include when each point was captured. Deferred because the JSON format is consumed by other tools — changing the schema needs coordination.

### CSV export
Export records as CSV in addition to JSON for compatibility with spreadsheet tools.

### Sweep stability — configurable zero-raw guard
The current capture guard rejects any stability window that contains a pen sample with `RawPressure == 0`. This prevents 0↔1 bounce captures. In future this could be a configurable tolerance (e.g., allow up to N zero-raw samples in a window of M) for users working at the very edge of the activation threshold.

### Sweep output — promote to manual collection
Add a button in Sweep Data to promote selected stable captures to the manual recording collection, so they feed into the main chart and JSON export.

---

## Medium-Term

### Configuration persistence
Save and restore metadata field values (user name, tablet, driver, OS) between sessions so they don't need to be re-entered on every run.

### Multiple pen input paths
`AvaloniaPointerSession` is already wired up as the "Avalonia Pointer" option in the API dropdown. It works for tablets configured to use **Windows Ink mode** (WM_POINTER / PT_PEN). Tablets running in WinTab mode deliver events to Avalonia as `PointerType.Mouse` — the `AvaloniaPointerSession` filter drops them silently.

**Requirement**: to use Avalonia Pointer, enable "Use Windows Ink" in the tablet driver (e.g. Wacom Tablet Properties → pen → Use Windows Ink). The ComboBox tooltip in the UI explains this. No code change is needed once the driver is configured.

### Graph interaction
ScottPlot supports pan/zoom — currently disabled (`UserInputProcessor.IsEnabled = false`). Could be re-enabled optionally, e.g., via a toggle button, for more detailed inspection.

### Overlay / comparison
Load two JSON files and overlay their curves on the same chart for side-by-side pen comparison.

---

## Long-Term

### WinPenKit — switch to NuGet
WinPenKit is vendored at v0.2.0 as a DLL (`libs/WinPenKit/v0.2.0/`). When WinPenKit publishes to NuGet, switch to a `PackageReference`. Track upstream releases and update as new input paths or fixes land.

### Non-serial scale support
Some scales use USB HID instead of serial COM port. Abstract the scale input behind an interface so different scale backends can be plugged in without changing the rest of the app.

### Report generation
Generate a formatted PDF or HTML report from a JSON file including the chart image, metadata table, and key statistics (IAF, saturation pressure, mid-range linearity).

### Extrapolation of saturated pressure range
Captures at logical pressure = 100% are currently excluded (pen clips all forces above its maximum to 100%). Future work: use the curve shape below saturation to extrapolate where the physical force would have continued rising.
