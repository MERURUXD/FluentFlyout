# Stage 3 performance audit and runtime changes

This stage records a code-level performance audit of the downstream checkout and
the small runtime changes selected from that audit. It is not a CPU, memory, or
ETW benchmark; no profiler measurements are claimed here. The before/after
observations below are structural facts that can be verified from the current
control flow.

## Baseline and audit boundary

The audit covered optional windows, the taskbar visualizer, periodic/background
work, artwork loading, and startup ownership. Existing Stage 2/downstream
changes were treated as pre-existing worktree state and were not reorganized.

### Optional windows

Before Stage 3, `MainWindow.MicaWindow_Loaded` unconditionally constructed both
`VolumeMixerWindow` and `TaskbarWindow`.

- `VolumeMixerWindow` eagerly constructed `VolumeMixerViewModel`. The view model
  attached to the default render device, enumerated audio sessions, subscribed
  to default-device changes, and started a one-second `DispatcherTimer` even
  when Volume Control and Volume Mixer were disabled.
- `TaskbarWindow` called `Show()` and started a 1.5-second positioning timer in
  its constructor. Its update path returned early when the widget was disabled,
  but the timer and window still existed.
- `NextUpWindow` and `LockWindow` were already created on demand. They were
  audited and intentionally not rewritten.

The stage now creates the mixer only when a volume or taskbar-volume consumer
actually needs its view model. The taskbar window is created only when the
widget is enabled and premium access is available. Disabling the widget closes
the window and stops its timer; display/Explorer recovery does not recreate a
disabled window. Existing enable, disable, and recreation entry points remain
the ownership boundary.

### Visualizer

Before Stage 3, `TaskbarVisualizerControl` held a static eager
`new Visualizer()`. Merely constructing the taskbar XAML therefore allocated a
`WriteableBitmap`, FFT/bar buffers, and registered audio-device, session-switch,
and power events. Capture itself started only when the setting was enabled, but
the disabled state was not close to zero-cost.

The visualizer owner is now lazy and nullable. A disabled taskbar control does
not create it. Enabling an existing control creates and attaches one instance;
creating a taskbar window while the setting is already enabled does the same.
Disabling releases the existing instance (including capture, buffers, device,
watchdog, and system subscriptions) without creating a replacement. Closing
the taskbar widget releases it as well, and shutdown disposal is idempotent.
`Visualizer` now refuses restart after disposal or disable, cleans partial
start failures through `Stop`, and reuses the per-frame bar scratch buffer.
The capture, FFT, visual quality, target frame rate, and baseline behavior were
otherwise left unchanged.

### Timers and background work

The following existing behavior was confirmed rather than broadly refactored:

- the seekbar `System.Threading.Timer` runs only while the seekbar is active and
  the selected session is playing;
- the display-environment timer is a debounced, one-shot refresh timer;
- the volume-mixer polling timer exists only with a live mixer view model;
- the taskbar positioning timer exists only with a live taskbar widget;
- visualizer capture/watchdog work exists only while the visualizer is running.

No new polling loop, audio capture, network activity, or timer was added for a
disabled option.

### Artwork

The existing five-entry thumbnail and dominant-color LRU caches remain in
place. The media-property path previously computed a stable thumbnail hash and
then called `GetThumbnail`, which computed and read the same hash stream again.
`GetThumbnailWithHash` now reuses the already computed stable identity without
weakening the cache key.

Taskbar-only playback/property paths now skip thumbnail decoding and dominant
color work when no taskbar widget is desired. Main flyout and enabled Next Up
paths still load artwork, and the media-property comparison still uses stable
artwork identity rather than a title-only key. No image size, sampling quality,
or accent-color algorithm was changed.

## Before/after observations

| Path | Before | After | Expected impact |
| --- | --- | --- | --- |
| Startup, Volume Control off | Mixer window, device/session setup, and 1 s polling were created | No mixer is created until a volume consumer requests it | Removes disabled-state device/session work and polling |
| Startup, Taskbar Widget off | Taskbar window, `Show()`, and 1.5 s positioning timer were created | No taskbar window is created | Removes the disabled taskbar HWND and timer |
| Taskbar Widget disable | Existing window could remain hidden with an early-return timer | Window is closed and the taskbar visualizer instance is released | Releases optional window/timer/capture/subscription work |
| Visualizer off | Static visualizer allocated buffers and registered events | No visualizer instance is created | Removes disabled-state allocations and subscriptions |
| Repeated media-property artwork | Stable hash could be read twice for one event | Existing hash is passed into thumbnail loading | Removes duplicate stream/hash work |
| Taskbar absent/disabled | Taskbar update paths could decode artwork before a null-conditional UI call | Taskbar-only paths return before decode | Avoids work with no taskbar consumer |

These are expected structural changes, not measured percentage improvements.

## Validation boundary and risks

The source/build checks for this stage are `git diff --check`, the established
WPF x64 restore, and the `GitHub Release` x64 build. Static audits should also
confirm that optional constructors are no longer unconditional, the visualizer
has no eager static construction, and every created timer/subscription has a
matching stop/unsubscribe/dispose path.

Desktop GUI/audio, Explorer-restart, display-topology, migration, and controlled
network-capture scenarios require a Windows test profile and are not inferred
from a compile. The main remaining risks are runtime ordering around enabling a
widget or visualizer after startup, recovery while Explorer is restarting, and
audio-device changes during a visualizer restart. These paths retain the
existing single-instance/recreate boundaries and should be exercised manually
when a suitable desktop harness is available.
