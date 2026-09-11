# Current performance and lifecycle evidence

This document separates code-confirmed lifecycle contracts from automated tests
and real Windows measurements. It contains no CPU, memory, wake-up, or
percentage-improvement claim unless a controlled desktop report records the
comparison.

## Code-confirmed contracts

The current checkout makes optional work conditional at its ownership boundary:

- Volume mixer construction, audio-device/session subscriptions, and the
  one-second polling timer require an active volume/mixer/taskbar-volume
  consumer. Releasing the last consumer closes the window and releases the
  view-model resources.
- Taskbar widget construction and its 1.5-second positioning timer require the
  enabled widget and premium access. Disable/close stops the timer and closes
  the window.
- Taskbar visualizer allocation, loopback capture, watchdog, buffers, and system
  subscriptions are lazy. Disable/dispose drains callbacks and rejects stale
  in-flight restarts.
- The seekbar timer runs only while the selected session is playing and the
  visible seekbar is active. Display-environment refresh is a debounced one-shot
  timer.
- Taskbar-only media paths return before thumbnail decoding when there is no
  taskbar consumer. The existing thumbnail and dominant-color caches remain;
  artwork identity is reused instead of hashing the same stream twice.

These are structural observations from `MainWindow`, the optional windows,
`Visualizer`, `ResourceLifecycle`, and the focused tests. They are not measured
resource costs and do not prove that all Windows interleavings are leak-free.

## Automated evidence

The focused test project covers resource ownership, callback draining, seekbar
timer decisions, volume-mixer consumers, media-session selection/display
ownership, stable product identity, and downstream update metadata. Run the
tests from a Windows .NET 10 environment with the exact configuration shown in
the repository instructions; report the command and result in the stage
handoff. A green test run is automated evidence, not a desktop measurement.

## Controlled measurement protocol

For a comparable before/after result, use the same Windows 11 x64 host, Windows
build, power mode, display scale/monitor layout, media source, build
configuration, and isolated settings profile. Build the comparison and target
to separate output directories and record the full source SHA for each.

For each scenario, perform five cold starts. Use the same fixed readiness wait,
then sample for 60–120 seconds. Record raw values for:

- launch-to-ready time, with “ready” defined as the tray/coordinator being
  available;
- process CPU mean and peak;
- Working Set and Private Bytes;
- timer/wake-up observations; and
- relevant window, subscription, and audio-capture counts.

Keep raw traces and personal media data outside the repository unless a small,
fully sanitized fixture is deliberately added. The summary must identify units,
sampling method, warm/cold state, and every failed or discarded run. Do not
convert noise or a single run into a performance percentage.

## Required scenario matrix

| Scenario | Playback | Taskbar widget | Visualizer | Volume consumer | Lifecycle variant |
| --- | --- | --- | --- | --- | --- |
| Cold idle | none | off | off | off | never enabled |
| Cold playback | fixed repeatable source | off | off | off | never enabled |
| Taskbar | fixed source | on | off | off | enabled throughout |
| Visualizer | fixed source | on | on | off | enabled throughout |
| Volume | fixed source | off | off | on | enabled throughout |
| Disable/release | fixed source | on, then off | on, then off | on, then off | last consumer closed |

The release row must also be observed with a shared consumer retained, so one
feature turning off does not get mistaken for releasing a resource still needed
by another feature. Repeat the enable/disable cycle after display and Explorer
recovery when that controlled profile is available.

## Runtime evidence still required

The following cannot be inferred from a build or static audit: real media
selection with Spotify and browser sessions, app filtering, audio-device
switches, lock-screen/Explorer/display recovery, taskbar recreation, settings
migration/coexistence, controlled network capture, notifications, and
shutdown-time resource release. Record each as pass, fail, not run, or blocked
with a minimal reproduction and evidence path. See
[`06-windows-validation.md`](remediation-prompts/06-windows-validation.md) for
the acceptance matrix and do not claim a percentage until the raw measurements
exist.
