#Requires -Version 5.1
<#
.SYNOPSIS
    Phase 30 Morning Message Sync - Windows VM helper for the two-account test.

.DESCRIPTION
    Modes:

    Prepare      (elevated; app closed in BOTH accounts) Backs up both accounts' app data
                 (%LOCALAPPDATA%\Carowinds Passdown) and the existing shared sync folder, verifies
                 every copied file by SHA-256, records a fingerprint of the existing shared sync
                 folder, pauses Personal Sync in both accounts (PersonalSyncEnabled -> false in
                 sync-config.json; folder kept) so the first launch cannot touch the existing
                 shared folder, checks permissions, and only then creates a NEW EMPTY test folder
                 and gives both accounts Modify on that new folder only. Changes nothing else.

    CheckAccess  (run in EACH account, app closed) Proves this account can create and delete a
                 file in the test folder and in every subfolder the app created there (also ones
                 the other account created). Writes and immediately deletes one probe file per
                 folder. Nothing else is changed.

    Inspect      (any account; elevated shows both accounts) Read-only. Shows this account's sync
                 settings, the test folder's contents, and for each computer's
                 morning-message\<id>.json the dates and field names with text LENGTHS only.

    VerifyOld    (elevated) Read-only. Confirms the existing shared sync folder still matches the
                 fingerprint recorded by Prepare (previous pricing and draft test data undisturbed).

    ASCII only on purpose (Windows PowerShell 5.1 reads a .ps1 without a BOM as ANSI).
    Never prints message text and never deletes app data. The only app setting it changes is the
    Personal Sync pause in Prepare, after the backup is verified.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Prepare', 'CheckAccess', 'Inspect', 'VerifyOld')]
    [string]$Mode,

    [string[]]$Accounts = @('Kishan', 'CarowindsTestB'),
    [string]$SharedRoot = 'C:\Users\Public\CarowindsSyncTest',
    [string]$TestFolderName = 'Phase30-MorningMessage-Test',
    [string]$EvidenceRoot = 'C:\CarowindsEvidence'
)

$ErrorActionPreference = 'Stop'
$SyncFolderName = 'Carowinds Operations Sync'
$AppDataFolderName = 'Carowinds Passdown'
$TestFolder = Join-Path $SharedRoot $TestFolderName
$OldSyncFolder = Join-Path $SharedRoot $SyncFolderName
$FingerprintFile = Join-Path $EvidenceRoot 'phase30-old-sync-folder-fingerprint.csv'

# Every run leaves a report on this account's Desktop (Desktop\Phase30-results).
$resultsDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Phase30-results'
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
Start-Transcript -LiteralPath (Join-Path $resultsDir ("{0}-{1}-{2}.txt" -f $Mode, $env:USERNAME, (Get-Date -Format 'yyyyMMdd-HHmmss'))) | Out-Null

function Write-Result([string]$status, [string]$text) {
    $color = 'Gray'
    if ($status -eq 'PASS') { $color = 'Green' }
    elseif ($status -eq 'FAIL') { $color = 'Red' }
    elseif ($status -eq 'WARN') { $color = 'Yellow' }
    Write-Host ('[{0}] {1}' -f $status, $text) -ForegroundColor $color
}

function Test-IsAdministrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ProfileAppData([string]$account) {
    return Join-Path (Join-Path (Join-Path (Join-Path 'C:\Users' $account) 'AppData') 'Local') $AppDataFolderName
}

function Get-RelativeHashes([string]$root) {
    $rows = @()
    if (-not (Test-Path -LiteralPath $root)) { return $rows }
    $prefix = (Get-Item -LiteralPath $root).FullName.TrimEnd('\') + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName)) {
        $rows += [pscustomobject]@{
            Path   = $file.FullName.Substring($prefix.Length)
            Bytes  = $file.Length
            SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $rows
}

function Compare-HashSets($expected, $actual) {
    $problems = @()
    $actualByPath = @{}
    foreach ($row in $actual) { $actualByPath[$row.Path] = $row }
    $expectedPaths = @{}
    foreach ($row in $expected) {
        $expectedPaths[$row.Path] = $true
        if (-not $actualByPath.ContainsKey($row.Path)) { $problems += "missing: $($row.Path)" }
        elseif ($actualByPath[$row.Path].SHA256 -ne $row.SHA256) { $problems += "changed: $($row.Path)" }
    }
    foreach ($row in $actual) {
        if (-not $expectedPaths.ContainsKey($row.Path)) { $problems += "added: $($row.Path)" }
    }
    return $problems
}

function Read-JsonFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json) }
    catch { return 'UNREADABLE' }
}

function Show-SyncConfig([string]$account, [string]$appData) {
    $config = Read-JsonFile (Join-Path $appData 'sync-config.json')
    $deviceFile = Join-Path $appData 'device-id.txt'
    $device = '(none yet)'
    if (Test-Path -LiteralPath $deviceFile) { $device = (Get-Content -LiteralPath $deviceFile -Raw).Trim() }
    Write-Host ''
    Write-Host "Account $account  ($appData)"
    Write-Host "  Sync device id:            $device"
    if ($null -eq $config) {
        Write-Host '  sync-config.json:          (none - Personal Sync never set up)'
    }
    elseif ($config -is [string]) {
        Write-Result 'WARN' '  sync-config.json could not be read'
    }
    else {
        $mm = $false
        if ($config.PSObject.Properties.Name -contains 'MorningMessageSyncEnabled') { $mm = [bool]$config.MorningMessageSyncEnabled }
        Write-Host "  Personal Sync enabled:     $($config.PersonalSyncEnabled)"
        Write-Host "  Sync folder:               $($config.SyncRootPath)"
        Write-Host "  Sync Morning Messages:     $mm"
    }
    $state = Read-JsonFile (Join-Path $appData 'morning-message-sync-state.json')
    if ($null -eq $state) {
        Write-Host '  Morning Message sync state: (none)'
    }
    elseif ($state -is [string]) {
        Write-Result 'WARN' '  morning-message-sync-state.json could not be read'
    }
    else {
        $conflicts = 0
        if ($null -ne $state.Conflicts) { $conflicts = @($state.Conflicts).Count }
        Write-Host "  Morning Message sync state: last synced $($state.LastSyncedUtc); stored conflicts: $conflicts; last pass failed: $($state.LastPassFailed)"
    }
    $content = Read-JsonFile (Join-Path $appData 'morning-message-content.json')
    if ($null -ne $content -and -not ($content -is [string])) {
        $dates = @()
        $source = $content
        if ($content.PSObject.Properties.Name -contains 'Dates') { $source = $content.Dates }
        foreach ($p in $source.PSObject.Properties) { if ($p.Name -match '^\d{4}-\d{2}-\d{2}$') { $dates += $p.Name } }
        Write-Host "  Local morning-message dates: $(($dates | Sort-Object) -join ', ')"
    }
    return $device
}

function Test-AppRunning {
    $running = @(Get-Process -Name 'Carowinds.Passdown.Desktop' -ErrorAction SilentlyContinue)
    return $running.Count -gt 0
}

# ---------------------------------------------------------------------------------------------
if ($Mode -eq 'Prepare') {
    Write-Host 'PHASE 30 SYNC TEST - PREPARE' -ForegroundColor Cyan
    if (-not (Test-IsAdministrator)) {
        Write-Result 'FAIL' 'Run this as administrator (right-click the .cmd > Run as administrator). Nothing was changed.'
        exit 1
    }
    if (Test-AppRunning) {
        Write-Result 'FAIL' 'Carowinds Operations is running in at least one account. Close it in BOTH accounts (or sign the other account out), then run again. Nothing was changed.'
        exit 1
    }
    foreach ($account in $Accounts) {
        if (-not (Test-Path -LiteralPath (Join-Path 'C:\Users' $account))) {
            Write-Result 'FAIL' "Windows account folder C:\Users\$account not found. Nothing was changed."
            exit 1
        }
    }
    if (-not (Test-Path -LiteralPath $SharedRoot -PathType Container)) {
        Write-Result 'FAIL' "$SharedRoot not found. Nothing was changed."
        exit 1
    }
    if (Test-Path -LiteralPath $TestFolder) {
        $existing = @(Get-ChildItem -LiteralPath $TestFolder -Force)
        if ($existing.Count -gt 0) {
            Write-Result 'FAIL' "$TestFolder already exists and is not empty. Use it as it is (skip Prepare) or pass -TestFolderName <new name>. Nothing was changed."
            exit 1
        }
    }

    # 1. Backups, verified file by file.
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupRoot = Join-Path $EvidenceRoot "Phase30-backup-$stamp"
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $sources = @()
    foreach ($account in $Accounts) {
        $sources += [pscustomobject]@{ Name = "$account-profile"; Path = (Get-ProfileAppData $account) }
    }
    $sources += [pscustomobject]@{ Name = 'old-shared-sync-folder'; Path = $OldSyncFolder }

    $allRows = @()
    foreach ($source in $sources) {
        $target = Join-Path $backupRoot $source.Name
        if (-not (Test-Path -LiteralPath $source.Path)) {
            Write-Result 'INFO' "$($source.Name): $($source.Path) does not exist (nothing to back up)."
            continue
        }
        Copy-Item -LiteralPath $source.Path -Destination $target -Recurse -Force
        $before = Get-RelativeHashes $source.Path
        $after = Get-RelativeHashes $target
        $problems = Compare-HashSets $before $after
        if ($problems.Count -gt 0) {
            Write-Result 'FAIL' "$($source.Name): backup does not match the original ($($problems.Count) difference(s)). Test folder NOT created."
            $problems | Select-Object -First 10 | ForEach-Object { Write-Host "    $_" }
            exit 1
        }
        Write-Result 'PASS' "$($source.Name): $($before.Count) file(s) backed up and verified -> $target"
        foreach ($row in $before) {
            $allRows += [pscustomobject]@{ Source = $source.Name; Path = $row.Path; Bytes = $row.Bytes; SHA256 = $row.SHA256 }
        }
    }
    $allRows | Export-Csv -LiteralPath (Join-Path $backupRoot 'SHA256SUMS.csv') -NoTypeInformation

    # 2. Fingerprint of the existing shared sync folder, for VerifyOld at the end.
    Get-RelativeHashes $OldSyncFolder | Export-Csv -LiteralPath $FingerprintFile -NoTypeInformation
    Write-Result 'PASS' "Fingerprint of the existing shared sync folder saved: $FingerprintFile"

    # 3. Current settings, so the tester knows what each account pointed at before.
    foreach ($account in $Accounts) { Show-SyncConfig $account (Get-ProfileAppData $account) | Out-Null }
    Write-Host ''

    # 3b. Pause Personal Sync in both accounts (PersonalSyncEnabled -> false; folder, device id and
    # every other value kept; the original file is in the verified backup). Otherwise the test
    # build's first launch would sync with the EXISTING shared folder before the tester can choose
    # the new one. The tester turns it back on in the app, pointed at the new test folder.
    foreach ($account in $Accounts) {
        $configPath = Join-Path (Get-ProfileAppData $account) 'sync-config.json'
        if (-not (Test-Path -LiteralPath $configPath)) { continue }
        $text = [System.IO.File]::ReadAllText($configPath)
        $paused = [regex]::Replace($text, '("PersonalSyncEnabled"\s*:\s*)true', '${1}false')
        if ($paused -eq $text) {
            Write-Result 'PASS' "$account`: Personal Sync already off."
            continue
        }
        $temp = $configPath + '.phase30-' + [guid]::NewGuid().ToString('N') + '.tmp'
        [System.IO.File]::WriteAllText($temp, $paused, (New-Object System.Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temp -Destination $configPath -Force
        Write-Result 'PASS' "$account`: Personal Sync paused (turned off; folder setting kept). Original saved in the backup."
    }

    $tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'Carowinds*' })
    foreach ($task in $tasks) {
        Write-Result 'WARN' "Scheduled task '$($task.TaskName)' exists (state $($task.State)). It runs an installed app copy at 6 AM. Finish each test session before 6 AM, or note it."
    }
    Write-Host ''

    # 4. Permissions: deny rules on the parent would break the test; report them and stop.
    $parentAcl = Get-Acl -LiteralPath $SharedRoot
    $denies = @($parentAcl.Access | Where-Object { $_.AccessControlType -eq 'Deny' })
    if ($denies.Count -gt 0) {
        Write-Result 'FAIL' "$SharedRoot has Deny permission entries; test folder NOT created. Ask the developer."
        $denies | ForEach-Object { Write-Host "    Deny $($_.IdentityReference) $($_.FileSystemRights)" }
        exit 1
    }
    Write-Result 'PASS' "$SharedRoot has no Deny entries."

    # 5. Create the new empty folder and give both accounts Modify on it (and on what is created inside it).
    New-Item -ItemType Directory -Path $TestFolder -Force | Out-Null
    $acl = Get-Acl -LiteralPath $TestFolder
    foreach ($account in $Accounts) {
        $identity = "$env:COMPUTERNAME\$account"
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $TestFolder -AclObject $acl

    $verify = Get-Acl -LiteralPath $TestFolder
    foreach ($account in $Accounts) {
        $identity = "$env:COMPUTERNAME\$account"
        $ok = @($verify.Access | Where-Object {
                $_.IdentityReference.Value -ieq $identity -and $_.AccessControlType -eq 'Allow' -and
                (($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Modify) -eq [System.Security.AccessControl.FileSystemRights]::Modify) -and
                (($_.InheritanceFlags -band [System.Security.AccessControl.InheritanceFlags]::ContainerInherit) -ne 0) -and
                (($_.InheritanceFlags -band [System.Security.AccessControl.InheritanceFlags]::ObjectInherit) -ne 0)
            }).Count -gt 0
        if ($ok) { Write-Result 'PASS' "$account has Modify on $TestFolder (inherited by everything inside)." }
        else { Write-Result 'FAIL' "$account does NOT have Modify on $TestFolder. Ask the developer." ; exit 1 }
    }

    Write-Host ''
    Write-Result 'PASS' "READY. New empty test folder: $TestFolder"
    Write-Host "Backups: $backupRoot"
    Write-Host 'Only change in the accounts: Personal Sync paused. The existing shared sync folder was not changed.'
    exit 0
}

# ---------------------------------------------------------------------------------------------
if ($Mode -eq 'CheckAccess') {
    Write-Host "PHASE 30 SYNC TEST - CHECK ACCESS as $env:USERNAME" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $TestFolder -PathType Container)) {
        Write-Result 'FAIL' "$TestFolder not found. Run Prepare first."
        exit 1
    }
    if (Test-AppRunning) {
        Write-Result 'WARN' 'Carowinds Operations is running. Close it first so no sync pass sees the probe file.'
        exit 1
    }
    $folders = @((Get-Item -LiteralPath $TestFolder))
    $syncRoot = Join-Path $TestFolder $SyncFolderName
    if (Test-Path -LiteralPath $syncRoot) {
        $folders += Get-Item -LiteralPath $syncRoot
        $folders += @(Get-ChildItem -LiteralPath $syncRoot -Directory -Force)
    }
    $failed = 0
    foreach ($folder in $folders) {
        $probe = Join-Path $folder.FullName ('.access-probe-' + [guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [System.IO.File]::WriteAllText($probe, 'probe')
            Remove-Item -LiteralPath $probe -Force
            Write-Result 'PASS' "can create and delete files in $($folder.FullName)"
        }
        catch {
            $failed++
            Write-Result 'FAIL' "cannot write in $($folder.FullName): $($_.Exception.GetType().Name)"
            if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($failed -gt 0) { exit 1 }
    exit 0
}

# ---------------------------------------------------------------------------------------------
if ($Mode -eq 'Inspect') {
    Write-Host "PHASE 30 SYNC TEST - INSPECT as $env:USERNAME (read-only)" -ForegroundColor Cyan
    $deviceToAccount = @{}
    if (Test-IsAdministrator) {
        foreach ($account in $Accounts) {
            $id = Show-SyncConfig $account (Get-ProfileAppData $account)
            if ($id -ne '(none yet)') { $deviceToAccount[$id.ToLowerInvariant()] = $account }
        }
    }
    else {
        $id = Show-SyncConfig $env:USERNAME (Join-Path $env:LOCALAPPDATA $AppDataFolderName)
        if ($id -ne '(none yet)') { $deviceToAccount[$id.ToLowerInvariant()] = $env:USERNAME }
        Write-Host '  (Run as administrator to see both accounts.)'
    }

    $syncRoot = Join-Path $TestFolder $SyncFolderName
    Write-Host ''
    Write-Host "Test sync folder: $syncRoot"
    if (-not (Test-Path -LiteralPath $syncRoot)) {
        Write-Host '  (not created yet - the app creates it when Personal Sync first uses the folder)'
        exit 0
    }
    foreach ($dir in (Get-ChildItem -LiteralPath $syncRoot -Directory -Force | Sort-Object Name)) {
        $count = @(Get-ChildItem -LiteralPath $dir.FullName -File -Force).Count
        Write-Host ('  {0,-24} {1} file(s)' -f ($dir.Name + '\'), $count)
    }

    $mmDir = Join-Path $syncRoot 'morning-message'
    Write-Host ''
    Write-Host 'morning-message\ (one file per computer; text lengths only, never the text):'
    if (-not (Test-Path -LiteralPath $mmDir)) {
        Write-Host '  (folder does not exist - no computer has published Morning Messages here)'
        exit 0
    }
    foreach ($file in (Get-ChildItem -LiteralPath $mmDir -File -Force | Sort-Object Name)) {
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($file.Name).ToLowerInvariant()
        $owner = 'unknown computer'
        if ($deviceToAccount.ContainsKey($stem)) { $owner = 'account ' + $deviceToAccount[$stem] }
        Write-Host ''
        Write-Host "  $($file.Name)  ($owner, $($file.Length) bytes, modified $($file.LastWriteTime))"
        $json = Read-JsonFile $file.FullName
        if ($null -eq $json -or $json -is [string]) { Write-Result 'WARN' '    not readable as JSON (temp or conflict copy?)'; continue }
        Write-Host "    Format: $($json.Format) v$($json.FormatVersion)"
        if ($null -ne $json.Preferences) {
            $names = @($json.Preferences.PSObject.Properties | ForEach-Object { $_.Name })
            Write-Host "    Preferences: $($names -join ', ')"
        }
        if ($null -ne $json.Dates) {
            foreach ($date in ($json.Dates.PSObject.Properties | Sort-Object Name)) {
                $parts = @()
                foreach ($field in $date.Value.PSObject.Properties) {
                    $value = $field.Value.Value
                    if ($value -is [string]) { $parts += "$($field.Name)($($value.Length) chars)" }
                    else { $parts += "$($field.Name)($value)" }
                }
                Write-Host "    $($date.Name): $($parts -join ', ')"
            }
        }
    }
    exit 0
}

# ---------------------------------------------------------------------------------------------
if ($Mode -eq 'VerifyOld') {
    Write-Host 'PHASE 30 SYNC TEST - VERIFY EXISTING SHARED SYNC FOLDER (read-only)' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $FingerprintFile)) {
        Write-Result 'FAIL' "No fingerprint at $FingerprintFile. Prepare was not run on this VM."
        exit 1
    }
    $expected = @(Import-Csv -LiteralPath $FingerprintFile)
    $actual = @(Get-RelativeHashes $OldSyncFolder)
    $problems = Compare-HashSets $expected $actual
    if ($problems.Count -eq 0) {
        Write-Result 'PASS' "$OldSyncFolder is unchanged ($($expected.Count) file(s), every SHA-256 identical)."
        exit 0
    }
    Write-Result 'FAIL' "$OldSyncFolder changed: $($problems.Count) difference(s)."
    $problems | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
    exit 1
}
