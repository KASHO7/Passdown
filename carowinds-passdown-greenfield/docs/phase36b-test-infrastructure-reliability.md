# Phase 36B — Test infrastructure reliability

Status: implemented locally (test and tool code only) / no Windows validation required for this scope / awaiting owner review / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `da65b53` (Phase 36A audit record) |
| Ending commits | One local commit with the changes below (hash reported in the completion report) |
| Reviewed source baseline | `da65b53`; production source (`src/`) unchanged and identical to RC.14 release source `3d603f0` |
| Dirty-tree caveats | Clean tree at start; build, test and render output only in ignored `bin`/`obj` folders and the session scratch folder |
| Authorization | Owner approved the narrowed 36B from the [Phase 36A audit](phase36a-current-architecture-technical-debt-audit.md) §15 ("Approve 36B", 2026-11-27): TD-01, TD-03, TD-04. TD-02 (checked-in test runner) needs separate owner approval of new tooling and was **not** implemented |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published; version `0.1.0-rc.14` unchanged; no package built |
| Database schema / sync formats | Schema 18; sync envelope 1; draft 4; snapshot 2; unchanged |

## Scope

Test and tool enforcement for the three procedural safeguards confirmed by the Phase 36A audit. No production C#,
XAML, project reference, package, migration, sync format, PDF template, retrieval code, version or release script
changed.

## Changes

1. **TD-01: real-window confinement guard.** New `tests/Carowinds.Passdown.Infrastructure.Tests/RepositoryGuards/RealWindowPartitionGuardTests.cs`
   adds 10 tests to the repository guards:
   - A scan of every Ui.Tests source file. It fails when `new Window` or `new MainWindow` (including
     namespace-qualified forms) or `.Show()`/`.ShowDialog()` appears outside comments in a file that does not
     declare one of the six run-alone classes.
   - A check that each run-alone class still exists.
   - A check that the `build-and-test` skill (mixed-partition exclusions and run-alone loop) and
     `docs/architecture/testing-and-performance.md` list exactly the same six classes.
   - Seven detector cases, covering violations, permitted partials, comments and look-alike `Show…Command` names.
2. **TD-03: bounded screenshot harness.** New `tools/visual-fixtures/CaptureBudget.cs` and `Program.cs`. Before
   rendering, the tool computes the planned captures (filter × sizes × appearance modes).
   - It refuses a run of more than **48** captures (exit code 2).
   - Before each window it stops at a **3,072 MB** working set (exit code 3), after writing the geometry report
     gathered so far.
   - `CAROWINDS_VISUAL_FIXTURES_MAX_CAPTURES` and `CAROWINDS_VISUAL_FIXTURES_MAX_WORKING_SET_MB` raise a limit for
     one deliberate run; any value that is not a positive whole number exits with code 2.
   - Normal runs exit with 0.
3. **TD-04.** `Bitmap.Save(string, int?)` (CS0618) is replaced by `Save(string, PngBitmapEncoderOptions.Default)`.
   `tools/visual-fixtures` now builds with 0 warnings.
4. **Documentation:**
   - `build-and-test` skill: how to add real-window tests and run-alone classes; the macOS test-host match for
     memory watchdogs (observed in 36A); the fixture limits.
   - `testing-and-performance.md`: guard count 21 → 31, the window and fixture checks, evidence links.
   - `tools/visual-fixtures/README.md`: a "Process limits" section. This also repairs a pre-existing list break
     that had separated "Every size is rendered in both Light and Dark" from the sizes item.

## Validation

All results are from the macOS development host (Apple M2 Pro, macOS 27.2.1, .NET SDK 10.0.400). None of it is
Windows validation.

| Check | Result |
|---|---|
| Repository guards | 31 / 31 passed (21 existing + 10 new) |
| Guard trips on a real violation | A temporary `ZzGuardProbeTests.cs` in Ui.Tests with `new Avalonia.Controls.Window()` made `RealWindows_AreCreatedOnlyInRunAloneClasses` fail, naming the file; the file was then deleted. The first probe passed, which exposed the namespace-qualified gap that is now fixed and covered by a detector case |
| Deterministic partitions (`build-and-test` skill) | 5,924 / 5,924 passed, 0 skipped (5,914 baseline + 10 new guards). Per partition: Domain 467, Application 1,396, Infrastructure 1,350, Documents 484, Ui mixed 1,986, run-alone 30 + 144 + 2 + 7 + 4 + 54. UI partitions ran with `--blame-hang --blame-hang-timeout 90s` and a command-line-matched 3 GB watchdog (no kill; peak combined test-host RSS 457 MB). The first Infrastructure run had 1 failure, `RelativeMarkdownLinks_Resolve`: the testing guide already linked this record while it was still being written. The partition was rerun after the record existed: 1,350 / 1,350 |
| Clean Release build (solution) | 0 warnings, 0 errors (all `bin`/`obj` deleted, `--no-incremental`) |
| `tools/visual-fixtures` build | 0 warnings, 0 errors (was 1 warning, CS0618) |
| Fixture output unchanged | The original tool (HEAD `Program.cs`) and the new tool, run back to back on `Shell-Expanded main`: all 9 files (8 PNG + geometry) byte-identical. A run about 15 minutes after the baseline differed only in the two Passdown PNGs (57 and 147 bytes of decoded pixel data, geometry identical); this is time-dependent content (the 34C record notes 12 such scenarios), confirmed by the same-minute comparison |
| Fixture limits | `*` with `all` sizes → "would take 1224 captures", exit 2, no files written. Limit 10 against 16 planned → exit 2. Limit `abc` or `0` → exit 2. Working-set limit 400 MB → stopped after 6 of 16 captures, exit 3, 6-line geometry report written. 16 captures under the default → exit 0, peak RSS 679 MB |
| Windows validation | NOT EXECUTED — not required: no production, packaging or Windows-specific code changed |

## Documentation maintenance

The product specification describes product behavior, not developer tooling; no requirement changed.

**Product specification:** Reviewed — no changes required

`docs/architecture/testing-and-performance.md` now records the new guard, its count and the fixture limits. No
production architecture (dependencies, DI, state, persistence, sync, PDF, retrieval, lifecycle) changed.

**Architecture documentation:** Updated

## Known limitations and follow-ups

- **TD-02 not implemented.** A checked-in partition runner that applies `--blame-hang` and an RSS cap is new
  tooling and awaits a separate owner decision. Until then, the safeguards remain manual steps in the
  `build-and-test` skill, which now documents the macOS process-matching pitfall.
- **The window guard is pattern-based.** A window created through a target-typed `new()` or a shared factory
  would not be detected (stated in the guard's remarks).
- **Per-capture retention in the harness is unchanged** (about 22 MB). The limits bound it; they do not remove
  it, and its source was not investigated (tool-only, not a production leak per Phase 35G).
- **No 36C work was started.**
