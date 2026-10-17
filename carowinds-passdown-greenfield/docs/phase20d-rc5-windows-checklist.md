# RC.5 Windows validation checklist (Phase 20C spell-check navigation + current-year history, with 19A/19B/20A regressions)

Release: **0.1.0-rc.5**, built from `10bf37d` (Phase 20C source `6498aa9` + version commit). Record
results in `docs/release-manifest.md` (RC.5 section). Status values:
**PASS / PASS AFTER FIX / PASS BY TEST / PARTIAL / NOT EXECUTED / NOT APPLICABLE / FAIL**.

Defect policy: a shipped-code defect found here is fixed in source with a regression test and ships as
**0.1.0-rc.6**. The RC.5 files are never rebuilt or replaced under the same name. Docs-only notes do
not need a new build.

Log: `%LOCALAPPDATA%\Carowinds Passdown\logs\app-yyyyMMdd.log`. No schema change in RC.5 (DB stays 16).

## 0. BACK UP FIRST (required before launching RC.5)

The profile is already schema 16 (RC.4), so RC.4 can still open it. Back it up anyway: RC.5 adds
historical park-hours rows to the database.

Close every Carowinds window, then in PowerShell:

```
$src = "$env:LOCALAPPDATA\Carowinds Passdown"
$dst = "$env:USERPROFILE\Documents\Carowinds Passdown backup $(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item $src $dst -Recurse
Get-ChildItem $dst | Select-Object Name, Length, LastWriteTime
```

- [ ] Source path: ______
- [ ] Backup path: ______ · created at: ______
- [ ] `carowinds-passdown.db` size (bytes): ______ ← this is **DB size before** for §6
- [ ] `-wal` present: yes / no · `-shm` present: yes / no

## 1. Artifacts and machine

| | Expected | Result |
|---|---|---|
| ZIP | `Carowinds-Operations-0.1.0-rc.5-win-x64.zip`, 73,739,645 bytes | |
| ZIP SHA-256 | `570c00df05a933a383ed69b7508ecd5e33ea22c5ee21096e0062e1c4145e3c25` | |
| EXE (experimental) | `Carowinds-Operations-0.1.0-rc.5-win-x64.exe`, 187,364,172 bytes | |
| EXE SHA-256 | `2bbf540cac7f4b1dc1f499c26829e6a21457440fa04bc622aee2e3a943a2c5ad` | |

Before extracting: `Get-FileHash .\Carowinds-Operations-0.1.0-rc.5-win-x64.zip -Algorithm SHA256`

- [ ] Windows edition, version/build: ______ · x64: ______ · standard/admin: ______
- [ ] Display scaling: ______ · Windows language: ______ · spell-check language: ______
- [ ] Extraction folder (path with a space, e.g. `...\Desktop\RC5 Test\`): ______
- [ ] OneDrive: ______ · Teams desktop: ______ · same PC as RC.4 validation: yes / no

## 2. Launch and identity

- [ ] Hash matches (ZIP) · (EXE, if tested)
- [ ] Extract to a clean folder; launch `Carowinds.Passdown.Desktop.exe`. Window opens; Dashboard shows cached data at once.
- [ ] Log: "Database ready at schema version 16"; **no** "Applied migration" line.
- [ ] Background refresh does not block the UI; navigation works while it runs.
- [ ] Settings → About this build: **`0.1.0-rc.5 (10bf37d)`**; running folder = the RC.5 folder.
- [ ] Close cleanly; second launch works.

## 3. Spell-check navigation (highest priority)

- [ ] **S1** Associate field (e.g. Daily Feature): type `accomodation`, wait for the red underline. Go to **Dashboard** → no squiggle anywhere. **Operations Calendar** → none. **Products** → none.
- [ ] **S3** Back to the Associate field: text unchanged; `accomodation` is underlined again.
- [ ] **S2** Type a new misspelling and switch screens **immediately** (under ⅓ s). No underline appears later on the next screen. Repeat 5×. Log shows no exception.
- [ ] **S4** With the misspelling present: Associate → Dashboard → Associate → Calendar → Associate → Products → Associate (twice through). No doubled/offset squiggles, no growing lag; right-click still shows one menu with suggestions / Ignore / Add to dictionary / Cut / Copy / Paste.
- [ ] **S5** Operational Notes body: same as S1 + S3.
- [ ] **S6 (observation, do not fix in RC.5)** Leave a word underlined in a visible Associate field, open **Season pass price history**. Does the squiggle draw on top of the dimmed overlay?
      Judge: **PASS** hidden/acceptable · **PARTIAL** visible but minor · **FAIL** distracting enough to need a fix (→ RC.6). Result: ______ · notes: ______

## 4. Spell-check regression (quick, not the full RC.4 matrix)

- [ ] Misspelling underlined; `accommodation` not flagged.
- [ ] `Carowinds SCarowinds WinterFest ERT Conjuring Copperhead` not flagged.
- [ ] Right-click: suggestions; pick one → replaced and autosaved.
- [ ] Ignore (session only) and Add to dictionary (harmless test word) behave as in RC.4.
- [ ] Cut / Copy / Paste work. Typing and autosave stay smooth with a misspelling present.
- [ ] Log contains no typed text.

## 5. Historical park hours (current year)

Use the existing profile (the backup is the safety net). If it already has most of the year, the
first-run checks become "resume / up to date" checks — note which you saw.

- [ ] **H1** App usable immediately. Startup official-data check runs first; ~20 s later the Calendar shows `Loading this year's history…` then `Historical data: N of M dates checked`. No full-screen wait.
- [ ] **H2** Log order: startup check lines **before** "Historical backfill starting for 2026: … newest first." (or "resuming …").
- [ ] Scope: log/diagnostics target year **2026** only. No requests or new rows for 2025 or earlier; older stored data untouched.
- [ ] Newest first: Calendar fills from yesterday backwards (current month first, then September, …), not from January.
- [ ] **H3** Open an incomplete past month (e.g. June): it fills in while you watch, no restart or manual refresh; month navigation stays responsive. The opened month is processed next (after operational work).
- [ ] **H4** During backfill press **Check Online**: it starts at once; log "Historical backfill yielded to operational refresh work…"; history resumes later. Repeat with **Deep Refresh**.
- [ ] **H10** Dashboard does not reload/flicker during backfill; the official-data line ("Official data checked h:mm") is not changed by history, and never shows "Some official data couldn't be updated" because of history.
- [ ] Status line under the Calendar month is muted/subtle, readable, distinct from the official-data line; no fake "up to date" while dates remain.
- [ ] **H5** After a meaningful number of dates (note N: ______), close normally, reopen: "Historical backfill resuming for 2026: P of M past dates still to check (C already stored)". Completed dates are not requested again.
- [ ] Restart once more after the year is substantially complete: no hundreds of requests; "up to date" or only the boundary probe (Mac: second run = 1 request).
- [ ] **H6** Final state: `Historical data partially available` with tooltip "Official park hours on or before Mar 24 are no longer published." (boundary is discovered, may differ) or `Historical data up to date`. Log: "…no longer publishes 2026-03-24; N dates on or before it are not available…".
- [ ] No fabrication: a date on/before the boundary (e.g. Mar 10) shows **no** hours, no copied neighbour values and no invented "Closed".
- [ ] **H7** Storage: DB size after (bytes) ______ · dates added ______ · delta ______ · bytes/date ______ (dev: 205 dates ≈ 116 KB ≈ 580 B/date; full year ≈ 205 KB). Flag only megabytes of growth.
- [ ] No raw responses stored: no new large files in the profile folder; DB growth matches the line above.
- [ ] Responsiveness during backfill: Dashboard, Associate, Calendar, Price History and typing in Notes stay smooth; no CPU spike in Task Manager; no repeated error dialogs.
- [ ] Close the app **while** backfill runs: closes promptly (≈2 s); log "Historical backfill stopped (app closing)…"; next launch resumes cleanly.
- [ ] **H8** Offline (if practical: disconnect during backfill): app stays usable; log "Historical backfill paused after 3 consecutive failed checks…"; no retry loop; stored data and current data intact. Reconnect, relaunch → resumes.
- [ ] **H9** 6 AM scheduled task (or run it manually from Task Scheduler): runs once, refreshes current data, exits; its log has **no** "Historical backfill" lines and the process does not linger.
- [ ] **H11** Diagnostics `summary.txt` has "Historical park-hours backfill (current year only, this app session):" with target year, status, in scope, stored, remaining, checked/queued/failed, boundary, last activity. No rows/raw data.

## 6. Phase 19A / 19B / 20A regressions

- [ ] Dashboard updates automatically on local edits; no Dashboard Refresh button.
- [ ] Today and a future date with stale data auto-check; a past date makes no ordinary Associate request. Check Online and Deep Refresh work; the 4-hour automatic check still appears in the log.
- [ ] `source-status.txt`: `dated-bring-a-friend-duo.official` not Error after it ended (NotPublished/absence).
- [ ] SCarowinds night shows The Conjuring when published; non-SCarowinds date has no empty SCarowinds heading (Associate, PDF, Teams).
- [ ] Season pass price history opens; existing rows kept; Check Online adds no duplicate row for an unchanged price; Include retired shows retired products. Diagnostics still schema 16.

## 7. PDF, Teams, History, Sync

- [ ] Classic, Compact and Modern PDF generate.
- [ ] Copy → Today's Pricing, Operations Summary, Operations + Pricing paste correctly into Teams.
- [ ] An older finalized History entry and its Final PDF are unchanged after backfill.
- [ ] Personal Sync (if enabled): user-authored sync works as before; historical rows and price history are **not** synced. Two-PC sync: NOT EXECUTED unless a second RC.5 PC exists.

## 8. Logs, diagnostics, display, EXE

- [ ] Log: historical start/resume/yield/boundary/finished lines only — no Info line per date; no typed text, notes, clipboard or raw responses.
- [ ] Diagnostics also keep the automatic-refresh, catalog lifecycle and Season price history sections.
- [ ] 100% / 125% / 150% scaling: Calendar status line + month as hours fill in, spell-check underline, Price History overlay, Associate controls — no clipping/overlap.
- [ ] Keyboard: Calendar month navigation by keyboard; focus normal after navigating away from/back to a spell-checked field; Narrator reads the historical status line; Price History overlay unchanged (Escape closes).
- [ ] Experimental EXE: starts, About shows `0.1.0-rc.5 (10bf37d)`, same `%LOCALAPPDATA%` data, history resumes consistently, nothing written next to the EXE, closes cleanly. Do not distribute the EXE alone (no notices).
