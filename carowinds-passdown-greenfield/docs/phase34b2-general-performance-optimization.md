# Phase 34B-2 — Application-wide performance optimization

Status: implemented locally (not pushed, not tagged, not released). RC.12 is published and frozen; anything here
ships as **RC.13 or later**. Source `<Version>` stays `0.1.0-rc.12`. Database schema 18, Personal Sync formats,
PDF templates, fonts and generated PDF content are unchanged. No dependency was added or updated.

Evidence labels: **[Measured]** = timed in this phase (environment in §5.1); **[Code]** = read from source;
**[Estimate]** = derived, not measured.

## 1. Executive summary

| # | Change | Result (Mac development host) | Kept? |
|---|---|---|---|
| 1 | **Background PDF engine warm-up** after the window opens (Part A) | First preview's PDF work **82.3 → 13.3 ms** median (−69 ms, −84 %), n = 9 fresh processes. Worst case (warm-up and first PDF start together) no slower than today. UI-thread cost 0.4 ms. | Yes |
| 2 | **Price History reader**: synced observations read and folded once per open, not once per product click (Part B) | Product switch at 20,000 synced observations **65.9 → 0.46 ms** (−99 %); at 2,000: **6.96 → 0.13 ms**. Opening at 20,000: ~148 → ~80 ms. | Yes |
| 3 | **Coalesce identical official POSTs** (Part C) | One 7-day rolling window through the admission and Fast Lane fixtures: **21 → 9 POSTs** (−57 %). Admission 7 → 1. | Yes |
| 4 | **Render the preview straight into the bitmap** (Part D) | Managed allocation per preview render **4,286 KB → 1.3 KB**; render 1.11 → 0.63 ms (busy page). Gen-2 collections barely change (see §9). Pixel-identical. | Yes |
| — | Associate / Calendar loads off the UI thread | Measured **0.25–0.78 ms** even with 3 years of data: nothing to gain | Deferred (§7) |
| — | Price History off the UI thread | Remaining one-time open cost small at realistic sizes; thread-affinity risk | Deferred (§7) |
| — | Printing memory (Part D §8) | 225 MB transient managed allocation per 600 DPI page, 62–97 ms render work per page | Investigated; fix proposed (§9.3) |
| — | Personal Sync transient-lock retry (Part E) | Hypothesis only; current failure mode is already safe | Deferred (§10) |
| — | Opt-in benchmarks (Part F) | `tools/perf-benchmarks` | Added |

All Mac results are from Apple Silicon. They do **not** establish Windows speed; the Windows checklist (§12) covers
that, and its results are recorded separately.

## 2. Baseline and audit references

| Item | Value |
|---|---|
| Repository / branch | git root `~/Documents/Passdown`, app in `carowinds-passdown-greenfield/`, branch `feature/passdown-redesign` |
| Starting HEAD | **`af9e53a`** (RC13-Phase34B1-LOCALTEST record), on top of Phase 34B-1 `87b265e` |
| Working tree at start | only pre-existing unrelated changes (`.DS_Store` files, staged `.claude/skills/pdf-design/SKILL.md`); left untouched and not committed |
| Version | `0.1.0-rc.12` / FileVersion `0.1.0.12` (not bumped) |
| Schema | 18 migrations (`Migration0001`…`Migration0018`); unchanged |
| Key packages | Avalonia 12.1.1, QuestPDF, bblanchon.PDFium 159.0.8138, Microsoft.Data.Sqlite 10.0.11; unchanged |
| Tests at start | 5,378 (Phase 34B-1 record) |
| Release build at start | 0 warnings, 0 errors |
| Read | `phase34a-repository-performance-audit.md`, `phase34a-windows-performance-checklist.md`, `phase34b1-preview-responsiveness.md`, `phase33-live-pdf-preview-printing.md`, `releases/rc12-release-readiness.md` |

Two audit statements were corrected by this phase's code reading:

- **Accesso POST bodies are not byte-identical** (audit §9). Every body carries a fresh `request_token`, a
  client-generated per-request nonce (`docs/retrieval/dated-admission-source.md`). Coalescing on the exact body
  would never have merged anything (§8).
- **The updater and backup do not retry sharing violations** (audit §15). The HResult checks there
  (`0x80070070`, `0x80070027`) detect a *full disk*. Nothing in the app retries a sharing violation today (§10).

## 3. Changes implemented

### 3.1 PDF engine warm-up (F04)

- `Desktop/Services/PdfEngineWarmUp.cs` (new). When the main window has opened and the UI goes idle
  (`Dispatcher.UIThread.Post(…, DispatcherPriority.ApplicationIdle)` from `Window.Opened`), `TryStart()` starts
  **one** background thread at **below-normal priority**. It generates one synthetic PDF
  (`Documents/Associate/AssociatePassdownPdfWarmUpDocument.cs`: every text is "Warm-up", date 2000-01-01, no
  operational data) in the user's saved template, then opens it with PDFium and renders one ~30 × 40 px page.
  The bytes are discarded.
- Nothing is displayed, saved, printed, synced or written. No draft is touched, so autosave never runs. The
  template preference is read through the existing store (one small local file read, never written).
- **At most once** per process (`Interlocked`). No timer, no polling.
- **No redundant work.** It skips entirely once a real PDF has *started* generating
  (`AssociatePassdownPdfGenerator.HasStartedRendering`). At startup that is usually the live preview's first
  page, which pays the cost itself. The PDFium step is skipped when PDFium is already loaded
  (`PdfiumPdfRasterizer.IsLibraryLoaded`).
- **Never blocks a real PDF beyond its own cost.** QuestPDF font registration is a CLR static constructor (runs
  once, thread-safe). Separate documents generate independently. The PDFium lock is held only for the library
  load and one tiny render.
- **Shutdown.** `ShutdownRequested` calls `Cancel()`, which skips any step not yet started. The thread is a
  background thread, so it can never keep the process alive. Failures are swallowed with a debug log; the next
  real PDF then simply pays the cold cost.

### 3.2 Price History reader (F06/F15)

- `ISeasonPriceHistoryService.OpenReaderAsync()` (new, default-implemented as a pass-through for other
  implementations). `SeasonPriceHistoryService`'s reader loads the synced observations once and applies the
  legacy Season Pass identity fold (`ImportedPassIdentityFolding`). It then answers the product list and every
  product's history from that folded copy. **This computer's own intervals are still read fresh for every
  product.** The merge logic (`PriceHistoryTimelineMerge`) is unchanged and is fed exactly the same inputs as
  before.
- `PriceHistoryViewModel` builds a new reader on every open and every in-place refresh, and drops it on close.
  Invalidation:
  - Synced observations change only through Personal Sync merges, which publish `DataChangeKind.OfficialPriceHistory`.
  - The Season Pass catalog used by the fold changes only through official refreshes, which publish
    `DataChangeKind.OfficialData`. The open screen now refreshes on that too, keeping selection, search, filter
    and "Include retired".
  - An older open finishing after a newer refresh is discarded (`_productsLoadVersion`).
  - A selection made while closed reads directly from the service.

### 3.3 POST request coalescing (F07)

- `CoalescingRetrievalHttpClient` now coalesces POSTs as well as GETs, under the existing rules: join in-flight
  requests, reuse **successful** responses for the existing 2-minute window, never cache failures or exceptions,
  and keep per-caller cancellation.
- The POST key is: method + absolute endpoint + **every header** (name case-insensitive, value exact,
  order-independent) + body. The body comparison form comes from `AccessoKeywordCalendarCoalescing.KeyBody`,
  which, **for the accesso calendar endpoint only**, removes `request_token` and nothing else. Keyword,
  display_zero_capacity, start and end dates and every other field must still match. Any other endpoint, or a
  body that is not a flat JSON object containing that nonce, is compared byte for byte.
- The six accesso dated sources (admission, Fast Lane, haunted, Bring-A-Friend, Bring-A-Friend Duo, Military /
  First Responder) now share the app's one coalescing client, alongside park hours and shows (`App.axaml.cs`).
  Each source still parses the shared response for its own requested date with its own timestamp, so
  classifications, freshness, last-known-good and partial-failure handling are unchanged. Bounded refresh
  concurrency (3) is unchanged.

### 3.4 Direct-to-bitmap preview rendering (F11)

- New optional `IPdfDirectRasterDocument` (Application) with `GetRenderPixelSize` and `RenderPageInto(…, IntPtr
  destination, int stride, …)`, implemented by PDFium via `FPDFBitmap_CreateEx` over caller-owned memory. The
  PDFium bitmap is created and destroyed inside the call, under the existing process-wide PDFium lock, after the
  same disposed check as `RenderPage`. Nothing refers to the memory after the call returns.
- `AvaloniaPdfPageImageFactory.Render` creates the `WriteableBitmap`, locks its framebuffer for exactly the
  duration of the render, and disposes the bitmap if rendering throws or is cancelled. Documents without direct
  rendering (test fakes) fall back to the old render-and-copy path (`IPdfPageImageFactory.Render` default).
- The preview view model calls `Render` in its two render sites. Its ownership, stale-token and disposal logic
  (date change, zoom, hide, replace) is unchanged.
- Printing is unchanged (it still uses `RenderBand`).

### 3.5 Opt-in benchmarks

`tools/perf-benchmarks` (outside the solution, like `tools/visual-fixtures`). Usage: its `README.md`.

## 4. Changes investigated but deferred

| Item | Why deferred |
|---|---|
| Associate date load, compose and Calendar month off the UI thread | **[Measured]** 0.25–0.40 ms and 0.5–0.78 ms on a 3-year synthetic database (§7). Moving them would add thread hops and race surface (date-switch ordering, autosave/flush, conflict handling) for no measurable gain. Re-evaluate only if the Windows checklist shows stalls. |
| Price History load off the UI thread | After 3.2, the remaining cost is once per open: ~10 ms at 2,000 synced observations, ~80 ms at 20,000 (an extreme size). Price History views are bound in real-Window tests, and this repository has a documented headless thread-affinity hazard. A UI-marshalling seam would be needed first (Phase 34C recommendation). |
| Warming up through the export service (workspace → model) as well | Would need a synthetic `AssociateWorkspace` in production code for ~6 ms more (warmed first generation 10.2 ms vs fully warm 3.2 ms). Not worth the extra surface. |
| Printing allocations | Investigation only, as instructed (§9.3). |
| Personal Sync transient-lock retry | Insufficient evidence (§10). |
| Gen-2 collections from preview bitmaps | Driven by the bitmap's native memory, not managed arrays (§9.1). Reusing bitmaps would change ownership; not justified. |

## 5. Before-and-after benchmarks

### 5.1 Environment

Apple M2 Pro (12 logical CPUs), macOS 27.2.1, .NET 10.0.11 Arm64, **Release**. Headless Avalonia + Skia (real
`WriteableBitmap`, no compositor, no window). Synthetic data only. Median / p95 over the stated number of samples,
after warm-ups (5 for timing loops, 10 for render loops, 3 for preview edits). "Before" figures were taken in the
same session on the same build, through the code path the change replaces, which is still present: the uncached
`GetHistoryAsync`, the copy render path, and no warm-up.

### 5.2 Summary table

| Measurement | Before | After | Δ | Δ % | Samples | User-visible? |
|---|---|---|---|---|---|---|
| First preview PDF work (generate + PDFium open/render), fresh process | 82.3 ms (p95 93.4) | **13.3 ms** (p95 13.9) | −69.0 ms | −84 % | 9 processes each | Yes, when the first PDF action comes after the UI went idle (preview hidden at startup, first Create PDF / Print) |
| First Create PDF generation, fresh process | 76.1 ms | **10.2 ms** | −65.9 ms | −87 % | 9 | as above |
| First PDFium open + render (first Print preparation / preview page) | 6.3 ms | **3.2 ms** | −3.1 ms | −49 % | 9 | marginal on this host |
| Worst case: warm-up and first PDF start together | 82.3 ms | 81.6 ms | ≈ 0 | — | 9 | no regression |
| Price History product switch, 20,000 synced | 65.9 ms (p95 70.7) | **0.46 ms** (p95 1.0) | −65.4 ms | −99 % | 40 | Yes (UI thread) |
| Price History product switch, 2,000 synced | 6.96 ms | **0.13 ms** | −6.8 ms | −98 % | 40 | small |
| Price History product switch, 0 synced | 0.15 ms | 0.14 ms | — | — | 40 | none |
| Price History open incl. first product, 20,000 synced | 147.6 ms | **79.8 ms** | −67.8 ms | −46 % | 15 + 40 | Yes |
| Official POSTs, 7-day window (admission + Fast Lane gap dates) | 21 | **9** | −12 | −57 % | deterministic | No (background), fewer official requests |
| Preview page render, busy page, managed allocation | 4,286 KB | **1.3 KB** | −4,285 KB | −99.97 % | 300 | No (GC churn) |
| Preview page render time, busy page | 1.11 ms | **0.63 ms** | −0.48 ms | −43 % | 300 | No |
| Preview page render time, 80-note page | 2.96 ms | 2.51 ms | −0.45 ms | −15 % | 300 | No |

"Open incl. first product" = product list + the first selected product's history, which is what opening the
overlay does. Before: 81.7 ms + 65.9 ms; after: 79.3 ms + 0.46 ms.

## 6. Startup measurements

`pdf-startup`, 9 fresh processes per mode, Classic template, busy synthetic day [Measured]:

| Metric (median) | cold | warmed | concurrent |
|---|---|---|---|
| `TryStart` on the calling (UI) thread | — | 0.42 ms | 0.41 ms |
| Warm-up duration (background thread) | — | 81.5 ms | — |
| First generation | 76.1 ms | **10.2 ms** | 73.2 ms |
| First PDFium open + render (150 % preview) | 6.3 ms | **3.2 ms** | 8.5 ms |
| First preview PDF work | 82.3 ms | **13.3 ms** | 81.6 ms |
| Second generation (warm) | 3.6 ms | 3.2 ms | 3.6 ms |
| First Modern generation after a Classic one | 5.8 ms | 4.9 ms | 5.7 ms |

- **Cold application startup** is unaffected by construction. The warm-up is queued only after `Window.Opened`,
  at `ApplicationIdle` priority, and costs 0.4 ms on the UI thread. Its ~80 ms of work runs on one
  below-normal-priority core. Time-to-window cannot be measured headless; checklist §A covers it on Windows.
- The Phase 34A audit's 186 ms first-PDF figure came from a harness that also cold-started the Associate view
  model; this phase isolates the PDF engine. The audit's one-time ~306 ms PDFium library load was **not
  reproduced** on this host (6.3 ms; the OS had the library cached). On a cold Windows boot it may be larger,
  and the warm-up moves it too.
- With the preview visible at startup (the default), the preview's own first render usually starts first and
  the warm-up does nothing (`AlreadyWarm`). The benefit is for the first PDF action when the preview is hidden,
  and for Create PDF and Print.
- [Estimate] On Windows laptops (Phase 34A: 1.5–3× slower for PDF work), roughly 100–200 ms would move off
  the first PDF action. Not measured.

## 7. Database responsiveness findings

`db` on a synthetic database: 3 years of park hours + 8-product admission results (2,192 dated rows), 24 tracked
season products × 40 local intervals, and 0 / 2,000 / 20,000 synced observations from 3 devices. Every call
**completed synchronously on return** (40/40), so "caller" time is UI-thread time. [Measured]

| Call (median, ms) | 0 synced | 2,000 | 20,000 |
|---|---|---|---|
| Associate date load (`LoadAssociateDateAsync`) | 0.40 | 0.39 | 0.26 |
| Calendar month (`OperationsCalendarService.LoadAsync`) | 0.78 | 0.75 | 0.52 |
| Price History list (`ListTrackedProductsAsync`) | 3.69 | 19.4 | 81.7 |
| Price History product, before (`GetHistoryAsync`) | 0.15 | 6.96 | 65.9 |
| — of which: reading the whole synced table (`GetAllAsync`) | 0.02 | 4.8 | 66.1 |
| — of which: this product's local intervals | 0.12 | 0.07 | 0.08 |
| Price History list via reader (34B-2) | 3.67 | 10.0 | 79.3 |
| Price History product via reader (34B-2) | 0.14 | 0.13 | 0.46 |

- **Associate and Calendar** are sub-millisecond, so they stay on the UI thread (§4). Phase 34A's concern was
  variance on a slow Windows disk or a held write lock, not the steady-state cost. Checklist §F still asks about
  date-switch stalls.
- **Price History** was the only read that scaled: 95 % of every product click was re-reading the whole synced
  table. The reader removes that per click.
- The 2,000-row list time (19.4 → 10.0 ms) does the same work on both paths; the difference is attributed to
  run order and is **not claimed** as an improvement.
- Real profiles were not inspected. 20,000 synced observations is an intentionally extreme size; a real
  multi-computer profile is expected to hold hundreds.

## 8. Official refresh request counts

Deterministic fixtures (`refresh-requests` benchmark and `Phase34B2PostCoalescingTests`), "today" 2026-08-20:

| Scenario | RC.12 | 34B-2 |
|---|---|---|
| Admission, 7 dates (identical 120-day calendar request) | 7 POSTs | **1** |
| Fast Lane, 7 gap dates (own single-day request + identical horizon probe) | 14 | **8** |
| Total | 21 | **9** |

Haunted, Bring-A-Friend (+ Duo) and Military send the same identical horizon probe for each date without a
product, so they gain in the same way on closed or gap dates [Code]. Live traffic was not measured (live tests
stay opt-in). A Check Online within 2 minutes of an identical successful request reuses that response, exactly
as GETs already did; a failed or thrown request is never reused.

## 9. PDF memory measurements

### 9.1 Preview renders (`pdf-render`, 300 renders per path, page 1 at 1.503 px/pt = 920 × 1190 px) [Measured]

| Page | Path | Time median / p95 | Allocated / render | Gen-2 GCs / render | Managed heap after |
|---|---|---|---|---|---|
| busy | copy (RC.12) | 1.11 / 1.42 ms | 4,286 KB | 0.82 | 1.6 MB |
| busy | **direct** | **0.63 / 0.75 ms** | **1.3 KB** | 0.57 | 1.7 MB |
| 80 notes | copy | 2.96 / 3.20 ms | 4,286 KB | 0.94 | 1.6 MB |
| 80 notes | **direct** | **2.51 / 2.59 ms** | **1.8 KB** | 1.00 | 1.8 MB |

- The 4 MB large-object-heap array and the copy per render are gone. No managed growth on either path.
- **Gen-2 collections are not materially reduced.** They come from the `WriteableBitmap`'s native pixel memory,
  which the runtime counts as memory pressure, not from the managed array. That cost is inherent to creating a
  new bitmap per page image. Reusing bitmaps would change the view model's image ownership and is not justified
  at today's heap size.
- Native memory cannot be measured on macOS (`HandleCount`/private bytes read 0). Checklist §G records Windows
  private working set after repeated edits.
- **Pixel identity** is proven by `Phase34B2DirectPdfRenderingTests`: every template, every page, scales
  0.37 / 1.0 / 1.5 / 2.08, normal and padded row strides, byte-for-byte equal to `RenderPage`.

### 9.2 Preview latency now (`preview`, 25 isolated edits, 300 ms apart) [Measured]

Busy page **14.6 ms** median (p95 18.4); 80-note document **34.2 ms** (p95 42.6). Edit → page image through the
real preview view model. This harness differs from Phase 34B-1's (no `AssociateViewModel`), so it is a new
baseline for future comparisons, not a comparison with 34B-1's numbers.

### 9.3 Printing (investigation only; `print-bands`, no GDI) [Measured + Code]

| Document | Render size | Bands | Band-loop time / page | Managed allocated / page | Gen-2 GCs |
|---|---|---|---|---|---|
| busy (1 page) | 5100 × 6600 px (600 DPI Letter) | 9 × ≤ 822 rows | 62 ms median | 225 MB | ~5 per page |
| 80 notes | same | 9 | 97 ms median | 225 MB | ~5 per page |

- Each band reloads the page (`FPDF_LoadPage`), allocates a 16 MiB BGRA array and a ~12.6 MiB BGR24 array,
  and holds the PDFium lock only for that band. A preview render therefore waits for **at most one band**
  (~7–11 ms here), not the whole job. This corrects the audit's "blocks preview during spooling".
- Time is not a problem; the transient allocation is high, but it is released after each band.
- **Proposed separate fix (not implemented):** rent one BGRA and one BGR24 buffer per job from `ArrayPool`,
  render each band straight into the rented BGRA buffer through `RenderPageInto` (offset rendering), convert
  in place, and load each page once per page instead of once per band. Print resolution, scaling, settings and
  output stay identical. It needs Windows measurement of GDI spool time and output first (checklist §H–I).
  GDI handle stability, spool time, printer drivers and physical output are Windows-only and unmeasured here.

## 10. Personal Sync reliability decision

**Deferred, documented.** `AtomicFileWriter` writes temp → flush to disk → `File.Move(overwrite)`, and deletes the
temp on any failure. A transient sharing violation (`0x80070020`/`0x80070021`) from OneDrive or antivirus during
that rename is a **hypothesis**: no tester has reported it, and RC.11/RC.12 Windows validation passed. If it
happens, the outcome is already safe: the destination keeps its complete previous version, nothing partial is
visible, and the next sync cycle redoes the write. The user would see one "sync failed" status; the log line
"…could not be written right now; it will be retried on a later sync" identifies it.

Why not retry anyway: sync files are named by entity, so two computers can write the same file. A retry loop
would slightly widen the window between the envelope conflict check and the rename. That is small next to
OneDrive latency, but it is not zero, and it would add delay to shutdown flushes for an unobserved problem.

Evidence to collect: checklist §K (10 edits over 2 minutes with OneDrive active). If a sharing violation is
ever logged, the recommended fix is three attempts at 50 / 100 / 200 ms, on those two HResults only, around
`File.Move`, never for permission or data errors, with the temp file kept between attempts and deleted at the end.

## 11. Regression tests

New, all deterministic (no timing assertions):

- `Infrastructure.Tests/Retrieval/Http/Phase34B2PostCoalescingTests` (24):
  - concurrent identical POSTs → 1 request, every caller gets the response;
  - sequential reuse within the window and refetch after it;
  - no merging across different keyword, start date, end date, display_zero_capacity, header value, extra
    header, missing headers or endpoint;
  - header order and name case do not matter;
  - outside the accesso endpoint, bodies must match byte for byte; without a body key, nonce-different bodies
    are not merged;
  - neither failure statuses nor exceptions are reused, and concurrent callers share a failure but the next call
    retries;
  - one caller cancelling does not cancel the shared POST; GET and POST to the same URI stay separate;
  - the body key ignores only `request_token`, and anything unexpected stays exact;
  - real sources on fixtures: admission over 7 dates gives 1 POST with per-date results identical to direct
    calls; Fast Lane gap dates share 1 horizon probe; a repeated refresh after the window refetches with its
    own timestamp; a failed shared POST is an error for that date and the next date retries.
- `Infrastructure.Tests/PersonalSync/Phase26LegacyPassIdentitySyncTests` (+3, real SQLite, two computers):
  - the reader's list and every product's history equal the uncached service, including the legacy Gold fold,
    on both computers;
  - a reader is a stable snapshot while a new reader sees the next merge;
  - local intervals are still read fresh.
- `Ui.Tests/Phase34B2DirectPdfRenderingTests` (9, real QuestPDF + PDFium):
  - pixel identity across every template, page, scale and stride;
  - padding bytes are never written;
  - after dispose, under cancellation, with a short stride, a null target or a bad page: refused and nothing
    written;
  - size clamping matches `RenderPage`;
  - concurrent direct and copy renders stay serialized and identical;
  - the factory falls back to render-and-copy.
- `Ui.Tests/Phase34B2PdfEngineWarmUpTests` (12):
  - runs once in the saved template on the synthetic document and renders one tiny page;
  - does nothing when a real PDF already started (with the renderer warm or cold); skips PDFium when it is
    already loaded;
  - cancel before start prevents it; cancel mid-generation reports Cancelled; failures are swallowed and never
    retried;
  - the default runner uses a background thread;
  - real engine end to end; the synthetic document generates in every template;
  - startup wiring (idle after `Opened`, cancel on shutdown).
- `Ui.Tests/Phase34B2PriceHistoryReaderTests` (6):
  - one reader per visit;
  - `OfficialPriceHistory` and `OfficialData` replace it, keeping the selection; unrelated changes keep it;
  - close drops it and reopen reads fresh data;
  - a late older open never replaces a newer list.

No existing test was changed, weakened or skipped. The Phase 32/32A/33/34B-1 suites, PDF template and real-render
tests, Copy menu, pricing and official-source fixture tests, Personal Sync / conflict tests, and updater and
release-content tests all pass unchanged.

**Final deterministic partition** (Release, `Category!=LiveSmoke`, the six real-Window classes alone):

| Project / partition | Tests |
|---|---|
| Domain | 462 |
| Application | 1,369 |
| Documents | 479 |
| Infrastructure | 1,211 (+27) |
| Ui mixed | 1,746 (+27) |
| OperationsCalendarViewTests / Phase15ERealWindowLayoutTests / RealWindowStartupTimingTests / SpellCheckNavigationLifecycleTests / Phase21DMathEditorWindowTests / Phase33PreviewWindowTests | 26 / 117 / 2 / 7 / 4 / 9 |
| **Total** | **5,432 passed, 0 failed, 0 skipped** (5,378 + 54) |

Clean Release build (`dotnet build CarowindsPassdown.slnx -c Release --no-incremental`): **0 warnings, 0 errors**.

## 12. Windows validation checklist

`docs/releases/rc13-phase34b2-localtest-windows-checklist.md` (LOCAL-ONLY candidate `RC13-Phase34B2-LOCALTEST`).
Nothing is marked passed until the product owner runs it; Windows results go there, separately from §5–§9.

## 13. Known limitations

- Mac numbers only. Windows timing, GPU composition, cold-boot disk and antivirus effects, native memory, GDI
  handles, printing and OneDrive behaviour are unmeasured here.
- The warm-up does not warm the workspace → document-model step (~6 ms on the first generation).
- Price History: while the overlay is open, a write made by a **separate** process (the headless
  `--scheduled-refresh` task) is not signalled to the app. The open overlay then shows the fold from when it was
  opened until it is reopened or refreshed. The product list already behaved this way.
- The direct render path still creates a new native bitmap per page image, so gen-2 collections continue (§9.1).
- POST coalescing reuses a successful official response for up to 2 minutes, the same established window as GETs.
- Printing allocations and the sync-folder retry are proposals only.

## 14. Recommendations for Phase 34C

1. Consolidate the six accesso dated sources' shared helpers (audit F09). The coalescing key now centralizes
   the request identity, which makes this easier.
2. Add a UI-marshalling seam (`IUiDispatcher` or a view-model load runner) before moving any further loads off
   the UI thread; then reconsider Price History open at large synced sizes.
3. Print path: `ArrayPool` band buffers + `RenderPageInto` + one page load per page (§9.3), once Windows
   measurements confirm spool behaviour.
4. Personal Sync bounded retry, only if §K or logs show sharing violations (§10).
5. The audit's safe cleanups (unused members, `logo.png` resource, the 5 s busy-timeout test, the
   `RealWindowStartupTimingTests` name) and the RID-conditional Avalonia platforms / used-Lato-faces packaging
   items, each gated by its own fingerprint or release-content test.
6. Run `tools/perf-benchmarks all` on Windows in `CarowindsTest` to get the first real Windows baseline.
