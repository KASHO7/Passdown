---
name: build-and-test
description: Build the Carowinds Operations solution and run the right deterministic test partitions (including the isolated real-window UI classes). Use after any source or test edit, before reporting a task complete, and before implementation-phase close.
---

# Build and test

Follow `CLAUDE.md` workflow step 4 and `.claude/rules/testing.md`. Paths below are relative to the
application directory (`carowinds-passdown-greenfield/`, which contains `CarowindsPassdown.slnx`);
the Git root is its parent folder, so `git diff --name-only` prints `carowinds-passdown-greenfield/…`
paths — use `--relative` when building pathspecs.

Documentation-only work needs consistency checks, not full test runs. The repository guards run in
seconds and are the mechanical part of that check:

```bash
dotnet test tests/Carowinds.Passdown.Infrastructure.Tests/Carowinds.Passdown.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~.RepositoryGuards."
``` Never report a run that did not
happen, and never claim non-Windows runs as Windows validation.

## 1. Build

```bash
dotnet build CarowindsPassdown.slnx -c Release
```

The solution builds the six `src/` projects, the five test projects and `tools/update-signing`.
`tools/perf-benchmarks`, `tools/visual-fixtures`, `tools/updater-offline-stager` and
`tools/updater-test-harness` are outside the solution; build them explicitly when touched.
The established Release build has 0 warnings and 0 errors; a change must not introduce any.
At implementation-phase close, run a clean Release build (delete `bin`/`obj` or use
`--no-incremental`).

## 2. Focused tests while working

| Changed area | Test project |
|---|---|
| Domain rules | `tests/Carowinds.Passdown.Domain.Tests` |
| Use cases, workspace, sync payloads | `tests/Carowinds.Passdown.Application.Tests` |
| Retrieval, SQLite, sync files, updater, packaging checks | `tests/Carowinds.Passdown.Infrastructure.Tests` (also references `tools/update-signing`) |
| QuestPDF documents, Daily Feature math | `tests/Carowinds.Passdown.Documents.Tests` |
| Avalonia views, view models, preview | `tests/Carowinds.Passdown.Ui.Tests` |

```bash
dotnet test tests/<Project>/<Project>.csproj -c Release --filter "Category!=LiveSmoke"
```

A focused `--filter` run can hide thread-affinity defects; only the mixed UI partition exposes
them, so run it before declaring UI work complete.

## 3. Deterministic partitions (required at implementation-phase close)

The deterministic total is `-c Release --filter "Category!=LiveSmoke"` per test project, with
`Carowinds.Passdown.Ui.Tests` split into a mixed partition plus **six real-window classes, each run
alone** (Avalonia headless thread affinity breaks them when mixed):

`OperationsCalendarViewTests`, `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests`,
`SpellCheckNavigationLifecycleTests`, `Phase21DMathEditorWindowTests`, `Phase33PreviewWindowTests`.

```bash
for p in Domain Application Infrastructure Documents; do
  dotnet test tests/Carowinds.Passdown.$p.Tests/Carowinds.Passdown.$p.Tests.csproj -c Release --filter "Category!=LiveSmoke"
done
```

```bash
dotnet test tests/Carowinds.Passdown.Ui.Tests/Carowinds.Passdown.Ui.Tests.csproj -c Release --filter "Category!=LiveSmoke&FullyQualifiedName!~.OperationsCalendarViewTests.&FullyQualifiedName!~.Phase15ERealWindowLayoutTests.&FullyQualifiedName!~.RealWindowStartupTimingTests.&FullyQualifiedName!~.SpellCheckNavigationLifecycleTests.&FullyQualifiedName!~.Phase21DMathEditorWindowTests.&FullyQualifiedName!~.Phase33PreviewWindowTests."
```

```bash
for c in OperationsCalendarViewTests Phase15ERealWindowLayoutTests RealWindowStartupTimingTests SpellCheckNavigationLifecycleTests Phase21DMathEditorWindowTests Phase33PreviewWindowTests; do
  dotnet test tests/Carowinds.Passdown.Ui.Tests/Carowinds.Passdown.Ui.Tests.csproj -c Release --filter "FullyQualifiedName~.$c."
done
```

Filter by `FullyQualifiedName~.ClassName.` (some classes live in sub-namespaces such as
`Carowinds.Passdown.Ui.Tests.Startup`). Report the per-partition counts and compare them with the
latest phase or release record (the current baseline is recorded there, not here). A new
real-window class joins the run-alone list and must be recorded in the phase record.

## 4. Known hazards

- Guard memory for UI partitions (kill the test host above ~3 GB). In realized headless windows a
  `TextBox` containing a blank line, or a `<LineBreak />` inline, has caused unbounded memory growth.
- Never use `[MemberData]` in `Carowinds.Passdown.Ui.Tests`; use `[InlineData]`.
- A hung mixed run (idle test host) can be killed and rerun; attribute recurrences with
  `--blame-hang --blame-hang-timeout 90s`. Record intermittent failures as pre-existing or new.
- `tools/visual-fixtures` renders one scenario per process; do not batch scenarios in one process.
  Headless Skia renders are development evidence, not Windows DPI/accessibility validation.
- Use synthetic data and fake preference stores; never launch against the personal profile.

## 5. Live tests are opt-in only

`Category=LiveSmoke` tests contact official websites. Run them only when explicitly asked, never as a
gate, and never add a cart item or submit sign-in/payment forms:

```bash
dotnet test tests/Carowinds.Passdown.Infrastructure.Tests/Carowinds.Passdown.Infrastructure.Tests.csproj --filter Category=LiveSmoke
```

Release-package tests (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`) belong to the release
workflow in `docs/release-strategy.md`.

## 6. Completion

Required failing tests block implementation completion. Fix failures the change caused; report
pre-existing failures separately with evidence. Never delete, skip or weaken tests to pass.
