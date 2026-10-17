# RC.6 Windows validation checklist (Phases 21A–21D, combined)

Release: **0.1.0-rc.6**, built from `ef13a6d` (Phase 21D source `91a4a65` + version/tester-guide
commit). Record results in `docs/release-manifest.md` (RC.6 section). Per-phase detail lives in
`docs/phase21a-pricing-historical-integrity.md`, `docs/phase21b-associate-navigation-toolbar.md`,
`docs/phase21c-official-show-schedules.md` and `docs/phase21d-daily-feature-math.md`.

Status values: **PASS / PASS AFTER FIX / PASS BY TEST / PARTIAL / NOT EXECUTED / NOT APPLICABLE /
FAIL**. Nothing below is pre-marked; every Windows item starts NOT EXECUTED.

Defect policy: a shipped-code defect found here is fixed in source with a regression test and ships
as **0.1.0-rc.7**. The RC.6 files are never rebuilt or replaced under the same name. Docs-only notes
do not need a new build.

Log: `%LOCALAPPDATA%\Carowinds Passdown\logs\app-yyyyMMdd.log`. No schema change in RC.6
(DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1).

## 1. Preparation and backup (required before launching RC.6)

Same schema numbers do **not** mean RC.5 is safe to go back to: RC.6 stores new kinds of cached rows
(dated Military/First Responder prices, show schedules) and drafts can carry `Shows` and LaTeX text
an older build does not understand.

1. Close Carowinds Operations (check the taskbar / Task Manager for `Carowinds.Passdown.Desktop.exe`).
2. Make sure the scheduled/headless refresh is not running (Task Scheduler → `Carowinds Passdown
   Daily Refresh` → Status not "Running"; it runs briefly around 6:00 AM).
3. In PowerShell:

```
$src = "$env:LOCALAPPDATA\Carowinds Passdown"
$dst = "$env:USERPROFILE\Documents\Carowinds Passdown backup $(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item $src $dst -Recurse
Get-ChildItem $dst | Select-Object Name, Length, LastWriteTime
```

4. Keep the backup outside the live folder. Never copy it back while the app is running.

- [ ] Backup path: ______ · created at: ______
- [ ] `carowinds-passdown.db` size (bytes): ______ · `-wal` present: yes / no · `-shm`: yes / no
- [ ] **Personal Sync users:** shared sync folder also copied: path ______ (before any two-PC test)

### ⚠ Cross-device Shows warning (read before §6 and §9)

Phase 21C added per-date show selections to Associate drafts **without** changing
`SyncSchemaVersion` (still 1). An older RC (RC.5 or earlier) that opens, edits and saves a draft
carrying show selections **silently drops those selections**, and the drop then syncs to every PC.

- [ ] Every PC sharing the sync folder runs RC.6 **before** any show is ticked: PCs ______
- [ ] No older RC is used to edit show-enabled drafts
- [ ] Shared sync folder backed up before the first cross-device test

The packaging phase did not (and must not) change the sync schema to prevent this.

## 2. Artifact verification

| | Expected | Result |
|---|---|---|
| ZIP | `Carowinds-Operations-0.1.0-rc.6-win-x64.zip`, **74,889,881** bytes | |
| ZIP SHA-256 | `04feee4b7b928f26dac982c7e0fa1700a058b454679b95db85bc3ed90bf1e381` | |
| EXE (experimental) | `Carowinds-Operations-0.1.0-rc.6-win-x64.exe`, **190,509,900** bytes | |
| EXE SHA-256 | `9987edcad06c7dfb5bdc51ccb996ef0852e62e3d7fee1c4245c9edeb8ab45aaa` | |

`Get-FileHash .\Carowinds-Operations-0.1.0-rc.6-win-x64.zip -Algorithm SHA256`

- [ ] ZIP hash matches · EXE hash matches (if tested)
- [ ] Extract All → one folder `Carowinds-Operations-0.1.0-rc.6-win-x64\`, 283 files; `licenses\STIXTwoMath-OFL.txt` present
- [ ] Optional (needs a repository checkout + PowerShell 7): `pwsh -File packaging\windows\Test-ReleasePackage.ps1 -Path <zip> -RepositoryRoot <checkout>` — PASS / NOT EXECUTED
- [ ] Windows edition/build ______ · x64 ______ · scaling ______ · language ______ · OneDrive ______
- [ ] Extraction folder (with a space in the path): ______

## 3. Launch and About

- [ ] Launch `Carowinds.Passdown.Desktop.exe` directly from the extracted folder; window opens; Dashboard shows cached data at once
- [ ] Log: "Database ready at schema version 16", **no** "Applied migration" line
- [ ] Settings → About this build: **`0.1.0-rc.6 (ef13a6d)`**, running path = the RC.6 folder
- [ ] Explorer → exe Properties → Details: Product version `0.1.0-rc.6+ef13a6d…`, File version `0.1.0.6`
- [ ] Windows Search "Carowinds" → whichever entry opens, About shows the RC.6 path (else note the stale copy: ______)
- [ ] Dashboard, Associate, Operations Calendar, Settings, History all open
- [ ] Close cleanly (process gone in Task Manager); second launch works, no recovery prompt

## 4. Phase 21A pricing

- [ ] Associate, today: Military and First Responder are separate rows; dated price matches the official store for today: Military $____ / FR $____ (official: $____ / $____)
- [ ] A future date: same comparison: ______
- [ ] Where the store has no dated price, `From $X` is shown — never an invented exact price
- [ ] Do not expect the earlier $55/$60 values; compare against the official source for the tested date
- [ ] PDF (all three styles) and Teams Copy → Today's Pricing show the same prices as Associate
- [ ] Operations Calendar → past regular-season date → Date Pricing: no current SCarowinds products or current promotions projected backward; exact recorded values shown where they exist; otherwise an honest "not available"; park hours still shown
- [ ] 6 AM Eastern operational cutoff (if practical, before/after 6 AM): ______
- [ ] Season passes: 2026 and 2027 rows stay distinct (when both are offered); price histories not merged; no duplicate rows after Check Online
- [ ] Historical source cases that cannot be reproduced → NOT EXECUTED (do not fabricate test data)
- [ ] Associate → pick an old date → Dashboard → **Open Passdown** opens **today** (Eastern). Repeat from a future date.

## 5. Phase 21B navigation and toolbar

- [ ] ‹ / › step one day; month rollover (e.g. Oct 31 → Nov 1); year rollover where practical
- [ ] Today shortcut returns to today; pressing it when already on today causes no reload/flicker
- [ ] Date text width fixed while stepping; Today/Yesterday/Tomorrow label correct
- [ ] Calendar popup: selected date highlighted, today marked, month and year pickers work
- [ ] Popup keyboard: arrows move, Enter selects, Escape dismisses; outside click dismisses; no clipping, no focus loss
- [ ] Popup in light **and** dark theme
- [ ] Toolbar: Check Online · Create PDF (shows current style) · Copy ▾ · More ▾ (Classic/Compact/Modern, Season Price History); nothing missing vs RC.5
- [ ] Settings → Diagnostics → Export Diagnostics: destination picker, ZIP written; **not** present in Associate any more

## 6. Phase 21C shows (read the §1 sync warning first)

- [ ] Official source returns shows for the selected date: names, venues, start times, multiple performances; no invented duration or cancellation status
- [ ] A date with no published schedule shows an honest empty/unavailable state (catalog-only entries without times are **not** listed as scheduled)
- [ ] Dashboard Today's Shows: today's performances only; upcoming ordered, earlier ones separated; updates as time passes; no next-day carryover; empty state correct
- [ ] Operations Calendar: several dates → correct shows each, no carryover from the previous date; past dates without a recorded schedule stay unavailable; Calendar stays responsive; historical park hours still work
- [ ] Associate Shows: off by default; individual checkboxes; Select all; Clear; nothing auto-ticked; per-date persistence; autosave; survives restart
- [ ] Newly published show is not auto-selected; removed show marked; changed time flagged (where testable — else NOT EXECUTED)
- [ ] Teams Copy (Operations Summary, Today's Pricing, Operations + Pricing): **no** show names, times or venues even with shows ticked

## 7. Phase 21D math (Daily Feature)

- [ ] ∑ Math button location sensible; popup does not overlap the Associate date picker; toolbar layout unchanged
- [ ] Five symbol groups; insert ∫ ∂ ∇ ∑ √ π ∞ ≤ ≥ — insertion at caret, replaces a selection, focus returns to the editor
- [ ] Undo / redo; autosave after insertion
- [ ] Templates insert with the placeholder selected; Escape / outside click dismiss the picker
- [ ] Paste each and check the preview (no visible `\(` `\)` `\[` `\]`, fractions stacked, integral limits correct, no clipped glyphs, no tofu, updates on edit):
  - `\(\frac{1}{2}\)`
  - `\(\frac{d}{dx}x^2=2x\)`
  - `\(\int_0^1 x^2\,dx=\frac13\)`
  - `\(\sum_{i=1}^{n}i\)`
  - `\(\frac{\partial f}{\partial x}\)`
  - `The derivative of \(x^2\) is \(2x\).`
  - `\[\int_0^1 x^2\,dx=\frac13\]`
- [ ] Malformed (e.g. `\(\frac{1}{`): no crash, source text intact, readable fallback, subtle warning, autosave works, PDF still generates, no exception text shown
- [ ] Spell check: misspelled prose underlined; LaTeX commands inside math spans not underlined; navigate away → no underline anywhere; back → rechecked
- [ ] Offline (network disconnected): preview and PDF still render formulas
- [ ] Known limitation (not a defect): a Daily Feature paragraph containing math is drawn as shapes in the PDF — not selectable/searchable. Plain-text Daily Features remain searchable.

## 8. Combined PDFs

Busy passdown: operational data, several products, 3+ selected shows, a mathematical Daily Feature
(inline + display + long formula), normal notes.

| Template | Content correct | Only selected shows, right times/venues/date | Math sharp at 400% zoom, no tofu | No clipping / overlap / blank page | Pages | Result |
|---|---|---|---|---|---|---|
| Classic | | | | | | |
| Compact | | | | | | |
| Modern | | | | | | |

- [ ] No Shows heading when no show is ticked; no blank reserved space
- [ ] A second page is acceptable if readable — note it
- [ ] Prices in the PDF = Associate = Teams Copy

## 9. History and sync

- [ ] With safe test data: finalized passdown keeps its selected shows; a later refresh does not change them; frozen PDF unchanged
- [ ] Old History records (pre-RC.6) still open with their Final PDF
- [ ] Do not alter important existing finalized records to test this
- [ ] Season Pass Price History opens; existing rows kept; no duplicate intervals; pass identities correct
- [ ] Historical park-hours backfill still runs, Calendar fills in live, Check Online takes priority, no fabricated early-January hours, restart does not redo completed dates, UI never blocked
- [ ] Personal Sync, one PC: drafts with Shows and LaTeX sync to the folder; no raw show cache or rendered math cache in the folder
- [ ] **Two-PC sync** (draft sync, date-specific Shows, LaTeX source, conflict handling, no dropped fields): ______ — if no second RC.6 PC: **NOT EXECUTED — TWO-PC VALIDATION PENDING**

## 10. Diagnostics

- [ ] Settings → Diagnostics → Export works to a chosen folder
- [ ] Bundle excludes note bodies, Daily Feature source, equations, synced draft contents, raw HTTP responses, the database, PDF contents, clipboard, credentials
- [ ] App log contains no typed equations or Daily Feature text (search the log for `frac`)

## 11. Scheduled refresh

- [ ] Enable Automatic Refresh.cmd from the RC.6 folder succeeds; task points at the RC.6 exe
- [ ] Run the task manually: current-data refresh only, **no** "Historical backfill" lines, no visible window, exits successfully, no overlap with a running refresh
- [ ] Disable Automatic Refresh.cmd removes it (re-enable afterward if wanted)

## 12. Scaling and accessibility

At 100% / 125% / 150% and a narrow window, light and dark: date bar, calendar popup, toolbar
wrapping, Shows section, math picker, math preview, PDF controls, Settings Diagnostics.

- [ ] 100% ______ · 125% ______ · 150% ______ · narrow ______ · dark ______
- [ ] Keyboard: Tab order reaches date bar, toolbar, Shows checkboxes, Math button; focus visible
- [ ] Spell-check navigation regression: underline in Associate → Dashboard (gone) → Calendar (gone) → back (reappears)

## 13. Experimental EXE

Do **not** promote or distribute alone (no notices travel with it).

- [ ] Launch, About `0.1.0-rc.6 (ef13a6d)` with the EXE path
- [ ] Same `%LOCALAPPDATA%\Carowinds Passdown` data; nothing written next to the EXE
- [ ] Math preview renders; a PDF with math generates
- [ ] Clean shutdown

## 14. Final sign-off

| Gate | Result |
|---|---|
| Backup taken (§1) | |
| Artifact hashes (§2) | |
| Launch / About (§3) | |
| 21A pricing (§4) | |
| 21B navigation (§5) | |
| 21C shows (§6) | |
| 21D math (§7) | |
| Combined PDFs (§8) | |
| History / sync (§9) — two-PC | |
| Diagnostics (§10) | |
| Scheduled refresh (§11) | |
| Scaling / accessibility (§12) | |
| Experimental EXE (§13) | |
| Tester / date / PC | |

**WINDOWS VALIDATED** only when every mandatory row above is PASS (or PASS AFTER FIX in a later RC).
The QuestPDF **DISTRIBUTION LICENSE DECISION** is separate and stays PENDING regardless of these results.
