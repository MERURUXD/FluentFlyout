# Stage 4 Windows validation report

**Date:** 2026-09-12
**Status:** Core surface smoke pass; the latest media-key attempt lacked an active session, and comparison, Store, coexistence, and controlled-profile items remain not run
**Target:** `codex/stage4-final-cleanup` (packaged validation commit `938bc93f5e50c5d663c05ce886427e0282d04564`)
**Host:** Windows 11 Home 64-bit, build `26200`, .NET SDK `10.0.401`

This report records evidence for the current downstream checkout. It does not
claim that a build or a focused test run proves GUI, audio, media-session,
coexistence, network-capture, or performance behavior.

## Scope and comparison

The accepted target is the final Stage 4 downstream checkout on the branch above.
No separate before/after comparison build was selected: the historical review
SHA is not a measured baseline for this run, and producing a comparable baseline
would be a separate controlled measurement task. Therefore no performance
percentage or resource improvement is reported.

The packaged validation commit was clean. The development ZIP below is an
artifact inspection, not a stable release candidate or a publishing operation.

## Static and automated evidence

| Check | Result | Evidence |
| --- | --- | --- |
| WPF restore | Pass | `dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64` |
| WPF x64 `GitHub Release` build | Pass | `dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore`; 0 errors, existing compiler warnings only |
| Focused test restore | Pass with existing `NU1904` advisory | `dotnet restore tests\FluentFlyoutWPF.Tests\FluentFlyoutWPF.Tests.csproj -p:Platform=x64` |
| Focused production and downstream contract tests | Pass | 70 passed, 0 failed, 0 skipped |
| WPF x64 non-GitHub `Release` build | Pass | `dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c Release -p:Platform=x64 --no-restore`; 0 errors |
| Solution format | Pass | `dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic`; 0 files formatted |
| Final diff check | Pass | `git diff --check` |
| Stage 4C retired-resource audit | Pass | 114-key manifest; 2529 expected localization removals; 0 unexpected removals/additions; 29/29 XML dictionaries parse |

The automated suite exercises resource lifecycle, callback draining, seekbar
timer decisions, media-session selection/display ownership, retired-settings and
surface compatibility, product-version parsing, and metadata-only update
behavior. These are automated seams, not replacements for the desktop matrix
below.

## Development ZIP artifact

The non-publishing package check used on clean commit `938bc93f5e50c5d663c05ce886427e0282d04564`:

```powershell
$sha = (git rev-parse HEAD).Trim()
$archive = Join-Path $env:TEMP "FluentFlyoutDownstream-development-$($sha.Substring(0, 7))-stage4-final.zip"
.\scripts\Build-DownstreamZip.ps1 `
  -Channel development `
  -ExpectedCommit $sha `
  -ArchivePath $archive
```

Observed artifact facts:

- manifest version: `2.15.0`;
- build identity: `development+938bc93`;
- source state: `clean`;
- archive SHA-256: `a299fc4443e10872ecbcf977004cad22886d36885b834240aa5a7b8d58cc3cac`;
- archive entry count: 503; and
- required entries present: `FluentFlyoutDownstream.exe`, `LICENSE`,
  `README.txt`, `THIRD-PARTY-NOTICES.txt`, `RELEASE-INFO.txt`, and resolved
  runtime/third-party notices.

The archive's `RELEASE-INFO.txt` recorded the same commit, clean source state,
source URL, and metadata-only updater boundary. Its retained WPF assets include
the downstream application icon/tray icons, Home hero, Next Up demo, Widget
demo, and Visualizer image; the retired icon, Demo5, and upstream tray assets
are absent. No tag, release, upload, MSIX install, Store operation, or signing
operation was performed.

## Functional and lifecycle matrix

| Scenario | Status | Evidence or blocker |
| --- | --- | --- |
| Cold start with retained optional surfaces never enabled | Not run as a disposable-profile result | Static ownership conditions and automated lifecycle tests pass; a valid isolated desktop profile was not used for this scenario. |
| Taskbar Widget metadata, artwork, and playback surface | Pass on an active-session run | The earlier final-build taskbar screenshot and stable media-session smoke observed the metadata path; the latest f99a856 launch had no active media session for metadata. |
| Taskbar body-click ↔ Main Media Flyout | Pass | Current source retains `ShowMediaFlyout(toggleMode: true, forceShow: true)`; the final runtime is unchanged from the Stage 3 desktop pass that opened/toggled it. |
| Main Media Flyout media and volume-key semantics | Prior runtime pass; latest media-key run blocked | The f99a856 attempt had no active media session, so it is not counted as a current media-key pass; the unchanged Stage 3 final smoke covered media and volume keys with `MediaFlyoutVolumeKeysExcluded`. |
| Visualizer pause/resume and render-device reattach | Pass | Stage 3 final smoke passed pause/resume and Realtek headphone → speaker → headphone reattach; Stage 4 changes do not alter production runtime code. |
| Next Up normal display, Main-visible suppression, and post-hide recovery | Pass | Stage 3 final smoke used a stable Spotify SMTC source and observed all three states with matching title/artist/artwork. |
| Settings navigation and retired-surface search contract | Pass | Current UIA navigation exposed only Home, Widget, Visualizer, Next Up, System, and About; three downstream contract tests passed. |
| Device switch, lock-screen/Explorer/display recovery, taskbar rebuild | Not run | Requires a controlled interactive Windows profile and device/display actions. |
| Spotify/browser selection, pause combinations, filtering, Automatic/unknown mode, restart with same metadata | Automated policy pass; desktop not run | Focused media policy/ownership tests pass; the three-state Next Up smoke used Spotify SMTC, but the full matrix was not repeated here. |
| Fresh settings, legacy migration, backup recovery, downstream/upstream coexistence | Static contract only | Requires a disposable Windows profile and a separately installed official product; real user profile was not used. |
| Update/network boundary | Automated fixture pass; live capture not run | Tests use local responses; no real network capture or upstream service observation was claimed. |
| ZIP version/SHA/source/licence inspection | Pass | Clean development archive facts above; it is not a stable release candidate. |

## Controlled desktop acceptance checklist

Use this checklist for the next real Windows run. It supplements the matrix
above; a build or automated test does not mark a desktop item complete.

### Test setup and evidence

- [ ] Use an authorized Windows desktop with a disposable account or profile
  and controlled media, audio, display, and network conditions.
- [ ] Record the target and comparison commits, build configuration and
  version, artifact source, and exact test commands. Use temporary directories
  and backups; do not use production credentials or the user's primary profile.
- [ ] Mark every item as pass, fail, not run, or blocked, with the minimal
  reproduction and evidence path.

### Function and lifecycle

- [ ] Cold-start with taskbar, visualizer, Next Up, and Main Media Flyout never
  enabled.
- [ ] Enable each retained optional surface, disable it, and repeat the
  enable/disable cycle; verify its timer, capture, subscriptions, callbacks,
  and window stop or release.
- [ ] Keep the shared media/session owner active while disabling another
  surface; verify that resources still needed by the shared owner remain active.
- [ ] Interleave device switching, lock-screen resume, Explorer/display
  recovery, high-frequency flyout/taskbar updates, and taskbar close/recreate;
  check for isolated capture, hidden-window growth, exceptions, and stale
  callbacks.

### Media, settings, and identity

- [ ] Exercise Spotify and browser playback/pause combinations, app filtering,
  `Automatic` and unknown-mode fallback, newly added unselected sessions, and
  same-ID session close/restart with identical metadata. Confirm that Next Up
  and media controls use the same selected session.
- [ ] With a disposable fixture, verify fresh settings, legacy migration,
  backup recovery, explicit feature values, and downstream identity. Preserve
  the original legacy file; test official-product coexistence only in a
  controlled environment.

### Network and artifact boundaries

- [ ] Keep automated network checks on local responses or fixtures. If live
  capture is performed, observe only the controlled process and distinguish the
  supported downstream metadata request from user-initiated external links;
  verify that upstream telemetry, experiments, and update requests are absent.
- [ ] Unpack the ZIP and verify version, commit/SHA, source entry, and licence
  materials. MSIX installation, Store operations, signing, and publishing are
  outside this checklist's automatic authorization.

### Comparable performance

- [ ] Use the same machine, Windows build, power mode, display/scale, media
  source, build configuration, and feature settings for the comparison and
  target; build them to separate output directories.
- [ ] For each scenario, run five cold starts with a fixed readiness wait and a
  60–120 second sample window. Cover no-playback all-off, fixed playback
  all-off, taskbar, taskbar plus visualizer, Next Up, and each retained surface
  enabled then disabled.
- [ ] Record launch-to-ready time, CPU mean and peak, Working Set, Private
  Bytes, timer/wakeup observations, and capture/window counts. Keep raw traces
  and personal media data outside the repository, and make no percentage claim
  without comparable measurements.

## Performance measurement status

No comparable measurement was accepted. The required five cold starts per
scenario, fixed 60–120 second sample windows, paired comparison build, and
identical playback/power/display conditions were not all available. In
particular, no CPU, Working Set, Private Bytes, timer-wakeup, capture-count, or
window-count percentage is claimed.

An initial launch check kept the process alive for a 10-second observation, but
the attempted `APPDATA` override did not redirect Windows' known Application
Data folder. It therefore read the real downstream profile and was rejected as
an isolated measurement. The original startup registry value and observed
`LastKnownVersion` were restored, the test process was terminated, and the
test-generated log was removed. No value from that attempt is used as a
performance result.

## Final assessment and follow-up

- **Functionality:** the final build's Widget/settings smoke passed; its latest
  media-key attempt was blocked by the absence of an active media session. The
  unchanged Stage 3 runtime also has real Widget/Main Flyout, Visualizer,
  device-reattach, and Next Up evidence. Controlled shutdown/recovery items
  remain unverified where marked above.
- **Resource release:** source, contract tests, and focused tests provide
  evidence for ownership paths; full shutdown/recovery still needs a controlled
  desktop profile.
- **Performance:** not comparable; no improvement claim is made.
- **Artifact:** the development ZIP is structurally valid and traceable from a
  clean commit, but its development channel means it must not be treated as a
  stable release candidate.

The next valid desktop run needs a disposable Windows account or a tested
known-folder isolation method, a clean target checkout, a separately built
comparison commit, and a fixed raw-data destination outside the repository.
This Stage 4 validation does not authorize adding optimizations, changing media
behavior, or publishing the inspected artifact.
