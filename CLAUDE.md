# CLAUDE.md

## Project

This repository contains the greenfield implementation of **Carowinds Passdown**.

The authoritative product and technical requirements are in:

`AGENT_BUILD_SPEC.md`

Read `AGENT_BUILD_SPEC.md` completely before performing significant implementation work.

If this file and `AGENT_BUILD_SPEC.md` conflict, follow `AGENT_BUILD_SPEC.md`.

---

## Product Owner Interaction

The product owner does not want to manually write, modify, or debug source code.

You are responsible for:

- Creating and editing source files.
- Creating project files and solution structure.
- Adding and configuring dependencies.
- Writing database migrations.
- Writing tests.
- Running builds.
- Running tests.
- Diagnosing compilation errors.
- Diagnosing test failures.
- Fixing problems you introduce.
- Producing development and release artifacts where requested.

Do not instruct the product owner to manually edit C#, AXAML, SQL, project files, configuration files, or scripts.

Only ask the product owner questions when a genuine product, UX, or business-rule decision cannot be reasonably resolved from `AGENT_BUILD_SPEC.md`.

Make reasonable technical implementation decisions yourself and document important ones.

---

## Greenfield Rule

This is a new implementation.

Do not patch, reskin, or continue the previous Carowinds Passdown application.

Previous projects may be inspected only as reference material for:

- Known official URLs.
- Previous retrieval experiments.
- Website behavior.
- Selectors.
- Failure modes.
- Example output behavior.

Do not assume previous code or previous retrieved data is correct.

Do not copy old architecture simply because it already exists.

---

## Implementation Phases

Follow the phases in `AGENT_BUILD_SPEC.md` in order.

Do not automatically proceed to the next phase.

The normal workflow is:

1. Inspect the repository and current phase requirements.
2. Plan the implementation.
3. Implement only the requested scope.
4. Build.
5. Run relevant tests.
6. Fix failures.
7. Review the resulting diff.
8. Report completion and remaining issues.
9. Stop.

Wait for the product owner to request the next phase.

---

## Architecture Rules

Maintain these project boundaries:

### Carowinds.Passdown.Domain

Contains pure domain concepts and business rules.

Must not depend on:

- Avalonia
- SQLite
- Playwright
- HttpClient
- QuestPDF
- Windows APIs

Domain should not reference another application project.

### Carowinds.Passdown.Application

Contains application use cases, orchestration abstractions, and application services.

May depend on Domain.

Must not contain Avalonia UI implementation.

Avoid direct dependencies on SQLite, Playwright, or Windows-specific APIs.

### Carowinds.Passdown.Infrastructure

Contains implementations for:

- SQLite persistence
- HTTP retrieval
- Playwright retrieval
- Logging infrastructure
- File-system storage
- Windows scheduling/platform services

Infrastructure may implement abstractions defined by Domain/Application.

Do not expose Playwright or SQLite-specific types across project boundaries.

### Carowinds.Passdown.Documents

Contains PDF/document generation.

QuestPDF-specific implementation belongs here.

Do not put PDF layout logic in the Desktop project.

### Carowinds.Passdown.Desktop

Contains:

- Avalonia views
- View models
- presentation state
- commands
- user interaction

Use MVVM.

Do not place retrieval, persistence, business rules, or PDF-generation logic directly in Avalonia code-behind.

Keep code-behind minimal.

---

## Dependency Direction

Prefer:

Domain
↑
Application
↑
Desktop

Domain/Application
↑
Infrastructure

Domain/Application
↑
Documents

Do not introduce circular project references.

---

## Data Retrieval Rules

Use only official public Carowinds/Six Flags sources as specified in `AGENT_BUILD_SPEC.md`.

Prefer:

1. Stable official public JSON/API/structured HTTP data.
2. Direct HTML retrieval when dependable.
3. Playwright browser automation only when necessary.

Do not use:

- Search engine snippets.
- Third-party pricing sites.
- Guessed values.
- Made-up fallback values.
- Private credentials.
- Authentication reverse engineering.

Exact-date data must be confirmed for the requested date before being treated as verified.

Never silently substitute data from another date.

A failure in one retrieval source must not invalidate successful results from other sources.

---

## Browser Automation Rules

Playwright is a fallback, not the default retrieval mechanism.

For a refresh job:

- Prefer one browser instance.
- Reuse browser contexts where appropriate.
- Limit concurrency.
- Close pages and browser processes reliably.
- Support cancellation.
- Use bounded timeouts.
- Prefer readiness conditions over arbitrary sleep delays.
- Block unnecessary images, media, fonts, or analytics when safe.

Do not launch a browser simply because the user selected another date.

---

## Cache and Persistence Rules

Use SQLite unless there is a documented reason to use another storage approach.

Keep separate concepts for:

- Dated results.
- Catalog products.
- Source state.
- Product history.
- Associate drafts.
- Manager drafts.

A retrieval failure must never overwrite the last known good result.

Store last attempt separately from last successful retrieval.

Manual draft overrides must never alter official cached values.

Use schema/database migrations for persisted model changes.

---

## Date Selection Rule

Changing the selected date is a local operation.

Selecting a date must:

- Load the saved draft.
- Load cached dated results.
- Merge reusable catalog information.
- Update the UI.

Selecting a date must NOT:

- Launch Playwright.
- Automatically perform HTTP retrieval.
- Trigger a full refresh.

Online retrieval must happen through an explicit refresh/check action or scheduled refresh.

---

## Testing Rules

All meaningful business logic should have automated tests.

At minimum, preserve the test requirements in `AGENT_BUILD_SPEC.md`.

When modifying production behavior:

1. Add or update tests.
2. Run the relevant test project.
3. Run the complete test suite when the change can affect multiple projects.

Do not report implementation complete while relevant tests are failing.

Live website tests must be opt-in and must not make normal builds depend on an external website being available.

---

## Build Rules

After meaningful implementation changes, run:

`dotnet build`

Run relevant tests with:

`dotnet test`

Before reporting a phase complete, run the complete build and test suite.

If a failure is caused by your changes, diagnose and fix it yourself.

Do not simply report compiler errors to the product owner and ask them to fix them.

---

## Git Rules

Before large changes:

- Inspect `git status`.
- Understand existing uncommitted changes.
- Do not overwrite unrelated user work.

Keep changes scoped to the current task.

Do not perform destructive Git operations such as:

- `git reset --hard`
- deleting branches
- rewriting published history

unless explicitly requested.

Do not commit secrets, generated caches, browser profiles, build intermediates, or private employee information.

When asked to create a checkpoint, make a clear Git commit describing the completed behavior.

---

## UI Rules

Design for nontechnical Windows users.

Use clear language.

Prefer labels such as:

- `Check this date online`
- `Saved on this computer`
- `Create PDF`
- `Last verified ...`

Avoid unnecessarily technical labels such as:

- `Probe`
- `Hydrate`
- `Persist`
- `Execute`

Use accessible keyboard navigation and visible focus states.

Do not rely on color alone to communicate status.

---

## PDF Rules

PDF output must follow `AGENT_BUILD_SPEC.md`.

PDF generation should be deterministic and testable.

When modifying PDF layouts:

- Generate representative sample PDFs.
- Verify page count.
- Verify page dimensions.
- Check typical and high-content fixtures.
- Do not shrink text excessively simply to force one page.

---

## Platform Rules

Primary development may occur on Fedora.

The target production platform is Windows 11 x64.

Keep Windows-specific functionality behind abstractions so the main application can still build and test on Fedora where practical.

Windows-specific behavior that cannot be reliably validated on Fedora must be flagged for Windows VM testing.

Examples include:

- Windows Task Scheduler.
- SmartScreen behavior.
- Windows-specific paths.
- Explorer integration.
- Windows scaling/DPI behavior.
- Final portable ZIP execution.

---

## Security and Privacy

Do not:

- Store employee credentials.
- Add cloud services unless explicitly required.
- Upload drafts automatically.
- Include sensitive manager notes in diagnostics.
- Commit real private employee information.
- Log personnel-sensitive draft content.

Sanitize diagnostic output.

---

## Working Style

Prefer small, reviewable changes over broad rewrites.

Before implementing a substantial change:

- Inspect the relevant code.
- Understand existing interfaces and tests.
- State a concise implementation plan.

Do not refactor unrelated parts of the repository.

Do not create abstractions merely for abstraction's sake.

When a simple implementation satisfies the architecture and requirements, prefer it.

---

## Completion Report

At the end of each requested task or phase, report:

- What was implemented.
- Important files added or modified.
- Architecture decisions made.
- `dotnet build` result.
- `dotnet test` result.
- Any tests intentionally not run and why.
- Any Windows-only verification still required.
- Known limitations.
- Recommended next phase.

Do not automatically begin the next implementation phase.
