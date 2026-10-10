@echo off
setlocal

rem Phase 7C Part E/O: double-clickable, nontechnical setup helper for the portable release.
rem Resolves its own directory (%~dp0, trailing backslash) so it works from any extracted
rem location without the user typing a path. Never accepts arguments or user-typed text, so
rem there is nothing here for command injection to work with.

set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%Carowinds.Passdown.Desktop.exe"
set "INSTALL_SCRIPT=%APP_DIR%tools\Install-ScheduledRefresh.ps1"

if not exist "%APP_EXE%" (
    echo Could not find Carowinds.Passdown.Desktop.exe next to this file.
    echo Keep this file inside the extracted Carowinds Operations folder and try again.
    echo.
    pause
    exit /b 1
)

if not exist "%INSTALL_SCRIPT%" (
    echo Could not find tools\Install-ScheduledRefresh.ps1 next to this file.
    echo Keep this file inside the extracted Carowinds Operations folder and try again.
    echo.
    pause
    exit /b 1
)

echo Enabling automatic refresh for this copy of Carowinds Operations...
echo.

rem -ExecutionPolicy Bypass applies only to this one PowerShell process (Part D). It does not
rem change the machine or user execution policy, and nothing is written permanently by this
rem line. Only the trusted script shipped in tools\ next to this file is executed, and the
rem executable path passed to it is resolved above, never typed by the user.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" -ExecutablePath "%APP_EXE%"
set "RESULT=%ERRORLEVEL%"

echo.
if not "%RESULT%"=="0" (
    echo Automatic refresh could not be enabled. See the messages above for details.
    echo.
    pause
    exit /b 1
)

echo Automatic refresh is enabled.
echo Carowinds Operations will check for due updates each day.
echo.
pause
