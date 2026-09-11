# Stage 6 Windows validation report

**Date:** 2026-09-11
**Status:** Partial acceptance with explicit desktop and comparability blockers
**Target commit:** `b5be9567820b138e0e2956a0be524212bd300900`
**Host:** Windows 11 Home 64-bit, build `26200`, .NET SDK `10.0.401`

This report records evidence for the current downstream checkout. It does not
claim that a build or a focused test run proves GUI, audio, media-session,
coexistence, network-capture, or performance behavior.

## Scope and comparison

The accepted target is the post-stage-5 `master` commit above. No separate
before/after comparison build was selected: the historical review SHA is not a
measured baseline for this run, and producing a comparable baseline would be a
separate controlled measurement task. Therefore no performance percentage or
resource improvement is reported.

The checkout was intentionally dirty only because the pre-existing
`fluentflyout_codex_prompts_6stage/` task material remained untracked. This
means the development ZIP below is an artifact inspection, not a clean release
candidate.

## Static and automated evidence

| Check | Result | Evidence |
| --- | --- | --- |
| WPF restore | Pass | `dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64` |
| WPF x64 `GitHub Release` build | Pass | `dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore`; existing compiler warnings only |
| Focused test restore | Pass with existing `NU1904` advisory | `dotnet restore tests\FluentFlyoutWPF.Tests\FluentFlyoutWPF.Tests.csproj -p:Platform=x64` |
| Focused production-behavior tests | Pass | 67 passed, 0 failed, 0 skipped |
| Solution format | Pass | `dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic`; 0 files formatted |
| Final diff check | Pass | `git diff --check` |

The automated suite exercises resource lifecycle, callback draining, seekbar
timer decisions, volume consumers, media-session selection/display ownership,
product-version parsing, and metadata-only update behavior. These are
automated seams, not replacements for the desktop matrix below.

## Development ZIP artifact

The non-publishing package check used:

```powershell
$sha = (git rev-parse HEAD).Trim()
$archive = Join-Path $env:TEMP "FluentFlyoutDownstream-development-$($sha.Substring(0, 7))-stage6.zip"
.\scripts\Build-DownstreamZip.ps1 `
  -Channel development `
  -ExpectedCommit $sha `
  -ArchivePath $archive
```

Observed artifact facts:

- manifest version: `2.15.0`;
- build identity: `development+b5be956`;
- source state: `dirty` (the task material described above);
- archive SHA-256: `4a4ba1173360965263d03fb9da43ecf7bec452cd21b773cb66073921633e8ba4`;
- archive entry count: 512; and
- required entries present: `FluentFlyoutDownstream.exe`, `LICENSE`,
  `README.txt`, `THIRD-PARTY-NOTICES.txt`, `RELEASE-INFO.txt`, and resolved
  runtime/third-party notices.

The archive's `RELEASE-INFO.txt` recorded the same commit, dirty source state,
source URL, and metadata-only updater boundary. No tag, release, upload, MSIX
install, Store operation, or signing operation was performed.

## Functional and lifecycle matrix

| Scenario | Status | Evidence or blocker |
| --- | --- | --- |
| Cold start with optional consumers never enabled | Not accepted as a desktop result | Static ownership conditions and automated lifecycle tests pass; a valid isolated desktop profile was not available. |
| Enable then disable the last taskbar/visualizer/volume consumer | Automated pass; desktop not run | Focused lifecycle/consumer tests pass; no controlled audio/display profile was used. |
| Shared consumer remains active while another consumer disables | Automated pass; desktop not run | Consumer ownership tests pass; no GUI/audio observation. |
| Device switch, lock-screen/Explorer/display recovery, taskbar rebuild | Not run | Requires a controlled interactive Windows profile and device/display actions. |
| Spotify/browser selection, pause combinations, filtering, Automatic/unknown mode, restart with same metadata | Automated policy pass; desktop not run | Focused media policy/ownership tests pass; no Spotify or browser playback session was used. |
| Fresh settings, legacy migration, backup recovery, downstream/upstream coexistence | Static contract only | Requires a disposable Windows profile and a separately installed official product; real user profile was not used. |
| Update/network boundary | Automated fixture pass; live capture not run | Tests use local responses; no real network capture or upstream service observation was claimed. |
| ZIP version/SHA/source/licence inspection | Pass | Development archive facts above; it is not a clean release candidate. |

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

- **Functionality:** automated policy/lifecycle seams pass; real GUI/audio/media
  behavior remains unverified for the scenarios marked above.
- **Resource release:** source and focused tests provide evidence for ownership
  paths; shutdown/recovery still needs a controlled desktop profile.
- **Performance:** not comparable; no improvement claim is made.
- **Artifact:** the development ZIP is structurally valid and traceable, but its
  dirty source state means it must not be treated as a release candidate.

The next valid desktop run needs a disposable Windows account or a tested
known-folder isolation method, a clean target checkout, a separately built
comparison commit, and a fixed raw-data destination outside the repository.
Stage 6 does not authorize adding optimizations, changing media behavior, or
publishing the inspected artifact.
