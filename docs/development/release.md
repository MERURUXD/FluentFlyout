# Downstream release process

This checkout publishes a separate Windows x64 ZIP channel for the
`MERURUXD/FluentFlyout` repository. It does not use the FluentFlyout Store,
MSIX signing certificate, website dispatch, or upstream release workflow.

## Local release build

The CI and manual stable-release commands use the `GitHub Release`
configuration, a self-contained `win-x64` publish, and the semantic version
from the downstream MSIX manifest. Replace `X.Y.Z` with the intended release
version:

```powershell
$version = 'X.Y.Z'
$publishDir = Join-Path $env:TEMP 'FluentFlyoutDownstream-release'
$archive = Join-Path $env:TEMP "FluentFlyoutDownstream-$version-win-x64.zip"

dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64 -r win-x64
dotnet publish FluentFlyoutWPF\FluentFlyout.csproj `
  --configuration 'GitHub Release' `
  --framework net10.0-windows10.0.22000.0 `
  --runtime win-x64 `
  --self-contained true `
  --no-restore `
  --output $publishDir `
  -p:Platform=x64 `
  -p:Version=$version
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archive -Force
```

The output should contain `FluentFlyoutDownstream.exe`. This is an unpackaged
portable build: it is not an installer and it does not replace an installed
MSIX package automatically.

## Rolling `dev` release

`.github/workflows/downstream-dev-release.yml` runs only for `master` pushes
or a manually dispatched workflow whose selected ref is `master`. It builds
the self-contained ZIP first, then force-moves the lightweight `dev` tag and
creates or refreshes one GitHub prerelease with the stable asset name
`FluentFlyoutDownstream-dev-win-x64.zip`. The asset is replaced with
`--clobber` after a successful build, and the release notes include the commit
SHA.

Pull requests never publish. The publisher job alone has `contents: write` and
uses the built-in `GITHUB_TOKEN`; no PAT, signing secret, or downstream release
secret is required. The existing `MSIX_CERT_BASE64` secret remains scoped to
the separate upstream-derived MSIX workflow and is not needed for this ZIP
channel.

The `dev` prerelease is intentionally excluded from the updater's stable
`releases/latest` lookup. A failed build does not move the tag or replace the
previous rolling artifact.

## Manual stable release

Stable releases are manual and use immutable semantic tags:

1. Update `FluentFlyoutMSIX\Package.appxmanifest` to
   `Version="X.Y.Z.0"`.
2. Verify that the checkout points to the downstream repository and that the
   authenticated GitHub account can push tags and create releases in
   `MERURUXD/FluentFlyout`. No upstream account, PAT, signing credential, or
   upstream repository permission is required:

   ```powershell
   $origin = (git remote get-url origin).Trim()
   if ($origin -notmatch '(?i)github\.com[/:]MERURUXD/FluentFlyout(?:\.git)?$') {
     throw "Refusing to publish: origin is not MERURUXD/FluentFlyout ($origin)"
   }
   ```

3. Check that the working tree is the intended `master` state, run
   `git diff --check`, and run the local release build above.
4. Create and push the tag only after the build succeeds:

   ```powershell
   git status --short
   git branch --show-current
   git tag -a vX.Y.Z -m "FluentFlyout Downstream vX.Y.Z"
   git push origin vX.Y.Z
   ```

4. Create the release and upload the ZIP from the downstream repository:

   ```powershell
   gh release create vX.Y.Z `
     "<path-to>\FluentFlyoutDownstream-X.Y.Z-win-x64.zip" `
     --repo MERURUXD/FluentFlyout `
     --verify-tag `
     --title "FluentFlyout Downstream vX.Y.Z" `
     --notes "Stable downstream x64 release vX.Y.Z."
   ```

Do not move or reuse a published stable tag. The updater only reads the latest
stable release metadata, opens the fixed downstream release page, and never
downloads, executes, or installs an update.

## Validation boundary

The repository can validate source/build behavior locally and in the existing
Windows CI. It does not claim a GitHub-hosted workflow run, real `dev` release
replacement, stable release publication, desktop notification behavior,
network behavior, MSIX signing, Store publishing, or installation unless that
operation is separately exercised and recorded.
