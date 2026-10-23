@echo off
rem Phase 30 Morning Message Sync test: proves this account can write in the test folder. Changes nothing.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Phase30-SyncTest.ps1" -Mode CheckAccess
pause
