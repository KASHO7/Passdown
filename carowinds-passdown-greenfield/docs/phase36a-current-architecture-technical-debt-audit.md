# Phase 36A — Current architecture and technical-debt audit

Status: audit complete locally (documentation only) / no Windows validation required or executed / awaiting owner review / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local branch one commit ahead of `origin/feature/passdown-redesign`; nothing pushed) |
| Starting commit | `7c2027e` — "Documentation governance, Claude instructions and repository guards (maintenance)" |
| Ending commits | One local documentation commit adding this record and its index line (hash reported in the completion report, not here) |
| Reviewed source baseline | `7c2027e`. Production source (`src/`) is identical to RC.14 release source `3d603f0`; `git diff --stat 3d603f0 7c2027e -- src tests tools packaging Directory.Build.props CarowindsPassdown.slnx` shows only the six repository-guard test files, `.DS_Store` removals and a `Directory.Build.props` comment edit |
| Dirty-tree caveats | Clean working tree at start (no staged, unstaged or untracked files). Build/test/benchmark output went to ignored `bin`/`obj` folders and the session scratch folder only |
| Authorization | Owner's Phase 36A brief: audit only; source inspection, existing tests and benchmarks, audit documentation and one small local documentation commit |
| Release status | `v0.1.0-rc.14` tag → `3d603f0`; published 2026-11-27 per [RC.14 readiness](releases/rc14-release-readiness.md); not merged to `main`. Source version `0.1.0-rc.14` / FileVersion `0.1.0.14` (`Directory.Build.props`), unchanged |
| Database schema / sync formats | SQLite schema 18 (`Migration0018CreateOfficialReferenceDocumentsTable`); sync envelope 1; draft payload 4; finalized snapshot 2; finalized/operational-note/monthly-ERT/ERT-override sync payloads 1. Read from source constants; unchanged |

## 1. Executive summary

The production architecture at RC.14 is in good condition. I found **no P0 or P1 defects**. The Phase 34
performance work is intact and reproduces on the Mac development host. The preview, PDFium and Personal Sync
paths have sound resource ownership and concurrency control. Official retrieval still coalesces only the
approved Accesso calendar endpoint and ignores only `request_token`.

The real remaining debt is concentrated in **test infrastructure**. Three things are enforced only by written
procedure, not by the repository:

- the real-window isolation convention;
- the hang and memory safeguards;
- the screenshot-harness memory mitigation.

That procedure proved fragile during this audit (§11.2). Associate workspace size is a maintenance cost but not
a defect. One narrow child view-model extraction (the Early Ride Time editor) is safe and well bounded. The
composition root, Personal Sync and performance proposals do not currently justify implementation work.

**Recommended Phase 36 roadmap:**

| Milestone | Recommendation |
|---|---|
| 36B | Proceed, narrowed to test-only work |
| 36C | Narrow to the Early Ride Time editor, plus the three direct dispatcher posts |
| 36D | Cancel |
| 36E | Defer until Windows measurements exist |
| 36F | Combine into each milestone's close-out |

## 2. Verified maintenance baseline and final commit

| Item | Reported in handoff | Verified now | Evidence |
|---|---|---|---|
| Branch / HEAD | `feature/passdown-redesign` | `feature/passdown-redesign` @ `7c2027e643df` | `git rev-parse HEAD` |
| Final maintenance commit | — | `7c2027e` (Nov 27 2026 07:40 −05:00) | `git log`; commit message lists scope and 5,914 tests |
| Maintenance committed | yes | yes; tree clean; branch 1 commit ahead of origin (maintenance commit not pushed) | `git status --porcelain=v2 --branch` |
| Maintenance record | — | **No standalone `docs/` maintenance record exists**; the evidence is the commit message plus the review baseline in `docs/architecture.md` (which names HEAD `9351c64` and states that no build or test was run for that read-only review) | `ls docs`; `docs/architecture.md` |
| Production behavior unchanged | yes | yes: no `src/` file changed between `3d603f0` and `7c2027e` | `git diff --stat 3d603f0 HEAD -- src` (empty) |
| Published release | `v0.1.0-rc.14` | tag `v0.1.0-rc.14` → `3d603f0` | `git rev-list -n1 v0.1.0-rc.14` |
| Version | rc.14 | `0.1.0-rc.14` / `0.1.0.14` | `Directory.Build.props` |
| Repository guards | 21 | 21 passed, 0 failed (17 attributes; theories expand to 21) | `--filter FullyQualifiedName~.RepositoryGuards.` |
| Deterministic tests | 5,914 / 0 failed / 0 skipped | **5,914 / 0 / 0** (rerun, §11.1) | partition logs |
| Clean Release build | 0 warnings, 0 errors | **0 warnings, 0 errors** after deleting every `bin`/`obj` in `src`, `tests`, `tools/update-signing` and building `--no-incremental` | build log |
| Real-window classes | six, passed | six, passed | §11.1 |
| Hang detection / 3 GB guard | "enabled" | **Procedural only**: `build-and-test` skill text; no runsettings, script or code in the repository enforces either (§11.2) | repo search |

The handoff mentions "current Phase 35 and Phase 36 records". No Phase 36 record existed before this one.

## 3. Repository architecture inventory

**Toolchain.** .NET SDK 10.0.400, `net10.0` for every project (`Directory.Build.props`), runtime 10.0.11.

**Major packages:**

| Package | Version |
|---|---|
| Avalonia, Desktop, Fluent, Fonts.Inter, Headless | 12.1.1 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Microsoft.Extensions.* (DI, Logging, Options, Configuration) | 10.0.11 |
| Microsoft.Data.Sqlite | 10.0.11 |
| QuestPDF | 2026.7.3 |
| bblanchon.PDFium (Win32/macOS/Linux) | 159.0.8138 |
| xunit | 2.9.3 |
| PdfPig (UI tests) | 0.1.15 |

No central package management.

**Project graph** (verified from `.csproj` and by `ProjectDependencyGraphTests`):

| Project | References |
|---|---|
| Domain | none |
| Application | Domain |
| Infrastructure | Application, Domain |
| Documents | Application, Domain |
| Desktop | Application, Infrastructure, Documents |
| Updater | Infrastructure |

**Size** (measured 2026-11-27; `.cs` and `.axaml` lines including comments):

| Project | .cs | .axaml | Lines |
|---|---|---|---|
| Domain | 41 | 0 | 4,638 |
| Application | 202 | 0 | 19,671 |
| Infrastructure | 132 | 0 | 19,704 |
| Documents | 27 | 0 | 4,636 |
| Desktop | 125 | 14 | 27,745 |
| Updater | 1 | 0 | 33 |
| **Production total** | 528 | 14 | 76,427 |

| Test project | .cs files | Lines |
|---|---|---|
| Domain.Tests | 29 | 3,188 |
| Application.Tests | 114 | 21,363 |
| Infrastructure.Tests | 132 | 24,175 |
| Documents.Tests | 32 | 6,205 |
| Ui.Tests | 160 | 40,609 |

**Largest components:**

| File | Lines | Note |
|---|---|---|
| `Desktop/ViewModels/AssociateViewModel.cs` | 3,163 | 73 `[ObservableProperty]`, 24 `[RelayCommand]`, 26 constructor parameters (24 optional) |
| `Desktop/Views/AssociateView.axaml` | 2,258 | Of which 679 lines are view-local styles |
| `Infrastructure/PersonalSync/PersonalSyncService.cs` | 2,061 | Five entity channels |
| `Desktop/ViewModels/PassdownPdfPreviewViewModel.cs` | 1,325 | |
| `Desktop/App.axaml.cs` | 1,085 | Composition root; 152 registrations |
| `Application/Associate/AssociatePassdownService.cs` | 1,008 | |
| `Infrastructure/PersonalSync/PersonalSyncConflictResolutionService.cs` | 816 | |
| `Documents/DailyFeatureMath/MathLayoutEngine.cs` | 784 | |
| `Desktop/Themes/PassdownStyles.axaml` | 760 | |
| `Desktop/Views/DashboardView.axaml` / `OperationsCalendarView.axaml` | 743 / 730 | |

The next-largest view models are `OperationalNotesViewModel` (647), `OperationsCalendarViewModel` (627) and
`OperationsDashboardViewModel` (512). The largest code-behind file is `AssociateView.axaml.cs` (323 lines:
preview splitter layout, calendar flyout keys, time-box keys, price-popover anchoring and focus), all of it
presentation.

**Composition and background work.** `App.axaml.cs` uses `Microsoft.Extensions.DependencyInjection`: 140
singletons and 12 transients, of which the workspace view models are transient but resolved once through
`MainViewModel`. The same composition is reused by the `--scheduled-refresh` host (`Program.cs`).

Background operations started after the window is assigned:

- Personal Sync coordinator
- official-data refresh coordinator (4 h interval)
- historical backfill
- update coordinator (90 s delay)
- update and temporary-PDF cleanup
- PDF engine warm-up at `ApplicationIdle` after `Opened`

**Data contracts.** Schema 18 with 18 contiguous migrations, all registered (guarded). Sync and payload versions
are listed in the header table.

**Pipelines and Windows integration:**

- **PDF and preview.** QuestPDF via `AssociatePassdownPdfGenerator`, wrapped by `AssociatePassdownPdfExportService`
  and shared by preview, Print, Save PDF and Create PDF. PDFium rasterizes through `PdfiumPdfRasterizer` under
  one process-wide `PdfiumNative.Sync` lock.
- **Retrieval.** One `HttpClient` wrapped by `RetrievalHttpClient`, then `CoalescingRetrievalHttpClient` shared
  by park hours, shows and the Accesso dated sources.
- **Windows-specific code.** P/Invoke or `OperatingSystem.IsWindows` appears in:
  - `WindowsPdfPrintService`
  - `WindowsCaptionStyler`
  - `WindowsAnimationEffectsReader`
  - `ScheduledTaskStatusProvider`
  - `SpellingBackendFactory`
  - `SyncFolderValidator`
  - `SyncTempFileCleaner`
  - `HistoryPdfTempFileStore`
  - `UpdatePaths`
  - `InstallationInspector`
  - `PdfiumNative`

  All of these are in Infrastructure behind Application ports, except a few guards in `App.axaml.cs`.
- **Updater.** `Carowinds.Passdown.Updater` (33 lines) delegates to Infrastructure `UpdateInstaller`, which
  uses signed manifests, hashes, safe extraction and rollback. This audit made no change and found no finding.

## 4. Component ownership

| State or responsibility | Owner | Evidence |
|---|---|---|
| Selected operating date | `AssociateViewModel.SelectedDate` (single property; the Calendar, Dashboard "open passdown" and `SelectEasternToday` all route through it) | `AssociateViewModel.cs:229`, `:1718-1790` |
| Date of the fields being edited | `_draftDateInEdit` (distinct from `SelectedDate` during flush-then-switch) | `:112`, `:1792-1845` |
| Editable draft | Fields on `AssociateViewModel`, assembled by `BuildDraft()`; persisted only by `SaveDraftNowAsync` | `:2746`, `:2858` |
| Autosave lifecycle | `ScheduleAutosave` → `RunAutosaveAsync` (debounce) → `SaveDraftNowAsync` (serialized by `_saveSemaphore`, conditional on the loaded version); `FlushPendingAutosaveAsync` for switch, close and update | `:2801-3031` |
| Validation | Per-field parse and error properties on `AssociateViewModel`; domain rules in Application/Domain | `RequiredTimeError`, `TryParseNonNegativeInt` |
| Preview requests | `PassdownPdfPreviewViewModel`, fed by `CapturePreviewRequest` and `ScheduleAutosave → Preview.NotifyContentChanged()`; it never writes drafts | `AssociateViewModel.cs:912-925`, `:1309`, `:2810` |
| Price edit popover | `PriceEditorViewModel`, with the predicate `IsPriceEditTargetCurrent` | `:261`, `:326` |
| Early Ride Time editor | `AssociateViewModel` fields; persists independently through `IErtConfigurationService` with its own base hashes | `:2188-2356` |
| Pass Perks / Official Details | Child view models `PassPerksPanelViewModel` and `OfficialDetailsDialogViewModel` | constructor |
| UI section and expansion state | `AssociateViewModel` (`SelectedSection`, `_collapsedProductSections`, `_expandedShowIds`); device-local and never in the draft | `:254-420`, `:783` |
| Navigation and workspaces | `MainViewModel`; every workspace is created once at startup and held for the session | `MainViewModel.cs:47-63` |

The required invariant holds: one selected date, one draft, one autosave lifecycle. Row view models
(`ProductRowViewModel`, `CustomProductRowViewModel`, `AssociateShowRowViewModel`,
`AssociateOperationalNoteRowViewModel`) receive the `ScheduleAutosave` callback rather than owning persistence.

## 5. Associate workspace findings

1. **Size is real but not defective.** The view model mixes date and draft lifecycle, section presentation, ERT
   editing, deals and details loading, and command plumbing. The coupling that matters (date guard checks after
   every await, `_isApplyingWorkspace`, `_isDirty` and `_saveInFlight`) is *intentionally* centralized. Splitting
   section view models that each held draft fields would violate the invariant; this audit rejects it (§16).
2. **The smallest safe view-model extraction is the ERT editor** (finding TD-05). It is self-contained:
   - about 170 lines of commands (`OpenErtEditorAsync`, `LoadErtEditorFieldsAsync`, `CancelErtEditor`,
     `SaveMonthlyErtAsync`, `SaveOverrideAsync`, `RemoveOverrideAsync`, `BuildErtDraftFields`);
   - about 15 editor properties, base hashes and `_ertEditorLoadedFields`;
   - it never touches the draft or autosave, and persists through `IErtConfigurationService` only.

   It has five touch points with the parent, each expressible as a constructor callback in the established
   `PriceEditorViewModel` pattern:

   | Touch point | Callback shape |
   |---|---|
   | `SelectedDate` | `Func<DateOnly>` read |
   | Workspace reload after save | `RefreshWorkspaceAsync(date)` |
   | `SyncConflictMessage` | setter |
   | Reload deferral | `IsErtEditorDirty()` |
   | `ApplyWorkspaceToFields` | closes the panel |

   **Binding evidence** (`AssociateView.axaml` occurrences / Ui.Tests files referencing the name):

   | Member | XAML | Test files |
   |---|---|---|
   | `MonthlyErt*` | 7 | 9 |
   | `IsErtEditorOpen` | 1 | 5 |
   | `OpenErtEditor` | 1 | 5 |
   | `SaveOverride` | 1 | 5 |
   | `SaveMonthlyErt` | 1 | 3 |
   | `RemoveOverride` | 1 | 3 |
   | `ErtUseOverrideForDate` | 2 | 1 |
   | `OverrideRidesText` | 1 | 1 |
   | `CancelErtEditor` | 1 | 1 |
   | `OverrideReasonText` | 1 | 2 |
   | `OverrideNoErt`, `OverrideSaveError` | 2 each | 0 |

   No other production file references these members. Phase 34A (F10) and Phase 34C (§9, §14.1) already
   identified this extraction and deferred it only because bindings change.
3. **XAML extraction offers less benefit than expected.** The four sections occupy contiguous ranges of
   `AssociateView.axaml`:

   | Section | Lines | Approximate size |
   |---|---|---|
   | Overview | 1118-1569 | 450 lines |
   | Products | 1570-1929 | 360 lines |
   | Shows | 1930-2038 | 110 lines |
   | Notes | 2039-2207 | 170 lines |

   All bind directly to `AssociateViewModel`, so a `UserControl` per section would need no view-model change and
   would inherit the view-local styles. However:
   - 22 Ui.Tests files read `AssociateView.axaml` as text and assert on its markup, so they would need
     retargeting;
   - named elements (`ShowsPanel`, `DailyNotesSection`, `PersistentNotesSection`, `IncludeInPassdownPanel`,
     `PriceEditorPanel`) would move into a separate name scope;
   - the code-behind's `PriceEditButton_OnClick` anchoring and key handlers reference controls in the Products
     section.

   The result would be smaller files, not lower coupling. Visual-regression risk (approved Phase 35 designs) and
   test churn outweigh the benefit, so this audit recommends against XAML extraction now (§16).
4. **The seam gap remains.** Two of the three direct `Dispatcher.UIThread.Post` calls left in view models are in
   the workspace path: `AssociateViewModel.cs:960` (sync change) and `OperationalNotesViewModel.cs:216`. The
   third is `PersonalSyncSettingsViewModel.cs:167`. The adjacent official-data path already uses `IUiDispatcher`.
   Phase 34C §14.2 carry-over (TD-06).

## 6. UI architecture findings

- **Tokens are consistent.** No view contains a hard-coded hex colour. Views use `DynamicResource` tokens
  (AssociateView 124, Calendar 50, Dashboard 41). There are no unexplained style islands.
- **View-local style blocks:**

  | View | Local style lines |
  |---|---|
  | AssociateView | 679 |
  | OperationsCalendarView | 309 |
  | DashboardView | 167 |
  | PersonalSyncSettingsView | 107 |
  | HistoryView | 55 |
  | PassdownPdfPreviewView | 52 |
  | OperationalNotesView | 47 |

  Ten AssociateView class selectors also exist in `PassdownStyles.axaml`: `active`, `categoryBand`, `fieldError`,
  `fieldLabel`, `hoursHeadline`, `pageTitle`, `pricePopover`, `priorityText`, `sectionTab` and
  `subsectionTitle`. The file documents them as deliberate deltas for the dense editor. This is TD-07 (P3):
  consolidating them would risk approved visuals for no functional gain.
- **Code-behind** is layout, focus, keyboard and native integration only. `DashboardView.axaml.cs` (133 lines)
  switches the grid by width, and `PersonalSyncSettingsView.axaml.cs` (144 lines) handles the folder picker. I
  found no business logic, persistence or retrieval in code-behind.
- **Markup-text tests.** About 44 test files assert on raw `.axaml` text (per-file counts overlap):

  | Asserted file | Test files |
  |---|---|
  | AssociateView | 22 |
  | PassdownTokens | 16 |
  | PassdownStyles | 12 |
  | MainWindow | 9 |
  | PersonalSyncSettingsView | 8 |
  | Calendar / Dashboard | 7 each |

  These tests protect approved markup cheaply, but they couple tests to file layout (TD-08). This is not a
  defect; it is a cost to weigh before any view extraction.
- No obsolete styles were found in this pass. Phase 35G removed `ShowColumnsPanel` and the Fury banner, and
  tests keep them retired. I did not verify whether compiled bindings are the project default, and I make no
  claim about it.

## 7. Composition root findings

`BuildServiceProvider` (about 330 lines, `App.axaml.cs:528-854`, plus helper registrations to line 1085) is long
but linear and commented by phase. `CompositionRootTests` (14 tests) and the migration-registration guard cover
it.

Lifetimes are coherent. Every workspace view model is created once, so event subscriptions to singleton
notifiers live for the process and do not leak. Shutdown runs one flush with a timeout and stops the coordinators
(`AttachShutdownFlush`, `ShutdownFlushTimeout`). The service provider and its disposable singletons are not
disposed explicitly at process exit. This is harmless for a process that is ending (no flush depends on it), and
I record it as an observation, not debt.

I found no evidence that changes to registration are difficult. Subsystem `Add…` extraction would move code
without reducing coupling (TD-10, P3). **Recommendation: no 36D work.**

## 8. Persistence and Personal Sync findings

**Integrity controls verified in source:**

- one pass at a time: an in-process `_gate` plus `PersonalSyncProcessLock` across processes;
- read-decide-write under `BEGIN IMMEDIATE` for drafts, notes, monthly ERT and ERT overrides;
- conditional, version-checked draft saves that turn a concurrent change into a conflict, not an overwrite;
- sync-space identity reconciliation before import;
- `AtomicFileWriter`: temp file in the target directory, `Flush(flushToDisk: true)`, then `File.Move(overwrite)`;
- stale temp-file cleanup;
- change notification published even when a pass fails;
- price history as a separate merge-only channel that runs last.

225 PersonalSync tests in Infrastructure.Tests passed.

**No confirmed integrity risk.** Theoretical concerns that remain:

- No bounded retry for transient sharing violations on sync-folder renames (Phase 34A F08). The current failure
  is safe and retried on the next pass. This is a hypothesis without Windows evidence, so it stays deferred.
- Rollback journal rather than WAL (F20). This is a hypothesis; no lock-wait evidence.

**Duplication (TD-09, P3).** The five channels each follow an Import/Process/Export triad.
`ProcessMonthlyErtEnvelopeAsync` and `ProcessErtDateOverrideEnvelopeAsync` differ in 60 of 200 lines. A shared
helper would reduce maintenance, but it touches conflict semantics in the riskiest subsystem and fixes no defect.

Any future refactor must satisfy all of the following:

1. Byte-identical sync files for a synthetic multi-device corpus.
2. Identical conflict records and tombstone ordering.
3. Unchanged `SyncedContentHash` bookkeeping.
4. All 225 PersonalSync tests passing, plus a new before/after golden-corpus test.
5. Rollback by reverting the single commit, which is possible only because formats are unchanged.

**Recommendation: defer.**

## 9. PDF and printing findings

Phase 33 and 34 behavior is present in source:

- leading-edge refresh plus a 200 ms trailing quiet window;
- serialized generations with stale-generation rejection (`_generation`);
- delayed "Updating" indicator;
- warm-up at `ApplicationIdle` after `Opened`, cancelled on shutdown;
- direct-to-bitmap rendering;
- one export service for preview, Print, Save and Create PDF.

**Resource ownership:**

- **Superseded results.** A raster or image that is superseded, hidden or failed is disposed on every path
  (`PassdownPdfPreviewViewModel.cs:1115-1160`, `:1228-1252`).
- **Page swaps.** `SwapPageImage` disposes the previous bitmap.
- **Document close.** `PdfiumPdfRasterDocument` checks disposal under the process-wide lock, so a background
  render cannot race a close (`PdfiumPdfRasterizer.cs:214-245`).
- **Native print handles.** `WindowsPdfPrintService` calls `AbortDoc` on every failure path and `GlobalFree`s
  the DEVMODE/DEVNAMES handles in `finally`.

**No leak found.**

| Category | Observation |
|---|---|
| Expected temporary allocation | Printing allocates about 225 MB of managed memory per 600 DPI page, transiently (measured §12; F12). Windows memory is unmeasured. Proposal: Phase 34B-2 §9.3, gated on Windows J4/J5 |
| Managed retention | None observed in preview (heap 1.7 → 1.8 MB over 200 renders) |
| Native retention | No owner gaps. The PDFium document has no `SafeHandle` or finalizer backstop (F21, TD-14, defense in depth only) |
| Test-harness artifacts | visual-fixtures growth (§11) |

## 10. Retrieval findings

**Phase 34B-2 coalescing is intact** (`CoalescingRetrievalHttpClient`):

- one in-flight task per key, plus a 2-minute reuse window for successful responses only;
- per-caller cancellation;
- the POST key is URI + headers (case-insensitive names) + body;
- `AccessoKeywordCalendarCoalescing.KeyBody` normalizes **only** `AccessoKeywordCalendarRequest.Endpoint` and
  removes **only** `request_token` from a flat string object; any other body compares byte for byte.

The deterministic benchmark reproduces 21 → 9 POSTs. **Phase 34C** shared Accesso transport and evidence are
present (`AccessoKeywordCalendar.cs`). The dormant `IJobBrowserSessionFactory` seam is still intentionally
retained (four files, F23).

One observation, not a defect:

- **TD-11, P3 hypothesis.** Retrieval uses `services.AddSingleton(new HttpClient())`, which has the default
  handler with no `PooledConnectionLifetime`. The updater client sets 10 minutes
  (`GitHubReleaseSource.cs:60-65`).
- **Possible effect.** DNS changes may be ignored during a very long session.
- **Evidence.** None. Defer.

## 11. Test infrastructure findings

### 11.1 Fresh deterministic run (Mac host, §12 environment)

| Partition | Passed | Failed | Skipped | Runner time |
|---|---|---|---|---|
| Domain.Tests | 467 | 0 | 0 | 0.07 s |
| Application.Tests | 1,396 | 0 | 0 | 0.8 s |
| Infrastructure.Tests (incl. 21 guards) | 1,340 | 0 | 0 | 15 s |
| Documents.Tests | 484 | 0 | 0 | 3 s |
| Ui.Tests mixed | 1,986 | 0 | 0 | 8 s |
| `OperationsCalendarViewTests` | 30 | 0 | 0 | 4 s |
| `Phase15ERealWindowLayoutTests` | 144 | 0 | 0 | 25 s |
| `RealWindowStartupTimingTests` | 2 | 0 | 0 | 1 s |
| `SpellCheckNavigationLifecycleTests` | 7 | 0 | 0 | 10 s |
| `Phase21DMathEditorWindowTests` | 4 | 0 | 0 | 1 s |
| `Phase33PreviewWindowTests` | 54 | 0 | 0 | 9 s |
| **Total** | **5,914** | **0** | **0** | |

The total matches the maintenance commit's 5,914, which is +21 guards over RC.14's 5,893. The UI partitions ran
with `--blame-hang --blame-hang-timeout 90s`. No hang occurred in two runs of the mixed partition and the large
real-window classes.

**Peak test-host resident memory** (second run, 1 s sampling):

| Partition | Peak |
|---|---|
| Mixed | 474 MB |
| `Phase15ERealWindowLayoutTests` | 535 MB |
| `Phase33PreviewWindowTests` | 405 MB |
| `SpellCheckNavigationLifecycleTests` | 179 MB |

All of these are far below the 3 GB limit.

### 11.2 Findings

- **TD-01 (P2): real-window confinement is a convention, not a check.**
  - **Root cause.** Ui.Tests has no Avalonia dispatcher loop: `Avalonia.Headless.XUnit` conflicts with xunit v2
    (`HeadlessTestSetup.cs`). Real `Window` tests therefore have to run alone.
  - **Current practice.** Later phases add real-window tests as *partials* of run-alone classes.
    `Phase35BWorkspaceWindowTests.cs`, `Phase35CProductsWindowTests.cs`, `Phase35DShowsNotesWindowTests.cs` and
    `Phase35DCompactDateWindowTests.cs` are all partials of `Phase33PreviewWindowTests`, so their file names no
    longer match the class that the partition filter selects.
  - **Gap.** Nothing fails if a future real-window test lands in a mixed-partition class. Phase 34A observed 148
    failures from one mis-partitioned run.
  - **Fix.** Add a test-only source guard: a test that scans Ui.Tests for `new Window`, `new MainWindow(` or
    `.Show()` and fails outside the six classes.
- **TD-02 (P2): hang and memory safeguards are procedural.**
  - The `build-and-test` skill tells the operator to use `--blame-hang` and to kill a test host above about 3 GB.
    Nothing in the repository applies either.
  - **Evidence from this audit.** My first watchdog matched on the process name `testhost`. On macOS the host runs
    as `dotnet … testhost.dll`, so the watchdog never matched (logged peak 0) while the suite ran unguarded. The
    rerun used a corrected match.
  - **Fix.** Check in a small partition runner (for example `tools/run-deterministic-tests.sh` or `.ps1`) that
    applies the six-class split, `--blame-hang` and an RSS cap. The owner must approve this, because it is new
    tooling.
- **TD-03 (P2): the screenshot-harness mitigation is documentation-only.**
  - Phase 35G-1 removed the largest retention: a shared appearance controller had kept every closed window, which
    produced the 18 GB run.
  - The README still documents growth to about 5.8 GB after 223 captures in one process.
  - **Bounded measurement today** (`main` size, forced Light/Dark):
    - 2 scenarios (4 captures): 324 MB peak;
    - 8 `Shell-` scenarios (16 captures): 594 MB peak;
    - about **22 MB retained per additional capture**, consistent with the README.
  - **Gap.** The tool still accepts `*`, which renders every scenario in one process, and nothing caps it.
  - **Assessment.** The mitigation is sufficient *if followed*; it is not enforced.
  - **Fix.** Refuse multi-scenario filters above a small count without an explicit flag, or re-launch one child
    process per scenario. This is tool-only and does not change production code.
- **TD-04 (P3).** `tools/visual-fixtures` builds with one warning (CS0618 `Bitmap.Save(string, int?)`, `Program.cs:412`).
  It is outside the solution, so this does not contradict the 0-warning solution build.
- **Intermittent stall.** The Ui assembly stall was "seen once in 35E" and did not recur in 35G's runs or in this
  audit. The evidence is insufficient to justify work: **Unverified / no action**.
- **Repository guards.** The implementation is sound and mechanical. It runs in under 0.1 s inside the normal
  Infrastructure partition. The grandfathered list is pinned at 62 records. No change is recommended.
- **Markup-text assertions (TD-08)**: see §6.

## 12. Performance evidence

**Environment.**

| Item | Value |
|---|---|
| Hardware | Apple M2 Pro, 16 GB |
| OS | macOS 27.2.1 (26B131) |
| Runtime | .NET 10.0.11 Arm64, Release |
| Tool | `tools/perf-benchmarks` (unchanged) |
| Data | Synthetic database only; scratch folder outside the repository and cloud sync |
| Network | None |

These are **Mac development-host numbers, not Windows evidence.**

| Benchmark | Result now | Phase 34B-2 record | Status |
|---|---|---|---|
| First preview PDF work, cold / warmed (5 fresh processes) | 87.7 / **13.1 ms** median | 82.3 / 13.3 ms | Resolved (warm-up intact) |
| Warm-up caller cost on the UI thread | 0.37 ms | 0.42 ms | Resolved |
| Preview render, busy page: copy vs direct | 1.14 ms, 4,286 KB vs **0.65 ms, 1.3 KB** | 1.11/4,286 vs 0.63/1.3 | Resolved |
| Edit → page image (real preview VM, 200 ms quiet window), busy / 80 notes | 13.5 / 35.4 ms median (n=20) | — | Resolved (no fixed 450 ms debounce) |
| Official POSTs, 7-day window | 21 → **9** | 21 → 9 | Resolved |
| Associate date load / Calendar month, 20,000 synced | 0.26 / 0.55 ms (UI thread) | 0.25–0.78 ms | Low impact; SQLite UI-thread work is not material |
| Price History product switch, 20,000 synced | **0.46 ms** (legacy path 68 ms) | 0.46 ms | Resolved |
| Price History open, 20,000 / 2,000 synced | 84 / 10 ms (UI thread) | 79.8 / ~10 ms | Present but low impact (once per open; 20,000 is extreme) |
| Print band loop, 600 DPI | 68–108 ms/page, **225 MB managed allocated/page** | 225 MB | Present, measured on Mac; Windows impact unverified |

**Areas without a benchmark this phase:**

| Area | Status | Evidence |
|---|---|---|
| Dashboard rendering, Calendar navigation, Operational Notes list, Shows expansion | Resolved / low impact | Phase 35G harness: notes selection under 2 ms; Shows expand plateaus after realization |
| Repeated keyboard focus | Unverified on Windows | About 28 KB retained per focus change in headless (Phase 35G); pre-existing on RC.13 |
| Repeated navigation and long-session retention | Unverified on Windows | +6 MB per 200 cycles after first realization in headless (Phase 35G) |
| Startup | Unverified on Windows | Mac about 200 ms in the startup timing test (Phase 34C); F16/F17 need Windows startup measurement |
| Native printing memory | Unverified on Windows | J4/J5 checklist items were never reported |

## 13. Resolved historical issues (Phase 34A register)

| 34A ID | Issue | Status now |
|---|---|---|
| F01, F02, F03 | Fixed 450 ms debounce; debounced date switch; Updating chip | **Resolved** (34B-1; confirmed in source and benchmark) |
| F04 | Cold first PDF | **Resolved** (34B-2 warm-up) |
| F05, F19 | Lato faces; unused `logo.png` | **Resolved** (34C) |
| F06 | SQLite on the UI thread | **No longer applicable** at measured sizes (0.26–0.85 ms) |
| F07, F09 | Duplicate POSTs; six duplicated Accesso helpers | **Resolved** (34B-2, 34C) |
| F11 | 4 MB LOH copy per render | **Resolved** (34B-2) |
| F15 | Price History repeated scan | **Resolved** (34B-2 reader) |
| F18, F22 | Dead members; 5.2 s busy-timeout test | **Resolved** (34C) |
| F08, F13, F20 | Sync sharing-violation retry; DPI change re-render; WAL | **Unverified hypotheses**; deferred |
| F10 | Associate size | Open as TD-05 (narrowed) |
| F12 | Print band allocations | Open as TD-12, Windows-gated |
| F14 | Superseded generations not cancelled | Present but low impact (≤ 50 ms background CPU); no action |
| F16, F17 | Eager workspace load; non-Windows backends in ZIP | **Unverified**; need Windows startup measurement |
| F21 | PDFium `SafeHandle` | Open as TD-14 (P3) |
| F23 | Browser-session seam | Intentionally retained; no action |

## 14. Prioritized technical-debt register

There are no P0 or P1 findings. Every item is **confirmed** unless marked *hypothesis* or *unverified*.

| ID | Pri | Subsystem | Source | Problem | Evidence | Impact | Sev / Likelihood | Impl. risk | Benefit | Recommended change | Tests | Windows validation | Depends on | Milestone |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| TD-01 | P2 | Tests | Ui.Tests run-alone partition | Real-window confinement is unenforced; new tests hide inside partials | §11.2; 34A saw 148 failures from mis-partition | Developer: silent flaky partitions | Med / Med | Low (test-only) | High | Source-scanning guard test limiting `Window` creation to the six classes | The guard itself plus a fixture proving it trips | None | — | 36B |
| TD-02 | P2 | Tests | `build-and-test` skill | Hang and 3 GB safeguards are manual; a mismatched watchdog ran unguarded | §11.2 | Developer: unbounded memory or hangs possible | Med / Low–Med | Low | Med | Checked-in partition runner applying the split, `--blame-hang` and an RSS cap (owner approval: new tooling) | Run the full suite through it; count equals baseline | Run once on Windows (PowerShell variant) | Owner decision | 36B |
| TD-03 | P2 | Tooling | `tools/visual-fixtures/Program.cs` | About 22 MB retained per capture; `*` unbounded | §11.2 bounded measurement; 35G 18 GB / 5.8 GB | Developer: memory exhaustion | High when misused / Low | Low | Med | Cap scenarios per process or auto-spawn per scenario | Bounded run before and after; capture bytes identical | None (headless tool) | — | 36B |
| TD-05 | P2 | Associate | `AssociateViewModel.cs:2188-2356` | ERT editor embedded in the 3,163-line view model | §5.2 | Developer: ERT changes touch the workspace class | Low / — | Medium (about 20 binding paths, 9+ test files) | Med | `ErtEditorViewModel` child with callbacks (PriceEditor pattern); no draft or autosave ownership | Existing ERT, sync-conflict and real-window ERT tests retargeted, not weakened; new child unit tests | ERT panel open, save and remove, keyboard and Narrator on Windows | — | 36C |
| TD-06 | P3 | Desktop | `AssociateViewModel.cs:960`, `OperationalNotesViewModel.cs:216`, `PersonalSyncSettingsViewModel.cs:167` | Direct `Dispatcher.UIThread.Post` bypasses `IUiDispatcher` | 34C §14.2 | Tests cannot schedule sync reloads deterministically | Low / — | Low | Low–Med | Route through the injected `IUiDispatcher` | Sync-reload tests on the fake dispatcher | None beyond 36C smoke | — | 36C |
| TD-07 | P3 | UI | `AssociateView.axaml:72-754` | 679 lines of local styles; 10 selectors also defined in shared styles | §6 | Style lookup harder to reason about | Low / — | Medium (visual risk) | Low | None now; consolidate only alongside an approved visual change | Visual fixtures before and after | DPI and contrast review | — | Defer |
| TD-08 | P3 | Tests | About 44 Ui.Tests files | Raw `.axaml` text assertions couple tests to file layout | §6 | Raises the cost of any view extraction | Low / — | — | Low | Keep; prefer realized-tree assertions for new tests | — | — | — | Defer |
| TD-09 | P3 | Personal Sync | `PersonalSyncService.cs` (2,061 lines) | Five parallel channel triads; the two ERT process methods are about 70 % identical | §8 | Maintenance only; no defect | Low / — | **High** | Low–Med | None now; see invariants in §8 | Golden multi-device corpus | Sync-folder (OneDrive) run | Owner approval | Defer |
| TD-10 | P3 | Composition | `App.axaml.cs:528-1085` | Long registration method | §7 | None demonstrated | Low / — | Low | Low | None | — | — | — | Cancel (36D) |
| TD-11 | P3 (*hypothesis*) | Retrieval | `App.axaml.cs:979` | Retrieval `HttpClient` has no `PooledConnectionLifetime` (updater sets 10 min) | §10 | Possible stale DNS in multi-day sessions | Low / Low | Low | Low | Only on evidence: a transport-only handler setting | Transport and coalescing tests | Long-session refresh | Evidence | Defer |
| TD-12 | P3 (*unverified on Windows*) | Printing | `WindowsPdfPrintService`, `PdfiumPdfRasterizer` | 225 MB managed allocation per 600 DPI page | §12 | Possible print memory spike | Low–Med / — | Low–Med | Unknown | 34B-2 §9.3 pooled band buffers, only after Windows J4/J5 | Print output byte/visual equality; band tests | **Required** (J4/J5 memory) | Windows data | 36E (gated) |
| TD-13 | P3 (*unverified*) | UI / memory | Headless focus and navigation retention | About 28 KB per focus change; +6 MB per 200 navigation cycles | 35G §8 | Unknown on Windows | Unknown | — | Unknown | Windows long-session observation first | — | **Required** (checklist 20) | Windows data | 36E (gated) |
| TD-14 | P3 | PDF | `PdfiumPdfRasterizer.cs:75-245` | No `SafeHandle` or finalizer backstop | 34A F21 | Defense in depth only; all paths dispose | Low / Very low | Low | Low | Optional `SafeHandle` | Disposal tests | None | — | Defer |
| TD-04 | P3 | Tooling | `tools/visual-fixtures/Program.cs:412` | CS0618 obsolete `Bitmap.Save` overload | Build output | Noise | Low | Low | Low | Use the `BitmapEncoderOptions` overload with TD-03 | Capture bytes identical | None | — | 36B |

## 15. Recommended Phase 36 roadmap

| Milestone | Recommendation | Scope | Reasoning |
|---|---|---|---|
| **36B** Test infrastructure reliability | **Proceed, narrowed** | TD-01 guard; TD-03 per-process cap; TD-04 warning; TD-02 runner only if the owner approves new tooling | Confirmed enforcement gaps, test or tool code only, zero production risk. Does not repeat the completed guards or partitions |
| **36C** Associate workspace decomposition | **Narrow** | TD-05 ERT editor child view model and TD-06 dispatcher seam. Nothing else | The only extraction with a clear boundary that cannot duplicate draft state. Optional: skip if no ERT work is planned |
| **36D** Composition root and shared UI organization | **Cancel** | — | No demonstrated difficulty (TD-10). Style consolidation (TD-07) needs a visual decision |
| **36E** Performance and resource hardening | **Defer** | TD-12, TD-13 and F16/F17 after the Windows J4/J5, startup and long-session measurements | No measured defect. Phase 34 resolved the measurable items, and rules require Windows profiling before Windows claims |
| **36F** Final architecture integration review | **Combine** | Each milestone closes with the full partitions, a clean build, guards, the phase-review skill and an architecture update | A separate milestone after one or two small changes duplicates their close-out |

Suggested order: 36B, then 36C. 36B first means the 36C real-window ERT tests land under the new confinement
guard.

## 16. Deferred or rejected proposals

- **Rejected:** section view models (Overview, Products, Shows, Notes) that each own draft fields. These break
  the one-draft, one-autosave invariant.
- **Rejected for now:** per-section `UserControl` extraction of `AssociateView.axaml`. It produces smaller files
  without lower coupling, and costs churn in 22 markup-test files, name-scope moves and visual risk (§5.3).
- **Rejected:** a different DI framework or a composition-root restructure (TD-10).
- **Rejected:** replacing QuestPDF or PDFium, adding another preview debounce, or broadening request coalescing.
- **Deferred:** Personal Sync channel consolidation (TD-09), sharing-violation retry (F08), WAL (F20),
  `HttpClient` lifetime (TD-11), print buffers (TD-12), Windows-only Avalonia backends (F17) and eager-load
  deferral (F16). All wait for evidence or approval.
- **No action:** superseded autosave `CancellationTokenSource` instances are replaced without `Dispose`
  (`AssociateViewModel.cs:2823-2826`). They are timer-free, so nothing unmanaged is retained. The preview's
  ≤ 50 ms superseded generation (F14) also needs no action.

## 17. Compatibility and rollback requirements

Every future Phase 36 change must:

- leave schema 18, the migrations, every sync and payload version, draft serialization, finalized snapshots and
  stored PDFs untouched;
- keep official retrieval and coalescing keys unchanged;
- keep PDF output byte-identical for the existing compatibility corpus;
- keep every binding, accessibility name and keyboard path.

Each milestone must be a single revertible commit series with no data migration, so rollback is `git revert`
with no user-data step. 36B is test and tool only. 36C changes binding paths only and must keep every existing
ERT test passing, retargeted but not weakened.

## 18. Windows verification requirements

- **This audit:** none required, none executed.
- **36B:** if TD-02 is approved, run the PowerShell runner once on Windows.
- **36C:** Windows smoke test of the ERT panel: open, monthly save, override save and remove, sync conflict
  message, keyboard order, Narrator names, 125 %/150 % DPI.
- **Before any 36E work:** Phase 34A checklist J4/J5 print memory, startup timing (F16/F17), and the
  long-session focus and navigation observation (35G checklist item 20).

## 19. Product specification review status

`AGENT_BUILD_SPEC.md` §21 (performance) was read in full, and the section list (§§6–7 retrieval, §10 Associate, §15 PDF, §16 Personal Sync, §19 storage) was checked for requirements these findings could affect. The audit changes no behavior
or requirement. §21 already requires separating development-host and Windows results, which this record
follows.

**Product specification:** Reviewed — no changes required

## 20. Architecture documentation review status

`docs/architecture.md` and the nine subsystem guides were checked against source. The following match current
source:

- the dependency table;
- the schema and sync constants;
- the preview scheduling description;
- the coalescing description;
- the lifecycle order;
- the six-class partition.

The overview's review baseline (`9351c64`, no tests run) and the testing guide's "RC.14 readiness reports 5,893"
are accurate dated statements. The current count (5,914) is recorded here and in the maintenance commit, per the
`build-and-test` rule that baselines live in phase records. The testing guide's "remaining concerns" are
consistent with this register. The diff adds this record and one `docs/README.md` index line; it contains no
architectural change.

**Architecture documentation:** Reviewed — no changes required

## 21. Outstanding evidence gaps

- There is no Windows evidence for printing memory, startup, long-session retention, DPI or Narrator behavior;
  every measurement here is from the Mac host.
- The per-item scope of the owner's Windows test of RC.13 and RC.14 is unknown (release records).
- I did not verify whether compiled bindings are the default.
- The intermittent Ui stall has not been reproduced (seen once, in 35E).
- Phase 34A's F08, F13 and F20 remain untested hypotheses.
- There is no standalone maintenance record; its evidence is the `7c2027e` commit message.
- LiveSmoke tests were not run (opt-in; not requested).

## 22. Final recommendation

No production refactoring is warranted to fix defects.

1. **Approve a narrowed 36B.** It is test and tool enforcement only: the real-window guard, the
   screenshot-harness cap and, if the owner agrees, a checked-in safeguarded test runner.
2. **Then optionally a narrowed 36C.** Extract the ERT editor as a child view model and finish the dispatcher
   seam.
3. **Cancel 36D.**
4. **Defer 36E** until the Windows measurements in §18 exist.
5. **Fold 36F** into each milestone's close-out.
