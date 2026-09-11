# Upstream synchronization checklist

This is a review and preparation checklist for updating the downstream fork. It
does not authorize a fetch-and-merge, rebase, force-push, tag move, release, or
automatic conflict resolution.

## 1. Capture the current state

From the repository root, record the current branch, `HEAD`, working-tree
status, and both remote URLs before changing refs:

```powershell
git status --short --untracked-files=all
git branch --show-current
git rev-parse HEAD
git remote -v
git ls-remote origin refs/heads/master
git ls-remote upstream refs/heads/master
```

Preserve uncommitted user changes. A clean sync base means the intended
downstream commit and a deliberately reviewed working tree, not an erased
working tree.

## 2. Compare without merging

After confirming the remote names and the upstream branch, fetch only the
required upstream ref if a current comparison is needed:

```powershell
git fetch upstream master
git log --oneline --decorate HEAD..upstream/master
git diff --stat HEAD...upstream/master
git diff --name-status HEAD...upstream/master
```

Read the upstream commits and file-level diff before choosing a sync strategy.
Do not treat the historical review SHA or an old prompt line number as proof
that the same issue still exists.

## 3. Review downstream invariants before applying changes

For each upstream change, check whether it touches:

- `DownstreamPolicy`, `ProductIdentity`, settings migration, defaults, or local
  logging;
- media-session filtering, selection mode, ownership, deduplication, and
  session-close/restart behavior;
- optional window, timer, audio-device, capture, callback, and shutdown
  lifecycles;
- `ProductVersion`, the metadata-only updater, ZIP contents, and source SHA; or
- the retained MSIX/Store workflows and upstream contribution/CLA files.

Prefer a small adapter or an isolated policy conflict resolution. Do not copy
an upstream behavior that re-enables telemetry, experiments, upstream update
checks, Store purchase UI, or unsupported release paths. Do not rewrite the
media state machine to resolve a documentation or merge conflict.

## 4. Validate the selected sync

Record the selected upstream commit range and every conflict decision. Then run
the applicable Windows checks from [`AGENTS.md`](../../AGENTS.md), including the
focused tests for changed behavior, `dotnet format`, and `git diff --check`.
Recheck the downstream release script and workflow conditions. Use a controlled
Windows profile for migration, coexistence, audio, GUI, and network claims;
builds and static inspection do not replace those observations.

Only after review should a separately authorized branch be pushed or a pull
request opened. Do not merge a sync automatically, and never force-push the
published downstream `master` history.
