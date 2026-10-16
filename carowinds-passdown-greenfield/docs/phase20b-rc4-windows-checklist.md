# RC.4 Windows validation checklist (Phase 20A spell check + price history, with 19A/19B regressions)

Release: **0.1.0-rc.4**, built from `67a5dd6` (Phase 20A source `588cd93` + version commit). Record
results in `docs/release-manifest.md` (RC.4 section). Status values:
**PASS / PASS AFTER FIX / PASS BY TEST / PARTIAL / NOT EXECUTED / NOT APPLICABLE / FAIL**.

Defect policy: a shipped-code defect found here is fixed in source with a regression test and ships as
**0.1.0-rc.5**. The RC.4 files are never rebuilt or replaced under the same name. Docs-only notes do
not need a new build.

## 0. BACK UP FIRST (required before launching RC.4)

RC.4 upgrades the database from **schema 15 to 16** on first launch. RC.3 and older builds **refuse to
open** a schema-16 database. Do not continue until the backup exists, and keep it after this phase.

Close every Carowinds window, then in PowerShell:

```
$src = "$env:LOCALAPPDATA\Carowinds Passdown"
$dst = "$env:USERPROFILE\Documents\Carowinds Passdown backup $(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item $src $dst -Recurse
Get-ChildItem $dst | Select-Object Name, Length, LastWriteTime
```

- [ ] Source path: ______
- [ ] Backup path: ______ · created at: ______
- [ ] `carowinds-passdown.db` present: yes / no · `-wal` present: yes / no · `-shm` present: yes / no
- [ ] Do **not** start RC.3 against the upgraded profile afterwards. To go back to RC.3, restore this backup.

## 1. Artifacts and machine

| | Expected | Result |
|---|---|---|
| ZIP | `Carowinds-Operations-0.1.0-rc.4-win-x64.zip`, 73,696,526 bytes | |
| ZIP SHA-256 | `55f0babb84b82bbc0e3498806b1140916384bd6bb42e8a22a693fd6ed469b3fb` | |
| EXE (experimental) | `Carowinds-Operations-0.1.0-rc.4-win-x64.exe`, 187,097,932 bytes | |
| EXE SHA-256 | `2f3b1b922fbe222c5216a83e5c3f450ca6a3a4a1dccef93d5679ef69113cf070` | |

Before extracting: `Get-FileHash .\Carowinds-Operations-0.1.0-rc.4-win-x64.zip -Algorithm SHA256`

- [ ] Windows edition, version/build: ______ · x64: ______ · standard/admin: ______
- [ ] Display scaling: ______ · Windows language: ______ · spell-check language (Settings → Time & language → Language): ______
- [ ] Extraction folder (use a path with a space, e.g. `...\Desktop\RC4 Test\`): ______
- [ ] OneDrive: ______ · Teams desktop: ______ · normal/affected PC: yes / no

## 2. Launch, identity, migration 15 → 16

- [ ] Hash matches (ZIP) · (EXE, if tested)
- [ ] Launch `Carowinds.Passdown.Desktop.exe` from the extracted folder. The window opens normally.
- [ ] `app-yyyyMMdd.log` shows "Applied migration 16: Create season_price_history table" and "Database ready at schema version 16". No migration error.
- [ ] Dashboard, Associate (existing drafts), Operational Notes, ERT, History, Settings all load with the data that was there before.
- [ ] Settings → About this build: **`0.1.0-rc.4 (67a5dd6)`**; running folder = the RC.4 folder.
- [ ] Background refresh does not block the UI; navigation works while it runs.
- [ ] Close; second launch works (no second migration line).
- [ ] Diagnostics export `summary.txt` → schema version **16**.

## 3. Spell check (highest priority)

- [ ] Daily Feature: type `accomodation`. After a short pause it gets a red wavy underline. No crash, typing stays smooth.
- [ ] Type `accommodation` (correct). Not underlined.
- [ ] Type `Carowinds SCarowinds WinterFest ERT DAP Conjuring FunPix Passdown passholder Copperhead Intimidator`. None underlined.
- [ ] Type `VIP` and `Fury325`. Not underlined (acronym / digits skipped).
- [ ] Underline also appears in: ERT note · ERT override reason · private buyout details · custom item notes · daily Operational Notes · Operational Notes title · Operational Notes body (spot-check at least four).
- [ ] **No** underline in: attendance, pre-solds, times, gate overrides, price overrides, custom item price/name, ERT ride lists, Price History search.
- [ ] Right-click the underlined word: up to five suggestions, **Ignore**, **Add to dictionary**, then **Cut / Copy / Paste** (enabled as appropriate).
- [ ] Right-click a correct word: the normal Cut/Copy/Paste menu.
- [ ] Context-menu key / Shift+F10 with the caret in a flagged word opens the same menu; arrow keys + Enter pick a suggestion.
- [ ] Pick a suggestion: the word is replaced, the caret sits after it, typing continues, and the draft autosaves (reopen the date to confirm).
- [ ] **Ignore** a word: it stops being underlined everywhere for this session. Restart the app: it is underlined again (session only). It was not added to the Windows dictionary.
- [ ] **Add to dictionary** with a harmless test word (e.g. `Zarquonia`): it stops being underlined; after restart it stays accepted (Windows custom dictionary). Remove it afterwards if wanted (Settings → Privacy & security → Inking & typing personalization → custom dictionary, or `%APPDATA%\Microsoft\Spelling\<lang>\default.dic`).
- [ ] With a misspelling present: autosave, Preview/Create PDF, finalization, Copy for Teams, navigation and closing the app all work.
- [ ] Type fast for ~20 seconds in the Operational Notes body: no lag/freeze; underlines catch up shortly after pausing.
- [ ] Log: no typed text anywhere. No network activity attributable to spell checking (optional: Resource Monitor → Network while typing with the refresh idle).
- [ ] Failure behavior: NOT EXECUTED unless a safe way exists (covered by `SpellCheckServiceTests`/`SpellCheckBehaviorTests`). If the spell-check language pack is missing, there are simply no underlines and editing works.

## 4. Season Pass price history

- [ ] Associate → Products → **Season pass price history**: overlay opens, read-only, readable. Escape and Close both close it.
- [ ] Season Pass rows and Season Pass Add-On rows show a **Price history** button; Daily Admission, Fast Lane, SCarowinds, Promotions (incl. Bring-A-Friend), general add-ons and custom items do not.
- [ ] The list contains Season Passes, Season Pass Add-Ons and Military/First Responder tier passes only.
- [ ] Backfill: products with earlier official observations show rows dated before today (on the Mac dev copy, the first rows were Aug 22, 2026). Nothing earlier than this install's own observations appears.
- [ ] One-state product: one row (price, First seen, Last confirmed) + "No price changes recorded yet."
- [ ] Multi-state product (if backfilled): oldest first; Change like `+$100.00 (+20.0%)`; US dates (`Oct 15, 2026`); `From $109.00` where the site says "Starting From".
- [ ] Note one product's row count and Last confirmed. Run **Check Online**, then **Deep Refresh**. Same row count; Last confirmed advances to today; price unchanged.
- [ ] After the next automatic 4-hour refresh (or the log entry for it), the same holds. Log shows no "Price changed" lines unless the official price really changed.
- [ ] Search "Gold": only matching products. Clear: list restored. Tick **Include retired products** with a search: retired matches are included.
- [ ] **Include retired products**: retired tracked products (if any) are listed as "· Retired" with their rows; they do not reappear in Associate pricing.
- [ ] Tab order in the overlay: search → checkbox → list → Close; arrow keys move in the list; Narrator reads list items and rows.
- [ ] Increase / decrease / temporary sale / Exact↔From / retrieval error / NotPublished / reactivation / rename: **PASS BY TEST** unless observed live (`SeasonPriceHistoryReconciliationTests`, `SeasonPriceHistorySqliteTests`, `PriceHistoryTests`, build-host smoke in the manifest).

## 5. Phase 19A / 19B regressions

- [ ] Edit Projected Attendance, Pre-solds, ERT, Avenue override, North Gate override → Dashboard updates automatically. No Dashboard Refresh button. Local edits trigger no online check.
- [ ] Today and a future date with stale data auto-check; a past date makes no request. Check Online and Deep Refresh still work.
- [ ] Status line: "Checking official data…" → "Official data checked h:mm AM/PM"; offline shows the partial/failure state.
- [ ] After a comprehensive check, `source-status.txt` shows `dated-bring-a-friend-duo.official` **Ok**; no failure state caused by Duo.
- [ ] SCarowinds night: section with haunted products incl. **The Conjuring: Beyond Fear** if published, correct prices. Non-SCarowinds date: no empty SCarowinds heading (Associate, PDF, Teams).

## 6. PDF, Teams, History, Sync

- [ ] Classic, Compact and Modern PDF all generate; current prices unchanged in meaning (no history shown in PDFs).
- [ ] Copy → Today's Pricing, Operations Summary, Operations + Pricing paste correctly into Teams.
- [ ] An older finalized History entry opens unchanged; Open Final PDF shows the original.
- [ ] Personal Sync (if enabled): a synced draft/note/ERT change still works; editor-conflict behavior unchanged. Price history is **not** synced (expected: two PCs may show different histories if one was off).
- [ ] Two-PC sync: NOT EXECUTED unless a second RC.4 PC exists.

## 7. Logs, diagnostics, display, EXE

- [ ] Log contains "Price history baseline created" only for new products, and "Price changed" / "Price semantics changed" only for real changes. No line per unchanged confirmation. No note text, spell-check text, clipboard text or raw web responses.
- [ ] Diagnostics `summary.txt`: schema 16, "Season price history" counts + latest change time, automatic refresh section, catalog lifecycle counts. No notes, DB, PDFs, sync bodies or price lists.
- [ ] 100% / 125% / 150% scaling: spell-check context menu, Price History overlay/table/checkbox/search, Products row buttons, existing Associate controls — no clipping/overlap.
- [ ] Non-US region (temporarily): `$` prices, US dates, `+6.1%` percentages, AM/PM freshness times, PDFs/copy unchanged. Restore the region.
- [ ] Experimental EXE: starts, About shows rc.4, uses the same `%LOCALAPPDATA%` data (already schema 16, no new migration line), writes nothing next to the EXE, closes cleanly. Do not distribute the EXE alone (no notices).
