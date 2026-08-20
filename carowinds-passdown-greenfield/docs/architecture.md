# Architecture — Phase 1 baseline, updated through Phase 2

Established during Phase 1 (AGENT_BUILD_SPEC.md §17 Phase 1) and extended
through Phase 2 (§17 Phase 2). Source adapters and UI screens are added in
later phases; this records the boundaries and infrastructure they build on.

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
deterministic, DST-safe time. Retrieval-side abstractions (the actual
`IOfficialDataSource`-shaped adapters) remain Phase 3's job.
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

## Deferred to later phases

Source adapters, Avalonia screens/view models beyond the template
placeholder, PDF generation, scheduling orchestration, and packaging are
explicitly out of scope through Phase 2 (see the approved Phase 1 and
Phase 2 plans).
