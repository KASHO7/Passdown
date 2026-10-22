@echo off
rem Phase 29A: locks the clipboard for 20 seconds so a Copy made meanwhile fails on purpose. No admin rights.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Hold-Clipboard.ps1" -Seconds 20
pause
