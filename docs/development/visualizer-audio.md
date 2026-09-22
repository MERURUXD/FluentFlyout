# Taskbar visualizer audio sources

## Behavior and ownership

`TaskbarVisualizerAudioSource` persists as an integer: desktop (0), microphone
(1), or both (2). Missing and out-of-range values resolve to desktop. Changing
the setting while the visualizer is disabled does not create capture resources.
`TaskbarVisualizerSourceStatus` is transient and excluded from settings XML.
The settings page uses the existing entitlement policy and links to Windows
sound settings for default-device selection.

Desktop initially uses WASAPI loopback on the default multimedia render endpoint.
After a render notification, it prefers the event's device ID regardless of Role
(Console, Multimedia, or Communications), preserving the previous output-event
contract even if the multimedia default still points to the old endpoint. Missing
or unresolvable IDs fall back to the multimedia default; an empty notification
clears the remembered ID. Microphone uses WASAPI capture on the default multimedia
input endpoint. A separate input-device event
restarts only the microphone, querying the current default endpoint again.
There is no microphone playback, recording to disk, or upload.

`VisualizerAudioEngine` serializes resource operations per endpoint and uses
generation checks to reject obsolete starts, callbacks, and queued UI frames.
Each endpoint owns its capture, FFT state, callback drain, and one-shot 500 ms
inactivity timer. Native stop/dispose runs outside the callback state lock.
Disable/dispose invalidates pending work, drains callbacks, and releases both
captures. Dispose also cancels pending retry delays. No timer polls for missing
devices while disabled or after an exhausted retry sequence.

An unavailable or denied endpoint gets at most five consecutive startup
attempts, with 500 ms delays between failed attempts. The other endpoint remains
usable. The microphone-only mode never falls back to desktop audio. Device
changes, unlock/resume, or re-enabling the feature permit another attempt.
Recording-stop events initiate endpoint-local recovery; successful audio
callbacks reset the consecutive-failure budget. Receiving silent packets is
not a capture failure. No-data expiry clears only the affected source and its
partial FFT, without restarting a healthy capture merely for silence.

The decoder supports unsigned PCM8, signed PCM16/24/32, and float32, including
PCM/float extensible subformats. It averages channels by frame, retains split
frames across callbacks, and calculates each endpoint's 4096-sample FFT using
its own sample rate. This also corrects the previous loopback path's treatment
of interleaved channels and its assumption that every 32-bit format was float.
Frequency bands remain logarithmic from 40 Hz to 8 kHz, bounded by Nyquist.
Both mode takes the maximum amplitude in each band before applying the existing
sensitivity, peak, frequency boost, and smoothing. It does not sum raw waveforms.
Audio-driven updates retain a 30 FPS maximum; state transitions and inactivity
clears can force a frame. All bitmap and content/status changes run on the WPF
Dispatcher after an ownership check. The bar renderer and visual style remain
unchanged.

## Validation (2026-09-22)

Automated tests use the existing Windows .NET 10 test project. The spectrum
tests cover setting compatibility, source selection, integer/float and
extensible formats, sample rates, channel framing, split buffers, non-finite
samples, unsupported formats, and per-band merging. Engine tests use injected
captures and a manual clock to cover missing/denied input, bounded retries,
independent inactivity, silence, default-input replacement, stale callbacks,
rapid mode changes, disable/dispose during startup, provisional-resource
cleanup, callback draining, cancelled retries, and frequency-buffer resizing.

Commands from the repository root:

```powershell
dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64
dotnet build FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic
git diff --check
```

The full test run passed 161 tests, including 39 new visualizer cases. The x64
GitHub Release build, formatter verification (zero files changed), diff check,
XAML parsing, localization-key uniqueness, and documentation links passed.
The compilation performed during the full test run still emitted existing
warnings outside the changed files; no unrelated warning cleanup was included.

Follow-up output-device regression checks add ten cases covering all render
roles while the multimedia default remains unchanged, lookup failure fallback,
empty-ID reset, disabled-state notifications, and capture-event isolation. The
49 visualizer cases pass with:

```powershell
dotnet test tests/FluentFlyoutWPF.Tests/FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64 --filter "FullyQualifiedName~Visualizer"
```

These use injected endpoint lookups and the production audio engine; actual
Windows Console/Multimedia default divergence has not been reproduced manually.

A temporary console probe outside the repository exercised the production
engine and WASAPI adapter against the host's real default endpoints, without
opening the application UI or modifying Windows device settings. Desktop-only,
microphone-only, and both modes received audio packets (48 kHz, two-channel,
float32 on this host). Input-only restart preserved desktop capture. Disabling
released every created capture and produced zero subsequent packets over the
200 ms observation window. No samples were written to disk. The observed
packets did not produce nonzero visible bands, so this is evidence of native
capture/lifecycle operation, not an audible-signal or visual acceptance test.

Still requiring controlled manual acceptance: speaking into the microphone,
desktop playback, both signals together, the rendered settings and taskbar UI,
changing the Windows default input, physical unplug/replug, real privacy denial
and recovery, microphone-in-use indicator after disable/exit, and lock/resume.
Injected failures and explicit endpoint restart do not establish these desktop
scenarios. No computer-use tooling was used and no CPU/memory improvement is
claimed.

## Upstream maintenance

Settings, localization, the visualizer integration, and the shared device monitor
are merge-sensitive. Capture ownership and signal processing stay in the small
downstream helper classes; media-session selection and playback controls are
unchanged. This work does not authorize a release, push, or merge.
