# Control Flow

Sequence diagrams for the scenarios most likely to come up when changing the code.
File-line references point at the entry of each step.

Terminology used here is defined in [GLOSSARY.md](GLOSSARY.md).

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
        ▼                                if _thresholdEnabled:
MainWindow.OnPenDataReceived              (Iaf|Max)Controller.OnScaleData
   SessionLogger.LogPenReading           update reading_phys_pressure
   if _stabilityEnabled:                     update reading_scale_rate
     StabilityController.OnPenData           (if Threshold tab visible:
   if _thresholdEnabled:                  throttled chart refresh ~10 fps
     (Iaf|Max)Controller.OnPenData        so the live orange line tracks)
   UpdateRibbon (proximity + readouts)
   UpdateCards   (live readings)
```

Both callbacks are guaranteed to run on the UI thread.
[`PenSessionManager.OnTick`](../PenPressureProfiler/Sessions/PenSessionManager.cs) is the UI-thread handoff for pen data; `ScaleSessionManager.ReadLoopAsync` does `Dispatcher.UIThread.Post` for every parsed line.

---

## 2. Manual record

```
Click "Record" button
   ▼
btn_record_Click
   _recordCollection.Add(_physicalPressure, _logicalPressure)
      │  _physicalPressure  ← most recent OnScaleReading
      │  _logicalPressure   ← most recent OnPenDataReceived (SmoothedPressure)
   UpdateChart()
      plt.Clear() + Add.Scatter(...)
      ApplyAxisRange()                ← honors comboBox_axis_range mode
      UpdateChartTitle()              ← BRAND/ID/DATE in title
      plotView.Refresh()
      listBox_records.ItemsSource = _recordCollection.Items
      txt_record_count.Text = "N records"
```

Key point: there's no buffering between the live stream and the recorded point. `_physicalPressure` and `_logicalPressure` are just the last-seen values, so clicking **Record** *samples* whatever's on screen.

---

## 3. Stability auto-capture

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
   fire RawPairAvailable(gf, penAvg)   ← drives grey dots on Stability chart
```

MainWindow's reactions to the two events:

| Event | Handler | Does |
|---|---|---|
| `RawPairAvailable` | `OnStabilityRawPair` | append to `_stabilityRawX/Y` (cap 600), `RefreshStabilityPlot` at most every 100ms |
| `StableCaptured` | `OnStabilityStableCapture` | `RefreshStabilityPlot` + `UpdateStabilityData` (right-panel ListBox + count) |

---

## 3a. Threshold capture (Auto-IAF / Auto-MAX)

The Threshold tab routes pen + scale data to exactly one of two controllers,
picked by `comboBox_threshold_mode`. Both controllers persist their estimate
lists independently; switching mode sets `_thresholdEnabled = false` and
re-renders the chart against the other controller's data.

```
comboBox_threshold_mode_Changed
   _thresholdMode = (selected == "MAX from below") ? MaxFromBelow : IafFromAbove
   _thresholdEnabled = false                            (stop any active capture)
   btn_threshold_enable.Content = ThresholdStartLabel() ("Start Auto-{IAF,MAX}")
   RefreshThresholdPlot() + UpdateThresholdData()       (swap to other controller's data)

btn_threshold_enable_Click
   if current controller IsFull: clear it first         (restart from empty)
   toggle _thresholdEnabled
   update button label

OnPenDataReceived / OnScaleReading
   if _thresholdEnabled:
     mode == IafFromAbove → IafController.OnPenData / OnScaleData
     mode == MaxFromBelow → MaxController.OnPenData / OnScaleData
```

Below: the per-controller logic. Only one is fed at a time.



```
IafController.OnPenData(d)           ← called from MainWindow.OnPenDataReceived
   if d.PacketCount == 0: return       (idle tick)
   if IsFull:              return       (10 estimates reached)

   if d.RawPressure > 0:
     _prev = _curr
     _curr = (raw, _lastScaleGf)
     if _lastScaleGf > _peakGf: _peakGf = _lastScaleGf
     if _peakGf >= MinPeakGf:   _armed  = true

   else if _curr is not null:           (raw transitioned nonzero → zero)
     if _armed:
       iafGf = ExtrapolateIaf(_prev, _curr)     ← line through last two
                                                  nonzero (gf, raw) samples,
                                                  solve for raw = 0;
                                                  fallback to _curr.Gf when
                                                  flat/rising/identical-gf
       _estimates.Add(IafEstimate(now, iafGf, _peakGf))
       fire EstimateAdded
     else:
       fire SweepRejected                       (≥30 gf was never reached)

     reset _prev, _curr, _peakGf, _armed

IafController.OnScaleData(gf)         ← called from MainWindow.OnScaleReading
   _lastScaleGf = gf                   (no further state — IAF is computed
                                        at the pen transition tick)
```

`MainWindow.OnIafEstimateAdded`, `OnIafBelowEstimateAdded`, and
`OnMaxEstimateAdded` all delegate to `OnAnyThresholdEstimateAdded()`, which
refreshes the chart, updates the list, and on the 10th estimate auto-stops by
setting `_thresholdEnabled = false`. The controllers' `SweepRejected` events
are no longer consumed — a rejected sweep simply adds no estimate (progress is
read from the Progress N/10 reading and the armed dot).

---

## 3b. Auto-MAX capture

```
MaxController.OnPenData(d)           ← called from MainWindow.OnPenDataReceived
   if d.PacketCount == 0: return
   if IsFull:              return

   if d.RawPressure == 0:
     _readyForNextCycle = true        (lift detected — arm next approach)
     reset _prev, _curr, _baselineGf, _hasSeenSubMax

   elif d.NormalizedPressure >= 1.0:
     if _readyForNextCycle and _hasSeenSubMax and _curr is not null:
       maxGf = ExtrapolateMax(_prev, _curr)   ← line through last two
                                                sub-saturated (gf, norm)
                                                samples, solve for norm = 1.0
       _estimates.Add(MaxEstimate(now, maxGf, _baselineGf, …))
       fire EstimateAdded
       _readyForNextCycle = false             (must lift to re-arm)
     reset _prev, _curr, _baselineGf, _hasSeenSubMax

   else:                              (sub-saturated, nonzero)
     _prev = _curr
     _curr = (norm, _lastScaleGf)
     if _lastScaleGf < _baselineGf: _baselineGf = _lastScaleGf
     _hasSeenSubMax = true

MaxController.OnScaleData(gf) is identical to IafController's — store
_lastScaleGf for pairing.
```

Same UI path as IAF — `OnMaxEstimateAdded` delegates to
`OnAnyThresholdEstimateAdded()`. No equivalent of `SweepRejected`.

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

## 5. Save / load — manual session

```
Save ("Save…" button)
   btn_save_Click
      tl.StorageProvider.SaveFilePickerAsync(...)
         SuggestedFileName = "{inventoryid}_{date}.json"
      BuildTestFile()  ← gathers all metadata TextBoxes + ToRecordArrays()
      JsonSerializer.SerializeAsync(stream, file, JsonWriteOptions)
      (failures → Debug.WriteLine; no on-screen status)

Load ("Load…")  or drag-and-drop JSON onto window
   btn_load_Click  /  OnDrop
   LoadFromStorageFileAsync(item)
      JsonSerializer.DeserializeAsync<PressureTestFile>(stream)
      _recordCollection = data.ToRecordCollection()    ← percent → fraction
      populate all metadata TextBoxes
      UpdateChart()
```

Drag-and-drop is wired in the ctor: `AddHandler(DragDrop.DropEvent, OnDrop)`. The drop handler picks the first `.json` file from the drop payload and routes it through the same `LoadFromStorageFileAsync` as the file picker.

---

## 6. Save / load — Stability snapshot

```
Save (Stability panel "Save…")
   btn_stability_save_Click
      if _stabilityController.Captures.Count == 0: return    ← no-op for empty
      SaveFilePickerAsync (SuggestedFileName = "sweep_{id}_{date}.json")
      snapshot = StabilitySnapshotFile.From(_stabilityController.Captures)
      JsonSerializer.SerializeAsync(stream, snapshot, JsonWriteOptions)

Load (Stability panel "Load…")
   btn_stability_load_Click
      OpenFilePickerAsync
      JsonSerializer.DeserializeAsync<StabilitySnapshotFile>(stream)
      captures = snapshot.ToStabilityCaptures().OrderBy(c => c.PhysicalGf).ToList()
      _stabilityController.LoadCaptures(captures)            ← replaces, doesn't merge
      _stabilityRawX.Clear(); _stabilityRawY.Clear()
      RefreshStabilityPlot(); UpdateStabilityData()
```

Stability save/load is independent of manual save/load — two separate file pickers, two separate JSON schemas. There is no "save everything" combined format.

---

## 7. Lifecycle (window open → close)

```
Window construction (MainWindow ctor)
   TransparencyLevelHint = Mica → Acrylic → None
   InitializeComponent()
   create PenSessionManager / ScaleSessionManager / SessionLogger
   wire StabilityController events
   AddHandler KeyDown/KeyUp (tunnel)  ← so we see keys before children
   AddHandler DragDrop.DragOver/Drop
   PenInputSurface.PointerWheelChanged += OnChartAreaWheel
   PenInputSurface.PointerMoved        += OnChartAreaPointerMoved
   PenInputSurface.PointerPressed      += OnChartAreaPointerPressed

OnOpened
   populate ApiCombo from PenSessionFactory.GetAvailableApis() (+ AvaloniaPointer)
   populate comboBox_comport from SerialPort.GetPortNames()
   populate axis range dropdowns
   default field_date, field_user, field_os

OnLoaded                              (Background-priority Post)
   InitializePlot()                    ← pressure chart axes/labels
   InitializeStabilityPlot()               ← Stability chart axes/labels
   UpdateChart()                       ← empty, just renders titles

ApiCombo.SelectionChanged → StartSession
   _penManager.Stop()                  ← always stop first; safe if already stopped
   _penManager.Start(_apis[selected])
   dot_pen.Fill = IsRunning ? green : gray

OnClosing
   _penManager.Dispose()  → Stop()
   _scaleManager.Dispose() → cancel CTS, port.Dispose()
   _sessionLogger.Dispose() → flush + close CSVs
```

`StartSession` is the only place a pen session begins; it runs on every API-combo change, including the initial `SelectedIndex = 0` set in `OnOpened`.
