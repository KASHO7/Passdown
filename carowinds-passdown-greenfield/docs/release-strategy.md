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

## What's deferred (not yet done)

- **7C**: any installer, if one is ever decided to be needed (spec currently only asks for a
  portable ZIP, not an installer).
- **7D**: legacy-machine file-collision verification against a real legacy install (Phase 7A's
  review found no evidence of collision but had no legacy directory available to diff against
  directly).
- **7E**: actual Windows-host execution — launching the published `.exe`, running
  `--scheduled-refresh` against it, and validating `Publish-Windows.ps1`/`Build-PortableRelease.ps1`
  under a real PowerShell host. Nothing in Phase 7A or 7B claims Windows execution was tested; only
  `dotnet publish`/packaging output was inspected from this (non-Windows) dev host.
- **7F**: sanitized diagnostic export, tester instructions for a nontechnical coworker beyond the
  short `README.txt` already shipped in the ZIP (spec §19).
