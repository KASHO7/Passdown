# RC.3 Windows validation checklist (Phase 19A + 19B)

Release: **0.1.0-rc.3**, built from `1744320`. Record results in `docs/release-manifest.md` (RC.3
section). Status values: **PASS / PASS BY SIMULATION / PARTIAL / NOT EXECUTED / NOT APPLICABLE / FAIL**.

Defect policy: a code defect found here is fixed in source with a regression test and ships as
**0.1.0-rc.4**. The RC.3 files are never rebuilt or replaced under the same name. Validation notes that
only change docs do not need a new build.

Before starting: copy `%LOCALAPPDATA%\Carowinds Passdown` to a dated backup folder.

## 0. Artifacts and machine

| | Expected | Result |
|---|---|---|
| ZIP | `Carowinds-Operations-0.1.0-rc.3-win-x64.zip`, 73,547,365 bytes | |
| ZIP SHA-256 | `6f08a796473abee5d94efe4e7e0cee47b6830537b1856d023a962efc929980eb` | |
| EXE (experimental) | `Carowinds-Operations-0.1.0-rc.3-win-x64.exe`, 186,606,412 bytes | |
| EXE SHA-256 | `915cc5488c734299d80a236a639808932e5006297a3906dea0164680bcc4ff72` | |

Check the hash in PowerShell **before** extracting:
`Get-FileHash .\Carowinds-Operations-0.1.0-rc.3-win-x64.zip -Algorithm SHA256`

- [ ] Windows edition, version/build: ______
- [ ] x64 (not ARM64): ______ · standard user or admin: ______
- [ ] Extraction folder (use a path with a space, e.g. `C:\Users\<you>\Desktop\RC3 Test\`): ______
- [ ] Windows region: ______ · OneDrive available: ______ · Teams desktop: ______
- [ ] This is the PC that earlier showed the Dashboard problems: yes / no

**Known RC.3 packaging note:** step 3 of the shipped `TESTER-GUIDE.txt` still mentions a Dashboard
"Refresh" button. Phase 19A removed that button. Ignore that sentence; the source copy is fixed for the
next RC.

## 1. Launch and identity

- [ ] Hash matches (ZIP) · (EXE, if tested)
- [ ] Extract to a new clean folder and launch `Carowinds.Passdown.Desktop.exe` from it.
- [ ] Window appears quickly; cached Dashboard data is visible right away, before any online check finishes.
- [ ] Navigation works between all five sections during the background check.
- [ ] Settings → About this build: **`0.1.0-rc.3 (1744320)`**; running folder = the RC.3 folder.
- [ ] Close the app, launch it again, and confirm the second launch works.
- [ ] Windows Search for "Carowinds Passdown" / "Carowinds Operations". Record each result path and version. Confirm the window under test is the RC.3 folder copy.

## 2. Phase 19A: automatic refresh

- [ ] Startup status line goes "Not checked yet" or the cached time → "Checking official data…" → "Official data checked h:mm AM/PM". No modal dialog, and the UI stays responsive.
- [ ] Startup duplicate suppression: run a Deep Refresh, close the app, relaunch within the hour. The log shows the startup check skipped as fresh.
- [ ] Four-hour coordinator: the log shows one "next automatic check" schedule, and no repeated checks while navigating. Waiting the full 4 h is optional (PARTIAL is acceptable; timing is covered by tests).
- [ ] Dashboard has **no Refresh button**, and the header has no awkward gap.
- [ ] On Associate, edit each of these: Projected Attendance, Pre-solds, ERT, Avenue override, North Gate override. Each updates on the Dashboard automatically after autosave.
- [ ] Reset the Avenue and North Gate overrides. The Dashboard returns to the default/effective times automatically.
- [ ] Local edits alone do not trigger an online check (no "Checking…" status; log shows only "Dashboard local data invalidated").
- [ ] **Today** in Associate: cached draft appears immediately. If data is missing or stale, the app checks this date online by itself and the products fill in. If the data is fresh, no request is made.
- [ ] **Future date**, stale data: auto-check runs. Fresh data: no request.
- [ ] Switch quickly across several dates. The final date shows its own data, with no flood of requests.
- [ ] **Past date**: selecting it makes no online check. History stays untouched.
- [ ] Check Online still checks the selected date immediately.
- [ ] Deep Refresh still works, resets the 4-hour clock (diagnostics "last comprehensive check"), and leaves drafts and manual values intact.
- [ ] The status line reads the same on Dashboard and Associate. It says "checked", not "updated", and uses US time.
- [ ] **Offline startup** (Wi-Fi off): the app opens with cached data, Dashboard and Associate work, and the status shows "Some official data couldn't be updated · Last checked …". No crash, no rapid retries. Reconnect, then Check Online works.
- [ ] Sleep/resume across a 4-hour boundary: only one catch-up check runs afterwards. (NOT EXECUTED is acceptable.)
- [ ] Task Scheduler: double-click `Enable Automatic Refresh.cmd`. The task points at the RC.3 folder, no admin prompt appears, and a path with spaces works. "Run" the task: it exits normally and no window stays open. The app shows that check, and the next launch does not repeat it immediately. Disable afterwards if not wanted.

## 3. Phase 19B: seasonal catalog and product lifecycle

- [ ] After a comprehensive check, diagnostics `source-status.txt` shows `dated-bring-a-friend-duo.official` as **Ok**, not Error.
- [ ] The status line does **not** say "Some official data couldn't be updated" because of Duo.
- [ ] After two automatic or Deep Refresh checks, Bring-A-Friend Duo is gone from Promotions. The log shows "candidate absence", then "now inactive (retired from the current catalog)".
- [ ] Threshold: after the first successful miss the product is still listed; after the second it is gone. A failed (offline) check in between does not count. Offline-check evidence is optional.
- [ ] SCarowinds night (while the season runs): the SCarowinds section shows haunted products, including **The Conjuring: Beyond Fear** if published. Prices match the official site, with no duplicates.
- [ ] Date after SCarowinds ends (e.g. Nov 15), or any non-SCarowinds date: no empty SCarowinds heading in Associate, the PDF, or Teams copy.
- [ ] There is no global season switch anywhere in the UI.
- [ ] WinterFest: if WinterFest products are live, they appear. Otherwise **NOT EXECUTED LIVE** (covered by simulation tests).
- [ ] Retired products do not appear as selectable rows and show no error badges.
- [ ] A future date where a product is not offered: that date's status says not published, and the product still appears on other dates.

## 4. PDF and Teams copy

- [ ] Create a PDF in **Classic**, **Compact**, and **Modern**. Each shows only current products, no "Retired" label, and no empty seasonal section. An empty Daily Feature is omitted (Phase 18F).
- [ ] SCarowinds selected: Conjuring is in the PDF and in Copy → Today's Pricing. SCarowinds deselected: it is in neither.
- [ ] Paste Today's Pricing into Teams: same products as the PDF, prices like `$59.00` / `From $37.00`.
- [ ] Copy → Operations Summary and Copy → Operations + Pricing both work.

## 5. History, drafts, sync

- [ ] An older finalized History entry opens with unchanged content. Open Final PDF shows the original PDF, including products that have since retired.
- [ ] An older draft that used a now-retired product loads without error; that product is simply not listed.
- [ ] One-PC Personal Sync (if enabled): a synced draft, gate override, note, or ERT change updates the Dashboard automatically. Dirty-editor conflict behavior is unchanged.
- [ ] Two-PC sync: requires a second PC on RC.3; otherwise NOT EXECUTED.

## 6. Logs, diagnostics, display

- [ ] `app-yyyyMMdd.log` contains Phase 19A events: startup decision, automatic start/finish, fresh-skip, date auto-check, Dashboard invalidated, refresh wait/skip.
- [ ] `app-yyyyMMdd.log` contains Phase 19B events: discovered, candidate absence, retired, zero products where relevant.
- [ ] The log contains no note text, clipboard text, draft text, or raw web responses.
- [ ] Export Diagnostics → `summary.txt` has the automatic refresh section (4-hour interval, last/next check) and the "Official catalog lifecycle" counts.
- [ ] The diagnostics export contains no database, drafts, notes, PDFs, or sync payloads.
- [ ] Scaling at 100%, 125%, and 150%: the status line, Associate product sections, Copy ▾ menu, gate override controls, and Dashboard header all look correct.
- [ ] Non-US region (temporarily): prices still show `$`, times AM/PM, and the PDF, Calendar, and History stay in US format. Restore the region afterwards.
- [ ] Experimental EXE: starts, About shows rc.3, data goes to the same `%LOCALAPPDATA%` folder, no files appear next to the EXE, and it closes cleanly. Do not hand the EXE out on its own (it has no notices).

## Live-transition items that cannot be run today

| Item | Status |
|---|---|
| Live SCarowinds end-of-season | NOT EXECUTED LIVE: PASS BY SIMULATION (`SeasonalCatalogLifecycleTests`) |
| Live WinterFest arrival | NOT EXECUTED LIVE: PASS BY SIMULATION |
| Reactivation of a retired product | PASS BY SIMULATION (no debug path in the shipped app) |
