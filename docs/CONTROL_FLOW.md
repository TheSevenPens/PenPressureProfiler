# Control Flow

Sequence diagrams for the scenarios most likely to come up when changing the code.
File-line references point at the entry of each step.

Terminology used here is defined in [GLOSSARY.md](GLOSSARY.md).

There are two modes — **Curve** (tab key `"capture"`) and **Threshold** (tab key
`"threshold"`) — selected by the ribbon **MODE** dropdown. Curve has two centre
chart types (scatter plot / time series); there is no longer a separate Manual
or Monitor mode.

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
        ▼                                if _thresholdEnabled (dispatch on _thresholdMode):
MainWindow.OnPenDataReceived              Iaf | IafBelow | Max .OnScaleData
   SessionLogger.LogPenReading           update reading_phys_pressure / reading_scale_rate
   if _stabilityEnabled:                  AppendMonitorScale + RefreshMonitorIfDue
     StabilityController.OnPenData             (only while monitorView visible)
   if _thresholdEnabled (dispatch):        if threshPlotView visible:
     Iaf | IafBelow | Max .OnPenData        throttled chart refresh ~10 fps
   UpdateRibbon (proximity + readouts)      + UpdateThresholdArmedIndicator
   UpdateCards   (live readings)          if stabilityPlotView visible:
   if _liveFollow: PushLiveTrail +          throttled live-line refresh ~10 fps
     throttled scatter refresh
   AppendMonitorPen + RefreshMonitorIfDue
        (only while monitorView visible)
```

Both callbacks are guaranteed to run on the UI thread.
[`PenSessionManager.OnTick`](../PenPressureProfiler/Sessions/PenSessionManager.cs) is the UI-thread handoff for pen data; `ScaleSessionManager.ReadLoopAsync` does `Dispatcher.UIThread.Post` for every parsed line.

The threshold dispatch (`OnScaleReading` / `OnPenDataReceived`) routes to exactly
one controller per `_thresholdMode`: `IafFromAbove → _iafController`,
`IafFromBelow → _iafBelowController`, `MaxFromBelow → _maxController`.

---

## 2. View switching (mode + Curve chart type)

The ribbon VIEW ComboBox picks the mode; for Curve, a second dropdown picks the
centre chart type. The right panel and the mode-gated ribbon groups follow.

```
comboBox_view_mode_Changed                ← "Curve" / "Threshold"
   if panel_right_stability is null: return   (fires during OnOpened init)
   "Threshold" → SetActiveTab("threshold"); RefreshThresholdPlot(); UpdateThresholdData()
   else        → SetActiveTab("capture");   RefreshCaptureChart()

SetActiveTab(tab)
   capture   = tab == "capture"
   threshold = tab == "threshold"
   panel_right_stability.IsVisible = capture        ← right panels
   panel_right_threshold.IsVisible = threshold
   group_curve_capture.IsVisible     = capture      ← mode-gated ribbon groups
   group_threshold_capture.IsVisible = threshold
   threshPlotView.IsVisible          = threshold
   stabilityPlotView.IsVisible = capture && !_captureTimeSeries   ← Curve scatter
   monitorView.IsVisible       = capture &&  _captureTimeSeries   ← Curve time series
   group_view_follow.IsVisible = capture            ← chart-type picker + its option
   UpdateCaptureViewControls()
```

```
comboBox_capture_chart_Changed            ← "Scatter Plot" (0) / "Time series" (1)
   if stabilityPlotView is null: return       (init guard)
   _captureTimeSeries = SelectedIndex == 1
   if panel_right_stability.IsVisible:        (only while Curve is active)
      stabilityPlotView.IsVisible = !_captureTimeSeries
      monitorView.IsVisible       =  _captureTimeSeries
      UpdateCaptureViewControls()             ← Follow-live (scatter) vs Overlay (series)
      RefreshCaptureChart()

RefreshCaptureChart()
   UpdateStabilityData()                       ← captures list (shared by both types)
   if _captureTimeSeries: ResetMonitor(); RefreshMonitorPlots()
   else:                  RefreshStabilityPlot()
```

`monitorView` is the live scrolling time-series (formerly the standalone
"Monitor" mode); it is now just Curve's second chart type. The captures pane
(list, Record, save/load) is shared — you can record while watching either view.

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
        logNorm = penWindow.Average

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
| `StableCaptured` | `OnStabilityStableCapture` | `RefreshStabilityPlot` + `UpdateStabilityData` (right-panel ListBox + unique count) |

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

## 3a. Threshold capture (scale-aligned bracket)

The Threshold tab routes pen + scale data to exactly one controller, picked by
`comboBox_threshold_mode`. All three controllers persist their estimate lists
independently; switching mode sets `_thresholdEnabled = false` and re-renders the
chart against the selected controller's data.

```
comboBox_threshold_mode_Changed
   _thresholdMode = "IAF from below" → IafFromBelow
                    "MAX from below" → MaxFromBelow
                    else             → IafFromAbove
   _thresholdEnabled = false                            (stop any active capture)
   row_iaf_method.IsVisible = (_thresholdMode == IafFromBelow)   ← method picker shown only here
   btn_threshold_enable.Content = "Start"
   RefreshThresholdPlot() + UpdateThresholdData()       (swap to selected controller's data)

comboBox_iaf_method_Changed                              ← IAF-from-below only
   _iafBelowController.Method = Current | PressThrough | Regression | TimeWindow | MinDelta
      (affects NEW captures only — existing estimates untouched, so methods compare)

btn_threshold_enable_Click
   if current controller IsFull: clear it + refresh    (restart from empty)
   toggle _thresholdEnabled; update button label (Start/Stop)
   on Start: StartScaleIfIdleAsync()

btn_threshold_arm_Click                                  ← manual Arm() path
   active controller .Arm()                              (force-arm, bypass auto-arm condition)
   UpdateThresholdArmedIndicator()

OnScaleReading / OnPenDataReceived
   if _thresholdEnabled: dispatch on _thresholdMode → (Iaf|IafBelow|Max).OnScaleData / OnPenData
```

The common shape: **IAF is captured as a scale-aligned bracket** — a lower
(0%-side) force and an upper (non-zero-side) force, a real scale interval apart —
and the reported IAF lies inside it. Capture decisions are made on *scale*
samples. Each controller fires `EstimateAdded`; the UI refreshes (see §3d). Up to
**20** estimates per controller (`MaxEstimates`); the median is highlighted.

The per-controller commit conditions are summarised below. The detailed theory,
the IAF-from-below method variants, rejections, and tunable constants live in
[THRESHOLD_METHODS.md](THRESHOLD_METHODS.md) — not duplicated here.

---

## 3b. IAF from below — push sweep (`IafBelowController`)

Lift to the rest floor (≤ `MaxRestingGf`, 2 gf), then press up slowly through
activation. The bracket is resolved by the selected `IafBelowMethod`.

```
IafBelowController.OnScaleData(gf)        ← scale-aligned; drives capture
   _lastScaleGf = gf; push (now, gf) into _scaleHistory; TrimHistory
   if _lastPenRaw == 0:                    (pen lifted)
      if gf ≤ MaxRestingGf: _armed = true  ← (re-)arm only while lifted
      _zeroForce = gf                       ← freshest 0%-side bracket force
      return
   if !_armed || IsFull: return
   if !_active:                            (first scale sample after pen registered)
      if _zeroForce is null: return
      _active = true; _activationTime = now; _activationZeroForce = _zeroForce
      _activeSamples.Clear()
   _activeSamples.Add((_lastPenRaw, gf))
   if IsComplete(now): CommitSweep()        ← per-method completion (see below)

IafBelowController.OnPenData(d)
   if d.PacketCount == 0: return
   _lastPenRaw = d.RawPressure
   if raw == 0:                             (released)
      if _active: CommitSweep()             ← finalize methods still collecting
   else if (pressed without prior lift) && !_armed:
      fire SweepRejected
```

```
IsComplete(now):  PressThrough/Regression → _lastPenRaw ≥ PressThroughLevels
                  TimeWindow              → elapsed ≥ TimeWindowMs
                  Current/MinDelta        → true (commit on first post-activation sample)

CommitSweep() → EmitCurrent | EmitPressThrough | EmitRegression | EmitTimeWindow | EmitMinDelta
   each calls RecordBracket(zeroGf, nonZeroGf, nonZeroRaw, iaf?)
      iafGf = iaf ?? (zeroGf + nonZeroGf)/2          ← midpoint, or fitted intercept (Regression)
      reject if zeroGf ≤ 0 || nonZeroGf < zeroGf || iafGf ≤ 0  → fire SweepRejected
      else add IafEstimate; fire EstimateAdded
   then _active = false; _armed = false                ← cycle consumed; must lift to re-arm
```

---

## 3c. IAF from above (release) + MAX from below (push)

```
IafController.OnScaleData(gf)             ← release sweep, scale-aligned
   _lastScaleGf = gf
   if _lastPenRaw > 0:                     (pen registering)
      _peakGf = max(_peakGf, gf); if _peakGf ≥ MinPeakGf: _armed = true
      _activeForce = (_lastPenRaw, gf)     ← non-zero ("on") side of the bracket
      return
   if _activeForce is set:                 (first 0%-reading sample after release)
      if _armed && !IsFull && active.Gf < MinPeakGf:
         RecordBracket(zeroGf: gf, nonZeroGf: active.Gf, active.Raw)   ← IAF = midpoint
      else fire SweepRejected              (release "under load" / not armed)
      ResetSweepState()
IafController.OnPenData(d): _lastPenRaw = d.RawPressure   (release captured from scale stream)
```

```
MaxController.OnPenData(d)                 ← saturation, pen-driven; _lastScaleGf paired in
   if d.PacketCount == 0 || IsFull: return
   if raw == 0:  _readyForNextCycle = true; ResetCycle()        ← lift re-arms
   elif norm ≥ SaturationNorm (1.0):        (sub-saturated → saturated transition)
      if _readyForNextCycle && _hasSeenSubMax && _curr is set:
         maxGf = ExtrapolateMax(_prev, _curr)   ← line through last two sub-sat (gf,norm) → norm=1
         add MaxEstimate; fire EstimateAdded; _readyForNextCycle = false
      ResetCycle()
   else:                                    (sub-saturated, nonzero)
      _prev = _curr; _curr = (norm, _lastScaleGf)
      _baselineGf = min(_baselineGf, _lastScaleGf); _hasSeenSubMax = true
MaxController.OnScaleData(gf): _lastScaleGf = gf
```

MAX estimates carry no activation bracket — they render as a plain `gf → %` line.

---

## 3d. Threshold UI refresh + manual record

```
OnIafEstimateAdded / OnIafBelowEstimateAdded / OnMaxEstimateAdded
   → OnAnyThresholdEstimateAdded()
        RefreshThresholdPlot()   ← blue dots, red dashed median, orange live-force line
        UpdateThresholdData()    ← Progress N/20, Median/Min/Max/Avg, estimate cards
        if CurrentThresholdIsFull():     _thresholdEnabled = false; button → "Start"

btn_threshold_record_Click          ← force-record current scale force, bypass detection
   active controller .RecordManual(_physicalPressure)  → fires EstimateAdded → refresh
btn_threshold_clear_Click           ← clear the active controller, refresh
btn_card_delete_Click (✕ on a card) ← CurrentThresholdControllerRemoveAt(card.Index), refresh
btn_threshold_copy_Click            ← BuildThresholdMarkdown(entries) → clipboard
```

Card rendering (`UpdateThresholdData` → `ThresholdEstimateCard`): an IAF estimate
from a real sweep (`LastNonZeroGf > 0`) renders as the three-point progression
`(A gf, 0%) → (B gf, IAF) → (C gf, X%) · DeltaPhys D gf`; MAX and manual records
fall back to a plain `gf → %` line. The `SweepRejected` events are not consumed —
a rejected sweep simply adds no estimate (progress is read from the N/20 readout
and the armed dot, refreshed each scale tick via `UpdateThresholdArmedIndicator`).

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
as the file picker). Threshold estimates are session-only — they are not saved or
loaded.

---

## 6. Lifecycle (window open → close)

```
Window construction (MainWindow ctor)
   TransparencyLevelHint = Mica → Acrylic → None
   InitializeComponent()
   create PenSessionManager / ScaleSessionManager / SessionLogger
   wire StabilityController events (RawPairAvailable, StableCaptured)
   wire Iaf / IafBelow / Max controllers' EstimateAdded → handlers
   AddHandler DragDrop.DragOver/Drop
   PenInputSurface.PointerWheelChanged += OnChartAreaWheel
   PenInputSurface.PointerPressed      += OnChartAreaPointerPressed
   ActualThemeVariantChanged → ReapplyChartThemes

OnOpened
   populate ApiCombo from PenSessionFactory.GetAvailableApis() (+ AvaloniaPointer)
   populate comboBox_comport from SerialPort.GetPortNames()
   populate comboBox_threshold_mode (IAF below / IAF above / MAX below)
   populate comboBox_iaf_method (Current / Press-through / Regression / Time window / Min-delta)
   populate comboBox_tolerancePreset (LOW/MEDIUM/HIGH) + SyncTolerancePresetSelection + UpdateCurveSummary
   populate comboBox_view_mode (Curve / Threshold)
   populate comboBox_capture_chart (Scatter Plot / Time series)
   default _metadata.Date/User/Os ; UpdateScaleDot

OnLoaded                              (Background-priority Post)
   InitializeStabilityPlot()           ← scatter chart axes/labels
   InitializeThresholdPlot()           ← threshold chart axes/labels
   InitializeMonitorPlots()            ← time-series (pen + scale) axes/labels
   RefreshStabilityPlot(); UpdateStabilityData(); UpdateThresholdData()

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
   threshPlotView.IsVisible → threshPlotView   (threshold)
   else                     → stabilityPlotView (scatter)

OnChartAreaWheel  → ZoomChartAtPoint(ActiveChart(), …)   ← scroll up = zoom in
OnChartAreaPointerPressed (right button) → reset the active chart's axes:
   monitorView    → RefreshMonitorPlots()   (rolling-window axes)
   threshPlotView → RefreshThresholdPlot()  (fixed threshold range)
   else           → RefreshStabilityPlot()  (default calibrated range)
```

The time-series (`monitorView`) plots are created with `userInputEnabled: false`
(fixed rolling window); the scatter and threshold charts open at a fixed default
range and accept wheel-zoom / right-click-reset.
