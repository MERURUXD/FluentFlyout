# FluentFlyout downstream working rules

This is a personal downstream fork of [FluentFlyout](https://github.com/unchihugo/FluentFlyout). The goals are lower optional-feature overhead and continued upstream maintainability, not broad feature deletion or a new media architecture.

## Scope and compatibility

- Preserve the GPL-3.0-or-later license, copyright notices, and upstream attribution.
- Keep the existing solution and project directory names. Do not perform broad namespace renames, mass formatting, repository-wide reorganization, or unrelated feature deletion.
- Do not rewrite the media state machine without evidence that a smaller change cannot solve the problem. Prefer small downstream policy files and narrow adapters over changes scattered across upstream code.
- Treat `FluentFlyoutWPF/MainWindow.xaml.cs`, media/session code, settings, packaging, and existing workflows as upstream-heavy conflict hotspots. Identify that risk before editing them.
- Keep fixes, pure formatting, and unrelated cleanup separate. For multi-issue work, agree on a staged scope and finish its validation before moving to the next stage; do not implement an entire roadmap just because it is documented.

## Downstream invariants

These are requirements for changes, not a claim that all existing paths already satisfy them. Verify the current implementation and distinguish new regressions from inherited defects.

- Keep the code-owned isolation in `FluentFlyoutWPF/Classes/Downstream/DownstreamPolicy.cs`: imported settings must not re-enable upstream telemetry, experiments, or update infrastructure. Keep purchase UI and onboarding policy explicit.
- Keep updates on the fixed downstream GitHub stable-release channel. The rolling `dev` prerelease is not a stable update. Do not add binary download, execution, or installation to a metadata-only updater.
- Preserve the separate downstream identity for settings, logs, startup registration, mutex/events, and notifications. Legacy settings are migration input, not a write destination; preserve explicit feature choices and the migration's identity reset.
- Apply app filtering before media preference. Preserve Automatic mode and unknown-mode fallback. Selected-session UI and controls must agree; deduplication must not outlive its session ownership. Do not add a polling loop to conceal missed session events.
- Disabled optional features must not acquire new timers, subscriptions, capture, or network work. Check both never-enabled and enabled-then-disabled states, including shared consumers and in-flight callbacks. Hidden, unloaded, closed, and disposed are different states.
- Pair resource creation with stop/unsubscribe/dispose paths. Coordinate restart, disable, and disposal across threads; a disposed flag alone does not serialize resource publication. Apply WPF UI changes on the Dispatcher and recheck ownership there.
- Keep ZIP distribution separate from MSIX/Store support. The supported downstream release configuration is `GitHub Release`; hiding purchase UI does not prove that other build configurations avoid Store APIs. Do not request signing secrets or change Store behavior incidentally.

## Before editing and reading development notes

1. Inspect the working tree, branch, remotes, and current base SHA. Preserve unrelated user changes and task-material directories, including `fluentflyout_codex_prompts_6stage/` if present.
2. Read the relevant implementation and notes below. Record the intended scope, acceptance checks, and upstream conflict risk before changing code.
3. Treat historical descriptions as context, not instructions to restore old behavior. If a note and implementation disagree, identify the mismatch rather than assuming either is proof of successful validation.

Development notes:

- [Downstream policy and identity](docs/development/downstream-policy.md): isolation, migration, identity, and defaults.
- [Runtime architecture](docs/development/architecture.md): ownership and media flow; verify snapshot descriptions against current code, especially startup, defaults, and online services.
- [Performance audit](docs/development/performance.md): structural changes and intended lifecycle guarantees, not measured CPU or memory results.
- [Release process](docs/development/release.md): ZIP/version/release procedures; reading a procedure is not permission to publish.
- [Historical baseline](docs/development/baseline.md): the original takeover snapshot, not the current runtime contract.
- [Staged remediation prompts](docs/development/remediation-prompts/README.md): bounded task instructions and acceptance criteria; execute only the requested stage.

## Formatting and validation

Follow the applicable EditorConfig rather than imposing a new style. `FluentFlyoutWPF/.editorconfig` requires four-space C# indentation, CRLF line endings, no final newline, and the existing copyright/SPDX header. Preserve the formatter's import ordering. That configuration does not apply to every directory or to Markdown.

For executable WPF changes, use Windows with a .NET 10 SDK and the explicit configuration/platform from the repository root:

```powershell
dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64
dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic
git diff --check
```

- Also check the final staged/committed diff, not only an empty working-tree diff. Run the focused regression tests for changed behavior and report their exact command; do not invent a test target before one exists.
- These build commands validate compilation, not release version injection or publishing. Use the release guide for publish inputs. Check each applicable CI workflow independently; one green build does not establish that formatting, tests, or release checks passed.
- Documentation-only changes need diff checks and validation of links, paths, commands, and consistency. State when build, formatter, or desktop checks were not run and why.
- Use controlled Windows profiles for GUI, audio, migration, coexistence, and network checks. Static audits and CI compilation do not establish that those scenarios work. Record untested cases explicitly.
- Do not claim performance percentages without comparable before/after measurements. Do not clear unrelated upstream warnings, change quality settings, or weaken checks just to get a green build.

## Change handoff and authorization

Every stage handoff should include the summary, files changed, architecture decisions, validation commands/results, upstream compatibility/conflict risk, known risks, remaining work, and relevant commit hashes when commits exist. Label static inspection, automated tests, and desktop measurements separately.

Do not create commits, push, or open a pull request unless the user authorizes it for the requested work. When authorized, use a scoped branch/PR and do not rewrite published `master` history. Permission to modify code or open a PR is not permission to merge, move release tags, publish releases, sign packages, or deploy. Check workflow triggers before an authorized push and ask if it would cause publishing beyond that authorization.
