# Carowinds Operations — Current architecture

**Review baseline:** local Git repository, branch `feature/passdown-redesign`, HEAD `9351c64` (RC.14 release record). Production source (`src/`, `Directory.Build.props`) is identical to RC.14 release source commit `3d603f0`; the commits after it are documentation only. Verified by read-only source review on 2026-11-26 during the repository documentation maintenance task (project references, component paths, schema and sync constants, migrations); no build, test or Windows validation was run for that review. Dirty-tree caveat: the review ran with the owner's uncommitted documentation reorganization and pre-staged `.DS_Store` / `pdf-design` entries present; no production source was modified.

**Post-RC.14 production change (Phase 36C, `07ae59b`–`18fac73`, local only):** the Associate workspace's Early Ride Time editor is a child view model (`EarlyRideTimeEditorViewModel`) and three view models marshal background notifications through `IUiDispatcher`; no project, dependency, schema, sync, PDF or retrieval change. Production source therefore no longer equals `3d603f0`; see [Phase 36C](phase36c-ert-editor-maintainability.md) and [Desktop UI](architecture/desktop-ui.md). The product owner reported a successful Windows test of the `RC15-Phase36C-LOCALTEST` candidate (2026-11-27; owner statement, no per-item results; Claude ran no Windows test).

**Proposed, not implemented:** Weather integration (NWS) is planned in [Phase 37A](phase37a-weather-source-architecture-plan.md) and awaits owner approval; nothing in the guides below describes it as current.

Release state from records: `0.1.0-rc.14` published 2026-11-27T00:56Z as a GitHub prerelease ([RC.14 readiness](releases/rc14-release-readiness.md)); not merged to `main`. The owner reported a successful genuine RC.13 → RC.14 in-app update on Windows (RC.14 readiness §15). Phase 35 (35A–35G) is implemented; the product owner accepted the final 35G local candidate on Windows overall, without per-item checklist results ([Phase 35G](phase35g-final-uiux-polish-readiness.md)). Refresh this status from actual repository evidence at each phase close.

## Dependency direction

| Project | Direct internal dependencies | Responsibility |
|---|---|---|
| Domain | None | Pure business rules and identities |
| Application | Domain | Use cases, ports, policies, coordination |
| Infrastructure | Application, Domain | SQLite, HTTP, filesystem, sync, PDFium, Windows printing and updater implementation |
| Documents | Application, Domain | QuestPDF models, templates, math |
| Desktop | Application, Infrastructure, Documents | Avalonia MVVM, DI composition, UI coordination |
| Updater | Infrastructure | Separate staged update executable |

Verified from `src/*/*.csproj` at the review baseline. Tools outside this table: `tools/update-signing` (in the solution) and `tools/updater-offline-stager`, `tools/updater-test-harness` reference Infrastructure; `tools/perf-benchmarks` and `tools/visual-fixtures` reference Desktop. Test projects reference only the production project they cover (Infrastructure.Tests also references `tools/update-signing`). Keep Infrastructure and Documents independent; no concrete infrastructure/UI dependencies in Application or Domain. `Directory.Build.props` declares the source version and target framework; inspect it rather than deriving versions from historical records.

## Subsystem map

| Current-state guide | Scope |
|---|---|
| [Application lifecycle](architecture/application-lifecycle.md) | Composition, startup, shutdown, coordinators |
| [Domain and Application](architecture/domain-and-application.md) | Use cases, workspace state, rules |
| [Data and persistence](architecture/data-and-persistence.md) | SQLite, drafts, finalized history, local preferences |
| [Retrieval and refresh](architecture/retrieval-and-refresh.md) | Official sources, freshness, coalescing |
| [Personal Sync](architecture/personal-sync.md) | File exchange, conflicts, compatibility |
| [Desktop UI](architecture/desktop-ui.md) | Shell, workspace, design system |
| [PDF and printing](architecture/pdf-and-printing.md) | Shared generation, preview, printing |
| [Updates and release](architecture/updates-and-release.md) | Signing, packaging, installation |
| [Testing and performance](architecture/testing-and-performance.md) | Test partitions, benchmarks, technical debt |

These guides summarize current contracts at the review baseline and link to existing implementation records; they are not a replacement for inspecting source. Historical records can contain superseded implementation details. The [original architecture history](architecture/architecture-history.md) is preserved byte-for-byte and is not the current-state authority.

## Data contracts and scope

Verified in source at the review baseline: SQLite schema 18 (latest migration `Migration0018CreateOfficialReferenceDocumentsTable`), Personal Sync envelope `SyncEnvelopeSerializer.CurrentSyncSchemaVersion` 1, draft payload `AssociateDraftPayload.CurrentSchemaVersion` 4, finalized snapshot `FinalizedPassdownSnapshot.CurrentSchemaVersion` 2; finalized/operational-note/ERT sync payloads and the official price history file are version 1; update manifest and signature formats are version 1. No schema or sync change in Phase 35 or RC.14. Manager remains deferred; Morning Message production code and sync channel are retired, compatibility safeguards remain. Do not use historical schema/version descriptions as current requirements.

## Maintenance

Every task reviews architecture impact; meaningful changes update the affected current-state guide and overview in the same change set. Phase close records the actual reviewed baseline, test/build results, Windows status and unresolved issues. Follow `CLAUDE.md` and `.claude/rules/documentation.md`. Automated verification does not replace source review.
