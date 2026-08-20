# Architecture — Phase 1 baseline

Established during Phase 1 (AGENT_BUILD_SPEC.md §17 Phase 1). Domain models,
adapters, and UI screens are added in later phases; this records the
boundaries and infrastructure they will build on.

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
the concrete adapters. None exist yet — Phase 2/3 add them alongside the
domain models they serve.

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
  Phase 1 ships one migration (`Migration0001CreateSchemaVersionTable`)
  that creates only the diagnostic history table — no domain tables yet.
  Domain/catalog/draft schema arrives in Phase 2 as additional numbered
  migrations, per spec §6.2's separate dated-results/catalog-results/
  source-state/drafts model.
- **Composition root**: `Carowinds.Passdown.Desktop.App` builds a single
  `IServiceProvider` in `Initialize()`, runs the migration runner before
  resolving the main window in `OnFrameworkInitializationCompleted()`, and
  resolves `MainWindow`/`MainViewModel` through DI. Later phases register
  their services here rather than introducing a second composition
  mechanism.

## Deferred to later phases

Domain models, real freshness cadences, source adapters, Avalonia
screens/view models beyond the template placeholder, PDF generation,
scheduling, and packaging are explicitly out of scope for Phase 1 (see the
approved Phase 1 plan).
