# Contributing to FluentFlyout Downstream

Thank you for helping improve this personal downstream fork. Keep changes
narrow, preserve the GPL-3.0-or-later license and upstream attribution, and
describe which product and release channel you tested.

## Choose the right project

- Report downstream-only behavior, downstream documentation, policy, identity,
  ZIP, or CI issues in the [MERURUXD/FluentFlyout issue chooser](https://github.com/MERURUXD/FluentFlyout/issues/new/choose).
- Send a change intended to benefit the general FluentFlyout project to the
  [upstream repository](https://github.com/unchihugo/FluentFlyout) and follow
  its contribution and CLA requirements. A downstream PR is not an upstream
  contribution agreement.
- Use the upstream [Weblate project](https://hosted.weblate.org/engage/fluentflyout/)
  for translations unless the change is specifically downstream-only.

## Local development

Use Windows with the .NET 10 SDK. Keep the application closed while testing;
it is a single-instance tray application. From the repository root, the
supported downstream compile/test entry points are:

```powershell
dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64
dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
dotnet test tests\FluentFlyoutWPF.Tests\FluentFlyoutWPF.Tests.csproj -c "GitHub Release" -p:Platform=x64 --no-restore
dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic
git diff --check
```

The `GitHub Release` configuration is the supported downstream x64 build
configuration. The portable ZIP is separate from the retained upstream-derived
MSIX/Store workflows; do not request signing secrets or publish a release from
an ordinary contribution.

## Issue reports

Use the [bug report form](https://github.com/MERURUXD/FluentFlyout/issues/new?template=bug_report.yaml)
for reproducible defects. Include the downstream channel, version and commit
SHA, Windows build, architecture, relevant settings, reproduction steps, and
whether the issue occurs after enabling then disabling an optional feature.
Attach only sanitized logs; remove account names, file paths, media titles,
tokens, and other personal information. A build passing does not replace a
Windows desktop, audio, media-session, or network observation.

## Pull requests

Use a scoped branch and keep one stage or independent issue per PR. The PR
description should separate:

- static inspection and documentation checks;
- restore, build, format, and focused automated tests;
- Windows desktop/audio/network measurements; and
- scenarios that were not run or are blocked by environment or dependency.

Preserve existing media behavior unless the requested stage explicitly covers
it. Do not merge a documented plan as if it were a completed runtime test, and
do not create releases, move tags, or publish packages as part of review.

AI tools may assist with planning or implementation, but contributors remain
responsible for understanding, checking, and explaining every proposed change.
