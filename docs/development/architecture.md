# Runtime architecture

This document is the Stage 1 snapshot of the current FluentFlyout downstream checkout. It records observed control flow and ownership so later downstream isolation can be implemented as small policy boundaries. It does not describe a new architecture and does not imply that the current eager paths are optimal.

## Solution shape

The solution has three relevant layers:

1. `FluentFlyoutWPF/` is the Windows desktop application. `App` owns process startup; `MainWindow` is the long-lived coordinator; pages, controls, view models, helper classes, and optional windows provide the UI and integrations.
2. `FluentFlyout.SourceGenerators/` is a Roslyn analyzer/source generator. The WPF project passes `Pages/**/*.xaml` as `AdditionalFiles`; the generator scans indexable page markup and emits `SettingsWindow.SearchItems` data.
3. `FluentFlyoutMSIX/` is the Windows App Packaging project. It references the WPF output and owns manifest/assets/signing/package concerns. It is intentionally separate from the compile-only downstream CI path.

## Startup and shutdown

```text
App.xaml StartupUri=MainWindow
        |
        v
App.OnStartup
  - register unhandled-exception and toast activation handlers
  - await ExperimentsService.GetExperimentsAsync()
  - continue WPF startup
        |
        v
MainWindow constructor
  - create/bind settings and media manager
  - restore settings and startup/run registration
  - create tray icon and cancellation state
  - start media manager and low-level keyboard hook
  - subscribe media, shell, and display-position events
  - begin experiment, localization, update, and first-update work
        |
        v
MainWindow.Loaded
  - hide/apply theme and attach WndProc hook
  - initialize license state and save flags
  - request experiments again
  - create VolumeMixerWindow and TaskbarWindow
  - update taskbar surface
        |
        v
MainWindow cleanup / application exit
  - stop timers and cancel async state
  - unsubscribe media/shell/display handlers
  - dispose visualizer and optional windows
  - restore native volume OSD and shut down NLog
```

The application uses `ShutdownMode=OnExplicitShutdown`; the tray and coordinator therefore outlive normal flyout visibility. The cleanup path stops the display timer, removes subscriptions/hooks, disposes the static visualizer, closes optional windows, and shuts down logging. No explicit media-manager stop was visible in the audited cleanup path; this is a follow-up verification point, not a Stage 1 change.

## Media-session flow

The media manager observes Windows media sessions and raises property/state events. `MainWindow.GetActiveMediaSession()` is the single ownership lookup for the downstream as well as the upstream-compatible path:

1. Read `CurrentMediaSessions`.
2. Filter through `IsSessionAllowed` (the app-filtering policy); a filtered session is never eligible for downstream preference.
3. Read the WindowsMediaController focused session once.
4. In `Automatic` mode, prefer the focused allowed session and otherwise use the first allowed session, retaining the upstream behavior.
5. In `Spotify Preferred` mode, prefer an allowed Spotify session only while it is playing. If Spotify exists but is not playing, prefer an allowed non-Spotify session that is playing, using the focused eligible player when possible. If neither preference applies, fall back to the same focused-then-first automatic selection.
6. Reflect the selected session into every single-owner surface.

The persisted `MediaSessionSelectionMode` values are deliberately small and closed: `0` is `Automatic` and `1` is `Spotify Preferred`. Unknown values degrade to `Automatic`; there is no Spotify Exclusive mode. Spotify is identified from the media-session application identity (`MediaSession.Id`), not from title/artist text or process enumeration, so a browser playing Spotify Web Player remains a browser session and multiple Chromium sessions remain distinguishable.

The selection policy receives only sessions already accepted by `IsSessionAllowed`. It reevaluates the current manager collection on every lookup and does not cache a selected or Spotify `MediaSession`; closing and restarting a player therefore resolves the currently registered session object naturally. The policy is limited to deciding whether Spotify Preferred has an override; automatic focused-session/first-session fallback remains in `MainWindow`.

The single-session consumers that must continue to use this lookup are the main media flyout (`UpdateUI`), taskbar widget, previous/play/pause/next/repeat/shuffle/seek controls, active-media taskbar volume targeting, open-player activation, timeline/seek updates, Next Up metadata, and playback/property/session-close refresh handlers. Event handlers must verify that the event source is still the selected session before applying metadata, and playback timers must use the selected session's current playback state rather than the event source's state. Session close/restart and setting changes refresh all persistent selected-session surfaces, including stale Next Up content. Next Up may retain only its own origin-session reference plus ID as a short-lived UI ownership token, so a same-ID restart cannot leave an old card visible; this is not a general selected-session cache.

The required transition invariants are:

| Scenario | Expected owner in `Spotify Preferred` |
| --- | --- |
| Spotify only, playing or paused | Spotify through normal selection |
| Spotify playing + paused browser | Spotify |
| Spotify paused + playing browser | Playing browser |
| Spotify starts while browser is active | Browser until Spotify is actually playing; then Spotify |
| Browser starts while Spotify is playing | Spotify remains preferred (the independent Pause Other Sessions option may pause the browser) |
| Spotify closes | Recompute immediately; fall back to the remaining allowed session or clear ownership |
| Spotify restarts | Resolve the newly registered Spotify session; never retain the closed object |
| Browser closes | Recompute from the remaining allowed sessions |
| Multiple Chromium sessions | Use Windows focus when an eligible playing session is needed; never infer Spotify from media title |
| App Filtering whitelist/blacklist | Filtered sessions cannot win preference or ownership |
| Paused sessions only | Do not force Spotify; use normal focused-then-first semantics |
| Metadata-only change from an unselected session | Do not update selected-session UI |
| Identical metadata after a session switch | Session identity invalidates property deduplication |

The seekbar timer remains configured for a 300 ms cadence but is active only when the feature is enabled and the selected session is playing. This path is upstream-heavy and should be changed through narrow policy/adapters rather than a broad state-machine rewrite.

## Optional windows and services

### Taskbar widget and visualizer

`MainWindow.Loaded` creates `TaskbarWindow` unconditionally. The window starts a 1.5-second dispatcher timer and is shown; `UpdateUi` collapses/stops it when the widget is disabled or the user is not premium. Its XAML contains `TaskbarVisualizerControl` and `TaskbarWidgetControl`.

The visualizer control has static lifetime. Type initialization creates bitmap/FFT/bar state and subscribes to audio-device and system events even if `TaskbarVisualizerEnabled` is false. Enabling it starts NAudio WASAPI loopback capture; a watchdog checks the capture path and restarts it after device/session/power changes. Disposal removes subscriptions and stops capture. This is the clearest current candidate for a later lazy-lifecycle boundary.

### Volume mixer

`MainWindow.Loaded` also creates `VolumeMixerWindow`. Construction creates its view model, which attaches the default audio device, refreshes application sessions, subscribes to `AudioDeviceMonitor`, and starts a one-second dispatcher timer independent of the Volume Control/Volume Mixer setting values. The window is shown by the flyout path and disposes its view model when closed.

The `VolumeControlEnabled` setting controls volume input/flyout behavior; `VolumeMixerEnabled` controls the page/widget behavior but does not prevent the initial window/view-model construction observed in this snapshot.

### Next Up

`NextUpWindow` is lazy. It is created from media-property changes only if Next Up is enabled, the app is not fullscreen, the main media flyout is not visible (`IsVisible == false`), playback is active, and a thumbnail is available. A delayed close avoids tearing down the preview during transient media updates.

### Lock keys

`LockWindow` is lazy-created on lock-key key-up events when the feature is enabled. Caps Lock, Num Lock, Scroll Lock, and Insert defaults are enabled. The global low-level keyboard hook is installed by `MainWindow` at startup regardless of the lock-window setting, so hook cost and lock-window cost are separate concerns.

## Settings flow

`SettingsManager.Current` exposes the `UserSettings` singleton/view model. `SettingsManager.RestoreSettings()` deserializes values from `%AppData%\FluentFlyout\settings.xml` into `UserSettings` and keeps a `.bak` copy. Property changes are debounced for 500 ms before persistence; `CompleteInitialization` ends the load-time suppression period.

Settings changes can directly affect coordinator behavior through generated/partial property callbacks. Examples observed in the snapshot include taskbar position/visibility updates, marquee updates, visualizer enable/disable forwarding, volume-control layout decisions, lock-key eligibility, notification/telemetry flags, and allowed-app filtering. Main-window startup separately checks the `Startup` setting for run registration; no `OnStartupChanged` callback was observed. The settings UI uses the source-generated search index from page XAML tagged `Indexable`.

Important defaults include startup, media flyout, and lock keys enabled; taskbar widget, taskbar visualizer, volume control, and volume mixer disabled; anonymous telemetry and update notifications enabled. Defaults are evidence for the current checkout, not a policy decision for the future fork.

## Online-service flow

```text
App startup / MainWindow.Loaded
       -> ExperimentsService
       -> FluentFlyoutApiClient (https://fluentflyout.com/api/)

startup/manual update check
       -> UpdateCheckerService
       -> API metadata and external changelog/update URL

user/app events (when allowed)
       -> TelemetryService
       -> API event POST

Store build settings/purchase UI
       -> LicenseManager / StoreContext
       -> Windows Store license/add-on/purchase services
```

The API client uses a two-second `HttpClient` timeout and resets the client after a timeout. Experiments are requested in both `App.OnStartup` and the loaded window path. Telemetry is gated by `AnonymousTelemetryAllowed` but remains enabled by default in this snapshot. The `GITHUB_RELEASE` build defines premium as unlocked and avoids the Store-backed purchase path; other configurations use Windows Store licensing. Windows toast registration/activation is local, while changelog, update, Store, GitHub, and API links are externally reachable.

Downstream isolation is intentionally deferred. A future policy layer must make the fork's online endpoints and telemetry behavior explicit, prevent upstream update advertisements, and preserve a clear opt-in boundary for any permitted service.

## Packaging and CI

The WPF project is the compile source. `build-msix.yml` and `FluentFlyoutMSIX/FluentFlyoutMSIX.wapproj` add Windows package metadata, assets, certificate decoding, and publishing artifacts. `dotnet-format.yml` provides formatting validation. The Stage 1 `.github/workflows/downstream-build.yml` validates restore and the `GitHub Release` x64 WPF build only; it does not sign, publish, access Store secrets, or modify the existing packaging workflows.

## Primary upstream conflict hotspots

| Area | Why it changes frequently / conflict risk | Safe downstream seam |
| --- | --- | --- |
| `FluentFlyoutWPF/MainWindow.xaml.cs` | Long-lived coordinator combining startup, hooks, media, flyout, taskbar, timers, updates, and cleanup | Small policy service, adapter, or isolated partial only when necessary |
| Media manager/session selection | Upstream behavior and Windows API assumptions are central to correctness | Session filter/selection policy with focused tests |
| `ViewModels/UserSettings.cs` and settings pages | Generated properties/callbacks and XAML search metadata are cross-cutting | New policy settings and narrow callbacks; preserve generated contract |
| Taskbar/visualizer/volume windows and controls | UI lifetime, timers, audio-device subscriptions, and premium gating interact | Lazy factories/lifecycle interfaces, introduced separately and measured |
| API, experiments, telemetry, update, and Store classes | Network/privacy/release policy is fork-specific but upstream code may move | Explicit endpoint/policy abstraction with a small call-site surface |
| `.github/workflows/*`, `FluentFlyoutMSIX/*` | Release signing, packaging, and upstream automation are operationally sensitive | Additive compile-only workflow; avoid rewriting publishing workflows |
| Source generator and page XAML | Generator assumptions span all settings pages | Keep `AdditionalFiles`, `Indexable`, and `DynamicResource` conventions stable |

Before a later edit, record the hotspot touched and whether a new file can hold the downstream-specific logic. Avoid scattering policy changes through this table's upstream-heavy files.
