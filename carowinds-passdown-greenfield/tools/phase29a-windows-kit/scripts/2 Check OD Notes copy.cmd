@echo off
rem Phase 29A: checks the OD Notes that Carowinds Operations just copied. Read-only; no admin rights.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Inspect-TeamsClipboard.ps1" -Kind OdNotes -Label od-notes
pause
