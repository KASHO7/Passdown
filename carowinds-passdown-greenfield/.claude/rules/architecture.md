---
paths:
  - "src/**/*.cs"
  - "src/**/*.csproj"
  - "Directory.Build.props"
  - "CarowindsPassdown.slnx"
---

# Project boundaries and implementation

Domain is pure; Application depends on Domain and declares use-case/repository abstractions. Infrastructure and Documents depend on Application/Domain, not on each other. Desktop composes Application, Infrastructure and Documents; Updater depends on Infrastructure. No cycles or concrete infrastructure/UI dependencies in Domain/Application. Domain must not acquire framework or I/O packages. Documents must not access database, network or UI. Code-behind is limited to presentation, focus, layout and native control integration.

Preserve DI composition and existing lifetimes; maintain one authoritative workspace, one autosave lifecycle and one document pipeline. Extract components incrementally, preserve bindings and behavior, and test dependency direction. Do not remove deferred Manager or retired-feature compatibility code without a compatibility review. Consult `docs/architecture.md` and relevant linked records.
