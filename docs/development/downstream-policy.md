# Downstream policy and identity

Stage 2 gives this checkout an explicit downstream boundary while keeping the
upstream media/session implementation and service classes mergeable. The
policy is deliberately code-owned rather than user-configurable: an imported
or legacy settings file cannot re-enable upstream infrastructure.

## Online-service policy

`FluentFlyoutWPF/Classes/Downstream/DownstreamPolicy.cs` is the single source
of truth for the current downstream switches:

- `EnableUpstreamTelemetry = false`
- `EnableUpstreamExperiments = false`
- `EnableUpstreamUpdateCheck = false`
- `EnableDownstreamUpdateCheck = true`
- `EnableUpstreamPurchaseUi = false`
- `EnableOnboarding = false`

Experiments, telemetry, and update checking retain their upstream service
implementations but return before contacting `FluentFlyoutApiClient` when the
corresponding policy is disabled. The downstream updater is a separate
metadata-only GitHub Releases check for `MERURUXD/FluentFlyout`; it reads only
the stable `tag_name`, compares semantic versions, and opens the fixed
downstream release page. It never downloads, replaces, executes, or installs a
binary. Update notifications, release-page actions, and manual update controls
use the downstream channel when it is enabled. The API client and upstream
updater remain in the project for future mergeability, but the upstream branch
is unreachable under the current policy.

The policy does not change the Store/GitHub licensing implementation. The
`GitHub Release` build continues to use the existing premium behavior, while
the upstream purchase UI is hidden in the downstream shell.

## Build and update identity

`FluentFlyoutMSIX/Package.appxmanifest` is the repository-owned source for the
stable semantic base version. Unpackaged builds carry separate assembly
metadata for `DownstreamBuildChannel`, `DownstreamBuildVersion`, and
`DownstreamSourceRevision`; ordinary builds default to the non-stable
`development` channel, while the ZIP packager injects `dev` or `stable` and
the current full commit SHA.

Only a stable identity such as `v2.15.0` participates in downstream update
comparison. Development and rolling `dev` identities remain non-stable even
when they include the manifest version, and the updater exits before making a
request for them. The updater reads release metadata and opens the fixed
downstream release page; it never downloads, executes, installs, or replaces a
binary.

## Runtime identity

`FluentFlyoutWPF/Classes/Downstream/ProductIdentity.cs` centralizes the values
that must not overlap with an official FluentFlyout installation:

- slug/AppData directory: `FluentFlyoutDownstream`
- display name: `FluentFlyout Downstream`
- executable/assembly: `FluentFlyoutDownstream`
- mutex and settings event: downstream-specific names
- startup registry value: `FluentFlyoutDownstream`
- toast activator CLSID: `A4B7F5C8-5A1B-4B1F-9F54-4CF40A4E4E85`
- downstream repository and issue URLs: `MERURUXD/FluentFlyout`

The WPF shell uses a downstream-named icon resource. The current resource is
an isolated copy of the existing artwork; a visual rebrand is intentionally
out of scope for this stage. MSIX identity, display/startup names, toast CLSID,
and COM executable registration mirror the same downstream identity. Existing
localized FluentFlyout wording and upstream attribution are not mass-edited.

## Settings migration

Normal reads and writes use `%AppData%\FluentFlyoutDownstream\settings.xml`.
When that file and its backup are absent, startup may read the legacy
`%AppData%\FluentFlyout\settings.xml` or `.bak` once. The migrated settings
are written only to the downstream path; the legacy files are read-only and
left untouched. Explicit feature values win over constructor defaults. The
migration generates a new downstream UUID and clears the persisted Store
identity so the two products do not share telemetry or licensing identity.

Logs use the downstream AppData directory as well. Packaged log lookup first
checks the package cache's downstream folder and then falls back to the same
unpackaged path.

## Fresh-install defaults

The downstream defaults retain media flyout and fullscreen protection, while
starting Lock Keys, Next Up, Volume Mixer, Taskbar Visualizer, telemetry, and
upstream update notifications disabled. Deserialization does not reapply these
defaults to an existing settings file.

## Non-goals and verification boundary

This stage does not delete optional implementations, remove localization or
the Source Generator, rewrite media/session selection, change Store signing,
or claim desktop/network/coexistence tests that were not exercised. Build and
static audits cover the policy, release, and identity seams; desktop migration,
coexistence, and controlled network-capture checks require a Windows desktop
test profile.
