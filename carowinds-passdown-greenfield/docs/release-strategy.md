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

