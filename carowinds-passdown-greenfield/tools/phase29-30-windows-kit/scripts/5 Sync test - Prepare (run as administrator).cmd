@echo off
rem Phase 30 Morning Message Sync test: backs up both accounts, then creates the new empty test folder. Run as administrator.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Phase30-SyncTest.ps1" -Mode Prepare
pause
