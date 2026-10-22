# Carowinds Operations 0.1.0-rc.9 — Windows validation record

Execution record for the frozen RC.9 artifacts: first launch, the database 16 → 17 upgrade and its
backups, Phase 25 official pricing, Phase 26 price-history sync and source links, the RC.8 → RC.9
in-app update, and RC.8 regression areas (`docs/releases/rc8-windows-validation.md`).

**Status: NOT EXECUTED.** RC.9 was built on macOS. No Windows 11 environment was available to the build
session. Every item stays **NOT EXECUTED** until someone runs it on Windows and records the outcome.
Build-host results (`docs/release-manifest.md` "0.1.0-rc.9") are synthetic-data evidence only and do not
replace any check here.

Result values: **PASS** · **PASS AFTER FIX** · **FAIL** · **PARTIAL** · **NOT EXECUTED** · **BLOCKED**.
A failure that needs a new binary ships as **0.1.0-rc.10**; RC.9 files are never rebuilt or replaced.

**Safety rules for every test:** use a disposable Windows user profile or a backed-up copy of
`%LOCALAPPDATA%\Carowinds Passdown` — never the working profile. Use a **separate, empty test Personal
Sync folder**, never the active work folder. Do not test the updater against the public `KASHO7/Passdown`
repository.

## 0. Artifacts under test

Check hashes before extracting (PowerShell: `Get-FileHash .\<file> -Algorithm SHA256`). Built from commit
`e20d846`; About this build should read `0.1.0-rc.9 (e20d846)`.

| Artifact | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.9-win-x64.zip` (primary) | 75,892,218 | `e1ad521fe711dcee0c169cbd7244ea04f6d547df801c2f72b375bfd781fe1558` |
| `Carowinds-Operations-0.1.0-rc.9-win-x64-singlefile.exe` (experimental) | 192,508,748 | `5fa0769f5ae1726cddb3aa4694cfc1ebcc6929beadd35d3123d13a6b6454faa9` |
| `Carowinds-Operations-0.1.0-rc.9-win-x64.update.json` (signed manifest, not published) | 49,164 | `936b196c927afe34b6fc9b9516e1e2d5a630cad835e10a5fc91a76733bed160f` |
| `Carowinds-Operations-0.1.0-rc.9-win-x64.update.json.sig` | 206 | `af82eaf596a153d03fa89cd533c61e684f21c788af1976f5c2b872d441d993e2` |

## 1. Environment (fill in for each machine)

| Field | Machine A | Machine B | Machine C |
|---|---|---|---|
| Test date | | | |
| Tester | | | |
| Windows edition and build (`winver`) | | | |
| CPU architecture | | | |
| Installed build tested (About this build) | | | |
| ZIP SHA-256 confirmed | | | |
| Test profile used (disposable user / restored copy) | | | |
| Test sync folder path | | | |
| Display scaling | | | |
| Windows theme | | | |

## 2. First launch (clean profile)

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 2.1 | Extract ZIP to a new writable folder | 287 files, one top-level folder | NOT EXECUTED | |
| 2.2 | Start `Carowinds.Passdown.Desktop.exe` | Opens; no .NET/SDK/runtime prompt; no admin prompt | NOT EXECUTED | |
| 2.3 | Version | About this build: `0.1.0-rc.9 (e20d846)` + extracted folder | NOT EXECUTED | |
| 2.4 | No migration backup on a clean profile | `backups` folder absent or empty | NOT EXECUTED | |
| 2.5 | Fury banner | Title only ("CAROWINDS OPERATIONS") | NOT EXECUTED | |
| 2.6 | Dashboard, Calendar, Associate, Notes, History, Settings | Open without errors | NOT EXECUTED | |
| 2.7 | Appearance | System / Light / Dark apply immediately and persist | NOT EXECUTED | |
| 2.8 | PDFs | Classic/Compact/Modern Create PDF succeed | NOT EXECUTED | |
| 2.9 | Teams Copy | Three options paste correctly | NOT EXECUTED | |
| 2.10 | What's New on first launch | Does **not** open automatically; Settings → About → What's New shows RC.9 notes offline | NOT EXECUTED | |

## 3. Database 16 → 17 upgrade (manual ZIP install over an RC.8 profile)

Use a restored copy of a real RC.8 profile, or one built by running RC.8 and creating drafts, notes,
History PDFs, price history, Personal Sync settings and a Dark appearance.

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 3.1 | Record before: DB size, History count, a draft's text, note list, appearance, sync folder | Recorded | NOT EXECUTED | |
| 3.2 | First RC.9 start | Opens normally | NOT EXECUTED | |
| 3.3 | Pre-migration backup | `backups\carowinds-passdown-pre-migration-schema-16-to-17-…db` exists, plus `backups\README.txt` | NOT EXECUTED | |
| 3.4 | Backup opens in RC.8 | Copy the backup to a second disposable profile as `carowinds-passdown.db`; RC.8 opens it with the 3.1 data | NOT EXECUTED | |
| 3.5 | Data preserved in RC.9 | Drafts, notes, History PDFs (Open Final PDF), prices, price history, Personal Sync settings, appearance | NOT EXECUTED | |
| 3.6 | Current prices unchanged before Check Online | Same as RC.8 | NOT EXECUTED | |
| 3.7 | Second start | No new backup; nothing changes | NOT EXECUTED | |
| 3.8 | RC.8 against the upgraded DB | RC.8 refuses to start (logs "schema version 17 is newer"); DB file unchanged (hash before/after) | NOT EXECUTED | |
| 3.9 | Backup failure | Profile on a nearly full drive (or `backups` replaced by a read-only file): RC.9 closes, log says backup failed, DB still schema 16 and opens in RC.8 | NOT EXECUTED | |
| 3.10 | Locked database | Hold the DB open with another program (e.g. DB Browser with a write transaction): RC.9 fails without changing it | NOT EXECUTED | |
| 3.11 | Scheduled refresh first | With Automatic Refresh enabled, let the 6 AM task run RC.9 headless on an RC.8 profile: backup made, DB 17, app then opens normally | NOT EXECUTED | |

## 4. Phase 25 live pricing (backed-up or disposable profile)

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 4.1 | Associate → Products → Check Online | Completes; source Verified or honest partial state | NOT EXECUTED | |
| 4.2 | Season Pass source | Carowinds Memberships & Season Passes | NOT EXECUTED | |
| 4.3 | Silver, Gold, Prestige | Separate rows, separate prices | NOT EXECUTED | |
| 4.4 | Membership rates | Separate products; "/month" units; never a yearly total | NOT EXECUTED | |
| 4.5 | Initiation fees | Separate from monthly price | NOT EXECUTED | |
| 4.6 | Renewal offers | Labelled renewal; separate from new purchase | NOT EXECUTED | |
| 4.7 | Current vs stale labels | Unreadable offers keep last verified value and say so; nothing shown as $0 | NOT EXECUTED | |
| 4.8 | Source link | **View Official Memberships & Season Passes Page** opens the sixflags.com page in the default browser; Tab + Enter work | NOT EXECUTED | |
| 4.9 | Price History | Older RC.8 history still present; new observations appended | NOT EXECUTED | |

Record the live amounts observed (do not copy them into code or tests).

## 5. Phase 26 price-history sync (three profiles or computers, one **test** sync folder)

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 5.1 | PC-A with older observations, PC-B newer, PC-C recent only | Prepared (e.g. restored profiles of different ages) | NOT EXECUTED | |
| 5.2 | Sync A, B, C, then A, B | All three show the same history | NOT EXECUTED | |
| 5.3 | No duplicate display rows | One row per distinct observation | NOT EXECUTED | |
| 5.4 | Original dates and sources | Imported rows keep the contributor's dates; never shown as this PC's check time | NOT EXECUTED | |
| 5.5 | Current price | Unchanged by imported history on each PC | NOT EXECUTED | |
| 5.6 | Repeat Sync Now | "new observations merged" no longer appears; files unchanged | NOT EXECUTED | |
| 5.7 | Offline | Sync folder unavailable → honest status; nothing lost | NOT EXECUTED | |
| 5.8 | Locked file | Another program holds a price-history file open → honest retry state; nothing lost | NOT EXECUTED | |
| 5.9 | Drafts and notes | Still sync in both directions alongside price history | NOT EXECUTED | |

## 6. Mixed versions in one test sync folder

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 6.1 | RC.8 + RC.9 share the folder | RC.8 ignores `official-price-history\`; never deletes or edits it | NOT EXECUTED | |
| 6.2 | Drafts/notes between RC.8 and RC.9 | Sync both ways | NOT EXECUTED | |
| 6.3 | RC.8 receives no price history | Expected; no error | NOT EXECUTED | |

## 7. What's New

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 7.1 | Manual install | Not shown automatically | NOT EXECUTED | |
| 7.2 | After successful in-app update (§8) | Shown once; RC.9 notes | NOT EXECUTED | |
| 7.3 | Restart after that | Not shown again | NOT EXECUTED | |
| 7.4 | After a failed update (§8.12–8.14) | Never shown | NOT EXECUTED | |

## 8. In-app update RC.8 → RC.9 (real external updater)

**Prerequisite — BLOCKED until prepared:** an RC.8 build configured for a **private test repository**
and a **test signing key**, plus a matching test-signed RC.9 package built from `e20d846` with the same
test configuration (the production RC.8 trusts only `carowinds-release-1` and `KASHO7/Passdown`). The
owner must choose the private test repository (outstanding since RC.8). Use a disposable profile created
by RC.8 with representative data.

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 8.1 | Detection | RC.8 offers RC.9 (Check for Updates or after ~90 s) | BLOCKED | Harness not prepared |
| 8.2 | Notes preview | Starts "RC.9 updates the local database. A backup is required before installation. Older versions cannot open the updated database." | BLOCKED | |
| 8.3 | Install approval | Nothing downloads before Install Update; nothing closes before Restart and Install | BLOCKED | |
| 8.4 | Download + signature | Progress shown; tampered ZIP / wrong key rejected (separate runs) | BLOCKED | |
| 8.5 | Safe shutdown | Unsaved draft saved first; failed save postpones the update | BLOCKED | |
| 8.6 | Pre-update backup | `backups\…pre-update-0.1.0-rc.8-to-0.1.0-rc.9-…db` exists, opens in RC.8, and was written **before** the app folder changed (compare timestamps) | BLOCKED | |
| 8.7 | File replacement | App folder holds RC.9 files; user files kept; no UAC prompt | BLOCKED | |
| 8.8 | Restart + migration | RC.9 opens; `…pre-migration-schema-16-to-17…db` also exists; DB schema 17 | BLOCKED | |
| 8.9 | Health confirmation | Install record `Succeeded` only after the window opened (`%LOCALAPPDATA%\Carowinds Operations Updates\records`) | BLOCKED | |
| 8.10 | Version + What's New | About shows RC.9; What's New once | BLOCKED | |
| 8.11 | Data preserved | As §3.5 | BLOCKED | |
| 8.12 | Failure before migration | Test RC.9 whose exe exits immediately → RC.8 restored and reopened once; DB still 16; "was restored" message | BLOCKED | |
| 8.13 | Failure after migration | Test RC.9 that upgrades then never opens a window → **not** reversed; RC.8 not started; `UPDATE-RECOVERY.txt` lists DB, backup, RC.8 files; RC.9 message on next start | BLOCKED | |
| 8.14 | Backup cannot be made | Fill the drive (or lock the DB) before Restart and Install → nothing installed; RC.8 explains on restart | BLOCKED | |
| 8.15 | Recovery procedure | Follow installation guide §7 from 8.13's state; RC.8 opens the restored backup; set-aside DB kept | BLOCKED | |
| 8.16 | Locked installation | Second instance or scheduled refresh running → not installed; nothing killed | BLOCKED | |

If 8.1–8.15 cannot be demonstrated, RC.9 stays **manual-install only** (installation guide §3, §7).

## 9. RC.8 regression areas

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 9.1 | Save-failure protection | Read-only data folder → "Save failed", Retry Save, no freeze, no loop | NOT EXECUTED | |
| 9.2 | Dashboard Shows | "+N more" expansion; no overlap | NOT EXECUTED | |
| 9.3 | Frozen History | Read-only; Open Final PDF exact bytes | NOT EXECUTED | |
| 9.4 | Calendar | Future park hours; rapid clicks show last date | NOT EXECUTED | |
| 9.5 | Notes | Create/edit/resolve | NOT EXECUTED | |
| 9.6 | Scheduled refresh | Enable/Disable Automatic Refresh.cmd; 6 AM task runs | NOT EXECUTED | |
| 9.7 | Date change offline | No online retrieval triggered by changing date | NOT EXECUTED | |

## 10. DPI and accessibility

| # | Check | 100% | 125% | 150% | Notes |
|---|---|---|---|---|---|
| 10.1 | Dashboard / Calendar / Associate / Notes / History / Settings: no clipping, no horizontal scroll, small and maximized | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED | |
| 10.2 | Settings controls (Appearance, Updates, Personal Sync) | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED | |
| 10.3 | Dashboard Show disclosures | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED | |
| 10.4 | Price History dialog + source links | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED | |
| 10.5 | Update dialogs / previous-attempt and migration messages | NOT EXECUTED | NOT EXECUTED | NOT EXECUTED | |

| # | Check | Result | Notes |
|---|---|---|---|
| 10.6 | Keyboard: Tab order, visible focus, Enter/Space on source links and Show disclosures | NOT EXECUTED | |
| 10.7 | Narrator: navigation, gear ("Update available" help text), source link name and destination, Price History rows, update and recovery messages | NOT EXECUTED | |

## 11. Experimental single-file EXE

| # | Check | Expected | Result | Notes |
|---|---|---|---|---|
| 11.1 | Launch | Opens; version `0.1.0-rc.9 (e20d846)` | NOT EXECUTED | |
| 11.2 | Updates | Settings → Application Updates says **Manual update required** | NOT EXECUTED | |
| 11.3 | Migration | On an RC.8 profile copy: pre-migration backup made, DB 17 | NOT EXECUTED | |

## 12. Failures, recovery and sign-off

| Field | Value |
|---|---|
| Failures found (item, description, screenshot) | |
| Recovery outcome | |
| Overall Windows result | NOT EXECUTED |
| Signed off by / date | |
