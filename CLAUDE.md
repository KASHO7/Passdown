# Carowinds Passdown

Authoritative product specification: `AGENT_BUILD_SPEC.md`.

## Product owner

The product owner does not write or debug source code.

Handle:
- source creation and editing
- dependency setup
- builds
- tests
- debugging
- migrations
- packaging

Do not ask the product owner to manually edit code.

Ask only about genuine product/UX/business decisions that cannot be
resolved from the specification.

## Greenfield rule

This is a new implementation.

Do not patch, reskin, or copy the previous application.
Old code is reference material only.

## Architecture

Projects:

- Domain: pure business rules; no Avalonia, SQLite, HTTP, Playwright,
  QuestPDF, or Windows APIs.
- Application: use cases and abstractions; depends on Domain.
- Infrastructure: SQLite, HTTP, Playwright, filesystem, Windows services.
- Documents: QuestPDF/document generation.
- Desktop: Avalonia MVVM presentation only.

Do not put persistence, retrieval, business rules, or PDF generation
in Avalonia code-behind.

## Workflow

Work on only the requested phase/task.

For each task:

1. Inspect relevant files.
2. Read only the relevant portions of `AGENT_BUILD_SPEC.md`.
3. Plan briefly.
4. Implement.
5. Run relevant tests.
6. Run `dotnet build`.
7. Fix failures caused by the work.
8. Review the diff.
9. Report results.
10. Stop.

Do not automatically start the next phase.

## Retrieval rules

Official Carowinds/Six Flags sources only.

Prefer:
1. official structured HTTP/API data
2. direct HTTP/HTML
3. Playwright only when necessary

Never invent data or silently substitute another date.

Changing the selected date must never trigger online retrieval.

## Persistence

Use SQLite unless a documented technical reason requires otherwise.

A failed retrieval must not overwrite last-known-good data.

Manual draft changes must never modify official cached values.

## Testing

Do not report a task complete while relevant tests are failing.

Live website tests must be opt-in and must not make the normal build
depend on external websites.

## Git

Inspect `git status` before large changes.

Do not overwrite unrelated user work.

Never use destructive Git operations unless explicitly requested.

## Platform

Development host: Fedora.
Target platform: Windows 11 x64.

Keep Windows-only behavior isolated and flag it for Windows VM testing.

## Efficiency

Do not reread the entire specification for every task.

Read the global requirements relevant to the current task and the
specific implementation phase being executed.

Do not scan unrelated directories unless necessary.

Prefer focused tests over repeatedly running the entire suite during
implementation. Run the full suite before phase completion.
