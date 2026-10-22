@echo off
rem Phase 29A: checks any other copy (Operations Summary, Today's Pricing, Operations + Pricing, one section, an image).
set /p LABEL=Short name for this copy (for example ops-summary-plain): 
if "%LABEL%"=="" set LABEL=other-copy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Inspect-TeamsClipboard.ps1" -Kind Any -Label "%LABEL%"
pause
