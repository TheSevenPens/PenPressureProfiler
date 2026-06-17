# WinPenKit — what we use

WinPenKit is vendored as two DLLs in `libs/WinPenKit/v0.3.0/`:

```
WinPenKit.dll           — core: factories, sessions, point + button types
WinPenKit.Avalonia.dll  — AvaloniaPointerSession adapter
```

There is **no source in this repo** for either. This page documents the public
surface this app actually depends on, observed from `using WinPenKit;` /
`using WinPenKit.Avalonia;` call sites in:

- [`PenSessionManager.cs`](../PenPressureProfiler/Sessions/PenSessionManager.cs)
- [`MainWindow.axaml.cs`](../PenPressureProfiler/MainWindow.axaml.cs)
- [`Scribble.Avalonia/MainWindow.axaml.cs`](../Scribble.Avalonia/MainWindow.axaml.cs) — secondary user; useful as a worked example of all the fields below

If you upgrade WinPenKit, this page is the contract to re-verify.

---

## `InputApi` (enum)

Selects the underlying driver backend.

| Value | Used here? | Notes |
|---|---|---|
| `WintabSystem` | ✅ default | Most Wacom/XP-Pen/Huion drivers in WinTab mode. |
| `WintabDigitizer` | ✅ | High-resolution digitizer context, when available. |
| `AvaloniaPointer` | ✅ | Synthesized from Avalonia `Pointer` events on the attached control. Local-scope: only fires while the pen is over that control. |
| `WmPointer` | ❌ filtered out | Available from `GetAvailableApis()` but excluded in `MainWindow.OnOpened` — "WM_POINTER subclassing doesn't receive events in Avalonia." |

The app calls `PenSessionFactory.GetAvailableApis()`, filters `WmPointer`, and appends `AvaloniaPointer` manually (since the factory only knows about native backends).

---

## `PenSessionFactory` (static class)

```csharp
IReadOnlyList<InputApi> GetAvailableApis();
IPenSession             Create(InputApi api);
```

`Create` does not accept `AvaloniaPointer` — for that we construct
`new AvaloniaPointerSession(control)` directly. The pattern in `PenSessionManager.Start`:

```csharp
session = api == InputApi.AvaloniaPointer
    ? new AvaloniaPointerSession(_penInputSurface)
    : PenSessionFactory.Create(api);
```

---

## `IPenSession`

The runtime interface for whichever backend is active.

| Member | Used as |
|---|---|
| `string? Start(IntPtr hwnd)` | Begins the session. Returns `null` on success or an error message. WinTab variants need a valid HWND; `AvaloniaPointerSession` ignores it. |
| `void Stop()` | Stops the session — packets stop arriving. |
| `void Dispose()` | Releases native resources. |
| `int MaxPressure { get; }` | Driver-defined maximum raw pressure (used as denominator for normalization). |
| `PenPoint[] DrainPoints()` | Returns and clears all packets queued since the previous call. Called once per UI poll tick (`PenSessionManager.OnTick`). |
| `IPenCaptureRegion? CaptureRegion { get; set; }` | **Spatial scope** of capture — see below. We set this on every backend. |

The drain pattern means the queue depth grows between ticks; at 60 fps poll and a ~133 Hz tablet, expect ~2 points per drain on average, more under load.

---

## Capture region (spatial scope) — *new in v0.3.0*

The input APIs natively differ in **where on screen the pen must be** for the app
to receive data:

| Backend | Native scope |
|---|---|
| `AvaloniaPointer` | the attached control only |
| `WmPointer` | the app window only |
| `WintabSystem` / `WintabDigitizer` | the **entire desktop** — even outside the app window |

WinPenKit unifies this with `IPenSession.CaptureRegion`:

```csharp
public interface IPenCaptureRegion { bool Contains(double desktopX, double desktopY); } // screen px

public static class PenCaptureRegion
{
    IPenCaptureRegion Unbounded { get; }                 // desktop-wide (Wintab only)
    IPenCaptureRegion Window(IntPtr hwnd);               // a window's GetWindowRect bounds
    IPenCaptureRegion Rect(double x, y, w, h);           // fixed screen rectangle
}
```

- **`null` (default) → window-scoped:** the session reports points only within the
  app window passed to `Start`. This flips Wintab's default from desktop-global to
  window-scoped so it matches the pointer backends. (Pointer sessions are already
  control-scoped, so `null` leaves that natural scope unchanged.)
- **Set a region** to scope every backend identically. WinPenKit.Avalonia ships
  `ControlCaptureRegion(Control)`, which tracks a control's live screen bounds
  (DPI-correct; cached on the UI thread so it's safe to read from the Wintab
  capture thread).
- **`PenCaptureRegion.Unbounded`** opts back in to desktop-wide capture — honored
  only by backends advertising `PenCapabilities.GlobalCapture` (Wintab).

**What the Profiler does:** `PenSessionManager.Start` sets
`session.CaptureRegion = new ControlCaptureRegion(_penInputSurface)` on whichever
backend is active, so the pen behaves the same on WinTab and Avalonia — data only
flows when the pen is over the chart surface. The region is disposed on `Stop`.

> v1 limitation: filtering is by screen **rectangle**, so a point passes if it
> falls in the region's bounds even when another window is on top of it
> (no occlusion / z-order test).

---

## `AvaloniaPointerSession` (in `WinPenKit.Avalonia`)

```csharp
new AvaloniaPointerSession(Control attachmentControl);
```

Subscribes to pointer events on the supplied control and synthesizes
`PenPoint`s only for `PointerType.Pen`. **Critical:** the attachment control
must have no interactive children that mark events `Handled` before this
session's handler runs. See [`ARCHITECTURE.md#peninputsurface`](ARCHITECTURE.md#peninputsurface)
for why we always attach to a childless transparent `Border`.

Only fires on `PointerMoved`, so when the pen is held still the session
sees nothing — this is why `PenSessionManager` preserves the last reading
on no-packet ticks while `TipDown` is true.

---

## `PenPoint`

The packet struct returned by `DrainPoints()`. Fields used by this codebase:

| Field | Type | Notes |
|---|---|---|
| `Pressure` | `uint` (used as `int` in `Scribble`) | 0 … `IPenSession.MaxPressure` |
| `Azimuth` | `double` | 0–360°, compass direction of tilt |
| `Altitude` | `double` | 0° flat … 90° perpendicular |
| `TiltX`, `TiltY` | `double` | −90 … +90° |
| `RawX`, `RawY` | numeric | Driver-coord position (used in `Scribble` only) |
| `DesktopX`, `DesktopY` | numeric | Screen-space position (used in `Scribble` only) |
| `Cursor` | (opaque) | Cursor index — debug-display only in `Scribble` |
| `Twist` | `double` | Roll around pen axis. Read by `Scribble` for display; **not used** in PenPressureProfiler. |

PenPressureProfiler reads pressure + tilt only and discards positional fields. Buttons go through `PenButtonTracker` rather than being read off the point directly.

---

## `PenButtonTracker`

Stateful helper that converts the raw button bitmask on each `PenPoint` into
named queries.

```csharp
var bt = new PenButtonTracker();
foreach (var pt in points) bt.Update(pt);

bool tip      = bt.IsTipDown;
bool barrel1  = bt.IsBarrelDown(1);
bool barrel2  = bt.IsBarrelDown(2);
bool eraser   = bt.IsEraser;          // used by Scribble, not by Profiler
uint raw      = bt.LastRawButtons;    // debug

bt.Reset();                           // called on session Stop()
```

Profiler reads `IsTipDown`, `IsBarrelDown(1)`, `IsBarrelDown(2)`. Edge
detection (transition from down→up) is done by the manager itself by
caching the previous tick's values — `PenButtonTracker` only reports
current state.

---

## What we don't use

For completeness, things that exist in WinPenKit but are not referenced:

- `WmPointer` backend (filtered out, see above)
- `PenPoint.Twist` (Scribble reads it for debug; Profiler doesn't)
- `PenPoint.RawX/RawY/DesktopX/DesktopY/Cursor` (position-related; Profiler is purely pressure/tilt)
- `PenButtonTracker.IsEraser` (Scribble uses it; Profiler doesn't distinguish eraser-tip)

If a future feature needs any of these, no new WinPenKit dependency is required — they're already on the types we hold.
