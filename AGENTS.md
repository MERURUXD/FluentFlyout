# FluentFlyout downstream working rules

This repository is a personal downstream fork of [FluentFlyout](https://github.com/unchihugo/FluentFlyout). The default goal is to keep the mature Windows and media behavior mergeable while adding narrowly scoped downstream policy and adapters.

## Scope and compatibility

- Preserve the GPL-3.0-or-later license, copyright notices, and upstream attribution.
- Keep the existing solution and project directory names. Do not perform broad namespace renames, mass formatting, aesthetic repository-wide reorganizations, or feature deletion.
- Do not rewrite the media state machine without evidence that a smaller change cannot solve the problem.
- Treat `FluentFlyoutWPF/MainWindow.xaml.cs`, media/session code, settings, packaging, and existing workflows as upstream-heavy conflict hotspots. Keep downstream logic in small new files where practical.
- A disabled option should not acquire new timers, event subscriptions, background work, audio capture, or network activity. Verify this against the current implementation before changing it; Stage 1 records behavior but does not optimize it.
- Future downstream work must not send telemetry or experiments to FluentFlyout infrastructure and must not present upstream releases as updates for this fork. Implement that isolation in a later focused stage, not as an incidental baseline edit.

## Before editing

1. Read the relevant implementation and the applicable development notes under `docs/development/`.
2. Check the working tree and remotes. Preserve unrelated user changes, including the untracked `fluentflyout_codex_prompts_6stage/` task-material directory unless the user explicitly asks to change it.
3. Identify whether the change touches an upstream-heavy file and record the conflict risk.
4. Prefer the smallest reversible change. Do not silently fold unrelated cleanup into a feature or baseline task.

## Validation

For the WPF application, use the repository's explicit configuration and platform:

```powershell
dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64
dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
```

Also run `git diff --check`. The CI workflow intentionally validates the WPF x64 build and does not sign, publish, or build the Store/MSIX package. Do not claim a GUI scenario was tested unless it was exercised on a Windows desktop.

## Change handoff

Every stage handoff should state the summary, files changed, architecture decisions, validation results, upstream compatibility/conflict risk, known risks, remaining work, and relevant commit hashes. Do not create commits, push, or open a pull request unless the user asks.
