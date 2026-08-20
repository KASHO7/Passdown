---
name: build-and-test
description: Build the Carowinds Passdown solution and run the appropriate test tier(s) for the current task. Use after any source edit, before reporting a task complete, and before phase completion.
---

# Build and test

Follow CLAUDE.md workflow steps 5–7 (run relevant tests, run `dotnet build`, fix
failures caused by the work).

## 1. Locate the solution

The solution lives under `carowinds-passdown-greenfield/` (see
AGENT_BUILD_SPEC.md §4). If it does not exist yet, there is nothing to build —
stop and say so rather than inventing a path.

```bash
find carowinds-passdown-greenfield -maxdepth 1 -name '*.sln' -o -maxdepth 1 -name '*.slnx'
```

## 2. Build

```bash
dotnet build carowinds-passdown-greenfield/CarowindsPassdown.slnx
```

Fix only failures caused by the current work (CLAUDE.md). Do not silently
patch unrelated pre-existing failures — report them instead.

## 3. Choose the right test tier(s)

Match the test project to what changed. Do not run the full suite for a
narrow change; run it before phase completion (CLAUDE.md "Efficiency").

| Changed area | Test project | Notes |
|---|---|---|
| Domain rules (hours, gate calc, freshness, product lifecycle, staffing) | `tests/Carowinds.Passdown.Domain.Tests` | Pure logic, no I/O. Spec §14.1. |
| Source adapters, cache, storage | `tests/Carowinds.Passdown.Infrastructure.Tests` | Uses fixtures, not live internet. Spec §14.2. |
| PDF generation | `tests/Carowinds.Passdown.Documents.Tests` | Page-count/overflow checks. Spec §14.1, §11.5. |
| Avalonia UI/view models | `tests/Carowinds.Passdown.Ui.Tests` | Spec §14.4. |

```bash
dotnet test carowinds-passdown-greenfield/tests/<Project>/<Project>.csproj
```

## 4. Live smoke tests are opt-in only

Retrieval contract tests (§14.2) must run against committed fixtures, never
the live internet, and must never gate a normal build. Live smoke tests
(§14.3) exist separately and are excluded by default — only run them when
explicitly asked, e.g. via a distinct filter/category:

```bash
dotnet test carowinds-passdown-greenfield/tests/Carowinds.Passdown.Infrastructure.Tests \
  --filter Category=LiveSmoke
```

If no such category/trait exists yet when this is needed, add one rather than
letting live tests run by default.

## 5. Before declaring a task complete

- All tests run for the touched area must pass (CLAUDE.md "Testing").
- `dotnet build` must succeed with no new warnings/errors introduced by the change.
- Do not report success based on live-website behavior alone; contract tests
  against fixtures are the source of truth for parser correctness.

## 6. Before phase completion

Run the full suite (excluding live smoke) once for the phase:

```bash
dotnet test carowinds-passdown-greenfield/CarowindsPassdown.slnx --filter Category!=LiveSmoke
```
