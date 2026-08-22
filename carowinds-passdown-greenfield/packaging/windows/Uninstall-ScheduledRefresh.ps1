#Requires -Version 5.1
<#
.SYNOPSIS
    Removes the per-user "Carowinds Passdown Daily Refresh" Windows Task Scheduler task, if present.

.DESCRIPTION
    Targets only the one task this project registers, by its exact stable name. Never touches any
    other scheduled task, and never deletes application data, cache, logs, or drafts — those live
    under the user's local application-data directory (AppPaths) and are untouched by this script.

    Succeeds cleanly (exit 0) whether or not the task exists, so it is safe to run more than once.

.EXAMPLE
    .\Uninstall-ScheduledRefresh.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Must match Install-ScheduledRefresh.ps1's $TaskName and ScheduledTaskPlan.TaskName exactly.
$TaskName = 'Carowinds Passdown Daily Refresh'

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

if (-not $existingTask) {
    Write-Host "Scheduled task '$TaskName' is not registered; nothing to do."
    exit 0
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false

Write-Host "Removed scheduled task '$TaskName'."
Write-Host "Application data, drafts, cache, and logs under %LOCALAPPDATA%\Carowinds Passdown were left untouched."
