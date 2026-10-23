@echo off
rem Phase 30 Morning Message Sync test: read-only view of the test sync folder. Run as administrator to see both accounts.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Phase30-SyncTest.ps1" -Mode Inspect
pause
