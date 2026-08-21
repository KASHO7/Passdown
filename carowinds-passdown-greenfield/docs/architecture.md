# Architecture — Phase 1 baseline, updated through Phase 3 (complete)

Established during Phase 1 (AGENT_BUILD_SPEC.md §17 Phase 1) and extended
through Phase 2 (§17 Phase 2) and the full Phase 3 (§17 Phase 3 — retrieval
performance and consistency, now complete through the Phase 3H integrated
review). UI screens beyond the template placeholder and PDF generation are
added in later phases; this records the boundaries and infrastructure they
build on.

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

## Deferred to later phases

Avalonia screens/view models beyond the template placeholder (wiring the
date experience, product display, and manual refresh actions to
`IRefreshOrchestrator`), PDF generation, the 6 AM scheduler and Windows Task
Scheduler integration, and packaging remain out of scope — Phase 4 onward.
