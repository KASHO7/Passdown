@echo off
setlocal

rem Phase 7C Part F/O: matching removal helper. Resolves its own directory the same way as
rem Enable Automatic Refresh.cmd. Removes only the one named scheduled task
rem (Uninstall-ScheduledRefresh.ps1 takes no parameters and only ever targets
rem "Carowinds Passdown Daily Refresh") and never touches the app, database, drafts, cache, or
rem logs. Safe to run whether or not automatic refresh is currently enabled.

set "APP_DIR=%~dp0"
set "UNINSTALL_SCRIPT=%APP_DIR%tools\Uninstall-ScheduledRefresh.ps1"

if not exist "%UNINSTALL_SCRIPT%" (
    echo Could not find tools\Uninstall-ScheduledRefresh.ps1 next to this file.
    echo Keep this file inside the extracted Carowinds Passdown folder and try again.
    echo.
    pause
    exit /b 1
)

echo Turning off automatic refresh for this copy of Carowinds Passdown...
echo.

rem -ExecutionPolicy Bypass applies only to this one PowerShell process (Part D) and changes
rem nothing permanently. Only the trusted script shipped in tools\ next to this file is
rem executed, with no extra or user-supplied arguments.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALL_SCRIPT%"
set "RESULT=%ERRORLEVEL%"

echo.
if not "%RESULT%"=="0" (
    echo Automatic refresh could not be turned off. See the messages above for details.
    echo.
    pause
    exit /b 1
)

echo Automatic refresh has been turned off.
echo Your drafts, cached park data, and logs were not affected.
echo.
pause
