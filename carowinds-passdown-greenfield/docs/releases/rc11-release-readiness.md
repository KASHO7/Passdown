# RC.11 release readiness — 0.1.0-rc.11

Date: 2026-11-21 · Branch `feature/passdown-redesign` · Build commit **`573d17c`**. Built on macOS.
**Nothing uploaded, tagged, pushed or merged.** Publication is gated on the Windows validation below (§6).

Result values: **PASS** · **USER-VERIFIED ON WINDOWS** · **FAIL** · **NOT EXECUTED** · **BLOCKED**. "PASS" means an
automated or build-host check; **USER-VERIFIED ON WINDOWS** means the product owner ran the step by hand on
Windows with the RC.11 ZIP below.

## Candidate

The candidate is held locally (LOCAL-ONLY until the gate passes). The Windows test uses **these exact signed
files** — not a separately built test ZIP — so what is tested is what would be published.

| File (`artifacts/releases/`) | Bytes | SHA-256 |
|---|---|---|
| `Carowinds-Operations-0.1.0-rc.11-win-x64.zip` | 76,428,245 | **`ab34110b99a23a83a02c70d167ddc3c3fd49f8a6cf6f61c8909bc01dd68d8e0d`** |
| `Carowinds-Operations-0.1.0-rc.11-win-x64.update.json` | 49,316 | `78916c9de9bc689b76617634b6b6ed50e5dc89f557d8336aac224c6d348016aa` |
| `Carowinds-Operations-0.1.0-rc.11-win-x64.update.json.sig` | 206 | `066d6b2401ee526cb096c227606ac034be4e8b17675ca154c0c78bd4a04f3ee8` |

All three are read-only. Informational version in all six first-party assemblies:
`0.1.0-rc.11+573d17c86f127ce98c3997eb1dc27de37cb20c8a`. About this build should read `0.1.0-rc.11 (573d17c)`.
No single-file EXE was built.

What RC.11 contains since RC.10 (`a8a1d96`): Phase 31 (dynamic seasons, official deals, schema 18), 31B
(availability, fine print, price-history aggregation, ERT sync), 31B finalization, 31C (compact Products, View
details), 31D (Passdown Preferences, Operational Notes default, Shows checkbox under Include in Passdown), 31E
(Shows sidebar section), and the RC.11 build commits `d02b9e4` (migration safety tests, What's New) and `573d17c`
(tester guide and portable README). `src/` is identical at `d02b9e4` and `573d17c`.

## 1. Baseline

| Check | Result |
|---|---|
| Start: branch `feature/passdown-redesign`, HEAD `21808c6`; 6 commits since the RC.10 record `f5469d1` (= `origin/feature/passdown-redesign`), all committed | PASS |
| Commits between RC.10 tag `a8a1d96` and `f5469d1` are docs-only (RC.10 record) | PASS |
| Working tree: only the pre-existing unrelated `.DS_Store` / `.claude/skills/pdf-design/SKILL.md` changes, left staged and untouched (not in any RC.11 commit) | PASS |
| Shows is a sidebar section (Overview, Products, Shows, Notes); no Shows panel on Overview; Shows checkbox under Include in Passdown; Passdown Preferences persisted in `passdown-defaults.json` — present in `21808c6` and covered by tests | PASS |
| No Morning Message code in `src/`; none in any packaged file | PASS |
| RC.8 (4 files), RC.9 (4 files) and RC.10 (3 files) SHA-256 unchanged before and after the RC.11 build and signing | PASS |

## 2. Regression suite and build

| Check | Result |
|---|---|
| Clean Release build (bin/obj deleted, `--no-incremental`) at `d02b9e4`: 0 warnings, 0 errors | PASS |
| Deterministic suite (Release, `Category!=LiveSmoke`, Ui run-alone partition) at `d02b9e4`: **5,105 / 5,105** — Domain 402, Application 1355, Infrastructure 1144, Documents 479, Ui mixed 1569, OperationsCalendarView 26, Phase15ERealWindowLayout 117, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4 | PASS |
| At `573d17c` (packaged text only): Ui `Release` tests 49/49, Infrastructure packaging tests 86/86, 0 warnings | PASS |
| Weakened tests: no test files deleted since RC.10, no skipped tests; 14 test lines removed (31D/31E retargeting of moved controls) | PASS |
| Live-website tests (LiveSmoke) | NOT EXECUTED (opt-in by design) |

Functional areas covered by the suite (automated, disposable data; not Windows verification): official park hours
and prices; dynamic Oktoberfest / SCarowinds / WinterFest handling; Current Deals and Black Friday offers;
Military Gold Pass availability; Important Details and deadlines; Price History aggregation and price-change
calculation; Associate save/reopen; Shows navigation and per-show selection; Shows PDF inclusion independent of
selection; Passdown Preferences and Operational Notes default; ERT persistence and conflict resolution; Personal
Sync; Notes, Calendar, Dashboard, History; Classic and Modern PDFs; the three original Copy options; updater
and manifest verification.

## 3. Database migration (schema 17 → 18)

Disposable profiles in temporary folders only (`Rc11MigrationSafetyTests`, plus Phase 27/31 migration tests). The
real operational database on the build Mac (`~/Library/Application Support/Carowinds Passdown`, last modified
2026-10-15) was not opened; the app was never launched.

| Check | Result |
|---|---|
| RC.10-shaped profile (schema 17: drafts, notes, season price history, synced price observations, History PDF) upgrades to 18 | PASS |
| Updater pre-update backup `pre-update-0.1.0-rc.10-to-0.1.0-rc.11` written first, schema 17, identical rows, integrity OK | PASS |
| Runner pre-migration backup `pre-migration-schema-17-to-18` written before migrating, schema 17, identical rows, integrity OK | PASS |
| Every existing row preserved; only `official_reference_documents` and one migration-log row added; `PRAGMA integrity_check` OK | PASS |
| Second start is idempotent and makes no further backup | PASS |
| No silent downgrade: an RC.10 build refuses the schema-18 database (`UnsupportedSchemaVersionException` 18 > 17) and leaves it untouched; RC.10 opens the backup | PASS |
| Failed migration 18 rolls back to 17 with every row intact, keeps the backup; a later start succeeds | PASS |
| Insufficient disk space for the backup stops at 17 and changes nothing | PASS |
| The Desktop app registers exactly migrations 1–18 | PASS |
| Same on Windows with a real RC.10 profile | USER-VERIFIED ON WINDOWS (§6 items 2, 3, 14) |

## 4. Package, manifest and signature

| Check | Result |
|---|---|
| `Build-PortableRelease.ps1` (frozen-release guard active): `Test-ReleasePackage.ps1` passed on staging and ZIP (287 files); `RealReleaseZip_WhenSupplied_PassesEveryRule`; `unzip -t` clean; one top-level folder | PASS |
| Same 287-file list as RC.10 | PASS |
| Bundled What's New = `0.1.0-rc.11`; `TESTER-GUIDE.txt` and `README.txt` updated for rc.11 and its database upgrade | PASS |
| No developer paths, PDBs, databases, logs, `.update.json`/`.sig`, LOCAL-ONLY markers or Morning Message text in the package; 16-byte-window scan for private-key bytes: 0 files | PASS |
| Manifest: format 1, `carowinds-operations`, `0.1.0-rc.11`, `release-candidate`, `win-x64`, title "Seasons, official deals and Passdown preferences"; package size 76,428,245 and SHA-256 recomputed = manifest; 287 file entries, each size + SHA-256 recomputed from a fresh extraction, 0 mismatches | PASS |
| Signature: `carowinds-release-1` (ECDSA P-256), key at the established path outside the repository, never printed or copied; `verify` with the app's `appsettings.json` = **VERIFIED**; `appsettings.json` (trusted key) unchanged since RC.10 | PASS |
| Tamper: version text changed → `InvalidSignature`; package SHA-256 changed → `InvalidSignature`; original → VERIFIED | PASS |
| RC.10 → RC.11 offered by RC.10's updater (numeric rc ordering, same trusted key) | PASS (by tests + analysis) |
| In-app RC.10 → RC.11 install on Windows | NOT EXECUTED — possible only after publication (§8) |

## 5. Decision so far

Automated and build-host checks: **all PASS, nothing FAIL.** Windows validation: **14 / 14 USER-VERIFIED ON
WINDOWS, 0 FAIL** (§6). The gate is passed; publication proceeds as authorised (§7). If anything fails, the
candidate is kept as-is for diagnosis and a fix ships as `0.1.0-rc.12`; these signed files are never rebuilt.

## 6. Windows validation checklist (RC.11)

Use a **disposable Windows test account** (Settings → Accounts → Other users → Add account) or a **VM snapshot**,
so your real `%LOCALAPPDATA%\Carowinds Passdown` and real sync folder are never touched. Use made-up values only.

**Setup — make an RC.10 profile first (for items 2–3):**

1. Download RC.10 from https://github.com/KASHO7/Passdown/releases/tag/v0.1.0-rc.10, extract, run it once.
2. In RC.10: save a draft for a date (type attendance, uncheck **Operational Notes**), add an Operational Note,
   open More ▾ → Season Price History once. Close RC.10.
3. Copy the RC.11 ZIP over. PowerShell: `Get-FileHash <zip>` must be
   `AB34110B99A23A83A02C70D167DDC3C3FD49F8A6CF6F61C8909BC01DD68D8E0D`.
4. Extract RC.11 to a **new** folder (not over RC.10) and run `Carowinds.Passdown.Desktop.exe`.

| # | Check | Expected | Result |
|---|---|---|---|
| 1 | Version and startup | Opens in seconds, no .NET/admin prompt. Settings → About this build: `0.1.0-rc.11 (573d17c)` and the new folder | USER-VERIFIED ON WINDOWS |
| 2 | Schema 17 → 18 migration | Starts normally. `%LOCALAPPDATA%\Carowinds Passdown\backups` contains a new `…pre-migration-schema-17-to-18….db`. Optional: starting RC.10 again afterwards does **not** open the upgraded database — RC.10 closes at startup without changing it, and its newest log says "newer than this application supports" (RC.10 has no friendlier message; see §9). Then go back to RC.11 | USER-VERIFIED ON WINDOWS |
| 3 | Data preserved | The RC.10 draft (attendance, Operational Notes still **unchecked**), the note and Season Price History rows are all there | USER-VERIFIED ON WINDOWS |
| 4 | Official pricing and Current Deals | Check Online; Products shows official prices and a compact Current Deals list (Black Friday deals if currently offered) | USER-VERIFIED ON WINDOWS |
| 5 | Military Gold Pass | Listed with its price, or "Availability unconfirmed" — never "Retired" | USER-VERIFIED ON WINDOWS |
| 6 | View details windows | On a deal and on a pass: terms, Buy by / Use by dates, official link; Esc/Close returns; inferred dates labelled | USER-VERIFIED ON WINDOWS |
| 7 | Dynamic seasonal information | Include in Passdown shows the date's official event (e.g. WinterFest on a December weekend); SCarowinds appears only in its season | USER-VERIFIED ON WINDOWS |
| 8 | Shows in left navigation | Sidebar reads Overview, Products, Shows, Notes; Overview has no Shows list | USER-VERIFIED ON WINDOWS |
| 9 | Shows selection and PDF inclusion | Select two shows on Shows; with Shows checked under Include in Passdown they print; unchecked they do not print but stay selected; re-checked they print again | USER-VERIFIED ON WINDOWS |
| 10 | Operational Notes default | A date never opened before has Operational Notes checked; Settings → Passdown Preferences changes survive a restart; the RC.10 date from item 3 keeps its own choice | USER-VERIFIED ON WINDOWS |
| 11 | Classic and Modern PDFs | Both create, open and look right (prices, attendance, notes, shows) | USER-VERIFIED ON WINDOWS |
| 12 | ERT conflicts and Personal Sync | With a **new empty** sync folder: Sync Now OK; if a second test PC is available, the same rides in a different order are not a conflict, different rides offer merge/edit | USER-VERIFIED ON WINDOWS |
| 13 | Restart and clean shutdown | Close (X); app leaves Task Manager; reopen — everything still there; newest log in `logs\` has no unexpected `Exception` | USER-VERIFIED ON WINDOWS |
| 14 | Backup and recovery evidence | `backups\` holds the pre-migration copy and `README.txt`; a copy of that `.db` opens in RC.10 when placed in a separate test profile (optional) | USER-VERIFIED ON WINDOWS |

Report each item as PASS / FAIL with a short note (and a screenshot for any FAIL).

**Reported by the product owner on 2026-11-21: "all of them pass" — 14 / 14 USER-VERIFIED ON WINDOWS, 0 FAIL.**
Only the overall result was reported, not per-item notes. Not claimed from this report: which optional parts were
run (RC.10 reopening in item 2, a second PC in item 12, opening the backup in RC.10 in item 14), display scaling,
and Narrator.

## 7. Publication plan (only after §6 has no FAIL)

Push `feature/passdown-redesign` (no force, no merge to `main`); annotated tag `v0.1.0-rc.11` → **`573d17c`**;
public prerelease "Carowinds Operations v0.1.0-rc.11" with exactly the three files above; then anonymous download,
hash, signature and tag checks. RC.10's release and assets stay as they are.

## 8. Post-publication

| Check | Result |
|---|---|
| Release page, anonymous downloads, hashes, signature, tag → `573d17c` | NOT EXECUTED |
| In-app update RC.10 → RC.11 on Windows (discover, download, backup, install, launch, What's New, data intact) | NOT EXECUTED |

## 9. Known limitations

- **Going back to RC.10 after the upgrade:** RC.10 (frozen) logs "Database schema version 18 is newer than this
  application supports" and exits at startup without changing the database; it shows no dialog. Recovery is
  restoring the pre-migration or pre-update backup (see `backups\README.txt`). Verified by tests on disposable
  data (§3); RC.10's behavior cannot be changed.
- **Mixed versions on one Personal Sync folder:** computers still on RC.10 keep syncing drafts and notes; upgrade
  every computer that shares a folder. Passdown Preferences are per computer and never synced.
- **Not covered automatically:** Windows DPI scaling, Narrator, real OneDrive behavior, and live Carowinds pages
  (LiveSmoke) — covered only by the Windows checklist where listed.
