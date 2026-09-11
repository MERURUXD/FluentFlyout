# Development baseline

This is the Stage 1 repository takeover record. It describes the checkout as inspected on 2026-09-10 and is deliberately descriptive: no product behavior was intentionally changed for this baseline.

## Repository and fork base

- Checkout: the local working tree inspected for this record
- Upstream project: <https://github.com/unchihugo/FluentFlyout>
- Downstream `origin`: <https://github.com/MERURUXD/FluentFlyout.git>
- Branch at inspection: `master`
- `HEAD`: `4947f695955e9e7181c0ea6c9c671704985b2cb0` (`Merge pull request #1120 from unchihugo/fix/issue-1115`)
- `HEAD` and `origin/master` resolved to the same SHA at inspection. The historical fork-base SHA is not independently determinable from the repository metadata currently exposed for review; rerun the local remote/merge-base checks in the checkout if that historical relationship is needed.

## Project and build targets

The main application is `FluentFlyoutWPF/FluentFlyout.csproj`:

- Output: WPF `WinExe`; `UseWPF=true`.
- Target framework: `net10.0-windows10.0.22000.0`.
- Declared platforms: `x64;ARM64`.
- Runtime identifiers: `win-x64;win-arm64`.
- Configurations: `Debug`, `Release`, and `GitHub Release`.
- `GitHub Release` defines `GITHUB_RELEASE`; the Store-backed premium path is conditional on that symbol.
- The source generator project targets `netstandard2.0`, uses Roslyn `4.8.0`, and is referenced as an analyzer by the WPF project.
- The MSIX project is `FluentFlyoutMSIX/FluentFlyoutMSIX.wapproj`. It is a separate packaging path and is not part of the lightweight CI build.

## Local validation commands

Run from the repository root in a Windows PowerShell terminal with a .NET 10 SDK and Windows desktop/WPF targeting support installed:

```powershell
git diff --check
dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64
dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
```

The same restore/build pair is used by `.github/workflows/downstream-build.yml`. A build is compile validation only; it does not prove that media sessions, global keyboard hooks, audio capture, notifications, Store APIs, or the tray behave correctly on a desktop.

There is currently no test project in the solution. If a later change adds one, keep the focused test invocation alongside the build rather than making the baseline workflow dependent on unrelated UI automation.

## Environment assumptions

- Windows 11 x64 is the reference environment because the application uses WPF, Windows App SDK/WinRT-facing APIs, media sessions, shell hooks, keyboard hooks, taskbar integration, Windows toasts, and optional WASAPI loopback capture.
- A Windows desktop is required for meaningful runtime checks. CI validates compilation on a Windows runner but does not exercise interactive windows.
- The first restore needs NuGet access. The application currently depends on third-party packages and code that reaches Windows or online services at runtime.
- Use a disposable or controlled profile for runtime experiments. Settings are persisted under `%AppData%\FluentFlyout\settings.xml`, with a `.bak` recovery copy; property changes are debounced before save.
- Do not use production credentials, Store purchase data, or personal telemetry identifiers in test evidence.

## Direct dependencies and online boundaries

The WPF project currently references:

| Package | Version | Role observed in the checkout |
| --- | --- | --- |
| `Dubya.WindowsMediaController` | 2.5.6 | Windows media session/control integration |
| `MicaWPF` | 6.3.2 | WPF window backdrop/visual styling |
| `Microsoft.Toolkit.Uwp.Notifications` | 7.1.3 | Windows toast construction/activation |
| `NAudio` | 2.3.0 | Audio device monitoring and WASAPI loopback visualizer capture |
| `NLog` | 6.1.3 | Application logging |
| `unchihugo.WPF-UI` | 4.4.2 | WPF UI controls/theme surface |
| `WPF-UI.Tray` | 4.3.0 | Tray icon integration |
| `CommunityToolkit.Mvvm` | 8.4.2 | Observable settings/view-model plumbing |

The current implementation has these online boundaries; Stage 1 records them and does not isolate them yet:

| Boundary | Current behavior and code area |
| --- | --- |
| FluentFlyout API | `FluentFlyoutWPF/Classes/Clients/FluentFlyoutApiClient.cs` uses `https://fluentflyout.com/api/` and a 2-second `HttpClient` timeout. The experiments, update metadata, and telemetry service calls use this client. |
| Experiments | `ExperimentsService.GetExperimentsAsync()` is awaited during `App.OnStartup` and requested again from `MainWindow` after load. |
| Updates | `UpdateCheckerService` performs an asynchronous startup check and supports a manual UI check. Update/changelog URLs can be opened externally; the current system is upstream-oriented. |
| Telemetry | `TelemetryService` posts event data when `AnonymousTelemetryAllowed` is enabled. The payload includes event/experiment information, a UUID/session identifier, app version, and store/GitHub context. |
| Store/premium | `LicenseManager` uses Windows Store `StoreContext`/license/add-on/purchase APIs outside `GITHUB_RELEASE`; the GitHub Release build defines premium as unlocked. Settings also query premium product information and the Home page contains a Store link. |
| Local notifications | Windows toast notifications are registered/activated by the app and used for first-update/update notices. |

## Eager and optional runtime work

The observed startup is intentionally documented before any optimization:

- `App.xaml` uses `StartupUri=MainWindow` and `ShutdownMode=OnExplicitShutdown`.
- `App.OnStartup` registers unhandled-exception and toast activation handling, awaits an experiments request, then calls the base startup path.
- `MainWindow` constructs the settings binding and media manager, restores settings, may configure Run-at-login, creates the tray icon, starts media monitoring, installs the low-level keyboard hook, subscribes to media/shell events, starts update/localization/first-update work, and creates a disabled position timer. Its `Loaded` path hides/applies theme, initializes licensing, repeats an experiments request, creates `VolumeMixerWindow` and `TaskbarWindow`, and updates the taskbar surface.
- `TaskbarWindow` is created during `MainWindow.Loaded` even when its widget/visualizer options are disabled. Its constructor creates the WPF window, starts a 1.5-second dispatcher timer, and calls `Show`; update logic collapses/stops it when the widget is disabled or not premium. The XAML contains both taskbar controls.
- `TaskbarVisualizerControl` has static initialization that creates the bitmap/FFT/bar state and subscribes to `AudioDeviceMonitor`/`SystemEvents`. Actual WASAPI loopback capture starts only when the visualizer is enabled; a watchdog checks/restarts capture and reacts to device/session/power changes. Disposal removes subscriptions and stops capture.
- `VolumeMixerWindow` is also created during `Loaded`. Its view model attaches to the default audio device, refreshes sessions, subscribes to the audio monitor, and starts a one-second dispatcher timer regardless of the Volume Control/Volume Mixer settings. The window is shown for the flyout and disposes the view model on close.
- `NextUpWindow` is lazy-created from media-property changes only when its setting is enabled, the app is not fullscreen, the main media flyout is not visible (`IsVisible == false`), playback is active, and a thumbnail is available. A delay controls close behavior.
- `LockWindow` is lazy-created on lock-key key-up events when the feature is enabled. The low-level keyboard hook itself is installed by `MainWindow` regardless of that setting.
- `MainWindow` chooses the active session by filtering `CurrentMediaSessions` through `IsSessionAllowed`, preferring the focused allowed session and otherwise the first allowed session. The seekbar timer runs at 300 ms only when enabled and playing.

These facts are a baseline and a list of candidate follow-up work, not permission to change behavior in this stage.

## Repeatable performance baseline

Capture a raw record for every run, including commit SHA, Windows build, power mode, display scale/monitor count, app configuration, media source, and whether the process was launched from a clean boot/profile. Use the same Windows 11 x64 host and the same media source for comparisons.

For each scenario, perform five cold starts. After launch, allow a fixed settling period, then sample for 60–120 seconds. Record at least:

- cold-start elapsed time: process launch to the main window/tray being ready;
- process Working Set and Private Bytes;
- process CPU percentage, including the sample mean and peak;
- notable timer, audio-capture, or network activity visible in logs/Process Monitor/ETW when available.

Use the following matrix, changing one option at a time and recording the exact setting state:

| Scenario | Media state | Taskbar Widget | Visualizer | Volume Mixer |
| --- | --- | --- | --- | --- |
| Baseline idle | no active playback | off | off | off |
| Playback idle UI | Spotify or a repeatable media source playing | off | off | off |
| Widget enabled | same playback state as paired control | on | off | off |
| Visualizer enabled | same playback state | on | on | off |
| Volume Mixer enabled | same playback state | off | off | on |

For every optional-feature comparison, record both the off and on measurements from the same reboot/profile or clearly mark the run as warm. Do not compare across different media sources or power modes. Store raw traces outside the repository unless a sanitized fixture is intentionally added; summarize only reproducible findings in a later stage.

## Known baseline risks and follow-up candidates

- Several optional windows and service subscriptions are created eagerly, so “disabled” currently does not necessarily mean zero initialization cost.
- Startup requests experiments before and after the main window loads, and update/telemetry/licensing paths can contact external services.
- The `FluentFlyoutMSIX` workflow includes signing/publishing concerns and should remain separate from compile-only downstream CI.
- `FluentFlyout.SourceGenerators/SearchItemsGenerator.cs` consumes `Pages/**/*.xaml` as `AdditionalFiles` and generates settings search entries; changes to page `Tag="Indexable"`, `DynamicResource`, or generator assumptions can fail builds or silently change search coverage.
- `MainWindow.xaml.cs`, settings, media integration, taskbar/visualizer controls, API/Store classes, MSIX metadata, and existing workflows are likely merge-conflict hotspots with upstream.

No speculative optimization, upstream isolation, branding, or Spotify preference was implemented in Stage 1.
