# Current runtime architecture

This document describes the current downstream checkout. It is an implementation
map, not a proposal to replace the upstream media state machine. Recheck the
source and focused tests before relying on a detail after an upstream sync.

## Solution shape

1. `FluentFlyoutWPF/` is the Windows desktop application. `App` owns process
   startup; `MainWindow` is the long-lived coordinator; pages, controls,
   view-models, helper classes, and optional windows provide the UI and
   integrations.
2. `FluentFlyout.SourceGenerators/` is a Roslyn analyzer/source generator. The
   WPF project passes `Pages/**/*.xaml` as `AdditionalFiles`; indexable page
   markup produces the settings search data.
3. `FluentFlyoutMSIX/` owns the retained Windows packaging path, manifest,
   assets, and signing/package concerns. It is separate from the downstream
   compile-and-ZIP path in CI.

## Startup and shutdown

```text
App.xaml StartupUri=MainWindow
        |
        v
App.OnStartup
  - register unhandled-exception and toast activation handlers
  - call ExperimentsService (the downstream policy returns before network I/O)
  - continue the WPF startup path
        |
        v
MainWindow constructor
  - bind and restore downstream settings
  - register downstream identity/startup/mutex/event values
  - start media monitoring and the low-level keyboard hook
  - subscribe media-session, shell, and display-environment events
  - schedule policy-gated onboarding/experiment and update work
        |
        v
MainWindow.Loaded
  - hide/apply theme and attach the window hook
  - initialize license state and save the downstream settings
  - refresh experiments (still policy-gated)
  - refresh filtered media and optional surfaces on demand
        |
        v
CleanupResources / OnClosed
  - mark cleanup, stop timers, cancel async state, and unsubscribe media events
  - dispose seekbar/visualizer resources and unhook native handlers
  - close lock, Next Up, taskbar, and mixer windows
  - restore the native volume OSD and shut down logging
```

`ShutdownMode=OnExplicitShutdown` keeps the tray/coordinator alive while the
flyout is hidden. `CleanupResources` is idempotent at the coordinator boundary
and the optional windows own their timers/subscriptions. The current cleanup
path removes the `MainWindow` media event subscriptions but does not call an
independent media-manager stop method; treat that upstream-owned lifetime as a
remaining desktop verification point rather than assuming it is stopped by the
documentation.

## Downstream policy and identity

`FluentFlyoutWPF/Classes/Downstream/DownstreamPolicy.cs` is the code-owned gate:

- upstream telemetry, experiments, update checks, purchase UI, and onboarding
  are disabled;
- the downstream update check is enabled; and
- imported settings cannot turn those upstream paths back on.

The supported updater requests only the downstream GitHub stable-release
metadata for a stable identity. Development and rolling `dev` identities do not
request update metadata. The updater opens the fixed downstream release page on
user action and never downloads, executes, installs, or replaces a binary.

`ProductIdentity` keeps the downstream AppData directory, executable/display
name, mutex, settings event, startup value, toast CLSID, and repository URLs
separate from the official product. The supported release configuration is the
portable downstream ZIP; retained MSIX/Store code and workflows are not evidence
that the Store path is part of the supported downstream product.

## Settings and defaults

`SettingsManager` writes to `%AppData%\FluentFlyoutDownstream\settings.xml` and
uses a `.bak` backup. On a first downstream run only, it may read the legacy
`%AppData%\FluentFlyout\settings.xml` or backup as migration input. It leaves
the legacy files untouched, writes only to the downstream directory, preserves
explicit values, creates a new UUID, and clears the persisted Store identity.

Fresh defaults retain the media flyout, fullscreen protection, acrylic surfaces,
and startup registration. Lock Keys, Next Up, taskbar widget/visualizer, volume
control/mixer, update notifications, and anonymous telemetry start disabled.
Existing settings are not overwritten by constructor defaults during
deserialization. Settings changes are debounced before persistence.

## Media-session flow and ownership

`MainWindow` reads the current media-manager collection for each ownership
decision. It first applies `IsSessionAllowed` app filtering, then applies the
selected mode:

1. `Automatic` prefers the focused allowed session and otherwise the first
   allowed session.
2. `Spotify Preferred` prefers an allowed Spotify session only while it is
   playing. A paused Spotify session does not displace a playing non-Spotify
   session; the automatic focused-then-first fallback remains available.
3. Unknown persisted mode values fall back to `Automatic`.

Spotify is identified from the Windows media-session application identity, not
from title text or process enumeration. The selection policy does not cache a
long-lived `MediaSession`; close/restart resolves the currently registered
instance. The selected owner is shared by the flyout, taskbar, controls,
seek/timeline updates, Next Up, and active-media volume targeting.

Property callbacks verify the source session before preparing data and recheck
ownership on the WPF Dispatcher before committing it. Metadata deduplication is
scoped to the selected session and invalidated by ownership-changing epochs.
Next Up keeps only its own short-lived origin-session token. A stale callback or
same-ID session restart must not move selected UI backward.

## Optional resources and lifecycle

- The taskbar window is created only when the widget is enabled and premium
  access is available. Its positioning timer exists only with that window;
  disabling or closing the widget closes the window and stops the timer.
- The volume mixer is created on demand for an active volume/mixer/taskbar
  consumer. Its view-model device/session subscriptions and one-second timer
  are released when the last consumer is released; a hidden volume flyout keeps
  the shared consumer alive when appropriate.
- The taskbar visualizer owns a nullable visualizer instance. It allocates audio
  capture, buffers, watchdog work, and system subscriptions only while enabled,
  drains in-flight callbacks on disable/dispose, and rejects stale restarts.
- Next Up and Lock Keys remain lazy. The low-level keyboard hook is a separate
  startup cost from the Lock Keys window.
- The seekbar `System.Threading.Timer` is active only when the seekbar is
  visible, supported, enabled, and the selected session is playing. The display
  environment refresh is a debounced one-shot Dispatcher timer.

These are lifecycle contracts backed by the Stage 2/3 focused tests and source
inspection. They are not a claim that every Windows audio, Explorer, display,
or shutdown interleaving has been manually exercised.

## Online, packaging, and validation boundaries

The supported downstream online boundary is the GitHub stable-release metadata
request described above. Upstream API, experiments, telemetry, Store, and MSIX
implementations remain in the tree for mergeability or upstream compatibility,
but the downstream policy makes the supported ZIP path explicit. The
`downstream-build.yml` workflow validates restore, formatting, the x64 WPF build,
focused tests, and a development ZIP without publishing. The dev-release
workflow is separately gated to downstream `master` and can publish only after
its same-SHA quality/package checks pass.

Builds and focused tests do not prove GUI, audio, media-session, migration,
coexistence, network-capture, signing, Store, or release behavior. Those claims
require the controlled Windows validation summarized in the
[current validation report](windows-validation.md).

## Upstream conflict hotspots

| Area | Why it is sensitive | Preferred downstream seam |
| --- | --- | --- |
| `FluentFlyoutWPF/MainWindow.xaml.cs` | Long-lived coordinator for startup, media, hooks, timers, and cleanup | Small policy or lifecycle adapter |
| Media manager/session selection | Windows API and upstream behavior are correctness-sensitive | Filter/selection/ownership policy with focused tests |
| Settings and XAML pages | Generated properties and search metadata cross-cut the UI | Narrow callbacks and policy files |
| Taskbar/visualizer/mixer windows | UI lifetime, timers, devices, and capture interact | Explicit owner lifecycle and tests |
| API, telemetry, update, and Store classes | Fork-specific privacy/release policy overlaps upstream code | Code-owned policy gate |
| `.github/workflows/*` and `FluentFlyoutMSIX/*` | Signing, packaging, and publishing are operationally sensitive | Additive downstream workflow; preserve upstream path |

Before editing a hotspot, verify the current source and record the intended
scope. Do not use this document as permission for a media-state-machine rewrite.
