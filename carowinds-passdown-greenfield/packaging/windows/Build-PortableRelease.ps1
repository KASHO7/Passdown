#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the versioned portable Windows release ZIP for Carowinds Passdown.

.DESCRIPTION
    Phase 7B release packaging (AGENT_BUILD_SPEC.md Phase 7, docs/release-strategy.md). This is
    the "extract and run" deliverable — no installer, no admin rights, no separate .NET install.

    Reuses Publish-Windows.ps1 for the actual `dotnet publish` step rather than duplicating its
    publish rules (it already handles the win-x64 self-contained publish and the .pdb cleanup).
    This script only adds packaging on top: a clean versioned staging folder, the user-facing
    README and scheduled-refresh helper scripts, and the final ZIP.

    Version comes from Directory.Build.props' single authoritative <Version> element — this
    script does not define its own version number (CLAUDE.md/spec: avoid duplicating version
    numbers across scripts).

.PARAMETER SkipPublish
    Skips re-running Publish-Windows.ps1 and packages whatever is already in
    artifacts/publish/win-x64. Useful for iterating on packaging alone. Default: publish is
    always run first, so the ZIP never silently ships stale/prior-build output.

.EXAMPLE
    .\Build-PortableRelease.ps1

.EXAMPLE
    .\Build-PortableRelease.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

# Repo root is two levels up from this script (packaging/windows/Build-PortableRelease.ps1) —
# never a developer-specific path, and correct regardless of the caller's current directory.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$publishScript = Join-Path $repoRoot 'packaging' 'windows' 'Publish-Windows.ps1'
$buildPropsPath = Join-Path $repoRoot 'Directory.Build.props'
$publishDirectory = Join-Path $repoRoot 'artifacts' 'publish' 'win-x64'
$releasesDirectory = Join-Path $repoRoot 'artifacts' 'releases'

if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
    Write-Error "Publish-Windows.ps1 not found at expected path: '$publishScript'."
    exit 1
}

if (-not (Test-Path -LiteralPath $buildPropsPath -PathType Leaf)) {
    Write-Error "Directory.Build.props not found at expected path: '$buildPropsPath'."
    exit 1
}

# Single authoritative version source (Directory.Build.props' <Version>) — never a second,
# independently-maintained version string in this script.
[xml]$buildProps = Get-Content -LiteralPath $buildPropsPath
$version = $buildProps.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Error "Could not read <Version> from '$buildPropsPath'."
    exit 1
}
$version = $version.Trim()

$releaseName = "Carowinds-Passdown-$version-win-x64"
Write-Host "Building portable release: $releaseName"

if (-not $SkipPublish) {
    Write-Host ""
    Write-Host "Running Publish-Windows.ps1..."
    & $publishScript
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish-Windows.ps1 failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path -LiteralPath $publishDirectory)) {
    Write-Error "Publish output not found at '$publishDirectory'. Run without -SkipPublish first."
    exit 1
}

$executablePath = Join-Path $publishDirectory 'Carowinds.Passdown.Desktop.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    Write-Error "Expected executable not found at '$executablePath'."
    exit 1
}

# Clean staging: only this script's own version-specific staging path under artifacts/, never
# user data and never any other artifacts/ content (CLAUDE.md "Do not overwrite unrelated user
# work").
$stagingRoot = Join-Path $repoRoot 'artifacts' 'release-staging'
$stagingReleaseDirectory = Join-Path $stagingRoot $releaseName
if (Test-Path -LiteralPath $stagingReleaseDirectory) {
    Remove-Item -LiteralPath $stagingReleaseDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingReleaseDirectory -Force | Out-Null

Write-Host ""
Write-Host "Staging publish output into: $stagingReleaseDirectory"
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingReleaseDirectory -Recurse -Force

# Defensive: Publish-Windows.ps1 already removes .pdb files, but this guards the ZIP itself
# against ever shipping one even if that step is skipped or changes in the future.
$strayPdbs = Get-ChildItem -LiteralPath $stagingReleaseDirectory -Filter '*.pdb' -Recurse -File
if ($strayPdbs.Count -gt 0) {
    $strayPdbs | Remove-Item -Force
    Write-Host "Removed $($strayPdbs.Count) stray .pdb file(s) from staging."
}

# Developer-oriented scheduled-refresh scripts (Part E/F/L). These already accept an absolute
# -ExecutablePath and never hard-code a developer path, so they work unmodified against the
# extracted location. Placed under tools\ (not the release root) so a nontechnical user isn't
# asked to run anything just to launch the app normally, and isn't tempted to open a raw .ps1
# directly (see the double-click helpers below for the actual user-facing entry point).
$toolsDirectory = Join-Path $stagingReleaseDirectory 'tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot 'packaging' 'windows' 'Install-ScheduledRefresh.ps1') -Destination $toolsDirectory -Force
Copy-Item -Path (Join-Path $repoRoot 'packaging' 'windows' 'Uninstall-ScheduledRefresh.ps1') -Destination $toolsDirectory -Force

# User-facing double-click setup helpers (Phase 7C Part E/F/L). These live at the release root,
# next to the .exe, so a nontechnical user finds them immediately without opening tools\ or
# PowerShell. Each resolves its own directory and the app's tools\*.ps1 scripts relative to
# itself (Part C) — nothing here is copied from a developer-specific path, and the build fails
# loudly below if either file is missing rather than silently shipping a ZIP without them.
$enableHelperSource = Join-Path $repoRoot 'packaging' 'windows' 'Enable Automatic Refresh.cmd'
$disableHelperSource = Join-Path $repoRoot 'packaging' 'windows' 'Disable Automatic Refresh.cmd'
if (-not (Test-Path -LiteralPath $enableHelperSource -PathType Leaf)) {
    Write-Error "Enable Automatic Refresh.cmd not found at '$enableHelperSource'."
    exit 1
}
if (-not (Test-Path -LiteralPath $disableHelperSource -PathType Leaf)) {
    Write-Error "Disable Automatic Refresh.cmd not found at '$disableHelperSource'."
    exit 1
}
Copy-Item -Path $enableHelperSource -Destination $stagingReleaseDirectory -Force
Copy-Item -Path $disableHelperSource -Destination $stagingReleaseDirectory -Force

# User-facing README (Part F). Not the developer/architecture docs under docs/.
$readmeSource = Join-Path $repoRoot 'packaging' 'windows' 'README-portable-release.md'
if (-not (Test-Path -LiteralPath $readmeSource -PathType Leaf)) {
    Write-Error "Portable release README not found at '$readmeSource'."
    exit 1
}
Copy-Item -Path $readmeSource -Destination (Join-Path $stagingReleaseDirectory 'README.txt') -Force

# Fail loudly if anything unexpected snuck into staging (Part M privacy/security review): the
# staging folder is built solely from Publish-Windows.ps1's own output plus the two additions
# above, so this is a sanity check, not the primary safeguard.
$forbiddenPatterns = @('*.git*', '*.db', '*.db-wal', '*.db-shm', '*CLAUDE.md', '*AGENT_BUILD_SPEC.md')
foreach ($pattern in $forbiddenPatterns) {
    $matches = Get-ChildItem -LiteralPath $stagingReleaseDirectory -Filter $pattern -Recurse -Force -ErrorAction SilentlyContinue
    if ($matches.Count -gt 0) {
        Write-Error "Staging directory unexpectedly contains files matching '$pattern': $($matches.FullName -join ', ')"
        exit 1
    }
}

if (-not (Test-Path -LiteralPath $releasesDirectory)) {
    New-Item -ItemType Directory -Path $releasesDirectory -Force | Out-Null
}

$zipPath = Join-Path $releasesDirectory "$releaseName.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Write-Host ""
Write-Host "Creating ZIP: $zipPath"
Compress-Archive -Path $stagingReleaseDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$stagedFiles = Get-ChildItem -LiteralPath $stagingReleaseDirectory -Recurse -File
$fileCount = $stagedFiles.Count
$uncompressedBytes = ($stagedFiles | Measure-Object -Property Length -Sum).Sum
$uncompressedMegabytes = [Math]::Round($uncompressedBytes / 1MB, 1)
$zipBytes = (Get-Item -LiteralPath $zipPath).Length
$zipMegabytes = [Math]::Round($zipBytes / 1MB, 1)

Write-Host ""
Write-Host "Portable release build complete."
Write-Host "  ZIP path:            $zipPath"
Write-Host "  Uncompressed size:   $uncompressedMegabytes MB"
Write-Host "  ZIP size:            $zipMegabytes MB"
Write-Host "  File count:          $fileCount"
Write-Host ""
Write-Host "Extract the ZIP anywhere writable and launch Carowinds.Passdown.Desktop.exe directly."
Write-Host "Double-click 'Enable Automatic Refresh.cmd' / 'Disable Automatic Refresh.cmd' at the"
Write-Host "release root for optional background refresh setup (no PowerShell typing required)."
Write-Host "Windows-host runtime validation is deferred to Phase 7E (docs/release-strategy.md)."
