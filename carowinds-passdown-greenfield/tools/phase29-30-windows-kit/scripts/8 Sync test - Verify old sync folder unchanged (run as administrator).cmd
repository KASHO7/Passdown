@echo off
rem Phase 30 Morning Message Sync test: read-only check that the earlier shared sync folder is untouched.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Phase30-SyncTest.ps1" -Mode VerifyOld
pause
