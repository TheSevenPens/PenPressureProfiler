# Control Flow

Sequence diagrams for the scenarios most likely to come up when changing the code.
File-line references point at the entry of each step.

Terminology used here is defined in [GLOSSARY.md](GLOSSARY.md).

There are three modes — **Curve** (tab key `"capture"`), **Time series**
(tab key `"timeseries"`) and **Accumulator** (tab key `"accumulator"`) —
selected by the ribbon **MODE** dropdown. Curve and Time series share the same
captures pane and AUTO-CAPTURE controls; the mode itself drives which centre
chart shows (there is no separate chart-type picker, and no standalone Manual or
Monitor mode).

---

## 1. Live pen + scale streams (the always-on path)

This is the pipeline every other flow depends on. Both streams converge on the UI thread before anything else looks at them.

```
WinPenKit (background)         ScaleSessionManager (threadpool)
        │                                  │
        │ packets queued                   │ port.ReadLine()
        ▼                                  │ ScaleLineParser.Parse()
DispatcherTimer 60Hz (UI)                  │
PenSessionManager.OnTick                   │ Dispatcher.UIThread.Post
   session.DrainPoints()                   ▼
   PenButtonTracker.Update            MainWindow.OnScaleReading
   MovingAverage.AddSample              SessionLogger.LogScaleReading
   emit PenReadingData                   if _stabilityEnabled:
        │                                  StabilityController.OnScaleData
        ▼                                if _accumulatorEnabled:
MainWindow.OnPenDataReceived               if _scaleLagComp: FlushPenLagQueue(now-τ)
   SessionLogger.LogPenReading             AccumulatorController.OnScaleData(gf)
   if _stabilityEnabled:                    (counts only; chart refresh below)
     StabilityController.OnPenData        update reading_phys_pressure / reading_scale_rate
   if _accumulatorEnabled:                AppendMonitorScale + RefreshMonitorIfDue
     if _scaleLagComp: queue pen event         (only while monitorView visible)
     else FeedPenToActiveController        if stability OR accum plot visible:
   UpdateRibbon (proximity + readouts +     throttled live-line refresh ~10/150 ms
     hover Z; blank when pen absent)
   UpdateCards   (live readings)
   if _liveFollow: PushLiveTrail +
     throttled scatter refresh
   AppendMonitorPen + RefreshMonitorIfDue
        (only while monitorView visible)
```

Both callbacks are guaranteed to run on the UI thread.
[`PenSessionManager.OnTick`](../PenPressureProfiler/Sessions/PenSessionManager.cs) is the UI-thread handoff for pen data; `ScaleSessionManager.ReadLoopAsync` does `Dispatcher.UIThread.Post` for every parsed line.

When the accumulator is running, `OnScaleReading` and `OnPenDataReceived` cooperate
on a single `AccumulatorController` (see §3a): the scale stream drives the counting,
while the pen stream supplies the on/off state — time-aligned through `_penLagQueue`
when scale-lag compensation is on.

---

## 2. View switching (three modes)

The ribbon VIEW ComboBox picks the mode directly. There is no separate chart-type
picker — the mode is what decides the centre chart. The right panel and the
mode-gated ribbon groups follow.

```
comboBox_view_mode_Changed                ← "Curve" / "Time series" / "Accumulator"
   if panel_right_stability is null: return       (fires during OnOpened init)
   if not "Accumulator": _accumulatorEnabled = false   ← leaving stops counting
   _penLagQueue.Clear()
   "Accumulator"  → SetActiveTab("accumulator")
                             btn_accumulator_enable.Content = Stop/Start
                             RefreshAccumulatorPlot(); UpdateAccumulatorData()
   "Time series"  → SetActiveTab("timeseries"); RefreshCaptureChart()
   else (Curve)   → SetActiveTab("capture");    RefreshCaptureChart()

SetActiveTab(tab)
   capture     = tab == "capture"        ← Curve (scatter)
   timeseries  = tab == "timeseries"
   accumulator = tab == "accumulator"
   _captureTimeSeries = timeseries                    ← mode drives the chart type
   curveLike = capture || timeseries
   panel_right_stability.IsVisible   = curveLike      ← right panels (shared captures)
   panel_right_accumulator.IsVisible = accumulator
   group_curve_capture.IsVisible        = curveLike   ← AUTO-CAPTURE (Edit… + summary), shared
   group_accumulator_settings.IsVisible = accumulator (range + bucket)
   stabilityPlotView.IsVisible = capture              ← Curve scatter
   monitorView.IsVisible       = timeseries           ← Time-series live traces
   accumPlotView.IsVisible     = accumulator
   group_mode_curve.IsVisible       = curveLike       ← MODE: Start + Follow-live/Overlay-traces
   group_mode_accumulator.IsVisible = accumulator     ← MODE: Measure + Start/Clear
   UpdateCaptureViewControls()
```

`UpdateCaptureViewControls` swaps the one option in `group_mode_curve` by mode:
Follow-live (`chk_live_follow`, Curve) vs Overlay-traces (`chk_capture_overlay`,
Time series).

```
RefreshCaptureChart()
   UpdateStabilityData()                       ← captures list (shared by both modes)
   if _captureTimeSeries: ResetMonitor(); RefreshMonitorPlots()   ← traces start fresh
   else:                  RefreshStabilityPlot()
```

`monitorView` is the live scrolling time-series (formerly the standalone
"Monitor" mode); it is now its own top-level mode. The captures pane (list,
Record, save/load) and the AUTO-CAPTURE group are shared between Curve and Time
series — you can record while watching either view. Entering Time series calls
`ResetMonitor` so the rolling traces start at "now".

---

## 3. Curve auto-capture

```
StabilityController.OnPenData(d)         ← called from MainWindow.OnPenDataReceived
   if d.PacketCount == 0: return       (idle tick — don't disturb the window)
   _penWindow.Enqueue(...)             windowDepth = max(5, MinStableMs/21)
   compute penMin, penMax
   penStable    = (penMax-penMin) ≤ PenTolerance
   scaleStable  = (scMax-scMin)  ≤ ScaleTolerance   (needs _scaleWindow.Count ≥ 2)
   // saturated (penMax==1.0) and zero-raw windows are accepted

   if penStable && scaleStable && _lastScaleGf > 0:
     _stableStart ??= now              ← start the "stable for long enough" clock
     if (now - _stableStart) ≥ MinStableMs
        && (now - _lastCaptureTime) ≥ MinGapMs:

        physGf  = scaleWindow.Average
        logNorm = d.SmoothedPressure      ← smoothed pen value (matches the crosshair / manual Record)

        existing = _captures.FirstOrDefault(within tolerances)
        if existing:    existing.Count++ ; fire StableCaptured(existing)
        elif count<MAX: add new StabilityCapture; fire StableCaptured(new)

        _lastCaptureTime = now ; _stableStart = null
   else:
     _stableStart = null               ← any disturbance resets the gate
```

```
StabilityController.OnScaleData(gf)       ← called from MainWindow.OnScaleReading
   _lastScaleGf = gf
   _scaleWindow.Enqueue(...)           windowDepth = max(2, MinStableMs/115 + 1)
   fire RawPairAvailable(gf, penAvg)   ← drives grey dots on the scatter chart
```

MainWindow's reactions to the two events:

| Event | Handler | Does |
|---|---|---|
| `RawPairAvailable` | `OnStabilityRawPair` | append to `_stabilityRawX/Y` (cap 600), `RefreshStabilityPlot` at most every 100ms when the scatter chart is visible |
| `StableCaptured` | `OnStabilityStableCapture` | `RefreshStabilityPlot` + `UpdateStabilityData` (right-panel ListBox + "Count" readout); in Time series (`monitorView` visible) also `AddMonitorCaptureMark` → a red dot (`#DC2626`) on the live traces where the capture landed |

**Record (manual, bypasses detection).**

```
btn_stability_record_Click
   _ = StartScaleIfIdleAsync()              ← start the scale if idle (COM port selected)
   _stabilityController.RecordManual(_physicalPressure, _logicalPressure)
      fires StableCaptured → OnStabilityStableCapture (refresh plot + list)
```

`btn_stability_enable_Click` toggles `_stabilityEnabled` (button label Start/Stop)
and, on Start, also calls `StartScaleIfIdleAsync()`.

---

## 3a. Accumulator (force-bucketed activation %)

The Accumulator counts scale samples into fixed-width force buckets, splitting each
bucket by whether the pen reading is **under** or **at-or-over** the active target's
raw-pressure threshold `T` (IAF `T=1` ≡ pen >0%; Max pressure `T=MaxRawPressure` ≡
pen at 100%). The force where "at-or-over" overtakes "under" (the per-bucket **%**
column crossing ~50%) is the threshold, read directly off the BUCKETS table.
One `AccumulatorController` holds **two target states** (IAF, MaxPressure), each with
its own range, bucket-width set and data; only the active `Target` accumulates.
Within the active target, every bucket width is counted at once; the scale stream
drives the counting and the pen stream supplies the under/at-or-over state.

```
btn_accumulator_enable_Click             ← Start / Stop toggle
   _accumulatorEnabled = !_accumulatorEnabled ; button → Stop/Start
   txt_accum_status.Text updated
   _penLagQueue.Clear()                    ← switch cleanly between runs
   on Start: StartScaleIfIdleAsync()
   RefreshAccumulatorPlot(); UpdateAccumulatorData()

btn_accumulator_clear_Click
   _accumulatorController.Clear()          ← zero all buckets + out-of-range counters
   RefreshAccumulatorPlot(); UpdateAccumulatorData()

comboBox_accum_target_Changed → SetTarget(IAF | MaxPressure); SyncAccumUiToActiveTarget()
   re-points the range/bucket pickers, step, labels, plot + table at the active
   target WITHOUT reconfiguring (each target's data is preserved)

comboBox_accum_bucket_Changed / accum_range_Changed → ApplyAccumulatorConfig()
   width = parsed from the bucket-size picker (active target's set)
   rangeSame = min/max unchanged?
   if rangeSame: _accumulatorController.SetWidth(width)    ← select that layout, NO clear (data preserved)
   else:         _accumulatorController.Configure(min, max, width)   ← rebuild + clear the ACTIVE target's layouts
   InitializeAccumulatorPlot(); RefreshAccumulatorPlot(); UpdateAccumulatorData()

accum_range_Wheel → nudge the field by ±Increment (×5 with Shift); step is
   per-target (1 gf IAF / 50 gf Max)

(scale-lag checkbox) → _scaleLagComp = checkbox.IsChecked ; _penLagQueue.Clear()
```

While running, the two live callbacks cooperate on the one controller:

```
OnScaleReading(record)                     ← drives the counting (one count / sample)
   if _accumulatorEnabled:
      if _scaleLagComp:                     (lag compensation on)
         FlushPenLagQueue(now − τ)          ← release queued pen events older than τ
                                              so the pen state matches the late scale
      if not _accumRequireProximity or _penPresent:   ← proximity gate (Tools ▸ Options; off by default)
         AccumulatorController.OnScaleData(gf)      ← feeds the active target's width layouts
         isUnder = !IsAtOrOver(_lastPenRaw)   (at-or-over = ThresholdRaw>0 && raw ≥ ThresholdRaw)
         foreach layout: layout.Add(gf, isUnder)
            gf < MinGf  → below(<min): BelowUnder/BelowAtOrOver++   ← out-of-range counters
            gf ≥ MaxGf  → above(≥max): AboveUnder/AboveAtOrOver++
            else        → bucket b = (gf−MinGf)/Width;
                          Under[b]++ or AtOrOver[b]++
      (no refresh here — the chart refresh is driven from OnScaleReading below)

OnPenDataReceived(d)                        ← supplies the on/off state only
   if _accumulatorEnabled:
      if _scaleLagComp: _penLagQueue.Add((now, d))           ← held for FlushPenLagQueue
      else              FeedPenToActiveController(d)          ← fed straight through
                          → AccumulatorController.OnPenData(d): _lastPenRaw = d.RawPressure
```

`τ` is the measured scale response lag, `ScaleSessionManager.ResponseLagMs`
(245 ms). With lag compensation on, a pen event is parked in `_penLagQueue` and
only released into the controller once it is older than `τ`, so the on/off state
applied to a scale count reflects what the pen was doing 245 ms earlier — the time
it takes the load to register on the scale. With it off, the freshest pen state is
used directly. `AccumulatorController.OnPenData` only records `_lastPenRaw`; it
never counts — every increment happens on a scale sample.

---

## 3b. Accumulator chart + table refresh

`RefreshAccumulatorIfDue` is invoked from `OnScaleReading` whenever the
accumulator chart is visible — *whether or not* accumulation is running — so the
live force line tracks the scale during exploration too.

```
RefreshAccumulatorIfDue()                  ← throttle: skip if < ~150 ms since last
   _lastAccumRefresh = now
   RefreshAccumulatorPlot()
   UpdateAccumulatorData()

RefreshAccumulatorPlot()
   DrawAccumulatorFractionFit(plt)         ← at-or-over % per bucket (atOrOver / total)
      per-bucket markers, area ∝ sample count (sqrt-scaled confidence)
      (no fit curve / dashed line — threshold is read off the % column)
   dotted 50% reference line
   live vertical force line at _physicalPressure   ← matches Curve mode's crosshair
   axes fixed to [MinGf, MaxGf] × [0, 100]

UpdateAccumulatorData()
   reading_accum_samples.Value   = TotalSamples
   UpdateAccumulatorTable()                ← the BUCKETS table, updated in place
```

`UpdateAccumulatorTable` rebuilds the `_accumRows` list
only when the bucket count changes (`BuildAccumulatorRows`); otherwise it writes the
per-row under/at-or-over counts in place and highlights the cell that the last sample
landed in (`LastChanged`/`LastBucket`/`LastUnderIncremented`). The table has the
in-range buckets plus a `< MinGf` row and a `≥ MaxGf` row for the out-of-range counters.

---

## 4. Stability edit dialog round-trip

```
btn_stability_edit_Click  (right panel "Edit…" button)
   new StabilityEditWindow(_stabilityController.Captures)
      _captures = captures.OrderBy(PhysicalGf).ToList()    ← local copy
      RefreshList()  →  ComputeViolators()  →  RefreshChart()

   await ShowDialog<List<StabilityCapture>?>(this)

      User interactions (all modify the local list only):
         - Right-click list row     → _captures.Remove(row.Capture); RefreshList
         - Delete Selected / Delete → _captures.RemoveAll(selected);  RefreshList
         - Click chart dot          → select nearest row within 15 px
         - Ctrl+click chart dot     → toggle selection
         - Wheel / RMB → chart nav (zoom / reset)

      Done_Click   → Close(_captures)            ← survivors
      Cancel_Click → Close(null)                 ← discard all changes

   result = await
   if result is null:  return                    ← dialog cancelled
   _stabilityController.LoadCaptures(result)         ← replaces in-memory captures
   _stabilityRawX.Clear(); _stabilityRawY.Clear()
   RefreshStabilityPlot()
   UpdateStabilityData()
```

`LoadCaptures` calls `Clear()` first, so the live sweep state (windows, stable-start clock, last-capture time) is reset along with the captures themselves.

---

## 5. Save / load — Stability snapshot

The only persisted artifact is the stability snapshot (`StabilitySnapshotFile`).
There is no separate manual-session format — manual `Record` captures land in the
same capture list and ride the same snapshot.

```
Save (Curve captures pane "Save…")
   btn_stability_save_Click
      if _stabilityController.Captures.Count == 0: return    ← no-op for empty
      if !await EnsureMetadataAsync(): return                ← metadata required before save
      SaveFilePickerAsync (SuggestedFileName = "stability_{id}_{date}.json")
      snapshot = StabilitySnapshotFile.From(_stabilityController.Captures, _metadata)
      JsonSerializer.SerializeAsync(stream, snapshot, JsonWriteOptions)
      (failures → Debug.WriteLine; no on-screen status)

Load (Curve captures pane "Load…")  or drag-and-drop a .json onto the window
   btn_stability_load_Click  /  OnDrop → LoadFromStorageFileAsync(item)
      JsonSerializer.DeserializeAsync<StabilitySnapshotFile>(stream)
      if snapshot.Metadata is set: _metadata = it
      captures = snapshot.ToStabilityCaptures().OrderBy(c => c.PhysicalGf).ToList()
      _stabilityController.LoadCaptures(captures)            ← replaces, doesn't merge
      _stabilityRawX.Clear(); _stabilityRawY.Clear()
      RefreshStabilityPlot(); UpdateStabilityData()
```

Drag-and-drop is wired in the ctor: `AddHandler(DragDrop.DropEvent, OnDrop)`. The
drop handler picks the first `.json` file from the payload and routes it through
`LoadFromStorageFileAsync`, which loads it as a `StabilitySnapshotFile` (same path
as the file picker). The accumulator has its own JSON (`AccumulatorSnapshotFile`,
v2) saved/loaded via the accumulator pane's Save/Load: **both targets** (IAF +
MaxPressure) with all their width layouts, via `AccumulatorController.ExportLayouts`
/ `ImportLayouts(target, …)`. Legacy v1 (single-IAF) and the old `"Saturation"`
target string still load.

---

## 6. Lifecycle (window open → close)

```
Window construction (MainWindow ctor)
   TransparencyLevelHint = Mica → Acrylic → None
   InitializeComponent()
   create PenSessionManager / ScaleSessionManager / SessionLogger
   wire StabilityController events (RawPairAvailable, StableCaptured)
   create AccumulatorController (no events — polled on refresh)
   AddHandler DragDrop.DragOver/Drop
   PenInputSurface.PointerWheelChanged += OnChartAreaWheel
   PenInputSurface.PointerPressed      += OnChartAreaPointerPressed
   ActualThemeVariantChanged → ReapplyChartThemes

OnOpened
   populate ApiCombo from PenSessionFactory.GetAvailableApis() (+ AvaloniaPointer)
   populate comboBox_comport from SerialPort.GetPortNames()
   populate comboBox_tolerancePreset (LOW/MEDIUM/HIGH) + SyncTolerancePresetSelection + UpdateCurveSummary
   populate comboBox_view_mode (Curve / Time series / Accumulator)
   populate comboBox_accum_target (IAF / Max pressure) → PopulateAccumBucketCombo
     (active target's widths + per-target step) + UpdateAccumLabels
   default _metadata.Date/User/Os ; UpdateScaleDot

OnLoaded                              (Background-priority Post)
   InitializeStabilityPlot()           ← scatter chart axes/labels
   InitializeAccumulatorPlot()         ← accumulator chart axes/labels (gf × 0–100%)
   InitializeMonitorPlots()            ← time-series (pen + scale) axes/labels
   RefreshStabilityPlot(); UpdateStabilityData()

ApiCombo.SelectionChanged → StartSession
   _penManager.Stop()                  ← always stop first; safe if already stopped
   _penManager.Start(_apis[selected])
   row_tablet.State = IsRunning ? Active : Inactive

OnClosing
   _penManager.Dispose()  → Stop()
   _scaleManager.Dispose() → cancel CTS, port.Dispose()
   _sessionLogger.Dispose() → flush + close CSVs
```

`StartSession` is the only place a pen session begins; it runs on every API-combo change, including the initial `SelectedIndex = 0` set in `OnOpened`.

---

## 7. Chart wheel zoom + right-click reset

```
ActiveChart()                          ← the chart the pointer overlay acts on
   monitorView.IsVisible    → monitorPenPlot   (time series)
   accumPlotView.IsVisible  → accumPlotView    (accumulator)
   else                     → stabilityPlotView (scatter)

OnChartAreaWheel  → ZoomChartAtPoint(ActiveChart(), …)   ← scroll up = zoom in
OnChartAreaPointerPressed (right button) → reset the active chart's axes:
   monitorView    → RefreshMonitorPlots()     (rolling-window axes)
   accumPlotView  → RefreshAccumulatorPlot()  (fixed gf × 0–100% range)
   else           → RefreshStabilityPlot()    (default calibrated range)
```

The time-series (`monitorView`) plots are created with `userInputEnabled: false`
(fixed rolling window); the scatter and accumulator charts open at a fixed default
range and accept wheel-zoom / right-click-reset.
