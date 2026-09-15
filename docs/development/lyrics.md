# Spotify lyrics

## Current UI integration (2026-09-14)

The taskbar widget now hosts a two-line lyrics view, opt-in through Taskbar
Widget settings. Only the currently selected native Spotify session may query.
Browser/Web Player sessions remain excluded, including when Spotify is open
in the background. Default setting is off; existing settings remain intact.

- Layout is fixed to cover, lyrics, spectrum. The previous position setting
  is retained only for settings-file compatibility and is ignored. Audio capture and
  visualizer renderers are unchanged. Existing playback-control order remains.
- Original and translation are shown together. With translation off or absent,
  the second line previews the next lyric. Native QRC word timestamps control
  gray-to-bright clipping; LRC remains line-synchronized. The renderer is new
  WPF code inspired by the discussed Lyricify behavior, not copied private UI.
- Auto width is measured once per new document and bounded to 100–420 logical
  pixels, additionally capped using the selected taskbar's span (one third,
  minus reserved space). Fixed width is selectable. Loading restores the new track title/artist immediately; a new document
  sets width once and fades in over 180 ms when animation is enabled. A line change never requests a new width. Long lines scroll
  inside the slot. This cap does not detect every third-party taskbar icon gap.
- The lyrics view sits inside the existing MainBorder/BackgroundGlow container,
  extending the same album-color glow across cover and lyrics without adding
  another blur layer. Small horizontal taskbars use smaller two-line text;
  vertical taskbars retain the previous cover/control presentation.
- The widget owns session event subscriptions, HTTP and the lyrics service.
  MainWindow's existing ownership transition invalidates stale owners. Timeline
  and playback events recalibrate a local elapsed clock, including playback
  speed; no media polling was added. Frame refresh is at most 30 Hz while
  playing with a visible document. Pause, hide, disable and unload stop it;
  closing the taskbar window also disposes network/service resources.
- Settings setters notify the active taskbar directly, without an extra global
  settings subscription while lyrics are disabled. Translation, timing offset
  (+ advances), width mode/value and spectrum position are available in English,
  Simplified Chinese and Traditional Chinese.

Changed integration hotspots are TaskbarWidgetControl, TaskbarWindow,
MainWindow ownership/settings notification, UserSettings and TaskbarWidgetPage.
The independent data module and media-selection policy are preserved. These
are narrow edits but still carry upstream merge risk in those files.

Automated validation adds LyricsPresentationTests for seek, word timing,
translation fallback, width constraints, no timer before playback, stopping
refresh, and stable paused WPF pixels. An offscreen synthetic two-line image
was visually checked. This is not desktop acceptance: actual Spotify playback,
seek latency, Explorer recovery, taskbar placement/DPI, glow appearance and
CPU/GPU/private-memory measurements still need a controlled Windows run.
No user settings were enabled automatically and no existing app was replaced.

## Historical data-stage evidence

The following sections describe the preceding data-only stage. The statement
that no service was instantiated applied to that stage; current opt-in runtime
integration is described above.
## Scope and integration boundary

This stage adds `Classes/Downstream/Lyrics/` and a pinned
`Lyricify.Lyrics.Helper` 0.2.0 dependency. It does **not** instantiate a service
at application startup, change settings, subscribe to SMTC, or render taskbar
lyrics. There is no new background work in the shipped app at this stage.
Base inspected: `5fa7ae352ea38bcd94c06dd02de37197f7e81a78`.

The next UI stage must feed `SpotifyLyricsService.SelectAsync` from the
existing, app-filtered `MainWindow.GetActiveMediaSession()` decision. Supply
the actual session object as owner and its application ID, not the song title
or a separately chosen background Spotify session. Disabled, hidden, closed,
or non-Spotify consumers must invalidate the service. After awaiting, recheck
the selected owner on the Dispatcher and read `Current` there. The API returns
completion, not a document that could later overwrite current UI.

`Spotify.exe`, `Spotify`, and the official Store application's full AUMID are
accepted case-insensitively. Browser/Web Player and unknown IDs fail closed.
This intentionally does not broaden or change the existing Spotify Preferred
selection policy. Real SMTC identity verification remains required.

## Data and lifecycle decisions

- QQ Music first, then NetEase. Each source searches title + artist, then title
  alone if necessary; already fetched IDs are not fetched twice in one lookup.
- Matching requires normalized title and artist and duration within two seconds.
  Normalize Unicode, Chinese script and punctuation; remove only parenthesized
  film/TV theme descriptions. Retain Live/remix/remaster title qualifiers.
  `SAJI萨吉` is explicitly mapped to `萨吉`. Matching album wins; otherwise
  ambiguous IDs are rejected. Unknown aliases/multi-artist naming can miss;
  a manual mapping UI is not implemented in this stage.
- QQ QRC yields word timings. NetEase provides HTTPS LRC/translation fallback
  in this adapter; the earlier research probe's YRC path is not implemented.
  Plain text is not given invented timestamps. Translation remains a separate
  timeline; the UI stage must align it rather than assume equal line counts.
- HTTP transport is caller-owned and cancellation reaches requests and reads.
  Do not use the library's global HTTP client. Each request has an eight-second
  timeout, the whole lookup a thirty-second deadline, and responses a 2 MiB cap.
- Owner replacement, disable, browser selection and dispose cancel the previous
  request. A generation check rejects results even if a provider ignores
  cancellation. Same-owner duplicate callbacks share their pending task.
- In-memory cache: at most 64 exact metadata keys, twelve-hour positive TTL,
  two-minute miss/error TTL. Expiration is checked on selection, without a
  maintenance timer. Cancellation from an obsolete owner never populates cache.
  No persistence, metadata logs, Spotify credentials or audio capture.

The parser returns read-only line/word collections. Third-party license and
attribution files are copied beside application outputs. The test project
explicitly references the same WPF shared framework as the app, avoiding an
obsolete System.Drawing.Common transitive package in the non-WPF test graph.

## Verification

Deterministic regression suite:

```powershell
dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64 --filter FullyQualifiedName~SpotifyLyricsTests
```

It covers native/browser gating, never enabled, QQ priority/title fallback,
source failure fallback, positive/negative caching and expiry, pending-task
deduplication, cancellation/late publication, identity restart, matching,
QRC/translation parsing and actual HTTP cancellation propagation. Synthetic
lyrics fixtures are used; CI does not contact music services.

2026-09-13 manual network probe using this module and the pinned package:

| Spotify-style input | Selected QQ ID | Timed lines | Word fragments |
| --- | --- | --- | --- |
| 玄鸟（《长月烬明》电视剧插曲） / SAJI萨吉 / 256 s | 404374186 | 58 | 398 |
| 羽化成蝶 / 鬼卞 / 193 s | 334574944 | 22 | 204 |
| 小雨 / 黃齡 / 166 s | 383142539 | 53 | 587 |

Direct NetEase fallback reads returned 23 LRC lines for `1456238188` and 50
for `1997435585`. These counts can include credits; they are not sung-line
counts. The probe exercised metadata matching and network/parser behavior,
not a running Spotify session. No independent translation was returned for
these three Chinese songs. Bilingual UI remains unverified.

Full repository checks: restore, GitHub Release x64 build, solution formatter
verification, focused/full test suite, and `git diff --check`; record final
results in the stage handoff. Desktop synchronization, CPU/GPU/memory cost,
Explorer recovery, pause/seek and taskbar visual acceptance are not tested.

## Upstream compatibility and next stage

Existing MainWindow, media/session selection, settings, packaging workflows
and taskbar renderer are unchanged. The project file has a small dependency
and notice-copy addition. Next-stage integration touches upstream-heavy
MainWindow/settings/taskbar ownership and must preserve their existing rules.
No commit, push, PR, release or merge is implied by this data-stage work.

### Final validation results (2026-09-13)

- `dotnet restore FluentFlyoutWPF/FluentFlyout.csproj -p:Platform=x64`: passed.
- `dotnet build FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore`: passed; 0 errors. Final clean WPF compilation reports the 13 existing warning locations twice (temporary WPF project + main project), 26 warning entries; none originate in the lyrics module.
- Focused command above: 24 passed, 0 failed.
- `dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64`: full suite 101 passed, 0 failed (final full run used `--no-build` following the focused build).
- `dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic`: exit 0, 0 files requiring changes.
- `git diff --check` and `git diff --no-index --check -- /dev/null <new-file>` for each untracked addition: passed. No files were staged or committed.
- `dotnet list tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj package --vulnerable --include-transitive --no-restore`: no vulnerable packages reported by the configured NuGet source.
- Notice files verified in build output. Publishing, MSIX and desktop measurements were not run.

## UI-stage validation handoff (2026-09-14)

- Restore: `dotnet restore FluentFlyoutWPF/FluentFlyout.csproj -p:Platform=x64` passed.
- Build: `dotnet build FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore` passed, 0 errors. Existing 13 warning locations are reported twice by WPF temporary/main compilation (26 entries); no new lyrics warnings.
- Lyrics tests: `dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64 --filter "FullyQualifiedName~Lyrics"` passed 32 tests.
- Full regression: `dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64` passed 109 tests on the final source.
- Formatting: `dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic` passed, exit 0, no changed files.
- `git diff --check` and `git diff --no-index --check -- /dev/null <new-file>` passed for tracked and untracked work. No staged/committed changes exist.
- Renderer QA: offscreen synthetic original/translation and original/next-line images inspected. Automated pixel tests cover normal and small horizontal heights. Actual taskbar/glow/DPI placement and live Spotify synchronization are not established by this check.
- Build artifact: `FluentFlyoutWPF/bin/x64/GitHub Release/net10.0-windows10.0.22000.0/FluentFlyoutDownstream.exe`. Lyrics are off by default; enable in Taskbar Widget → Spotify lyrics in the new build. Requires the taskbar widget to be enabled.

The final controller also handles a missing initial duration by waiting for the
next timeline event, and preserves its local playhead across pause/resume when
SMTC has not refreshed its timestamp. These event paths require live-player
acceptance. Width bounds are conservative; actual occupied taskbar icon gaps,
long tracks, translations with different timing, and third-party taskbar mods
remain manual scenarios. No performance percentage is claimed. No commit,
push, PR, packaging, publish or change to running application settings was made.

## Responsiveness correction (2026-09-14)

User desktop feedback reported delayed track switching and pause/resume. Static
inspection found lyrics loading left the metadata panel hidden, and animated
Width changes repeatedly queued shell positioning. The old taskbar geometry
helper blocked the Dispatcher with UI Automation waits (1000/500 ms). No
before/after desktop latency figure has been measured.

- New metadata synchronously invalidates old lyrics and restores title/artist.
  The lyric request runs separately, and only a current-owner result replaces
  metadata. Lookup/matching is dispatched to the thread pool.
- Width is committed once, with opacity fade instead of per-frame Width layout.
  Lyrics-triggered positioning requests are coalesced.
- `NonBlockingSnapshot` runs shell queries off-thread and returns cached bounds
  immediately; it never waits on incomplete tasks. Pending queries are bounded
  per element, and old-monitor/handle results are rejected. Existing periodic
  positioning reads completed results; first placement can use fallback bounds.
- Playback callbacks update the renderer's local clock at Send priority without
  waiting for lyrics, metadata or thumbnails. Event-to-Dispatcher queue time is
  accounted for at pause/resume; timeline events still provide corrections.
- Changes touch TaskbarWindow geometry (upstream-heavy), a small MainWindow
  playback notification, and the independent lyrics controller/service/view.
  Media selection and audio capture are unchanged.

Normal build output was locked by the user's running process. Delivery uses
`$env:TEMP/fluentflyout-latency-fix` with `--artifacts-path`; restore and build
must both use that path. The running application was not stopped.

Correction validation:

```powershell
dotnet restore FluentFlyoutWPF/FluentFlyout.csproj -p:Platform=x64 --artifacts-path "$env:TEMP/fluentflyout-latency-fix"
dotnet build FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore --artifacts-path "$env:TEMP/fluentflyout-latency-fix"
dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64 --artifacts-path "$env:TEMP/fluentflyout-latency-fix"
dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic
git diff --check
```

Build: 0 errors, existing 26 WPF warning entries. Full suite: 112 passed.
New tests cover hung shell queries, old-context rejection, and playback changes
without waiting for another timeline/lyric. Final runtime responsiveness still
requires the user's desktop test. No commit/push was created.

## Settings polish (2026-09-14)

User confirmed the feature behavior. Spotify lyrics now appears in the regular
Taskbar Widget settings list immediately before Scrolling Text, below the
introductory content and primary widget controls. It follows the existing
CardExpander pattern: icon, NEW badge, header toggle, default collapsed, and
separate rows for translation, width mode, compact width slider/value and
compact timing slider/value. An expanded dark-theme render was visually
checked with dummy settings in an isolated offscreen host.

Lyrics layout is fixed to cover → lyrics → spectrum. The former spectrum
selector and internal placement slot are removed; stored legacy position
values no longer affect lyrics layout. The existing non-lyrics visualizer
position setting is unchanged.

Build and full 112-test suite passed using --artifacts-path
"$env:TEMP/fluentflyout-lyrics-settings" with GitHub Release / Platform=x64.
Solution formatter verification and diff checks passed. Existing WPF warnings
remain. No commit, publish or user-setting change was made. Shimmer/glow for
active words was discussed only, not added or benchmarked in this change.

## CI package-license correction (2026-09-15)

PR #20 compiled and passed tests, but Build-DownstreamZip rejected the transitive
CHTCHSConv 1.0.0 package because its nuspec declares no license. The packaging
policy is unchanged. The full Lyricify.Lyrics.Helper NuGet dependency has been
removed. Three attributed Apache-2.0 QRC decoding/XML source files are linked
from third_party/Lyricify.Qrc at the package's exact upstream source revision.
SharpZipLib 1.4.2 is the only new direct package and declares MIT.

FluentFlyout now parses the supported QRC/LRC timelines directly and uses the
Windows NLS LCMapStringEx API for simplified-Chinese matching. Tests cover QRC
word timings, offsets, repeated LRC timestamps, untimed input and traditional
artist names. Full suite: 118 passed. Direct QQ lyric downloads still parsed
58/22/53 lines for the three sample IDs. QQ search returned empty results in
this probe; the service used NetEase fallback. Search transport was not changed.

The complete development ZIP build passed locally; archive inspection confirmed
no ChineseConverter.dll, CHTCHSConv or Lyricify.Lyrics.Helper.dll. Existing license
checks were not relaxed. Changes are isolated to lyrics dependencies/parsing,
vendored attribution and tests; no media state or UI behavior is intentionally
changed. Live synchronization was not re-measured in this packaging fix.
