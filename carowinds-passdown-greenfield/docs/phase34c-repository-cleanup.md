# Phase 34C — Repository cleanup, code consolidation and maintainability

Status: **implemented locally, not released.** Branch `feature/passdown-redesign`. Starting HEAD `e230520`
(RC13-Phase34B2-LOCALTEST record). RC.12 (`v0.1.0-rc.12`, tag → `7763c7e`) and every older release stay frozen;
source `<Version>` is still `0.1.0-rc.12`. Nothing was pushed, tagged, signed or published. Next release: RC.13.

Evidence labels: **[Measured]** on this Mac (Apple silicon, macOS, .NET SDK 10.0.400, Release builds);
**[Code]** confirmed by reading the source; **[Gate]** a before/after comparison harness described below.
Mac measurements are not Windows results.

---

## 1. Executive summary

1. **Smaller package, same output.** Carowinds.Passdown.Documents.dll now embeds 6 of QuestPDF's 18 Lato faces
   (13.8 MB → 6.0 MB in the package). A 45-PDF compatibility gate (every template, long notes, math, special characters, 1–6
   pages) produced **byte-identical PDFs** before and after. `logo.png` (source art, never loaded) is no longer
   embedded in Desktop.dll. Windows ZIP **80,979,212 → 75,953,582 bytes (−5.0 MB, −6.2 %)**; uncompressed −8.6 MB (§6).
2. **One accesso transport instead of six.** The six dated accesso sources share one calendar POST/parse step and
   one evidence builder. 2,142 → 1,617 lines in the sources (+144 shared). Product parsing and every decision
   stay per source. A new 66-case cross-source matrix passes both before and after the change.
3. **Two unused members removed**, three candidates deliberately kept (§3).
4. **Test suite:** 5,432 → **5,512** passing tests (+80), 0 skipped, 0 weakened; the 5.2 s busy-timeout test
   now takes ~1 s with the same real lock; the stale "Schema 11" startup test name is fixed.
5. **Deferred with reasons:** Windows-only Avalonia backends (startup-path risk), AssociateViewModel ERT
   extraction (would change bindings), printing buffers and Personal Sync retry (no Windows evidence), the
   dormant browser-session seam (documented reinstatement point), headless test partition (unchanged).

## 2. Starting baseline (verified, not assumed)

| Item | Value |
|---|---|
| Repository / branch / HEAD | `carowinds-passdown-greenfield` (repo root is the parent folder) / `feature/passdown-redesign` / `e230520` |
| Phase 34B-1 / 34B-2 present | `87b265e`, `5cf716e` are ancestors of HEAD; latest record `e230520` |
| Working tree | only the pre-existing staged/modified `.DS_Store` files and `.claude/skills/pdf-design/SKILL.md` — untouched and not in any 34C commit |
| Source version | `0.1.0-rc.12` (`Directory.Build.props`) |
| Database schema | 18 (`Migration0001`–`Migration0018`) |
| Key dependencies | Avalonia 12.1.1, QuestPDF 2026.7.3, PDFium 159.0.8138 (bblanchon), Microsoft.Data.Sqlite / Extensions 10.0.11, CommunityToolkit.Mvvm 8.4.2; tests xunit 2.9.3, PdfPig 0.1.15 |
| Deterministic suite | **5,432 / 5,432** (Domain 462, Application 1,369, Documents 479, Infrastructure 1,211, Ui mixed 1,746, run-alone 26/117/2/7/4/9) — re-run at the start of this phase [Measured] |
| Clean Release build | 0 warnings, 0 errors |
| Latest local candidate | `RC13-Phase34B2-LOCALTEST`, 80,979,212 bytes, 308 files |
| Frozen artifacts | 54 files (every file under `artifacts/releases` plus every earlier local-test ZIP) hashed before any change and re-hashed byte-identical after the RC13-Phase34C-LOCALTEST build |

## 3. Unused code and resources

Each candidate was checked against production source, tests, tools, XAML, JSON/SQLite/sync serialization,
reflection (`GetMethod`, none in `src`) and DI.

| Candidate | Finding | Decision |
|---|---|---|
| `SeasonResolution.LatestOccurrence` (Domain/Seasons/OperatingEvents.cs) | 0 references anywhere (one test *name* contains the phrase); a method on a record, never serialized; no doc or spec mentions it | **Removed** |
| `SyncEntityState.IsDirty` (Application/PersonalSync) | 0 references; `SyncReconciliation` compares `SyncedContentHash` itself; a method, so not part of the persisted sync state or the sync file format | **Removed** (no Personal Sync format or behavior change) |
| `OfficialPassPerkHistory.FirstSeenAtOfCurrentTerms` | **Used** — `PassPerksDocuments.cs:84` calls it as an extension method when a perk's terms change (the audit's search missed the extension-call form) | **Kept** |
| `SourceState.RecordSuccess` / `RecordFailure` (Domain) | No production caller (the orchestrator builds `SourceState` directly) but used by `SourceStateTests` and `FreshnessPolicyTests` as the documented state transitions | **Kept** as a legitimate Domain API / test seam |
| `Desktop/Assets/logo.png` (841 KB) | Not loaded by code, XAML, packaging or tests; `logo.ico` is the window and EXE icon; Phase 13G names it the icon's source art | **No longer embedded** (`<AvaloniaResource Remove="Assets\logo.png" />`); file kept in the repo; the original `Include="Assets\**"` line is unchanged |

New `Phase34CEmbeddedAssetTests` (Ui, 5): `logo.ico` and the banner are still embedded, `logo.png` is not, the
source art still exists, and only that one file is removed from the glob. Window/EXE icon and branding are
unchanged (existing `ProductBrandingTests` and `Phase22B`/`Phase23B` tests pass unchanged).

**Explicitly retained:** Migrations 0001–0018, the deferred Manager draft model and its migration, legacy
Personal Sync handling, price-identity folding, Morning Message retirement tests and guards, historical
file-format readers, updater compatibility code, backup and recovery protections.

## 4. Accesso consolidation design

**Before:** five sources each carried an identical `CallCalendarAsync` + `CalendarCallResult` (differing only in
payload type), the admission source inlined the same transport, and all six repeated `NotPublished`,
`Unavailable`, `Error` and `Truncate` [Code — diffed after normalizing the payload type: identical].

**After** (`Infrastructure/Retrieval/Sources/AccessoDatedSourceSupport.cs`):

- `AccessoKeywordCalendarCall.PostAsync(httpClient, body, ct)` — the POST with the shared endpoint and headers,
  then non-success status, malformed JSON and missing `SERVICE` checks. Returns the parsed service plus raw
  body, or the `Exception` describing the failure. `OperationCanceledException` still propagates. One shared
  `Truncate` (500 characters).
- `AccessoDatedResults<TPayload>` — builds the NotPublished / Unavailable / Error results with the source's own
  id, evidence name and official page (`DirectApi`, as every copy had). Wording is still passed in by each source.

**Unchanged, still per source:** request bodies (keyword, `display_zero_capacity`, dates, fresh
`request_token`), call order (primary → Conjuring → horizon), `IsNoDatesFound` handling, status decisions, date
confirmation, product parsing and classification, user-facing sentences, `SourceId` values.

**Phase 34B-2 coalescing untouched.** `AccessoKeywordCalendarCoalescing.KeyBody` and
`CoalescingRetrievalHttpClient` were not edited; the sources still call whatever `IRetrievalHttpClient` they are
given, so only the known nonce on the approved endpoint is ignored, every other field and header stays
significant, per-caller cancellation and failure isolation are unchanged, and each source still interprets the
shared response for its own date with its own timestamp.

**Why not more:** a generic "dated accesso source" base class was rejected — the six differ in call graphs
(admission: one window; haunted: two keywords + horizon; Military: eligibility classification), and merging them
would hide those differences. Evidence wording stays duplicated on purpose (it is user-facing and differs).

**Line counts:** admission 315 → 262, Fast Lane 345 → 247, haunted 405 → 307, Bring-A-Friend 345 → 253,
BAF Duo 329 → 237, Military/First Responder 403 → 311; total 2,142 → 1,617, plus 144 in the shared file.

**Regression coverage.** The existing per-source fixture suites (admission 21, Fast Lane 22, haunted 27, BAF 20,
Duo 18, Military 19 tests; published, missing, closed, gap, beyond-horizon, different seasons, date mismatch,
product classification, freshness timestamps) run unchanged. New `Phase34CAccessoDatedSourceConsolidationTests`
runs 11 cases × 6 sources (66):

| Case | Pins |
|---|---|
| Transport failure | that source's Error: id, evidence name, page, default wording, `DirectApi`, requested date, Eastern `TimeChecked`, exception in technical detail; no follow-up call |
| Non-success status (503, 700-char body) | exact technical text `POST … returned 503 ServiceUnavailable. Body: <500 chars>…` |
| Malformed JSON / missing `SERVICE` | Error with `JsonException` / the exact shape message |
| Caller cancellation | propagates, never becomes an Error |
| Later call fails after an earlier success (partial failure) | the whole result is an Error, so last-known-good is never replaced by a partial answer |
| "No dates found" from today | NotPublished with that source's wording, no technical detail, expected call count |
| Past date | Unavailable, zero requests |
| Request shape | official endpoint, storefront headers, keyword, a distinct `request_token` per call |
| Through the coalescing client | a repeated check reuses every calendar POST; a different date is asked again; a failed POST is never reused |

The identical test file was also run against the pre-refactor commit in a temporary worktree: **66/66 passed
there too**, which is the equivalence evidence. Last-known-good persistence and historical snapshots live in
the orchestrator and repositories (not in these classes) and are covered by the unchanged Application and
Infrastructure suites; no price or source-derived term was edited.

## 5. Architecture decisions

| Item | Decision | Reason |
|---|---|---|
| AssociateViewModel (2,907 lines) — ERT editor extraction | **Deferred** | The ERT editor is not self-contained: 14 XAML bindings point at AssociateViewModel properties (moving them changes binding paths, which this phase must not do); it shares `SelectedDate` supersession checks, `SyncConflictMessage`, `RefreshWorkspaceAsync`/`ApplyWorkspaceToFields` (which closes the panel) and the date-switch dirty check. Forwarding properties would add code without reducing coupling. Recommended later: a child `ErtEditorViewModel` with the XAML moved to a `UserControl` in the same change, with real-Window tests. Operational-note selection has the same binding constraint. |
| UI-thread dispatch abstraction | **No new abstraction** | `IUiDispatcher` / `AvaloniaUiDispatcher` already exist (Phase 19A) and six view models use them, with `ImmediateUiDispatcher` for tests. 34B-2 measured Associate/Calendar loads at 0.25–0.78 ms, so no call site benefits from moving off the UI thread now. Three remaining direct `Dispatcher.UIThread.Post` calls (Associate/OperationalNotes sync reload, Personal Sync status) could adopt the seam when those classes are next touched. Price History reader untouched. |
| Dormant browser-session layer (`IJobBrowserSession*`) | **Deferred** | Removing it changes the `RefreshOrchestrator` constructor and `SourceRegistration` (`RequiresBrowserSession`), ~18 references incl. orchestrator tests that pin lazy creation and disposal. It is the documented reinstatement point if a source ever needs a browser (CLAUDE.md retrieval rule 3; `PlaywrightDependencyTests`). It costs nothing at runtime (`Create()` is only called when a due source requires it; none does). Playwright is not reintroduced. |
| Printing memory (225 MB transient per 600 DPI page) | **Deferred** | No Windows evidence of a memory or responsiveness problem: the 34B-1/34B-2 checklists' J4/J5 memory fields were not reported, and printing passed Windows testing in general. The ArrayPool / `RenderPageInto` / one-load-per-page proposal (34B-2 §9.3) still needs Windows spool-time and output validation. |
| Personal Sync transient-lock retry | **Deferred** | No observed sharing violation (34B-2 checklist L1 reported nothing). Current failure mode is already safe (original intact, temp removed, next cycle retries). |

## 6. Package size

| | RC13-Phase34B2-LOCALTEST | RC13-Phase34C-LOCALTEST | Change |
|---|---|---|---|
| ZIP (bytes) | 80,979,212 | 75,953,582 | **−5,025,630 (−6.2 %)** |
| Uncompressed (bytes) | 177,363,581 | 168,741,499 | **−8,622,082 (−4.9 %)** |
| Files | 308 | 308 (identical file list) | |
| `Carowinds.Passdown.Documents.dll` | 13,795,328 | 6,041,600 (−7,753,728) | |
| `Carowinds.Passdown.Desktop.dll` | 6,049,792 | 5,206,016 (−843,776) | |

Release build (framework-dependent bin): Documents.dll 13,542,912 → 5,787,648 bytes.

**Windows-only Avalonia backends — deferred.** `Avalonia.Desktop` (28 KB) provides `UsePlatformDetect()`, which
binds directly to `Avalonia.Win32`, `Avalonia.X11` and `Avalonia.Native`. Dropping X11/FreeDesktop/AtSpi/Native/
`Tmds.DBus.Protocol` (4.63 MB uncompressed) from the win-x64 publish would need either RID-conditional package
references plus a Windows-only `UseWin32().UseSkia()` startup path that macOS development and the headless tests
never execute, or post-publish deletion that relies on the JIT never resolving those types. Both are the fragile
kind of change this phase must avoid without Windows startup validation. Recommended later only with a Windows
startup smoke test in the release checklist.

Nothing else was removed from the package: PDFium, its 17 dependency notices, QuestPDF and Skia binaries,
runtime diagnostic components, Inter and STIX fonts, the Lato OFL notice, migrations, update verification and
icons are all still shipped.

## 7. PDF fingerprint and visual equivalence

**Gate [Gate].** A scratch console harness (outside the repository) referenced the Documents project and
rendered 15 documents × Classic/Compact/Modern = **45 PDFs**: the 11 Documents.Tests sample days (minimal,
sparse, normal, dense, buyout, stale, excessive, season-pass, SCarowinds, add-ons, long Daily Feature) with their
`DateTimeOffset.UtcNow` stamps pinned, plus 80 long operational notes with special characters; special
characters in notes and Daily Feature (`é ñ Ü ß — – “ ” ‘ ’ … • ½ ¾ © ® ™ € £ ¥ ° ± × ÷ ≤ ≥ → ✓`); display and
inline math; and dense + long notes + math. Up to 6 pages; tables (product price grids) in every document.

Both runs deleted QuestPDF's loose `LatoFont/*.ttf` from the harness output first — the shipped package has no
loose Lato files, and QuestPDF otherwise auto-discovers all 18 from disk, which would mask the change. (Before
the change, loose + embedded vs embedded-only were confirmed identical.)

| Check | Result |
|---|---|
| Normalized PDF bytes (`/CreationDate`, `/ModDate` zeroed, the repository's established normalization) | **45 / 45 byte-identical** |
| Page counts, letter counts, per-letter font + position hash, fonts used | identical for all 45 |
| Faces used anywhere | Lato Regular, Bold, SemiBold, Italic; STIX Two Math; Type3 fallback glyphs — identical before/after |

Byte identity implies identical typography, page breaks, glyph coverage, preview pixels and printed output
(the print path rasterizes these same bytes). No font was substituted and no template was changed.

**Guard:** `Phase34CEmbeddedFontTests` (Documents, 5) pins the six embedded resources, fails if any Documents
source asks for Thin/ExtraLight/Light/Medium/ExtraBold/Black or a `FontWeight`, and checks that rendered PDFs
of every template only contain embedded Lato faces and STIX.

**Visual fixtures [Measured].** `tools/visual-fixtures` (all scenarios, 1280×860, Light and Dark) rendered from
`e230520` and from the final commit: 141 of 154 PNGs pixel-identical. The other 13: 12 scenarios differ between
two runs of the *same* build too (they show the wall-clock "Saved … at" time or capture an in-flight preview),
and Settings differs only by the commit hash in "About this build".

## 8. Test-suite performance

| | Baseline (`e230520`) | Final |
|---|---|---|
| Tests | 5,432 | **5,512** |
| Infrastructure runner time | 22 s | 18 s (with +70 tests) |
| Sum of all partition runner times | 81 s | 78 s |
| `UpdateInstallerDatabaseSafetyTests.DatabaseStateUnreadableAfterAFailedStart…` | 5.2 s | ~1 s |

Busy timeout: `SqliteDatabaseBackup` takes an optional `busyTimeoutSeconds` (default and production value 5,
minimum 1); `SqliteUpdateDatabaseGuard` reads the schema version with its backup's timeout. The test keeps its
real `BEGIN EXCLUSIVE` lock and all assertions and passes 1 s. `Phase34CBackupBusyTimeoutTests` (4) pins the
5 s default, rejects < 1 s, and proves a real lock still makes the guard's read fail and the committed version
is read once the lock is released. Production timeout and `SqliteConnectionFactory` are unchanged.

Startup test: `ColdStartToWindowOpened_ExistingSchema11Database` → `…_DatabaseAlreadyAtLatestSchema`, which now
asserts that the pre-migrated database is at the latest migration version. Opt-in
`CAROWINDS_STARTUP_TIMING_SAMPLES` (1–25) reports a median over more samples (5-sample Mac run: fresh 206 ms,
already-migrated 194 ms median); the deterministic suite still takes 1 sample and asserts no time.

**Headless partition — unchanged.** The six real-Window classes still run alone. The Phase 34A audit observed
148 failures from one mis-partitioned run. This phase found no supported, already-proven way to give each
real-Window class its own headless dispatcher inside one test run, and an experiment that mixes them can only
show flakiness probabilistically, so the partition was not changed: merging the classes would trade
reliability for fewer commands. Limitation documented; partition list in the release checklist unchanged.

## 9. Changes deliberately deferred

See §5 and §6: AssociateViewModel ERT/notes extraction, Windows-only Avalonia backends, printing buffers,
Personal Sync retry, the browser-session seam, headless partition consolidation.

## 10. Before-and-after performance [Measured, Mac]

`tools/perf-benchmarks all` on `e230520` and on the final commit, back to back, same machine; loose Lato files
removed from both harness outputs to mirror the package.

| Benchmark | `e230520` (34B-2) | Phase 34C | Verdict |
|---|---|---|---|
| First PDF, cold process (`pdf-startup`, generate / first preview work, median of 7) | 77.2 / 83.6 ms | 76.3 / 82.6 ms | unchanged (−1 ms, within noise) |
| First PDF after background warm-up | 10.2 / 13.7 ms | 10.2 / 13.4 ms | unchanged |
| Warm-up cost on its background thread | 82.0 ms | 79.9 ms | unchanged |
| Live preview, isolated edit → page image (`preview`, busy / 80 notes, median of 25) | 15.5 / 40.9 ms | 16.6 / 40.9 ms | unchanged (busy p95 17.5 → 19.3 ms; min/max ranges overlap) |
| Preview render, direct path (`pdf-render`, busy / 80 notes, median of 300) | 0.65 / 2.63 ms | 0.65 / 2.63 ms | identical |
| Managed allocation per preview render | 1.3 / 1.8 KB | 1.3 / 1.8 KB | identical |
| Price History product switch via reader, 0 / 2,000 / 20,000 synced rows | 0.13 / 0.15 / 0.50 ms | 0.12 / 0.14 / 0.53 ms | unchanged |
| Price History open via reader, 0 / 2,000 / 20,000 | 3.4 / 10.7 / 90.2 ms | 3.3 / 10.7 / 90.4 ms | unchanged |
| Official POSTs, 7-day window (`refresh-requests`) | 21 → 9 coalesced | 21 → 9 coalesced | identical (coalescing preserved) |
| Print band loop per 600 DPI page, busy / 80 notes | 68.3 / 105.6 ms, 225 MB | 64.8 / 104.4 ms, 225 MB | unchanged (print path not modified) |
| Test suite (sum of partition runner times) | 81 s | 78 s | −3 s with 80 more tests |
| Windows package | 80,979,212 B ZIP | 75,953,582 B ZIP | −5.0 MB |

The audit's estimate that fewer Lato faces would cut ~35 ms from the first PDF did **not** materialize on this
Mac: QuestPDF's registration cost does not scale with the number of embedded faces here. The gain is package
size only. No benchmark regressed beyond run-to-run noise.

## 11. Test totals

Release, `Category!=LiveSmoke`, six run-alone Ui classes:

| Project / partition | Baseline | Final | New tests |
|---|---|---|---|
| Domain | 462 | 462 | |
| Application | 1,369 | 1,369 | |
| Documents | 479 | 484 | +5 `Phase34CEmbeddedFontTests` |
| Infrastructure | 1,211 | 1,281 | +66 `Phase34CAccessoDatedSourceConsolidationTests`, +4 `Phase34CBackupBusyTimeoutTests` |
| Ui mixed | 1,746 | 1,751 | +5 `Phase34CEmbeddedAssetTests` |
| OperationsCalendarView / Phase15ERealWindowLayout / RealWindowStartupTiming / SpellCheckNavigationLifecycle / Phase21DMathEditorWindow / Phase33PreviewWindow | 26 / 117 / 2 / 7 / 4 / 9 | same | |
| **Total** | **5,432** | **5,512 passed, 0 failed, 0 skipped** | +80 |

No test was deleted, skipped or weakened. The only removed test lines are the old startup-test name, its
`const int Iterations = 1` (now a property defaulting to 1) and the busy-timeout test's `Prepare()` call (now
`Prepare(new SqliteDatabaseBackup(busyTimeoutSeconds: 1))`). Clean Release build (`--no-incremental`):
0 warnings, 0 errors.

Covered by the full run: retrieval source fixtures, Phase 34B-2 coalescing (`Phase34B2PostCoalescingTests`),
database/migration and backup tests, Personal Sync, PDF generation and preview (`Phase33*`, `Phase34B1*`,
`Phase34B2Direct*`), Print and Save PDF view-model tests, Phase 34B-1 scheduling, Price History, updater and
release-integrity tests.

## 12. Windows validation plan

Local candidate `RC13-Phase34C-LOCALTEST` (source `a569401`, ZIP 75,953,582 bytes, SHA-256
`cb43a1fd025aba3af03ab53da814b72858b03463c5858bbe3f06836ce768821d`) — record and checklist:
`docs/releases/rc13-phase34c-localtest-windows-checklist.md`. Highest-value checks: application starts (the
Desktop resource change), every PDF template looks identical in preview, Save PDF and Microsoft Print to PDF
(the font change), Check Online for admission, Fast Lane, haunted, Bring-A-Friend (+ Duo) and Military dates
(the accesso change). An update install/rollback run is not required: the busy-timeout change only adds an
optional parameter whose default is the previous value.

## 13. Known limitations

- Mac evidence only. Windows startup, fonts in the Windows print spooler, and DPI scaling need the checklist.
- The font gate covers the 15 documents above; a future template using a new weight is caught by the guard test,
  not by the gate.
- The visual-fixture harness has 12 inherently time-dependent scenarios; they were reviewed by eye only.

## 14. Remaining recommended cleanup

1. ERT editor → child view model + `UserControl` (one change, with real-Window tests).
2. Adopt `IUiDispatcher` in the three remaining direct `Dispatcher.UIThread.Post` sites.
3. Windows-only Avalonia backends, with a Windows startup smoke test.
4. Print band buffers (34B-2 §9.3) after Windows J4/J5 measurements.
5. `Truncate` is still duplicated in seven non-dated sources (catalog, park hours, shows, promotions); fold into
   one helper next time those sources change.
6. Browser-session seam removal only if the project formally decides never to use browser retrieval.

## 15. RC.13 release-readiness considerations

- Windows checklist for `RC13-Phase34C-LOCALTEST` must pass first; 34B-1 and 34B-2 candidates were confirmed
  working by the product owner.
- RC.13 prep: bump `<Version>` to `0.1.0-rc.13`, bundled `release-notes.md`, `TESTER-GUIDE.txt`,
  `README-portable-release.md`; build signed release from a clean tree; verify RC.12 → RC.13 in-app update.
- Database schema 18 and the Personal Sync format are unchanged, so RC.12 and RC.13 computers can share a sync
  folder.
