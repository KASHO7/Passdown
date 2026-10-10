#Requires -Version 5.1
<#
.SYNOPSIS
    Registers (or updates) the per-user "Carowinds Passdown Daily Refresh" Windows Task Scheduler
    task, which launches the existing headless refresh entry point:

        Carowinds.Passdown.Desktop.exe --scheduled-refresh

.DESCRIPTION
    This script is ONLY an operating-system trigger. It does not read freshness policy, does not
    call any source URL, and does not touch the application database — it registers a scheduled
    task whose action is the app's existing --scheduled-refresh command line. All refresh/freshness
    decisions are made by the application itself (Carowinds.Passdown.Application.Refresh), exactly
    as they are for an interactive launch.

    See docs/scheduled-refresh-task-scheduler.md for the full design rationale (trigger time,
    missed-run behavior, overlap policy, power/network conditions, and troubleshooting).

    Registration requires no administrator rights: this creates a per-user task (LogonType
    Interactive, RunLevel Limited) rather than a SYSTEM task, and stores no password.

.PARAMETER ExecutablePath
    Absolute path to the installed Carowinds.Passdown.Desktop.exe. Required — this script never
    assumes or hard-codes an installation location.

.PARAMETER TriggerHour
    Local hour (0-23) the task should fire at. Defaults to 6 (the product's 6:00 AM Eastern refresh
    target, applied as local time on the machine that owns this task — see the doc above for why).

.PARAMETER TriggerMinute
    Local minute (0-59) the task should fire at. Defaults to 0.

.EXAMPLE
    .\Install-ScheduledRefresh.ps1 -ExecutablePath "C:\Program Files\Carowinds Passdown\Carowinds.Passdown.Desktop.exe"

.EXAMPLE
    .\Install-ScheduledRefresh.ps1 -ExecutablePath "C:\Apps\Carowinds Passdown\Carowinds.Passdown.Desktop.exe" -TriggerHour 6 -TriggerMinute 0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExecutablePath,

    [ValidateRange(0, 23)]
    [int]$TriggerHour = 6,

    [ValidateRange(0, 59)]
    [int]$TriggerMinute = 0
)

$ErrorActionPreference = 'Stop'

# Kept in sync by hand with ScheduledTaskPlan.TaskName
# (src/Carowinds.Passdown.Desktop/ScheduledRefresh/ScheduledTaskPlan.cs) — the C# constant is the
# one exercised by automated tests (ScheduledTaskPlanTests); this literal is this script's copy of
# the same value. Registration/removal must only ever target this exact name (Part I).
#
# Phase 13G: the app's visible product name is now "Carowinds Operations," but this Task Scheduler
# task name is intentionally NOT renamed — it is an internal compatibility identifier so an
# upgrading user's existing scheduled task is found/updated in place rather than duplicated or
# orphaned (docs/phase13-hardening.md "Phase 13G" Task Scheduler decision).
$TaskName = 'Carowinds Passdown Daily Refresh'
$ScheduledRefreshArgument = '--scheduled-refresh'

if (-not [System.IO.Path]::IsPathRooted($ExecutablePath)) {
    Write-Error "ExecutablePath must be an absolute path. Got: '$ExecutablePath'"
    exit 1
}

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    Write-Error "Executable not found at path: '$ExecutablePath'. Install/copy the app first, then re-run with the correct path."
    exit 1
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
$workingDirectory = Split-Path -Path $resolvedExecutable -Parent

if ([System.IO.Path]::GetFileName($resolvedExecutable) -ne 'Carowinds.Passdown.Desktop.exe') {
    Write-Warning "Executable file name is '$([System.IO.Path]::GetFileName($resolvedExecutable))', not the expected 'Carowinds.Passdown.Desktop.exe'. Continuing, but double-check this is the right build."
}

# Action fields are passed to Task Scheduler as separate Execute/Argument/WorkingDirectory values
# (never a single concatenated command string), which is what avoids path-quoting/injection bugs
# for paths containing spaces (Part F/O).
$action = New-ScheduledTaskAction -Execute $resolvedExecutable -Argument $ScheduledRefreshArgument -WorkingDirectory $workingDirectory

$triggerTime = Get-Date -Hour $TriggerHour -Minute $TriggerMinute -Second 0
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerTime

# Part C: Task Scheduler's own overlap guard (IgnoreNew = "do not start a new instance"). This is
# defense in depth alongside — never a replacement for — the application-level
# FileScheduledRefreshLock, which still runs inside every --scheduled-refresh invocation.
#
# Part D power/network conditions:
#   - StartWhenAvailable: run as soon as possible after a missed scheduled start (laptop asleep/off
#     at 6 AM) — required by spec 6.6.
#   - AllowStartIfOnBatteries / (no) DisallowStartIfOnBatteries and DontStopIfGoingOnBatteries: a
#     nontechnical user's laptop is often unplugged; this is a short HTTP-only refresh, not a
#     reason to skip a day. Explicitly opts OUT of Task Scheduler's normal AC-power-only default.
#   - No WakeToRun: never wakes a sleeping/hibernating machine just to refresh. Spec 6.6 already
#     covers the asleep/off case via the app's own interactive startup catch-up the next time the
#     app opens (StartupCatchUpRefresh) — forcibly waking the machine would be intrusive for no
#     corresponding product requirement.
#   - No "requires network" condition: the app already handles offline failure safely (last-known-
#     good preservation, Phase 6B) — a network-available precondition would risk permanently
#     skipping a day whenever the trigger happens to land before Wi-Fi reconnects.
#   - ExecutionTimeLimit: a defensive cap (not a spec requirement) so a hung run cannot occupy the
#     overlap lock indefinitely; the application's own work is expected to take well under this.
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1)

# Per-user, no-admin, no stored password (Part N/O): Interactive logon type runs as the currently
# logged-on user without ever prompting for or storing a credential; Limited run level requests no
# elevation. This task only runs while that user is logged on (a locked screen still counts as
# logged on; a full sign-out does not) — documented in docs/scheduled-refresh-task-scheduler.md.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

$description = "Runs the Carowinds Operations unattended source refresh ($ScheduledRefreshArgument). " +
    "Task Scheduler is only the trigger; the app decides what is actually due. " +
    "See docs/scheduled-refresh-task-scheduler.md. Managed by Install-ScheduledRefresh.ps1 / Uninstall-ScheduledRefresh.ps1."

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

# -Force makes this idempotent by deliberately replacing only a task with this exact name
# (Part E/I) — never any other scheduled task on the machine.
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description $description -Force | Out-Null

if ($existingTask) {
    Write-Host "Updated existing scheduled task '$TaskName'."
} else {
    Write-Host "Registered new scheduled task '$TaskName'."
}

Write-Host "  Executable:       $resolvedExecutable"
Write-Host "  Arguments:        $ScheduledRefreshArgument"
Write-Host "  Working directory: $workingDirectory"
Write-Host ("  Trigger:          daily at {0:D2}:{1:D2} local time" -f $TriggerHour, $TriggerMinute)
Write-Host "  Missed start:     run as soon as possible after a missed start"
Write-Host "  Overlap policy:   do not start a new instance (Task Scheduler) + FileScheduledRefreshLock (application)"
Write-Host ""
Write-Host "Inspect with:"
Write-Host "  Get-ScheduledTask -TaskName '$TaskName' | Get-ScheduledTaskInfo"
