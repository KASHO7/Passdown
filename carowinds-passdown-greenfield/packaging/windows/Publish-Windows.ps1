#Requires -Version 5.1
<#
.SYNOPSIS
    Produces a reproducible self-contained win-x64 Release publish of Carowinds Operations into a
    clean staging directory.

.DESCRIPTION
    Phase 7A release publish (AGENT_BUILD_SPEC.md Phase 7, docs/release-strategy.md). This script
    only builds/publishes the app to a staging folder — it does not create a ZIP or installer,
    does not register the Windows Task Scheduler task (see Install-ScheduledRefresh.ps1, a
    separate later step against the resulting executable path), and does not touch any user
    application data. It requires no administrator rights.

    Publishes only the Desktop project (not the whole solution) with:
      -c Release -r win-x64 --self-contained true

    -p:PlaywrightPlatform=win-x64 is passed explicitly so the publish output only ever contains
    the win-x64 Playwright Node driver. Without it, publishing from a non-Windows dev host (the
    normal case per CLAUDE.md "Development host: Fedora"/this repo's actual macOS/Linux dev
    machines) also bundles the *host* platform's Playwright driver alongside win-x64's — verified
    empirically during Phase 7A (a clean win-x64 publish from a macOS host pulled in both
    .playwright\node\win32_x64 and .playwright\node\darwin-arm64, adding 100+ MB of Playwright
    binaries that can never run on the shipped Windows target). This flag is harmless when
    publishing directly on a Windows host, where it already resolves to the same single platform.

    .pdb files are deleted from the staging output after publish (Part H: "DO NOT SHIP .pdb unless
    deliberately retained" — none are). They remain available for local debugging in each project's
    own bin/ output, which this script never touches.

.PARAMETER OutputDirectory
    Staging directory the publish output is written to. Defaults to
    <repo root>/artifacts/publish/win-x64 (gitignored — see docs/release-strategy.md "Release
    staging directory"). Only this script's own staging output under artifacts/publish/win-x64 is
    cleaned before publishing; nothing else under artifacts/ or elsewhere in the repo is touched.

.EXAMPLE
    .\Publish-Windows.ps1

.EXAMPLE
    .\Publish-Windows.ps1 -OutputDirectory C:\Temp\carowinds-passdown-release
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

# Repo root is two levels up from this script (packaging/windows/Publish-Windows.ps1) — never a
# developer-specific path, and correct regardless of the caller's current directory.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$desktopProject = Join-Path $repoRoot 'src' 'Carowinds.Passdown.Desktop' 'Carowinds.Passdown.Desktop.csproj'

if (-not (Test-Path -LiteralPath $desktopProject -PathType Leaf)) {
    Write-Error "Desktop project not found at expected path: '$desktopProject'."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts' 'publish' 'win-x64'
}

# Clean only this script's own staging output — never any other artifacts/ content, and never a
# source or bin/obj directory (CLAUDE.md "Do not overwrite unrelated user work").
if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host "Publishing Carowinds Operations (Release, win-x64, self-contained)..."
Write-Host "  Project: $desktopProject"
Write-Host "  Output:  $OutputDirectory"

& dotnet publish $desktopProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $OutputDirectory `
    -p:PlaywrightPlatform=win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$removedPdbs = Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.pdb' -Recurse -File
if ($removedPdbs.Count -gt 0) {
    $removedPdbs | Remove-Item -Force
    Write-Host "Removed $($removedPdbs.Count) .pdb file(s) from the staging output (not shipped)."
}

$executablePath = Join-Path $OutputDirectory 'Carowinds.Passdown.Desktop.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    Write-Error "Publish completed but the expected executable was not found at: '$executablePath'."
    exit 1
}

$fileCount = (Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File).Count
$totalBytes = (Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
$totalMegabytes = [Math]::Round($totalBytes / 1MB, 1)

Write-Host ""
Write-Host "Publish complete."
Write-Host "  Executable: $executablePath"
Write-Host "  Files:      $fileCount"
Write-Host "  Size:       $totalMegabytes MB"
Write-Host ""
Write-Host "This is a staging directory, not a release ZIP/installer (Phase 7A scope). Next steps"
Write-Host "(later phases): zip this directory for distribution, and separately run"
Write-Host "  Install-ScheduledRefresh.ps1 -ExecutablePath `"$executablePath`""
Write-Host "against the final installed location once one is chosen."
