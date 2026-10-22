#Requires -Version 5.1
<#
.SYNOPSIS
    Phase 29A: keeps the Windows clipboard open for a few seconds so a copy made meanwhile fails on purpose.

.DESCRIPTION
    Controlled clipboard-failure test. While this script holds the clipboard, no other program can write to
    it. Press a Copy button in Carowinds Operations during the countdown and record what the app says.
    Expected: the app does not crash, does not claim the copy worked, and shows
    "Couldn't copy to the clipboard. Try again." The clipboard is released automatically at the end (or when
    the window is closed). Changes nothing else. Needs no administrator rights.

.PARAMETER Seconds
    How long to hold the clipboard (5 to 60). Default 20.
#>
[CmdletBinding()]
param(
    [ValidateRange(5, 60)]
    [int]$Seconds = 20
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    Write-Error 'This script holds the Windows clipboard and only runs on Windows.'
    exit 2
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Phase29AClipboardHold
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
}
'@

if (-not [Phase29AClipboardHold]::OpenClipboard([IntPtr]::Zero)) {
    Write-Error 'Could not open the clipboard (another program is using it). Try again.'
    exit 1
}

try {
    Write-Host 'The clipboard is now LOCKED for this test.'
    Write-Host 'Switch to Carowinds Operations and press Copy Formatted (then Copy Plain Text) now.'
    for ($i = $Seconds; $i -gt 0; $i--) {
        Write-Host ("  releasing in {0,2} s" -f $i)
        Start-Sleep -Seconds 1
    }
} finally {
    [void][Phase29AClipboardHold]::CloseClipboard()
    Write-Host 'Clipboard released. Write down the exact status message the app showed.'
}
