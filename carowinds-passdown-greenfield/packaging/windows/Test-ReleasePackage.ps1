#Requires -Version 5.1
<#
.SYNOPSIS
    Validates a portable release folder or ZIP against release-content-rules.json.

.DESCRIPTION
    Phase 18B PKG-002. Fails (exit 1) when the release is missing a required file (executable,
    runtime/native dependencies, scheduler helpers, license notices) or contains anything forbidden:
    PDBs, source, test assemblies, fixtures, databases/WAL/SHM, logs, diagnostics ZIPs, sync data,
    PDFs, Ghostscript artifacts, .DS_Store, macOS/Linux/non-x64 natives, secrets/config, or an
    absolute developer path.

    Phase 18C PKG-003: also fails when a required file is empty, a ZIP has duplicate entries, or a
    third-party license/notice file (noticeFilePatterns) is too short or still contains a template
    placeholder (noticePlaceholderTokens).

    Path scanning: the generic tokens (/Users/, /home/, C:\Users\) are searched in this app's own
    binaries (Carowinds.Passdown.*) and every text file — not in third-party binaries, some of which
    legitimately embed their vendor's build path. The repository checkout path and the current user
    name are searched in every file.

    The rules live in release-content-rules.json next to this script and are the same rules the
    repository's automated tests exercise (ReleasePackageValidator.cs).

.PARAMETER Path
    A release folder (the folder that contains Carowinds.Passdown.Desktop.exe) or a release ZIP
    (whose single top-level folder is that release folder).

.PARAMETER RepositoryRoot
    Optional checkout path to search for in every file. Defaults to two levels above this script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

$rulesPath = Join-Path $PSScriptRoot 'release-content-rules.json'
$rules = Get-Content -LiteralPath $rulesPath -Raw | ConvertFrom-Json
if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
}

$extraTokens = @($RepositoryRoot.TrimEnd('\', '/'))
if ($env:USERNAME -and $env:USERNAME.Length -ge 3) {
    $extraTokens += "\$($env:USERNAME)\"
}

function Test-TokenInBytes([byte[]]$Bytes, [string]$Token) {
    foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode)) {
        # Latin-1 view of the bytes keeps a 1:1 byte↔char mapping, so IndexOf works on binaries.
        $haystack = [Text.Encoding]::GetEncoding(28591).GetString($Bytes)
        $needle = [Text.Encoding]::GetEncoding(28591).GetString($encoding.GetBytes($Token))
        if ($haystack.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

# Collect relative path -> FileInfo / ZipArchiveEntry (both expose .Length).
$files = @{}
$duplicateEntries = New-Object System.Collections.Generic.List[string]
$zip = $null
if ((Test-Path -LiteralPath $Path -PathType Leaf) -and $Path.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    foreach ($entry in $zip.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) { continue }
        $full = $entry.FullName.Replace('\', '/').TrimStart('/')
        $slash = $full.IndexOf('/')
        $relative = if ($slash -ge 0) { $full.Substring($slash + 1) } else { $full }
        if ($files.ContainsKey($relative)) {
            $duplicateEntries.Add("duplicate ZIP entry: $relative")
        }
        $files[$relative] = $entry
    }
}
elseif (Test-Path -LiteralPath $Path -PathType Container) {
    $root = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/')
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $relative = $item.FullName.Substring($root.Length + 1).Replace('\', '/')
        $files[$relative] = $item
    }
}
else {
    Write-Error "Release path not found or not a folder/.zip: '$Path'."
    exit 1
}

function Read-Bytes($source) {
    if ($source -is [IO.FileInfo]) {
        return [IO.File]::ReadAllBytes($source.FullName)
    }
    $stream = $source.Open()
    try {
        $memory = New-Object IO.MemoryStream
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$violations = New-Object System.Collections.Generic.List[string]
foreach ($duplicate in $duplicateEntries) { $violations.Add($duplicate) }
$present = @{}
foreach ($key in $files.Keys) { $present[$key.ToLowerInvariant()] = $files[$key] }

foreach ($required in $rules.requiredFiles) {
    if (-not $present.ContainsKey($required.ToLowerInvariant())) {
        $violations.Add("missing required file: $required")
    }
    elseif ($present[$required.ToLowerInvariant()].Length -eq 0) {
        $violations.Add("empty required file: $required")
    }
}

try {
    foreach ($relative in $files.Keys) {
        $segments = $relative.Split('/')
        $fileName = $segments[-1]

        foreach ($pattern in $rules.forbiddenFileNamePatterns) {
            if ($fileName -like $pattern) {
                $violations.Add("forbidden file ($pattern): $relative")
            }
        }

        for ($i = 0; $i -lt $segments.Length - 1; $i++) {
            foreach ($directory in $rules.forbiddenDirectoryNames) {
                if ($segments[$i] -ieq $directory) {
                    $violations.Add("forbidden directory ($directory): $relative")
                }
            }
            if ($segments[$i] -ieq 'runtimes' -and ($i + 1) -lt ($segments.Length - 1)) {
                foreach ($prefix in $rules.forbiddenRuntimeIdentifierPrefixes) {
                    if ($segments[$i + 1].StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                        $violations.Add("non-win-x64 native runtime ($($segments[$i + 1])): $relative")
                    }
                }
            }
        }

        $scanGeneric = $false
        foreach ($pattern in @($rules.ownBinaryPatterns) + @($rules.textFilePatterns)) {
            if ($fileName -like $pattern) { $scanGeneric = $true }
        }

        $bytes = Read-Bytes $files[$relative]

        # PKG-003: license/notice files must be real, complete text — not a stub or a template.
        $isNotice = $false
        foreach ($pattern in $rules.noticeFilePatterns) {
            if ($relative -like $pattern) { $isNotice = $true }
        }
        if ($isNotice) {
            if ($bytes.Length -lt $rules.minimumNoticeFileBytes) {
                $violations.Add("notice file too short ($($bytes.Length) bytes): $relative")
            }
            $text = [Text.Encoding]::UTF8.GetString($bytes)
            foreach ($placeholder in $rules.noticePlaceholderTokens) {
                if ($text.Contains($placeholder)) {
                    $violations.Add("unresolved placeholder '$placeholder' in notice file $relative")
                }
            }
        }

        $tokens = @($extraTokens)
        if ($scanGeneric) { $tokens += @($rules.forbiddenPathTokens) }
        foreach ($token in $tokens) {
            if ($token -and (Test-TokenInBytes $bytes $token)) {
                $violations.Add("absolute developer path token '$token' in $relative")
            }
        }
    }
}
finally {
    if ($zip) { $zip.Dispose() }
}

if ($violations.Count -gt 0) {
    Write-Host "Release validation FAILED for '$Path' ($($violations.Count) problem(s)):" -ForegroundColor Red
    foreach ($violation in $violations) { Write-Host "  - $violation" }
    exit 1
}

Write-Host "Release validation passed for '$Path' ($($files.Count) files)."
exit 0
