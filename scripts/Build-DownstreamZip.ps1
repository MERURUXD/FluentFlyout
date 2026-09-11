[CmdletBinding()]
param(
    [ValidateSet('development', 'dev', 'stable')]
    [string]$Channel = 'development',
    [string]$ExpectedCommit,
    [string]$OutputDirectory,
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
trap {
    Write-Error $_
    exit 1
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'FluentFlyoutWPF\FluentFlyout.csproj'
$manifestPath = Join-Path $repoRoot 'FluentFlyoutMSIX\Package.appxmanifest'
$framework = 'net10.0-windows10.0.22000.0'
$runtime = 'win-x64'

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Command $($Arguments -join ' ')"
    }
}

function Get-NativeText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $text = (& $Command @Arguments | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $Command $($Arguments -join ' ')"
    }

    return $text
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "WPF project was not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "MSIX manifest was not found: $manifestPath"
}

$head = Get-NativeText 'git' @('-C', $repoRoot, 'rev-parse', 'HEAD')
if ($head -notmatch '^[0-9a-fA-F]{40}$') {
    throw "The current checkout did not produce a full commit SHA: '$head'"
}

$head = $head.ToLowerInvariant()
$expected = if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $head
} else {
    $ExpectedCommit.Trim().ToLowerInvariant()
}

if ($expected -notmatch '^[0-9a-f]{40}$') {
    throw "ExpectedCommit must be a full 40-character commit SHA: '$ExpectedCommit'"
}

if ($expected -ne $head) {
    throw "ExpectedCommit '$expected' does not match checkout HEAD '$head'"
}

$sourceStatusLines = @(& git -C $repoRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the checkout source state.'
}

$sourceState = if ([string]::IsNullOrWhiteSpace(($sourceStatusLines -join "`n"))) {
    'clean'
} else {
    'dirty'
}

if ($Channel -in @('dev', 'stable') -and $sourceState -ne 'clean') {
    throw "The $Channel channel requires a clean working tree, including untracked files."
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
if ($null -eq $identity) {
    throw "Package identity was not found in $manifestPath"
}

$fullVersion = [string]$identity.GetAttribute('Version')
if ($fullVersion -notmatch '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)\.0$') {
    throw "Expected a MAJOR.MINOR.PATCH.0 manifest version, got '$fullVersion'"
}

$baseVersion = "$($matches['major']).$($matches['minor']).$($matches['patch'])"
$shortCommit = $head.Substring(0, 7)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "FluentFlyoutDownstream-publish-$([Guid]::NewGuid().ToString('N'))"
}

$publishDir = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $publishDir) {
    throw "Refusing to reuse an existing publish directory: $publishDir"
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path ([IO.Path]::GetTempPath()) "FluentFlyoutDownstream-$Channel-$baseVersion-$shortCommit-$([Guid]::NewGuid().ToString('N')).zip"
}

$archivePath = [IO.Path]::GetFullPath($ArchivePath)
$checksumPath = "$archivePath.sha256"
if (Test-Path -LiteralPath $archivePath) {
    throw "Refusing to overwrite an existing archive: $archivePath"
}

if (Test-Path -LiteralPath $checksumPath) {
    throw "Refusing to overwrite an existing checksum: $checksumPath"
}

$archiveParent = Split-Path -Parent $archivePath
if (-not [string]::IsNullOrWhiteSpace($archiveParent)) {
    New-Item -ItemType Directory -Path $archiveParent -Force | Out-Null
}

Write-Host "Restoring $projectPath for $runtime..."
Invoke-Native 'dotnet' @('restore', $projectPath, '-p:Platform=x64', '-r', $runtime)

Write-Host "Publishing $Channel build at $head..."
Invoke-Native 'dotnet' @(
    'publish',
    $projectPath,
    '--configuration', 'GitHub Release',
    '--framework', $framework,
    '--runtime', $runtime,
    '--self-contained', 'true',
    '--no-restore',
    '--output', $publishDir,
    '-p:Platform=x64',
    "-p:Version=$baseVersion",
    "-p:DownstreamBuildChannel=$Channel",
    "-p:DownstreamBuildVersion=$baseVersion",
    "-p:DownstreamSourceRevision=$head"
)

$executablePath = Join-Path $publishDir 'FluentFlyoutDownstream.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "The downstream executable was not produced: $executablePath"
}

$distributionDir = Join-Path $repoRoot 'distribution'
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $publishDir 'LICENSE')
Copy-Item -LiteralPath (Join-Path $distributionDir 'README.txt') -Destination (Join-Path $publishDir 'README.txt')
Copy-Item -LiteralPath (Join-Path $distributionDir 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $publishDir 'THIRD-PARTY-NOTICES.txt')

$licenseRoot = Join-Path $publishDir 'licenses'
$dotnetLicenseDir = Join-Path $licenseRoot 'dotnet'
$nugetLicenseDir = Join-Path $licenseRoot 'nuget'
New-Item -ItemType Directory -Path $dotnetLicenseDir, $nugetLicenseDir -Force | Out-Null

$dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction Stop
$dotnetPath = if (-not [string]::IsNullOrWhiteSpace($dotnetCommand.Path)) {
    $dotnetCommand.Path
} else {
    $dotnetCommand.Source
}
$dotnetRoot = Split-Path -Parent $dotnetPath
$dotnetLicensePath = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNoticePath = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
foreach ($requiredDotnetFile in @($dotnetLicensePath, $dotnetNoticePath)) {
    if (-not (Test-Path -LiteralPath $requiredDotnetFile -PathType Leaf)) {
        throw "Required .NET runtime notice file was not found: $requiredDotnetFile"
    }
}
Copy-Item -LiteralPath $dotnetLicensePath -Destination (Join-Path $dotnetLicenseDir 'LICENSE.txt')
Copy-Item -LiteralPath $dotnetNoticePath -Destination (Join-Path $dotnetLicenseDir 'ThirdPartyNotices.txt')

$assetsPath = Join-Path $repoRoot 'FluentFlyoutWPF\obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw "Restore did not produce the dependency graph: $assetsPath"
}

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$targetName = "$framework/$runtime"
$targetProperty = $assets.targets.PSObject.Properties | Where-Object { $_.Name -eq $targetName } | Select-Object -First 1
if ($null -eq $targetProperty) {
    throw "Dependency graph does not contain the expected target '$targetName'"
}

$packageKeys = @($targetProperty.Value.PSObject.Properties.Name | Where-Object {
    $_ -match '^[^/]+/[^/]+$' -and $_ -notmatch '^runtime\.'
} | Sort-Object)
if ($packageKeys.Count -eq 0) {
    throw "No NuGet runtime packages were found in the dependency graph"
}

$nugetRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    [IO.Path]::GetFullPath($env:NUGET_PACKAGES)
} else {
    Join-Path $env:USERPROFILE '.nuget\packages'
}

$resolvedLines = [System.Collections.Generic.List[string]]::new()
$resolvedLines.Add("Resolved runtime packages for $head")
$resolvedLines.Add('')

foreach ($packageKey in $packageKeys) {
    $packageParts = $packageKey -split '/', 2
    $packageId = $packageParts[0]
    $packageVersion = $packageParts[1]
    $packageDir = Join-Path (Join-Path $nugetRoot $packageId.ToLowerInvariant()) $packageVersion
    if (-not (Test-Path -LiteralPath $packageDir -PathType Container)) {
        throw "Restored package directory was not found: $packageDir"
    }

    $nuspec = Get-ChildItem -LiteralPath $packageDir -Filter '*.nuspec' -File | Select-Object -First 1
    if ($null -eq $nuspec) {
        throw "Restored package has no nuspec: $packageKey"
    }

    [xml]$nuspecXml = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $nuspecXml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "NuGet package has no metadata node: $packageKey"
    }

    $licenseNode = $metadata.SelectSingleNode("./*[local-name()='license']")
    $licenseExpression = if ($null -eq $licenseNode) { '' } else { $licenseNode.InnerText.Trim() }
    if ([string]::IsNullOrWhiteSpace($licenseExpression)) {
        throw "Restored package has no declared license expression: $packageKey"
    }

    $projectUrlNode = $metadata.SelectSingleNode("./*[local-name()='projectUrl']")
    $projectUrl = if ($null -eq $projectUrlNode) { '' } else { $projectUrlNode.InnerText.Trim() }
    if ([string]::IsNullOrWhiteSpace($projectUrl)) {
        $repositoryNode = $metadata.SelectSingleNode("./*[local-name()='repository']")
        if ($repositoryNode -and $repositoryNode.Attributes['url']) {
            $projectUrl = [string]$repositoryNode.Attributes['url'].Value
        }
    }
    $authorsNode = $metadata.SelectSingleNode("./*[local-name()='authors']")
    $authors = if ($null -eq $authorsNode) { '' } else { $authorsNode.InnerText.Trim() }
    $licenseType = if ($licenseNode -and $licenseNode.Attributes['type']) {
        [string]$licenseNode.Attributes['type'].Value
    } else {
        'declared'
    }
    $licenseFiles = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse | Where-Object {
        $_.Name -match '(?i)^(LICENSE|COPYING|NOTICE|THIRD-PARTY-NOTICES)'
    })

    $copiedNames = [System.Collections.Generic.List[string]]::new()
    foreach ($licenseFile in $licenseFiles) {
        $destinationName = "$packageId-$packageVersion-$($licenseFile.Name)"
        Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $nugetLicenseDir $destinationName)
        $copiedNames.Add($destinationName)
    }

    $resolvedLines.Add("$packageId $packageVersion | license=$licenseExpression ($licenseType) | authors=$authors | project=$projectUrl")
    if ($copiedNames.Count -gt 0) {
        $resolvedLines.Add("  package files copied: $($copiedNames -join ', ')")
    }
}

$resolvedPackagePath = Join-Path $nugetLicenseDir 'RESOLVED-PACKAGES.txt'
[IO.File]::WriteAllLines($resolvedPackagePath, $resolvedLines, [Text.UTF8Encoding]::new($false))

$buildIdentity = switch ($Channel) {
    'stable' { "v$baseVersion"; break }
    'dev' { "dev+$shortCommit"; break }
    default { "development+$shortCommit"; break }
}

$releaseInfoLines = @(
    'Product: FluentFlyout Downstream',
    "Channel: $Channel",
    "Version: v$baseVersion",
    "Build identity: $buildIdentity",
    "Commit: $head",
    "Source state: $sourceState",
    "Source: https://github.com/MERURUXD/FluentFlyout/tree/$head",
    $(if ($sourceState -eq 'clean') {
        'Source note: The archive was built from this exact clean commit.'
    } else {
        'Source note: This development archive includes uncommitted working-tree changes; the commit URL is the base source revision, not an exact reproducible source.'
    }),
    'Updater: stable-release metadata only; no automatic download, execution, installation, or replacement.'
)
[IO.File]::WriteAllLines((Join-Path $publishDir 'RELEASE-INFO.txt'), $releaseInfoLines, [Text.UTF8Encoding]::new($false))

$requiredEntries = @(
    'FluentFlyoutDownstream.exe',
    'LICENSE',
    'README.txt',
    'THIRD-PARTY-NOTICES.txt',
    'RELEASE-INFO.txt',
    'licenses/dotnet/LICENSE.txt',
    'licenses/dotnet/ThirdPartyNotices.txt',
    'licenses/nuget/RESOLVED-PACKAGES.txt'
)
foreach ($requiredEntry in $requiredEntries) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir ($requiredEntry -replace '/', '\')) -PathType Leaf)) {
        throw "Required distribution file is missing before archive creation: $requiredEntry"
    }
}

Write-Host "Creating $archivePath..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archivePath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    throw "Archive was not created: $archivePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in $requiredEntries) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Required archive entry is missing: $requiredEntry"
        }
    }

    $releaseEntry = $zip.GetEntry('RELEASE-INFO.txt')
    $releaseStream = $releaseEntry.Open()
    try {
        $releaseInfo = [IO.StreamReader]::new($releaseStream).ReadToEnd()
    } finally {
        $releaseStream.Dispose()
    }

    if ($releaseInfo -notmatch "(?m)^Channel: $([regex]::Escape($Channel))\r?$") {
        throw "RELEASE-INFO.txt does not contain the expected channel '$Channel'"
    }
    if ($releaseInfo -notmatch "(?m)^Commit: $([regex]::Escape($head))\r?$") {
        throw "RELEASE-INFO.txt does not contain checkout HEAD '$head'"
    }
    if ($releaseInfo -notmatch "(?m)^Source state: $([regex]::Escape($sourceState))\r?$") {
        throw "RELEASE-INFO.txt does not contain source state '$sourceState'"
    }
    if ($releaseInfo -notmatch "(?m)^Source: https://github\.com/MERURUXD/FluentFlyout/tree/$([regex]::Escape($head))\r?$") {
        throw 'RELEASE-INFO.txt does not contain the immutable source locator'
    }
    $expectedSourceNote = if ($sourceState -eq 'clean') {
        'The archive was built from this exact clean commit.'
    } else {
        'This development archive includes uncommitted working-tree changes; the commit URL is the base source revision, not an exact reproducible source.'
    }
    if ($releaseInfo -notmatch "(?m)^Source note: $([regex]::Escape($expectedSourceNote))\r?$") {
        throw 'RELEASE-INFO.txt does not truthfully describe source provenance.'
    }
} finally {
    $zip.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$archiveHash *$([IO.Path]::GetFileName($archivePath))"
Set-Content -LiteralPath $checksumPath -Value $checksumLine -NoNewline -Encoding ascii
$recordedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($recordedHash -ne $archiveHash) {
    throw "Checksum verification failed for $archivePath"
}

Write-Output "CHANNEL=$Channel"
Write-Output "MANIFEST_VERSION=$baseVersion"
Write-Output "BUILD_IDENTITY=$buildIdentity"
Write-Output "SOURCE_REVISION=$head"
Write-Output "SOURCE_STATE=$sourceState"
Write-Output "PUBLISH_DIRECTORY=$publishDir"
Write-Output "ARCHIVE=$archivePath"
Write-Output "CHECKSUM=$archiveHash"
Write-Output "CHECKSUM_FILE=$checksumPath"
