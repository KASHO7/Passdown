# Phase 34A — Repository audit and performance investigation

Status: **audit only.** No production source, XAML, schema, sync format, dependency, version, release script
or release artifact was changed. Every measurement below came from disposable harnesses in a scratch
directory outside the repository, using synthetic fixtures and in-memory fakes. Next release: **RC.13**.

Evidence labels used throughout:

- **[Measured]** — timed by this audit, with the environment stated in §4.
- **[Code]** — confirmed by reading the current source (file and line given).
- **[Hypothesis]** — plausible from the code, not yet proven; says what would prove it.

---

## 1. Executive summary

1. **The live PDF preview feels slow because of its fixed 450 ms debounce, not because of PDF work.**
   [Measured] On a busy synthetic day, the time from an edit to the new page image is **468–471 ms median**
   for every kind of change: typing, checkboxes, product and show toggles, template switches. Of that,
   **450 ms (96 %) is the wait**. QuestPDF generation takes 13–14 ms, PDFium rendering 4–6 ms, and opening
   the document, copying the bitmap and dispatching to the UI together take under 1 ms. A multi-page
   document (80 notes) reaches 496–503 ms, of which generation is 39–45 ms.
2. **One small change in one class would make the preview feel immediate.** [Measured] With no wait, a
   checkbox or selection reaches the screen in **~20–37 ms**. With a 200 ms wait it takes ~220 ms. The
   recommended approach (§5.4) renders the *first* change right away and then waits 200 ms after the
   *last* change while typing continues. Expected result: discrete changes in ~25–40 ms; text 200–260 ms
   after the user pauses; at most 2 generations per typing burst.
3. **Date switches and the first preview after launch also wait for the debounce.** [Measured] A date
   switch takes 472 ms median, and the first preview arrives 722 ms after the view model is created
   (load + 450 ms wait + ~186 ms cold PDF start). Item 2 removes the 450 ms from both.
4. **The first PDF of a session costs ~186 ms.** [Measured] About 50 ms of that registers **all 19 Lato
   font files (11.9 MB)**, even though the documents use only Regular, Bold, Italic and SemiBold. The rest
   is JIT and QuestPDF warm-up. The work runs off the UI thread, so it delays the first preview without
   freezing the window.
5. **SQLite "async" calls complete synchronously on whichever thread calls them.** [Measured + Code]
   Repository methods return tasks that are already complete. When a view model calls them, all SQL and
   mapping runs on the UI thread. Each call is cheap at synthetic scale: below 1 ms for a date's rows, and
   ~21 ms for a 20,000-row synced price-history scan. This is the main structural risk for UI hitches on a
   slower Windows disk.
6. **One official refresh source re-sends an identical request for every date.** [Code] The admission
   source posts the same 120-day calendar request once for each of the 7 rolling-window dates. POST
   requests bypass the existing request coalescer.
7. **No memory leak in the preview pipeline.** [Measured] Managed memory stayed flat at 5.3 MB and the
   working set at ~129 MB over 600 preview updates. Each update allocates a 4 MB managed pixel array,
   which causes about 0.65 full (gen-2) collections per update. That is cheap at today's heap size, and
   easy to avoid.
8. **The code base is in good health.** There is very little dead code. Morning Message is fully gone from
   `src/`. The main maintainability risks are size, not rot: `AssociateViewModel` has 2,904 lines and the
   six dated Accesso sources share ~2,100 lines of near-duplicate code.
9. **Reliability: no P0 or P1 data-safety defect found.** Atomic writes flush to disk before renaming,
   migrations make a verified backup first, and the updater verifies an ECDSA signature plus per-file
   SHA-256 hashes. One Windows-only hypothesis: a temporary file lock (antivirus or OneDrive) during a
   Personal Sync rename (§14).

---

## 2. Repository baseline

| Item | Value |
|---|---|
| Git root | `/Users/kashyap/Documents/Passdown` (the app lives in `carowinds-passdown-greenfield/`) |
| Branch / HEAD | `feature/passdown-redesign` @ **`b3773e1`** "RC.12 release record: publication verified" |
| Working tree at start | Only pre-existing unrelated changes: `.DS_Store` files (several staged/modified) and staged `.claude/skills/pdf-design/SKILL.md`. Left untouched. |
| RC.12 | Annotated tag `v0.1.0-rc.12` → **`7763c7e`**. ZIP `artifacts/releases/…-rc.12-win-x64.zip` is read-only, 80,955,888 B, SHA-256 `9a36f20b…87372c`, which matches `docs/releases/rc12-release-readiness.md`. Re-hashed after the audit: unchanged (§25). |
| App version | `Directory.Build.props`: `<Version>0.1.0-rc.12</Version>`, `<FileVersion>0.1.0.12</FileVersion>` (not bumped) |
| Database schema | 18 migrations, `Migration0001` to `Migration0018` (`App.axaml.cs:508-528`); user_version 18 |
| Target framework / SDK | net10.0, .NET SDK 10.0.400 |
| Key packages | Avalonia 12.1.1 (+Desktop, Themes.Fluent, Fonts.Inter), CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.* 10.0.11, Microsoft.Data.Sqlite 10.0.11, QuestPDF `$(QuestPdfVersion)`, bblanchon.PDFium.{Win32,macOS,Linux} 159.0.8138; tests: xunit 2.9.3, PdfPig 0.1.15, Avalonia.Headless 12.1.1 |
| Source size (C#, excluding bin/obj) | Application 19.3k, Infrastructure 19.6k, Desktop 17.9k + 14 `.axaml`, Domain 4.6k, Documents 4.6k, Updater 33 lines. Tests ~85.8k lines. 1,311 tracked files. |
| Local build output | `artifacts/` 10 GB (all historical releases + local tests; git-ignored); `bin/` folders ~3 GB |
| Release build | `dotnet build CarowindsPassdown.slnx -c Release`: **0 warnings, 0 errors** |
| Deterministic tests | **5,347 / 5,347 pass**. Domain 462, Application 1,369, Documents 479, Infrastructure 1,184, Ui mixed 1,688, run-alone 26 + 117 + 2 + 7 + 4 + 9. Matches the RC.12 record. |
| Existing perf tooling | `RealWindowStartupTimingTests` (headless, 1 sample, no threshold); `tools/visual-fixtures` (screenshots); Phase 33 doc's ad-hoc timings. **No preview-latency or memory regression harness exists.** |

Docs read: `phase33-live-pdf-preview-printing.md`, `releases/rc12-release-readiness.md`, the Phase 32 entries in
the test-partition record, `release-manifest.md` (partition notes) and `morning-message-rollback.md` (via project memory).

---

## 3. Architecture overview (as built)

- **Domain** (pure rules), **Application** (use cases, refresh orchestration, sync, updates), **Infrastructure**
  (SQLite repositories, HTTP sources, PDFium, Windows printing, file stores, updater engine), **Documents**
  (QuestPDF generation, Daily Feature math), **Desktop** (Avalonia MVVM), **Updater** (33-line launcher).
- The composition root is `App.axaml.cs` (1,019 lines). Startup order (`OnFrameworkInitializationCompleted`,
  lines 97–221): file log → saved appearance → synchronous migration check → spell-check service →
  `MainWindow` + `MainViewModel` → shutdown flush hook → `syncCoordinator.Start()` →
  `refreshCoordinator.Start()` → historical backfill → update coordinator (90 s delay) → background clean-ups.
- `MainViewModel` is built eagerly with **every** section view model: Dashboard, Calendar, Associate, Notes,
  History, Settings and Price History. Each constructor starts its own load. `MainWindow.axaml:110-126` hosts
  all six section views at once and switches between them with `IsVisible`, so navigation never rebuilds a
  view.
- Preview pipeline (Phase 33): `AssociateViewModel.ScheduleAutosave` (`AssociateViewModel.cs:2543`) →
  `PassdownPdfPreviewViewModel.NotifyContentChanged` (`PassdownPdfPreviewViewModel.cs:312`) → 450 ms
  debounce (`:86`, `:750`) → `CapturePreviewRequest` on the UI thread (`AssociateViewModel.cs:1068`, applies the
  draft in memory) → `GeneratePdf` on the thread pool (`:815`) → fingerprint on the UI thread (`:842`) →
  `DisplayAsync` on the thread pool: PDFium open, render page, copy into `WriteableBitmap` (`:892-916`) →
  `SwapPageImage` on the UI thread (`:946`).

No circular project dependencies. The Domain/Application layers contain no Avalonia, SQLite or HTTP types (checked via project references).

---

## 4. Measurement methodology

| Aspect | Value |
|---|---|
| Hardware / OS | Apple M2 Pro (12 cores), 16 GB RAM, macOS 27.2.1 — **the macOS development host, not Windows** |
| Build | **Release** for every number (app projects, test projects and harnesses) |
| Harness | Scratch console app (outside the repo) referencing the production Desktop project plus `Ui.Tests/TestSupport` fakes; Avalonia headless platform with Skia (real `WriteableBitmap`, **no GPU compositor, no real window**) |
| Production code exercised | Real `AssociateViewModel`, `PassdownPdfPreviewViewModel`, `AssociatePassdownPdfExportService` (QuestPDF), `PdfiumPdfRasterizer`, `AvaloniaPdfPageImageFactory`, `ThreadPoolPreviewWorkScheduler`, real `Task.Delay` debounce. Thin timing decorators wrap the export service, rasterizer and image factory. |
| Fakes | `FakeAssociatePassdownService` (instant in-memory load/save), in-memory preference stores, fake printer and save picker — no database, network or profile files |
| Fixture | `BusyDayFixtures.ApplyBusyAssociate` from `tools/visual-fixtures` (synthetic: 12 products incl. passes/memberships, 4 shows (2 selected), ERT, 2 notes, math Daily Feature) = **1-page PDF, 62–71 KB**. Long variant: + 80 long operational notes = **2 pages (Classic/Compact) or 4 pages (Modern), 68–79 KB**. Viewport: fit-width 460 DIP at **150 % scaling** (906 × 1172 bitmap). |
| Repetitions | Warm scenarios: 3 discarded warm-ups + 25 measured (15 for date switch, 10 for bursts). Stage micro-benchmarks: 5 warm-ups + 40 measured. Cold: 5 fresh processes. Median / p95 / max reported. |
| SQLite | Scratch database migrated by the real `MigrationRunner`, filled with synthetic rows: 9,864 `dated_results` (3 years × 9 categories, ~4 KB payload each), 10,000 `season_price_history`, 20,000 `synced_price_observations`; `sqlite3 .timer` and `EXPLAIN QUERY PLAN` |
| Not measured | Real Windows timings, GPU composition/present latency, GDI handles, live official-site timings (live tests stay opt-in) |

**Limits.** "Visible" means the moment the view model assigns `PageImage`. On a real screen the compositor
adds about one frame (~7–17 ms at 60–144 Hz) before the pixels change. That frame is not included. macOS
numbers do **not** prove Windows performance. The Windows checklist (`phase34a-windows-performance-checklist.md`)
covers the gap.

---

## 5. Live PDF preview — latency breakdown

### 5.1 Stage costs, warm [Measured]

Micro-benchmark, 40 iterations after warm-up (ms, median / p95):

| Document | Generate (QuestPDF) | Fingerprint | PDFium open | Render @100 % | Render @150 % | Bitmap copy | Sum @150 % |
|---|---|---|---|---|---|---|---|
| Busy / Classic | 7.07 / 7.93 | 0.07 | 0.03 | 3.19 | 3.00 / 3.43 | 0.15 | **10.5 / 12.1** |
| Busy / Compact | 7.05 / 7.62 | 0.10 | 0.03 | 2.83 | 2.63 | 0.26 | 10.2 / 11.0 |
| Busy / Modern | 7.24 / 7.97 | 0.11 | 0.03 | 3.40 | 3.12 | 0.24 | 10.8 / 11.7 |
| 80 notes / Classic (2 p) | 23.3 / 26.3 | 0.13 | 0.04 | 4.36 | 4.38 | 0.14 | **28.2 / 31.2** |
| 80 notes / Compact (2 p) | 21.5 / 23.0 | 0.05 | 0.04 | 5.01 | 5.21 | 0.11 | 27.3 / 28.4 |
| 80 notes / Modern (4 p) | 21.5 / 22.4 | 0.06 | 0.05 | 3.28 | 3.27 | 0.11 | 25.0 / 26.0 |

In the end-to-end runs below, generation takes ~13–14 ms (busy) and ~39–45 ms (long). Back-to-back loops
measure lower because the thread pool and CPU caches stay hot. In real use the CPU has been idle during the
450 ms wait, so each generation starts cold. The end-to-end numbers are the realistic ones.

### 5.2 End-to-end, input → `PageImage` assigned, 450 ms debounce (production setting) [Measured]

Median (p95 / max), ms. Stage numbers follow the brief's list:

| Scenario | 1 input→notify | 2 debounce wait | 3 →generate start | 4 generate | 5+6 hand-offs + fingerprint | 6 open | 7 render | bitmap | 8 UI dispatch | **10 total** | gens |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Include Fast Lane checkbox | 0.07 | 450.3 | 0.20 | 13.3 | 0.20 | 0.05 | 3.8 | 0.16 | 0.09 | **468.4** (476.1 / 476.9) | 1 |
| Attendance edit | 0.06 | 450.3 | 0.11 | 14.5 | 0.27 | 0.07 | 5.5 | 0.22 | 0.12 | **471.0** (477.1 / 478.4) | 1 |
| Operational Notes keystroke | 0.04 | 450.1 | 0.08 | 13.4 | 0.17 | 0.07 | 5.5 | 0.18 | 0.11 | **469.2** (475.7 / 475.9) | 1 |
| Product Include toggle | 0.03 | 450.2 | 0.07 | 13.6 | 0.13 | 0.05 | 4.1 | 0.13 | 0.09 | **468.2** (475.5 / 476.2) | 1 |
| Show selection toggle | 0.03 | 450.2 | 0.07 | 13.5 | 0.16 | 0.07 | 5.4 | 0.18 | 0.11 | **468.9** (474.7 / 474.9) | 1 |
| Template switch | 0.04 | 450.2 | 0.07 | 13.8 | 0.12 | 0.06 | 4.4 | 0.17 | 0.10 | **468.9** (475.2 / 476.7) | 1 |
| Date switch (instant fake load) | 0.79 | 450.2 | 0.08 | 14.8 | 0.13 | 0.05 | 4.8 | 0.17 | 0.02 | **472.3** (534.7 / 534.7) | 1 |
| Typing burst, 15 keys @110 ms, from **last** key | — | — | — | 15.3 | — | — | — | — | — | **473.2** (478.2) | 1 |
| 5 checkbox toggles 60 ms apart, from last | — | — | — | — | — | — | — | — | — | **469.6** (482.9) | 1 |
| Section switch + keystroke | 0.03 | 450.2 | 0.07 | 13.9 | 0.16 | 0.06 | 6.2 | 0.19 | 0.12 | **470.8** (476.6 / 487.3) | 1 |
| 80 notes: Daily Feature keystroke | 0.05 | 450.3 | 0.12 | 45.4 | 0.15 | 0.07 | 6.7 | 0.14 | 0.09 | **503.4** (509.3 / 510.8) | 1 |
| 80 notes: checkbox | 0.03 | 450.2 | 0.08 | 38.8 | 0.12 | 0.06 | 5.9 | 0.12 | 0.09 | **496.1** (512.0 / 515.3) | 1 |

Section navigation in the view model costs ≈ 0 ms and does not touch the preview (confirmed: no regeneration).

**Cold** (first preview after the view model is created, new process): **722 ms** = instant fake load +
450 ms debounce + generation 131 ms + open 2.4 ms + render 4.6 ms. Cold split over 5 processes:
`QuestPDF.Settings` static constructor ~3 ms; **Lato + STIX font registration 48–66 ms**; first generation
~135 ms; second generation 7.5 ms. PDFium's first-ever open in a new process: 306 ms once (OS library
loading), then ~2 ms.

**Where the time goes (450 ms setting):** 96 % debounce · 3 % QuestPDF · 1 % PDFium render · < 0.5 %
everything else. Fingerprinting (SHA-256 + regex over ~70 KB, on the UI thread) costs ≤ 0.4 ms and does not matter.

### 5.3 Other latency and behaviour sources in the path [Code]

| # | Source | Effect |
|---|---|---|
| a | `DefaultDebounce = 450 ms` applies to **every** change kind (`PassdownPdfPreviewViewModel.cs:86`, `ScheduleRefresh :736`) | The dominant delay, even for single discrete clicks |
| b | Workspace replacement (date load, Check Online, official reload, notes, ERT) goes through `OnWorkspaceChanged → NotifyContentChanged` (`AssociateViewModel.cs:1048-1057`), so it is **debounced** too. Only Show Preview uses `RefreshNowAsync`. | Date switch and first preview wait an extra 450 ms |
| c | The "Updating…" chip turns on at the **start** of the debounce (`ScheduleRefresh :741-745`) and stays for the whole wait. It has `LiveSetting="Polite"` (`PassdownPdfPreviewView.axaml:175-181`). | A visible flicker on every edit makes the wait feel longer; Narrator may announce "Updating…" repeatedly while typing [Hypothesis — Windows Narrator check] |
| d | Generation is not cancellable: the export service takes a `CancellationToken`, but the view model passes none (`:815`) | A superseded generation runs to completion (≤ 50 ms) and its result is discarded. Harmless at 450 ms; matters only with a 0 ms debounce. |
| e | Every render allocates a new managed `byte[]` (4.1 MB at 150 %), then copies it row by row into a new `WriteableBitmap` (`PdfiumPdfRasterizer.cs:65-66`, `IPdfPageImageFactory.cs:25-31`) | Large-object-heap churn (§10). The copy itself is 0.1–0.3 ms. |
| f | Only `SizeChanged` reports the viewport (`PassdownPdfPreviewView.axaml.cs:19`). A DPI change with no size change (dragging the window to a monitor with different scaling) never re-renders. | A blurry or oversharp page until the next resize or edit [Hypothesis — Windows multi-monitor check] |
| g | The preview renders even when the Associate tab is not visible: `ShouldAutoRender` depends on preference and layout, not on the selected top-level section (`:307`) | One background generation at startup and after each data reload, while the user is on another tab. This doubles as useful warm-up (§6). |

### 5.4 Refresh strategies [Measured where stated]

Same harness, 25 reps each (busy fixture unless noted). "Gens" = PDF generations per user action.

| Strategy | Discrete change → visible | After typing pause | Gens per 15-key burst | Gens per 5 rapid toggles | CPU (bg thread) | UI-thread impact | Complexity / race risk |
|---|---|---|---|---|---|---|---|
| **A** 450 ms trailing (today) | 468–472 ms | 473 ms | 1 | 1 | lowest | none | none (shipping) |
| **B** 200 ms trailing | **219–225 ms** [Measured] | **226 ms** [Measured] | 1 at 110 ms keys [Measured]; 1 per key if keys are > 200 ms apart | 1 | ≈ A; ≤ 15 ms per slow keystroke | none | trivial (one constant) |
| **C** 0 ms for discrete, text debounced | **16–26 ms** busy, 37 ms long [Measured at 0 ms] | per B | 1 | **5** [Measured] | ~13 ms per toggle | none (work stays on the thread pool) | needs each call site classified as "text" vs "discrete" (ScheduleAutosave is one shared funnel today, `AssociateViewModel.cs:2543`) |
| **D** Adaptive (shorter for small changes or light docs) | ≈ C/B | 150–450 ms | 1–2 | 1–5 | ≈ B | none | most tuning; heuristics; harder to test |
| **E** Skip when the PDF is unaffected | unchanged latency | unchanged | — | — | saves ≤ 15 ms only on no-op edits | none | must compare document models; low payoff (generation is already cheap); the fingerprint already prevents flicker |
| **Recommended: leading edge + 200 ms trailing** (a form of D that needs no change classification) | **~20–40 ms** (first change after idle renders at once; ≈ 0 ms row) | **~220–260 ms** | **2** (first key + final) | **2** | ≤ 2 × 15 ms per burst | none | small: one timestamp in `ScheduleRefresh`; existing generation tokens already discard stale results |

**Recommendation.** If no change happened in the last ~200 ms, generate immediately. Otherwise restart a
200 ms trailing timer. Also refresh immediately when the workspace is replaced (date load). Expected effect,
estimated from the measured 0 ms and 200 ms runs: checkboxes, products, shows, templates and date switches
all drop from **~470 ms to ~20–40 ms**, and long documents from ~500 ms to ~40–55 ms. Text shows its first
keystroke at once and settles ~220 ms after the user stops typing. CPU cost goes up by at most one extra
generation per burst (~15 ms off the UI thread). Typing responsiveness is unchanged, because no work is
added to the UI thread. Optionally, pass a cancellation token into `GeneratePdf` so a generation that gets
superseded stops early. That matters only if the trailing window is ever set below ~50 ms.

Why not Strategy C: it reaches the same discrete latency but forces every one of ~30 `ScheduleAutosave`
call sites to be labelled "text" or "discrete", which is easy to get wrong in future fields. The leading-edge
approach changes one class and keeps the "one funnel" design from Phase 33.

---

## 6. Startup performance

| Phase | Evidence |
|---|---|
| Migration check on an up-to-date database | **0.02 ms** median [Measured, scratch DB]. Fresh 0→18 migration: 645 ms (first run only). |
| Pooled connection open + `SELECT 1` | 0.009 ms [Measured] |
| Real services + `MainWindow.Opened` (headless, existing DB) | **192 ms**; fresh DB 803 ms [Measured: `RealWindowStartupTimingTests`, 1 sample each, JIT shared between the two] |
| First PDF (font registration + JIT) | ~186 ms on a background thread [Measured] |
| Window appears → controls respond | Not measurable headless; needs Windows (checklist §A) |

Code-supported observations:

- **Eager construction of every section** [Code]: `MainViewModel` resolves all seven section view models
  (`App.axaml.cs:795`, `MainViewModel.cs` constructor). Dashboard, Calendar, Notes, History and Settings each
  start a database load in their constructors. Associate starts a date load, template-preference load,
  deals load and important-details load, then the preview (450 ms later). All six views are built
  (`MainWindow.axaml:110-126`). Most of this work runs on the UI thread at startup because SQLite completes
  synchronously (§8). Each load is cheap, but together they run in the first second while the user is
  looking at the Dashboard. **[Hypothesis]** On a slow Windows disk with antivirus scanning, this is the
  likeliest cause of a sluggish first second. Measure first (checklist §A–B) before restructuring.
- Migrations run synchronously on the UI thread before the window exists (`App.axaml.cs:127`). That is
  correct by design (the schema must be ready) and costs ~0 ms when nothing needs migrating.
- Background work is already deferred properly: sync, refresh, backfill and update checks (90 s) all start
  after the window is assigned.
- **Prewarming opportunity:** a single throwaway QuestPDF generation on a background thread after
  `MainWindow.Opened` would move the ~186 ms cold cost out of the first preview, Create PDF or Print.
  Today the hidden preview generation (§5.3 g) happens to do this ~450 ms after load, but only while the
  preview preference is on.

---

## 7. Navigation and UI responsiveness

- Top-level navigation flips `IsVisible` on views that already exist [Code]. No view model or view is rebuilt,
  so navigation is cheap by design. Associate section switching costs ≈ 0 ms and does not touch the
  preview [Measured].
- Lists use `ItemsControl` without virtualization, but every list is small: ≤ ~40 product rows, a 6×7
  calendar grid, show rows, notes. History and Notes use `ListBox`. **No virtualization finding.**
- Product sections are rebuilt as new `ProductRowViewModel`s on every workspace load
  (`AssociateViewModel.cs:2450-2473`). That is fine at current sizes.
- The only timer is the one-minute Dashboard show tick, which starts and stops with the view
  (`DashboardView.axaml.cs:12-28`). There is no polling anywhere.
- The UI-thread cost of navigation, Price History and Calendar is mostly the synchronous SQLite work in §8.
  For example, each Price History product selection loads the whole synced-observation table (§8).

---

## 8. SQLite performance

| Query (synthetic DB) | Plan | Time |
|---|---|---|
| `dated_results WHERE requested_date = ? ORDER BY category` (Associate load) | SEARCH via `ix_dated_results_requested_date` + small temp sort | 0.04–0.1 ms |
| `SELECT DISTINCT product_key FROM season_price_history` (every Associate load) | covering index scan | 0.3 ms (10k rows) |
| `dated_results WHERE category=? AND requested_date BETWEEN` (calendar) | PK range search | 0.04 ms |
| `synced_price_observations ORDER BY product_key, first_observed_at_utc, observation_id` (`GetAllAsync`) | index scan + temp B-tree for 2 sort terms | **14 ms SQL, 21–23 ms including mapping** (20k rows) |

Findings:

- **[Measured + Code] "Async" repository calls run synchronously on the calling thread.** In the probe, the
  tasks returned by `GetByRequestedDateAsync`, `GetTrackedProductKeysAsync` and `GetAllAsync` had
  `IsCompleted == true` immediately on return. Microsoft.Data.Sqlite has no real async I/O, and no repository
  or service offloads work (0 `Task.Run` in `Storage/`). So every load, compose and save triggered from a
  view model runs on the UI thread, including JSON deserialization of payloads in `ComposeAsync`
  (`AssociatePassdownService.cs:406-500`, about 10–15 queries per date load). Cheap today; the risk is
  variance on Windows (cold cache, antivirus, a background refresh holding the write lock under the rollback
  journal with its 5 s busy timeout, `SqliteConnectionFactory.cs:7`).
- **Price History reads the whole synced table for each product** (`SeasonPriceHistoryService.cs:164-171`,
  `208-243`). This is deliberate: the legacy Season-Pass identity fold needs every key (RC.9 defect fix).
  Cost grows linearly with the number of synced observations. Recommendation: cache the folded map while the
  overlay is open, or compute it off the UI thread. Do not narrow the query; that would re-break the fold.
- Indexes are adequate for every hot query examined. **No new index is recommended.**
- No N+1 problem of any significance. `ComposeAsync` loops once per catalog category (a fixed small count).
  Each repository call opens its own pooled connection, which costs 0.009 ms.
- Journal mode is the default rollback journal (`DELETE`); WAL is not enabled anywhere. **[Hypothesis]** WAL
  would let UI reads continue while a background refresh writes. It must first be evaluated against backups
  (`SqliteDatabaseBackup.cs:91` already forces `DELETE` on backup copies), the updater and the
  CLAUDE.md invariant: the live database and any WAL/SHM files must stay in `%LOCALAPPDATA%`, never in the
  sync folder. Only worth doing if Windows measurements show lock waits.

---

## 9. Official-source refresh

- Concurrency is bounded at 3 (`RefreshOrchestratorOptions.MaxConcurrency`). There is per-operation error
  isolation, the HTTP timeout is distinguishable from cancellation, and last-known-good is preserved (failed
  results are never cached).
- `CoalescingRetrievalHttpClient` merges identical **GET** requests (park hours + shows on the FIMS endpoint)
  and reuses them for 2 minutes. **POST requests pass straight through** (`CoalescingRetrievalHttpClient.cs:58`).
- **[Code] Duplicate POSTs.** `DatedAdmissionSource` builds its body from `today … max(requested, today+120)`
  (`DatedAdmissionSource.cs:74-80`). For every requested date inside the 120-day padding, the body is
  **byte-identical**. A scheduled rolling-window run covers 7 dates (`RollingDatePlanner.DefaultWindowLengthDays
  = 7`), so it sends the same admission POST 7 times. The Fast Lane, Haunted, Bring-a-Friend (+Duo) and
  Military sources send the same identical "horizon" POST for each date that has no product
  (`DatedFastLaneSource.cs:129-132` etc.). On closed or gap dates that is up to one redundant POST per date
  per source. Recommendation: let the coalescer also merge **POSTs with the same endpoint + body** inside
  the same short reuse window. Each source still interprets the shared response for its own date, so
  freshness, verification and last-known-good semantics are unchanged. Expected benefit: fewer official
  requests and a shorter refresh, which is background work, not user-visible. Not live-measured (live tests
  are opt-in).
- No refresh work happens when switching screens. The Associate view reloads only when data actually
  changed (`DataChangeNotifier`) and defers the reload while edits are unsaved.
- When a reload lands while the Associate tab is hidden, it still regenerates the preview (§5.3 g). That is
  harmless and cheap.

---

## 10. Memory, CPU and resources

Soak test, 80-note fixture, 600 preview updates (2/3 checkbox toggles, 1/3 template switches), forced
full GC before each sample [Measured]:

| Run | Managed after first preview → after 600 | Working set | Gen-2 GCs per update |
|---|---|---|---|
| Autosave active (fake service) | 5.1 → 15.4 MB, linear | 145 → 160 MB | ~0.6 |
| **Autosave suppressed (control)** | **5.1 → 5.3 MB, flat** | 146 → **129.5 MB, flat** | ~0.65 |

The linear growth in the first run is entirely the test fake's `SaveCalls` log (198 saved drafts retained),
not the application. **The preview pipeline does not leak managed memory.** Native memory (PDFium documents,
bitmaps) cannot be counted on macOS (`HandleCount`/`PrivateMemorySize` read 0). The code releases exactly
one page image and one PDFium document on replacement, hide, error and date change (`ReleaseDisplay :1023`,
`DisplayAsync :929-939`), and every reviewed path disposes them.

Findings:

- **LOH churn** [Measured + Code]: the 4.1 MB `PdfRasterImage.Bgra` array per render (§5.3 e) triggers
  ~0.65 gen-2 collections per preview update. That is cheap with a 5 MB heap, but full collections get more
  expensive as the heap grows on a long-running Windows session. Fix: render PDFium directly into the
  `WriteableBitmap` buffer (`FPDFBitmap_CreateEx` with an external buffer). This removes one allocation and
  one copy.
- **Printing** [Code]: `RenderBand` calls `FPDF_LoadPage` once per band, and each band allocates a ≤ 16 MB
  BGRA array plus a ~12 MB BGR24 array (`PdfiumPdfRasterizer.cs:28-47`, `WindowsPdfPrintService.cs` DrawPage).
  A 600 DPI Letter page takes ~9 bands, so ~250 MB of short-lived large arrays per page. The process-wide
  PDFium lock is held during each band render, so a preview render waits while printing. Fix: `ArrayPool`
  buffers and loading the page once per page. Priority P3. Windows measurement needed (checklist §H).
- **Native handle safety** [Code]: `PdfiumRasterDocument` owns `AllocHGlobal` memory and a document handle
  without a `SafeHandle` or finalizer. It leaks only if a future caller forgets `Dispose`. P3 hardening.
- Timers and subscriptions: one view-scoped timer. 77 `+=` and 14 `-=` in Desktop, but subscribers are
  long-lived singletons or view models created once (`MainViewModel` is resolved once). No growing
  subscriber lists were found. `ProductRowViewModel`s use callbacks, not events.
- Background tasks: sync, refresh (every 4 h), backfill (lowest priority) and update check (90 s delay). No
  idle CPU loop exists in code. Idle CPU on Windows is still to be confirmed (checklist §J).

---

## 11. Dead and obsolete code

Method: type-level scan of all 1,014 declared types (references outside the declaring file across src, tests
and `.axaml`), plus a public-method scan, then manual checks for DI, XAML, reflection and serialization.

| Candidate | Evidence | Classification | Confidence |
|---|---|---|---|
| `OfficialPassPerkHistory.FirstSeenAtOfCurrentTerms` (`Application/PassPerks/PassPerksDocuments.cs:132-140`) | 0 references in src/tests/xaml | **Confirmed unused** | High |
| `OperatingEvents.LatestOccurrence` (`Domain/Seasons/OperatingEvents.cs:181`) | 0 references | **Confirmed unused** | High |
| `SyncEntityState.IsDirty` (`Application/PersonalSync/SyncEntityState.cs:81`) | 0 references | **Likely unused** (sync domain: confirm no reflection or serialization use) | Medium-high |
| `SourceState.RecordSuccess/RecordFailure` (`Domain/Retrieval/SourceState.cs:37-41`) | tests only; the orchestrator builds `SourceState` directly | Likely unused in production | Medium |
| `MathParagraphRenderer.ClearCache`, `SyncEntityIdentity.TryParseFinalizedPassdownId` | tests only | Test seams. Keep, or mark `internal` + `InternalsVisibleTo` | Medium |
| `Desktop/Assets/logo.png` (1254², 841 KB) | Embedded via `AvaloniaResource Include="Assets\**"`; no reference in code, XAML, packaging or tests (only `logo.ico` is used) | **Likely unused embedded asset**. Keep the file as the icon's source art, but exclude it from the resource glob | Medium-high |
| `IManagerDraftRepository`, `SqliteManagerDraftRepository`, `ManagerDraft`, `Migration0007` | Not registered in DI; tests only | **Required for compatibility / deferred scope**. The `manager_drafts` table exists in every database; Manager is deferred, not cancelled | — |
| `IJobBrowserSessionFactory` / `UnavailableJobBrowserSession(Factory)` | Playwright was removed in Phase 7B; the orchestrator still calls `EnsureStartedAsync` (`RefreshOrchestrator.cs:334`) | **Moderate-risk simplification** (a dormant compatibility layer, ~18 references) | Medium |
| Morning Message Builder / rich clipboard | `src/` has none. `AvaloniaClipboardService` is plain-text only. Remnants are compatibility tests (`MorningMessageRetiredTests`, `RetiredMorningMessageCompatibilityTests`) and the release-script guard | **Required for compatibility — do not remove** | High |
| Historical migrations 0001–0017 | Needed to upgrade older databases | **Do not remove** | High |

Overall the scan found very little dead code. Most single-file types are private helpers (JSON shapes,
P/Invoke structs, parser results).

---

## 12. Duplication

| Area | Finding | Harmful? |
|---|---|---|
| Accesso dated sources | 6 files, 2,142 lines (`DatedAdmission/FastLane/Haunted/BringAFriend/BringAFriendDuo/MilitaryFirstResponderSource.cs`). `Truncate`, `Error`, `Unavailable`, `NotPublished` ×6; `CallCalendarAsync` + `CalendarCallResult` ×5 (differing only in payload type) | **Yes, moderately.** A fix to error handling or horizon logic must be made five or six times. Consolidate into one generic Accesso calendar helper; keep the per-source product mapping separate. |
| Preference stores | 5 `File*PreferenceStore` classes, all on the shared `AtomicFileWriter` | No. Thin and deliberate. |
| PDF pipeline | One export service shared by preview, Print, Save PDF, Create PDF and finalization (verified) | No. Good. |
| File-lock HResult handling | `0x80070070/0x80070027` checks repeated in 4 updater/backup files | Minor. A small helper would do. |
| Date/time | `EasternTimeZone` used consistently | No |

---

## 13. Architecture

- **Oversized:** `AssociateViewModel.cs` 2,904 lines, `AssociateView.axaml` 1,796, `App.axaml.cs` 1,019,
  `PassdownPdfPreviewViewModel.cs` 1,102. The Associate view model mixes ERT editing, operational-note picking,
  products and custom products, shows, the autosave state machine, Copy for Teams and preview wiring. Split
  it gradually: extract the ERT editor and the operational-note picker first (self-contained state and
  commands). **No rewrite recommended.**
- State ownership is clear: the Associate view model owns the draft fields, the workspace is immutable, and
  the preview reads a captured snapshot. Clean layering and no cycles.
- **UI-thread data access** (§8) is the one structural issue worth fixing incrementally, at the view-model
  boundary.

---

## 14. Dependencies and packaging (RC.12 ZIP, read-only inspection)

307 files, 177.3 MB uncompressed, 81.0 MB zipped. Largest contributors:

| Item | Size (uncompressed) | Note |
|---|---|---|
| .NET runtime (self-contained, untrimmed) | ~70 MB | Expected. Trimming is risky with reflection, XAML and JSON: not recommended. |
| `Carowinds.Passdown.Documents.dll` | **13.8 MB** | **11.9 MB is 19 embedded Lato files** (`Documents.csproj:29-32`); documents use only Regular, Bold, Italic and SemiBold (`.Bold()` ×28, `.Italic()` ×7, `.SemiBold()` ×3). Plus STIX Two Math 1.5 MB. |
| Skia (Avalonia + QuestPDF) | libSkiaSharp 11.6 + QuestPdfSkia 8.2 MB | Two separate Skia builds; required by each library |
| `pdfium.dll` | 7.8 MB | Phase 33; required |
| ANGLE `av_libglesv2.dll` | 5.4 MB | Avalonia GPU on Windows; required |
| `qpdf.dll` | 4.4 MB | QuestPDF dependency |
| **Non-Windows Avalonia platforms** | **4.63 MB**: `Avalonia.X11`, `Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.Native` (macOS), `Tmds.DBus.Protocol` | Pulled in by the `Avalonia.Desktop` meta-package; never loaded on Windows |
| Runtime debug components | ~6.2 MB: `mscordaccore` ×2 (1.36 MB each, duplicate names), `mscordbi`, `createdump.exe`, `Microsoft.DiaSymReader.Native.amd64` | Part of the runtime pack; needed for crash dumps and stack traces. **Keep.** |

Safe opportunities (verify with `release-content-rules.json` and the RealReleaseZip test):

1. Embed only the Lato faces actually used: −8 to −10 MB uncompressed, and about −35 ms off the first-PDF
   cold start (estimated in proportion to the measured 50 ms registration). **Requirement:** the PDF content
   fingerprint must be byte-identical for every template before and after; QuestPDF must not choose a
   different face.
2. Windows-only Avalonia references for the win-x64 publish: −4.6 MB. Moderate: dev and test hosts need the
   macOS/Linux backends, so the references must be RID-conditional.
3. Drop `logo.png` from the embedded resources: −0.84 MB inside `Desktop.dll`.

No unused NuGet package was found. Playwright is already gone. License files: 43 present, including the
17 PDFium dependency notices. Do not remove any.

---

## 15. Reliability and data safety

No P0 or P1 defects found. Reviewed, without changes:

- **Atomic writes** (`PersonalSync/AtomicFileWriter.cs:64-99`): temp file → `Flush(flushToDisk: true)` →
  `File.Move(overwrite)`, temp removed on failure, stale-temp cleanup exists. Used by all preference, sync,
  device-id and update-state files. Correct.
- **[Hypothesis, P2] Transient lock on the sync folder:** `File.Move` into the Personal Sync folder
  (typically OneDrive) has no retry for a brief sharing violation caused by antivirus or the OneDrive
  client. Updater and backup code do retry these HResults; sync writes do not. Failure is safe: the
  original stays intact, the temp file is deleted and the next sync cycle retries. Only the "sync failed"
  status and the delay are user-visible. Validate on Windows with OneDrive active (checklist §K).
- **Migrations:** gated by `PRAGMA user_version`, newer-than-known schemas are refused, a verified backup is
  made before migrating, and `integrity_check` runs after. **Keep all 18 migrations.**
- **Drafts:** conditional save against the loaded hash (sync conflict = explicit conflict, never an
  overwrite). The shutdown flush and Phase 23A failure handling are intact. Preview capture never mutates
  the draft (verified: autosave suppression, `AssociateViewModel.cs:2621-2633`).
- **Official cache:** failures never overwrite last-known-good. Coalescing caches successes only.
- **Updater:** ECDSA-P256 signed manifest, configured trusted key id `carowinds-release-1` (rotation is
  documented and tested, `docs/phase23d-github-updater.md:127`), whole-ZIP and per-file SHA-256, safe ZIP
  extraction, downgrade refused (`UpdateDiscoveryService.cs:54` + installer checks). Release immutability:
  RC.10–RC.12 artifacts are read-only and guarded by `Build-PortableRelease.ps1`. No private key material
  was accessed.

---

## 16. Test suite

| Observation | Evidence | Opportunity |
|---|---|---|
| Fast overall: ~37 s of runner time for 5,347 tests (excluding build) | per-project timings: Domain 1 s, Application 2 s, Documents 4 s, Infrastructure 20 s, Ui mixed 9 s, run-alone ~40 s (Phase15E 22 s, SpellCheck 11 s) | — |
| Infrastructure: one test takes **5.2 s** waiting for the SQLite busy timeout (`UpdateInstallerDatabaseSafetyTests.DatabaseStateUnreadableAfterAFailedStart…`), with `parallelizeTestCollections: false` | trx timings | Inject a short busy timeout in that test: about −5 s |
| Six run-alone real-`Window` classes (Avalonia headless thread-affinity flake) | Mixing them is not safe: a mis-partitioned run during this audit produced 148 failures | Investigate a per-class headless session, so the partition stops growing with every phase |
| No preview-latency or memory regression test | — | Add count-based deterministic tests (generations per action, "first change after idle renders without waiting") plus an **opt-in** timing harness like this audit's |
| `RealWindowStartupTimingTests`: 1 sample, no threshold, stale name "ExistingSchema11" (it pre-migrates to the latest) | `RealWindowStartupTimingTests.cs:164,185` | Rename; take ≥ 5 samples |
| Timing-based waits | `Task.Delay` in ~12 test files (sync coordinator, backfill, orchestrator) | Check for fragility; prefer the `ManualDebounceScheduler`-style seams already used |
| Live tests | `Category=LiveSmoke` excluded from the deterministic run | Correct |

---

## 17. Prioritized findings

| ID | Priority | Area | Finding | Evidence | Expected Benefit | Risk | Recommended Action |
|---|---|---|---|---|---|---|---|
| F01 | **P1** | Preview | A fixed 450 ms trailing debounce delays every change kind; 96 % of latency | [Measured] §5.2: 468–472 ms median; `PassdownPdfPreviewViewModel.cs:86,736-767` | Discrete changes ~470 → ~20–40 ms; text ~470 → ~220 ms after pause | Low (tokens already drop stale results) | Leading-edge render + 200 ms trailing debounce (§5.4) |
| F02 | **P1** | Preview | Date load and workspace replacement are debounced; first preview at startup waits too | [Measured] date switch 472 ms; cold first 722 ms; `AssociateViewModel.cs:1048-1057` | −450 ms on every date switch, Check Online result and first preview | Low | Covered by F01; or call `RefreshNow` on workspace replacement |
| F03 | P2 | Preview UX | "Updating…" chip shows for the whole debounce on every edit; live region may chatter in Narrator | [Code] `:741-745`, `PassdownPdfPreviewView.axaml:175-181` | Less flicker, calmer screen reader | Low | Show the chip only while generation runs longer than ~150 ms |
| F04 | P2 | Startup / PDF | First PDF ~186 ms cold; 48–66 ms is registering 19 Lato faces | [Measured] §5.2 cold split; `AssociatePassdownPdfGenerator.cs:30-35,66-79` | −186 ms on first preview, Create PDF and Print | Low | Background prewarm generation after `MainWindow.Opened` |
| F05 | P2 | Packaging / startup | 11.9 MB of Lato embedded; only ~4 faces used | [Code] §14 | −8–10 MB package; ~−35 ms cold (estimate) | Medium (font selection must stay byte-identical) | Embed used faces only; require identical fingerprints for all templates |
| F06 | P2 | SQLite / UI thread | Repository "async" methods complete synchronously, so DB work runs on the UI thread | [Measured] `IsCompleted==true`; 0 `Task.Run` in Storage | Removes the main source of UI hitches under slow disk or lock waits | Medium (threading of view-model state) | Offload heavy service calls (Associate compose, Price History, Calendar month) at the view-model boundary, guided by Windows measurement |
| F07 | P2 | Refresh | Identical admission/horizon POSTs repeated per date; POSTs bypass the coalescer | [Code] `DatedAdmissionSource.cs:74-80`, `CoalescingRetrievalHttpClient.cs:58` | Up to 6 fewer admission requests per scheduled run, plus horizon duplicates | Low–medium (retrieval semantics) | Coalesce POSTs on endpoint + body within the existing 2-minute window |
| F08 | P2 | Reliability | No retry on transient sharing violation for sync-folder renames | [Hypothesis] `AtomicFileWriter.cs:91` vs updater retries | Fewer spurious sync failures with OneDrive or antivirus | Low | Short bounded retry on 0x20/0x21 HResults; validate on Windows |
| F09 | P2 | Maintainability | Six dated sources duplicate ~5 helpers | [Code] §12 | One place to fix Accesso handling | Medium | Shared generic helper; keep per-source mapping |
| F10 | P2 | Maintainability | `AssociateViewModel` 2,904 lines | [Code] | Easier changes, smaller test surface | Medium | Extract ERT editor and note picker first |
| F11 | P3 | Memory | 4 MB LOH array per render; ~0.65 gen-2 GC per update | [Measured] §10 | Fewer full GCs; one copy fewer | Low | Render into the `WriteableBitmap` buffer via `FPDFBitmap_CreateEx` |
| F12 | P3 | Printing | Page reloaded per band; ~28 MB of new arrays per band; PDFium lock blocks preview during spooling | [Code] `PdfiumPdfRasterizer.cs:28-47` | Lower print memory; preview stays live while printing | Low | `ArrayPool` + load the page once per page; measure on Windows |
| F13 | P3 | Preview | DPI change across monitors does not trigger a re-render | [Hypothesis] `PassdownPdfPreviewView.axaml.cs:19` | Sharp page after moving monitors | Low | Also report on `TopLevel.ScalingChanged` |
| F14 | P3 | Preview | Superseded generations not cancelled | [Code] `:815` | Saves ≤ 50 ms of background CPU per superseded generation | Low | Pass a token if the trailing window drops below ~50 ms |
| F15 | P3 | Price History | Each product selection loads the entire synced table (deliberate for identity folding) | [Measured] 21 ms at 20k rows; `SeasonPriceHistoryService.cs:164-243` | Smooth selection as history grows | Low | Cache the folded map while the overlay is open; off the UI thread |
| F16 | P3 | Startup | Every section view model and view is built and loaded at startup | [Code] §6 | Possibly faster first second on Windows | Medium | Measure first (checklist); defer hidden-section loads only if justified |
| F17 | P3 | Packaging | 4.63 MB of Linux/macOS Avalonia backends in the win-x64 ZIP | [Code] §14 | −4.6 MB | Medium (RID-conditional refs) | Win-x64-only platform references |
| F18 | P3 | Dead code | `FirstSeenAtOfCurrentTerms`, `LatestOccurrence` unused; `IsDirty` likely unused | [Code] §11 | Less noise | Low | Remove in 34C after a final reference check |
| F19 | P3 | Assets | `logo.png` embedded but unreferenced | [Code] §11 | −0.84 MB | Low | Exclude from the `AvaloniaResource` glob; keep the source file |
| F20 | P3 | SQLite | Rollback journal; background writes can block UI reads up to the busy timeout | [Hypothesis] §8 | Smoother UI during refresh | Medium (backup, sync and updater interplay) | Evaluate WAL only if Windows shows lock waits |
| F21 | P3 | Native safety | PDFium document has no `SafeHandle`/finalizer | [Code] `PdfiumPdfRasterizer.cs:75-195` | Defense in depth | Low | `SafeHandle` wrapper |
| F22 | P3 | Tests | 5.2 s busy-timeout test; no perf regression coverage; growing run-alone partition | [Measured] §16 | −5 s; latency regressions caught | Low | See §16 |
| F23 | P3 | Simplification | Dormant `IJobBrowserSession` layer after Playwright removal | [Code] | Simpler orchestrator | Medium | Remove only alongside a refresh-orchestrator test pass |

---

## 18. Proposed Phase 34B — performance (not started)

Each step is independent, small and reversible (one revert per step). Run the full deterministic partition
plus `dotnet build -c Release` after each.

1. **Preview refresh: leading edge + 200 ms trailing** (F01, F02, F14 optional)
   - Area: `PassdownPdfPreviewViewModel.ScheduleRefresh/DebouncedRefreshAsync`. Track the time of the last
     change; if idle for ≥ 200 ms, `RefreshNowAsync` immediately, otherwise restart a 200 ms trailing delay.
     Keep `DefaultDebounce` as the trailing window, injectable as today.
   - Baseline: 468–472 ms (busy), 496–503 ms (long). Target on macOS harness: discrete ≤ 60 ms median,
     text ≤ 260 ms after pause, ≤ 2 generations per 15-key burst.
   - Tests: update the `Phase33PdfPreviewViewModelTests` debounce expectations with `ManualDebounceScheduler`
     plus a fake clock. New: a first change after idle generates without elapsing the delay; a burst gives
     exactly 2 generations; a date change still never shows another date's page; stale-token discard under a
     leading render overlapped by a trailing render.
   - Windows: checklist §C–E (checkbox, typing, long note). Rollback: restore the single trailing debounce.
2. **"Updating…" chip only during real work** (F03). Show it when generation has been running for
   > 150 ms. Tests: markup and view-model state. Windows: Narrator check (§C). Rollback: one-line revert.
3. **Prewarm QuestPDF after the window opens** (F04). One background `GeneratePdf` on a synthetic minimal
   workspace, result discarded, never displayed or saved. Baseline: first generation ~186 ms. Target:
   first real preview generation ≤ 30 ms. Tests: prewarm never touches the draft, preview or files. Windows:
   §A cold start (confirm no visible start-up regression).
4. **Offload heavy view-model data loads** (F06, F15). Start with Associate `LoadAsync/ComposeAsync` and
   Price History `GetHistoryAsync` via `Task.Run` at the view-model boundary; continuations already marshal
   back with `ConfigureAwait(true)`. Baseline: currently on the UI thread (to be timed on Windows, §F).
   Tests: existing load, flush and date-switch race tests must stay green; add one asserting the service is
   invoked off the UI thread. Rollback per call site.
5. **Coalesce identical POSTs** (F07). Extend `CoalescingRetrievalHttpClient` with key (endpoint, body
   hash); successes only; same 2-minute window. Tests: two dated sources and two dates in one job → one POST;
   failures never reused; different bodies never merged. Windows: none (logic only). Rollback: remove the
   POST path.
6. **Render into the bitmap buffer** (F11). Target: 0 LOH allocations per preview render. Tests: real
   PDFium rendering tests (pixel and ink equality with the current path). Windows: §G memory after
   repeated updates.

Out of 34B unless Windows data demands it: WAL (F20), deferred section loading (F16), print banding (F12).

---

## 19. Proposed Phase 34C — cleanup (not started)

**Safe, high-confidence**
- Remove `OfficialPassPerkHistory.FirstSeenAtOfCurrentTerms` and `OperatingEvents.LatestOccurrence`.
- Exclude `Assets/logo.png` from `AvaloniaResource` (keep it in the repo as the icon's source art).
- Rename `RealWindowStartupTimingTests.ColdStartToWindowOpened_ExistingSchema11Database`; shorten the 5 s
  busy-timeout test.

**Moderate-risk simplification**
- Consolidate the Accesso dated-source helpers (F09), following the `retrieval-source` skill and with every
  source's fixture tests unchanged.
- Embed only the Lato faces in use (F05), gated on identical PDF fingerprints for all templates.
- RID-conditional Avalonia platform references for win-x64 (F17), gated on the release-content rules and
  the RealReleaseZip test.
- Remove the dormant `IJobBrowserSession*` layer (F23).
- Confirm `SyncEntityState.IsDirty` and `SourceState.RecordSuccess/RecordFailure` and remove or internalize them.

**Compatibility-sensitive (keep unless a deliberate decision is made)**
- `ManagerDraft*` + `Migration0007` (deferred Manager scope; table exists in every database).
- Test-only seams (`ClearCache`, `TryParseFinalizedPassdownId`).

**Do not remove**
- Migrations 0001–0018; Morning Message compatibility tests and the release-script guard; legacy
  `morning-message*.json` handling; any license file; runtime debug components.

---

## 20. Windows-specific profiling gaps

Not measurable on the macOS host: real window cold and warm start; time to first interaction; GPU
compositor latency; Windows file-system and antivirus effect on synchronous SQLite; GDI handle stability
across Print dialogs; native PDFium memory; printing throughput and memory at 600 DPI; DPI changes across
monitors; Narrator behavior of the "Updating…" live region; OneDrive locking on sync writes; idle CPU. See
`docs/phase34a-windows-performance-checklist.md`.

## 21. Known uncertainties and limitations

- All timings are from macOS on Apple Silicon. Windows x64 laptops may be 1.5–3× slower for QuestPDF and
  PDFium work. The *ratio* (debounce ≫ work) is very likely to hold, because the debounce is fixed time.
- The harness measures up to the `PageImage` assignment; screen present adds ~1 frame.
- The fake service loads instantly, so the date-switch numbers exclude real database and compose time
  (§8 gives those separately).
- Strategy numbers for "leading edge + 200 ms" are derived from the measured 0 ms and 200 ms runs, not from
  an implementation (implementing it would violate audit-only scope).
- The synthetic database sizes are guesses at a "heavy" profile. Real profiles were deliberately not inspected.
- Duplicate-POST counts come from the code, not from live traffic.

## 22. Audit artifacts

Scratch-only (not committed; outside the repository): the benchmark harness, a DB probe, a synthetic
SQLite database and result logs, kept in the session scratchpad. Tracked changes made by this phase: this
report and `docs/phase34a-windows-performance-checklist.md` only (uncommitted).
