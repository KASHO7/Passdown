# Architecture — Phase 1 baseline, updated through Phase 4 (complete)

Established during Phase 1 (AGENT_BUILD_SPEC.md §17 Phase 1) and extended
through Phase 2 (§17 Phase 2), the full Phase 3 (§17 Phase 3 — retrieval
performance and consistency), and the full Phase 4 (§17 Phase 4 — Associate
workflow and visual system, closed out in the Phase 4G integrated review).
This records the boundaries and infrastructure the Associate workflow and
its PDF generation build on.

## Project boundaries and dependency direction

```
Domain            no project or third-party dependencies
  ^
Application       -> Domain                (use cases, abstractions/ports)
  ^
Infrastructure    -> Application, Domain   (SQLite, HTTP, Playwright adapters)
Documents         -> Application, Domain   (QuestPDF generation)
  ^
Desktop           -> Application, Infrastructure, Documents  (Avalonia MVVM, composition root)
```

This mirrors CLAUDE.md "Architecture" and spec §16 exactly: Domain stays
pure (no Avalonia/SQLite/HTTP/Playwright/QuestPDF/Windows references);
Infrastructure and Documents are siblings, both depending only on
Application/Domain, never on each other; Desktop is presentation-only and
composes everything else.

`tests/Carowinds.Passdown.Domain.Tests` includes a reflection-based guard
(`DependencyDirectionTests`) that fails the build if the compiled Domain
assembly ever references Avalonia, SQLite, Playwright, QuestPDF, HTTP, or
Windows APIs — enforced automatically rather than by convention alone.

Interfaces/abstractions for external systems (e.g. the `IOfficialDataSource`
shape sketched in spec §16) belong in Application; Infrastructure supplies
the concrete adapters. Phase 2 added the persistence side of this split —
`ISourceStateRepository`, `IDatedResultRepository`, `ICatalogProductRepository`,
`IProductHistoryRepository`, `IAssociateDraftRepository`, `IManagerDraftRepository`,
and `IUnitOfWork`/`IUnitOfWorkFactory` in Application, each with a SQLite
implementation in Infrastructure — plus `IClock`/`EasternTimeZone` for
deterministic, DST-safe time. Phase 3A added the retrieval side of this
split (`IOfficialDataSource<TPayload>` in Application; HTTP/Playwright
transport in Infrastructure) — see "Phase 3A additions" below. Concrete
Carowinds source adapters implementing that interface remain a later task.
`tests/Carowinds.Passdown.Application.Tests` includes a reflection-based
guard (`ApplicationDependencyDirectionTests`) mirroring Domain's, so
Application can't accidentally acquire a concrete Infrastructure dependency.

## Cross-cutting infrastructure established in Phase 1

- **Configuration**: `Microsoft.Extensions.Configuration`, bound from
  `appsettings.json` via the Options pattern
  (`Carowinds.Passdown.Infrastructure.Storage.StorageOptions` is the first,
  minimal example). Freshness-cadence configuration (spec §6.1) is added in
  Phase 2 the same way — as configuration, not scattered comparisons.
- **Logging**: `Microsoft.Extensions.Logging`, console provider wired in
  Desktop's composition root (`App.axaml.cs`). A file sink under
  `AppPaths.LocalApplicationDataDirectory` can be added later without
  redesigning call sites, since everything logs through `ILogger<T>`.
- **Local storage location**: `Carowinds.Passdown.Infrastructure.AppPaths`
  resolves the per-user local application-data directory (spec §3).
- **Storage/migrations**: `Carowinds.Passdown.Infrastructure.Storage`
  contains a SQLite connection factory
  (`SqliteConnectionFactory`, creates the directory/file on first use) and
  a versioned migration runner (`MigrationRunner`). Applied version is
  tracked via `PRAGMA user_version`; a `schema_version` table records
  human-readable migration history for diagnostics. Migrations implement
  `IMigration` and are applied in ascending version order, exactly once.
  Phase 1 shipped `Migration0001CreateSchemaVersionTable` (diagnostic
  history table only). Phase 2 added six more, per spec §6.2's separate
  dated-results/catalog-results/source-state/drafts model:
  `Migration0002CreateSourceStateTable`, `Migration0003CreateDatedResultsTable`,
  `Migration0004CreateCatalogProductsTable`, `Migration0005CreateProductHistoryTable`,
  `Migration0006CreateAssociateDraftsTable`, `Migration0007CreateManagerDraftsTable`.
  `SqliteUnitOfWork`/`SqliteUnitOfWorkFactory` let a catalog-row upsert and its
  history append commit as one transaction, for callers that need that (spec §7.3).
- **Composition root**: `Carowinds.Passdown.Desktop.App` builds a single
  `IServiceProvider` in `Initialize()`, runs the migration runner before
  resolving the main window in `OnFrameworkInitializationCompleted()`, and
  resolves `MainWindow`/`MainViewModel` through DI. Later phases register
  their services here rather than introducing a second composition
  mechanism.

## Phase 2 additions

Phase 2 built the domain/persistence/reconciliation backbone without wiring
any of it into the running app (Desktop only gained the migration-list
registration needed to keep applying schema changes at startup — no
ViewModel or screen touches these services yet):

- **Domain** (`Carowinds.Passdown.Domain`): `SourceCategory`; `Retrieval/`
  (`RetrievalStatus`, `RetrievalStrategy`, `SourceEvidence`, `DatedResultKey`,
  `LastKnownGood<T>`, `SourceState`, `FreshnessCadence`, `FreshnessPolicy`);
  `Catalog/` (`CatalogProductKey`, `ProductLifecycleState`, `CatalogProductLifecycle`).
- **Application** (`Carowinds.Passdown.Application`): `Persistence/` (the
  repository ports plus `IUnitOfWork`); `Time/` (`IClock`, `EasternTimeZone`);
  `Freshness/` (`FreshnessOptions`, `FreshnessPolicyCatalog`, `IFreshnessPlanner`,
  `IRollingDatePlanner`, `DatedResultProjectionExtensions`); `Catalog/`
  (`IProductReconciliationService`, `ObservedProduct`).
- **Infrastructure** (`Carowinds.Passdown.Infrastructure`): migrations 0002–0007;
  one SQLite repository per Application port; `SqliteUnitOfWork(Factory)`;
  `SqliteConvert` (consistent UTC-ISO8601/date-only text encoding); `SystemClock`.

Live retrieval (HTTP/Playwright source adapters), product-catalog UI, and PDF
generation remain out of scope — those are Phase 3+.

## Phase 3A additions

Phase 3A built the shared retrieval contracts and transport that concrete
source adapters will implement against, without implementing any adapter or
wiring anything into the running app:

- **Application** (`Carowinds.Passdown.Application.Retrieval`):
  `RetrievalRequest` (source id, category, optional date — mirrors
  `SourceStateRecord`'s choice to carry a source id as a persistence/adapter
  concern the Domain type doesn't); `SourceResult<TPayload>` (generic so the
  contract stays adapter-neutral while each adapter still gets a
  compile-time-typed payload; its `Success`/`WithoutPayload` factories
  enforce spec §5.2 in the type system — a payload can only exist alongside
  `Verified` evidence, never as a substitute for `Closed`/`NotPublished`/
  `Unavailable`/`Error`); `IOfficialDataSource<TPayload>` (spec §16's
  suggested shape — deliberately omits a `Freshness` member, since cadence
  is already owned per-category by Phase 2's `FreshnessPolicyCatalog`/
  `IFreshnessPlanner` keyed off `SourceId`, and restating it here would
  duplicate that state); `RetrievalDiagnostics.Error(...)` (builds
  consistent `Error` `SourceEvidence` from an exception, keeping the
  caller-supplied user-facing `Detail` separate from the exception's full
  `TechnicalDetail`, which is logs-only).
- **Infrastructure** (`Carowinds.Passdown.Infrastructure.Retrieval`):
  `Http/` — `IRetrievalHttpClient`/`RetrievalHttpClient`, a shared-`HttpClient`
  wrapper with its own per-call timeout (via a linked `CancellationTokenSource`,
  so a timeout throws `TimeoutException` while caller cancellation throws
  `OperationCanceledException`) and zero source-specific parsing — it
  returns a raw status/content-type/body. `Browser/` —
  `IBrowserSession`/`IBrowserSessionFactory` plus a Playwright-backed
  implementation: one browser and one shared context per refresh job (spec
  §6.4), pages opened per caller, context-level blocking of image/media/font
  requests, and cancellation checked before each launch step so a cancelled
  job never spawns a browser it would just have to tear down. Analytics
  blocking is deferred — it needs an observed domain blocklist this phase
  doesn't have evidence for.
- **Tests**: `tests/Carowinds.Passdown.Infrastructure.Tests/TestSupport/FixtureFile`
  resolves and reads committed fixtures under `fixtures/` by walking up from
  the test assembly's output directory to `CarowindsPassdown.slnx`, so
  fixture-backed contract tests work the same from `dotnet test` and an IDE
  runner. `TestSupport/TestCategories.LiveSmoke` is the
  `[Trait("Category", ...)]` value opt-in live checks use — excluded by
  `dotnet test --filter Category!=LiveSmoke` (see the `build-and-test`
  skill); two such tests exist so far (a real GET against the official
  park-hours page, and a real Chromium launch/page/dispose cycle that
  additionally requires `playwright install chromium` to have been run).

Live retrieval for any *specific* Carowinds/Six Flags product (park hours,
dated tickets, Fast Lane, haunted attractions, passes, add-ons, promotions),
refresh-job orchestration across multiple due sources, and wiring any of
this into the composition root remained out of scope for Phase 3A — all of
it was completed in Phase 3B–3H, described below.

## Phase 3B–3G additions — concrete source adapters and shared parsing

Eight concrete `IOfficialDataSource<TPayload>` adapters were added under
`Carowinds.Passdown.Infrastructure.Retrieval.Sources`, all `DirectApi`/`Html`
strategy (no Playwright launch for any currently-implemented production
source — verified both by code review and by an empirical process check
during a live Phase 3H run):

- **Dated** (Phase 3C–3E; `SourceScope.Dated`, keyed by requested date):
  `ParkHoursSource`, `DatedAdmissionSource`, `DatedFastLaneSource`,
  `DatedHauntedSource`. Each confirms the exact requested date from the
  response before ever returning `Verified` — enforced redundantly at the
  Domain layer too (`SourceEvidence`'s constructor throws if a `Verified`
  result's confirmed date doesn't match its requested date, spec §5.4).
  Documented per-source in `docs/retrieval/park-hours-source.md`,
  `dated-admission-source.md`, `dated-fast-lane-source.md`,
  `dated-haunted-source.md`.
- **Catalog** (Phase 3F1–3F6; `SourceScope.Catalog`, keyed by normalized
  product): `SeasonPassCatalogSource`, `SeasonPassAddOnsCatalogSource`,
  `PromotionsCatalogSource`, `GeneralAddOnsCatalogSource`. Feed
  `Application.Catalog.IProductReconciliationService` (Phase 2) via
  `ObservedProduct`/`CatalogPayload`. Documented in
  `docs/retrieval/catalog-source-inventory.md`, including the live
  Phase 3F4/3F5 addendum that disproved cross-page Sanity-id stability —
  `CatalogProductKey` scopes identity per `SourceCategory` so this can't
  cause a collision, and `ProductReconciliationService` additionally scopes
  its missing-product/confirmed-absence pass to the *reconciling* source
  (`evidence.SourceName`), not every source sharing a category, since
  `SourceCategory.AddOns` is deliberately shared by two independent sources
  (general add-ons and season-pass add-ons).
- **Shared parsing** (`Infrastructure.Retrieval.Sources`): `AccessoKeywordCalendar`
  (the accesso ticketing keyword-calendar JSON shape the three ticket-dated
  adapters share) and `ProductCardHtmlParser`/`CatalogCardMapper` (the
  `productv2-card` markup shape shared by season passes, season-pass
  add-ons, promotions, and the daily-tickets/Fast-Lane marketing pages —
  proven shared only after independent confirmation on 6+ live page
  fetches). `GeneralAddOnHtmlParser` is a separate, narrower parser for the
  one page (`add-ons`) that does *not* use that markup shape — deliberately
  not forced into the shared parser on single-page evidence.

## Phase 3G–3H additions — refresh orchestration and composition root

- **Application** (`Carowinds.Passdown.Application.Refresh`):
  `IRefreshOrchestrator`/`RefreshOrchestrator` — plans only due sources
  (`IFreshnessPlanner`), expands dated sources across the rolling window
  today → tomorrow → remaining days (`IRollingDatePlanner`, spec §6.5),
  and runs planned operations under a configurable `MaxConcurrency`
  (`RefreshOrchestratorOptions`, a bounded `SemaphoreSlim`, spec §6.4).
  `RunManualDateRefreshAsync` ("Check this date online", spec §6.3) is a
  separate entry point that deliberately skips freshness gating and
  `source_state` bookkeeping. Every operation persists independently as it
  completes (`DatedSourceRegistration`/`CatalogSourceRegistration`); a
  cancelled or failed operation can never discard or overwrite an earlier
  success (spec §6.7), and a catalog `Error` never reaches reconciliation.
  A job-scoped Playwright browser (`IJobBrowserSessionFactory`/
  `PlaywrightJobBrowserSession`, Infrastructure) starts at most once, lazily,
  only if a due source actually requires it — currently dead code in
  practice, since none of the eight adapters above does.
- **Desktop** (`App.axaml.cs`, `AddRefreshOrchestration`): composition-root
  DI wiring only — registers Phase 2 persistence/freshness services, Phase
  3A transport, all eight adapters, and `IRefreshOrchestrator`. No
  ViewModel/View references any of it yet; `CompositionRootTests`
  (`Carowinds.Passdown.Ui.Tests`) resolves the full graph offline to catch
  a broken registration without needing a live smoke test.

## Phase 4 additions — Associate workflow, visual system, and PDF generation

Phase 4A–4F built the complete Associate Passdown workflow end to end, closed
out by a Phase 4G integrated audit (regression pass, architecture-boundary
check, documentation cleanup — no functional changes beyond stale-comment
fixes):

- **Application** (`Carowinds.Passdown.Application.Associate`, Phase 4A):
  `IAssociatePassdownService`/`AssociatePassdownService` — the one Avalonia-
  free workflow surface Desktop binds to. `LoadAssociateDateAsync` composes a
  date's `AssociateWorkspace` from whatever is already cached (draft +
  dated/catalog repositories) and never calls `IRefreshOrchestrator`;
  `CheckAssociateDateOnlineAsync` is the sole explicit-refresh path (spec
  §6.3); `SaveDraftAsync` is the sole draft-persistence path. Composition
  re-derives effective park hours via the Domain's `ParkHoursCalculator` and
  projects official dated/catalog products plus draft-local custom products
  into one `AssociateProduct` list, keyed by a stable `AssociateProductIdentity`
  so include/exclude and display-price overrides survive a catalog refresh.
- **Desktop** (Phase 4B–4D, Phase 4F): `AssociateViewModel` — the only
  ViewModel the screen has — depends solely on `IAssociatePassdownService`,
  `IClock`, and the PDF export/save-file abstractions below; it never touches
  a repository, HTTP, Playwright, or SQL directly, and never re-derives the
  North Gate (-30 min)/Avenue (-45 min) offset math itself (always re-runs
  `ParkHoursCalculator.Calculate`). Adds debounced autosave (serialized by a
  semaphore so an in-flight save and a newer edit can never race), explicit
  flush points before a date switch/Check Online/Create PDF, editable
  product sections and custom-product rows, and the blue/white/red
  "Carowinds Admissions Operations" brand retheme (`App.axaml` resource
  tokens, `MainWindow.axaml`/`AssociateView.axaml`). `MainViewModel` hosts
  `AssociateViewModel` directly — no navigation/mode-selection state and no
  Manager placeholder (CLAUDE.md "current scope decision").
- **Documents** (`Carowinds.Passdown.Documents.Associate`, Phase 4E–4F):
  `AssociatePassdownDocumentModelBuilder` turns a composed `AssociateWorkspace`
  into a presentation-only `AssociatePassdownDocumentModel` (formatting only —
  no business/hours recalculation); `AssociatePassdownPreflight` validates
  before generation; `AssociatePassdownPdfGenerator` (QuestPDF) renders it,
  matching the Desktop shell's brand palette, with a labeled `Supporting
  Details` continuation page for unusually busy days (spec §11.4) rather than
  silent truncation. `IAssociatePassdownPdfExportService` is the Documents-side
  export port; `IPdfSaveFilePicker`/`AvaloniaPdfSaveFilePicker` (Desktop) is
  the native save-dialog adapter — Documents has no Desktop or Avalonia
  reference and performs no repository/network access.
- **Tests**: full contract coverage across all four layers — local-only date
  load, explicit Check Online, partial-refresh preservation, autosave/
  debounce/edit-during-save/flush-on-switch, product include/exclude and
  price-override/reset, custom-item persistence, JSON draft round-trip,
  category/status presentation, PDF content/page-count/overflow, PDF export
  picker cancel/success/failure, and accessibility-name assertions.

Windows 11 manual validation (launch, layout, resize, display scaling,
keyboard interaction, native PDF save dialog, generated PDF readability) was
completed externally by the product owner on a Windows VM — not something
Claude performed directly.

## Phase 6A additions — unattended scheduled-refresh entry point

Phase 6A added a second, headless startup path onto the same executable and
the same composition root — no second scheduler, no second refresh
implementation, no second DI setup:

```
Carowinds.Passdown.Desktop.exe                      normal desktop launch (unchanged)
Carowinds.Passdown.Desktop.exe --scheduled-refresh   headless unattended refresh, then exit
```

- **`Program.Main`** (`src/Carowinds.Passdown.Desktop/Program.cs`) parses `args`
  via `CommandLineArguments.Parse` (`Desktop.ScheduledRefresh`) before anything
  Avalonia-specific runs. No argument → `StartupMode.Normal` →
  `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`, exactly as
  before. `--scheduled-refresh` (exactly one argument, exact match) →
  `StartupMode.ScheduledRefresh` → the headless path below; Avalonia is never
  initialized on this branch. Anything else → `StartupMode.Invalid`, printed
  to stderr, exit code 3 — an unrecognized argument never silently falls
  through to either real mode.
- **Composition stays single-source**: the headless path calls the exact same
  `App.BuildServiceProvider()` (`App.axaml.cs`) the normal launch and
  `CompositionRootTests` already use — no parallel registration method.
  `IRefreshOrchestrator` and its Phase 3 dependency graph, `MigrationRunner`,
  `AppPaths`, and configuration are resolved identically either way.
  `MainWindow`/`MainViewModel`/`AssociateViewModel` remain registered (as
  before) but the headless path never asks the container for any of them —
  registering a transient does no work until something resolves it, so this
  costs nothing and keeps one composition root rather than splitting UI vs.
  headless registrations.
- **`Carowinds.Passdown.Application.Refresh.IScheduledRefreshRunner`/`ScheduledRefreshRunner`**
  (Application layer — it has no Avalonia/SQLite/HTTP/Playwright dependency,
  only `IRefreshOrchestrator`, `IClock`, and `ILogger`): logs a start line,
  calls `IRefreshOrchestrator.RunScheduledRefreshAsync` — never
  `RunManualDateRefreshAsync` — logs a summary/per-failure line, and
  classifies the result as `ScheduledRefreshOutcome.Success` or
  `PartialFailure` from `RefreshSummary.Failed` alone. It owns none of the
  freshness/persistence/retry/partial-failure logic already in
  `RefreshOrchestrator` — this is a thin logging/classification wrapper, not
  a second orchestrator. `Unavailable`/`NotPublished`/`Closed` never count as
  a failure here because they already fold into
  `SourceOperationOutcomeKind.Success` upstream (spec §5.2) — only a genuine
  `Failure` outcome can move this past `Success`.
- **`Carowinds.Passdown.Desktop.ScheduledRefresh.ScheduledRefreshHost`**: the
  headless host. Takes a `Func<IServiceProvider>` (testable without a real
  process — a test can substitute a minimal container or a throwing factory).
  Builds the provider, runs `MigrationRunner.Run()` (spec §17 Phase 1's
  "storage ready before anything touches the database", same ordering
  `OnFrameworkInitializationCompleted` already uses for normal mode),
  resolves `IScheduledRefreshRunner`, awaits it, and maps the outcome to a
  `ScheduledRefreshExitCode`. Any exception from provider construction,
  migration, or the runner — including a caller-requested cancellation — is
  caught and reported as `FatalFailure`, deterministically; nothing throws
  out of `Main`.
- **Exit codes** (`ScheduledRefreshExitCode`): `0` Success, `1`
  PartialFailure (one or more source operations failed, but the job ran to
  completion), `2` FatalFailure (startup/configuration/database/unhandled
  failure, or the run was cancelled before producing a summary), `3`
  InvalidUsage (unrecognized command-line arguments).
- **Logging**: unattended runs get everything the normal launch's console
  provider already logs, plus a second sink —
  `Carowinds.Passdown.Infrastructure.Logging.FileLoggerProvider` — added onto
  the built `ILoggerFactory` only on the headless path (`Program.cs`), since
  a scheduled run has no terminal to read console output from. One UTF-8
  append-only file per UTC day under
  `AppPaths.LocalApplicationDataDirectory/logs/scheduled-refresh-yyyyMMdd.log`;
  files older than `FileLoggerOptions.RetainedDays` (default 14) are pruned
  on first write each run, bounding growth without a third-party logging
  package. Construction does no I/O — the directory/file are created lazily
  on first log call, mirroring `SqliteConnectionFactory`'s same "no I/O until
  first use" contract so building the provider stays side-effect-free in
  tests.
- **Tests**: `Carowinds.Passdown.Application.Tests/Refresh/ScheduledRefreshRunnerTests.cs`
  (runner classification/logging, offline); `Carowinds.Passdown.Ui.Tests/ScheduledRefresh/`
  (`CommandLineArgumentsTests`, `ScheduledRefreshHostTests` — the latter
  using small disposable temp-file SQLite databases and a fake
  `IScheduledRefreshRunner`, never the real network or the real per-user
  AppPaths database); `Carowinds.Passdown.Infrastructure.Tests/Logging/FileLoggerProviderTests.cs`.
  `CompositionRootTests` gained one test confirming the normal launch path
  still resolves `MigrationRunner`/`MainViewModel` unchanged (stops short of
  constructing `MainWindow` itself, for the same headless-dispatcher-thread
  reason `AssociateViewTests` already documents).

### Deferred out of Phase 6A

- **Windows Task Scheduler registration** — creating the per-user scheduled
  task itself is Phase 6C. Phase 6A only makes `--scheduled-refresh` a valid,
  correct, testable thing for a task (or a person) to invoke; nothing here
  registers one.
- **Missed-run-on-launch / offline-retry hardening** (spec §6.6's "if the
  computer is asleep or off at 6 AM, refresh next time the app opens") is
  Phase 6B. `IRefreshOrchestrator`'s existing due-source planning already
  makes a late run harmless — a source just stays due until something
  actually calls `RunScheduledRefreshAsync` again — but no explicit
  "catch up on launch" trigger exists yet.
- **Single-instance/overlap protection**: nothing currently stops two
  `--scheduled-refresh` processes (or a scheduled run and an interactive
  Check Online) from running concurrently. `source_state` and dated/catalog
  writes are per-row upserts, not additive, so a genuine race would mean
  "last write wins" rather than corruption, but this hasn't been stress-
  tested under real concurrency. A mutex/lock-file guard is a Phase 6B
  concern (spec §17 Phase 6 "test offline and failure scenarios").

## Phase 6B additions — overlap protection, missed-run catch-up, and offline hardening

Phase 6B hardens the Phase 6A `--scheduled-refresh` path in place — no second
scheduler, no second refresh implementation, no second DI setup. Every
addition below either guards the existing `RefreshOrchestrator`/
`IScheduledRefreshRunner` pipeline from running twice at once, or proves (by
test, not new logic) that it already does the right thing when invoked late
or offline.

- **Overlap protection** (`Carowinds.Passdown.Infrastructure.Concurrency`):
  `IScheduledRefreshLock`/`FileScheduledRefreshLock` — one exclusively-held
  local file (`AppPaths.LocalApplicationDataDirectory/scheduled-refresh.lock`,
  `FileScheduledRefreshLock.DefaultPath`) opened with `FileShare.None`. A
  second attempt to open the same path fails with `IOException` (or, rarely,
  `UnauthorizedAccessException`) on both Windows and the Unix hosts this was
  developed/tested on — `TryAcquire()` turns either into a clean
  `false` rather than a crash. This was chosen over a Windows named `Mutex`
  because .NET's named-mutex/semaphore support on non-Windows hosts (this
  project's Fedora dev host among them) has historically been the less
  portable of the two, while a shared-file exclusive-open check needs nothing
  OS-specific and was verified empirically here, including a real two-process
  test (one process holding the file lock while a second, real
  `--scheduled-refresh` process was launched against it) — see "Manual smoke"
  below. `ScheduledRefreshHost` acquires this lock (via a factory,
  `Program.cs` supplies the real one) *before* running migrations or the
  refresh job; a failed acquire never touches the database and is reported
  as **`ScheduledRefreshExitCode.Overlap` (exit code 4)** — a distinct,
  nonfatal outcome, deliberately never folded into `PartialFailure` (a source
  failure) or `FatalFailure` (a startup failure), since neither happened. The
  lock is released (`Dispose()`) in a `finally` block covering every path:
  clean completion, partial failure, cancellation, or an unhandled exception.
  Normal interactive desktop launch never acquires this lock synchronously at
  startup — nothing about opening the window can be blocked by it — the only
  place normal mode touches the lock at all is the best-effort background
  catch-up below, which never blocks the window either.
- **Missed-run/late-invocation catch-up** (spec §6.6, Part B): required no
  new logic. `FreshnessPolicy.IsDue`/`IRefreshOrchestrator`'s existing
  due-source planning (Phase 2/3G) already computes "is this source due"
  from the last recorded success and the current instant — never from
  whatever time the process happens to be invoked — so a process that runs
  at 8:30 AM because the machine was asleep at 6:00 AM already refreshes
  exactly the sources that are due and nothing else. This is proved, not
  reimplemented, by
  `tests/Carowinds.Passdown.Application.Tests/Refresh/ScheduledRefreshLateInvocationTests.cs`:
  a daily source with no run yet today stays due at 8:30 AM and at 3:00 PM
  alike; a weekly (season passes) or biweekly (add-ons) catalog that is
  already fresh stays skipped regardless of what time the late invocation
  happens; and a full success→fail→fail→recovery sequence confirms
  last-known-good survives the failures and only a new success both replaces
  it and resets the failure count (Part K).
- **Interactive startup catch-up** (`Carowinds.Passdown.Desktop.Startup.StartupCatchUpRefresh`,
  Part C): spec §6.6 explicitly asks for "refresh the next time the app
  opens if that day's job has not successfully run," so
  `App.OnFrameworkInitializationCompleted` fires this as a **detached,
  never-awaited background task** immediately after `desktop.MainWindow` is
  assigned — a slow or failing refresh can never delay the window opening.
  It acquires the *same* `FileScheduledRefreshLock` a `--scheduled-refresh`
  process would (so an already-running scheduled process and an interactive
  catch-up can never run at once — whichever gets there first wins, the
  other silently skips) and, once acquired, calls the *same*
  `IScheduledRefreshRunner`/`IRefreshOrchestrator` a scheduled process calls —
  not a second implementation. Every source it covers is still gated by the
  same due-ness check, so this never refreshes merely because the app
  opened, and never because the selected date changed (it has no notion of
  the selected date at all). Any exception — lock acquisition, resolution,
  or the runner itself — is caught and logged inside `StartupCatchUpRefresh`
  and never reaches the caller, so a failure here cannot prevent the UI from
  opening or affect already-cached data. Logged via the normal
  `ILoggerFactory` (console in interactive mode) rather than the scheduled
  path's `FileLoggerProvider` — the authoritative, file-logged audit trail
  for scheduled work remains `--scheduled-refresh` itself; this is a
  best-effort supplement, not a second official log.
- **Offline/source-failure hardening** (Parts D/E/G): also required no new
  logic, only tests proving existing behavior. `RefreshOrchestrator` already
  persists every operation independently as it completes, never lets one
  source's failure (including an unexpected `throw`, not just an `Error`
  result) cancel another's success, and never lets an `Error` overwrite a
  prior last-known-good value (Phase 3G's `RefreshOrchestratorTests`, plus
  Phase 6B's `ScheduledRefreshRunnerTests.RunAsync_AllOperationsFail_...`
  and `ScheduledRefreshHostTests.RunAsync_EveryOperationFailed_...`, which
  confirm that even when *every* planned operation fails — the "fully
  offline" case — the process still reports `PartialFailure`, never
  `FatalFailure`; that code is reserved for a genuine local
  startup/database/unhandled failure, proved separately by
  `RunAsync_DatabaseDirectoryCannotBeCreated_...`).
- **Retry policy** (Part F): deliberately left alone. All eight current
  source adapters are `DirectApi`/`Html` (no Playwright adapter exists yet,
  so spec §6.4's "bounded retries with jitter for transient
  navigation/hydration failures" has nothing to attach to today), and the
  three ticket-dated adapters call Accesso's keyword-calendar endpoint via
  `POST` — its retry-safety under an automatic retry has not been reviewed
  here, so Part F's explicit conservatism ("never automatically retry ...
  arbitrary POST operations if repetition could have side effects") applies.
  A failed attempt already preserves last-known-good and is retried
  automatically by the very next due scheduled run (daily cadence) or an
  explicit "Check this date online," so the absence of retry costs at most
  one day's staleness, not data loss. Adding retry is deferred, not
  forgotten.
- **SQLite busy-timeout** (`SqliteConnectionFactory`, Part H): both
  `OpenConnection`/`OpenConnectionAsync` now set `Default Timeout=5` on the
  connection string, so a short write-write collision between the
  interactive UI's autosave and a scheduled/startup-catch-up refresh makes
  `Microsoft.Data.Sqlite` retry internally for up to 5 seconds instead of
  raising `SQLITE_BUSY` the instant two writers overlap by a few
  milliseconds. (Empirically, the driver's own default — 60 seconds — was
  already enough to make `SqliteConnectionFactoryBusyTimeoutTests` pass even
  before this change; 5 seconds is a deliberately shorter, explicit,
  documented value rather than relying on an implicit default, and still
  comfortably covers this app's short operations.) No other concurrency
  change was made — operations are already short-lived, and per-row
  upserts mean draft writes and refresh writes were already not corrupting
  each other; this is not a redesign of storage.
- **Cancellation** (Part I): `Program.cs`'s headless entry point now wires
  `Console.CancelKeyPress` to the same `CancellationToken` already threaded
  through `ScheduledRefreshHost.RunAsync`/`IRefreshOrchestrator`, so Ctrl+C
  during a development/console invocation stops outstanding work
  cooperatively instead of the process being killed mid-write. The
  cancelled-run exit code (`FatalFailure`) and log line
  ("Scheduled refresh was cancelled before completion.") are unchanged from
  Phase 6A — still deterministic, still distinct in the log from a genuine
  fatal error's `LogCritical` line, and Phase 3G's cancellation tests already
  prove completed operations survive a mid-job cancellation.
- **Logging** (Part J): the operational log now clearly distinguishes
  SUCCESS/PARTIAL/OVERLAP/FATAL/CANCELLED outcomes (see the exit-code list
  below) — the OVERLAP line was added in this phase; the others already
  existed. `FileLoggerProvider`'s best-effort pruning now also tolerates
  `UnauthorizedAccessException` (previously only `IOException`), covering a
  transient AV-scan/permission hold on an old log file without aborting the
  log write that triggered pruning or the refresh it belongs to.
- **Process exit codes** (`ScheduledRefreshExitCode`): `0` Success, `1`
  PartialFailure, `2` FatalFailure, `3` InvalidUsage (all unchanged from
  Phase 6A), plus the new `4` **Overlap** — a second overlapping invocation
  was skipped; never touched migrations or the refresh job.
- **Tests**: `tests/Carowinds.Passdown.Infrastructure.Tests/Concurrency/FileScheduledRefreshLockTests.cs`
  (acquire/contend/release, offline, no real process needed);
  `tests/Carowinds.Passdown.Infrastructure.Tests/Storage/SqliteConnectionFactoryBusyTimeoutTests.cs`;
  a `FileLoggerProviderTests` addition for the broadened prune-failure catch
  (Unix-permission-based, skipped on Windows where the same simulation isn't
  meaningful — the underlying catch clause is platform-agnostic);
  `ScheduledRefreshHostTests` additions for overlap/fatal-DB-open/lock-release
  scenarios (`Carowinds.Passdown.Ui.Tests`, extending the existing Phase 6A
  file rather than duplicating its harness);
  `tests/Carowinds.Passdown.Ui.Tests/Startup/StartupCatchUpRefreshTests.cs`;
  and `tests/Carowinds.Passdown.Application.Tests/Refresh/ScheduledRefreshLateInvocationTests.cs`
  plus one addition to the existing `ScheduledRefreshRunnerTests.cs`. Every
  pre-existing Phase 6A test still passes unchanged — the new
  `scheduledRefreshLockFactory` constructor parameter on `ScheduledRefreshHost`
  defaults to `null` ("no overlap protection"), which is exactly what every
  pre-6B test still exercises.

### Deferred out of Phase 6B

- **Windows Task Scheduler registration** — still Phase 6C, unchanged from
  Phase 6A's own deferral. Nothing in this codebase creates, queries, or
  assumes a Task Scheduler entry exists; "run as soon as possible after a
  missed start" is a Task Scheduler *setting* 6C will configure, not
  something Phase 6B reimplements at the application level (this phase's
  due-source planning already makes a late run correct regardless of *why*
  it's late).
- **Automatic retry policy** — reviewed (see above) and deliberately not
  added; revisit only alongside a specific adapter whose failure mode and
  request idempotency have been reviewed.
- **Manager workflow** — out of scope per CLAUDE.md's current scope
  decision, unaffected by this phase.

## Phase 6C additions — Windows Task Scheduler integration

Phase 6C adds the operating-system trigger for the existing `--scheduled-refresh` command — no new
refresh/freshness/overlap logic. Full design rationale, install/uninstall commands, and the manual
Windows VM validation checklist live in `docs/scheduled-refresh-task-scheduler.md`; this section
only records what was added and where.

- **`Carowinds.Passdown.Desktop.ScheduledRefresh.ScheduledTaskPlan`/`ScheduledTaskDefinition`**
  (`src/Carowinds.Passdown.Desktop/ScheduledRefresh/ScheduledTaskPlan.cs`): a pure, Windows-API-free
  description of the task's action fields (stable `TaskName`, the executable path a caller
  supplies, the fixed `CommandLineArguments.ScheduledRefreshFlag` argument — reused, not
  duplicated — and a working directory derived from the executable path). Exists solely so the
  task-definition rules Part J asks to keep testable (stable name, exact argument, absolute-path
  validation, safe quoting for a path containing spaces, no hard-coded user path) are covered by
  ordinary cross-platform unit tests (`ScheduledTaskPlanTests`,
  `tests/Carowinds.Passdown.Ui.Tests/ScheduledRefresh/`) rather than requiring a Windows host to
  exercise. Deliberately avoids `System.IO.Path.IsPathRooted`/`GetDirectoryName` for its own
  validation/splitting, since those only recognize the *host* platform's path conventions — this
  runs the same on the Fedora dev host and on Windows.
- **`packaging/windows/Install-ScheduledRefresh.ps1` / `Uninstall-ScheduledRefresh.ps1`**: the only
  things that actually call the Windows Task Scheduler APIs (`Register-ScheduledTask`/
  `Unregister-ScheduledTask`). Register a per-user task (`LogonType Interactive`, `RunLevel
  Limited` — no administrator rights, no stored password, never `SYSTEM`) named `Carowinds Passdown
  Daily Refresh`, whose action is the caller-supplied executable path plus
  `--scheduled-refresh`, triggered daily at a configurable local time (default 06:00). Install is
  idempotent (`Register-ScheduledTask -Force` replaces only that exact task name); uninstall
  targets only that name and never touches application data/cache/logs. Task Scheduler's own
  `-MultipleInstances IgnoreNew` and `-StartWhenAvailable` settings are configured as a first line
  of defense; the Phase 6B `FileScheduledRefreshLock` remains the authoritative overlap guard and
  is untouched by this phase.
- **Tests**: `ScheduledTaskPlanTests` (12 cases — task-name stability, argument correctness against
  the shared `CommandLineArguments.ScheduledRefreshFlag` constant, path-with-spaces quoting,
  rejection of null/blank/relative executable paths, working-directory derivation for both
  Windows-drive-rooted and UNC paths, and that repeated calls are deterministic). All existing
  Phase 6A/6B tests are unaffected — this phase added no changes to `ScheduledRefreshHost`,
  `ScheduledRefreshRunner`, or `FileScheduledRefreshLock`.

### Deferred out of Phase 6C

- **Real Windows Task Scheduler registration/execution** — the PowerShell scripts were reviewed but
  not run against a live Windows Task Scheduler by Claude; see `docs/scheduled-refresh-task-scheduler.md`'s
  manual VM validation checklist. This was subsequently executed and closed out in Phase 6D — see
  below.
- **Phase 7 packaging** — nothing here publishes a self-contained Windows build or a release ZIP;
  Phase 7 is expected to call `Install-ScheduledRefresh.ps1` with the packaged installation's path,
  unchanged.
- **Manager workflow** — out of scope per CLAUDE.md's current scope decision, unaffected by this
  phase.

## Phase 6D — final Phase 6 audit and completion checkpoint

Phase 6D was a review-only pass over Phase 6A–6C (no new scheduling/resilience logic; CLAUDE.md
"do not redesign working behavior merely for preference"). It re-read the full unattended-refresh
path end to end (`Program.cs` → `ScheduledRefreshHost` → `ScheduledRefreshRunner` →
`RefreshOrchestrator` → persistence/logging → exit code), the normal interactive-startup path
(`App.axaml.cs` → `StartupCatchUpRefresh`), the Task Scheduler integration
(`ScheduledTaskPlan`, `Install-/Uninstall-ScheduledRefresh.ps1`), and every Phase 6A–6C test, and
found no defects — no fixes were required.

- **Architecture**: confirmed a single `RefreshOrchestrator`, a single freshness planner, and one
  composition root are reused by both the normal launch and `--scheduled-refresh`; no duplicate
  scheduling/freshness/persistence/failure-classification logic exists; no Windows Task Scheduler
  logic leaked into Domain/Application; the PowerShell scripts contain no SQL/HTTP and register only
  the one named task.
- **Exit codes / overlap / offline resilience / cancellation / SQLite concurrency / logging**: all
  match `docs/architecture.md`'s Phase 6A/6B descriptions above and this document's/`docs/scheduled-refresh-task-scheduler.md`'s
  operational docs; verified directly against `ScheduledRefreshHost`, `RefreshOrchestrator`,
  `FileScheduledRefreshLock`, `SqliteConnectionFactory`, and `FileLoggerProvider` source, not just
  prior-phase notes.
- **Test coverage**: reviewed as one suite across 6A–6C (argument parsing, headless-vs-normal
  startup, migration-before-refresh, exit codes, overlap acquire/contend/release, late/missed-run
  due-ness, all-sources-offline still `PartialFailure` not `FatalFailure`, fatal DB-open failure,
  cancellation, repeated failure→recovery, log pruning, startup catch-up, task-name/argument/path
  stability). No coverage gaps were found; no tests were added or weakened.
- **Pre-existing test flake** (spec Part L): the previously-observed intermittent
  `NoManagerWorkflowTests` Avalonia XAML parallelism failure did not reproduce across repeated runs
  of `Carowinds.Passdown.Ui.Tests` during this phase. Recorded here as a known historical flake only
  — no action taken, no test weakened.
- **Legacy local-data collision review** (spec Part M): the greenfield app's database file is
  `carowinds-passdown.db` (`appsettings.json` → `Storage:DatabaseFileName`), a name distinct from
  whatever file the previous application used, under the same `%LOCALAPPDATA%\Carowinds Passdown`
  directory (`AppPaths`). Because the filename differs, the new app's `MigrationRunner`/
  `PRAGMA user_version` versioning cannot read or misinterpret a legacy database file, and
  `Uninstall-ScheduledRefresh.ps1` never touches anything under that directory by content, only the
  scheduled task by name. This was a review only — nothing was deleted or migrated. **Flagged as a
  Phase 7 packaging/first-run item**: confirm during Windows packaging that no other file this app
  writes (logs, lock file) can collide by name with anything the legacy app left in the same
  directory, since neither application's directory layout was cross-checked file-by-file here.
- **Windows Task Scheduler validation**: the full manual VM checklist in
  `docs/scheduled-refresh-task-scheduler.md` was executed and passed — **manually validated by the
  user on Windows 11**, not performed by Claude. See that document's "Result" section for the
  recorded outcomes (task registration, manual run, `LastTaskResult = 0`, settings verification,
  uninstall/reinstall safety).

**Phase 6 (Scheduling and resilience) is complete.** Phase 5 (Manager workflow) remains deferred by
design (CLAUDE.md "Current scope decision"). Phase 7 (Windows packaging) is next, carrying forward
the legacy local-data naming item noted above as something to confirm during first-run/packaging
work, not a blocker to declaring Phase 6 itself complete.

## Phase 7A — Windows release/publish strategy

Phase 7A defined and validated the exact Windows release shape before building any ZIP or
installer — no product/UI/business logic changed. Full rationale, the publish command, the
runtime-dependency audit (including a real Playwright cross-platform-publish bug found and fixed),
the publish-output content audit, and the legacy local-data collision verdict all live in
`docs/release-strategy.md`; this section only records what changed in the codebase.

- **`Directory.Build.props`**: added `<Version>0.1.0-dev</Version>` (single authoritative version
  source for every project — flows into `AssemblyVersion`/`FileVersion`/`InformationalVersion`;
  explicitly a development version, not an assigned product release number) and
  `<Product>Carowinds Passdown</Product>` (the built executable's file-properties "Product name,"
  independent of the `Carowinds.Passdown.Desktop.exe` binary file name, which is unchanged).
- **`Carowinds.Passdown.Desktop.csproj`**: no build-behavior changes — a comment records the
  deliberate `PublishSingleFile`/`PublishTrimmed`/`PublishAot` = `false` decision (all already the
  SDK default; nothing was flipped) so the rationale is discoverable at the point a future reader
  might consider changing it.
- **`packaging/windows/Publish-Windows.ps1`** (new): reproducible `dotnet publish` wrapper —
  `-c Release -r win-x64 --self-contained true -p:PlaywrightPlatform=win-x64`, output to
  `artifacts/publish/win-x64` (gitignored), plus a post-publish `.pdb` cleanup step. Registers
  nothing, builds no ZIP/installer, requires no admin rights, never touches user application data.
  Mirrors `Install-/Uninstall-ScheduledRefresh.ps1`'s existing pattern (repo-root-relative,
  idempotent, no hard-coded developer path). Written and reviewed this phase but not executed — no
  `pwsh` runtime is available on this session's dev host; the `dotnet publish` command it wraps
  *was* executed and validated directly.
- **`.gitignore`**: added `artifacts/` (the publish staging output).
- **Tests**: `tests/Carowinds.Passdown.Infrastructure.Tests/AppPathsTests.cs` (`AppPaths` resolves
  from the OS special-folder API, not the current working directory — matters because the release
  executable may run from a location ordinary users shouldn't write to); `tests/Carowinds.Passdown.Ui.Tests/Release/ReleaseVersionTests.cs`
  (the built Desktop assembly's version/product metadata actually reflects the new
  `Directory.Build.props` values rather than silently falling back to SDK defaults). Both added,
  none weakened; full suite is 609/609 (603 pre-existing + 6 new).
- **No code changes** were needed for first-run behavior, release-path independence, or the
  scheduled-refresh packaging contract — all three were audited and found already correct
  (`docs/release-strategy.md` "First-run filesystem expectations," "Release path independence"
  equivalent audit, "Scheduled-refresh packaging contract").

### Deferred out of Phase 7A

Everything covered under "What's deferred" in `docs/release-strategy.md`: the actual portable ZIP,
any installer decision, real Windows-host execution (launch + `--scheduled-refresh` smoke test of
the published binary, and running `Publish-Windows.ps1` itself under a real PowerShell host),
legacy-machine collision verification against a real legacy install, the sanitized diagnostic
export, nontechnical-tester instructions, and any further reduction of the Playwright package's
footprint. Manager workflow remains out of scope per CLAUDE.md's current scope decision,
unaffected by this phase.

## Phase 12A — Operations Calendar foundation

Full design rationale, date-semantics detail, and the "real bug found and fixed" note live in
`docs/phase12-operations-calendar.md`; this section only records what was added and where.

Phase 12A adds a fifth top-level app-shell section, `AppSection.OperationsCalendar`
(`Dashboard | Operations Calendar | Associate Passdown | Operational Notes | History`), placed
between Dashboard and Associate Passdown — a month-grid planning/routing surface, built the same
way Phase 10A's Dashboard and Phase 9D's History were: a local-only Application-layer read model
projected over existing state, never a second business-rule implementation.

- **Application** (`Carowinds.Passdown.Application.Calendar`): `IOperationsCalendarService`/
  `OperationsCalendarService` — depends on `IAssociatePassdownService` (the selected date's own
  composed workspace), `IAssociateDraftRepository` (`ListSavedDraftDatesAsync`, the same signal
  Phase 9B's `ScheduledPassdownFinalizer` already uses for eligibility), `IFinalizedPassdownRepository`
  (metadata only via `ListAsync` — never `GetByDateAsync`, so a calendar load can never materialize
  a historical PDF blob), `IOperationalNotesService` (`ListAsync` plus the existing
  `OperationalNoteRelevance.IsRelevantForDate` predicate, applied in memory per day — never a second
  relevance rule), and `IClock`. `OperationsCalendarSnapshot` carries a month grid
  (`OperationsCalendarDay`: date, in-month/today flags, finalized/draft/note-count indicators) plus
  one `OperationsCalendarSelectedDateSummary` (passdown status — NoDraft/DraftInProgress/Finalized —
  plus note count) for whichever date is currently selected. Exactly three repository/service calls
  happen per load regardless of grid size, never an N+1 per day cell.
- **Desktop** (`OperationsCalendarViewModel`, `OperationsCalendarDayViewModel`,
  `OperationsCalendarPresentation`, `OperationsCalendarView.axaml`): depends only on
  `IOperationsCalendarService`/`IClock` — no repository/HTTP/PDF dependency, mirroring
  `OperationsDashboardViewModel`'s own shape. "Today" is always Eastern via `IClock`+
  `EasternTimeZone`, never `DateTime.Today`/`.Now`. `SelectedDate` is independent of
  `AssociateViewModel.SelectedDate` except through the one explicit "Open Passdown" action, which
  sets it deliberately (the Calendar's whole purpose is routing to a specific date) — "Open
  History"/"Open Operational Notes" stay generic navigation, the same choice Phase 10C already made
  and documented for Dashboard's own routing (evaluated again here, not re-litigated). `MainViewModel`
  remains the sole navigation owner; the Calendar view model itself has no knowledge of `AppSection`
  or any sibling view model.
- **Tests**: `OperationsCalendarServiceTests` (Application, 18 cases — grid completeness/weekday
  alignment across several month shapes, Eastern-today near a UTC boundary, per-day/selected-date
  classification, the never-loads-a-PDF-blob guarantee), `OperationsCalendarViewModelTests` (13),
  `OperationsCalendarViewTests` (4), plus extensions to `MainViewModelNavigationTests` (state
  isolation between Calendar and Associate/History/Operational Notes in both directions, the dirty-
  Operational-Notes-editor guard extended to Calendar's own nav command) and `CompositionRootTests`.
  1286/1286 total (1242 Phase 11F baseline + 44 new), 0 warnings/errors, run twice for flakiness.

No schema change — database schema stays 10, snapshot schema stays 1. Deferred to 12B+: richer
per-day overlays, denser note previews, a History date-preselection seam, Alerts, and the Manager
workflow (still out of scope per CLAUDE.md).
