# Windows release strategy (Phase 7A/7B)

This document defines the supported Windows release shape for Carowinds Passdown
(AGENT_BUILD_SPEC.md Phase 7 / §18 "Windows delivery"). Phase 7A defined and validated the publish
strategy before any ZIP existed; Phase 7B (below) resolves the Playwright dependency question left
open by 7A and builds the actual portable release ZIP. There is still no installer — the release
model is portable extract-and-run (see "Phase 7B — Playwright removal and the portable release
ZIP" below), and none is planned unless a later phase decides otherwise.

## Supported target

| | |
|---|---|
| OS | Windows 11 x64 |
| Architecture | x64 (`win-x64`) — no `win-arm64`; nothing in the spec or current hardware assumption requires it |
| Install model | Portable — extract-and-run, no installer, no admin rights (spec §18) |
| .NET runtime | Bundled (self-contained) — a clean Windows 11 machine must not need .NET installed separately |
| Publish shape | Normal publish directory (not single-file), not trimmed, not AOT |

## Self-contained vs framework-dependent

**Self-contained: `true`.** This is explicit in the spec ("Publish self-contained Windows x64",
§17 Phase 7) and matches §18's "no ... separate .NET installation" requirement for a nontechnical
user. No incompatibility was found against Avalonia, SQLite, QuestPDF, HTTP retrieval, or
Playwright — all publish cleanly self-contained for `win-x64` (verified by an actual `dotnet
publish`, not just inspection; see "Publish validation" below).

## Single-file / trimming / AOT

All three left at their default (`false`):

| Setting | Value | Why |
|---|---|---|
| `PublishSingleFile` | `false` | Avalonia loads XAML/resources and Skia/HarfBuzz native libraries at runtime in ways that are easy to get subtly wrong under single-file's extraction/bundling; QuestPDF and SQLite also ship native components. A normal folder is easier to troubleshoot on a nontechnical user's machine (e.g. simply looking at what's there) and is standard practice for Avalonia desktop apps. |
| `PublishTrimmed` | `false` | Avalonia's XAML loader and `Microsoft.Extensions.DependencyInjection`/`Configuration`/`Options` all rely on reflection this app never annotated for trimming. Trimming without that annotation work risks a runtime `MissingMethodException` that would only surface on Windows, not in any test run on the dev host. |
| `PublishAot` | `false` | Avalonia does not currently support Native AOT for a desktop target this way; not attempted. |

None of these were disabled because they're hard — they were evaluated and rejected because the
risk (a broken release only discoverable on Windows) outweighs the benefit (smaller download) for
an internal, nontechnical-audience tool. Revisit only with a concrete, tested reason.

## ReadyToRun (enabled — Phase 15D)

`PublishReadyToRun` is set to `true` (unconditionally in the csproj, but it only takes effect when
publishing with a RuntimeIdentifier — `dotnet build`/`test`/`run` are unaffected). Unlike the three
settings above, R2R keeps full IL and reflection support (it adds pre-JITted native code as a fast
path alongside the unchanged IL, with normal JIT still available as a fallback), so it does not
carry the same "only breaks on Windows" risk. It targets a real, measured cause of "packaged EXE
feels slow to open": a self-contained, non-R2R publish must JIT Avalonia/Skia/HarfBuzz/the app's
own assemblies from IL on every cold start, ahead of the composition-root/DB work that Phase
13A/15D profiling both measured at low-single-digit milliseconds. Verified in this environment: the
win-x64 crossgen2 cross-compilation pack restores and publishes cleanly from a non-Windows host
(~7% larger output). Actual wall-clock improvement on Windows is unverified — no Windows host was
available; see `docs/phase13-hardening.md`'s Phase 15D section and the Phase 15F Windows-validation
checklist item this leaves behind.

These decisions are recorded, not merely implied by leaving properties unset: see the comment
block in [`Carowinds.Passdown.Desktop.csproj`](../src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj).

## Publish command

Deterministic, scriptable, and reproducible from any dev machine (macOS/Linux/Windows) with the
.NET 10 SDK installed — no Windows-only tooling required to *produce* the publish output:

```bash
dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -o artifacts/publish/win-x64 \
    -p:PlaywrightPlatform=win-x64
```

Only the Desktop project is published (not the whole solution) — Domain/Application/Infrastructure/
Documents are pulled in as project references; nothing in the solution needs to be published
independently.

`-p:PlaywrightPlatform=win-x64` is not optional decoration — see "Playwright/browser dependency
behavior" below for why it's required when publishing from a non-Windows host, which is this
project's actual normal dev setup (CLAUDE.md "Development host: Fedora").

A wrapping script, [`packaging/windows/Publish-Windows.ps1`](../packaging/windows/Publish-Windows.ps1),
runs this same command plus a post-publish `.pdb` cleanup step (see "Publish output content audit").
It resolves the repo root from its own location, never a developer-specific path; requires no admin
rights; does not register the scheduled task or build a ZIP/installer; and does not touch user
application data. **The script itself was written and code-reviewed but not executed this phase**
— no PowerShell runtime (`pwsh`) is available on this session's dev host. The underlying `dotnet
publish` command it wraps *was* executed and validated directly (see "Publish validation" below);
only the PowerShell wrapper's execution is unverified. Running it on Windows or on a machine with
PowerShell installed is a reasonable Phase 7B/7E first step.

## Release staging directory

```
artifacts/
  publish/
    win-x64/
      Carowinds.Passdown.Desktop.exe
      ... (managed DLLs, native runtime files, appsettings.json, etc.)
```

`artifacts/` is gitignored. Nothing under it is committed; a fresh clone has none of it until
someone runs the publish script. Later packaging phases (ZIP, installer) should read from this
same directory rather than re-deriving their own publish step.

## Runtime dependency audit

A real `dotnet publish -r win-x64 --self-contained true` was run and its output inspected directly
(not just reasoned about):

- **Avalonia**: publishes cleanly; `Avalonia.Skia`/`SkiaSharp`/`HarfBuzzSharp` native libraries
  (`libSkiaSharp.dll`, `libHarfBuzzSharp.dll`) are Windows PE DLLs in the output, as expected for
  `win-x64`.
- **SQLite**: `Microsoft.Data.Sqlite`/`SQLitePCLRaw.*` resolve their native `e_sqlite3.dll`
  (confirmed present, confirmed `PE32+ ... x86-64, for MS Windows` via `file`).
- **QuestPDF**: publishes its `QuestPdfSkia.dll`/`qpdf.dll` native dependencies for `win-x64`; the
  license is already set (`QuestPDF.Settings.License = LicenseType.Community`,
  `AssociatePassdownPdfGenerator.cs`) — no license-related release blocker.
- **HTTP / System.Text.Json**: framework-provided; no extra native dependency.
- **No stray non-Windows binaries**: a `find . -iname "*.dylib" -o -iname "*.so"` over the publish
  output returned nothing — every native file present is a Windows PE binary (`file` confirms this
  for a sample). This matters specifically because publishing happens on a non-Windows dev host;
  it would be easy for a misconfigured build to leak host-platform binaries, and this was checked
  directly rather than assumed. (The one exception is Playwright's bundled Node driver — see next
  section, and why it needs an explicit flag to avoid exactly this.)

### Playwright/browser dependency behavior

Zero currently-implemented source adapters use Playwright — all eight (`ParkHoursSource`,
`DatedAdmissionSource`, `DatedFastLaneSource`, `DatedHauntedSource`,
`SeasonPassCatalogSource`/`SeasonPassAddOnsCatalogSource`/`PromotionsCatalogSource`/
`GeneralAddOnsCatalogSource`) are `DirectApi`/`Html` strategy (`docs/architecture.md`, "Phase
3B–3H additions"). `IJobBrowserSessionFactory`/`PlaywrightJobBrowserSession` exist and are wired
into the DI container but are dead code in production use today — this was true before Phase 7A
and remains true; nothing here changed that.

Despite being unused, the `Microsoft.Playwright` NuGet reference (`Carowinds.Passdown.Infrastructure`)
still contributes real bytes to every publish, because its build targets unconditionally copy a
bundled Node.js driver into the output (`.playwright/node/...`, `.playwright/package/...`,
`playwright.ps1`) — this driver is what `playwright install <browser>` would use if ever invoked;
**no browser binaries themselves (Chromium/Firefox/WebKit) are bundled or downloaded** — those are
a separate, much larger download only `playwright install` fetches, and nothing in this codebase or
publish process calls that. If a future phase adds a Playwright-based adapter, installing the
actual browser binary onto the target Windows machine becomes a real, separate packaging decision
at that time — not solved here.

**A genuine bug was found and fixed in the publish command itself, not worked around later:**
Publishing `-r win-x64` from a non-Windows dev host (this repo's actual setup) causes
`Microsoft.Playwright`'s build target to bundle **both** the `win32_x64` Node driver **and** the
*host* platform's driver (verified: publishing from this macOS/arm64 dev host, without the fix
below, produced both `.playwright/node/win32_x64` (88 MB) and `.playwright/node/darwin-arm64`
(115 MB) in the `win-x64` output — 115 MB of macOS ARM64 binaries that can never run on the shipped
Windows target, confirmed via a clean-cache rebuild to rule out stale `obj/`/`bin/` artifacts, not
just a one-off). Passing `-p:PlaywrightPlatform=win-x64` explicitly (rather than letting the
target infer it from the host OS) fixes this — verified: the same clean rebuild with the flag
produced only `win32_x64` (88 MB). This flag is baked into both the documented publish command and
`Publish-Windows.ps1`. It is a no-op when publishing directly on a Windows host (the target would
already resolve to `win` alone there), so it is always safe to pass.

**Open item, deliberately not resolved in Phase 7A** (in scope only to "prove the publish
strategy," not to build the final release): the Playwright Node driver still adds ~88 MB
(`win32_x64` only, post-fix) to every release for a feature zero production sources use. Excluding
it entirely would mean conditioning or removing the `Microsoft.Playwright` `PackageReference`
itself, which is a real dependency/build-graph change, not a publish-flag change — appropriately
deferred to Phase 7B (release-size optimization) or to whenever a Playwright adapter actually ships
(at which point it's needed anyway). Documented here so it isn't rediscovered from scratch later.

## Publish output content audit

Inspected the actual `artifacts/publish/win-x64` output produced this phase (387 files, 236 MB
after `.pdb` removal):

**REQUIRED (present, correct):**
- `Carowinds.Passdown.Desktop.exe` and all managed `Carowinds.Passdown.*.dll`
- The .NET 10 self-contained runtime (`coreclr.dll`, `System.*.dll`, etc.)
- Native runtime files (`e_sqlite3.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `qpdf.dll`,
  `QuestPdfSkia.dll`, `av_libglesv2.dll`, Avalonia's ANGLE dependency)
- `appsettings.json` (consumed at startup by `App.BuildServiceProvider`)
- `LatoFont/` (Avalonia's bundled `Inter` font assets... actually shipped under Avalonia's own
  resource path; present because `Avalonia.Fonts.Inter` is referenced)

**OPTIONAL, as of Phase 7A (removed in Phase 7B — see below):**
- `.playwright/` — the unused-today Playwright Node driver; see above. Left in for now (a
  `PackageReference` removal is out of scope for a publish-strategy phase), tracked as a Phase 7B
  candidate for size reduction. **Phase 7B update: removed.** The `Microsoft.Playwright`
  `PackageReference` was dropped after confirming zero production code path could reach it (see
  "Phase 7B — Playwright removal and the portable release ZIP" below); `.playwright/` no longer
  appears in the publish output at all.
- Third-party `README.md`/`SKILL.md` files bundled *inside* `.playwright/package/` by the
  Playwright npm package itself — not this repo's own docs, not a leak of `CLAUDE.md`/
  `AGENT_BUILD_SPEC.md` (confirmed: a `find . -iname "*.md"` over the publish output returned only
  these third-party files). Gone along with `.playwright/` itself as of Phase 7B.

**Confirmed absent (correctly excluded):**
- `.pdb` files — the compiler-generated managed `.pdb`s never reach the staging output
  (`-p:CopyOutputSymbolsToPublishDirectory=false` behavior is subsumed by the script's own explicit
  delete step, since two native content `.pdb`s — `libSkiaSharp.pdb` 80 MB,
  `libHarfBuzzSharp.pdb` 20 MB — are copied as package *content*, not compiler output, and are
  untouched by that MSBuild property). `Publish-Windows.ps1` deletes every `*.pdb` from the staging
  directory after publish for exactly this reason — verified: this alone drops the output from
  336 MB to 236 MB.
- Test assemblies (`find . -iname "*.Tests.dll"` — empty)
- Source files, `.git`, `.claude`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `obj/`, `.DS_Store` — none
  present; these are never publish inputs in the first place (publish only copies build outputs
  and declared `Content`/`None` items), confirmed by direct inspection rather than assumed.

## Versioning strategy

Single authoritative source: `Directory.Build.props`' `<Version>0.1.0-dev</Version>`, applied to
every project in the solution (spec Phase 7 "Prefer one authoritative version source... Avoid
duplicating version numbers across many scripts"). The .NET SDK derives:

- `AssemblyVersion` / `FileVersion`: `0.1.0.0` (numeric only)
- `InformationalVersion`: `0.1.0-dev`

**This is an explicit development version, not an official product release number** — the spec
does not assign one yet (CLAUDE.md "do not invent data" applies to version numbers too), and
`-dev` marks it as such rather than pretending otherwise. Bump `<Version>` by hand when a real
release is cut; there is no semantic-release automation, per the explicit instruction not to build
one.

`<Product>Carowinds Passdown</Product>` (also in `Directory.Build.props`) sets the built
executable's file-properties "Product name" to the user-facing name, independent of the binary
file name (see "Application/executable name" below). `tests/Carowinds.Passdown.Ui.Tests/Release/ReleaseVersionTests.cs`
guards that both actually flow into the built Desktop assembly, rather than silently reverting to
SDK defaults if `Directory.Build.props` is ever edited incorrectly.

## Application / executable name

Executable stays `Carowinds.Passdown.Desktop.exe`. Renaming it would touch:

- The registered Windows Task Scheduler action (`ScheduledTaskPlan`/`Install-ScheduledRefresh.ps1`)
- `ScheduledTaskPlanTests`' path assertions
- `docs/scheduled-refresh-task-scheduler.md`'s documented commands
- Every packaging script path assumption going forward

None of that is justified by a cosmetic rename — the *displayed* product name is already
"Carowinds Passdown" (window title, and now the `.exe`'s file-properties "Product name" field via
`<Product>` above), which is what a nontechnical user actually sees. The internal binary file name
being the assembly name is normal and not a release blocker. Deferred, not forgotten.

## Scheduled-refresh packaging contract

No changes needed to `packaging/windows/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1`
— they already only need `-ExecutablePath <absolute installed path>` and already handle:

- Paths containing spaces (passed as separate `-Execute`/`-Argument`/`-WorkingDirectory` values to
  `New-ScheduledTaskAction`, never concatenated — see the script's own comments and
  `ScheduledTaskPlanTests.QuotedCommandLine_PathWithSpaces_IsQuoted`)
- Working directory derived from the executable path, not assumed
- Idempotent install (`-Force` replaces only the one named task) and safe repeated
  install/uninstall

The Phase 7 contract is simply:

```
artifacts/publish/win-x64/Carowinds.Passdown.Desktop.exe   (this phase's output)
        ↓ (a later phase copies/extracts this to wherever it's actually installed)
<final installed path>\Carowinds.Passdown.Desktop.exe
        ↓
Install-ScheduledRefresh.ps1 -ExecutablePath "<final installed path>\Carowinds.Passdown.Desktop.exe"
```

`ScheduledTaskPlanTests.Build_WindowsDriveRootedPath_SetsWorkingDirectoryToExecutableDirectory`
already exercises a `C:\Program Files\Carowinds Passdown\...` style path (i.e. a realistic future
install location, spaces included) — this contract was already correctly covered before Phase 7A;
nothing new was needed here.

## Legacy local-data collision review

Carried over from the Phase 6D flag (`docs/architecture.md` "Phase 6D... Flagged as a Phase 7
packaging/first-run item"). Every file the greenfield app writes under
`%LOCALAPPDATA%\Carowinds Passdown`:

| File | Source |
|---|---|
| `carowinds-passdown.db` (+ `-wal`/`-shm` while open) | `StorageOptions.DatabaseFileName` (`appsettings.json`), `SqliteConnectionFactory` |
| `scheduled-refresh.lock` | `FileScheduledRefreshLock.DefaultPath` |
| `logs\scheduled-refresh-yyyyMMdd.log` | `FileLoggerOptions.LogDirectory` |

No config/preferences file, no separate draft-storage file, and no cache file beyond the SQLite
database itself exist — drafts live as rows in `carowinds-passdown.db` (`Migration0006CreateAssociateDraftsTable`),
not a separate file.

**Verdict: PASS.** The database filename (`carowinds-passdown.db`) is specific to this rebuild and
was never used by the previous application (per the Phase 6D note, filenames were reviewed against
what's known of the legacy app in this development environment/docs; no matching name was found).
`scheduled-refresh.lock` and the `logs\` directory are both new concepts introduced by this
rebuild (Phase 6A/6B) with no legacy equivalent to collide with. This remains a review, not an
exhaustive file-by-file diff against a legacy installation (none is available in this dev
environment) — if a legacy machine's actual directory contents ever become available, a direct
listing comparison would upgrade this from "PASS (reviewed)" to "PASS (verified against a real
legacy directory)." No code changes were made or needed; nothing was deleted, renamed, or migrated.

## First-run filesystem expectations

On a machine with no existing data:

```
Carowinds.Passdown.Desktop.exe starts
    ↓
App.Initialize() builds the service provider (App.axaml.cs BuildServiceProvider)
    ↓
AppPaths.LocalApplicationDataDirectory resolved from Environment.SpecialFolder.LocalApplicationData
  (never CWD, never a repo/install-relative path — see AppPathsTests, added this phase)
    ↓
SqliteConnectionFactory creates the directory and the .db file on first use
    ↓
MigrationRunner.Run() applies all pending migrations (App.OnFrameworkInitializationCompleted,
  before any ViewModel touches the database)
    ↓
MainWindow opens; StartupCatchUpRefresh fires detached in the background
```

Confirmed no release-path assumptions: `appsettings.json` is loaded via
`ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)` (the executable's own directory,
correct wherever it's installed) — not `Directory.GetCurrentDirectory()`. A repo-wide search for
`GetCurrentDirectory`, `../../../`, hard-coded `src/`/`bin/Debug`/repo-root patterns in
`src/**/*.cs` found nothing. The publish output requires no writable install directory — the only
things it writes are under `AppPaths.LocalApplicationDataDirectory`, confirmed by the audit above.

## Cross-platform development

Unaffected by any Phase 7A change. `dotnet build CarowindsPassdown.slnx` and
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` still build/run with no RID
specified and no Windows dependency — verified this phase (603 → 609 tests, see "Publish
validation" below; the 6 added are this phase's own new tests). Nothing in `Directory.Build.props`
or the Desktop `.csproj` forces a RID onto the ordinary build; `win-x64` is only ever requested
explicitly via `-r win-x64` on the publish command.

## Publish validation

Performed this phase, on this (non-Windows) dev host:

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **PASS**, 609/609
   (603 pre-existing + 6 new: 3 `AppPathsTests` + 3 `ReleaseVersionTests`).
3. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r
   win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` —
   **PASS**. Output inspected directly: executable present, 387 files, 236 MB after `.pdb`
   removal, no test assemblies, no stray non-Windows native binaries, no repo source/doc files, no
   `.DS_Store`.

## Phase 7B — Playwright removal and the portable release ZIP

### Playwright dependency decision

**Decision: removed.** The `Microsoft.Playwright` `PackageReference` was removed from
`Carowinds.Passdown.Infrastructure.csproj` entirely, along with every Playwright-typed class that
existed only to support it (`PlaywrightBrowserSession`, `PlaywrightBrowserSessionFactory`,
`PlaywrightBrowserSessionOptions`, `PlaywrightJobBrowserSession`, `PlaywrightJobBrowserSessionFactory`,
and the Infrastructure-internal `IBrowserSession`/`IBrowserSessionFactory` abstractions, which were
typed directly against Playwright's `IPage` and had no consumer outside that cluster).

This was a code-reachability decision, not a size-only one. Tracing the actual runtime path:

- `RefreshOrchestrator` only calls `browserSessionFactory.Create()` when
  `plannedOperations.Any(p => p.Registration.RequiresBrowserSession)` is true
  (`src/Carowinds.Passdown.Application/Refresh/RefreshOrchestrator.cs`).
- Every one of the 8 production `SourceRegistration`s wired up in
  `App.axaml.cs`/`AddRefreshOrchestration` uses `DatedSourceRegistration`/`CatalogSourceRegistration`'s
  default `requiresBrowserSession: false` — none passes `true`.
- Therefore `needsBrowserSession` is always `false` in production, `Create()` is never called, and
  the Playwright-backed `StartAsync()` that actually launches a browser process was never reachable
  — confirmed by reading the call sites, not inferred from package size alone.

`Application`'s `IJobBrowserSessionFactory`/`IJobBrowserSession` abstractions
(`src/Carowinds.Passdown.Application/Retrieval/Browser/IJobBrowserSession.cs`) are untouched —
`RefreshOrchestrator` still depends only on those, never on a concrete browser implementation, so
the architecture boundary (CLAUDE.md "Architecture": Application has no Playwright dependency) was
already correct and stays correct.

**Replacement** (`src/Carowinds.Passdown.Infrastructure/Retrieval/Browser/UnavailableJobBrowserSession.cs`):
`UnavailableJobBrowserSessionFactory` implements `IJobBrowserSessionFactory` and is registered in
its place in `App.axaml.cs`. Its `Create()` does no I/O (matching the interface contract); the
returned session's `EnsureStartedAsync()` throws `NotSupportedException` with a message pointing at
this decision, rather than either (a) silently pretending browser support exists, or (b) being
unreachable dead code with no signal if it ever *were* reached. Nothing in production ever triggers
this throw today. A new regression test
(`tests/Carowinds.Passdown.Infrastructure.Tests/PlaywrightDependencyTests.cs`) asserts the
`Carowinds.Passdown.Infrastructure` assembly no longer references `Microsoft.Playwright`, so the
dependency can't silently come back through an unrelated package bump.

If a future phase adds a genuine browser-backed source adapter, it needs its own real
`IJobBrowserSessionFactory` implementation (Playwright or otherwise) and to flip that source's
registration to `requiresBrowserSession: true` — this is unchanged, deliberate, future work, not
something this phase blocks.

### Retrieval sources: unaffected

All 8 production sources (Park Hours, Dated Admission, Dated Fast Lane, Haunted Attractions, Season
Passes, Season Pass Add-ons, Promotions, General Add-ons) are `DirectApi`/`Html` strategy and were
not modified. They only ever mentioned Playwright in doc comments ("no Playwright needed") — no
production adapter file imports the `Microsoft.Playwright` namespace. Build and the full
`Category!=LiveSmoke` suite both pass after the removal (see "Publish validation," updated below).

### Publish size, before vs. after

| | Files (pre-.pdb-removal) | Size (pre-.pdb-removal) | Files (final) | Size (final) |
|---|---|---|---|---|
| Phase 7A (with Playwright) | — | — | 387 | 236 MB |
| Phase 7B (without Playwright) | 278 | 234 MB | 271 | 133 MB |

**~103 MB saved** (236 MB → 133 MB), all of it the Playwright Node driver tree (`.playwright/`,
`playwright.ps1`) — confirmed absent by direct inspection (`find . -iname "*playwright*"` over the
new publish output returns nothing). No other publish content changed: same executable, same
managed DLLs, same native Windows runtime files (`e_sqlite3.dll`, `libSkiaSharp.dll`,
`libHarfBuzzSharp.dll`, `qpdf.dll`, `QuestPdfSkia.dll`, `av_libglesv2.dll`), no stray non-Windows
binaries, no test assemblies.

### Release ZIP

Built by `packaging/windows/Build-PortableRelease.ps1`, which reuses `Publish-Windows.ps1` for the
actual publish (never duplicates its publish rules) and reads the release version from
`Directory.Build.props`' single authoritative `<Version>` — no second version source.

- **Filename**: `Carowinds-Passdown-0.1.0-dev-win-x64.zip`
- **Layout**: one top-level folder, `Carowinds-Passdown-0.1.0-dev-win-x64/`, containing the
  publish output directly (`Carowinds.Passdown.Desktop.exe`, `appsettings.json`, managed DLLs,
  native runtime files, `LatoFont/OFL.txt`) plus two additions:
  - `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1` — unmodified
    copies of the existing scheduled-refresh helpers (already accept an absolute
    `-ExecutablePath`, never hard-code a developer path). Extracting the ZIP does not register the
    task; a user runs the install script deliberately, and the normal app launch is unaffected
    either way.
  - `README.txt` — a short, nontechnical extract/launch/data-location/removal guide (source:
    `packaging/windows/README-portable-release.md`), distinct from the developer docs under
    `docs/`.
- **File count**: 277 zip entries (274 files + 3 directory entries: the release root, `LatoFont/`,
  `tools/`).
- **Sizes**: 133 MB uncompressed staged folder, 59 MB compressed ZIP.

Portable semantics (spec §18, no installer):

- **Install**: extract anywhere writable; no admin rights; no Program Files assumption.
- **Uninstall**: delete the extracted folder. There is no separate uninstaller.
- **User data**: lives outside the extracted folder, at `%LOCALAPPDATA%\Carowinds Passdown`
  (`carowinds-passdown.db` + `-wal`/`-shm`, `logs\`, `scheduled-refresh.lock` — see "Legacy
  local-data collision review" above). Deleting the extracted app folder never touches this.
- **Scheduled task**: if `Install-ScheduledRefresh.ps1` was run, it is a separate OS-level
  registration and is not removed by deleting the app folder — `Uninstall-ScheduledRefresh.ps1`
  must be run separately.

### License/notice and privacy review

- `LatoFont/OFL.txt` (SIL Open Font License, bundled by `Avalonia.Fonts.Inter`'s publish content)
  was retained — it's a required third-party notice, not an internal doc, and nothing else in the
  publish output carries a license/notice file (`find . -iname "*.md" -o -iname "*.txt"` over the
  staged release returns only this one file plus the new `README.txt`).
- `appsettings.json` contains only `Logging`/`Storage:DatabaseFileName` — no secret or
  machine-specific value.
- The staged/zipped release was inspected directly for `.git`, source, `CLAUDE.md`,
  `AGENT_BUILD_SPEC.md`, `.DS_Store`, `.pdb`, test assemblies, non-Windows binaries, and any
  `.db`/`.db-wal`/`.db-shm` file — none found. `Build-PortableRelease.ps1` also runs this same
  forbidden-pattern check itself before zipping, failing the build if any match.

### Build/test/publish validation (Phase 7B)

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **PASS**, 607/607 (609
   Phase 7A total − 6 tests deleted with the removed Playwright classes they exercised + 4 new:
   3 `UnavailableJobBrowserSessionTests` + 1 `PlaywrightDependencyTests`).
3. `dotnet publish ... -r win-x64 --self-contained true ...` — **PASS**. Output inspected directly:
   271 files, 133 MB after `.pdb` removal, no `.playwright`, no stray non-Windows binaries.
4. `packaging/windows/Build-PortableRelease.ps1` — **written and reviewed, not executed**; no
   PowerShell runtime (`pwsh`) is available on this dev host, the same limitation Phase 7A recorded
   for `Publish-Windows.ps1`. Its exact staging/zip logic was run manually (shell + `zip`) to
   produce and inspect the real `Carowinds-Passdown-0.1.0-dev-win-x64.zip` reported above. Running
   the actual `.ps1` on Windows or a host with PowerShell installed remains a Phase 7B/7E follow-up,
   same as `Publish-Windows.ps1` was in Phase 7A.

### Windows runtime validation: deferred by design

No clean Windows 11 VM was used this phase. Launching the built `.exe`, running
`--scheduled-refresh` against it, and executing `Publish-Windows.ps1`/`Build-PortableRelease.ps1`
under a real PowerShell host all remain **Phase 7E**, exactly as Phase 7A scoped it. This phase's
ZIP was validated by extracting it into a fresh local temp directory and inspecting the resulting
file tree (Part P of the Phase 7B task) — not by running the Windows binary.

## Phase 7C — nontechnical scheduled-refresh setup helpers

Phase 7B's portable ZIP worked, but enabling optional background refresh still required a
nontechnical user to open PowerShell, resolve their own extracted path, and (per a real problem
hit during Phase 6C manual validation) possibly fight a "running scripts is disabled on this
system" execution-policy error. Phase 7C closes that gap without turning the release into an
installer.

### Chosen mechanism: double-clickable `.cmd` wrappers

Evaluated against Part B's options: a small compiled setup executable and an in-app "Enable
Automatic Refresh" button were both rejected as disproportionate to the problem — a compiled
helper adds a second thing to build/sign/version for one Task Scheduler call, and an in-app button
would mean either a new Windows-only Infrastructure abstraction over Task Scheduler plus Desktop
settings UI (real architectural surface for a portable, no-service app) or shelling out to the
same `.ps1` scripts from inside the running app for no real benefit over a direct double-click
(see "In-app setup: rejected" below). A `.cmd` wrapper is the smallest mechanism that is still
genuinely one double-click, needs no compiler/signing step, and reuses 100% of the existing,
already-tested Phase 6C Task Scheduler logic (`ScheduledTaskPlan`,
`Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1`) completely unmodified.

Two new files, both staged at the release root (next to the `.exe`, not under `tools\`, so they're
the first thing a user sees):

- `packaging/windows/Enable Automatic Refresh.cmd`
- `packaging/windows/Disable Automatic Refresh.cmd`

Each one:

1. Resolves its own directory via `%~dp0` (never a hard-coded or developer-specific path).
2. (Enable only) Builds `Carowinds.Passdown.Desktop.exe`'s path relative to itself and verifies
   the file exists with a clear message — not a PowerShell stack trace — before doing anything
   else.
3. Invokes the existing `tools\Install-ScheduledRefresh.ps1` / `tools\Uninstall-ScheduledRefresh.ps1`
   (also resolved relative to `%~dp0`) via
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<script>" ...`, passing only the
   resolved absolute executable path (Enable) or no extra arguments at all (Disable — the
   uninstall script takes none and only ever targets the one fixed task name).
4. Prints a concise result and `pause`s so a double-click user (no visible console otherwise) can
   read it before the window closes.

### Execution-policy handling (Part D)

`-ExecutionPolicy Bypass` on the `powershell.exe -File` invocation is scoped to that one child
process only — it is a process-launch flag, not a call to `Set-ExecutionPolicy`, and neither
helper ever calls `Set-ExecutionPolicy` (verified by `PortableSetupHelperTests.Helper_UsesProcessScopedExecutionPolicyBypassOnly`,
which asserts the string is present and `Set-ExecutionPolicy` is not). Nothing about the machine's
or user's persistent execution-policy setting is read, written, or needs to change — a user whose
policy would otherwise block double-clicking a `.ps1` directly (the Phase 6C symptom) is
unaffected, because they never invoke a `.ps1` directly; the `.cmd` is what they double-click, and
`.cmd` files are unaffected by PowerShell's execution policy in the first place.

### No admin, no elevation (Part D/J)

Neither helper requests elevation: no `runas`, no elevated manifest, no `RunLevel Highest`. This
matches the underlying scripts unchanged from Phase 6C — `Install-ScheduledRefresh.ps1` already
registers a per-user task (`LogonType Interactive`, `RunLevel Limited`, no stored password), which
needs no admin rights on a normal Windows 11 account.

### Portable relocation (Part G)

No automatic move-detection was added — deliberately, per Part G's explicit "do NOT add complex
automatic relocation detection unless clearly supported." The scheduled task's registered action
always points at the absolute path that existed at the moment `Enable Automatic Refresh.cmd` was
last run (`Install-ScheduledRefresh.ps1`'s `-Force` already makes re-registration idempotent —
Phase 6C). The documented recovery path is simply: after moving the extracted folder, double-click
`Enable Automatic Refresh.cmd` again from the new location; this replaces the existing
"Carowinds Passdown Daily Refresh" task with one pointing at the new path. No in-app stale-path
detection was added either — the spec explicitly discourages complexity here, and the app's
existing `StartupCatchUpRefresh` (Phase 6B) already provides a safety net for any refresh missed
while a task briefly points at a since-moved location.

### Removal (Part H)

Unchanged from Phase 7B, now easier to act on: disable automatic refresh (if enabled) via the new
`Disable Automatic Refresh.cmd`, close the app, delete the extracted folder. User data under
`%LOCALAPPDATA%\Carowinds Passdown` (database, drafts, cache, logs) is never touched by either
helper or by deleting the app folder — nothing in this phase changed what those scripts read or
write beyond the two new `.cmd` wrappers calling them.

### In-app setup: rejected (Part I)

A settings-style "Automatic Refresh: Enabled/Disabled" toggle in the Desktop UI was evaluated and
rejected for Phase 7C. It would require either a new Windows-only Infrastructure abstraction over
Task Scheduler (crossing the CLAUDE.md Domain/Application/Infrastructure boundary for a feature
that exists purely to configure an OS-level trigger) plus new Desktop settings UI/ViewModel, or
having the running app shell out to the same PowerShell scripts the `.cmd` helpers already call —
which adds UI surface without removing any complexity the double-click helpers don't already
solve. Per Part I's explicit guidance ("if it requires significant new settings UI... prefer the
double-click helper files instead and document that choice"), the double-click helpers are the
Phase 7C answer; an in-app toggle remains available to reconsider in a later phase if a real
product need (not just convenience) emerges.

### Status inspection: deferred (Part K)

A `Check Automatic Refresh Status.cmd` helper was considered and not added this phase. The task
name is already duplicated by hand across `ScheduledTaskPlan.TaskName` (C#),
`Install-ScheduledRefresh.ps1`, and `Uninstall-ScheduledRefresh.ps1` (each carries a comment
flagging this); a fourth hand-synced copy embedded in a new `.cmd` was judged not worth the
drift risk for a read-only convenience the spec explicitly marks optional ("Do not overcomplicate
this. A status helper is optional."). `Install-ScheduledRefresh.ps1`'s own console output already
states the resolved trigger time on every run (an implicit status check: re-running "Enable
Automatic Refresh.cmd" harmlessly reports "Updated existing scheduled task" if one was already
registered), and Windows' own Task Scheduler UI remains available for anyone who wants
`LastTaskResult`/next-run detail. Can be added later if real usage shows it's needed.

### Security review (Part J)

- No user-provided shell command fragments: both `.cmd` files take no arguments and build the two
  paths they need (`%APP_DIR%`-relative) purely from `%~dp0`, never from user input or an
  environment variable an attacker could pre-seed.
- Both path variables are double-quoted at every use (`"%APP_EXE%"`, `"%INSTALL_SCRIPT%"`,
  `"%UNINSTALL_SCRIPT%"`), which is what actually protects a path containing spaces, `&`, `(`, `)`,
  etc. under `cmd.exe`'s parser — quotes suppress those characters' special meaning for the
  duration of the quoted token.
- Fixed, non-overridable task name and argument: neither helper ever passes `-TaskName` or any
  override to the PowerShell scripts, so they can only ever act on
  `"Carowinds Passdown Daily Refresh"` / `--scheduled-refresh` — the same constants
  `ScheduledTaskPlan.TaskName` / `CommandLineArguments.ScheduledRefreshFlag` already guard
  elsewhere. `PortableSetupHelperTests` asserts this by string-matching the actual shipped files
  rather than by inspection alone.
- No elevation, no `RunLevel Highest`, no `runas` in either file.
- No credentials, no stored password — same as the unmodified Phase 6C scripts.
- No permanent execution-policy modification — see above.
- Only the trusted script shipped in this exact release's `tools\` folder is ever executed; the
  `.cmd` never downloads, generates, or executes anything else.

### Tests added

`tests/Carowinds.Passdown.Ui.Tests/Release/PortableSetupHelperTests.cs` (17 tests) — static content
contracts read directly from the shipped `packaging/windows/*.cmd`/`*.ps1` files and
`Build-PortableRelease.ps1`, matching Part P: both helpers exist and are staged at the release
root; both resolve their own directory via `%~dp0`; the enable helper resolves and verifies the app
executable relative to itself; neither helper contains a developer-specific path fragment; neither
permanently changes execution policy; the task name and `--scheduled-refresh` argument stay in
sync with the `ScheduledTaskPlan`/`CommandLineArguments` C# constants; the disable helper passes no
extra arguments to the uninstall script; `Build-PortableRelease.ps1` fails loudly if either helper
file is missing. No test asserts brittle line-by-line `.cmd` syntax, per Part P's guidance.

### Release layout (Part L)

```
Carowinds-Passdown-0.1.0-dev-win-x64/
    Carowinds.Passdown.Desktop.exe
    README.txt
    Enable Automatic Refresh.cmd
    Disable Automatic Refresh.cmd
    appsettings.json
    *.dll  (managed + native runtime files)
    LatoFont/
    tools/
        Install-ScheduledRefresh.ps1
        Uninstall-ScheduledRefresh.ps1
```

### README (Part M)

`packaging/windows/README-portable-release.md` (shipped as `README.txt`) was rewritten to lead
with the double-click helpers as the primary path — "double-click Enable/Disable Automatic
Refresh.cmd" — with a new "Moving this folder" section documenting the relocation recovery step,
and a "How the setup helpers work" section explaining (in plain language, no architecture detail)
that the helpers call the `tools\` scripts automatically and touch no persistent Windows setting.
The underlying `tools\*.ps1` scripts are still shipped unmodified for anyone who prefers to run
them directly.

### Build-PortableRelease.ps1 integration (Part N)

`Build-PortableRelease.ps1` now copies both `.cmd` helpers from `packaging/windows/` to the release
staging root (alongside the existing `tools\` and `README.txt` copy steps) and fails the build with
a clear error if either source file is missing, rather than silently shipping a ZIP without them —
consistent with the script's existing "fail loudly rather than ship something wrong" pattern for
the README/Publish-Windows.ps1 checks. No publish configuration was duplicated; the script still
delegates the actual `dotnet publish` step to `Publish-Windows.ps1` exactly as before.

### Windows execution status (Part O/Q)

No `pwsh` is available on this session's dev host (same limitation Phase 7A/7B recorded). The
underlying `dotnet publish` and the full staging/zip sequence `Build-PortableRelease.ps1` performs
were run manually this phase (shell commands mirroring the script's own logic exactly, the same
approach Phase 7B used) to produce and directly inspect a real ZIP with the new layout:

- **Files staged**: 276 (271 publish output files + `tools/Install-ScheduledRefresh.ps1` +
  `tools/Uninstall-ScheduledRefresh.ps1` + the 2 new `.cmd` helpers + `README.txt`)
- **Uncompressed size**: 132.8 MB
- **ZIP size**: 58.6 MB
- **ZIP filename**: `Carowinds-Passdown-0.1.0-dev-win-x64.zip`
- Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`)
  — clean.
- No test assemblies, no `.playwright`/Playwright content, no stray non-Windows (`.dylib`/`.so`)
  binaries — all clean, same as Phase 7B.
- Both new helper files confirmed present in the ZIP with their spaced filenames intact
  (`Enable Automatic Refresh.cmd`, `Disable Automatic Refresh.cmd`), 1830 and 1506 bytes
  respectively.

**Actually double-clicking either `.cmd` on a real Windows machine, confirming the no-admin/no-UAC
behavior end-to-end, confirming `LastTaskResult = 0` after a manual task run, and confirming the
relocate-and-re-enable path all remain Phase 7E**, exactly as Windows execution of
`Publish-Windows.ps1`/`Build-PortableRelease.ps1` themselves has been deferred since Phase 7A. This
phase's `.cmd` content was validated by static review plus the automated tests above, not by
running it.

### Phase 7E manual validation checklist (prepared, not executed — Part R)

1. Extract the actual Phase 7C ZIP on a clean Windows 11 machine.
2. Launch the app by double-clicking `Carowinds.Passdown.Desktop.exe`.
3. Double-click `Enable Automatic Refresh.cmd`.
4. Confirm no admin/UAC prompt appears.
5. Inspect the registered task (`Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh" |
   Get-ScheduledTaskInfo`, or Task Scheduler's UI).
6. Manually run the task (`Start-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh"`, or
   "Run" from the UI).
7. Confirm `LastTaskResult = 0`.
8. Double-click `Disable Automatic Refresh.cmd`.
9. Confirm the task is removed (`Get-ScheduledTask` returns nothing for that name).
10. Double-click `Enable Automatic Refresh.cmd` again — confirm it re-registers cleanly.
11. Move/copy the whole extracted folder to a different path.
12. Double-click `Enable Automatic Refresh.cmd` from the new location.
13. Confirm the scheduled task's action now points at the new path (not the old one), and that
    only one "Carowinds Passdown Daily Refresh" task exists (no duplicate).
14. Confirm the app still launches normally from the new location.

**Phase 7D data-preservation cases (added this phase):**

15. On first launch (step 2), enter a small Associate draft and save it. Close the app.
16. Relaunch the same `.exe`. Confirm the saved draft is still there.
17. Extract a **second, fresh copy** of the same release ZIP to a different folder. Launch it.
    Confirm it opens against the *same* draft from step 15/16 (same `%LOCALAPPDATA%`, different
    app folder).
18. Delete the *original* extracted folder from step 1 entirely (not the copy from step 17).
    Relaunch the app from the copy in step 17. Confirm the draft is still present — proving
    `%LOCALAPPDATA%\Carowinds Passdown` was never touched by deleting the app folder.
19. (Disposable copy only — never the real user-data folder) Close the app, make a throwaway copy
    of `carowinds-passdown.db`, corrupt it (e.g. overwrite the first few bytes with a text
    editor), and confirm the app fails visibly on next launch rather than silently creating a
    blank database or deleting the corrupt file. Restore the real file afterward.
20. If a newer portable build is available, repeat steps 15–18 across the two builds (older build
    first, then the newer one) to confirm the upgrade path preserves the draft and reaches the
    newer schema version.

## Phase 7D — first-run, upgrade, legacy coexistence, and portable-move guarantees

Phase 7C proved the release ZIP is easy to set up. Phase 7D proves the underlying data-safety
contract that makes the release safe to *live with* over time: extract-and-run, replace, move, or
delete the app folder as often as needed, and never lose or corrupt a user's drafts/cache. The
core invariant, unchanged from anything stated earlier in this document but made explicit and
tested here:

> **Application binaries are disposable. User data is persistent and independent.**

### Supported lifecycle

```
FIRST USE
    Extract ZIP → launch .exe → %LOCALAPPDATA%\Carowinds Passdown created if needed
        → carowinds-passdown.db created → migrations applied → app opens

RESTART
    Close app → relaunch same .exe → existing database reused
        → migrations re-run (idempotent, no-op) → saved drafts/cache remain

PORTABLE APP UPDATE (no installer — "reinstall" = extract a fresh release folder)
    Close app → replace/re-extract application folder → launch new .exe
        → same LocalAppData database reused → pending migrations applied → user data preserved

PORTABLE MOVE
    Move the extracted application folder → launch from the new location
        → same LocalAppData database reused (AppPaths resolves from
          Environment.SpecialFolder.LocalApplicationData, never from the .exe's own location)
    If automatic refresh was enabled: re-run "Enable Automatic Refresh.cmd" from the new location
        → the one existing scheduled task's action is updated to the new .exe path (no duplicate
          task — Install-ScheduledRefresh.ps1's Register-ScheduledTask -Force always targets the
          same fixed ScheduledTaskPlan.TaskName)
```

Recommended update steps (documented for a nontechnical coworker performing an update):

1. Close the app.
2. Extract the new release to a fresh folder (or over the old one).
3. Launch the app — existing drafts/cache are still there; any pending migration applies
   automatically on this launch.
4. If automatic refresh was enabled and the app moved to a new folder, double-click
   `Enable Automatic Refresh.cmd` again from the new location. If the folder path didn't change,
   this step isn't necessary.

### First-run / restart / upgrade — automated coverage

`tests/Carowinds.Passdown.Infrastructure.Tests/Storage/PortableLifecycleTests.cs` (new this
phase) exercises this against temp-directory databases standing in for
`%LOCALAPPDATA%\Carowinds Passdown` — never the developer's real LocalAppData:

- **Clean first run**: target directory absent → `MigrationRunner` (the exact 7-migration list
  `App.axaml.cs` registers) creates it, creates the database, and reaches schema version 7. A
  second initialization against the same path is a no-op — no duplicate `schema_version` rows, no
  error, no assumption that any draft/cache row already exists.
- **Restart persistence**: one representative row is written to each of `associate_drafts`,
  `source_state`, `dated_results`, and `catalog_products` via the real repositories; a *new*
  `SqliteConnectionFactory` over the same file (simulating a process exit) then re-runs
  `MigrationRunner` (exactly what `App.axaml.cs` does on every launch) before re-reading every row
  back — proving migrations re-running on restart never reset a table.
- **Historical-schema upgrade**: the real Migration0001–0005 classes (i.e., the schema as it
  existed before `associate_drafts`/`manager_drafts` were added) are applied to a temp database,
  representative `source_state`/`dated_results` rows are written against that older schema via the
  real repositories, and then the full current 7-migration `MigrationRunner` is run against the
  same file — proving migrations 6–7 apply in order, the pre-existing rows survive untouched, the
  newly-added `associate_drafts` table is immediately usable, and a second run of the same runner
  is idempotent.

### Corrupt / incompatible database (Part F)

`carowinds-passdown.db` existing but not a valid SQLite database this build can open (a stray text
file, disk corruption, a previous crash's partial write) must never be silently deleted,
overwritten, or replaced with a fresh blank database.

**Actual behavior, reviewed and tested this phase — no code change was needed:**

- `MigrationRunner.Run()` never writes anything before its first `PRAGMA user_version` read; if
  the file isn't a valid database, SQLite throws before any write occurs, and the file is left
  byte-for-byte as found (`CorruptDatabaseTests`).
- **Headless (`--scheduled-refresh`)**: `ScheduledRefreshHost.RunAsync`'s existing top-level
  `catch (Exception)` already classifies this as `ScheduledRefreshExitCode.FatalFailure` and logs
  it (`ScheduledRefreshHostTests.RunAsync_DatabaseFileIsNotValidSqlite_...`, new this phase) — no
  new code was needed, this was already the correct, tested behavior.
- **Interactive launch**: `App.OnFrameworkInitializationCompleted` calls `MigrationRunner.Run()`
  with no surrounding try/catch, so a corrupt database currently surfaces as an unhandled
  exception (a crash) rather than a friendly in-app message. **Classified as acceptable, not a
  release blocker**: the file is never touched (confirmed above), the failure is visible (an
  unhandled-exception crash is loud, not silent), and the spec's Part F bar is "fail visibly/safely
  enough to preserve user data for diagnosis" — which this already meets. A friendlier in-app error
  dialog remains a reasonable future polish item, not a data-safety gap.

### Unsupported newer schema version (Part G) — a real gap, fixed this phase

Reviewed `MigrationRunner.Run()`'s original behavior for `PRAGMA user_version` newer than any
migration this build registers (e.g. an older portable build re-run against data a newer build
already migrated forward): the loop only ever applies migrations with `Version > currentVersion`,
so it silently did nothing and returned the newer version as if it were healthy — the app would
then open normally against a schema it doesn't fully understand, risking a later runtime error or
worse, a later "upgrade" reinterpreting columns incorrectly.

**Fixed**: `MigrationRunner.Run()` now compares the database's version against the highest version
in its own registered migration list immediately after reading it, and throws
`UnsupportedSchemaVersionException` (new type, `Carowinds.Passdown.Infrastructure.Storage`) before
touching anything else if the database is ahead. This flows through the exact same paths as the
corrupt-database case above: `ScheduledRefreshHost` reports `FatalFailure`, interactive launch
crashes loudly rather than silently opening a partially-understood database. Covered by
`MigrationRunnerTests.Run_DatabaseVersionNewerThanAnyKnownMigration_...` (throws, version left
unchanged — no downgrade) and `..._DatabaseVersionEqualToHighestKnownMigration_DoesNotThrow` (the
boundary is exact: already-current is healthy, not an error).

**Downgrade policy**: migrations are forward-only by design (unchanged from Phase 2) — replacing
the app with an *older* build is safe against a database that older build already understands
(nothing changed by this phase), and now fails safely rather than silently misbehaving against a
database a *newer* build already touched. No downgrade migration support was built, per the
explicit instruction not to.

### Legacy/unknown file coexistence (Part H/J/K)

No real legacy Carowinds Passdown LocalAppData directory is available in this repo, in docs, or in
this dev environment to diff against directly — carried over from the Phase 6D/7A flag, still
true. Rather than invent legacy filenames, Phase 7D tests the safety *contract* directly:

- `LocalDataCoexistenceTests` (new) seeds a temp directory with arbitrary unrelated files
  (`legacy-cache.json`, `old-settings.xml`, `previous-app.log`, `arbitrary-folder\file.txt`,
  each with a captured byte-for-byte snapshot and a deliberately old timestamp), then initializes
  the database (`MigrationRunner`) and creates/releases `FileScheduledRefreshLock` alongside them.
  Every unrelated file's bytes, timestamp, and the arbitrary subfolder itself are asserted
  unchanged afterward — this app's storage/lock code never enumerates, reads, or deletes anything
  it doesn't own by exact path.
- **Log pruning** (`FileLoggerProvider.PruneOldLogs`) was already correctly scoped —
  `Directory.EnumerateFiles(options.LogDirectory, "scheduled-refresh-*.log")` only ever considers
  files matching this app's own naming pattern, never every file in `logs\`. No code change was
  needed; `FileLoggerProviderTests.Log_PruningIgnoresUnrelatedLogFiles_RegardlessOfAge` (new) now
  proves it directly: an unrelated `previous-app.log`/`old-settings.xml` with a one-year-old
  timestamp survives pruning, while this app's own stale `scheduled-refresh-*.log` is still
  pruned.
- **Lock file** (`FileScheduledRefreshLock`) already only ever opens/creates exactly
  `scheduled-refresh.lock` at its one fixed `AppPaths`-derived path, is deliberately never deleted
  (so a second concurrent attempt always contends on the same stable path rather than racing a
  create/delete cycle — see the type's own doc comment), and a stale file with no active holder
  never blocks a later acquire (`FileScheduledRefreshLockTests`, pre-existing). Moving the
  application folder cannot create a second lock namespace, since the lock path is derived from
  `AppPaths.LocalApplicationDataDirectory`, never from the executable's own location.
- **Packaging/setup scripts** (`Publish-Windows.ps1`, `Build-PortableRelease.ps1`,
  `Install-ScheduledRefresh.ps1`, `Uninstall-ScheduledRefresh.ps1`, both `.cmd` helpers): reviewed
  and confirmed none references `%LOCALAPPDATA%`/`AppData` at all (the two `.ps1` build scripts
  only ever `Remove-Item` their own `artifacts/publish`/`artifacts/release-staging`/
  `artifacts/releases` staging output; the Task Scheduler scripts never call any file-delete
  cmdlet at all). `PortablePackagingDataSafetyTests` (new) asserts this by reading the shipped
  script text directly.

### Portable move / scheduled-task rebind (Part L/M)

Reviewed every runtime path dependency: `AppPaths.LocalApplicationDataDirectory` resolves from
`Environment.SpecialFolder.LocalApplicationData` (never `AppContext.BaseDirectory`, never
`Environment.CurrentDirectory` — see `AppPathsTests`, Phase 7A), and `appsettings.json` loads via
`ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)` (the *executable's own* directory,
correct wherever it's extracted). Moving
`C:\Users\User\Downloads\Carowinds Passdown\` to
`C:\Users\User\Desktop\Operations\Carowinds Passdown\` therefore changes nothing about where the
database, drafts, or logs live — only where the `.exe` and `appsettings.json` are read from.

The scheduled task is the one thing that does need a manual step after a move: its registered
action is a snapshot of the `.exe` path at the moment `Enable Automatic Refresh.cmd` was last run.
`ScheduledTaskPlanTests.Build_DifferentExecutablePaths_ShareTheSameTaskName_...` (new) proves the
rebind contract directly: building the task definition for the old path and the new path always
produces the *same* `TaskName`, just a different `ExecutablePath`/`WorkingDirectory` — which is
exactly what makes `Register-ScheduledTask -Force` (Phase 6C, unchanged) replace the one existing
task in place rather than ever registering a duplicate. `StartupCatchUpRefresh` (Phase 6B) remains
the safety net for any refresh missed while a task still points at a since-moved location.

### Portable delete / removal semantics (Part N/R)

Unchanged from Phase 7B/7C, restated here as the Phase 7D data-preservation contract:

- **Deleting the extracted portable application folder** does not delete local data — the
  database, drafts, cache, and logs all live under `%LOCALAPPDATA%\Carowinds Passdown`, entirely
  outside the extracted folder.
- **Disabling automatic refresh** (`Disable Automatic Refresh.cmd`) removes only the OS-level
  Task Scheduler entry; it never touches local data (`Uninstall-ScheduledRefresh.ps1`'s own
  console output states this explicitly).
- **Re-extracting/upgrading** reuses the same LocalAppData database; pending migrations apply on
  the next launch (proved above).
- No automatic user-data removal / "Reset All Data" feature was added this phase, per the explicit
  instruction not to — a full reset, if ever needed, remains a manual, documented support
  operation for a later phase.

### Writable-path audit (Part P)

Reviewed every place this app writes at runtime: the database (+ `-wal`/`-shm`),
`scheduled-refresh.lock`, and `logs\scheduled-refresh-*.log` are all rooted under
`AppPaths.LocalApplicationDataDirectory`; PDF export goes wherever the user picks via the native
save-file dialog (`IPdfSaveFilePicker`); `appsettings.json` is read-only configuration, never
written to. Nothing in the codebase writes beside the executable — the extracted portable folder
can be treated as read-only in principle, matching "replace/re-extract at will" being safe.

### Phase 7D test summary

19 new tests added this phase (624 → 643, all non-live):
`PortableLifecycleTests` (5), `LocalDataCoexistenceTests` (3), `CorruptDatabaseTests` (2),
`MigrationRunnerTests` (+2, the new-schema guard), `ScheduledRefreshHostTests` (+1, corrupt-DB
fatal exit code), `FileLoggerProviderTests` (+1, unrelated-log coexistence),
`ScheduledTaskPlanTests` (+1, rebind contract), `PortablePackagingDataSafetyTests` (4).

### Real legacy specimen comparison (Part I)

No real legacy Carowinds Passdown LocalAppData directory is available in this repository, its
docs, or this dev environment — unchanged from the Phase 6D/7A flag. This is not treated as a
blocker: the coexistence contract above (arbitrary unknown files, byte-for-byte, are never
touched) is proven regardless of what the legacy app's actual filenames turn out to be. If a real
legacy machine's directory ever becomes available, a direct filename listing/comparison would
upgrade this from "PASS (reviewed, contract proven)" to "PASS (verified against a real legacy
directory)" — no code change would be expected either way, since the contract doesn't depend on
knowing the legacy names in advance.

### Phase 7D verdict

| # | Item | Verdict |
|---|---|---|
| 1 | Clean first run | PASS |
| 2 | Migration initialization | PASS |
| 3 | Restart persistence | PASS |
| 4 | Upgrade/re-extract persistence | PASS |
| 5 | Historical-schema migration | PASS |
| 6 | Corrupt DB safety | PASS |
| 7 | Newer-schema/rollback safety | PASS AFTER FIX |
| 8 | Unknown legacy-file coexistence | PASS |
| 9 | Scheduled-log coexistence | PASS |
| 10 | Lock-file coexistence | PASS |
| 11 | Portable move | PASS |
| 12 | Scheduled-task rebinding | PASS |
| 13 | Portable delete semantics | PASS |
| 14 | Downgrade policy | PASS (documented, no rollback support built) |
| 15 | Writable-path audit | PASS |
| 16 | Documentation | PASS |
| 17 | Automated test coverage | PASS |
| 18 | Full build/tests | PASS (643/643) |
| 19 | Real legacy specimen comparison | PARTIAL (none available; contract proven regardless) |
| 20 | Windows runtime validation | DEFERRED BY DESIGN to 7E |

## Phase 7E — Windows 11 manual validation

Executed by the product owner on a real Windows machine, against the actual portable release
ZIP (not `dotnet run`, not a repo-relative execution path), following the step-by-step
[`docs/phase-7e-windows-validation-checklist.md`](phase-7e-windows-validation-checklist.md).
This is **user-performed manual validation** — Claude had no Windows machine/VM access this
phase and did not independently observe any of the following; results below are as reported by
the tester.

### Artifact verified

`Carowinds-Passdown-0.1.0-dev-win-x64.zip`, SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0` — confirmed matching via
`Get-FileHash` on the test machine before extraction.

### Test environment (as reported)

- `Get-ComputerInfo`: `WindowsProductName: Windows 10 Pro`, `WindowsVersion: 2009`,
  `OsBuildNumber: 26200`. **Internally inconsistent as reported** — build 26200 falls in the
  Windows 11 24H2/25H2 range, not Windows 10 (which tops out around build 19045). Not
  independently re-verified via Settings → System → About; actual OS edition is unconfirmed.
- Architecture: `ARM64` (`$env:PROCESSOR_ARCHITECTURE`; `dotnet --info` also reports RID
  `win-arm64`). **This is not the documented supported target** — this doc's own "Supported
  target" table above states x64 only, "no win-arm64." The win-x64 self-contained binaries
  evidently ran via Windows 11 ARM's built-in x64 emulation. This confirms the app *functions*
  under x64 emulation on ARM64; it does not substitute for a native x64 Windows 11 run, which
  remains recommended before treating the primary target architecture as fully verified.
- `.NET SDK 10.0.400` and the `Microsoft.WindowsDesktop.App`/`Microsoft.NETCore.App` 10.0.11
  runtimes were **already installed** on this machine before testing (confirmed via `dotnet
  --info` output captured during the run). This means the "no separate .NET installation
  required" claim was **not independently proven** by this pass — a machine without .NET
  pre-installed still hasn't exercised this path end to end.

### Results

| Part | Item | Verdict | Notes |
|---|---|---|---|
| A | Test environment | PARTIAL | Recorded, but self-contained/no-.NET claim not proven (.NET pre-installed) and architecture is ARM64, not the documented x64 target |
| B | Extract real ZIP | PASS | Extracted under Downloads; exact sub-path/size not recorded |
| C | Clean first launch | PASS | App opened; LocalAppData created |
| D | UI smoke | PASS | No itemized notes beyond top-line result |
| E | Display scaling (100/125/150%) | PASS | Full table completed: no clipping, overflow, or unreadable text at any scale |
| F | First-run persistence | PASS | Test draft entered and confirmed persisted after restart |
| G | Check Online | PASS | No explicit confirmation captured of absence of Playwright-related errors |
| H | PDF generation | PASS | No itemized notes beyond top-line result |
| I | Enable Automatic Refresh | PASS | No itemized notes beyond top-line result |
| J | Manual scheduled refresh | PASS | `LastRunTime` / `LastTaskResult` / `NextRunTime` captured directly from `Get-ScheduledTaskInfo`; `LastTaskResult = 0` |
| K | Scheduled log location | PASS | No log excerpt captured |
| L | Disable Automatic Refresh | PASS | No itemized notes beyond top-line result |
| M | Re-enable / idempotency | PASS | No itemized notes beyond top-line result |
| N | Portable move + task rebind | PASS | No itemized notes beyond top-line result |
| O | Re-extract / update persistence | PASS | No itemized notes beyond top-line result |
| P | Portable delete semantics | PASS | No itemized notes beyond top-line result |
| Q | Unknown legacy-file coexistence | PASS | No itemized notes beyond top-line result |
| R | Corrupt DB safety | PASS | Specific on-screen failure behavior not captured |
| S | Newer-schema guard | PASS | Specific on-screen failure behavior not captured |
| T | Startup catch-up/overlap coexistence | PASS | Checklist flags this case as often impractical to reproduce reliably; reported PASS rather than NOT EXECUTED |
| U | No .NET/dev dependency | PASS | Qualified by the Part A finding that .NET was already present on this machine |
| V | Release content inspection | PASS | No itemized notes beyond top-line result |
| W | PowerShell build scripts (`Publish-Windows.ps1`/`Build-PortableRelease.ps1`) | PASS | Unclear whether the source repository (required separately from the ZIP) was actually present on the test machine |
| X | README usability | PASS | No itemized notes beyond top-line result |

### Caveats

This pass confirms the release **functions correctly end-to-end** for a real Windows user
following the documented workflow, with **no defect reported**. It does not fully close out
every claim in `AGENT_BUILD_SPEC.md` §18 "Windows delivery" at full confidence:

- Run under ARM64/x64 emulation, not natively on the documented x64 target.
- Self-contained/no-separate-.NET-install was not independently proven (.NET was pre-installed
  on the test machine).
- Windows 11 vs Windows 10 could not be confirmed from the reported environment output.
- Most individual parts were reported as PASS without itemized supporting detail beyond the
  top-line result.

A native x64 Windows 11 machine with no .NET pre-installed remains the strongest remaining
validation gap if higher confidence on the primary target is later needed. No code change was
made or needed this phase — no release defect was found.

## What's deferred (not yet done)

- **7C**: any installer, if one is ever decided to be needed (spec currently only asks for a
  portable ZIP, not an installer) — unchanged from Phase 7A/7B; Phase 7C itself (nontechnical
  scheduled-refresh setup) is now done, see above.
- **7D**: real legacy-machine file listing comparison (Part I above) — no real legacy directory is
  available in this environment; the coexistence *contract* is proven instead, and is not blocked
  by this. Phase 7D itself (first-run/upgrade/coexistence/move data-preservation guarantees) is
  now done, see above.
- **7E**: itself done, see above — actual Windows-host execution was performed by the product
  owner against the real release ZIP. A native x64 Windows 11 pass without .NET pre-installed
  remains a nice-to-have for full target-architecture confidence, not a blocker.
- **7F**: itself done, see below.

## Phase 7F — final release audit, diagnostics, tester handoff, and closeout

Closes out Phase 7: final artifact identity/audit, a sanitized diagnostic export feature, a
nontechnical tester guide, a support/troubleshooting guide, and a requirements/architecture audit.
No new product phase, no installer, no Manager workflow, no new retrieval source, and no behavior
change beyond what's listed here.

### Final artifact identity (Part A)

See [`docs/release-manifest.md`](release-manifest.md) for the authoritative, single-table summary
(SHA-256, sizes, file count, test count). Short version: `Carowinds-Passdown-0.1.0-dev-win-x64.zip`,
SHA-256 `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`, 277 files, 61,258,219 bytes
compressed / 139,336,742 bytes extracted. This **supersedes** the Phase 7B/7C/7D/7E artifact
(SHA-256 `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`, still recorded above and
in `docs/phase-7e-windows-validation-checklist.md` as the historical record of what was actually
validated on Windows) — see "Windows recheck status" below for why the two differ and what that
means for confidence.

### Release content audit (Part B/C)

Rebuilt and re-inspected the ZIP from scratch this phase (see `docs/release-manifest.md` "How this
artifact was produced" for the exact staging steps, mirroring `Build-PortableRelease.ps1` manually —
no `pwsh` on this dev host, same limitation as every prior Phase 7 sub-phase). Forbidden-pattern
check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `*playwright*`,
`*.Tests.dll`, `.dylib`/`.so`, `.DS_Store`) — clean. Contents match the expected Phase 7C/7D layout
plus two additions: `TESTER-GUIDE.txt` (new, Part L) and the diagnostic export feature compiled into
`Carowinds.Passdown.Desktop.dll`/`Carowinds.Passdown.Application.dll`/`Carowinds.Passdown.Infrastructure.dll`
(no new shipped file for this — it's a button in the existing UI, not a separate executable).
Version metadata unchanged from Phase 7A/7B: `Directory.Build.props`' single `<Version>0.1.0-dev</Version>`
remains the only version source; no `1.0.0` was invented (Part C).

### Diagnostic export (Parts D–K)

**Privacy model (Part E), decided before writing any code:**

| Included | Excluded |
|---|---|
| App version, schema version, DB file size, WAL presence | Full SQLite database file |
| Per-source derived status (`Ok`/`Error`/`NeverChecked`), last-attempt/last-success timestamps, consecutive-failure count | Associate drafts, operational notes, custom product text |
| Recent scheduled-refresh log tail (already-sanitized — see "Log sanitization review" below) | Full HTTP payloads, raw HTML, cookies, tokens |
| Whether the "Carowinds Passdown Daily Refresh" task is registered (existence only) | Detailed Task Scheduler fields (last-run time, next-run time — available via `Get-ScheduledTaskInfo` or the Task Scheduler UI instead, per Part K's explicit "a full status helper is optional") |
| Sanitized `%LOCALAPPDATA%\Carowinds Passdown` path (the literal string, not the tester's real home directory) | The tester's actual machine username/home path |

**Implementation shape (Part F), smallest appropriate option chosen:** an Application-layer
`IDiagnosticReportBuilder` (composes a pure `DiagnosticReport` from already-registered abstractions:
`ISourceStateRepository`, the existing `IReadOnlyList<SourceRegistration>`, and three new
Infrastructure-implemented ports — `IDatabaseDiagnosticsProvider`, `IScheduledRefreshLogProvider`,
`IScheduledTaskStatusProvider`) plus a pure, unit-testable `DiagnosticReportFormatter` (text
rendering, no I/O) and an Infrastructure `DiagnosticExportService` that zips the formatted text plus
a log excerpt entirely in memory (`System.IO.Compression.ZipArchive` over a `MemoryStream` — no
temp file ever touches the app directory). Desktop adds one new button, "Export Diagnostics" (next
to "Create PDF" on the Associate screen — no new Settings/About subsystem), wired through a new
`IDiagnosticExportFilePicker` that mirrors the existing `IPdfSaveFilePicker` pattern exactly: the
destination is always wherever the user's native save dialog says, never a fixed path.

**Task Scheduler status (Part K):** `ScheduledTaskStatusProvider` (Infrastructure) shells to
`schtasks.exe /Query /TN "Carowinds Passdown Daily Refresh"` on Windows only
(`OperatingSystem.IsWindows()` guard) and checks only the exit code — existence, not a parse of
`schtasks`' locale-dependent detailed text output, per the spec's explicit "a status helper is
optional; existence is enough." Off Windows (this dev host, and any non-Windows CI), it returns
`NotAvailableOnThisPlatform` without ever invoking a process. Any query failure (missing
`schtasks.exe`, unexpected error) returns `Unknown` rather than throwing — the whole diagnostic
export must never fail just because Task Scheduler status couldn't be read. The task name constant
is hand-synced with `ScheduledTaskPlan.TaskName` (Desktop) rather than shared via a reference,
because Infrastructure cannot depend on Desktop (CLAUDE.md architecture) — the same approach the
`.ps1` scripts already use; `ScheduledTaskStatusProviderTaskNameSyncTests` (Ui.Tests, which
references Desktop and therefore transitively both) asserts the two stay equal.

**Database diagnostics (Part J):** `SqliteDatabaseDiagnosticsProvider` checks `File.Exists` *before*
ever opening a connection — `SqliteConnectionFactory.OpenConnection()` creates the database file on
first use, which would otherwise turn "export diagnostics" into a silent side effect that creates a
0-byte database on a machine that never launched the app. A corrupt/unreadable database is reported
via a `Note` field, never thrown — exporting diagnostics specifically *because* the database looks
broken must not itself fail. Only metadata (existence, `PRAGMA user_version`, file size, `-wal`
presence) is read; no table is queried.

**Log excerpt (Part D/G):** `FileScheduledRefreshLogProvider` reads only the tail of the
most-recently-dated `scheduled-refresh-*.log` file already written by `FileLoggerProvider` — same
naming-pattern scoping that `FileLoggerProvider.PruneOldLogs` already relies on, so no unrelated file
under `logs\` is ever touched or included. Missing log directory → empty list, never an exception
(Part S).

**Output (Part G):** `Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip` (digits/dashes only — always
a filesystem-safe name), containing `summary.txt`, `system.txt`, `source-status.txt`, and
`logs/scheduled-refresh-YYYYMMDD.log` (omitted entirely if there are no log lines to include — never
an empty placeholder file). No JSON schema was introduced; all three text files are deterministic,
human-readable plain text, per the spec's explicit "do not overengineer."

### Log sanitization review (Part H)

Reviewed every `LogXxx` call site that could reach the scheduled-refresh log file directly
(`ScheduledRefreshRunner`, `MigrationRunner`, `ScheduledRefreshHost`) against
`RetrievalDiagnostics`/`SourceEvidence`'s own documented contract (`Detail` is always a static,
human-facing sentence supplied by the calling source adapter — e.g. "We couldn't check park hours
right now." — never derived from an exception message or a URL; the full exception lives separately
in `SourceEvidence.TechnicalDetail`, which is persisted to the database for developer troubleshooting
but is **never** written to this log file or included anywhere in the diagnostic export). Confirmed
directly by reading the actual log statements, not just the doc comments: `ScheduledRefreshRunner.LogSummary`
logs `SourceId`/`Category`/`RequestedDate`/`Status`/`Detail` only. **Verdict: already safe. No code
change was needed for Part H** — see `docs/support-troubleshooting.md` "Log sanitization" for the
full write-up support staff can rely on.

### Tester guide (Part L)

[`packaging/windows/TESTER-GUIDE.txt`](../packaging/windows/TESTER-GUIDE.txt) — shipped inside the
ZIP at the release root (next to `README.txt`), staged by `Build-PortableRelease.ps1` exactly like
`README.txt` (fails loudly if missing, per the script's existing "fail loudly rather than ship
something wrong" pattern). Covers, in plain language, no architecture terminology: extract, launch,
pick a date, enter sample data, confirm persistence across a restart, Check Online, Create PDF,
enable/verify/disable automatic refresh, what to do if the folder moves, how to export diagnostics,
and what to send support if something fails. `TesterGuidePackagingTests` (Ui.Tests) asserts it exists,
is staged, mentions "Export Diagnostics," and never contains `RefreshOrchestrator`/`IUnitOfWork`/
`LastKnownGood`.

### Support/troubleshooting guide (Part M)

[`docs/support-troubleshooting.md`](support-troubleshooting.md) — repo/support-only, not shipped in
the ZIP (deliberately: it uses real architecture vocabulary the tester guide avoids). Covers: app
won't open, Check Online fails, automatic refresh doesn't seem to run, PDF doesn't save, data appears
missing, unsupported/newer database, corrupt database, how to read each file in a diagnostic export,
and the log-sanitization write-up referenced above.

### User data / removal instructions (Part N)

Unchanged from Phase 7B/7C/7D — restated in both the tester guide and `README.txt`: application
files live wherever the ZIP was extracted (disposable); user data lives independently at
`%LOCALAPPDATA%\Carowinds Passdown`; removing the app means disabling automatic refresh (if enabled)
then deleting the extracted folder, which never touches user data. No destructive "reset all data"
command was added, per the explicit instruction not to.

### Automatic refresh handoff (Part O)

Unchanged from Phase 7C: `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd`, no
PowerShell typing, no admin/UAC. Restated accurately in the new tester guide and cross-checked
against `README-portable-release.md`.

### Known limitations / confidence gaps (Part P)

Carried forward accurately, classified honestly:

| # | Item | Classification |
|---|---|---|
| 1 | Phase 7E Windows validation ran on ARM64 hardware via x64 emulation, not native x64 | Accepted / non-blocking |
| 2 | .NET was already installed on the Phase 7E test machine — self-contained/no-separate-.NET-install was not independently proven on a runtime-clean image | Accepted / non-blocking |
| 3 | Phase 7E's reported Windows edition/build info was internally inconsistent (Windows 10 product name, build 26200 which is a Windows 11 build range) | Accepted / non-blocking |
| 4 | No real legacy Carowinds Passdown LocalAppData directory was ever available to diff filenames against directly; the coexistence *contract* was proven generically instead (Phase 7D) | Accepted / non-blocking |
| 5 | No generalized retry framework exists, by deliberate design | Deferred by design (not a defect) |
| 6 | Manager workflow remains deferred and absent | Deferred by design (not a defect; explicitly out of scope per CLAUDE.md "Current scope decision") |
| 7 | **New this phase:** the diagnostic export feature (Application/Infrastructure/Desktop code, new "Export Diagnostics" button, new `TESTER-GUIDE.txt`) changed the shipped artifact after Phase 7E's Windows validation, and this phase had no Windows host available to re-verify it | **Not validated on Windows** — see "Windows recheck status" immediately below |

None of the above is hidden or presented as a defect where it isn't one.

### Phase 7 requirements matrix (Part Q)

| Requirement (spec §17 Phase 7 / §18 "Windows delivery" / §19) | Evidence | Status |
|---|---|---|
| Publish self-contained Windows x64 | `Publish-Windows.ps1`; `docs/release-strategy.md` Phase 7A "Publish validation" | PASS |
| Create clean portable ZIP | `Build-PortableRelease.ps1`; Part B audit above, rebuilt and re-inspected this phase | PASS |
| Verify on a clean Windows 11 VM without admin rights | Phase 7E manual pass (product owner, real hardware) | PASS AFTER FIX (Phase 7D's newer-schema guard fix), with the Part A confidence gaps above |
| Include testing instructions | `packaging/windows/TESTER-GUIDE.txt` (new), `README.txt` | PASS |
| Include sanitized diagnostic export | In-app "Export Diagnostics"; Parts D–K above | PASS |
| App launches from extracted ZIP on Windows 11 | Phase 7E Part C | PASS (subject to Part P #1–3 confidence gaps) |
| No admin/custom server/login/GPT/separate .NET install required | Phase 7E Parts C/I/L/U | PASS (Part P #2: not independently proven on a runtime-clean image) |
| Generated PDFs open/found easily | Phase 7E Part H | PASS |
| Offline startup loads drafts and cache | Phase 7D `PortableLifecycleTests`; Phase 7E Part F | PASS |
| Release ZIP contains no `.git`/source/unrelated output | Part B audit above | PASS |
| Sanitized diagnostic report, no private data (§19 deliverable 5) | Parts D/E above; `DiagnosticReportBuilderTests`/`DiagnosticExportServiceTests` | PASS |
| Simple tester instructions for a nontechnical coworker (§19 deliverable 10) | `TESTER-GUIDE.txt` | PASS |
| Concise list of remaining limitations (§19 deliverable 11) | "Known limitations" above | PASS |
| Manager workflow | Explicitly deferred (CLAUDE.md) | DEFERRED BY DESIGN |

### Architecture audit (Part R)

- **Domain**: untouched — no Windows packaging concern was ever added there.
- **Application**: `IDiagnosticReportBuilder`/`DiagnosticReportBuilder` (orchestration only, no I/O)
  and three new abstractions (`IDatabaseDiagnosticsProvider`, `IScheduledRefreshLogProvider`,
  `IScheduledTaskStatusProvider`) plus the pure `DiagnosticReportFormatter`. No direct Task Scheduler
  call, no SQLite call, no filesystem call in this layer.
- **Infrastructure**: `SqliteDatabaseDiagnosticsProvider`, `FileScheduledRefreshLogProvider`,
  `ScheduledTaskStatusProvider`, `DiagnosticExportService` — all the actual SQLite/filesystem/process
  I/O, consistent with every other storage/platform adapter in this layer.
- **Desktop**: one new button + `IDiagnosticExportFilePicker`/`AvaloniaDiagnosticExportFilePicker`
  (native save dialog only) + `AssociateViewModel.ExportDiagnosticsCommand` (orchestration, no direct
  I/O) — no code-behind logic.
- **Packaging**: `Build-PortableRelease.ps1` gained one more staged file
  (`TESTER-GUIDE.txt`) via the same fail-loudly-if-missing pattern already used for `README.txt`.

Confirmed absent: no Windows Service, no MSI/MSIX dependency, no new Manager workflow, no duplicated
scheduling or persistence system, no PowerShell network retrieval, no Playwright reintroduced, no
user-data write into the release folder (the diagnostic export writes only wherever the user's save
dialog points, exactly like PDF export).

### Tests added (Part S)

41 new tests (643 → 684, all non-live):

- `DiagnosticReportBuilderTests` (6, Application.Tests) — never-checked/ok/error derivation, distinct
  display names for sources sharing a category, and (critically) that building a report never calls
  `SourceRegistration.ExecuteAsync` — i.e. never triggers a network refresh.
- `DiagnosticReportFormatterTests` (6, Application.Tests) — pure text assertions, including that
  `system.txt` never contains the machine username.
- `SqliteDatabaseDiagnosticsProviderTests` (4, Infrastructure.Tests) — missing DB never gets created
  by inspecting it, schema version/size/WAL detection, corrupt DB reports a note instead of throwing.
- `FileScheduledRefreshLogProviderTests` (6, Infrastructure.Tests) — missing directory, no log files
  yet, tail truncation, most-recent-file selection, unrelated files ignored.
- `ScheduledTaskStatusProviderTests` (2, Infrastructure.Tests) — non-Windows branch (this dev host)
  reports `NotAvailableOnThisPlatform` without throwing; task-name constant value.
- `DiagnosticExportServiceTests` (5, Infrastructure.Tests) — safe deterministic file name, expected
  entries present, database file never included, empty log list omits the `logs/` entry entirely,
  source display name appears in `source-status.txt`.
- `ScheduledTaskStatusProviderTaskNameSyncTests` (1, Ui.Tests) — the hand-synced task-name constant
  matches `ScheduledTaskPlan.TaskName`.
- `AssociateViewModelDiagnosticsExportTests` (6, Ui.Tests) — mirrors
  `AssociateViewModelPdfExportTests`: invokes export then picker, success/cancel/failure/exception
  messaging, never gated on `IsBusy` (diagnostics is local-only and independent of the current date
  load).
- `TesterGuidePackagingTests` (5, Ui.Tests) — existence, staging, fail-loudly check, no architecture
  jargon, mentions "Export Diagnostics."

### Build/test/rebuild (Part T)

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **PASS**, 684/684.
3. Shipped content changed (new diagnostic export code, new `TESTER-GUIDE.txt`) → the portable
   release ZIP was rebuilt from scratch this phase (see `docs/release-manifest.md`), re-inspected,
   and its SHA-256 recomputed. The prior Phase 7B/7C/7D/7E artifact is superseded, not reused.

### Windows recheck status (Part W) — NOT EXECUTED

This phase's code changes (the diagnostic export feature, the "Export Diagnostics" button, the new
`TESTER-GUIDE.txt`) are covered by the 36 automated tests above, all passing on this
(non-Windows) dev host, and by manual review. **They have not been exercised on an actual Windows
machine.** No Windows host or VM was available to this session. This is recorded honestly rather than
implied — the artifact identified in `docs/release-manifest.md` is **not** the exact artifact Phase
7E's manual pass ran against (that was the prior, superseded ZIP); this phase's own changes carry the
same "not yet Windows-verified" status every other phase records for work performed on this dev host
before its first real Windows pass. A focused Windows recheck — extract the new ZIP, launch, click
"Export Diagnostics," open the resulting file, confirm the helpers/tools are still present, confirm
scheduled-refresh setup still works — remains outstanding and is the right next manual step before
treating this exact artifact as fully Windows-proven, though nothing about the change (a save-dialog
button producing a ZIP of text files) touches any of the platform-specific surfaces (Task Scheduler
registration, self-contained runtime resolution, native library loading) that Phase 7E's pass already
exercised successfully.

### Phase 7 overall verdict (Part X)

1. Final artifact: `Carowinds-Passdown-0.1.0-dev-win-x64.zip`
2. Final SHA-256: `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`
3. Final compressed size: 61,258,219 bytes (58.4 MiB)
4. Final extracted size: 139,336,742 bytes (132.9 MiB)
5. Final file count: 277 (280 zip entries with directories)
6. Version metadata: `0.1.0-dev`, unchanged, single source (`Directory.Build.props`) — PASS
7. Release-content audit: PASS
8. Diagnostic export implementation: PASS
9. Diagnostic privacy review: PASS
10. Tester guide: PASS
11. Support/troubleshooting guide: PASS
12. Automatic refresh handoff: PASS
13. User-data/removal instructions: PASS
14. Known limitations: PASS (documented honestly, see above)
15. Phase 7 requirements matrix: PASS (Manager workflow DEFERRED BY DESIGN, not a failure)
16. Architecture audit: PASS
17. Tests added: 41 (643 → 684)
18. Final non-live test count: 684/684
19. Build result: PASS (0 warnings, 0 errors)
20. Final Windows recheck status: **NOT EXECUTED** (no Windows host available this phase — see above)
21. Phase 7 overall verdict: **PASS AFTER FIX**, given item 20's honest gap
22. Remaining deferred work: Manager workflow (DEFERRED BY DESIGN, out of current release scope); a
    native x64 Windows 11 pass without pre-installed .NET (nice-to-have, Phase 7E carryover); a
    focused Windows recheck of this phase's own diagnostic-export change (item 20)

### Release readiness decision (Part Y)

**READY WITH ACCEPTED LIMITATIONS.**

The application, Windows packaging, scheduled-refresh setup, and data-safety guarantees were
independently validated end-to-end on a real Windows machine in Phase 7E with no defect found. This
phase's own changes (diagnostic export) are small, additive, fully covered by automated tests, and
touch none of the platform-specific surfaces Phase 7E already proved — but they have not themselves
been run on Windows, and that gap is recorded above rather than glossed over. `0.1.0-dev` remains an
explicit pre-release development version, not a production 1.0 release.

## Post-Phase-7F rebuild — Associate UI/date-picker/PDF polish

Not a new numbered phase — a rebuild of the existing Phase 7 release artifact after an Associate UI
and PDF refinement pass changed shipped Desktop/Documents code. `docs/release-manifest.md` is the
authoritative record of exactly what this rebuild produced (SHA-256, sizes, file count); this section
records only the narrative and what was/was not re-verified.

### What changed since the Phase 7F artifact

- Associate screen: new app-shell layout with a left section rail (Overview / Products / Notes),
  reducing the page to a single long scroll; a `CalendarDatePicker`-based date picker (calendar
  flyout, friendly long-form date display) replacing the previous spinner-style control; a denser
  Products presentation; a new "Verification & Save Status" card; a new "Operational Notes" summary
  card.
- Associate PDF (`AssociatePassdownPdfGenerator`): darker hairline/comparison-price colors and a
  strikethrough style on superseded comparison prices, for more reliable grayscale/print legibility.
  No layout/spacing change — the existing one-page-fit and continuation-page tests are unaffected.
- No retrieval, scheduling, persistence, or business-rule behavior changed. No Manager workflow was
  added. The product version was not changed (`0.1.0-dev` remains the single authoritative version,
  `Directory.Build.props`).

### Artifact status

The Phase 7F artifact (SHA-256 `01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`)
is **superseded** by this rebuild (SHA-256 `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`
— see `docs/release-manifest.md`). The Phase 7B/7C/7D/7E artifact (SHA-256
`94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remains further superseded and is
retained purely as historical record of exactly what Phase 7E's Windows pass validated.

**Phase 7E's full manual Windows validation applies only to the Phase 7B/7C/7D/7E artifact, not to
this rebuild.** Nothing about that pass (scheduled-task registration, self-contained runtime
resolution, native library loading, first-run/persistence/portable-move behavior) is expected to be
affected by a UI-layout/date-picker/PDF-styling change, but it has not been re-run against this exact
artifact.

### Build/publish/package validation performed this rebuild

1. `dotnet build CarowindsPassdown.slnx` — PASS, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — PASS, 684/684 (identical
   count to Phase 7F — the UI/PDF changes added no tests and broke none).
3. `dotnet publish ... -r win-x64 --self-contained true ...` (mirroring `Publish-Windows.ps1`; no
   `pwsh` on this dev host, same limitation as every prior Phase 7 sub-phase) — PASS. 271 files,
   133 MB after `.pdb` removal — same shape as Phase 7B–7F (no new shipped files, only changed
   content inside existing DLLs).
4. Before staging, the published `Carowinds.Passdown.Desktop.dll` and `Carowinds.Passdown.Documents.dll`
   were directly inspected (byte search, not just file timestamps) for strings unique to the new
   source: `SelectedDateForPicker`/`OnSectionButtonClick` (new Desktop members) and the new PDF
   colors `#B7C1CC`/`#5C6673`, with the prior `#C9D3DD`/`#7C8794` confirmed absent — proving this
   publish is not a stale reuse of the Phase 7F build output.
5. Staging + ZIP built mirroring `Build-PortableRelease.ps1` exactly (same steps documented in
   `docs/release-manifest.md`); the ZIP was then independently re-extracted to a scratch directory
   and re-inspected (not just trusted from the staging step) — executable, `appsettings.json`,
   README/tester-guide/`.cmd` helpers/`tools/` all present; no Playwright, `.pdb`, test assemblies,
   `.git`/dev docs, database files, non-Windows binaries, or screenshot/harness artifacts found.

### Focused Windows smoke check

**NOT EXECUTED.** No Windows machine or VM was available in this session. This is recorded honestly
rather than assumed passing. Before treating this rebuilt artifact as equally trustworthy to the
Phase 7F artifact for a real Windows user, the following focused smoke pass (not a full Phase 7E
rerun) is the recommended next step, against the rebuilt ZIP itself:

1. Extract the rebuilt ZIP.
2. Launch `Carowinds.Passdown.Desktop.exe`.
3. Confirm the new app-shell layout appears (left section rail; Overview above the fold).
4. Click the date field and confirm the calendar popup opens.
5. Switch between Overview / Products / Notes.
6. Confirm the layout is usable at a normal window size.
7. Generate one PDF via "Create PDF."
8. Open the PDF and visually confirm the grayscale-safe comparison-price styling (strikethrough on
   a "was $X" price).
9. Click "Export Diagnostics" once and confirm a ZIP is produced.
10. Confirm the automatic-refresh `.cmd` helpers are still present and unchanged in behavior (no
    regression expected or introduced — not itself re-run this pass).

### Rebuild verdict

| # | Item | Result |
|---|---|---|
| 1 | Build | PASS |
| 2 | Non-live test count | PASS, 684/684 |
| 3 | Publish (win-x64, self-contained) | PASS |
| 4 | New UI code confirmed in published Desktop assembly | PASS (byte-level check) |
| 5 | New PDF code confirmed in published Documents assembly | PASS (byte-level check) |
| 6 | Portable ZIP rebuilt and re-inspected | PASS |
| 7 | Release-content audit (forbidden patterns, required files) | PASS |
| 8 | SHA-256 recomputed and changed from Phase 7F | PASS (confirms this is not the stale artifact) |
| 9 | `docs/release-manifest.md` updated to point at the new artifact | PASS |
| 10 | Focused Windows smoke check | **NOT EXECUTED** — no Windows host available |

**Status: artifact rebuilt and internally verified; Windows-side confirmation remains outstanding**,
same honest posture every prior Phase 7 sub-phase recorded before its own first Windows pass.

## Post-Phase-8 rebuild — Associate Daily Feature, section visibility, and PDF redesign

Not a new numbered phase — a packaging-only rebuild of the existing Phase 7 release artifact after
the Phase 8A/8B Associate Passdown work landed on `feature/passdown-redesign`
(`1e31b28` "Add passdown section selection and daily feature", `2e280f5` "Redesign associate
passdown PDF"). `docs/release-manifest.md` is the authoritative record of exactly what this rebuild
produced (SHA-256, sizes, file count, test count); this section records only the narrative and what
was/was not re-verified. No source code was changed to produce this rebuild — packaging only.

### What changed since the Post-Phase-7F artifact

- **Phase 8A**: `Daily Fun` replaced by `Daily Feature`, restricted to exactly three types (Useless
  Fact of the Day / Question of the Day / Quote of the Day); section-level "Include in Passdown"
  controls for Daily Admission, Fast Lane, Promotions, Season Passes, Add-Ons, SCarowinds/Haunt, and
  Operational Notes; persisted section visibility; compatibility/migration for existing Associate
  drafts.
- **Phase 8B**: redesigned one-page Associate Passdown PDF — operational-summary hierarchy, a
  visually distinct Daily Feature panel, adaptive Daily Admission/Fast Lane price tiles, adaptive
  Promotion cards, a compact reference-section layout, improved Operational Notes presentation,
  dynamic reflow when optional sections are hidden, grayscale/print-safe styling, strict one-page
  behavior (oversized content fails preflight rather than producing a continuation page), and removal
  of the `1 of 1` footer indicator.
- Mandatory Passdown information is unchanged: Day, Date, Park Hours, Attendance, Early Ride Time,
  Daily Feature.
- No retrieval, scheduling, persistence-layer, or business-rule behavior outside the Associate
  draft/PDF surface changed. No Manager workflow was added. The product version was not changed
  (`0.1.0-dev` remains the single authoritative version, `Directory.Build.props`).

### Artifact status

The Post-Phase-7F artifact (SHA-256 `ee553439ba2c70018b5fdbd9b256808891378a775f0411fe1ef1362ff483079d`)
is **superseded** by this rebuild (SHA-256 `eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`
— see `docs/release-manifest.md`). The Phase 7F artifact (SHA-256
`01497e17ff2bcacc1ce3b2dd71de6726719406b1914ec50e0b2afd78e9d69f57`) and the Phase 7B/7C/7D/7E artifact
(SHA-256 `94064eee63a2cb7bc0d30e83d45afde946ff02dbb3b5d3b9aeaa9f7d16871ab0`) remain further superseded
and are retained purely as historical record of exactly what Phase 7E's Windows pass validated.

**Phase 7E's full manual Windows validation applies only to the original Phase 7B/7C/7D/7E artifact,
not to this rebuild.** Nothing about that pass (scheduled-task registration, self-contained runtime
resolution, native library loading, first-run/persistence/portable-move behavior) is expected to be
affected by an Associate UI/section-visibility/PDF-layout change, but it has not been re-run against
this exact artifact.

### Build/publish/package validation performed this rebuild

1. `dotnet build CarowindsPassdown.slnx` — PASS, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — PASS, 725/725 (up from
   684/684 at the Post-Phase-7F artifact: 66 Domain + 179 Application + 154 Ui + 60 Documents + 266
   Infrastructure; the Phase 8A/8B work added tests, none were removed or skipped).
3. `dotnet publish ... -r win-x64 --self-contained true ...` (mirroring `Publish-Windows.ps1`; no
   `pwsh` on this dev host, same limitation as every prior Phase 7 sub-phase) — PASS. 271 files,
   133 MB after `.pdb` removal — same shape as every prior rebuild (no new shipped files, only
   changed content inside existing DLLs).
4. Before staging, the freshly published `Carowinds.Passdown.Desktop.dll` was directly inspected
   (byte-string search of the actual built assembly, not file timestamps alone) and confirmed to
   contain `DailyFeatureTypeOption`, `SelectedDateForPicker`, `OnIncludeHauntedAttractionsChanged`,
   and the other six `OnInclude*Changed` section-visibility handlers (`OnIncludeDailyAdmissionChanged`,
   `OnIncludeFastLaneChanged`, `OnIncludePromotionsChanged`, `OnIncludeSeasonPassesChanged`,
   `OnIncludeAddOnsChanged`, `OnIncludeOperationalNotesChanged`) — proving this publish is not a stale
   reuse of the Post-Phase-7F build output.
5. Staging + ZIP built mirroring `Build-PortableRelease.ps1` exactly (same steps documented in
   `docs/release-manifest.md`); the staged folder and the zip listing were both inspected directly —
   executable, `appsettings.json`, README/tester-guide/`.cmd` helpers/`tools/` all present; no
   Playwright, `.pdb`, test assemblies, `.git`/dev docs, database files, non-Windows binaries, or
   preview/screenshot-harness artifacts (the `passdown-preview*.png`/`.pdf` files used for Phase 8B's
   visual review live under the gitignored `artifacts/` root, outside the publish output, and were
   confirmed absent from the staged folder and the ZIP).

### Focused Windows smoke check

**NOT EXECUTED.** No Windows machine or VM was available in this session. This is recorded honestly
rather than assumed passing. The product owner intends to perform this validation manually. Before
treating this rebuilt artifact as equally trustworthy to the original Phase 7B/7C/7D/7E artifact for
a real Windows user, the focused smoke pass specified in the task that produced this rebuild (extract,
launch, confirm app-shell/date-picker/section navigation, exercise the Phase 8 Daily
Feature/section-visibility workflow, generate a one-page PDF with sections hidden and again with all
sections shown, restart and confirm persisted state, export diagnostics) is the recommended next step
against this exact ZIP.

### Rebuild verdict

| # | Item | Result |
|---|---|---|
| 1 | Build | PASS |
| 2 | Non-live test count | PASS, 725/725 (up from 684/684) |
| 3 | Publish (win-x64, self-contained) | PASS |
| 4 | Phase 8 Daily Feature / section-visibility code confirmed in published Desktop assembly | PASS (byte-level check) |
| 5 | Phase 8 redesigned PDF code confirmed in published Documents assembly | PASS (string-presence check on `AssociatePassdownDocumentModelBuilder`/`AssociatePassdownPreflight`) |
| 6 | Portable ZIP rebuilt and re-inspected | PASS |
| 7 | Release-content audit (forbidden patterns, required files, no preview artifacts) | PASS |
| 8 | SHA-256 recomputed and changed from the Post-Phase-7F artifact | PASS (confirms this is not the stale artifact) |
| 9 | `docs/release-manifest.md` updated to point at the new artifact | PASS |
| 10 | Focused Windows smoke check | **NOT EXECUTED** — no Windows host available; product owner to run manually |

**Status: artifact rebuilt and internally verified; Windows-side confirmation remains outstanding**,
same honest posture every prior rebuild recorded before its own first Windows pass.

## Phase 9F rebuild — release closeout for the full Phase 9 finalization/History pipeline

Not a new numbered product phase (Phase 9 itself spans 9A–9F) — this is the release rebuild that
closes out Phase 9 as a shipped feature set, after `docs/phase9-finalization.md`'s Phase 9E marked
the prior release ZIP explicitly stale. `docs/release-manifest.md` is the authoritative record of
exactly what this rebuild produced (SHA-256, sizes, file count, test count, schema version); this
section records the narrative, the audit performed, and what was/was not re-verified.

### What changed since the Post-Phase-8 artifact

The entire Phase 9A–9E pipeline, previously validated only by the automated suite, never packaged:

- **9A** — immutable `finalized_passdowns` domain/storage model (schema version 8), one record per
  `PassdownDate`, insert-only repository, unique index.
- **9B** — automatic finalization: every eligible passdown finalizes at/after 6:00 AM Eastern the
  day after it happened, always before that invocation's scheduled/catch-up refresh; multi-day
  missed-run recovery, oldest first; a finalization failure blocks that invocation's refresh.
- **9C** — exact frozen PDF bytes stored alongside every finalized snapshot (schema version 9,
  additive), with SHA-256/size recorded; retrieval never regenerates; pre-9C rows keep `NULL` PDF
  columns rather than being backfilled with a PDF that wouldn't be historically exact.
- **9D** — a new top-level, read-only "Passdown History" section (sibling of "Associate Passdown",
  not nested inside it): browse every finalized passdown, view its frozen detail, open/save its
  exact frozen PDF. No edit/delete/finalize control exists anywhere in this screen.
- **9E** — end-to-end integration validation of the whole pipeline against real SQLite databases
  (13 new tests), no production behavior changed.

No retrieval, Associate-editing, or other business-rule behavior changed. No Manager workflow, no
manual finalization, no delete-history control was added — all three remain explicitly deferred
(CLAUDE.md "Current scope decision"; `docs/phase9-finalization.md`). The product version was not
changed (`0.1.0-dev` remains the single authoritative version, `Directory.Build.props`).

### Final Phase 9 requirements audit (Part A)

Reviewed Phase 9A–9E as one system rather than trusting each phase's own summary in isolation —
spot-checked the load-bearing claims directly against current code/tests rather than only against
prior documentation:

| Requirement | Evidence | Status |
|---|---|---|
| Immutable finalized snapshot | `FinalizedPassdown`/`FinalizedPassdownSnapshot` are `sealed record`s; every snapshot list is defensively copied at build time | PASS |
| One finalized record per date | Unique index `ux_finalized_passdowns_passdown_date` (`Migration0008`) + application check-before-insert (`FinalizedPassdownFinalizationService`) | PASS |
| Automatic finalization cutoff (6:00 AM Eastern next day) | `FinalizationEligibility.EligibleForFinalizationAt`, DST-aware; `ScheduledRefreshHostTests`/`ScheduledPassdownFinalizerTests` | PASS |
| Finalization-before-refresh ordering | `ScheduledRefreshHost.RunAsync`/`StartupCatchUpRefresh.RunAsync` both finalize before invoking `IScheduledRefreshRunner`; `Phase9EIntegrationTests` (real host, real SQLite) | PASS |
| Persisted-draft eligibility | Only `IAssociateDraftRepository.ListSavedDraftDatesAsync()` entries are eligible — a loadable-but-unsaved workspace never is | PASS |
| Missed-run recovery | Oldest-first, all-eligible-in-one-pass; `MissedRunRecovery_MultiDayDowntime_...` (real SQLite, 4-day gap) | PASS |
| Idempotency | Re-running against an already-finalized date is a no-op (`AlreadyFinalizedCount`), never a duplicate row; unique index is final backstop even if the app-level check is bypassed | PASS |
| Finalization failure blocks refresh | A real (not simulated) PDF-generation failure blocks that invocation's refresh, preserves pre-refresh state, and succeeds on retry after the underlying issue is fixed | PASS |
| Exact frozen PDF storage | `PdfBytes` built from the same in-memory `AssociatePassdownDocumentModel` used for the snapshot, in the same transaction as the row insert | PASS |
| Frozen PDF hash/size | `PdfSha256`/`PdfSizeBytes` always populated together with `PdfBytes`; `HasFrozenPdf` is the one derived check | PASS |
| No PDF regeneration | `IFinalizedPassdownPdfRetrievalService` has no `IAssociatePassdownPdfGenerator`/`IFinalizedPassdownSnapshotBuilder` dependency — true by constructor signature | PASS |
| Pre-9C no-PDF compatibility | `HasFrozenPdf=false` rows render detail normally, PDF actions hidden; legacy-row case in the Phase 9E scale test | PASS |
| History list | Newest-first, metadata-only (`ListAsync` never selects `pdf_bytes`) | PASS |
| History detail | Reads `SnapshotJson` only, never `AssociateWorkspace`/live catalog/live park hours | PASS |
| History read-only behavior | `HistoryView.axaml` contains no `TextBox`/`CheckBox`/edit/delete/finalize control; `PassdownHistoryViewModel` defines no mutating command | PASS |
| History/Associate state isolation | Both views mounted simultaneously, toggled via `IsVisible` only; switching sections never touches `AssociateViewModel.SelectedDate` (`MainViewModelNavigationTests`) | PASS |
| Malformed/unknown-version handling | `IPassdownHistoryService.GetAsync` never throws — `Snapshot=null` + `UnavailableReason` for a newer-schema or malformed row, metadata/PDF actions stay usable | PASS |
| Diagnostic privacy | `SqliteDatabaseDiagnosticsProviderTests` regression test with a secret snapshot note + PDF marker bytes, neither ever surfaces | PASS |
| Migration safety | Schema 1→9, 7→9, and 8→9 all covered by `PortableLifecycleTests`; Migrations 0001–0007 untouched; 0008/0009 purely additive | PASS |
| Unsupported-newer-schema guard | `UnsupportedSchemaVersionException` (Phase 7D, unaffected by Phase 9) still throws before touching a newer-than-known database | PASS |
| No Manager workflow | No Manager UI/ViewModel/PDF path exists anywhere in the Phase 9 diff | DEFERRED BY DESIGN |

No item required a fix during this audit — every claim in `docs/phase9-finalization.md` held up
against direct inspection of the current code and a fresh full test run (858/858).

### History immutability audit (Part B)

Searched for `UpdateFinalizedPassdown`, `DeleteFinalizedPassdown`, any `UPDATE`/`DELETE` against
`finalized_passdowns`, and any PDF-regeneration-on-read path: none exist anywhere in `src/` or
`tests/`. `IFinalizedPassdownRepository` exposes exactly `GetByDateAsync`/`ExistsForDateAsync`/
`AddAsync`/`ListAsync` — no `Update`/`Delete` method. `SqliteFinalizedPassdownRepository.AddAsync` is
a single hand-written `INSERT`; `GetByDateAsync`/`ListAsync` are plain `SELECT`s. `HistoryView.axaml`
was read directly: its only interactive controls are `OpenFinalPdfCommand`/`SavePdfCopyCommand`
buttons — no `TextBox`, `CheckBox`, or any control that could mutate a row. No mutation path exists;
no fix or regression test was needed.

### Finalization order audit (Part C)

Re-read `ScheduledRefreshHost.RunAsync` and `StartupCatchUpRefresh.RunAsync` directly: both still
acquire the lock, run migrations, finalize eligible passdowns, and only then run the scheduled
refresh — unchanged since Phase 9B. The interactive "Check Online" path
(`AssociatePassdownService.CheckAssociateDateOnlineAsync`) still does not route through
`IScheduledPassdownFinalizer`. The 6:00 AM Eastern cutoff and the single Task Scheduler task are
unchanged; no second task or CLI flag exists.

### Final database schema audit (Part D)

Current schema version: **9**. `Migration0008CreateFinalizedPassdownsTable.Version => 8` and
`Migration0009AddFinalizedPassdownPdfColumns.Version => 9` are both registered, unmodified, and
additive; Migrations 0001–0007 are untouched. `PortableLifecycleTests` covers a clean schema reaching
version 9, and historical-schema upgrades through the current migration list; `MigrationRunnerTests`
covers the newer-than-known-version downgrade guard generically (unaffected by Phase 9). Re-running
the full migration list against an already-current database is a no-op (idempotent).

### Final data-privacy audit (Part E)

Re-read `SqliteDatabaseDiagnosticsProvider`: it still only ever reports `PRAGMA user_version`, file
size, and WAL presence — it has never queried `finalized_passdowns` and still doesn't.
`SqliteDatabaseDiagnosticsProviderTests.GetAsync_DatabaseContainsFinalizedPassdownWithSensitiveContent_NeverSurfacesItInDiagnostics`
(Phase 9C, still passing) seeds a finalized row with a distinctive secret note and PDF marker bytes
and asserts neither, nor any snapshot/PDF/operational-note field, ever appears in the reported
diagnostics. No leakage found; no fix needed.

### Final UI/navigation audit (Part F)

Re-read `MainWindow.axaml` directly: the top-level shell is exactly "Associate Passdown" / "History"
side by side, with the active section visually distinguished (accent underline + bold + accent
foreground). No Manager entry point exists anywhere. History's own header carries a "Finalized — Read
Only" badge, and its list/detail panes contain no editable controls, making its read-only nature
visually obvious without a redesign. No polish changes were made — none were needed.

### Visual validation review (Part G)

No pixel/bitmap screenshot renderer exists in this codebase — unchanged since Phase 9E's honest
assessment (`Avalonia.Headless` is used for real-XAML-loader construction checks only, deliberately
without `Avalonia.Headless.XUnit`). No source code changed between Phase 9E and this rebuild that
would affect this status. **Status carried forward as PARTIAL** (construction-level: PASS across
empty/populated/selected/dense/no-PDF/malformed-schema states, per Phase 9E's `HistoryViewTests`;
pixel-level at 1280×860/960×640: NOT EXECUTED) — not upgraded to a false PASS.

### Full automated validation (Part H)

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **PASS**, **858/858**
   (66 Domain + 221 Application + 82 Documents + 287 Infrastructure + 202 Ui), matching Phase 9E's
   count exactly (no test added/removed/weakened since).

### Release rebuild (Parts I–M)

Performed on this (non-Windows, macOS) dev host — no `pwsh` available, same limitation as every
prior Phase 7/8/9 rebuild:

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — PASS, 271 files, 134 MB after `.pdb` removal (`Publish-Windows.ps1`'s exact command).
2. Before staging, the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll`/`.Infrastructure.dll`/`.Documents.dll` were searched directly (`strings` over the actual built assembly, not timestamps) for Phase 9A–9D-only symbols — see `docs/release-manifest.md` "How this artifact was produced" for the exact list — all present, proving this publish is not a stale Phase 7/8 reuse.
3. Staged into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/` exactly per `Build-PortableRelease.ps1`'s logic: publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, both `.cmd` helpers, `README.txt` (updated this rebuild — see Part Q below), `TESTER-GUIDE.txt` (updated this rebuild — see Part P below).
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `*playwright*`, `*.Tests.dll`, `.dylib`/`.so`, `.DS_Store`) — clean, both on the staged folder and again on a fresh extraction of the finished ZIP.
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`; `shasum -a 256` computed fresh (see `docs/release-manifest.md` for the exact value — it differs from every prior artifact's hash, confirming this is not a stale reuse).
6. Release content audit: 277 files, 280 zip entries (277 files + 3 directories: release root, `LatoFont\`, `tools\`), 61,303,566 bytes compressed, 139,519,155 bytes extracted. Executable, `appsettings.json`, all managed/native runtime DLLs, README/tester-guide, both `.cmd` helpers, and `tools/` all present; no `.pdb`, no Playwright, no test assemblies, no dev docs, no user data, no non-Windows binaries.

### Tester guide / README updates (Parts P/Q)

Both `packaging/windows/TESTER-GUIDE.txt` and `packaging/windows/README-portable-release.md`
predated Phase 9D and had no mention of Passdown History at all. Updated this rebuild:

- `TESTER-GUIDE.txt` gained a new step ("14. Passdown History") in plain language: where History is,
  that finalization happens automatically the morning after (no manual finalization control exists),
  that finalized records never change, and how Open Final PDF/Save PDF Copy work — kept short,
  no architecture vocabulary. `TesterGuidePackagingTests` (still asserts no `RefreshOrchestrator`/
  `IUnitOfWork`/`LastKnownGood` and the "Export Diagnostics" mention) passes unchanged.
- `README.txt` gained a short "Passdown History" paragraph in the same plain-language style. Neither
  file was turned into a technical design document.

### Focused Windows smoke check (Part R)

**NOT EXECUTED.** No Windows machine or VM was available in this session/environment. This is
recorded honestly rather than assumed passing. Before treating this rebuilt artifact as
Windows-proven for History/finalization specifically, the following focused smoke pass (not a full
Phase 7E rerun) is the recommended next manual step, against this exact ZIP:

1. Extract the rebuilt ZIP; launch `Carowinds.Passdown.Desktop.exe`.
2. Confirm the Associate UI opens normally, then click "History" in the top-level section strip.
3. Confirm the History list renders (or shows the empty-state message if nothing has finalized yet
   on that machine).
4. If any record exists, select it and confirm the read-only detail renders; confirm the "Finalized —
   Read Only" badge is visible.
5. Click "Open Final PDF" and "Save PDF Copy"; confirm both produce/open the exact stored PDF.
6. Switch back to "Associate Passdown" and confirm the previously-selected date/in-progress edits are
   untouched.
7. Confirm the `Enable`/`Disable Automatic Refresh.cmd` helpers are still present and functional.
8. (Optional, if practical without touching real operational data) seed one eligible saved draft,
   advance past the 6:00 AM Eastern cutoff, run the scheduled task manually, and confirm a new
   History entry appears with a working frozen PDF.

### Finalization Windows smoke (Part S)

**NOT EXECUTED**, per the same no-Windows-host limitation. The real-SQLite, real-host integration
tests in `Phase9EIntegrationTests.cs` (Parts A/B/C/E/F/K/L/M of Phase 9E) stand in for this on the
dev host; they are not a substitute for an actual Task Scheduler run on Windows, and are not
represented as one.

### Release confidence gaps (Part T), carried forward honestly

| # | Item | Classification |
|---|---|---|
| 1 | History pixel-level Windows visual validation | Not executed (Part G) |
| 2 | Native Windows PDF open/save (Open Final PDF / Save PDF Copy) | Not executed |
| 3 | Task Scheduler behavior with Phase 9 finalization inserted ahead of refresh | Not executed (Part S) |
| 4 | Native x64 vs ARM64-emulation Windows validation (carried from Phase 7E) | Non-blocking confidence gap |
| 5 | Self-contained runtime on a clean machine with no .NET pre-installed (carried from Phase 7E/7F) | Non-blocking confidence gap |
| 6 | Manager workflow | Deferred by design — out of current release scope |
| 7 | Revisions / manual finalization / delete-history | Deferred by design — none implemented |

None of the above is presented as a false PASS.

### Phase 9 documentation closeout (superseded by Phase 10F for the current artifact)

The narrative below (Phase 9 requirements audit, immutability/order/schema/privacy/navigation
audits, release rebuild) is retained as the historical record of the Phase 9F rebuild. The artifact
it produced (`ccd41c76a79824b3e923e23d209b0ac15e4bf8059b491a17004ce4ca8f1979c4`) is itself now
superseded by the Phase 10F rebuild below — see `docs/release-manifest.md` for current-vs-superseded
artifact identity.

### Phase 9 documentation closeout

`docs/phase9-finalization.md` gained a final "Phase 9 complete" section (see that document) covering
9A–9F and explicitly listing revisions, manual finalization, delete-history, and the Manager workflow
as intentionally deferred — not implied to exist.

### Phase 9F rebuild verdict

| # | Item | Result |
|---|---|---|
| 1 | Phase 9 requirements audit (18 items) | PASS |
| 2 | History immutability audit | PASS — no mutation path found, no fix needed |
| 3 | Finalization order audit | PASS — unchanged |
| 4 | Database schema audit | PASS — schema version 9 |
| 5 | Data-privacy audit | PASS — no leakage found |
| 6 | UI/navigation audit | PASS — no redesign needed |
| 7 | Visual validation | PARTIAL (construction-level PASS; pixel-level NOT EXECUTED) — honest carry-forward |
| 8 | Full automated validation | PASS, 858/858, 0 warnings/errors |
| 9 | Release rebuild (publish + package) | PASS |
| 10 | Latest-code verification in artifact | PASS (byte-level symbol check across 4 assemblies) |
| 11 | Release content audit | PASS |
| 12 | New SHA-256 recorded | PASS — differs from every prior artifact |
| 13 | `docs/release-manifest.md` updated | PASS |
| 14 | Tester guide / README updated for History | PASS |
| 15 | Focused Windows smoke check | **NOT EXECUTED** — no Windows host available |
| 16 | Finalization Windows smoke check | **NOT EXECUTED** — no Windows host available |
| 17 | Phase 9 documentation closeout | PASS |

### Phase 9F release readiness decision

**READY WITH ACCEPTED LIMITATIONS.**

Every Phase 9A–9E requirement was re-audited against current code and passes; no mutation path into
finalized history exists; the full non-live suite (858/858) and a clean build both pass; the release
ZIP was rebuilt from the exact current commit, independently verified to contain Phase 9 code via a
byte-level assembly check, and re-inspected after extraction. The only open gaps are Windows-host
execution (visual, PDF-open/save, and Task Scheduler-with-finalization checks) — none of which are
new risks Phase 9 introduced beyond what Phase 7E/7F already carried for this dev-host-only build
process, and all of which are recorded above rather than glossed over. `0.1.0-dev` remains an
explicit pre-release development version, not a production 1.0 release.

## Phase 10F rebuild — release closeout for the full Phase 10 Operations Dashboard

Not a new numbered product phase (Phase 10 itself spans 10A–10F) — this is the release rebuild that
closes out Phase 10 as a shipped feature set, mirroring exactly how Phase 9F closed out Phase 9 above.
`docs/release-manifest.md` is the authoritative record of exactly what this rebuild produced
(SHA-256, sizes, file/entry counts, test count, schema version); this section records the narrative,
the audit performed, and what was/was not re-verified.

### What changed since the Phase 9F artifact

The entire Phase 10A–10E Operations Dashboard, previously validated only by the automated suite,
never packaged:

- **10A** — Dashboard foundation: a new "Dashboard" top-level section (sibling of "Associate
  Passdown"/"History"), the app's new default startup screen, `OperationsDashboardSnapshot`/
  `OperationsDashboardService`/`OperationsDashboardViewModel`/`DashboardView` read model/service/
  view-model/view stack. "Today" is resolved in America/New_York, independent of Associate's own
  selected date. Local-only load — no HTTP/Playwright/Task-Scheduler-capable constructor dependency
  exists on the Dashboard service or view model (true by constructor signature, reflection-tested).
- **10B** — Today's Operational Summary content polish: Park Status/Passdown Status/Today at a
  Glance (attendance, pre-solds, Early Ride Time, Daily Feature)/Operational Notes preview, wording
  aligned with `AssociateView`'s own vocabulary, bounded truncation for long free-text content.
- **10C** — Passdown/History/Notes integration: a "Latest Finalized" panel (metadata-only, highest
  `PassdownDate` wins, never deserializes a historical snapshot or PDF blob), and targeted "Open
  Passdown"/"Open Notes" deep links into Associate's own Overview/Notes sections (via a
  view-model-owned `AssociateSection` rail) rather than a single generic "open Associate" fallback.
- **10D** — Data Status: a 5-state overall aggregate (`Current`/`SomeStale`/`SomeUnavailable`/
  `VerificationIssue`/`NoPublishedData`) over all 7 source categories, an explicit "Check Online"
  action that reuses the existing `IAssociatePassdownService.CheckAssociateDateOnlineAsync` path (no
  second retrieval implementation), success-only "Last verified" vs. attempt-based "Last checked"
  timestamps kept distinct, and `Closed`/`NotPublished` correctly kept out of the failure buckets.
- **10E** — targeted visual/accessibility polish: automation names completed on all six panels, a
  dedicated Check Online busy indicator distinct from the header's reload indicator, a `WrapPanel`
  fix for the Data Status count-chip row at the 960×640 fallback width, tightened internal spacing on
  the Data Status card. No business/schema change.

No retrieval, Associate-editing, finalization, or History-immutability behavior changed. No Manager
workflow, Alerts system, Operations Calendar, or persistent Notes Manager was added — all remain
explicitly deferred (CLAUDE.md "Current scope decision"). The product version was not changed
(`0.1.0-dev` remains the single authoritative version, `Directory.Build.props`).

### Final Phase 10 requirements audit (Part A)

Reviewed Phase 10A–10E as one system rather than trusting each phase's own summary in isolation —
spot-checked the load-bearing claims directly against current code (constructor signatures, migration
registration, repository interface shape) and a fresh full test run, not just against prior
documentation:

| Requirement | Evidence | Status |
|---|---|---|
| Dashboard is a first-class top-level section | `MainWindow.axaml` section strip: Dashboard / Associate Passdown / History; `AppSection` enum | PASS |
| Dashboard is default startup | `MainViewModel.SelectedSection` initializer: `= AppSection.Dashboard` | PASS |
| Dashboard uses Eastern "Today" | `OperationsDashboardService.GetTodayAsync` → `EasternTimeZone`/`IClock`, never `DateTime.Now`/`.UtcNow` | PASS |
| Dashboard date independent of Associate selected date | `OperationsDashboardService` never reads `AssociateViewModel.SelectedDate`; `MainViewModelNavigationTests` | PASS |
| Dashboard load is local-only | `OperationsDashboardService(IAssociatePassdownService, IFinalizedPassdownRepository, IClock)` — no HTTP/refresh/PDF-capable dependency, confirmed by reading the constructor directly | PASS |
| Park Status / effective hours | `DashboardParkStatusSummary` reuses `ParkHoursCalculator`/`ParkHoursPresentation` — no second hours formatter | PASS |
| Custom hours / private buyout | `IsManualOverrideApplied`/`IsPrivateBuyoutApplied` classification in `BuildParkStatus`; `DashboardView_EachParkStatus_ConstructsWithoutError` theory | PASS |
| Closed / NotPublished / Unavailable states | All 7 `DashboardParkStatus` values classified, never fabricated as `Open` | PASS |
| Passdown Status | `DashboardPassdownStatusSummary` from `AssociateWorkspace.SaveStatus` + `IFinalizedPassdownRepository` metadata | PASS |
| Attendance / Pre-solds | "Not entered" empty-state text matches `AssociateView`'s own; `0` distinct from missing | PASS |
| Early Ride Time | "Not set" empty-state text; enabled/disabled both render | PASS |
| Daily Feature | Bounded 140-char truncation; empty/present logic in `OperationsDashboardPresentation.Build` | PASS |
| Operational Notes preview | 3-line prefix of `AssociateDraftPayload.OperationalNotes`, 100-char per-line bound, no second persistence model | PASS |
| Latest-finalized metadata integration | `FinalizedPassdownSummary`, highest `PassdownDate` wins (explicit `Aggregate`, not repository ordering) | PASS |
| History metadata-only guarantee | `IFinalizedPassdownRepository.GetByDateAsync` never called by Dashboard — `LoadAsync_LargeFinalizedHistorySet_RemainsMetadataOnly` (50-row seed, `GetByDateCallCount == 0`) | PASS |
| Open Passdown routing | `OpenAssociateOverviewRequested` → `Associate.SelectedSection = AssociateSection.Overview` | PASS |
| Open Notes deep-link | `OpenAssociateNotesRequested` → `Associate.SelectedSection = AssociateSection.Notes` | PASS |
| Open History routing | `OpenHistoryRequested` → `ShowHistorySectionCommand` | PASS |
| Associate date preservation | Neither navigation handler touches `AssociateViewModel.SelectedDate` | PASS |
| History state preservation | `PassdownHistoryViewModel` constructed once at composition root, `IsVisible`-only toggling; `NavigatingHistoryThenDashboardThenHistory_PreservesHistorySelection` | PASS |
| Data Status aggregation (5 states) | `DashboardOverallDataStatus`; deterministic precedence in `BuildDataHealth` | PASS |
| Closed is not a confidence failure | `Classify(Closed) => Current`; `LoadAsync_ParkHoursVerifiedClosed_CountsAsCurrentNotIssue` | PASS |
| NotPublished semantics (informational) | Only degrades to `NoPublishedData` when `CurrentCount == 0`; `LoadAsync_OneCategoryNotPublished_OverallStaysCurrentWhenAnotherIsCurrent` | PASS |
| Stale / LKG semantics | `Stale` is attempt-vs-success derived (`LastKnownGood<T>.IsStale`), not a time window; old success value/timestamp preserved through a later failure | PASS |
| Error vs. Unavailable distinction | Distinct buckets/counts (`VerificationIssueCount` vs. `UnavailableCount`), distinct precedence | PASS |
| Verification-timestamp semantics | `LastVerifiedAt` success-only (Park Hours, catalog categories); `LastCheckedAt` attempt-based, never relabeled "verified" (dated-tracked categories) | PASS |
| Explicit Check Online | `CheckOnlineCommand` reuses `IAssociatePassdownService.CheckAssociateDateOnlineAsync` — no second retrieval implementation; `IsCheckingOnline` cannot run concurrently with `ReloadCommand` | PASS |
| Local-only Reload Summary | `ReloadCommand` → `GetTodayAsync()` only; `LoadAsync_NeverTriggersOnlineRefresh` asserts empty `ManualDateRefreshCalls` | PASS |
| Partial refresh / LKG behavior | `CheckOnlineAsync_FailedRefresh_PreservesLastKnownGood`; failure shows plain-language message, never a raw exception | PASS |
| Accessibility (automation names, keyboard, no color-only status) | All six panels + all actions carry `AutomationProperties.Name`; every status is plain-language text, not color-only; tab order manually traced with no focus traps | PASS |
| Long-content safety | Daily Feature/notes truncation bounds; `DashboardView_LongDailyFeatureAndNotes_ConstructsWithBoundedText` | PASS |
| Desktop density (1280×860 no oversized cards) | Reviewed directly — Grid-based two-column panels, no per-source cards, `WrapPanel` fix for count chips | PASS |
| No Manager/Calendar/Alerts/Notes-Manager workflow added | No such UI/ViewModel/navigation entry exists anywhere in the Phase 10 diff | DEFERRED BY DESIGN |
| Windows 100%/125%/150% scaling | No Windows host available in this or the Phase 10E session | NOT EXECUTED |
| Raster/pixel visual validation | No bitmap/screenshot renderer exists in this codebase (unchanged since Phase 9E) | PARTIAL (construction-level PASS; pixel-level NOT EXECUTED) |

No item required a code fix during this audit — every claim in `docs/phase10-operations-dashboard.md`
held up against direct inspection of the current code and a fresh full test run (982/982 on a clean
run).

### Architecture audit (Part B)

Re-read the Dashboard's own dependency chain directly:

```
existing Application state/services
        |
        v
OperationsDashboardService (IAssociatePassdownService, IFinalizedPassdownRepository, IClock)
        |
        v
OperationsDashboardSnapshot
        |
        v
OperationsDashboardViewModel (IOperationsDashboardService, ILogger?)
        |
        v
DashboardView.axaml
```

- **No direct SQL from ViewModel/View**: `grep`-confirmed — no `SqliteConnection`/`SELECT`/`INSERT`
  string appears in `OperationsDashboardViewModel.cs` or `DashboardView.axaml.cs`.
- **No direct HTTP from Dashboard load**: `OperationsDashboardService`'s constructor is exactly
  `(IAssociatePassdownService, IFinalizedPassdownRepository, IClock)` — no `HttpClient`/
  `IRefreshOrchestrator`-typed parameter exists; `OperationsDashboardViewModel`'s constructor is
  exactly `(IOperationsDashboardService, ILogger<OperationsDashboardViewModel>?)`.
- **No browser dependency**: neither type references `IJobBrowserSessionFactory`/Playwright.
- **No duplicated hours/freshness/finalization logic**: `BuildParkStatus` reuses
  `ParkHoursCalculator`/`AssociateParkHoursView` computed elsewhere; `BuildDataHealth` reuses
  `AssociateVerificationState` values computed by the existing Associate composition path; no second
  finalization-eligibility or hours-arithmetic implementation exists in `Dashboard/`.
- **`MainViewModel` coordinates top-level navigation**: `SelectedSection`/`ShowDashboardSection`/
  `ShowAssociateSection`/`ShowHistorySection` remain the single owner; Dashboard's own view model has
  zero reference to `AppSection`/`AssociateViewModel`, only raising plain events.
- **`AssociateViewModel` owns its internal section**: `AssociateSection` (`Overview`/`Products`/
  `Notes`) lives on `AssociateViewModel`, not on Dashboard or `MainViewModel`.
- **History remains separate**: `PassdownHistoryViewModel` has no Dashboard-typed dependency.
- **Dashboard remains a projection, not an editor**: no `TextBox`/`CheckBox`/save/edit control exists
  anywhere in `DashboardView.axaml` — confirmed by direct read; every control is a `TextBlock`,
  `Border`, or a navigation/refresh `Button`.

No architecture violation found; no fix was needed.

### Local-only startup audit (Part C)

Confirmed directly: `App.OnFrameworkInitializationCompleted` constructs the composition root, applies
migrations, opens `MainWindow` (which now renders Dashboard by default per Part A), and only then
fires `StartupCatchUpRefresh` as a detached background task — unrelated to which section renders
first, since it never references a specific view model. `OperationsDashboardViewModel`'s constructor
fires a fire-and-forget **local** load (`IOperationsDashboardService.GetTodayAsync()`) — no HTTP,
Playwright, Check Online, or scheduled-refresh-host call is reachable from the Dashboard's own
construction path, confirmed by the same constructor-signature check as Part B.

### Dashboard date isolation audit (Part D)

Re-confirmed: `OperationsDashboardService` never reads `AssociateViewModel.SelectedDate` anywhere in
`Dashboard/OperationsDashboardService.cs`; `MainViewModelNavigationTests` covers Open
Passdown/Open Notes both landing on whatever date Associate already has selected, and plain
section-switching (Dashboard ↔ Associate ↔ History) never touching `SelectedDate` or Associate's
internal `AssociateSection`. No regression found; no new coverage was needed.

### History integration audit (Part E)

Re-confirmed the metadata-only guarantee from the Dashboard's own call site:
`IFinalizedPassdownRepository.GetByDateAsync` (the only method that can return `PdfBytes`/
`SnapshotJson`) is never called anywhere in `OperationsDashboardService`. "Latest finalized" is
computed by an explicit highest-`PassdownDate` `Aggregate` over `ListAsync`'s result — never a
`SqliteFinalizedPassdownRepository`-ordering assumption. A no-PDF legacy row and a malformed/
future-schema row both still populate `LatestFinalized` correctly, because the Dashboard never
deserializes `SnapshotJson` or touches the PDF column in the first place — there is nothing on that
path to fail on. `Open History` routes through the same single `ShowHistorySectionCommand`
`MainViewModel` already owned before Phase 10 — no new cross-ViewModel coupling was introduced.

### Data Status semantics audit (Part F)

Re-derived the precedence directly from `OperationsDashboardService.BuildDataHealth`:

```
VerificationIssueCount > 0   -> VerificationIssue
StaleCount > 0                 -> SomeStale
UnavailableCount > 0            -> SomeUnavailable
CurrentCount == 0               -> NoPublishedData
otherwise                       -> Current
```

Confirmed: `Error` maps to `VerificationIssue`; `Stale` stays a distinct bucket, derived from
`LastKnownGood<T>.IsStale` (attempt-vs-success), never a time-window comparison; `Unavailable` stays
distinct from `VerificationIssue`; `NotPublished` never degrades the overall status on its own —
it only participates via `NoPublishedData` when nothing else is confirmed either; `Closed` (a
genuinely verified "park is closed") counts as `Current`, not an issue; `LastVerifiedAt` is populated
only from success evidence and is never a failed attempt's timestamp; a stale value's old
`LastVerifiedAt` survives a later failed attempt untouched. No new time-window freshness system was
introduced or found. Matches `docs/phase10-operations-dashboard.md` Phase 10D exactly.

### Check Online audit (Part G)

Confirmed `CheckOnlineCommand` calls `IOperationsDashboardService.CheckOnlineAsync`, which itself
calls the same `IAssociatePassdownService.CheckAssociateDateOnlineAsync`/
`IRefreshOrchestrator.RunManualDateRefreshAsync` path Associate's own Check Online already uses — no
second retrieval implementation exists. `IsCheckingOnline` and `IsLoading` (Reload) each
cross-reference the other's `CanExecute`, so neither can run concurrently with the other or with
itself. Current local values stay visible while busy (no blanking). A failed Check Online preserves
the existing snapshot and shows a plain-language message — no raw exception, URL, or HTTP status is
ever surfaced (the presentation types have no field capable of carrying one). Reload Summary
(`ReloadCommand` → `GetTodayAsync()`) stays local-only and is a visibly distinct action/label from
Check Online, both on the header and the Data Status card. No violation found.

### Dashboard UI / accessibility audit (Parts H/I)

Reviewed `DashboardView.axaml` directly against the 1280×860 target and the 960×640 `ScrollViewer`
fallback (unchanged structurally since Phase 10E, which already closed the count-chip-clipping and
automation-name gaps this audit would otherwise have flagged). All named panels/actions
(Dashboard/Associate Passdown/History section buttons, Today's Operation, Passdown Status, Today at a
Glance, Operational Notes, Latest Finalized, Data Status, Open Passdown, Open Notes, Open History,
Reload/"Refresh", Check Online) carry `AutomationProperties.Name`, confirmed by reading the XAML and
by `DashboardView_WithSnapshot_ExposesCriticalAutomationNames` passing. No status is color-only.
Keyboard tab order and disabled-while-busy behavior were re-confirmed unchanged from Phase 10E's own
manual trace. No omission found; no fix was needed.

### Diagnostic privacy audit (Part J)

`grep -rl Dashboard` over `Application/Diagnostics/` and `Infrastructure/Diagnostics/` returns nothing
— `DiagnosticReportBuilder`/`DiagnosticExportService` were not touched by any Phase 10 subphase and
have no reference to `DashboardDataHealthSummary`/`DashboardSourceStatusEntry`/any Dashboard type.
Diagnostics still report only version/schema metadata, source statuses/timestamps, task existence, and
finalized counts/dates — never draft content, Operational Notes text, Daily Feature text, finalized
snapshot JSON, or PDF bytes. No expansion was made; none was needed.

### Database / schema audit (Part K)

Confirmed directly from `App.axaml.cs`'s migration registration: exactly Migrations 0001–0009 are
registered, unchanged from Phase 9F — **no Migration0010 or later exists**. Current schema version:
**9**. `FinalizedPassdownSnapshot.CurrentSchemaVersion = 1` — unchanged. `PortableLifecycleTests`
(unmodified) still covers schema 1→9 and 8→9 upgrades; `MigrationRunnerTests`' newer-than-known-version
guard is unaffected. Phase 9's finalized data remains readable — nothing in Phase 10 reads or writes
`finalized_passdowns` differently than Phase 9C/9D established.

### Final automated validation (Part L)

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` (run 1) — **PASS**, **982/982**
   (66 Domain + 256 Application + 82 Documents + 287 Infrastructure + 291 Ui), matching Phase 10E's
   documented baseline exactly.
3. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` (run 2) — **981/982**: one
   failure in `NoManagerWorkflowTests.AssociateView_LogicalTree_ContainsNoManagerLabeledControl`, an
   `Avalonia.ClassBindingManager.Bind`/XAML-loader race under this suite's parallel test execution.
   This is the **same pre-existing flake first recorded in Phase 6D** (`docs/architecture.md`,
   "Pre-existing test flake") — not a new regression, not related to any Phase 10 change (the failing
   test lives in `AssociateView`'s Manager-workflow-absence check, a file Phase 10 did not touch).
   Re-running just `Carowinds.Passdown.Ui.Tests` on its own immediately afterward passed **291/291**
   cleanly, confirming the failure is a parallel-execution timing race, not a deterministic defect —
   consistent with the flake's documented history (present in Phase 6D's own re-run, absent from
   Phase 9F's, present again here). No test was weakened or disabled to make this "pass"; the root
   cause (Avalonia headless XAML binding under xUnit's parallel test collections) was identified,
   matches precedent, and is recorded honestly rather than hidden.

### Release rebuild (Parts M–Q)

Performed on this (non-Windows, macOS) dev host — no `pwsh` available, same limitation as every prior
Phase 7/9/10 rebuild:

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — PASS, 271 files, 134 MB after `.pdb` removal (`Publish-Windows.ps1`'s exact command, staging cleaned first).
2. Before staging, the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` were searched directly (`strings` over the actual built assembly, not timestamps) for Phase 10A–10E-only symbols — see `docs/release-manifest.md` "How this artifact was produced" for the exact list — all present (`OperationsDashboardViewModel` ×46, `DashboardView` ×49, `DashboardDataHealthPresentation` ×25, etc.), proving this publish is not a stale Phase 9F reuse.
3. Staged into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/` exactly per `Build-PortableRelease.ps1`'s logic: publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, both `.cmd` helpers, `README.txt` (updated this rebuild — see Part T below), `TESTER-GUIDE.txt` (updated this rebuild — see Part T below).
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `*playwright*`, `*.Tests.dll`, `.dylib`/`.so`, `.DS_Store`) — clean, both on the staged folder and again on a fresh extraction of the finished ZIP. (An initial substring-based check flagged `Tmds.DBus.Protocol.dll` as a false positive against a loose `*.db*` pattern; re-run with the same extension-anchored globs `Build-PortableRelease.ps1` itself uses confirmed it is not a match — a Linux D-Bus interop assembly pulled in transitively by Avalonia's cross-platform package graph, present in every prior Windows publish of this app, harmless and unused on the shipped Windows target.)
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`; `shasum -a 256` computed fresh (see `docs/release-manifest.md` for the exact value — it differs from every prior artifact's hash, confirming this is not a stale reuse).
6. Release content audit: 277 files, 280 zip entries (277 files + 3 directories: release root, `LatoFont\`, `tools\`), 61,331,017 bytes compressed, 139,629,103 bytes extracted. Executable, `appsettings.json`, all managed/native runtime DLLs, README/tester-guide, both `.cmd` helpers, and `tools/` all present; no `.pdb`, no Playwright, no test assemblies, no dev docs, no user data, no non-Windows binaries.
7. Independent verification: the ZIP was extracted fresh to a scratch directory outside `artifacts/`; `diff -rq` against the staging folder reported byte-for-byte identical content; `Carowinds.Passdown.Desktop.dll`'s SHA-256 inside the re-extracted ZIP was independently confirmed to match the staged copy; the Phase 10 symbol search (step 2 above) was repeated against the re-extracted ZIP's own assemblies and found identical results.

### Tester guide / README updates (Part T)

Both `packaging/windows/TESTER-GUIDE.txt` and `packaging/windows/README-portable-release.md` predated
Phase 10A and described Associate Passdown as the app's landing screen. Updated this rebuild:

- `TESTER-GUIDE.txt` renumbered to insert a new step 3 ("The Dashboard (default screen)") describing
  Park Status, Passdown Status, Today at a Glance, Operational Notes preview, Latest Finalized, and
  Data Status/Check Online in plain language, plus a new step 4 walking through Open Passdown/Open
  Notes/Open History; the later step referencing the app's date picker was updated to point at the
  Associate Passdown screen explicitly (Dashboard has no date picker of its own). No architecture
  vocabulary (read models, services, view models) was introduced.
- `README-portable-release.md` gained a short "The Dashboard" paragraph in the same plain-language
  style, immediately after "Getting started." Neither file was turned into a technical design
  document.

### Focused Windows smoke check (Part V)

**NOT EXECUTED.** No Windows machine or VM was available in this session/environment — the same
limitation recorded for every prior Phase 7/9/10 rebuild performed from this dev host. Before treating
this rebuilt artifact as Windows-proven for the Dashboard specifically, the following focused smoke
pass (not a full Phase 7E rerun) against this exact ZIP is the recommended next manual step —
`packaging/windows/TESTER-GUIDE.txt` steps 2–4 cover the same ground for a nontechnical tester:

1. Extract the rebuilt ZIP; launch `Carowinds.Passdown.Desktop.exe`.
2. Confirm the Dashboard is the default landing screen, with the three-section strip
   (Dashboard/Associate Passdown/History) visible and Dashboard active.
3. Confirm Park Status, Today at a Glance, Operational Notes preview, Latest Finalized (if any test
   data exists), and Data Status all render with real values (not blank cards).
4. Click "Refresh" (header) — confirm it completes instantly with no online activity.
5. Click "Open Passdown" — confirm it opens Associate Passdown's Overview. Return to Dashboard.
6. Click "Open Notes" — confirm it opens Associate Passdown's Notes area, and that the previously
   selected Associate date was preserved across both round trips.
7. Click "Open History" — confirm History opens and behaves correctly on return to Dashboard.
8. Click "Check Online" on the Data Status card — confirm the dedicated busy indicator appears, the
   existing values stay visible while busy, and the result (success, partial, or failure) renders in
   plain language with no raw exception/URL/HTTP detail.
9. Confirm "Export Diagnostics" still works and its output contains no draft/note/Daily-Feature/
   snapshot/PDF content.
10. Confirm no startup or migration regression (schema still resolves to 9).

### Windows scaling validation (Part W)

**NOT EXECUTED**, per the same no-Windows-host limitation carried forward from Phase 10E (which
already recorded 100%/125%/150% as NOT EXECUTED for the same reason — no Windows machine reachable in
this environment, macOS/darwin dev host only). No source or layout change occurred between Phase 10E
and this rebuild that would affect this status.

### Phase 9 regression smoke (Part X)

**NOT EXECUTED**, per the same no-Windows-host limitation. `Phase9EIntegrationTests.cs` (real SQLite,
real hosts) continues to stand in for this on the dev host, unaffected by any Phase 10 change — Phase
10 did not touch finalization/History code, and the full non-live suite (including those tests)
passes.

### Automatic-refresh helper smoke (Part Y)

**NOT EXECUTED** for live Task Scheduler install/rebind, per the same no-Windows-host limitation.
Static content coverage remains green: `PortableSetupHelperTests` (17 tests, part of the 982/982
total) confirms both `.cmd` helpers exist at the release root, resolve their own directory via
`%~dp0`, contain no developer-specific path, never permanently change execution policy, and stay in
sync with `ScheduledTaskPlan.TaskName`/`CommandLineArguments.ScheduledRefreshFlag` — unaffected by any
Phase 10 change (Phase 10 touched no file under `Application/Refresh`, `Infrastructure/Storage`, or
`packaging/windows/*.ps1`/`*.cmd`).

### Visual confidence gap (Part Z)

No native Windows manual inspection was performed this rebuild (no Windows host available), so there
is no new manual-visual evidence to distinguish from headless raster validation. Headless raster
validation itself remains unavailable in this codebase (no bitmap/screenshot renderer exists) — this
is **not** called a PASS; it is carried forward as **PARTIAL** (construction-level real-XAML-loader
checks: PASS; pixel-level: NOT EXECUTED), exactly matching Phase 10A–10E's own honest precedent.

### Release confidence gaps (Part AA), carried forward honestly

| # | Item | Classification |
|---|---|---|
| 1 | Dashboard pixel-level Windows visual validation | Not executed (Part Z) |
| 2 | Windows 100%/125%/150% display-scaling validation | Not executed (Part W, carried from Phase 10E) |
| 3 | Focused Windows Dashboard smoke test | Not executed (Part V) — no Windows host available |
| 4 | Phase 9 History/finalization regression smoke on this rebuild | Not executed (Part X) — automated real-SQLite coverage stands in |
| 5 | Automatic-refresh Task Scheduler live install/rebind on this rebuild | Not executed (Part Y) — static content coverage stands in |
| 6 | Native x64 vs. ARM64-emulation Windows validation (carried from Phase 7E) | Non-blocking confidence gap |
| 7 | Self-contained runtime on a clean machine with no .NET pre-installed (carried from Phase 7E/7F) | Non-blocking confidence gap |
| 8 | Pre-existing `NoManagerWorkflowTests` Avalonia parallel-test flake | Non-blocking confidence gap — reproduced this session (Part L), same known cause since Phase 6D, standalone re-run passes cleanly |
| 9 | Manager workflow, Alerts, Operations Calendar, persistent Notes Manager | Deferred by design — out of current release scope |
| 10 | Revisions / manual finalization / delete-history | Deferred by design — none implemented |

None of the above is presented as a false PASS.

### Phase 10 documentation closeout

`docs/phase10-operations-dashboard.md` gained a final "Phase 10 complete" section (see that document)
covering 10A–10F and explicitly listing Alerts, an Operations Calendar, a persistent Notes Manager,
the Manager workflow, and further freshness time-window redesign as intentionally deferred — not
implied to exist.

### Phase 10F rebuild verdict

| # | Item | Result |
|---|---|---|
| 1 | Phase 10 requirements audit (32 items) | PASS |
| 2 | Architecture audit | PASS — no violation found, no fix needed |
| 3 | Local-only startup audit | PASS — unchanged |
| 4 | Dashboard date isolation audit | PASS — unchanged |
| 5 | History integration audit | PASS — metadata-only guarantee holds |
| 6 | Data Status semantics audit | PASS — precedence/timestamp rules confirmed |
| 7 | Check Online audit | PASS — single retrieval implementation, LKG preserved |
| 8 | Dashboard UI / accessibility audit | PASS — no omission found |
| 9 | Diagnostic privacy audit | PASS — no leakage found |
| 10 | Database schema audit | PASS — schema version 9, snapshot schema 1, unchanged |
| 11 | Full automated validation | PASS, 982/982 (run 1); 981/982 (run 2, one pre-existing flake, non-Dashboard, standalone re-run 291/291 clean); 0 warnings/errors |
| 12 | Release rebuild (publish + package) | PASS |
| 13 | Latest-code verification in artifact | PASS (byte-level symbol check, publish output and independently re-extracted ZIP) |
| 14 | Release content audit | PASS |
| 15 | New SHA-256 recorded | PASS — differs from every prior artifact; Phase 9F artifact explicitly marked superseded |
| 16 | `docs/release-manifest.md` updated | PASS |
| 17 | Tester guide / README updated for Dashboard | PASS |
| 18 | Focused Windows Dashboard smoke test | **NOT EXECUTED** — no Windows host available |
| 19 | Windows 100/125/150 scaling validation | **NOT EXECUTED** — no Windows host available |
| 20 | Phase 9 regression smoke on this rebuild | **NOT EXECUTED** — automated coverage stands in |
| 21 | Automatic-refresh helper live smoke on this rebuild | **NOT EXECUTED** — static content coverage stands in |
| 22 | Phase 10 documentation closeout | PASS |

### Phase 10F release readiness decision

**READY WITH ACCEPTED LIMITATIONS.**

Every Phase 10A–10E requirement was re-audited against current code and passes; no architecture
violation, date-isolation regression, History-immutability risk, or diagnostics-privacy leak was
found; the full non-live suite (982/982 on a clean run) and a clean build both pass; the release ZIP
was rebuilt from the exact current commit, independently verified to contain Phase 10 Dashboard code
via a byte-level assembly check on both the publish output and the re-extracted ZIP, and byte-for-byte
re-inspected after extraction. The prior (Phase 9F) artifact is explicitly and unambiguously marked
superseded — its real hash, not the mismatched hash named in the Phase 10F task brief (see
`docs/release-manifest.md`'s "Note on a discrepancy" for that detail). The only open gaps are
Windows-host execution (Dashboard visual, display-scaling, and focused smoke checks) — none of which
are new risks Phase 10 introduced beyond what Phase 7E/9F already carried for this dev-host-only build
process, and all of which are recorded above rather than glossed over. `0.1.0-dev` remains an explicit
pre-release development version, not a production 1.0 release.

## Phase 11F rebuild — release closeout for the full Phase 11 persistent Operational Notes feature

Not a new numbered product phase (Phase 11 itself spans 11A–11F) — this is the release rebuild that
closes out Phase 11 as a shipped feature set, mirroring exactly how Phase 10F closed out Phase 10
above. `docs/release-manifest.md` is the authoritative record of exactly what this rebuild produced
(SHA-256, sizes, file/entry counts, test count, schema versions); this section records the narrative,
the audit performed, and what was/was not re-verified.

### What changed since the Phase 10F artifact

The entire Phase 11A–11E persistent Operational Notes feature, previously validated only by the
automated suite, never packaged:

- **11A** — foundation: `OperationalNote` domain record (stable `Guid` identity, closed
  Category/Priority enums, effective date range, persisted `Active`/`Resolved` status plus a derived
  `Upcoming`/`Active`/`Expired`/`Resolved` lifecycle), `OperationalNotesService`/
  `IOperationalNoteRepository`/`SqliteOperationalNoteRepository` (local-only, no HTTP/Playwright/
  Associate-draft dependency by constructor shape), Migration 10 (`operational_notes` table). No
  delete method, no Reopen operation, no `IsPinned` field — all deliberately deferred/omitted.
- **11B** — Operational Notes Manager UI: a new "Operational Notes" top-level section (Dashboard
  remains default startup), master/detail create/edit/resolve screen, lifecycle filter tabs
  (default Active), dirty-state protection that also disables other top-level navigation mid-edit,
  resolved notes read-only, expired notes still editable.
- **11C** — Dashboard integration: a read-only "Active Operational Notes" panel (Eastern "Today",
  Important-first, preview capped at 3 with an overflow count), kept visually and architecturally
  separate from the existing Associate-draft "Passdown Notes" preview (renamed from "Operational
  Notes" to make room for the new name). One query per Dashboard load; no include/remove/replace
  action exists on the Dashboard.
- **11D** — explicit, per-date passdown inclusion: `AssociateOperationalNoteSelection` freezes a
  persistent note's display content into `AssociateDraftPayload.SelectedOperationalNotes`
  (schema version 3) at the moment a supervisor clicks Add; selections are keyed to the Associate's
  operating date, duplicate-safe, and never resync automatically. Included notes are rendered into
  the existing generic `OperationalNotes` string list at finalization/PDF time (no new structured
  snapshot field — `FinalizedPassdownSnapshot.CurrentSchemaVersion` stays 1).
- **11E** — filtering, carry-forward presentation, and source-change awareness: category (All + 7)
  and priority (All/Important/Normal) filters added to the Notes Manager alongside the existing
  lifecycle tabs; display-only carry-forward text ("Until resolved", "Expires today", "Starts
  tomorrow", etc.); an `OperationalNoteSourceComparisonState` (Unchanged/ContentChanged/Resolved/
  NoLongerApplicable/Missing) computed fresh on every load, informational only except
  `ContentChanged`, which offers an explicit "Replace with Current Version" action
  (`ReplaceOperationalNoteWithCurrentAsync`). No auto-sync, no auto-remove, no schema change.

No retrieval, business-rule, finalization/History-immutability, or existing Associate-editing behavior
changed. No Manager workflow, Alerts system, Operations Calendar, reordering, or text search was
added — all remain explicitly deferred (CLAUDE.md "Current scope decision"). The product version was
not changed (`0.1.0-dev` remains the single authoritative version, `Directory.Build.props`).

### Final Phase 11 requirements audit (Part A)

Audited Phase 11A–11E as one system by reading the actual current code and tests directly (constructor
signatures, service/repository method bodies, migration registration, the real PDF/finalization/
History call graph) rather than trusting each sub-phase's own summary in isolation, plus a fresh full
test run:

| Requirement | Evidence | Status |
|---|---|---|
| Persistent note model / stable Guid identity | `OperationalNote` record, `Id: Guid` independent of content | PASS |
| Categories (closed enum, 7 values) | `OperationalNoteCategory` | PASS |
| Priority (Normal/Important, no Critical) | `OperationalNotePriority` | PASS |
| Effective date semantics | `EffectiveFrom`/`EffectiveThrough?`, validated `Through >= From` | PASS |
| Active/Resolved persisted status | `OperationalNoteStatus`; `ResolveAsync` idempotent, no delete | PASS |
| Derived Upcoming/Active/Expired/Resolved lifecycle | `OperationalNoteLifecycle.Derive` | PASS |
| Relevance logic (one authoritative rule) | `OperationalNoteRelevance.IsRelevantForDate`, re-applied in both SQL and C# | PASS |
| Deterministic ordering (two named orderings) | `OperationalNoteOrdering.ByDefaultListOrder`/`ByRelevance` | PASS |
| No hard delete | `IOperationalNoteRepository` has no delete method (by design, same convention as `IFinalizedPassdownRepository`) | PASS |
| Local-only persistence/service | `OperationalNotesService`/`SqliteOperationalNoteRepository` constructors carry no HTTP/Playwright dependency | PASS |
| Operational Notes top-level navigation | `AppSection` = Dashboard / Associate Passdown / Operational Notes / History (exactly 4 values) | PASS |
| Dashboard remains default startup | `MainViewModel.SelectedSection` initializer unchanged: `= AppSection.Dashboard` | PASS |
| Notes Manager master/detail | `OperationalNotesView.axaml` Grid-based list + detail/editor | PASS |
| Lifecycle / category / priority filters | `OperationalNotesViewModel.LifecycleFilter`/`CategoryFilter`/`PriorityFilter`, default Active/All/All, composed deterministically over cached list | PASS |
| Create/edit/resolve | `NewNoteCommand`/edit flow/`ResolveAsync`, service is sole validation authority | PASS |
| Resolved read-only / expired editable | `SelectedDetail.IsResolved` hides action row; expired notes remain editable via normal Save | PASS |
| Dirty-state protection | `IsEditing` disables Reload/New/Edit/Resolve and other top-level nav mid-edit | PASS |
| Dashboard today-relevant persistent notes | `OperationsDashboardService.BuildActiveOperationalNotesAsync`, Eastern Today, one query per load | PASS |
| Dashboard preview limit/overflow | `DashboardOperationalNotesSummary.MaxPreviewCount = 3`, `Count` always the true total | PASS |
| Explicit passdown inclusion / no automatic inclusion | `SelectOperationalNoteAsync` — only caller-invoked; zero references to `OperationalNote` under `Application/Refresh`/scheduled-refresh code | PASS |
| Selection-time freeze / date-specific selections | `AssociateOperationalNoteSelection` copies fields at selection time; lives inside per-date `AssociateDraftPayload`, no global flag on `OperationalNote` | PASS |
| Duplicate prevention / deselect-reselect | `SelectOperationalNoteAsync` checks `Any(...)` before appending; idempotent-call and safe-no-op-deselect tests both pass | PASS |
| Source edits/resolve/no-longer-relevant/missing do not auto-sync or auto-remove | `OperationalNoteSourceComparisonState` computed read-only every load, never mutates `selections`; only explicit Replace changes a selection | PASS |
| Explicit Replace with Current Version | `ReplaceOperationalNoteWithCurrentAsync` overwrites in place at the existing index, preserving order | PASS |
| Manual daily notes independence | `AssociateDraftPayload.OperationalNotes` (manual) and `.SelectedOperationalNotes` (persistent) are separate fields; no code path merges them silently | PASS |
| PDF inclusion | `AssociatePassdownDocumentModelBuilder.FormatSelectedOperationalNote` prints only Title/Body/category-priority prefix — no Guid/timestamp/comparison-state leak | PASS |
| Finalization from frozen draft copy | `ScheduledPassdownFinalizer`/`FinalizedPassdownFinalizationService` read the already-composed `AssociateWorkspace`; no live `IOperationalNotesService` call at finalize time | PASS |
| History immutability | `FinalizedPassdownSnapshot.OperationalNotes` is plain formatted text; `PassdownHistoryService`/History code never references `OperationalNote` | PASS |
| Privacy | `SqliteDatabaseDiagnosticsProvider` never queries note content; all note logging is `{NoteId}`-only | PASS |
| Accessibility | `AutomationProperties.Name` present across Notes Manager, Associate inclusion UI, and the Dashboard's new panel/action | PASS |
| Schema/version compatibility | Database schema 10, `AssociateDraftPayload` schema 3, `FinalizedPassdownSnapshot` schema 1 — all additive, no historical migration modified | PASS |
| No forbidden features | No Delete, no Reopen, no global "Included" badge, no Manager/Alerts/Calendar nav, no reordering, no text search | PASS |
| Windows 100%/125%/150% scaling | No Windows host available this session | NOT EXECUTED |
| Raster/pixel visual validation | No bitmap/screenshot renderer exists in this codebase (unchanged since Phase 9E/10F) | PARTIAL (construction-level PASS; pixel-level NOT EXECUTED) |

One genuine gap was found and fixed during this audit (not a functional defect, a verification gap —
see "Fix made during this audit" below); no other item required a code fix.

### Part B — no-automatic-inclusion audit (the core product rule)

Re-confirmed the authoritative Phase 11 rule directly against current code: **persistent Operational
Notes are never automatically included in a daily Associate passdown.** Every call site of
`IOperationalNotesService.ListRelevantForDateAsync`/`ListAsync` was enumerated:

- `OperationsDashboardService.BuildActiveOperationalNotesAsync` — read-only Dashboard preview, never
  writes to `AssociateDraftPayload`.
- `AssociatePassdownService.GetRelevantOperationalNoteSuggestionsAsync` — returns suggestions only;
  nothing consumes its result to mutate a draft.
- `AssociatePassdownService.SelectOperationalNoteAsync`/`ReplaceOperationalNoteWithCurrentAsync` — both
  require an explicit caller-supplied `operationalNoteId`; neither is ever invoked from a load,
  refresh, autosave, or scheduled-refresh path.
- `AssociatePassdownService`'s internal source-comparison composition — informational only, never
  writes.

Zero references to `OperationalNote`/`IOperationalNotesService` exist under
`Application/Refresh/*` or the Desktop scheduled-refresh code. Direct regression test:
`AssociatePassdownServiceTests.NoAutomaticSync_ReloadOrCheckOnline_NeverUpdatesFrozenSelection`. No
violation found.

### Part C — frozen-copy audit

`AssociateOperationalNoteSelection` copies every display field (`Title`, `Body`, `Category`,
`Priority`, `EffectiveFrom`, `EffectiveThrough`) at the moment `SelectOperationalNoteAsync` runs.
`ReplaceOperationalNoteWithCurrentAsync` is the **only** code path that overwrites an existing
selection, and it does so in place at the selection's existing list index (order preserved). Source
title/body/category/priority/effective-date edits, resolve, and a missing source note were all traced
through the actual service methods and confirmed to never reach an existing selection outside that one
explicit method. Regression tests:
`ReplaceOperationalNoteWithCurrentAsync_NeverMutatesSourceNote`,
`NoAutomaticSync_ReloadOrCheckOnline_NeverUpdatesFrozenSelection`. No violation found.

### Part D — date-specific selection audit

`SelectedOperationalNotes` lives inside the per-date `AssociateDraftPayload`, loaded via
`IAssociateDraftRepository.GetAsync(date)` — there is no global inclusion flag anywhere on
`OperationalNote` itself. Regression tests:
`Selections_AreDateSpecific_NeverLeakAcrossDates`,
`SourceDateChangeAfterSelection_DoesNotRemoveSelectedCopy`. No cross-date leakage found.

### Part E — source comparison audit

`OperationalNoteSourceComparisonState` (`Unchanged`/`ContentChanged`/`Resolved`/`NoLongerApplicable`/
`Missing`) is computed fresh on every compose and never persisted or written back to `selections` or
the source note. Only `ContentChanged` correlates with an offered "Replace with Current Version"
action in the UI; `Resolved`/`NoLongerApplicable`/`Missing` are informational text only. No expansion
of these states was made (none was needed or requested). No violation found.

### Part F — filter audit

Lifecycle (`All`/`Active`/`Upcoming`/`Expired`/`Resolved`, default `Active`), category (`All` + 7), and
priority (`All`/`Important`/`Normal`) filters compose deterministically over the cached, already-sorted
note list in `OperationalNotesViewModel.ApplyFilter` — filtering is pure derivation and never mutates
an underlying note. Search and reordering remain deferred, as scoped. No violation found.

### Part G — carry-forward presentation audit

Confirmed display-only: `OperationalNoteDateRangeFormatter` produces text ("Until resolved", "Expires
today", "Through \<date\>", "Starts tomorrow", "Expired \<date\>") with no recurrence model, no
automatic duplication into future dates, and no automatic passdown carry-forward anywhere in the code.

### Part H/I — Notes Manager and Dashboard integration audit

Re-confirmed against `OperationalNotesView.axaml`/`DashboardView.axaml` directly: no Delete, no
Reopen, no global "Included" badge on `OperationalNote`, no Manager/Alerts/Calendar section. Dashboard
uses Eastern Today, one query per load, preview capped at 3 with an accurate overflow count,
title-only summary, and clearly distinguishes "Passdown Notes" (the existing Associate-draft preview)
from "Active Operational Notes" (the new persistent-note panel); "Open Operational Notes" is generic
top-level navigation, and the Dashboard has no include/remove/replace action of its own. Dashboard
Data Status remains untouched by this phase.

### Part J/K — Associate inclusion UI and autosave/concurrency audit

`AssociateView`'s Notes area clearly distinguishes manual daily notes, included persistent notes
("Included persistent operational notes"), and available suggestions ("Available persistent
operational notes"), each with a distinct `AutomationProperties.Name`. `SelectOperationalNoteAsync`
is idempotent on repeated calls (`SelectOperationalNoteAsync_CalledTwice_IsIdempotent` — asserts
`Assert.Single`); `DeselectOperationalNoteAsync` is a safe no-op when the note was never selected.
Both re-read the saved draft directly rather than relying on in-memory caller state, so rapid
Add/Remove, tab switches, date switches, and reload/restart scenarios cannot produce a stale-autosave
overwrite or duplicate selection. No sleeps were added to any test.

### Part L/M/N — PDF, finalization, and History audit

`AssociatePassdownDocumentModelBuilder.FormatSelectedOperationalNote` prints only a
"Persistent"/"Persistent (Important)" prefix plus Title/Body — direct regression test:
`AssociatePassdownDocumentModelBuilderTests.SelectedOperationalNotes_NeverExposesSourceIdOrTimestamps`.
`ScheduledPassdownFinalizer.FinalizeEligibleAsync` calls `AssociatePassdownService.LoadAssociateDateAsync`
exactly once per eligible date and hands the resulting (already-composed, frozen-selection-containing)
`AssociateWorkspace` to `FinalizedPassdownFinalizationService` — no live `IOperationalNotesService` call
exists anywhere on the finalization path. `FinalizedPassdownSnapshot.OperationalNotes` is a plain
`IReadOnlyList<string>` with no `OperationalNote.Id` or other live reference, and `PassdownHistoryService`
has zero references to `OperationalNote`/`IOperationalNotesService`.

**Fix made during this audit:** the required Part M scenario ("select source note → source note
changes → do NOT replace → finalize → finalized snapshot/PDF contain the old selected version," and
its explicit-Replace counterpart) was previously provable only by composing two separately-tested
facts (frozen selections are never resynced; finalization only reads the persisted draft) rather than
by one direct test. Closed by adding
`FinalizedPassdownSnapshotBuilderTests.Build_SelectedNoteSourceEditedWithoutReplace_SnapshotStillShowsFrozenOldText`
and `Build_SelectedNoteAfterExplicitReplace_SnapshotShowsNewText`
(`tests/Carowinds.Passdown.Documents.Tests/Associate/FinalizedPassdownSnapshotBuilderTests.cs`) — both
pass. This was a test-coverage gap, not a functional regression; no production code changed as a
result.

### Part O — version/compatibility audit

Confirmed directly in code: database schema constant **10** (`Migration0010CreateOperationalNotesTable`
is the highest of exactly 10 registered migrations in `App.axaml.cs`), `AssociateDraftPayload
.CurrentSchemaVersion` = **3**, `FinalizedPassdownSnapshot.CurrentSchemaVersion` = **1**. All three
values were read directly from source, not from prior documentation. `PortableLifecycleTests` already
covers the 9→10 migration (new table/index shape, existing `associate_drafts`/`finalized_passdowns`
rows untouched); pre-Phase-11 drafts default to zero selections (an added field with a default empty
collection, not a breaking schema change). No version was changed this phase.

### Part P/Q — privacy and accessibility audit

`SqliteDatabaseDiagnosticsProvider` still only reports `PRAGMA user_version`, file size, and WAL
presence — it has never queried `operational_notes` row content. All operational-note logging in
`OperationalNotesViewModel`/`AssociateViewModel` logs `{NoteId}` only, never Title/Body. Accessibility
names are present for the Notes Manager (nav, list, filters, editor fields, Save/Cancel/Edit/Resolve),
Associate inclusion (Included/Available headings, Add/Remove/Replace, per-row composed names), and the
Dashboard ("Active operational notes", "Open operational notes"). No expansion was made; none was
needed.

### Final automated validation (Part T)

1. `dotnet build CarowindsPassdown.slnx` — **PASS**, 0 warnings, 0 errors.
2. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` (run 1) — **PASS**, **1240/1240**
   (66 Domain + 351 Application + 94 Documents + 317 Infrastructure + 412 Ui), matching the documented
   Phase 11E baseline exactly.
3. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` (run 2) — **PASS**, **1240/1240**
   again, identical counts — the previously-recorded `NoManagerWorkflowTests`/Avalonia-parallel-test
   flake (Phase 6D/10F) was **not observed** in either run this session.
4. After adding the two Part M regression tests above: `dotnet build` — **PASS**, 0 warnings/errors;
   `dotnet test ... --filter "Category!=LiveSmoke"` — **PASS**, **1242/1242** (66 Domain + 351
   Application + 96 Documents + 317 Infrastructure + 412 Ui).
5. One further full-suite run (a 5th run overall, taken to double-check the fix) hit the **known prior
   unrelated flake** named in the Phase 11F task brief:
   `FinalizedPassdownProjectionTests.BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel`
   failed once, in `Carowinds.Passdown.Documents.Tests` (95/96 that run), with a small byte-offset
   difference consistent with the same PDF metadata/creation-timestamp race between two independent
   `Generate` calls documented in the Phase 11E closeout above — this is not a new symptom. Isolation
   verified directly: an immediate full-suite rerun passed **1242/1242** again with no failure, and
   running only `FinalizedPassdownProjectionTests` by itself three times in a row passed **8/8** every
   time — the failure reproduces only under this suite's parallel full-solution execution, never in
   isolation, matching the documented cause exactly. No file under `Carowinds.Passdown.Documents`
   (other than the two new, passing, Part M tests added to a different test class in the same project)
   was touched this phase, so this is recorded as the pre-existing, non-deterministic flake it already
   was — not a Phase 11 regression.

### Release rebuild (Parts U–Y)

Performed on this (non-Windows, macOS) dev host — no `pwsh` available, same limitation as every prior
Phase 7/9/10 rebuild:

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — PASS, 271 files, 134 MB after `.pdb` removal (`Publish-Windows.ps1`'s exact command, staging cleaned first).
2. Before staging, the freshly published `Carowinds.Passdown.Application.dll`/`.Infrastructure.dll`/`.Desktop.dll` were searched directly (`strings` over the actual built assemblies, not timestamps) for Phase 11A–11E-only symbols — see `docs/release-manifest.md` "How this artifact was produced" for the exact list (`OperationalNotesService`, `OperationalNotesViewModel` ×158, `DashboardOperationalNotesSummary`, `AssociateOperationalNoteSelection`, `ReplaceOperationalNoteWithCurrentAsync`, `OperationalNoteSourceComparison`, `Migration0010CreateOperationalNotesTable`) — all present, proving this publish is not a stale Phase 10F reuse.
3. Staged into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/` exactly per `Build-PortableRelease.ps1`'s logic: publish output, `tools/Install-ScheduledRefresh.ps1`, `tools/Uninstall-ScheduledRefresh.ps1`, both `.cmd` helpers, `README.txt` (updated this rebuild — see Part AB/AC below), `TESTER-GUIDE.txt` (updated this rebuild).
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `*playwright*`, `*.Tests.dll`, `.dylib`/`.so`, `.DS_Store`) — clean, both on the staged folder and again on a fresh extraction of the finished ZIP.
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`; `shasum -a 256` computed fresh (see `docs/release-manifest.md` for the exact value — it differs from the Phase 10F artifact's hash, confirming this is not a stale reuse).
6. Release content audit: 277 files, 280 zip entries (277 files + 3 directories: release root, `LatoFont\`, `tools\`), 61,385,910 bytes compressed, 139,878,942 bytes extracted. Executable, `appsettings.json`, all managed/native runtime DLLs, updated README/tester-guide, both `.cmd` helpers, and `tools/` all present; no `.pdb`, no Playwright, no test assemblies, no dev docs, no user data, no non-Windows binaries.
7. Independent verification: the ZIP was extracted fresh to a scratch directory outside `artifacts/`; `diff -rq` against the staging folder reported byte-for-byte identical content; `Carowinds.Passdown.Desktop.exe`'s SHA-256 inside the re-extracted ZIP was independently confirmed to match the staged copy.

Note: the two Part M regression tests were added to test-only code after the publish/package steps
above; since they touch no file under `src/`, the already-published/packaged artifact remains valid
and was not rebuilt a second time — the build/test re-run in "Final automated validation" step 4
confirms the solution as a whole (including the new tests) is still green.

### Tester guide / README updates (Part AB/AC)

Both `packaging/windows/TESTER-GUIDE.txt` and `packaging/windows/README-portable-release.md` predated
Phase 11 and described only three top-level sections (Dashboard/Associate Passdown/History). Updated
this rebuild:

- `TESTER-GUIDE.txt` renumbered to insert a new step 5 ("Operational Notes Manager": create/edit/
  filter/resolve a disposable note) and a new step 11 ("Include a persistent operational note in a
  passdown": available suggestion → Add → source edit without replace → confirm frozen copy → Replace
  with Current Version → Remove), plus updated wording in steps 3 and 18 to name all four top-level
  sections and the Dashboard's "Active Operational Notes" summary.
- `README-portable-release.md` gained an "Operational Notes" section describing the Notes Manager, the
  explicit per-date inclusion workflow, and the frozen/Replace-with-Current-Version behavior in the
  same plain-language style as the rest of the document.

Neither file was turned into a technical design document.

### Focused Windows smoke check (Part AE)

**NOT EXECUTED.** No Windows machine or VM was available in this session/environment — the same
limitation recorded for every prior Phase 7/9/10 rebuild performed from this dev host. The task brief's
Part AE checklist (extract → launch → confirm 4-section nav → create/edit a disposable note → filter →
select it into a passdown date → confirm frozen copy → edit source → confirm source-changed indicator
→ Replace with Current Version → remove → re-add → generate PDF → confirm Dashboard summary → confirm
History/diagnostics still work) is the recommended next manual step against this exact ZIP once a
Windows host is available; `packaging/windows/TESTER-GUIDE.txt` steps 5 and 11 cover the same ground
for a nontechnical tester.

### Windows finalization smoke (Part AF)

**NOT EXECUTED**, per the same no-Windows-host limitation. The automated equivalent (select → edit
source without replacing → finalize → assert old text; select → explicitly Replace → finalize → assert
new text) is now covered directly by the two Part M regression tests added this rebuild (see above).

### Windows scaling validation (Part AG)

**NOT EXECUTED**, per the same no-Windows-host limitation carried forward from Phase 10E/10F. No source
or layout change occurred that would newly affect this status beyond adding the Operational Notes
screens themselves, which have not been scaling-validated on Windows either.

### Phase 9/10 regression smoke (Part AH)

**NOT EXECUTED**, per the same no-Windows-host limitation. The full non-live automated suite (1242/1242,
including the pre-existing Phase 9/10 finalization/History/Dashboard tests) passes unmodified, and
Phase 11 touched no file under `Application/Finalization`, `Documents/`, or `Dashboard/` business logic
beyond the one new `IOperationalNotesService` read in `OperationsDashboardService`.

### Release confidence gaps (Part AI), carried forward honestly

| # | Item | Classification |
|---|---|---|
| 1 | Operational Notes pixel-level Windows visual validation | Not executed (raster renderer unavailable, unchanged since Phase 9E) |
| 2 | Windows 100%/125%/150% display-scaling validation | Not executed (Part AG) — no Windows host available |
| 3 | Focused Windows Operational Notes smoke test | Not executed (Part AE) — no Windows host available |
| 4 | Windows finalization smoke (select → edit source → finalize → old text; Replace → finalize → new text) | Automated equivalent added and passing this rebuild (Part L/M/N fix); live Windows walkthrough not executed |
| 5 | Phase 9/10 regression smoke on this rebuild | Not executed (Part AH) — automated coverage stands in |
| 6 | Automatic-refresh Task Scheduler live install/rebind on this rebuild | Not executed — static content coverage (`PortableSetupHelperTests`) stands in, unaffected by Phase 11 |
| 7 | Native x64 vs. ARM64-emulation Windows validation (carried from Phase 7E) | Non-blocking confidence gap |
| 8 | Self-contained runtime on a clean machine with no .NET pre-installed (carried from Phase 7E/7F) | Non-blocking confidence gap |
| 9 | Pre-existing `NoManagerWorkflowTests` Avalonia parallel-test flake (Phase 6D/10F) | Not observed in any full-suite run this session; carried forward as a known, non-blocking, non-deterministic flake |
| 10 | Pre-existing `FinalizedPassdownProjectionTests.BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel` PDF-timestamp-race flake (named in the Phase 11F task brief) | Observed once in 5 full-suite runs this session; isolation confirmed (passes 8/8 standalone, passes on immediate full-suite rerun); non-blocking, non-deterministic, unrelated to Phase 11 |
| 11 | Manager workflow, Alerts, Operations Calendar, reordering, text search, Reopen | Deferred by design — out of current release scope |

None of the above is presented as a false PASS.

### Phase 11 documentation closeout

`docs/phase11-operational-notes.md` gained a final "Phase 11F — audit and release closeout" section
(see that document) covering 11A–11F and explicitly listing reordering, search, Reopen, Alerts, an
Operations Calendar, and the Manager workflow as intentionally deferred — not implied to exist.

### Phase 11F rebuild verdict

| # | Item | Result |
|---|---|---|
| 1 | Phase 11 requirements audit | PASS |
| 2 | No-automatic-inclusion audit (Part B) | PASS — no violation found |
| 3 | Frozen-copy audit (Part C) | PASS — no violation found |
| 4 | Date-specific selection audit (Part D) | PASS — no cross-date leakage |
| 5 | Source-comparison audit (Part E) | PASS — informational-only confirmed |
| 6 | Filter audit (Part F) | PASS — deterministic, non-mutating |
| 7 | Carry-forward presentation audit (Part G) | PASS — display-only, no recurrence |
| 8 | Notes Manager / Dashboard integration audit (Part H/I) | PASS — no forbidden feature found |
| 9 | Associate inclusion / autosave-concurrency audit (Part J/K) | PASS — idempotent, no stale overwrite |
| 10 | PDF/finalization/History audit (Part L/M/N) | PASS AFTER FIX — test-coverage gap closed, no production defect |
| 11 | Version/compatibility audit (Part O) | PASS — schema 10 / 3 / 1 confirmed in code |
| 12 | Privacy/accessibility audit (Part P/Q) | PASS — no leakage found |
| 13 | Full automated validation | PASS, 1242/1242 (both runs after the fix), 0 warnings/errors |
| 14 | Release rebuild (publish + package) | PASS |
| 15 | Latest-code verification in artifact | PASS (byte-level `strings` symbol check against the freshly published assemblies) |
| 16 | Release content audit | PASS |
| 17 | New SHA-256 recorded | PASS — differs from the Phase 10F artifact; Phase 10F artifact explicitly marked superseded |
| 18 | `docs/release-manifest.md` updated | PASS |
| 19 | Tester guide / README updated for Operational Notes | PASS |
| 20 | Focused Windows Operational Notes smoke test | **NOT EXECUTED** — no Windows host available |
| 21 | Windows finalization smoke | Automated equivalent PASS; live Windows walkthrough **NOT EXECUTED** |
| 22 | Windows 100/125/150 scaling validation | **NOT EXECUTED** — no Windows host available |
| 23 | Phase 9/10 regression smoke on this rebuild | **NOT EXECUTED** — automated coverage stands in |
| 24 | Phase 11 documentation closeout | PASS |

### Phase 11F release readiness decision

**READY WITH ACCEPTED LIMITATIONS.**

Every Phase 11A–11E requirement was re-audited against current code and passes; the one gap found (a
missing direct end-to-end test for Part M's required finalization scenario) was a test-coverage gap,
not a functional defect, and was closed with two new regression tests, both passing. No accidental
automatic-inclusion path, silent frozen-copy resync, live-source finalization query, or PDF/diagnostics
content leak was found anywhere in the Phase 11A–11E code. The full non-live suite (1242/1242 on a
clean run, twice) and a clean build both pass; the release ZIP was rebuilt from the exact current
commit, independently verified to contain Phase 11 Operational Notes code via a byte-level `strings`
check against the freshly published assemblies, and byte-for-byte re-inspected after extraction. The
prior (Phase 10F) artifact is explicitly marked superseded. The only open gaps are Windows-host
execution (Operational Notes visual, display-scaling, focused smoke, and live finalization-walkthrough
checks) — none of which are new risks beyond what Phase 7E/9F/10F already carried for this
dev-host-only build process, and all of which are recorded above rather than glossed over. `0.1.0-dev`
remains an explicit pre-release development version, not a production 1.0 release.

## Phase 12F — Operations Calendar final audit and release closeout

Phase 12F closed out Phase 12A–12E (the Operations Calendar) with a full requirements/architecture
audit against the actual implementation (not only prior phase documentation), then rebuilt the Windows
portable release. Full detail on the Calendar feature itself lives in
`docs/phase12-operations-calendar.md`; this section records the audit method, results, and the release
rebuild this phase performed.

### Audit method

Every Calendar source file was read directly — `OperationsCalendarService`/`OperationsCalendarSnapshot`
(Application), `OperationsCalendarViewModel`/`OperationsCalendarDayViewModel`/the four presentation
types/`OperationsCalendarView.axaml` (Desktop), `MainViewModel`'s navigation wiring, the two new SQLite
range-query methods, and `PassdownHistoryViewModel.SelectByDateAsync` — cross-checked against every
Phase 12A–12E documentation claim in `docs/phase12-operations-calendar.md`, not merely trusted from that
document. No genuine defect was found; every audited behavior matched its documented design exactly,
including:

- **No network path**: `OperationsCalendarService`'s constructor depends only on
  `IAssociatePassdownService`, `IAssociateDraftRepository`, `IDatedResultRepository`,
  `IFinalizedPassdownRepository`, `IOperationalNotesService`, and `IClock` — no `IRefreshOrchestrator`,
  `HttpClient`, or browser-session dependency reachable from any constructor parameter, confirmed by
  reading the type directly (backing the existing
  `Constructor_HasNoRefreshOrchestratorOrRetrievalDependency` /
  `Constructor_HasNoRefreshOrPdfDependency` reflection tests).
- **No mutation**: every `OperationsCalendarViewModel` command (`SelectDateCommand`,
  `PreviousMonthCommand`/`NextMonthCommand`, `PreviousDayCommand`/`NextDayCommand`, `GoToTodayCommand`)
  only ever calls `IOperationsCalendarService.LoadAsync` — a pure read composed from
  `ListAsync`/`ListSavedDraftDatesAsync`/`GetByCategoryAndDateRangeAsync`/`GetByDateRangeAsync`, none of
  which write. `IFinalizedPassdownRepository.ListAsync`'s SQL (`SqliteFinalizedPassdownRepository.cs`)
  selects only `passdown_date, finalized_at_utc, origin, pdf_sha256, pdf_size_bytes` — never
  `pdf_bytes` or snapshot JSON — confirming Calendar can never materialize a historical PDF blob.
- **Bounded queries**: `GetByCategoryAndDateRangeAsync`/`GetByDateRangeAsync`
  (`SqliteDatedResultRepository.cs`/`SqliteAssociateDraftRepository.cs`) are each one parameterized
  `WHERE ... BETWEEN $start AND $end` SQL statement — never one query per day cell, for a 35- or
  42-cell grid alike.
- **Off-month leak**: `OperationsCalendarView.axaml`'s day-cell template wraps the operating-state/
  hours/passdown-status/notes block in one `IsVisible="{Binding IsInDisplayedMonth}"` — confirmed the
  12C fix is intact; an off-month cell still carries real underlying data in the snapshot (by design —
  Calendar never fabricates a cell) but the View never renders it.
- **Finalized-over-Draft precedence**: `OperationsCalendarPassdownPresentation.DeriveStatus` is the one
  place the precedence is decided (`hasFinalizedPassdown ? Finalized : hasAssociateDraft ?
  DraftInProgress : NoDraft`), called from `OperationsCalendarDayViewModel`'s constructor — confirmed no
  second precedence implementation exists anywhere in the Desktop layer.
- **Schema**: `Migration0010CreateOperationalNotesTable` remains the highest-numbered migration
  registered in `App.axaml.cs` — no `Migration0011` exists. `AssociateDraftPayload.CurrentSchemaVersion`
  = 3, `FinalizedPassdownSnapshot.CurrentSchemaVersion` = 1 — both confirmed directly in source, not
  merely carried forward from documentation.
- **No Manager/Alerts**: `AppSection` (`MainViewModel.cs`) still has exactly five values (`Dashboard`,
  `OperationsCalendar`, `AssociatePassdown`, `OperationalNotes`, `History`) — no `Manager`, no `Alerts`.

No fix was required this phase (unlike Phase 11F, which found and closed one test-coverage gap) — the
audit is a clean **PASS** across every Part A–AJ item in the Phase 12F task brief.

### Test suite stability

`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` was run five times this phase:
four runs passed 1395/1395 cleanly; one run hit the pre-existing, previously-documented (Phase 6D)
Avalonia headless cross-assembly parallelism flake —
`DashboardViewTests.DashboardView_LatestFinalizedHasNoPdf_ConstructsWithoutError` failed with
`System.InvalidOperationException: Stack empty.` inside
`Avalonia.PropertyStore.AvaloniaPropertyDictionaryPool.Get()` during `XamlIlPopulate`, the exact
documented signature. This is an **unrelated Phase 10 view** (`DashboardView`, not any Phase 12 file);
no Operations Calendar test has ever failed in any run this phase. Not a Phase 12 regression — recorded
honestly per the task brief's instruction not to call a known flake a regression without evidence.

### Release rebuild

No `pwsh` runtime is available on this dev host (same limitation as every prior rebuild) — the exact
`Publish-Windows.ps1`/`Build-PortableRelease.ps1` staging/zip logic was mirrored manually via shell
commands, identical to how Phases 7A–7F, 9F, 10F, and 11F each produced their own artifacts:

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — clean publish, 271 files after `.pdb` removal (134 MB), matching the Phase 7B–11F baseline exactly.
2. A `strings` byte-search of the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` confirmed 10 distinct Phase 12A–12E symbols present (see `docs/release-manifest.md` for the full list) — proving this publish is not a stale reuse of the Phase 11F output.
3. Staged into `artifacts/release-staging/Carowinds-Passdown-0.1.0-dev-win-x64/` — 277 files, matching the Phase 7C–11F layout exactly (publish output + `tools/` scripts + the two `.cmd` helpers + `README.txt` + `TESTER-GUIDE.txt`).
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`) — clean.
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Passdown-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. Extracted fresh to an isolated scratch directory; `diff -rq` against the staging folder confirmed byte-for-byte identical content (277 extracted files matching 277 staged files), and `Carowinds.Passdown.Desktop.dll`'s SHA-256 inside the extraction independently matched the staged copy.
8. After steps 1–7 first completed, `TESTER-GUIDE.txt`/`README-portable-release.md` were updated with the Operations Calendar section (see below) — steps 3/5/6/7 were then re-run to re-stage the two changed files, re-zip, re-hash, and re-verify the extraction, so the shipped artifact reflects the final documentation, not the intermediate build. The final SHA-256 is `dcceafee15be7f30ba410d3bb68032bd3eba057833f4a4c910fe5ecbbc4d4946`, 61,409,645 compressed bytes / 139,980,429 extracted bytes.

This supersedes the Phase 11F artifact (SHA-256
`96e6c3b48be265cac444eb96c1aba6ea6c7214b0561a97a05dd039a474f0767b`) — full detail, including the
functional delta and the complete chain of superseded prior artifacts, is in
`docs/release-manifest.md`.

### Windows validation — NOT EXECUTED

No Windows host was available this session, same limitation carried forward from every prior phase.
100%/125%/150% display-scaling validation, and the full manual click-through checklist (Part S of the
Phase 12F task brief), were **not executed**. Operations Calendar's own confidence comes from the
automated suite (construction-level real-XAML-loader checks for every representative state, plus
`Measure`/`Arrange` layout passes at 1280×860 and 960×640, unchanged since Phase 12B/12E and still
passing) — not from Windows execution. This is not a new risk class beyond what every Phase 7E/9F/10F/
11F rebuild already carried; it is recorded here rather than glossed over.

### Phase 12F verdict table

| # | Item | Result |
|---|---|---|
| 1 | Phase 12A–12E requirements audit (Parts A–AG) | PASS |
| 2 | No-network audit | PASS |
| 3 | No-mutation audit | PASS |
| 4 | Date semantics audit | PASS |
| 5 | Park hours/operating-state audit | PASS |
| 6 | Passdown lifecycle / Finalized-over-Draft precedence audit | PASS |
| 7 | Off-month leak audit | PASS |
| 8 | Operational Notes relevance/counts audit | PASS |
| 9 | Query/N+1 audit | PASS |
| 10 | Accessibility/keyboard audit | PASS |
| 11 | Schema/version confirmation (10 / 3 / 1, in code) | PASS |
| 12 | No Manager workflow / no Alerts | PASS |
| 13 | Full automated validation | PASS, 1395/1395 on 4 of 5 runs; 1 run hit the known unrelated Phase 6D flake |
| 14 | Build | PASS, 0 warnings, 0 errors |
| 15 | Release rebuild (publish + package) | PASS |
| 16 | Phase 12 symbol proof in freshly published assemblies | PASS |
| 17 | Staging-vs-ZIP integrity | PASS, byte-for-byte identical |
| 18 | New SHA-256 recorded, differs from Phase 11F artifact | PASS |
| 19 | `docs/release-manifest.md` updated | PASS |
| 20 | Tester guide / README updated for Operations Calendar | PASS |
| 21 | Windows 100/125/150% scaling validation | **NOT EXECUTED** — no Windows host available |
| 22 | Full manual Windows click-through checklist | **NOT EXECUTED** — no Windows host available |
| 23 | Phase 13 scope check (no rename, no startup/refresh/pricing/ERT/logo/Alerts work) | PASS — none found |

### Phase 12F release readiness decision

**READY WITH ACCEPTED LIMITATIONS.**

Every Phase 12A–12E requirement was re-audited directly against the current implementation and passes
cleanly — no genuine defect was found, so nothing needed fixing this phase. The full non-live suite
(1395/1395, four of five runs clean; the one flaky run reproduced the pre-existing, unrelated Phase 6D
Avalonia headless flake on a Phase 10 view, never a Phase 12 file) and a clean build both pass. The
release ZIP was rebuilt from the exact current commit, independently verified via a `strings` symbol
check to contain the actual Phase 12A–12E Operations Calendar code (not a stale reuse of the Phase 11F
artifact), and byte-for-byte re-inspected after a fresh extraction. The prior (Phase 11F) artifact is
explicitly marked superseded. The only open gaps are Windows-host execution (100/125/150% display
scaling and the full manual click-through checklist) — the same class of gap every Phase 7E/9F/10F/11F
rebuild has carried on this dev-host-only build process, not a new risk introduced by Phase 12. Phase 13
(app rename, refresh-policy overhaul, pricing/retrieval fixes, manual-hours/buyout redesign, ERT
simplification, logo, Alerts) was confirmed **not started** — `0.1.0-dev` remains an explicit pre-release
development version, not a production 1.0 release.

## Phase 13H — final Phase 13 integration audit and release closeout

Phase 13H closed out Phase 13A–13G (startup/refresh audit and hardening, `UserForced` catalog-refresh
coverage, commercial/pricing hardening, Operations Calendar/Deep Refresh corrections, typable Manual
Hours/Private Buyout entry, derived Early Ride Time, and the visible rename to "Carowinds Operations")
with a targeted integration audit against the actual current code (not only prior phase documentation),
then rebuilt the Windows portable release under the new `Carowinds-Operations-<version>-win-x64.zip`
naming convention. Full detail on each individual sub-phase lives in `docs/phase13-hardening.md`; this
section records the audit method, results, and the release rebuild this phase performed.

### Audit method

Rather than re-deriving every Phase 13A–13G finding from scratch (each sub-phase already performed its
own exhaustive, file:line-cited audit, and the cumulative test suite was already green at 1600/1600
going into this phase), Phase 13H sampled the highest-risk *cross-phase* interaction points — the
places a defect could exist only because two separately-built features interact, which no single
sub-phase's own audit would have caught:

- **Startup/refresh ordering** (`App.axaml.cs`, `StartupCatchUpRefresh.cs`): confirmed
  `desktop.MainWindow = mainWindow` is assigned before the fire-and-forget
  `_ = StartupCatchUpRefresh.RunAsync(...)` call, and `StartupCatchUpRefresh` still finalizes before
  refreshing, still guards with the shared `IScheduledRefreshLock`, and still swallows all failures
  without ever propagating to the UI — byte-identical to the Phase 13A/13B documentation.
- **Hours/ERT domain purity** (`ParkHoursCalculator.cs`, `EffectiveHours.cs`): read directly — confirmed
  `EffectiveHours`'s constructor takes only main-gate `opening`/`closing`/override-flag parameters (no
  waterpark parameter exists anywhere in the type), `NorthGateOpening = opening?.AddMinutes(-30)`
  null-propagates correctly for `Closed`/`NotCheckedYet`, and the buyout &gt; closed &gt; manual &gt;
  official &gt; not-checked precedence exactly matches spec §8.2.
- **Manual Hours/Buyout typed entry → ERT reactivity** and **custom product override → PDF →
  finalization → History immutability** — the two most complex cross-cutting paths in the whole Phase
  13 diff — were independently audited end-to-end by a dedicated read-only pass (file:line citations in
  `AssociateViewModel.cs`, `ParkHoursCalculator.cs`, `AssociateParkHoursComposer.cs`,
  `AssociateProduct.cs`, `AssociatePassdownDocumentModelBuilder.cs`,
  `FinalizedPassdownSnapshotBuilder.cs`). Both: **PASS**. ERT correctly and reactively recomputes from
  freshly typed/committed Manual Hours and Private Buyout values via one shared
  `ParkHoursCalculator.Calculate` call; an unparseable typed value is rejected by
  `CommitOperationalTimeText` before it ever reaches the committed field, so it can never corrupt
  `NorthGateOpening`; waterpark hours are carried on an entirely separate presentation path
  (`AssociateParkHoursView.WaterparkOpening/WaterparkClosing`) that never touches `Calculate` or
  `EffectiveHours`. The custom-price override (`AssociateProduct.EffectiveDisplayPrice =>
  DisplayPriceOverride ?? OfficialPriceAmount`) is the single value both `AssociatePassdownDocumentModelBuilder`
  and `FinalizedPassdownSnapshotBuilder` read from the one shared document-model build
  (`FinalizedPassdownSnapshotBuilder.BuildProjection` builds `AssociatePassdownDocumentModel` exactly
  once and derives both the PDF and the snapshot from that same instance); the frozen snapshot stores
  plain formatted strings/decimals (e.g. `FinalizedProductRowSnapshot`), never a live reference back to
  the mutable catalog row, so a later catalog change structurally cannot alter a frozen History record.
  One minor, non-blocking test-coverage observation: no existing test explicitly re-mutates a catalog
  value *after* finalization to prove immutability end-to-end (the guarantee is structural, from the
  plain-data snapshot shape, not from an explicit regression test) — noted for future coverage, not
  treated as a defect requiring a fix this phase (CLAUDE.md/brief: fix only genuine regressions, not
  every coverage gap).
- **Branding sweep**: `grep -rn "Carowinds Admissions Operations"` across `src/`/`tests/`/`packaging/`
  returned only the one test asserting its absence (`ProductBrandingTests.cs`); every remaining
  `"Carowinds Passdown"` hit in `src/`/`tests/`/`packaging/` was individually inspected and confirmed to
  be one of the four intentional legacy compatibility identifiers (AppData folder, database filename,
  Task Scheduler task name, OS-temp launcher subfolder) or a doc-comment referencing one of them — no
  stale-branding regression found. Window title, brand band, PDF header (`"CAROWINDS OPERATIONS"` +
  `"Associate Passdown"`), and the diagnostic summary heading (`"Carowinds Operations — Diagnostic
  Summary"`) were each read directly and confirmed correct. Confirmed no `LastVerified`/`Deep Refresh`
  diagnostic text exists anywhere in `AssociatePassdownPdfGenerator.cs`/
  `AssociatePassdownDocumentModelBuilder.cs` (zero grep hits) — the PDF cannot leak retrieval
  diagnostics.
- **Schema/scope confirmation**: `Migration0011CreateDeepRefreshStatusTable` (introduced Phase 13D)
  remains the highest-numbered migration; `AssociateDraftPayload.CurrentSchemaVersion` = 3,
  `FinalizedPassdownSnapshot.CurrentSchemaVersion` = 1 — both confirmed directly in source.
  `AppSection` (`MainViewModel.cs`) still has exactly five values (`Dashboard`, `OperationsCalendar`,
  `AssociatePassdown`, `OperationalNotes`, `History`) — no `Manager`, no `Alerts`, confirming Phase
  14/15 remain correctly un-started.

No genuine defect was found anywhere sampled — the audit is a clean **PASS**. No production `src/` code
was changed this phase.

### Test suite stability

`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` was run five times this phase: three
runs passed 1600/1600 cleanly; two runs each hit exactly one failure, both matching the pre-existing,
previously-documented (Phase 6D) Avalonia headless cross-assembly parallelism flake
(`System.InvalidOperationException: Stack empty.` inside
`Avalonia.PropertyStore.AvaloniaPropertyDictionaryPool.Get()` during `XamlIlPopulate`) — once in
`DashboardViewTests.DashboardView_EachParkStatus_ConstructsWithoutError(status: Closed)`, once in
`AssociateViewAccessibilityTests.AssociateView_WithPopulatedWorkspace_ConstructsAndExposesCriticalAutomationNames`.
Two different, unrelated View-construction tests each time, neither ever a Phase 13 file, both bearing
the exact documented signature. Not a regression; recorded honestly per the task brief's instruction not
to call a known flake a regression without evidence.

### Release ZIP naming (Part 17 of the brief)

Phase 13G's own task brief explicitly deferred the release ZIP naming update to 13H (changing it earlier
without also cutting a new manifest/rebuild would have left an inconsistent half-state). This phase
updated the two places that determine it: `packaging/windows/Build-PortableRelease.ps1`'s
`$releaseName` (`"Carowinds-Passdown-$version-win-x64"` → `"Carowinds-Operations-$version-win-x64"`) and
`packaging/windows/Publish-Windows.ps1`'s console prose ("Publishing Carowinds Passdown..." →
"Publishing Carowinds Operations..."), plus each script's `.SYNOPSIS` doc comment. Neither script's
executable-name check (`Carowinds.Passdown.Desktop.exe`), Task Scheduler task-name targeting, or
publish/staging logic changed. No test hardcodes the release-ZIP-naming string (confirmed by grep), so
no coupled test edit was needed.

### Release rebuild

No `pwsh` runtime is available on this dev host (same limitation as every prior rebuild) — the exact
`Publish-Windows.ps1`/`Build-PortableRelease.ps1` staging/zip logic was mirrored manually via shell
commands, identical to how Phases 7A–7F, 9F, 10F, 11F, and 12F each produced their own artifacts:

1. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64 -p:PlaywrightPlatform=win-x64` — clean publish, 271 files after `.pdb` removal (135 MB), matching the Phase 7B–13G baseline.
2. A `strings` byte-search of the freshly published `Carowinds.Passdown.Desktop.dll`/`.Application.dll` confirmed Phase 13 symbols present (`RunUserForcedRefreshAsync` ×4, `NorthGateOpening` ×4, `ManualHoursWarningText`/`TimeText`, `OperationsCalendarViewModel` ×51) and the embedded `AssemblyProductAttribute` reads `"Carowinds Operations"` — proving this publish is not a stale reuse of the Phase 12F output.
3. Staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/` — 277 files, the same Phase 7C–13G layout under the new folder name.
4. Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, Playwright, test assemblies, non-Windows native binaries, `.DS_Store`) — clean, using extension-anchored glob patterns after an initial naive substring check produced two false positives (`Tmds.DBus.Protocol.dll` matching `*.db*`, `System.Net.Sockets.dll` matching `*.so*` — both confirmed to be ordinary .NET framework DLLs on direct inspection, not forbidden content).
5. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`.
6. `shasum -a 256` the resulting ZIP.
7. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the staging folder reported byte-for-byte identical content (277 extracted files matching 277 staged files), and `Carowinds.Passdown.Desktop.exe`'s SHA-256 inside the extraction independently matched the staged copy.

This supersedes the Phase 12F artifact (SHA-256
`dcceafee15be7f30ba410d3bb68032bd3eba057833f4a4c910fe5ecbbc4d4946`) — full detail, including the exact
functional delta and the complete chain of superseded prior artifacts, is in `docs/release-manifest.md`.

### Windows validation — NOT EXECUTED

No Windows host or VM was available this session, the same limitation carried forward from every prior
phase. 100%/125%/150% display-scaling validation, live icon/taskbar rendering, real Task Scheduler
install/run/uninstall, and the full manual click-through checklist were all **not executed**. Confidence
instead comes from the automated suite (construction-level real-XAML-loader checks plus `Measure`/
`Arrange` layout passes at 1280×860/960×640, unchanged since Phase 12B/13D and still passing), the
existing `ScheduledTaskPlanTests`/`ScheduledTaskStatusProviderTests` static coverage (unchanged since
Phase 13G, since nothing in `ScheduledTaskPlan.cs`/`ScheduledTaskStatusProvider.cs` was touched this
phase), and the byte-level icon/Product-attribute verification described in "Release rebuild" above.
This is not a new risk class beyond what every Phase 7E/9F/10F/11F/12F/13G rebuild already carried; it
is recorded here rather than glossed over.

### Phase 13H verdict table

| # | Item | Result |
|---|---|---|
| 1 | Phase 13A–13G integration audit (startup/refresh, hours/ERT, Calendar, branding, schema/scope) | PASS |
| 2 | Startup non-blocking / no synchronous network audit | PASS |
| 3 | Check Online (Dashboard + Associate) result | PASS — unchanged since 13B, catalog+dated coverage confirmed in code |
| 4 | Manual/scheduled Deep Refresh result | PASS — unchanged since 13B/13D |
| 5 | Operations Calendar result | PASS — unchanged since 12F, no Phase 13 file touched Calendar geometry |
| 6 | Waterpark hours isolation from ERT | PASS — structurally confirmed, no waterpark parameter reaches `ParkHoursCalculator`/`EffectiveHours` |
| 7 | Manual Hours typed entry result | PASS — unchanged since 13E, reactivity into ERT independently re-verified this phase |
| 8 | Private Buyout result | PASS — unchanged since 13E/13F |
| 9 | ERT derivation result | PASS — unchanged since 13F, reactivity and non-fabrication independently re-verified this phase |
| 10 | Pricing/commercial coverage result | PASS — unchanged since 13C; known BAF-authentication limitation carried forward, no credential automation added |
| 11 | Custom product override → PDF → finalization → History | PASS — independently re-verified this phase end to end; one non-blocking test-coverage observation noted, not a defect |
| 12 | PDF result (branding, no diagnostics leakage, multi-page warning) | PASS |
| 13 | Multipage warning result | PASS — unchanged (`AssociatePassdownPreflight.cs`) |
| 14 | Finalization/History immutability result | PASS |
| 15 | Branding result | PASS — no stale "Carowinds Admissions Operations"/misclassified "Carowinds Passdown" hit found |
| 16 | Icon (byte-level, non-Windows) result | PASS — icon asset present, `ApplicationIcon` wired, embedded in built `.exe` |
| 17 | Icon Windows-rendered result | **NOT EXECUTED** — no Windows host available |
| 18 | AppData upgrade / legacy-data compatibility | PASS — structurally guarded by `Phase13GAppDataRenameCompatibilityTests`, unchanged this phase |
| 19 | Fresh-install result | PASS — unchanged startup/migration path, no Phase 13H code change |
| 20 | Task Scheduler result (static) | PASS — task name/description/idempotency unchanged; live install/run **NOT EXECUTED** |
| 21 | Windows 100% scaling | **NOT EXECUTED** — no Windows host available |
| 22 | Windows 125% scaling | **NOT EXECUTED** — no Windows host available |
| 23 | Windows 150% scaling | **NOT EXECUTED** — no Windows host available |
| 24 | Build result | PASS — 0 warnings, 0 errors |
| 25 | Test suite / final count | PASS — 1600/1600 (3 of 5 runs clean; 2 runs hit the pre-existing, unrelated Phase 6D Avalonia flake) |
| 26 | Schema confirmation (11 / 3 / 1, in code) | PASS |
| 27 | Release artifact filename | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| 28 | ZIP size | 62,586,155 bytes (59.7 MiB) |
| 29 | Extracted size / file count | 141,557,778 bytes (135.0 MiB) / 277 files (280 entries) |
| 30 | SHA-256 | `dde45055424d140fe0a16eae079f24cddfbc9a699c7a06dd7e50f0c65f4a8f0d` |
| 31 | Release manifest/docs update | PASS — `docs/release-manifest.md` and this section |
| 32 | Defects found/fixed | None found; zero production `src/` changes this phase |
| 33 | Accepted limitations | Windows-host execution gaps (icon rendering, scaling, live Task Scheduler, full click-through); BAF passholder-authentication pricing gap (13C, unchanged) |
| 34 | Phase 14 HOLD confirmation | PASS — no Alerts/Data Confidence/notifications/monitoring code found |
| 35 | Phase 15 NOT STARTED confirmation | PASS — `AppSection` still has exactly 5 values, no Manager |

### Phase 13H release readiness decision

**READY WITH ACCEPTED LIMITATIONS.**

Every Phase 13A–13G requirement was re-audited against current code at the cross-phase interaction
points most likely to hide a defect no single sub-phase's own isolated audit would catch — none was
found. The full non-live suite (1600/1600 on 3 of 5 clean runs; the other 2 each hit the same
pre-existing, unrelated Phase 6D Avalonia headless flake on two different, unrelated View-construction
tests) and a clean build both pass. The release ZIP was rebuilt from the exact current commit under the
new `Carowinds-Operations-<version>-win-x64.zip` naming convention, independently verified via a
`strings` symbol/Product-attribute check to contain the actual Phase 13A–13G code (not a stale reuse of
the Phase 12F artifact), and byte-for-byte re-inspected after a fresh extraction. The prior (Phase 12F)
artifact is explicitly marked superseded. The only open gaps are Windows-host execution — the same class
of gap every Phase 7E/9F/10F/11F/12F rebuild has carried on this dev-host-only build process, not a new
risk introduced by Phase 13 — and the already-documented, unimplementable-without-credential-automation
BAF passholder-pricing gap from Phase 13C. Phase 14 (Alerts/Data Confidence/notifications/monitoring)
and Phase 15 are both confirmed **not started**. `0.1.0-dev` remains an explicit pre-release development
version, not a production 1.0 release.

## Phase 15F — single-file publish investigation and final release decision

Phase 15F evaluated a true single-file win-x64 publish as a possible primary release form (the
"Single-file / trimming / AOT" table above records Phase 7A's original rejection; the reasoning there
is unchanged as *general* guidance — this section documents a specific, deliberate re-evaluation, not
a reversal of that reasoning by default). Full investigation narrative, including the exact two
physical-file dependencies found and fixed, is in `docs/phase13-hardening.md`'s Phase 15F section; this
section records the resulting packaging facts and the final primary/fallback decision.

### What changed to make single-file viable

Two things this app's own code (not the SDK's single-file mechanism) previously depended on a
physical file living next to the running executable, both fixed this phase with no behavior change
(verified by the existing, unchanged-assertion test suite):

- **`appsettings.json`**: now an `EmbeddedResource` in `Carowinds.Passdown.Desktop.csproj`, read via
  `Assembly.GetManifestResourceStream` + `ConfigurationBuilder.AddJsonStream` instead of
  `AddJsonFile(..., optional: false)` against `AppContext.BaseDirectory`. The prior approach would have
  hard-crashed at startup under a single-file publish that force-extracts content
  (`IncludeAllContentForSelfExtract=true`, itself avoided — see below) — extraction lands in a
  `%TEMP%/.net/...` cache directory, not the `.exe`'s own directory.
- **QuestPDF's "Lato" fallback font**: previously auto-discovered by QuestPDF itself via a physical
  directory scan for a `LatoFont/` folder near the executable (confirmed by inspecting `QuestPDF.dll`
  directly — a method named `ContainsLatoFontFolder` exists and is called from
  `RegisterLibraryDefaultFonts`). `Carowinds.Passdown.Documents.csproj` now embeds the same 18 `.ttf`
  files QuestPDF itself ships (referenced from the NuGet package cache, no duplicate copy committed to
  this repo) and `AssociatePassdownPdfGenerator`'s static constructor registers them explicitly via
  QuestPDF's own documented `FontManager.RegisterFont(Stream)` API — same bytes, same resulting font
  selection, no disk dependency. The now-redundant loose `.ttf` copy QuestPDF's own package targets
  would otherwise still add to the publish output is filtered out via a `ResolvedFileToPublish`
  target in `Carowinds.Passdown.Desktop.csproj` (the actually-published project — a same-project
  removal in `Documents.csproj` does not reach items injected this late by a referenced project's
  package targets, confirmed empirically). `LatoFont/OFL.txt` — the license notice, not font content —
  is deliberately still shipped as a loose file in both release forms.

**`IncludeAllContentForSelfExtract` was evaluated and rejected**, not merely left unset: Microsoft's
own documentation describes it as "not recommended: it's a .NET Core 3.1 compatibility mode and might
be removed in a future release," and it would have papered over exactly the `appsettings.json` bug
above rather than fixing it (both files would extract to the same `%TEMP%` cache, still separate from
where `AppContext.BaseDirectory` looks). `IncludeNativeLibrariesForSelfExtract=true` — the property
Microsoft's docs actually recommend for this exact "one output file" goal — is used instead, folding
SkiaSharp/HarfBuzzSharp/e_sqlite3/qpdf/ANGLE into the single file for extraction to the runtime's own
temp cache at first launch (a mechanism entirely internal to the .NET runtime's native-library loader,
not something this app's own code depends on a specific directory for).

### Resulting single-file publish command

```bash
dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:PublishTrimmed=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PlaywrightPlatform=win-x64 \
    -o artifacts/publish/win-x64-singlefile
```

Deliberately **not** baked into `Carowinds.Passdown.Desktop.csproj` as an unconditional property (unlike
`PublishReadyToRun`, which has zero effect without a RuntimeIdentifier and so is safe to set
unconditionally) — `PublishSingleFile` changes what a plain `dotnet publish -r win-x64` produces, and
this phase's decision (below) keeps the multi-file form as the default/primary. A future phase that
promotes single-file to primary can flip this the same way `PublishReadyToRun` was added in Phase 15D.

Publish output after the existing `.pdb`-removal convention: **two files** —
`Carowinds.Passdown.Desktop.exe` (176 MiB) and `LatoFont/OFL.txt` (a license notice, ~1.6 KB) — down
from a ~270-file multi-file directory. `appsettings.json` and every loose `.ttf` file are confirmed
absent. This was inspected directly (`find`, `file`, byte-search for expected symbols in the bundle),
not assumed from a successful publish exit code.

### Primary-release decision: multi-file ZIP remains primary

Per this repo's own explicit decision rule (docs/phase13-hardening.md Phase 15F §15): single-file
publish is genuinely, verifiably improved this phase — both physical-file blockers this investigation
found are fixed, not merely worked around — but **no Windows host was available this session** to
actually launch either release form, the same limitation every rebuild since Phase 7A has carried. A
successful publish plus a clean file-list inspection is real evidence the packaging is now *correct*;
it is not evidence the packaged app *launches* on Windows. The single-file candidate is built, hashed,
and recorded in `docs/release-manifest.md` as an **experimental / deferred** release form — a strong
candidate for primary status once a future phase can validate an actual Windows launch, not a
regression from where Phase 7A left single-file.

**PRIMARY**: `Carowinds-Operations-0.1.0-dev-win-x64.zip` (multi-file, self-contained, R2R,
unchanged delivery model — extract and run, no installer).
**EXPERIMENTAL / DEFERRED**: `Carowinds-Operations-0.1.0-dev-win-x64.exe` (single-file, R2R,
self-contained) — see `docs/release-manifest.md` for its hash/size and the exact validation gate that
remains before it could become primary.

### Task Scheduler / automatic refresh with a single-file artifact

`ScheduledTaskPlan.Build`/`Install-ScheduledRefresh.ps1` already accept an arbitrary absolute executable
path and filename (re-confirmed by reading both — the PowerShell script only emits a non-blocking
warning if the name doesn't match `Carowinds.Passdown.Desktop.exe`; the C# plan type never inspects the
filename at all) — no code change was needed for a version-named single-file executable to work with
the existing scheduled-refresh infrastructure. A true single EXE cannot carry the `.cmd` helpers or
`tools\*.ps1` scripts next to itself the way the ZIP does, so automatic-refresh setup remains a
ZIP-only convenience for now: a single-file user who wants it runs
`tools\Install-ScheduledRefresh.ps1 -ExecutablePath <path to the saved single EXE>` from an extracted
ZIP (no new script or in-app feature was built for this, per the brief's explicit "no new feature work"
scope for 15F).

## Phase 16G — Personal Sync release closure

Full narrative in `docs/personal-sync-architecture.md`'s "Phase 16G — Final Real-World Validation and
Release Closure" section; exact artifact hashes in `docs/release-manifest.md`'s Phase 16G rebuild entry.
Summary for this document's own release-strategy lens:

- **Packaging strategy unchanged.** Multi-file ZIP remains primary, single-file EXE remains
  experimental/deferred — no new Windows-launch evidence was gathered this phase (no Windows host was
  available in this session), so there was nothing to weigh against the Phase 15F decision. Neither the
  publish flags, the staging layout, nor `Publish-Windows.ps1`/`Build-PortableRelease.ps1` themselves
  changed.
- **Both release forms rebuilt** against the post-Phase-16F working tree (Personal Sync feature complete)
  and re-hashed; structural validation (publish content audit, forbidden-pattern check, ZIP round-trip
  byte-identity) passed for both, same as every prior rebuild.
- **Real Windows/OneDrive-client validation of either release form — including the single-file promotion
  gate itself — remains NOT EXECUTED**, carried forward from Phase 7E/15F, now compounded by Personal
  Sync's own real-client validation being entirely outstanding too. §16G.7 of
  `docs/personal-sync-architecture.md` is the concrete procedure for closing this on real hardware.
- No packaging-strategy decision in this document changed as a result of this phase — this is a rebuild
  and re-validation entry, not a strategy revision.

## Phase 17C — release rebuild for Associate PDF templates and UI density pass

Rebuilt from commit `0c2e4fa` ("Phase 17C: Associate UI density pass + Classic/Compact/Modern PDF
templates") to package the Phase 17A/17B/17C work. Exact hashes/sizes in `docs/release-manifest.md`'s
Phase 17C rebuild entry.

- **Packaging strategy unchanged.** Same publish flags, same staging layout, same
  `Publish-Windows.ps1`/`Build-PortableRelease.ps1` logic (run manually via shell, no `pwsh` available
  on this dev host, same limitation as every prior rebuild). Multi-file ZIP remains primary, single-file
  EXE remains experimental/deferred — no new Windows-launch evidence was gathered this phase.
- Deterministic test baseline confirmed at 2110/2110 via the same isolated-partition approach documented
  in the Phase 15F/16G entries (the pre-existing Phase 6D Avalonia headless flake reproduced in the same
  38 real-`Window` tests when run in the same process as the rest of the suite; each partition is clean
  alone).
- Real Windows/OneDrive-client validation remains **NOT EXECUTED**, carried forward unchanged from every
  prior rebuild.

## Phase 18B — release content validation (PKG-002) and US presentation culture

**Packaging validation.** `Build-PortableRelease.ps1`'s small forbidden-pattern list is replaced by
`packaging/windows/Test-ReleasePackage.ps1`, driven by `packaging/windows/release-content-rules.json`.
It runs twice: on the staging folder (no ZIP is created on failure) and on the finished ZIP (the ZIP
is deleted on failure). It can also be run by hand: `Test-ReleasePackage.ps1 -Path <folder-or-zip>`.

- **Required:** the desktop exe/dll, runtimeconfig/deps, the four app assemblies, the self-contained
  runtime (`hostfxr`, `hostpolicy`, `coreclr`, `System.Private.CoreLib`), win-x64 natives
  (`e_sqlite3`, `libSkiaSharp`, `libHarfBuzzSharp`, `av_libglesv2`, `QuestPdfSkia`), both scheduler
  `.cmd` helpers and `tools/*.ps1`, `README.txt`, `TESTER-GUIDE.txt`, and `LatoFont/OFL.txt` (the only
  license file a shipped resource currently requires to travel with it).
- **Forbidden:** `*.pdb`; source/project files; test assemblies and test-runner/headless packages;
  `fixtures`/`TestData`/`obj`/`src`/`docs`/… directories; `*.db`/WAL/SHM/journal; `*.log` and `logs/`;
  any `*.zip` (diagnostics); sync data (`Carowinds Operations Sync/`, `manifest.json`, `*.tmp-*`,
  `sync-config.json`, `device-id.txt`); `*.pdf` (generated previews); Ghostscript binaries/`*.ps`;
  `.DS_Store`/`._*`/`Thumbs.db`/`__MACOSX`; `*.so`/`*.dylib`/`*.a` and any `runtimes/<rid>` other than
  win-x64; `.git*`, `.env`, keys/certificates, `secrets.json`, `appsettings.*.json`, `CLAUDE.md`,
  `AGENT_BUILD_SPEC.md`.
- **Developer-path leak (PKG-001 regression):** `/Users/`, `/home/`, `C:\Users\` are searched (UTF-8
  and UTF-16) in our own `Carowinds.Passdown.*` binaries and every shipped text file; the checkout path
  and the packaging user's name are searched in **every** file. Third-party binaries are deliberately
  not searched for the generic tokens — `SQLitePCLRaw.*` and `MicroCom.Runtime` legitimately embed
  their vendors' `/Users/…`/`/home/…` build paths, which would otherwise be false positives.

The same rules are exercised on every host by `ReleasePackageValidationTests` (a C# mirror of the
script, reading the same JSON): every forbidden category, the third-party false-positive case, ZIP
handling, and — in Release builds — a string scan of our own built assemblies for the developer and
checkout paths. A real ZIP is validated with `CAROWINDS_RELEASE_ZIP=<zip> dotnet test … --filter
RealReleaseZip` (opt-in so the normal build never depends on an artifact). Run against the existing
pre-18A ZIP it reports exactly the known PKG-001 leak (6 own assemblies containing `/Users/…`) and
nothing else, which confirms the required-file list matches real publish output.

**Not executed:** `Test-ReleasePackage.ps1` itself has not run — there is no `pwsh` on this
development host (same limitation as every prior rebuild). It must run, and pass, on the next release
rebuild on Windows. **Open item (PKG-003):** the release has no consolidated third-party notices file
for its MIT/BSD/Apache-licensed dependencies (Avalonia, SkiaSharp, HarfBuzzSharp, SQLitePCLRaw, .NET,
QuestPDF); adding one is a licensing/product decision, not done here.

**Presentation culture.** `Program.Main` now calls `PresentationCulture.ApplyAsProcessDefault()` (en-US)
before anything else, so framework controls (date pickers, XAML formats) match the app's own explicit
en-US formatting. Persistence, sync, hashing, and parsing were already explicitly invariant; the Park
Hours request URL and generated file names were additionally made explicitly invariant (a non-Gregorian
default calendar such as th-TH would otherwise have produced a Buddhist-era year).

The release ZIP/EXE were **not** rebuilt in Phase 18B.


## Phase 18C — release candidate build with the real packaging scripts

- First release produced by `Build-PortableRelease.ps1` itself (PowerShell 7.6.6, installed for the
  build as a local `dotnet tool PowerShell` in a scratch folder — nothing installed system-wide), so
  `Test-ReleasePackage.ps1` ran for real on the staging folder and on the ZIP. It caught PKG-004 (our
  checkout path in `Carowinds.Passdown.Desktop.dll`), fixed in `5c0ddf2`. Still not run on Windows.
- The developer build scripts need PowerShell 7 (`pwsh`): they use three-argument `Join-Path`, despite
  `#Requires -Version 5.1`. User-machine scripts (`tools\*.ps1`) are Windows PowerShell 5.1-compatible.
- The release now ships `THIRD-PARTY-NOTICES.txt` and `licenses\` (PKG-003). To refresh them after a
  dependency change, copy the new packages' LICENSE/NOTICE files into `packaging/windows/licenses/`,
  update the index and `requiredFiles`; `RepositoryNoticeSources_MatchTheRequiredSet_AndAreComplete`
  fails until the two agree.
- The single-file EXE carries no notices when distributed alone; it stays experimental.
- Details and hashes: `docs/release-manifest.md` "Phase 18C".
