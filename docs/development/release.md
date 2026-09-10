# Downstream release process

This checkout publishes a separate Windows x64 ZIP channel for the
`MERURUXD/FluentFlyout` repository. It does not use the FluentFlyout Store,
MSIX signing certificate, website dispatch, or upstream release workflow.

## Local build and package validation

The shared `scripts/Build-DownstreamZip.ps1` entry point uses the
`GitHub Release` configuration, a self-contained `win-x64` publish, and the
semantic version from `FluentFlyoutMSIX\Package.appxmanifest`. It creates a
unique publish directory, validates the executable and distribution materials,
and writes a sibling SHA-256 file. It never creates tags, pushes, or calls
GitHub Releases.

For a non-mutating development package check at a known checkout SHA:

```powershell
$sha = (git rev-parse HEAD).Trim()
$archive = Join-Path $env:TEMP "FluentFlyoutDownstream-development-$($sha.Substring(0, 7)).zip"
.\scripts\Build-DownstreamZip.ps1 `
  -Channel development `
  -ExpectedCommit $sha `
  -ArchivePath $archive
```

The output contains `FluentFlyoutDownstream.exe`, `LICENSE`, `README.txt`,
`THIRD-PARTY-NOTICES.txt`, `RELEASE-INFO.txt`, the resolved package/runtime
notices, and `$archive.sha256`. The package is portable and is not an
installer; it does not replace an installed MSIX package automatically.

## Rolling `dev` release

`.github/workflows/downstream-dev-release.yml` runs only for `master` pushes
or a manually dispatched workflow whose selected ref is `master`. Its
dependency chain is `quality-dev` → `build-dev` → `publish-dev`: format,
build, and focused tests must pass for the same `GITHUB_SHA` before the
self-contained ZIP is created and validated. The publisher downloads only
that workflow run's artifact, checks its SHA-256 and `RELEASE-INFO.txt`, then
force-moves the lightweight `dev` tag and creates or refreshes one GitHub
prerelease with `FluentFlyoutDownstream-dev-win-x64.zip` and its checksum.

Pull requests never publish. The publisher job alone has `contents: write` and
uses the built-in `GITHUB_TOKEN`; no PAT, signing secret, or downstream release
secret is required. The existing `MSIX_CERT_BASE64` secret remains scoped to
the separate upstream-derived MSIX workflow and is not needed for this ZIP
channel.

The `dev` prerelease carries a non-stable build identity containing the full
source SHA and is intentionally excluded from the updater's stable
`releases/latest` lookup. A failed quality, package, or artifact-integrity
step does not move the tag or replace the previous rolling artifact.

## Manual stable release

Stable releases are manual and use immutable semantic tags. Every validation
failure stops the procedure before tag or release mutation:

1. Update `FluentFlyoutMSIX\Package.appxmanifest` to
   `Version="X.Y.Z.0"`.
2. Commit the version change. The commit is the source revision for the
   release; do not build a stable ZIP from uncommitted version edits.
3. Verify that the checkout points to the downstream repository and that the
   authenticated GitHub account can push tags and create releases in
   `MERURUXD/FluentFlyout`. No upstream account, PAT, signing credential, or
   upstream repository permission is required:

   ```powershell
   $origin = (git remote get-url origin).Trim()
   if ($origin -notmatch '(?i)github\.com[/:]MERURUXD/FluentFlyout(?:\.git)?$') {
     throw "Refusing to publish: origin is not MERURUXD/FluentFlyout ($origin)"
   }
   ```

4. Check that the working tree is the intended `master` state, capture the
   immutable SHA, and run all validation before any tag operation:

   ```powershell
   $sourceStatus = @(git status --porcelain --untracked-files=all)
   if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the working tree.' }
   if (-not [string]::IsNullOrWhiteSpace(($sourceStatus -join "`n"))) {
     throw 'Stable releases require a clean working tree, including untracked files.'
   }
   if ((git branch --show-current).Trim() -ne 'master') { throw 'Stable releases require master.' }
   [xml]$manifest = Get-Content -LiteralPath 'FluentFlyoutMSIX\Package.appxmanifest' -Raw
   $fullVersion = [string]$manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']").GetAttribute('Version')
   if ($fullVersion -notmatch '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)\.0$') {
     throw "Expected a MAJOR.MINOR.PATCH.0 manifest version, got '$fullVersion'"
   }
   $version = "$($matches['major']).$($matches['minor']).$($matches['patch'])"
   $releaseSha = (git rev-parse HEAD).Trim()
   if ($releaseSha -ne (git rev-parse HEAD).Trim()) { throw 'HEAD changed unexpectedly.' }
   git diff --check
   dotnet restore FluentFlyoutWPF\FluentFlyout.csproj -p:Platform=x64
   dotnet build FluentFlyoutWPF\FluentFlyout.csproj -c 'GitHub Release' -p:Platform=x64 --no-restore
   dotnet test tests\FluentFlyoutWPF.Tests\FluentFlyoutWPF.Tests.csproj -c 'GitHub Release' -p:Platform=x64 --no-restore
   dotnet format FluentFlyout.sln --verify-no-changes --verbosity diagnostic
   $archive = Join-Path $env:TEMP "FluentFlyoutDownstream-$version-win-x64.zip"
   .\scripts\Build-DownstreamZip.ps1 `
     -Channel stable `
     -ExpectedCommit $releaseSha `
     -ArchivePath $archive
   $sourceStatusAfter = @(git status --porcelain --untracked-files=all)
   if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($sourceStatusAfter -join "`n"))) {
     throw 'The working tree changed or could not be inspected during validation.'
   }
   if ($releaseSha -ne (git rev-parse HEAD).Trim()) { throw 'HEAD changed during validation.' }
   Get-FileHash -LiteralPath $archive -Algorithm SHA256
   Get-Content -LiteralPath "$archive.sha256"
   ```

5. Confirm that `vX.Y.Z` does not already exist, then — only with separate
   release authorization — create and push the immutable tag:

   ```powershell
   $existingTag = git ls-remote --tags origin 'refs/tags/vX.Y.Z'
   if ($LASTEXITCODE -ne 0) { throw 'Could not query origin tags; refusing to publish.' }
   if (-not [string]::IsNullOrWhiteSpace(($existingTag -join ''))) {
     throw 'Stable tag already exists; refusing to reuse it.'
   }
   git tag -a vX.Y.Z $releaseSha -m "FluentFlyout Downstream vX.Y.Z"
   git push origin vX.Y.Z
   ```

6. Create the release and upload the ZIP plus checksum from the downstream
   repository:

   ```powershell
   gh release create vX.Y.Z `
     $archive `
     "$archive.sha256" `
     --repo MERURUXD/FluentFlyout `
     --verify-tag `
     --title "FluentFlyout Downstream vX.Y.Z" `
     --notes "Stable downstream x64 release vX.Y.Z. Source: https://github.com/MERURUXD/FluentFlyout/tree/$releaseSha"
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
