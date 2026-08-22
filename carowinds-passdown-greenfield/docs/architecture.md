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
