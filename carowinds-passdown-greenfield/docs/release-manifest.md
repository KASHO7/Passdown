# Release manifest — Carowinds Operations 0.1.0-dev (win-x64)

Rebuilt for Phase 15F (final production-readiness audit and release closeout for Phase 15) after the
full Phase 15A–15E work landed — see `docs/release-strategy.md` "Phase 15F — single-file publish
investigation and final release decision" and `docs/phase13-hardening.md`'s Phase 15F section for the
full narrative. This is the single source of truth for exactly what was released; `docs/release-strategy.md`
has the full narrative history (Phases 7A–7F, the Post-Phase-7F rebuild, the Post-Phase-8 rebuild, the
Phase 9F/10F/11F/12F/13H rebuilds, and this rebuild).

**This rebuild introduces a second release form for the first time**: alongside the proven multi-file
portable ZIP (unchanged delivery model, now PRIMARY), a genuine single-file win-x64 EXE was built,
inspected, and hashed as an EXPERIMENTAL/DEFERRED candidate — see "Single-file release (experimental)"
below.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (explicit pre-release development version — unchanged this phase) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | working tree at the time of this rebuild — see "Git checkpoint" in `docs/phase13-hardening.md`'s Phase 15F section for the exact commit hash once committed |
| Current schema version | 11 (unchanged since Phase 13D) |
| AssociateDraftPayload schema version | 3 (unchanged since Phase 11D) |
| Snapshot schema version | 1 (unchanged since Phase 1) |
| Non-live automated test count | **1674/1674** (1673 baseline + 1 new Phase 15F regression test — see "Test suite stability" below for how this was confirmed given this session's high headless-flake occurrence rate) |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| ReadyToRun | Enabled for both release forms (unchanged since Phase 15D for the multi-file form; newly combined with single-file this phase) |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available this session — the same limitation recorded in every prior rebuild since Phase 7A. 100%/125%/150% scaling, live icon/taskbar rendering, real Task Scheduler install/run, SmartScreen/Defender first-launch behavior, and actual process-launch-to-visible-window timing are all **NOT EXECUTED** for either release form. |
| Known limitations | Ordinary Bring-A-Friend/Duo pricing requires no login (confirmed, unchanged since Phase 15A) but this phase's live spot check found and fixed a seasonal-package-naming gap (see "BAF live spot check" below). Windows-host execution gaps (icon rendering, scaling, live Task Scheduler, actual launch timing, SmartScreen/Defender observation) carried forward for both release forms. Manager workflow, Alerts, and any Phase 14/15+ feature remain out of scope and were not started. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `10b5453f86a3d03300da0f15cf4169e25f405905e076c904184b3af8201f96a3` |
| Compressed size | 70,235,012 bytes (67.0 MiB) |
| Extracted (staged) size | 159,764,130 bytes (152.4 MiB) |
| File count | 258 files (261 zip entries: 258 files + 3 directory entries — release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained — see "Branding vs. internal identity" below) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (unchanged since Phase 7C) |

File count/size differ from the Phase 13H artifact (277 files, 62.6 MB compressed) for two reasons,
both explained fully in `docs/release-strategy.md`'s Phase 15F section: (1) `appsettings.json` and all
18 `LatoFont/*.ttf` files are no longer shipped as loose companion files — both are now embedded
resources compiled into the DLLs that already ship (net file-count *decrease*); (2) R2R-compiled
assemblies are larger per file (net byte-size *increase*, a Phase 15D effect already documented, not
new this phase). Functional content is unchanged: same executable, same native runtime DLLs, same
`Enable`/`Disable Automatic Refresh.cmd`, same `tools\`, same README/tester guide.

## Single-file release (experimental / deferred)

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `6c8085e0b23e83b7d14b7acbba8ff9373d2ea2490e72b8670aab0c72c3d8b01f` |
| Size | 184,374,074 bytes (175.8 MiB) |
| File count | **1** (the executable itself is the only artifact users are asked to keep — see note below) |
| Publish flags | `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` |
| Delivery | Copy the one `.exe`, double-click it — no extraction, no companion files required to launch |
| Automatic refresh | Not available out of the box (a true single EXE cannot carry the `.cmd` helpers next to itself) — use the ZIP's `tools\Install-ScheduledRefresh.ps1 -ExecutablePath <path to this exe>` if wanted; see `docs/release-strategy.md` |
| **Status** | **EXPERIMENTAL / DEFERRED — not primary.** Publish-and-inspect verified clean (see below); actually launching this exe was **NOT EXECUTED** (no Windows host available). Do not distribute as the sole release form until a future phase validates a real Windows launch. |

Verified this phase, by direct inspection (not merely a successful `dotnet publish` exit code):
publish output contains exactly two files before the standard `.pdb`-removal step
(`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`, the font's license notice — a redistribution
attribution file, not a functional dependency of the running app); `file` confirms a valid `PE32+
executable (GUI) x86-64, for MS Windows`; a UTF-16LE byte-search of the sibling multi-file build's
`Carowinds.Passdown.Infrastructure.dll` (same source commit) for this phase's own `BAFWizardDailyTickets`/
`Bring-A-Friend Ticket` strings returns 2 hits each, and `Carowinds.Passdown.Desktop.dll`'s
`AssemblyProductAttribute` reads `Carowinds Operations` — confirming this is not a stale reuse of an
earlier build. `appsettings.json` and all 18 loose `LatoFont/*.ttf` files are confirmed absent from the
single-file output (both were present as loose companions before this phase's fixes — see
`docs/phase13-hardening.md` Phase 15F §3–5 for the full investigation of why, and what was changed).
The 176 MiB size (vs. the ZIP's 152 MiB extracted) reflects the R2R native code and every dependency
folded into one file rather than left as separate DLLs on disk — expected, not a defect.

## Branding vs. internal identity

Unchanged since Phase 13G/13H — every user-visible surface reads "Carowinds Operations"; four internal
compatibility identifiers are intentionally retained across both release forms:

- AppData folder: `%LOCALAPPDATA%\Carowinds Passdown`
- Database filename: `carowinds-passdown.db`
- Internal executable/assembly identity: `Carowinds.Passdown.Desktop` (the multi-file ZIP's `.exe` is
  literally named this; the single-file EXE's *distributed filename* is
  `Carowinds-Operations-0.1.0-dev-win-x64.exe`, but its internal assembly/product metadata is
  unchanged — no project rename was performed, per CLAUDE.md's "greenfield rule" scope)
- Task Scheduler task name: `"Carowinds Passdown Daily Refresh"`

## BAF live spot check (Phase 15F)

Network access was available this session (unlike prior non-Windows-host rebuilds), so this phase's
required limited live validation (spec Part) actually reached the official anonymous
`getkeywordcalendar` endpoint rather than being not-executed. Result: the underlying anonymous,
no-login Bring-A-Friend dated pricing discovery from Phase 15A **still works** — but the live check
surfaced a real, currently-observable gap Phase 15A's own (narrower) investigation never exercised: the
official site now labels the same package `"Fall Bring-A-Friend Ticket"` for dates outside the
immediate near-term window (confirmed live for 2026-09-12 and 2026-10-15; the plain `"Bring-A-Friend
Ticket"` name still applies for near-term dates like 2026-08-29), and this repo's exact-string-match
code would have silently reported those dates as unavailable. **Fixed** this phase — see
`docs/phase13-hardening.md` Phase 15F §6 for the full finding and fix, and
`DatedBringAFriendSourceTests.RetrieveAsync_SeasonallyPrefixedPackageName_StillMatchesAndNormalizesTheDisplayName`
for the regression coverage (built from a real, unmodified live capture, not a hand-authored fixture).

## Test suite stability (Phase 15F)

`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` was run **9 times** this phase.
**Zero of nine were clean** in this sandboxed session — every run hit between 9 and 21 failures, all
sharing the exact pre-existing Phase 6D Avalonia headless cross-thread signature
(`System.InvalidOperationException: The calling thread cannot access this object because a different
thread owns it.`), all confined to the three real-`Window`-constructing test classes
(`OperationsCalendarViewTests`, `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests` — 38
tests). This is a materially higher occurrence rate than prior phases recorded (2–3 of 5 clean), most
plausibly explained by this session's own sandboxed CPU/thread-scheduling characteristics rather than
anything this phase's changes touch (none of them modify test infrastructure, Avalonia wiring, or
anything the 38 affected tests exercise).

Per the brief's explicit "confirm failing tests pass independently" instruction, the suite was
partitioned to get a genuine, non-fabricated clean signal: the 576 non-`Window`-constructing
`Ui.Tests` pass 100% clean every time run alone; the 38 real-`Window` tests pass 100% clean every time
run alone, isolated from the other 576. The failure only manifests when both groups share one test-host
process — a test-harness scheduling interaction, not a defect in any test or in production code. Total
across every test in the repository, confirmed clean via this partition: **1674/1674** (105 Domain +
471 Application + 114 Documents + 370 Infrastructure + 576 + 38 Ui.Tests). No production-code fix was
attempted for the flake itself, per the brief's explicit "only attempt a fix if a clearly safe solution
is now evident" instruction and this codebase's established precedent since Phase 6D.

## How these artifacts were produced

Built manually on this (non-Windows, macOS) dev host — no `pwsh` runtime is available (same limitation
recorded in every prior rebuild since Phase 7A):

1. Full clean (`rm -rf` every `src/**/bin`, `src/**/obj`) to guarantee no stale build state.
2. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64` — the multi-file (ZIP) form.
3. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64-singlefile` — the single-file (experimental) form.
4. `.pdb` files removed from both outputs (unchanged convention since Phase 7A).
5. Multi-file output staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/` (publish output + `tools/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1` + both `.cmd` helpers + `README.txt` + `TESTER-GUIDE.txt` — the same layout Phase 7C–13H established); forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `.DS_Store`) — clean.
6. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`; `shasum -a 256` the result.
7. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the staging folder reported byte-for-byte identical content, and the `.exe`'s SHA-256 inside the extraction independently matched the staged copy.
8. Single-file output copied directly to `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` (no staging/zip step — it is already the one distributable file); `shasum -a 256`'d directly.
9. A UTF-16LE byte-search of both builds' assemblies for phase-specific symbols (`BAFWizardDailyTickets`, `Bring-A-Friend Ticket`, `Carowinds Operations` product attribute) confirmed neither is a stale reuse of an earlier commit's output.

This supersedes the Phase 13H artifact (SHA-256
`dde45055424d140fe0a16eae079f24cddfbc9a699c7a06dd7e50f0c65f4a8f0d`, 277 files / 280 entries,
62,586,155 compressed / 141,557,778 extracted bytes) as the primary multi-file release — the functional
delta between the two is all of Phase 15A–15F: product-section grouping fix + anonymous ordinary
Bring-A-Friend dated pricing (15A), the full-width Operations Calendar redesign (15B), cross-screen
workflow/state consistency fixes (15C), lifecycle/resilience hardening including the shutdown-autosave
fix and `PublishReadyToRun` (15D), Windows/UI production polish (15E), and this phase's single-file
investigation plus the BAF seasonal-naming fix (15F). Schema is unchanged (11 / 3 / 1) — no migration
was added since Phase 13D. The Phase 13H artifact, and every artifact it itself superseded (Phase 12F,
11F, 10F, 9F, Post-Phase-8, Post-Phase-7F, 7F, and the original Phase 7B/7C/7D/7E artifact — hashes
recorded in this file's prior revision, retrievable via `git log -p -- docs/release-manifest.md`),
remain further superseded, retained only as historical validation information.

---

# Release manifest — Phase 16G rebuild (final Personal Sync validation/release closure)

Rebuilt for Phase 16G (real-world validation and release closure for Personal Sync — see
`docs/personal-sync-architecture.md`'s "Phase 16G — Final Real-World Validation and Release Closure"
section for the full narrative, including the explicit environment constraint this phase operated
under). This supersedes the Phase 15F artifacts above as the current release; the functional delta is
all of Phase 16A–16G: the full Personal Sync feature (OneDrive-folder-based sync of Associate drafts,
Operational Notes, mutable conflict detection/resolution, tombstones, finalized History, byte-preserved
frozen PDFs, and automatic startup/debounced/periodic sync with retry/backoff and status UX), plus this
phase's one new automated test closing the last documented Phase 16F test gap. No product feature
outside Personal Sync changed since Phase 15F.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged — still an explicit pre-release development version) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | working tree at `c4d3c0d` (HEAD, "Phase 16F: automatic Personal Sync triggering, retry/backoff, and status UX") plus this phase's own uncommitted change (one new test file + these three doc updates) at the time this manifest entry was written — see the Phase 16G git checkpoint (`docs/personal-sync-architecture.md`) for the exact commit hash once created |
| DB schema version | 13 (unchanged since Phase 16D) |
| AssociateDraftPayload schema version | 3 (unchanged since Phase 11D) |
| Snapshot schema version | 1 (unchanged since Phase 1) |
| Non-live automated test count | **1940/1940** (1938 Phase 16F baseline + 2 new `PersonalSyncCoordinatorEndToEndTests`, closing the §16F.20 combined-coordinator-plus-two-device-harness gap) |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| ReadyToRun | Enabled for both release forms (unchanged since Phase 15D/15F) |
| Real Windows/OneDrive validation | **NOT EXECUTED.** No Windows host, VM, or OneDrive account was reachable from this session (macOS/Darwin dev host — same limitation as every prior phase, but this time inside the phase whose purpose is exactly this validation). Every item requiring a real running OneDrive desktop client, two physical/virtual Windows devices, the native Windows folder picker, real DPI scaling, or SmartScreen/Defender observation is explicitly `NOT EXECUTED` — see `docs/personal-sync-architecture.md` §16G.2 for the full itemized list and §16G.7 for the manual procedure a future Windows-equipped pass should follow. |
| Known limitations | Everything in the Phase 15F row below, unchanged, plus: real-client Personal Sync behavior (OneDrive Files On-Demand, conflict-copy filenames, real two-device propagation timing, native folder picker) remains unverified — the standing limitation every Personal Sync phase (16A–16F) has carried forward, now the single largest known gap in the release. Manager workflow, Alerts, and any Phase 14+/15+ feature remain out of scope and were not started (Phase 14 explicitly on hold per CLAUDE.md). |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `8de293a4e93cbe9da882da49d9ae741ee4e31858f54811c765edadfa2adcebfe` |
| Compressed size | 70,506,594 bytes (67.2 MiB) |
| Extracted (staged) size | 160,480,930 bytes (153.1 MiB) |
| File count | 258 files (261 zip entries: 258 files + 3 directory entries — release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained, unchanged) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (unchanged since Phase 7C) |

File count and content are unchanged from the Phase 15F artifact's shape (same 258 staged files); the
byte-size increase (159,764,130 → 160,480,930 staged bytes, +0.7 MiB) is the Personal Sync feature's own
compiled code across Phase 16A–16F — no new loose companion files, no change to what's embedded vs. what's
shipped as a separate file. Extraction round-trip verified this phase: the built ZIP was extracted to an
isolated scratch directory outside `artifacts/` and `diff -rq`'d byte-for-byte identical against the
staging folder; the `.exe`'s SHA-256 inside the extraction independently matched the staged copy.

## Single-file release (experimental / deferred — status unchanged)

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `ff3dd29e1e0f77daad453b7cd5dfe156472ed792d85bbb6afe66d8d2e0e6c1e2` |
| Size | 185,291,578 bytes (176.7 MiB) |
| File count | **1** (unchanged — see Phase 15F's original entry above for why) |
| Publish flags | `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` (unchanged) |
| Delivery | Copy the one `.exe`, double-click it — no extraction, no companion files required to launch |
| **Status** | **Still EXPERIMENTAL / DEFERRED — not promoted this phase.** Per `docs/personal-sync-architecture.md` §16G.5: no new Windows-launch evidence was gathered this phase (no Windows host available), so there is nothing to weigh against the standing Phase 15F decision. Publish-and-inspect verified clean (exactly two files before `.pdb` removal — `Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt` — matching every prior single-file rebuild, confirmed via direct inspection, not just a successful `dotnet publish` exit code); actually launching this exe remains **NOT EXECUTED**. Do not distribute as the sole release form until a future phase validates a real Windows launch. |

## How this rebuild's artifacts were produced

Built manually on this (non-Windows, macOS) dev host — no `pwsh` runtime available (same limitation
recorded in every prior rebuild since Phase 7A), mirroring `packaging/windows/Publish-Windows.ps1` /
`Build-PortableRelease.ps1`'s own logic exactly:

1. `dotnet build CarowindsPassdown.slnx` (Debug, sanity check) — 0 warnings, 0 errors — followed by the
   Release publishes below.
2. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64
   --self-contained true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64` — the multi-file
   (ZIP) form.
3. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false
   -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64 -o
   artifacts/publish/win-x64-singlefile` — the single-file (experimental) form.
4. `.pdb` files removed from both outputs.
5. Multi-file output staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/`
   (publish output + `tools/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1` + both `.cmd`
   helpers + `README.txt` + `TESTER-GUIDE.txt`); forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`,
   `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `.DS_Store`) — clean.
6. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`; `shasum -a 256`
   the result.
7. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the staging
   folder reported byte-for-byte identical content; the `.exe`'s SHA-256 inside the extraction
   independently matched the staged copy.
8. Single-file output copied directly to
   `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe`; `shasum -a 256`'d directly.
9. `file` confirmed both executables are `PE32+ executable (GUI) x86-64, for MS Windows`; a forbidden-
   pattern check on the single-file publish directory (same pattern list as step 5) — clean, and its
   two-file content (`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`) matches every prior
   single-file rebuild exactly, confirming no loose `appsettings.json`/font-file regression.

This is a code/test/artifact-correctness rebuild, not a real-Windows-validated release — see
`docs/personal-sync-architecture.md`'s Phase 16G section (§16G.2, §16G.7) for exactly what remains
outstanding and the procedure to close it on real hardware.

---

# Release manifest — Phase 17C rebuild (Associate UI density + Classic/Compact/Modern PDF templates)

Rebuilt from the Phase 17 tree to package the Associate PDF template selector (Classic/Compact/Modern),
the Associate UI density pass, the Phase 17A product lifecycle/classification fixes, and the Phase 17B
persistent monthly Early Ride Time configuration. Packaging strategy, publish flags, and staging layout
are all unchanged from the Phase 16G rebuild above — this is a rebuild/re-hash entry, not a
packaging-strategy revision.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged — still an explicit pre-release development version) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | `0c2e4fa` (HEAD, "Phase 17C: Associate UI density pass + Classic/Compact/Modern PDF templates") |
| DB schema version | 15 (`Migration0015CreateErtDateOverridesTable`, current since Phase 17B) |
| AssociateDraftPayload schema version | 4 (current since Phase 17-era draft changes) |
| Snapshot schema version | 2 |
| SyncSchemaVersion | 1 (unchanged since Phase 16) |
| Deterministic test count | **2110/2110** — 1454 non-UI (116 Domain + 641 Application + 152 Documents +
  545 Infrastructure) + 656 Ui.Tests, confirmed clean via the same documented partition prior rebuilds
  used: the 38 real-`Window`-constructing tests (`OperationsCalendarViewTests`,
  `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests`) pass 100% clean run alone; the
  remaining 618 `Ui.Tests` pass 100% clean run alone. The full-process run (all test projects together)
  hit the same pre-existing Phase 6D Avalonia cross-thread headless flake documented in every prior
  rebuild (`System.InvalidOperationException: The calling thread cannot access this object because a
  different thread owns it.`), confined to those same 38 tests — not a new failure, and not
  investigated/fixed per CLAUDE.md's explicit instruction not to chase this flake during release
  packaging. |
| Build | `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors |
| ReadyToRun | Enabled for both release forms (unchanged since Phase 15D) |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available this session (macOS/Darwin
  dev host) — same limitation recorded in every prior rebuild since Phase 7A. |
| Known limitations | Same standing gaps as the Phase 16G entry above (Personal Sync real-client
  validation, single-file EXE Windows-launch validation) — nothing in Phase 17A/17B/17C touched Personal
  Sync or the single-file publish path. Manager workflow (Phase 5/14+) remains explicitly deferred per
  CLAUDE.md and was not started. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `98d0b7e0603c7a3ac6cc4168dbf1ec4d7cfbae3fd95ec0cc5944a9ed34342f00` |
| Compressed size | 70,637,782 bytes (67.4 MiB) |
| Extracted (staged) size | 160,849,570 bytes (153.4 MiB) |
| File count | 258 files (261 zip entries: 258 files + 3 directory entries — release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained, unchanged) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (unchanged since Phase 7C) |

Extraction round-trip verified this phase: the built ZIP was extracted to an isolated scratch directory
outside `artifacts/` and `diff -rq`'d byte-for-byte identical against the staging folder. Content
confirmed post-Phase-17C (not a stale reuse of an earlier build) via a UTF-16LE string search of the
packaged `Carowinds.Passdown.Documents.dll`, which contains
`ClassicAssociatePassdownTemplateRenderer`, `CompactAssociatePassdownTemplateRenderer`, and
`ModernAssociatePassdownTemplateRenderer` — the three Phase 17C template renderer types. A
forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`,
`.DS_Store`) over the staged folder — clean.

## Single-file release (experimental / deferred — status unchanged)

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `ce7a6e7aec95334396a1c0a6687f6abe860528c800e694ae59ad4be8e2c51ddf` |
| Size | 185,807,674 bytes (177.2 MiB) |
| File count | **1** (unchanged — see the Phase 15F entry above for why) |
| Publish flags | `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` (unchanged) |
| Delivery | Copy the one `.exe`, double-click it — no extraction, no companion files required to launch |
| **Status** | **Still EXPERIMENTAL / DEFERRED — not promoted this phase.** No new Windows-launch evidence
  was gathered (no Windows host available), so there is nothing to weigh against the standing Phase 15F
  decision. Publish-and-inspect verified clean (exactly two files before `.pdb` removal —
  `Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`); actually launching this exe remains **NOT
  EXECUTED**. Do not distribute as the sole release form until a future phase validates a real Windows
  launch. |

## How this rebuild's artifacts were produced

Built on this (non-Windows, macOS) dev host — no `pwsh` runtime available (same limitation recorded in
every prior rebuild since Phase 7A), mirroring `packaging/windows/Publish-Windows.ps1` /
`Build-PortableRelease.ps1`'s own logic exactly (their `-SkipPublish`/staging/zip steps were run
manually via shell + `dotnet publish` + `zip`, not via `pwsh`, which is unavailable in this session):

1. Removed stale `artifacts/publish/`, `artifacts/release-staging/`, `artifacts/releases/` output left
   over from the Phase 16G rebuild (dated Sep 9) before building, so nothing new could mix with stale
   content.
2. `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
3. `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` plus the isolated-partition reruns
   described above — 2110/2110 confirmed clean.
4. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64
   --self-contained true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64` — the multi-file
   (ZIP) form. 252 files, 154 MB after `.pdb` removal (0 stray `.pdb`, `.dylib`, `.so`, or Playwright
   files found).
5. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r win-x64
   --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false
   -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64 -o
   artifacts/publish/win-x64-singlefile` — the single-file (experimental) form. Exactly two files
   before `.pdb` removal (`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`).
6. Multi-file output staged into
   `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/` (publish output +
   `tools/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1` + both `.cmd` helpers +
   `README.txt` + `TESTER-GUIDE.txt`, matching `Build-PortableRelease.ps1`'s layout exactly);
   forbidden-pattern check — clean.
7. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`; `shasum -a 256`
   the result.
8. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the staging
   folder reported byte-for-byte identical content.
9. Single-file output copied directly to
   `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe`; `shasum -a 256`'d directly.
10. `file` confirmed both executables are `PE32+ executable (GUI) x86-64, for MS Windows`. A UTF-16LE
    string search of the multi-file build's `Carowinds.Passdown.Documents.dll` confirmed the three
    Phase 17C template renderer type names are present, ruling out a stale-build reuse.

This supersedes the Phase 16G artifacts above as the current release; the functional delta is Phase
17A (product lifecycle/classification fixes), 17B (persistent monthly Early Ride Time configuration +
date override), and 17C (Associate UI density pass + Classic/Compact/Modern PDF templates). No Personal
Sync or Manager-workflow code changed since Phase 16G — real Windows/OneDrive-client validation remains
outstanding exactly as recorded there.

---

# Release manifest — Phase 17D rebuild (rides-only ERT, compact Overview panel, master PDF layout)

Rebuilt from the Phase 17D tree — a pure release-packaging task (no code changes made during this
rebuild). Packaging strategy, publish flags, and staging layout are unchanged from the Phase 17C
rebuild above; this is a rebuild/re-hash entry only.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged — still an explicit pre-release development version) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | `17a9dcd50bc69abce35b873c5e8f12e90e9c9ee6` (HEAD, "Phase 17D: rides-only ERT, compact Overview panel, master PDF layout") |
| Working tree at build time | Clean except macOS Finder `.DS_Store` noise (`../.DS_Store`, `.DS_Store`, `.claude/.DS_Store`, `.claude/skills/.DS_Store`, `.claude/skills/pdf-design/`) — no source changes |
| DB schema version | 15 (`Migration0015CreateErtDateOverridesTable`, unchanged since Phase 17B) |
| AssociateDraftPayload schema version | 4 (unchanged since Phase 17C) |
| Snapshot schema version | 2 (unchanged since Phase 17C) |
| SyncSchemaVersion | 1 (unchanged since Phase 16) |
| Deterministic test count | **2125/2125** — 116 Domain + 642 Application + 166 Documents + 545
  Infrastructure + 656 Ui.Tests, confirmed clean via the same documented partition prior rebuilds
  used: the 38 real-`Window`-constructing tests (`OperationsCalendarViewTests`,
  `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests`) pass 100% clean run alone; the
  remaining 618 `Ui.Tests` pass 100% clean run alone. The full-process combined run hit the same
  pre-existing Phase 6D Avalonia cross-thread headless flake documented in every prior rebuild
  (`System.InvalidOperationException: The calling thread cannot access this object because a
  different thread owns it.`), confined to those same 38 tests — not a new failure, and not
  investigated/fixed per CLAUDE.md's explicit instruction not to chase this flake during release
  packaging. |
| Build | `dotnet build CarowindsPassdown.slnx` (Release) — 0 warnings, 0 errors |
| ReadyToRun | Enabled for both release forms (unchanged since Phase 15D) |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available this session
  (macOS/Darwin dev host) — same limitation recorded in every prior rebuild since Phase 7A. |
| PDF smoke check | **NOT EXECUTED.** No existing tool in this repo dumps a generated PDF to disk
  outside the test/Avalonia UI itself, and no Windows host was available to run the packaged EXE;
  writing new throwaway generation code was judged out of scope for a pure packaging task. Phase
  17D's own visual PDF review (master layout, no branding/comparison pricing/ERT time) was already
  completed during Phase 17D implementation per the baseline this rebuild started from. |
| Known limitations | Same standing gaps as the Phase 16G/17C entries above (Personal Sync
  real-client validation, single-file EXE Windows-launch validation, Windows DPI validation).
  Manager workflow (Phase 5/14+) remains explicitly deferred per CLAUDE.md and was not started. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| Path | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `7d1c997cb7fc1e80fc6d721fdfc4b28c609b248939c88597c4da77f3e8664b52` |
| Size | 70,609,233 bytes (67.3 MiB) |
| File count | 258 files (261 zip entries: 258 files + 3 directory entries — release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained, unchanged) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (unchanged since Phase 7C) |

Extraction round-trip verified this rebuild: the built ZIP was extracted to an isolated scratch
directory outside `artifacts/` and `diff -rq`'d byte-for-byte identical against the staging folder;
the `.exe`'s SHA-256 inside the extraction independently matched the staged copy. Content confirmed
post-Phase-17D (not a stale reuse of an earlier build): a string search of the packaged
`Carowinds.Passdown.Documents.dll` confirms the `MasterProductLayout` type (introduced this phase,
`src/Carowinds.Passdown.Documents/Associate/Templates/MasterProductLayout.cs`) is present. A
forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`,
`.DS_Store`) over the staged folder — clean.

## Single-file release (experimental / deferred — status unchanged)

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| Path | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `4c242979392dc4ffd8ad1f68de0f4b206b5b190d9a979333f6d8450298f604da` |
| Size | 185,750,330 bytes (177.1 MiB) |
| File count | **1** (unchanged — see the Phase 15F entry above for why) |
| Publish flags | `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` (unchanged) |
| Delivery | Copy the one `.exe`, double-click it — no extraction, no companion files required to launch |
| **Status** | **Still EXPERIMENTAL / DEFERRED — not promoted this phase.** No new Windows-launch
  evidence was gathered (no Windows host available), so there is nothing to weigh against the
  standing Phase 15F decision. Publish-and-inspect verified clean (exactly two files before `.pdb`
  removal — `Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`); actually launching this exe
  remains **NOT EXECUTED**. Do not distribute as the sole release form until a future phase
  validates a real Windows launch. |

## Release output location

Both final artifacts are in `artifacts/releases/` — the directory this repository's own scripts
(`Build-PortableRelease.ps1`) and every prior manifest entry already treat as canonical. (Note:
`artifacts/release/` — singular — does not exist in this repository; `artifacts/releases/` is the
established path and was used here.)

## How this rebuild's artifacts were produced

Built on this (non-Windows, macOS) dev host — no `pwsh` runtime available (same limitation recorded
in every prior rebuild since Phase 7A), mirroring `packaging/windows/Publish-Windows.ps1` /
`Build-PortableRelease.ps1`'s own logic exactly (their publish/staging/zip steps were run manually
via shell + `dotnet publish` + `zip`, not via `pwsh`, which is unavailable in this session):

1. Removed stale `artifacts/publish/`, `artifacts/release-staging/`, `artifacts/releases/` output
   left over from the Phase 17C rebuild (dated Sep 14) before building, so nothing new could mix
   with stale content.
2. `dotnet build CarowindsPassdown.slnx -c Release` — 0 warnings, 0 errors.
3. `dotnet test CarowindsPassdown.slnx -c Release --filter "Category!=LiveSmoke"` plus the isolated-
   partition reruns described above — 2125/2125 confirmed clean.
4. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r
   win-x64 --self-contained true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64` — the
   multi-file (ZIP) form. 252 files, 154 MB after `.pdb` removal (0 stray `.pdb`, `.dylib`, `.so`, or
   Playwright files found).
5. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r
   win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
   -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64
   -o artifacts/publish/win-x64-singlefile` — the single-file (experimental) form. Exactly two files
   before `.pdb` removal (`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`).
6. Multi-file output staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/`
   (publish output + `tools/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1` + both
   `.cmd` helpers + `README.txt` + `TESTER-GUIDE.txt`, matching `Build-PortableRelease.ps1`'s layout
   exactly); forbidden-pattern check — clean.
7. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`;
   `shasum -a 256` the result.
8. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the
   staging folder reported byte-for-byte identical content; the `.exe`'s SHA-256 inside the
   extraction independently matched the staged copy.
9. Single-file output copied directly to
   `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe`; `shasum -a 256`'d directly.
10. `file` confirmed both executables are `PE32+ executable (GUI) x86-64, for MS Windows`. A string
    search of the multi-file build's `Carowinds.Passdown.Documents.dll` confirmed the
    Phase 17D-specific `MasterProductLayout` type is present, ruling out a stale-build reuse.

This supersedes the Phase 17C artifacts above as the current release; the functional delta is Phase
17D (rides-only ERT, compact Associate Overview panel, master PDF layout shared across
Classic/Compact/Modern). Schema is unchanged (DB 15 / Draft 4 / Snapshot 2 / SyncSchemaVersion 1) —
no migration was added since Phase 17B. No Personal Sync or Manager-workflow code changed since
Phase 16G — real Windows/OneDrive-client validation and Windows DPI validation both remain
outstanding exactly as recorded there.

---

# Release manifest — Phase 17E rebuild (Classic/Compact/Modern redesigned as genuinely distinct PDF compositions)

Rebuilt from the Phase 17E tree — a pure release-packaging task (no code changes made during this
rebuild). Packaging strategy, publish flags, and staging layout are unchanged from the Phase 17D
rebuild above; this is a rebuild/re-hash entry only.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged — still an explicit pre-release development version) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | `72ed424184a2048f3f88f05f17fafffa13333640` (HEAD, "Phase 17E: redesign Classic/Compact/Modern as genuinely distinct PDF compositions") |
| Working tree at build time | Clean except macOS Finder `.DS_Store` noise (`../.DS_Store`, `.DS_Store`, `.claude/.DS_Store`, `.claude/skills/.DS_Store`, `.claude/skills/pdf-design/`) — no source changes |
| DB schema version | 15 (`Migration0015CreateErtDateOverridesTable`, unchanged since Phase 17B) |
| AssociateDraftPayload schema version | 4 (unchanged since Phase 17C) |
| Snapshot schema version | 2 (unchanged since Phase 17C) |
| SyncSchemaVersion | 1 (unchanged since Phase 16) |
| Deterministic test count | **2137/2137** — 116 Domain + 642 Application + 178 Documents + 545
  Infrastructure + 656 Ui.Tests, confirmed clean via the same documented partition prior rebuilds
  used: the 38 real-`Window`-constructing tests (`OperationsCalendarViewTests`,
  `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests`) pass 100% clean run alone; the
  remaining 618 `Ui.Tests` pass 100% clean run alone. The full-process combined run hit the same
  pre-existing Phase 6D Avalonia cross-thread headless flake documented in every prior rebuild
  (`System.InvalidOperationException: The calling thread cannot access this object because a
  different thread owns it.`), confined to those same 38 tests — not a new failure, and not
  investigated/fixed per CLAUDE.md's explicit instruction not to chase this flake during release
  packaging. |
| Build | `dotnet build CarowindsPassdown.slnx -c Release` — 0 warnings, 0 errors |
| ReadyToRun | Enabled for both release forms (unchanged since Phase 15D) |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available this session
  (macOS/Darwin dev host) — same limitation recorded in every prior rebuild since Phase 7A. |
| PDF smoke check | Existing deterministic `TemplateCompositionTests` (8 tests, added this phase)
  run against the current packaged source tree — **8/8 PASS**. These assert real glyph Y-positions
  to structurally prove Classic (single-column), Compact (paired two-column), and Modern (editorial/
  numbered) are different compositions, not just different styling — direct evidence, not a new
  packaging-time PDF dump. |
| Known limitations | Same standing gaps as the Phase 16G/17C/17D entries above (Personal Sync
  real-client validation, single-file EXE Windows-launch validation, Windows DPI validation).
  Manager workflow (Phase 5/14+) remains explicitly deferred per CLAUDE.md and was not started. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| Path | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `cb47a6cb035cdbd453469a18a180ed402709289c509aa6e7da18d293ad2346f6` |
| Size | 70,611,885 bytes (67.3 MiB) |
| File count | 258 files (261 zip entries: 258 files + 3 directory entries — release root, `LatoFont\`, `tools\`) |
| Executable | `Carowinds.Passdown.Desktop.exe` (internal identity intentionally retained, unchanged) |
| Delivery | Portable — extract and run; no installer, no admin rights |
| Automatic refresh | Supported via included `Enable Automatic Refresh.cmd` / `Disable Automatic Refresh.cmd` (unchanged since Phase 7C) |

Extraction round-trip verified this rebuild: the built ZIP was extracted to an isolated scratch
directory outside `artifacts/` and `diff -rq`'d byte-for-byte identical against the staging folder;
the `.exe`'s SHA-256 inside the extraction independently matched the staged copy. Content confirmed
post-Phase-17E (not a stale reuse of the Phase 17D build): a UTF-16LE string search of the packaged
`Carowinds.Passdown.Documents.dll` confirms the new `SingleColumnProductLayout` type (introduced this
phase, used by Classic and Modern) is present alongside the pre-existing `MasterProductLayout` type
(still used by Compact alone), and all three renderer types
(`ClassicAssociatePassdownTemplateRenderer`, `CompactAssociatePassdownTemplateRenderer`,
`ModernAssociatePassdownTemplateRenderer`) are present. A forbidden-pattern check (`.git`,
`.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`, `.DS_Store`) over the staged folder —
clean.

## Single-file release (experimental / deferred — status unchanged)

| | |
|---|---|
| Artifact | `Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| Path | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `a766368f0755f791ad53f6efc9ccfec8ee268734f9d25113cf4fbcb1261f5b0c` |
| Size | 185,750,330 bytes (177.1 MiB) |
| File count | **1** (unchanged — see the Phase 15F entry above for why) |
| Publish flags | `-r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` (unchanged) |
| Delivery | Copy the one `.exe`, double-click it — no extraction, no companion files required to launch |
| **Status** | **Still EXPERIMENTAL / DEFERRED — not promoted this phase.** No new Windows-launch
  evidence was gathered (no Windows host available), so there is nothing to weigh against the
  standing Phase 15F decision. Publish-and-inspect verified clean (exactly two files before `.pdb`
  removal — `Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`); actually launching this exe
  remains **NOT EXECUTED**. Do not distribute as the sole release form until a future phase
  validates a real Windows launch. A UTF-16LE string search of this single-file exe also confirms
  `SingleColumnProductLayout` and the `Carowinds Operations` product attribute are present (not a
  stale reuse). |

## Release content cleanliness note

A byte-string scan of both release forms found the build host's absolute repository path
(`/Users/kashyap/Documents/Passdown/carowinds-passdown-greenfield/...`) embedded once per managed
assembly, as the PE CodeView debug-directory's `.pdb` path — standard compiler-emitted build
metadata (present even though no `.pdb` file itself is shipped), not user data, credentials, or a
new regression: every prior rebuild since Phase 15F was built on this same host and carries the
identical class of reference. No `.db`/`-wal`/`-shm`, log, sync-folder, draft, or credential content
was found in either release form.

## Release output location

Both final artifacts are in `artifacts/releases/` — the directory this repository's own scripts
(`Build-PortableRelease.ps1`) and every prior manifest entry already treat as canonical.

## How this rebuild's artifacts were produced

Built on this (non-Windows, macOS) dev host — no `pwsh` runtime available (same limitation recorded
in every prior rebuild since Phase 7A), mirroring `packaging/windows/Publish-Windows.ps1` /
`Build-PortableRelease.ps1`'s own logic exactly (their publish/staging/zip steps were run manually
via shell + `dotnet publish` + `zip`, not via `pwsh`, which is unavailable in this session):

1. Removed stale `artifacts/publish/`, `artifacts/release-staging/`, `artifacts/releases/` output
   left over from the Phase 17D rebuild (dated Sep 15) before building, so nothing new could mix
   with stale content.
2. `dotnet build CarowindsPassdown.slnx -c Release` — 0 warnings, 0 errors.
3. `dotnet test CarowindsPassdown.slnx -c Release --filter "Category!=LiveSmoke"` plus the isolated-
   partition reruns described above — 2137/2137 confirmed clean.
4. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r
   win-x64 --self-contained true -p:PlaywrightPlatform=win-x64 -o artifacts/publish/win-x64` — the
   multi-file (ZIP) form. 252 files, 154 MB after `.pdb` removal (0 stray `.pdb`, `.dylib`, `.so`, or
   Playwright files found).
5. `dotnet publish src/Carowinds.Passdown.Desktop/Carowinds.Passdown.Desktop.csproj -c Release -r
   win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
   -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64
   -o artifacts/publish/win-x64-singlefile` — the single-file (experimental) form. Exactly two files
   before `.pdb` removal (`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`).
6. Multi-file output staged into `artifacts/release-staging/Carowinds-Operations-0.1.0-dev-win-x64/`
   (publish output + `tools/Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1` + both
   `.cmd` helpers + `README.txt` + `TESTER-GUIDE.txt`, matching `Build-PortableRelease.ps1`'s layout
   exactly); forbidden-pattern check — clean.
7. `zip -r -X -9` into `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip`;
   `shasum -a 256` the result.
8. Extracted fresh to an isolated scratch directory outside `artifacts/`; `diff -rq` against the
   staging folder reported byte-for-byte identical content; the `.exe`'s SHA-256 inside the
   extraction independently matched the staged copy.
9. Single-file output copied directly to
   `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe`; `shasum -a 256`'d directly.
10. `file` confirmed both executables are `PE32+ executable (GUI) x86-64, for MS Windows`. A UTF-16LE
    string search of the multi-file build's `Carowinds.Passdown.Documents.dll` confirmed the
    Phase 17E-specific `SingleColumnProductLayout` type is present, ruling out a stale-build reuse of
    the Phase 17D artifact.

This supersedes the Phase 17D artifacts above as the current release; the functional delta is Phase
17E (Classic redesigned as a single-column operations sheet, Compact retained as the paired
two-column density benchmark, Modern redesigned with an editorial metadata grid and numbered-section
system — composition changes, not just styling). Schema is unchanged (DB 15 / Draft 4 / Snapshot 2 /
SyncSchemaVersion 1) — no migration was added since Phase 17B. No Personal Sync or Manager-workflow
code changed since Phase 16G — real Windows/OneDrive-client validation and Windows DPI validation
both remain outstanding exactly as recorded there.

# Release manifest — Dashboard ERT fix rebuild (rides-only Early Ride Time no longer breaks the Dashboard)

Rebuilt from commit `399d40c` ("Fix Dashboard load recovery on existing Windows data"). No code
changed during the rebuild itself. Packaging strategy, publish flags, staging layout and the 10-step
procedure are unchanged from the Phase 17E entry above; this entry only records the new build and
hashes. Functional change since Phase 17E: if today's month (or today's date) has a rides-only
Early Ride Time configuration, the Dashboard now loads instead of showing "Unable to load today's
operations summary." See `docs/support-troubleshooting.md`.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged; assembly informational version `0.1.0-dev+399d40c4a7bc54b9a99b3191d433051d8b8fd28d`) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | `399d40c4a7bc54b9a99b3191d433051d8b8fd28d` (HEAD) |
| Working tree at build time | No source changes. Only the same macOS `.DS_Store` / `.claude/skills/pdf-design/` noise recorded in the Phase 17E entry |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (all unchanged, no migration) |
| Deterministic test count | **2150/2150**: 116 Domain + 642 Application + 178 Documents + 545 Infrastructure + 669 Ui.Tests (38 real-`Window` tests run alone + the remaining 631 run alone, the same documented partition as before) |
| Build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors |
| Windows validation | **NOT EXECUTED.** No Windows host, VM or affected PC was available (macOS dev host). The Dashboard fix has not yet been confirmed on a real affected machine. |
| Known limitations | Same standing gaps as the Phase 17E entry (Personal Sync real-client validation, single-file EXE Windows-launch validation, Windows DPI validation). Manager workflow remains deferred. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `44dd165cccdf66882a1734d11df94897ea48127cf7c602353e20573b4bce8cc0` |
| Size | 70,612,096 bytes (67.3 MiB) |
| File count | 258 files (261 zip entries) |

Verification:

- The ZIP was extracted to an isolated scratch directory, and `diff -rq` showed it byte-for-byte
  identical to the staging folder.
- The staged and extracted `.exe` SHA-256 values match.
- `file` reports `PE32+ executable (GUI) x86-64, for MS Windows`.
- The packaged `Carowinds.Passdown.Desktop.dll` carries informational version `+399d40c…`, so this
  is not a stale reuse of the 17E build.
- Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`,
  `.DS_Store`): clean.
- No stray `.dylib`/`.so`/Playwright files.

## Single-file release (still experimental / deferred)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `2c675479c8ad8c3a704f6c4e75af3c3b7aefd8bc2632893798ae7c845c0bd6db` |
| Size | 185,750,330 bytes (177.1 MiB) |
| Status | Unchanged: **EXPERIMENTAL / DEFERRED**. It publishes and inspects clean, and the fix commit `399d40c…` is embedded in the bundle. Launching it on Windows: **NOT EXECUTED**. |

This build supersedes the Phase 17E artifacts as the current release.

# Release manifest — Phase 17F rebuild (refined PDF templates, Modern section numbering removed)

Rebuilt from commit `8671e92` ("Phase 17F: refine Associate PDF templates; remove Modern section
numbering"). No code changed during the rebuild itself. Packaging strategy, publish flags, staging
layout and the 10-step procedure are unchanged from the Phase 17E entry. This entry records the new
build and its hashes.

Functional change since the Dashboard ERT fix build:

- Modern no longer prints "01/02/…" section numbers. It is told apart by its two-line date and
  four-column summary grid.
- All three templates align prices in one fixed right-hand column.
- A section heading is never left alone at a page bottom.
- An overflowing PDF is re-rendered once with tighter spacing and kept if that saves a page.
- On multi-page PDFs the footer shows "1 / 2"; single-page footers are unchanged.

See `docs/associate-passdown-templates.md` §17.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged; assembly informational version `0.1.0-dev+8671e92a5dfedc3323f294f88905642dfceb06af`) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build commit | `8671e92a5dfedc3323f294f88905642dfceb06af` (HEAD) |
| Working tree at build time | No source changes. Only the same macOS `.DS_Store` / `.claude/skills/pdf-design/` noise recorded in the Phase 17E entry |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (all unchanged, no migration) |
| Deterministic test count | **2182/2182**: 116 Domain + 642 Application + 210 Documents + 545 Infrastructure + 669 Ui.Tests (631 run together; `Phase15ERealWindowLayoutTests` 10, `RealWindowStartupTimingTests` 2, `OperationsCalendarViewTests` 26 each run alone, per the documented partition) |
| Build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors |
| Windows validation | **NOT EXECUTED.** No Windows host or VM was available (macOS dev host). |
| Known limitations | Same standing gaps as the Phase 17E entry (Personal Sync real-client validation, single-file EXE Windows-launch validation, Windows DPI validation). Manager workflow remains deferred. |

## Primary release: multi-file portable ZIP

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `c504c211e175f1da4099e2b5b2436b3fbef81a8668b1329846482d41a41e7b53` |
| Size | 70,623,278 bytes (67.4 MiB) |
| File count | 258 files (261 zip entries) |

Verification:

- Stale `artifacts/publish/`, `release-staging/` and `releases/` were removed before publishing.
- The multi-file publish had 252 files after `.pdb` removal, with no stray `.pdb`/`.dylib`/`.so`/
  Playwright files.
- The ZIP was extracted to an isolated scratch directory, and `diff -rq` showed it byte-for-byte
  identical to the staging folder.
- The staged and extracted `.exe` SHA-256 values match
  (`3fb7e3e6341766b360dff11b40944227ec8bd8e80fce1ec001b7a59fd7629134`).
- `file` reports `PE32+ executable (GUI) x86-64, for MS Windows`.
- The packaged `Carowinds.Passdown.Desktop.dll` carries informational version `+8671e92…`.
- `Carowinds.Passdown.Documents.dll` contains the Phase 17F-only `PdfPrimitives`/`PdfDensity`/
  `KeepSection` symbols, and no longer contains the removed `NumberedSectionLabel`. This is not a
  stale reuse of an earlier build.
- Forbidden-pattern check (`.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md`, `.pdb`,
  `.DS_Store`): clean.

## Single-file release (still experimental / deferred)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `aa8cbe06b1a6c03507ba2799d41980b5abe9730c1828a102fdd568f2f3870cec` |
| Size | 185,779,002 bytes (177.2 MiB) |
| Status | Unchanged: **EXPERIMENTAL / DEFERRED**. The publish produced exactly two files before `.pdb` removal (`Carowinds.Passdown.Desktop.exe` and `LatoFont/OFL.txt`). The `PE32+ (GUI) x86-64` header, the `+8671e92…` version stamp, and the `PdfPrimitives`/`PdfDensity` symbols are present in the bundle, and `NumberedSectionLabel` is absent. Launching it on Windows: **NOT EXECUTED**. |

This build supersedes the Dashboard ERT fix artifacts as the current release.

# Release manifest — Phase 18C hardened release candidate (third-party notices, packaging validator)

> **Superseded by Phase 18D source changes (not yet rebuilt).** Phase 18D (Conjuring retrieval,
> North Gate/Avenue overrides, build identification — `docs/phase18-comprehensive-audit.md` §16)
> changes functional source. Do not distribute or Windows-validate the 18C ZIP/EXE below; rebuild
> from the Phase 18D commit first. The 18C artifacts share the 18D build's file names
> (`Carowinds-Operations-0.1.0-dev-win-x64.*`) — tell them apart by SHA-256 or by the
> informational version's commit (`+5c0ddf2…` = 18C).

Built from commit `5c0ddf292fe5a62ffb23d52a91d7126a921afd35` ("Phase 18C: third-party notices (PKG-003)
and Desktop.dll build-path leak fix"), on top of the Phase 18A/18B hardening (`5598618`). Docs-only
commit(s) after `5c0ddf2` do not change any shipped byte.

**Status: built and validated on the build host only. Not yet validated on Windows.** See "Windows
validation" below — every Windows item is NOT EXECUTED, so this is not yet the stable internal release
candidate.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | `0.1.0-dev` (unchanged; informational version `0.1.0-dev+5c0ddf292fe5a62ffb23d52a91d7126a921afd35`, confirmed inside the packaged `Carowinds.Passdown.Desktop.dll` and the single-file EXE) |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0 (Darwin 27.0.0), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.6 (installed as a local `dotnet tool` for this build — not Windows PowerShell) |
| Phase 18A/18B hardening | Included (all 18A/18B fixes; REL-004 open-editor protection) |
| Phase 18C changes | PKG-003 third-party notices shipped; release validator extended (notices, empty files, duplicate ZIP entries); **PKG-004 fixed** — `Carowinds.Passdown.Desktop.dll` leaked the build machine's checkout path (see `docs/phase18-comprehensive-audit.md` §15) |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (unchanged; no migration; no sync format change) |
| Deterministic tests | **2484/2484** = 116 Domain + 661 Application + 226 Documents + 748 Infrastructure + 695 Ui together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`) |
| Test count note | The Phase 18B figure "2476" counted Infrastructure.Tests' 11 opt-in `LiveSmoke` (live-internet) tests. Measured at 18B's commit `5598618` with the established filter, the deterministic baseline is **2465**. Phase 18C adds 19 (18 packaging-validator tests, 1 Desktop-assembly path test): 2465 + 19 = 2484 |
| Build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.zip` |
| SHA-256 | `776fbe47680cdaee1edd47bbc92b434516f71c74d3be15381127a6f5bda9d467` |
| Size | 73,413,887 bytes (70.0 MiB) |
| Content | 282 files (258 as before + `THIRD-PARTY-NOTICES.txt` + 23 files under `licenses\`); one top-level folder `Carowinds-Operations-0.1.0-dev-win-x64\`; 282 entries (`Compress-Archive` writes no separate directory entries — earlier `zip -r` builds had 3; extraction is identical) |

Produced by the real `packaging/windows/Build-PortableRelease.ps1` (first time a release has been built
by the script itself rather than by a manual shell mirror), which runs `Publish-Windows.ps1` and then
`Test-ReleasePackage.ps1` on the staging folder and again on the final ZIP.

Verification:

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on the staging folder and the ZIP (282 files
  each). Run under PowerShell 7.6.6 on the macOS build host, **not** on Windows. The C# mirror
  (`CAROWINDS_RELEASE_ZIP=… dotnet test … --filter RealReleaseZip`) also passed on the final ZIP.
- The first validator run **failed** (correctly) on `Carowinds.Passdown.Desktop.dll` containing the
  checkout path — PKG-004, fixed in `5c0ddf2`, then rebuilt. The check was not bypassed.
- ZIP structure: CRC test OK; no duplicate entries; no backslash entry names; no empty files; every
  `.dll`/`.exe` is a PE image (166 x64 native/R2R, 83 AnyCPU IL); extracted to a clean scratch directory
  outside `artifacts/` and found byte-identical (SHA-256 per file) to the staging folder.
- Required content present: executable, managed/runtime files, native `e_sqlite3`/`libSkiaSharp`/
  `libHarfBuzzSharp`/`av_libglesv2`/`QuestPdfSkia`/`qpdf`, `Enable/Disable Automatic Refresh.cmd`,
  `tools\Install-/Uninstall-ScheduledRefresh.ps1`, `README.txt`, `TESTER-GUIDE.txt`,
  `THIRD-PARTY-NOTICES.txt`, `licenses\…`, `LatoFont\OFL.txt`.
- Developer-path scan: our checkout path and user name appear in **no** file (UTF-8 and UTF-16).
  `/Users/`, `/home/`, `C:\Users\` appear in no project-owned file (`Carowinds.Passdown.*` binaries and
  all text files). Only the known third-party vendor build paths remain, in `MicroCom.Runtime.dll` and
  the three `SQLitePCLRaw.*.dll` files (their authors' own machines; accepted in 18B).

## Single-file release (EXPERIMENTAL — not the primary artifact)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-dev-win-x64.exe` |
| SHA-256 | `a013240875b71e65ae0583e359656067f7634b5c0ed09b5397de0fcb57f03924` |
| Size | 186,041,146 bytes (177.4 MiB) |
| Status | **EXPERIMENTAL / DEFERRED** (unchanged). `PE32+ (GUI) x86-64`; carries `+5c0ddf2…`; no checkout path or user name; the only `/Users/` strings are SQLitePCLRaw's vendor paths. Launch on Windows: **NOT EXECUTED**. |
| Limitation | Distributed alone, the EXE carries no `THIRD-PARTY-NOTICES.txt`/`licenses\` (the publish produces only the EXE plus `LatoFont\OFL.txt`). If it is ever promoted, ship it inside a folder/ZIP with the notices. |

## Third-party notices (PKG-003)

`THIRD-PARTY-NOTICES.txt` (release root) lists every redistributed third-party component with version,
license, copyright and the location of the full text. Full texts under `licenses\` are copied unchanged
from the shipped NuGet packages (no web-sourced text):

| Component | Version | License | Full text / notices |
|---|---|---|---|
| .NET runtime, Microsoft.Extensions.*, Microsoft.Data.Sqlite.Core | 10.0.11 | MIT | `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` |
| Avalonia (all `Avalonia.*`) | 12.1.1 | MIT | MIT text in the index (package has no license file) |
| ANGLE (`av_libglesv2.dll`) | 2.1.27548.20260419 | BSD-3-Clause | `ANGLE-LICENSE.txt` |
| Inter font (in `Avalonia.Fonts.Inter.dll`) | 3.019 | SIL OFL 1.1 | `Inter-OFL.txt` (copyright line read from the shipped font files) |
| SkiaSharp / HarfBuzzSharp (+ native) | 3.119.4 / 8.3.1.3 | MIT | `SkiaSharp-HarfBuzzSharp-LICENSE.txt`, `…-THIRD-PARTY-NOTICES.txt` |
| QuestPDF (+ `QuestPdfSkia.dll`, `qpdf.dll`) | 2026.7.3 | QuestPDF Community License | `QuestPDF-LICENSE.txt`, `QuestPDF-dependencies\` (12 bundled-library licenses) |
| Lato font (in `Carowinds.Passdown.Documents.dll`) | — | SIL OFL 1.1 | `LatoFont\OFL.txt` (unchanged location) |
| SQLitePCLRaw (+ `e_sqlite3.dll`; SQLite is public domain) | 2.1.12 | Apache-2.0 | `Apache-2.0.txt` |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `CommunityToolkit.Mvvm-LICENSE.txt`, `…-THIRD-PARTY-NOTICES.txt` |
| MicroCom.Runtime | 0.11.6 | MIT | MIT text in the index |
| Tmds.DBus.Protocol | 0.94.1 | MIT | MIT text in the index |

Not listed (not shipped): test packages, `AvaloniaUI.DiagnosticsSupport` (excluded from Release),
build tooling. Project-owned assets (`Assets\logo.ico`, `logo.png`) are not third-party.

**Open owner decision (not a technical defect, not legal advice):** QuestPDF's Community License
(`licenses\QuestPDF-LICENSE.txt`, "Community License Eligibility") excludes publicly traded companies
and organisations above USD 1M annual revenue, and treats code an AI tool writes on an organisation's
behalf as that organisation's. The app selects `LicenseType.Community` in code. Whether this tool's
use falls under an eligible licensee depends on who owns/operates it — confirm before wider
distribution.

## Windows validation

No Windows PC, VM, affected PC, or second PC was available in this session. Every item below is
**NOT EXECUTED**; nothing is inferred from automated tests.

| Item | Result |
|---|---|
| Packaging validator on Windows / Windows PowerShell | NOT EXECUTED (ran on macOS under PowerShell 7.6.6 — PASS there) |
| ZIP extract + launch as standard user; second launch | NOT EXECUTED |
| Affected-PC Dashboard (rides-only ERT) | NOT EXECUTED |
| Existing-data compatibility (DB 15, drafts, notes, History, Calendar, PDFs, settings) | NOT EXECUTED |
| US formatting under a non-US Windows region | NOT EXECUTED |
| App log (`%LOCALAPPDATA%\Carowinds Passdown\logs\app-YYYYMMDD.log`) | NOT EXECUTED |
| Diagnostics export contents | NOT EXECUTED |
| PDFs (Classic/Compact/Modern, multi-page numbering) in a Windows viewer | NOT EXECUTED |
| History "Open Final PDF" temp cleanup | NOT EXECUTED |
| Task Scheduler install/run/uninstall from the extracted ZIP | NOT EXECUTED |
| Personal Sync one-PC smoke test (OneDrive) | NOT EXECUTED |
| Two-PC Associate / Notes / ERT conflict validation (REL-004) | NOT EXECUTED |
| Sync temp cleanup and sync PDF skip in the real world | NOT EXECUTED |
| Single-file EXE launch | NOT EXECUTED |

Windows test machine details (version/build, x64 vs ARM64 emulation, extraction path,
SmartScreen/Defender behaviour): not recorded — no machine.

## Known limitations

- All Windows and two-PC OneDrive validation above is outstanding.
- `Build-PortableRelease.ps1` / `Publish-Windows.ps1` declare `#Requires -Version 5.1` but use
  three-argument `Join-Path` (PowerShell 6+). Run them with PowerShell 7 (`pwsh`). The scripts that run on
  user machines (`tools\*.ps1`, called by the `.cmd` helpers through `powershell.exe`) use no
  PowerShell 7-only syntax. Not changed in 18C.
- Single-file EXE: experimental, ships without notices when distributed alone (above).
- QuestPDF licence eligibility: owner decision (above).
- Standing gaps from earlier entries (Windows DPI validation; Manager workflow deferred).

## Release recommendation

**RELEASE BLOCKED — pending Windows validation.** No release-blocking defect is open: the one
blocker found (PKG-004) was fixed, regression-tested and rebuilt. But the Phase 18C acceptance gate
requires the packaging validator to pass on Windows, the ZIP to launch, the Dashboard to load, and
logging, diagnostics, PDFs and the scheduler to work on Windows — none of which could be run here.
After those pass on a Windows PC, the ZIP above (unchanged hash) can be called the stable internal
release candidate. Without two PCs it would be "READY WITH UNVALIDATED TWO-PC SYNC".

This build supersedes the Phase 17F artifacts as the current build.

# Release manifest — 0.1.0-rc.1 (Phase 18E, first uniquely-versioned release candidate)

> **Superseded by 0.1.0-rc.2 (Phase 18G).** Phase 18F (`8020edb`, Copy ▾ Teams text, empty Daily
> Feature omission) changed shipped code after these artifacts were built. RC.1 was never
> Windows-validated; do not distribute or validate it. The RC.1 files stay in `artifacts/releases/`
> under their own names and hashes.

**Status: built and validated on the build host. Windows validation PENDING**, to be run by the owner
on the affected x64 PC with `docs/phase18e-rc1-windows-checklist.md`. Every Windows item below stays
**NOT EXECUTED** until results come back. They will be filled in here, not inferred.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.1`**, file version `0.1.0.1`, informational `0.1.0-rc.1+3cace7856ad36141eeed98a8d70dbf8c5ff23fed` (read from the packaged `Carowinds.Passdown.Desktop.exe`/`.dll` version resources and the single-file EXE) |
| Source commit (features) | `096bc4a` (Phase 18D: Conjuring retrieval, North Gate/Avenue overrides, build identification) |
| Release/version commit | `3cace78`: version bump, FileVersion, RC numbering rule, two version-only test updates. No functional source change. Artifacts were built from this clean commit |
| About this build | `0.1.0-rc.1 (3cace78)`. The SDK stamps the commit the bytes were built from. That is the version commit, not `096bc4a`, because the version string itself is part of the shipped code |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0.1 (Darwin 27.0.0), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.6 (local `dotnet tool` in a scratch folder, nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (unchanged; no migration; no sync format change) |
| Deterministic tests | Baseline at `096bc4a`: **2536/2536**. Final at `3cace78`: **2537/2537** = 125 Domain + 671 Application + 237 Documents + 752 Infrastructure + 714 Ui together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` alone (`-c Release --filter "Category!=LiveSmoke"`). +1: `ReleaseVersionTests.DesktopAssembly_ReleaseCandidateFileVersionCarriesTheCandidateNumber`. `LaunchIdentityTests.DesktopAssembly_IdentifiesAsTheVisibleProduct_WithAReleaseVersion` updated: it hard-coded `-dev` and now accepts `-dev` or `-rc.N` |
| Build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors (before and after the version change) |
| Superseded | The Phase 18C `Carowinds-Operations-0.1.0-dev-win-x64.zip/.exe` (still in `artifacts/releases/`, different names, not nested in RC.1). Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.1-win-x64.zip` |
| Bytes | 73,446,617 |
| SHA-256 | `28fad2675f1b71ecbcdcaa71503352c55108cc432a9dd23120474c57887bec42` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.1-win-x64\` |
| Content | 282 files / 282 entries, 154 MB uncompressed |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on the staging folder and on the final ZIP
  (282 files each), run by the real `Build-PortableRelease.ps1` under PowerShell 7.6.6 on macOS. The
  C# mirror (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`) also PASSES on the final ZIP. Nothing
  bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** CRC test clean; exactly one top-level folder; no duplicate entries; no
  backslash names; no empty files; no `.DS_Store`/`__MACOSX`/`.pdb`/`.db`/`.log`/`.zip`/`0.1.0-dev`
  names. Extracted to a scratch folder outside `artifacts/`, it is byte-identical (SHA-256 per file)
  to the staging folder. Executable, `README.txt`, `TESTER-GUIDE.txt`, both
  `Enable/Disable Automatic Refresh.cmd`, `tools\Install-/Uninstall-ScheduledRefresh.ps1`,
  `THIRD-PARTY-NOTICES.txt`, `licenses\` (23 files), `LatoFont\OFL.txt` all present.
- **Developer-path scan: PASS.** The checkout path and the build user's name appear in no file
  (UTF-8 and UTF-16). `/Users/`, `/home/` and `C:\Users\` appear in no project-owned file
  (`Carowinds.Passdown.*` binaries and all text files). They appear only in the documented Phase 18B
  vendor exception: `SQLitePCLRaw.batteries_v2.dll`, `SQLitePCLRaw.core.dll` and
  `SQLitePCLRaw.provider.e_sqlite3.dll` (`/Users/eric/dev/SQLitePCL.raw/…`), and
  `MicroCom.Runtime.dll` (`/home/`).
- **Third-party notices: PASS (preserved).** `THIRD-PARTY-NOTICES.txt` and `licenses\` are
  byte-identical to the repository sources, which are unchanged since Phase 18C (`5c0ddf2`). Shipped
  dependency versions in `Carowinds.Passdown.Desktop.deps.json` match the notices table exactly:
  Avalonia 12.1.1, ANGLE 2.1.27548.20260419, .NET/Microsoft.* 10.0.11, SkiaSharp 3.119.4,
  HarfBuzzSharp 8.3.1.3, QuestPDF 2026.7.3, SQLitePCLRaw 2.1.12, CommunityToolkit.Mvvm 8.4.2,
  MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1. `RepositoryNoticeSources_MatchTheRequiredSet_AndAreComplete`
  passes.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.1-win-x64.exe` |
| Bytes | 186,106,700 |
| SHA-256 | `493087311ab65d416278f049c069bb2648f2e5871024c458768684c59b989ab2` |
| Checks | `PE32+ (GUI) x86-64`; carries `0.1.0-rc.1+3cace78…` and file version `0.1.0.1`; no checkout path or user name; only SQLitePCLRaw's vendor `/Users/eric/…` paths and one `/home/` (MicroCom) |
| Status | EXPERIMENTAL. Ships without `THIRD-PARTY-NOTICES.txt`/`licenses\` when distributed alone, so do **not** hand it out independently. The ZIP stays the distribution |

## QuestPDF licence

QuestPDF 2026.7.3 is a shipped dependency (`QuestPDF.dll`, `QuestPdfSkia.dll`, `qpdf.dll`). Its licence
text ships as `licenses\QuestPDF-LICENSE.txt` plus `licenses\QuestPDF-dependencies\`, it is indexed in
`THIRD-PARTY-NOTICES.txt` §6, and the app selects `LicenseType.Community` in code. No legal conclusion
is drawn here.

**DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Windows validation

To be run on the affected x64 PC with `docs/phase18e-rc1-windows-checklist.md`, against the exact hashes
above. That covers the checklist's Step 0 hash check and steps 9–27.

| Item | Result |
|---|---|
| Windows machine record | NOT EXECUTED (pending) |
| Windows Search before RC.1 (stale-copy evidence) | NOT EXECUTED (pending) |
| Direct ZIP launch, second launch, About = `0.1.0-rc.1 (3cace78)`, running folder | NOT EXECUTED (pending) |
| Affected-PC Dashboard (rides-only ERT, existing data) | NOT EXECUTED (pending) |
| Conjuring live retrieval | NOT EXECUTED (pending; automated tests remain the evidence) |
| North Gate / Avenue / both overrides, persistence, Reset | NOT EXECUTED (pending) |
| History freeze of effective gate times | NOT EXECUTED (pending) |
| Classic / Compact / Modern PDFs with override | NOT EXECUTED (pending) |
| US formatting under non-US region | NOT EXECUTED (pending) |
| App log | NOT EXECUTED (pending) |
| Diagnostics export | NOT EXECUTED (pending) |
| Task Scheduler install/run/uninstall from RC.1 folder | NOT EXECUTED (pending) |
| Windows Search after RC.1 | NOT EXECUTED (pending) |
| One-PC Personal Sync | NOT EXECUTED (pending) |
| Two-PC Associate / gate override / Notes / ERT sync | NOT EXECUTED (pending; requires a second PC on RC.1) |
| Single-file EXE launch | NOT EXECUTED (pending) |
| Packaging validator on Windows | NOT EXECUTED (PASS on macOS under PowerShell 7.6.6) |

## Known limitations

- All Windows and two-PC validation above is outstanding.
- Do not mix RC.1 with older builds on synced PCs: pre-RC.1 builds may drop the gate-override fields
  when they re-save an Associate draft.
- Single-file EXE: experimental; no bundled notices.
- QuestPDF distribution licence decision pending.
- Developer build scripts need PowerShell 7 (unchanged from 18C).
- Blocker policy: any shipped-code fix found during Windows validation becomes `0.1.0-rc.2`. These
  RC.1 bytes and hashes are never replaced under the same name.

## Release status

- **Technical: RELEASE BLOCKED, pending Windows validation.** No defect is open. Every build-host gate
  passes (tests, clean Release build, packaging validator, structure, path scan, notices). The
  acceptance gate needs the ZIP to launch on Windows, the Dashboard, overrides, PDFs, logs,
  diagnostics and the scheduler to work there, and none of that has run yet.
- **Distribution: LICENSE DECISION PENDING** (QuestPDF, above).

# Release manifest — 0.1.0-rc.2 (Phase 18G, rebuild of the Phase 18F source)

**Status: built and validated on the build host. Windows validation PENDING**, to be run by the owner
on the affected x64 PC with `docs/phase18g-rc2-windows-checklist.md`. No Windows PC was reachable from
the build session (a Windows 11 ARM64 VMware VM exists on the build Mac but was not running, is not
the affected x64 PC, and has no Teams/OneDrive setup known to this session). Every Windows item below
stays **NOT EXECUTED** until results come back. They will be filled in here, not inferred.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.2`**, file version `0.1.0.2`, informational `0.1.0-rc.2+dbaa7531242bcfecd21b139e007d18d07c6f1021` (read from the packaged `Carowinds.Passdown.Desktop.exe`/`.dll` version resources and the single-file EXE; neither contains `0.1.0-rc.1` or `0.1.0.1`) |
| Source commit (features) | `8020edb` (Phase 18F: Teams copy actions and empty Daily Feature omission) |
| Release/version commit | `dbaa753`: `Directory.Build.props` only (`<Version>` and `<FileVersion>`). No functional source change, no test change. Artifacts were built from this commit |
| About this build | `0.1.0-rc.2 (dbaa753)` |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0 (Darwin 27.0.0), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.6 (local `dotnet tool` in a scratch folder, nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (unchanged; no migration; no sync format change) |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `Carowinds Passdown Daily Refresh` task, sync product identity, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `8020edb`: **2620/2620**. Final at `dbaa753`: **2620/2620** = 125 Domain + 671 Application + 307 Documents + 752 Infrastructure + 727 Ui together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` alone (`-c Release --filter "Category!=LiveSmoke"`). No tests added: the RC rule tests (`ReleaseVersionTests`, `LaunchIdentityTests`) already check the rc.N / file-version pairing |
| Build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors (before and after the version change) |
| Superseded | RC.1 (`…-0.1.0-rc.1-win-x64.zip/.exe`) and the Phase 18C `…-0.1.0-dev-…` artifacts. Both remain in `artifacts/releases/` under different names; neither is nested in RC.2. Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.2-win-x64.zip` |
| Bytes | 73,461,483 |
| SHA-256 | `3b8fdb219755991ab3a401d58218d8c626faf35d696da3974a6cd0df73ce2d22` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.2-win-x64\` |
| Content | 282 files / 282 entries, 154 MB uncompressed |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on the staging folder and on the final ZIP
  (282 files each), run by the real `Build-PortableRelease.ps1` under PowerShell 7.6.6 on macOS. The
  C# mirror (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`) also PASSES on the final ZIP. Nothing
  bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** CRC test clean; exactly one top-level folder; 282 entries, no duplicates;
  no backslash names; no empty files; no `.DS_Store`/`__MACOSX`/`.pdb`/`.db`/`-wal`/`-shm`/`.log`/
  `.zip`/`.pdf`/`.png`/`.dylib`/`.so`/source/test-assembly/`0.1.0-dev`/`rc.1` names. Extracted to a
  scratch folder outside `artifacts/`, it is byte-identical (`diff -rq`) to the staging folder.
  Executable, `README.txt`, `TESTER-GUIDE.txt`, both `Enable/Disable Automatic Refresh.cmd`,
  `tools\Install-/Uninstall-ScheduledRefresh.ps1`, `THIRD-PARTY-NOTICES.txt`, `licenses\` (23 files),
  `LatoFont\OFL.txt`, and the native `e_sqlite3`/`libSkiaSharp`/`libHarfBuzzSharp`/`av_libglesv2`
  all present. The `.exe` is `PE32+ (GUI) x86-64`.
- **Developer-path scan: PASS.** The checkout path, the repository folder name and the build user's
  name appear in no file (UTF-8 and UTF-16). `/Users/`, `/home/` and `C:\Users\` appear in no
  project-owned file. They appear only in the documented Phase 18B vendor exception:
  `SQLitePCLRaw.batteries_v2.dll`, `SQLitePCLRaw.core.dll`, `SQLitePCLRaw.provider.e_sqlite3.dll`
  (`/Users/eric/…`) and `MicroCom.Runtime.dll` (`/home/kekekeks/…`).
- **Third-party notices: PASS (preserved).** `THIRD-PARTY-NOTICES.txt` and `licenses\` are
  byte-identical to the repository sources, unchanged since Phase 18C (`5c0ddf2`). Shipped versions in
  `Carowinds.Passdown.Desktop.deps.json` match the notices table: Avalonia/Avalonia.Fonts.Inter
  12.1.1, ANGLE 2.1.27548.20260419, .NET runtime/Microsoft.Data.Sqlite.Core 10.0.11, SkiaSharp
  3.119.4, HarfBuzzSharp 8.3.1.3, QuestPDF 2026.7.3, SQLitePCLRaw 2.1.12, CommunityToolkit.Mvvm 8.4.2,
  MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1. No license text regenerated.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.2-win-x64.exe` |
| Bytes | 186,155,852 |
| SHA-256 | `156da1bad68bcbca983718682d606b2c1f74c6dcfa8276d77095685590a30578` |
| Checks | `PE32+ (GUI) x86-64`; carries `0.1.0-rc.2+dbaa753…` and file version `0.1.0.2`; no checkout path or user name; only SQLitePCLRaw's `/Users/eric/` and MicroCom's `/home/kekekeks/` vendor paths |
| Status | EXPERIMENTAL. Ships without `THIRD-PARTY-NOTICES.txt`/`licenses\` when distributed alone, so do **not** hand it out independently. The ZIP stays the distribution |

## QuestPDF licence

Unchanged from RC.1: QuestPDF 2026.7.3 shipped, licence texts included, `LicenseType.Community`
selected in code. No legal conclusion is drawn here.

**DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Windows validation

To be run with `docs/phase18g-rc2-windows-checklist.md` against the exact hashes above.

| Item | Result |
|---|---|
| Windows machine record | NOT EXECUTED (pending) |
| Hash check on Windows (ZIP, EXE) | NOT EXECUTED (pending) |
| Windows Search stale-copy check | NOT EXECUTED (pending) — cause of the earlier Dashboard confusion not yet determined |
| Direct ZIP launch, second launch, About = `0.1.0-rc.2 (dbaa753)`, running folder | NOT EXECUTED (pending) |
| Affected-PC Dashboard (rides-only ERT, existing data) | NOT EXECUTED (pending) |
| Conjuring live retrieval, PDF/copy inclusion by SCarowinds selection | NOT EXECUTED (pending; automated tests remain the evidence) |
| North Gate / Avenue / both overrides, persistence, copy, Reset | NOT EXECUTED (pending) |
| History freeze of effective gate times | NOT EXECUTED (pending) |
| Empty Daily Feature — Classic / Compact / Modern | NOT EXECUTED (pending) |
| Copy ▾ menu keyboard/UI | NOT EXECUTED (pending) |
| Real Teams paste — Today's Pricing / Operations Summary / Operations + Pricing | NOT EXECUTED (pending) |
| PDF/copy pricing selection consistency | NOT EXECUTED (pending; automated tests cover the shared model) |
| No-pricing-selected behavior | NOT EXECUTED (pending) |
| Clipboard failure | NOT EXECUTED (pending; optional) |
| Scaling 100% / 125% / 150% | NOT EXECUTED / NOT EXECUTED / NOT EXECUTED (pending) |
| US formatting under non-US region | NOT EXECUTED (pending) |
| App log | NOT EXECUTED (pending) |
| Diagnostics export | NOT EXECUTED (pending) |
| Task Scheduler install/run/uninstall from RC.2 folder | NOT EXECUTED (pending) |
| One-PC Personal Sync | NOT EXECUTED (pending) |
| Two-PC sync | NOT EXECUTED (pending; requires a second PC on RC.2) |
| Single-file EXE launch | NOT EXECUTED (pending) |
| Packaging validator on Windows | NOT EXECUTED (PASS on macOS under PowerShell 7.6.6) |

## Known limitations

- All Windows and two-PC validation above is outstanding, including the first real Teams paste and
  the first DPI-scaling check of the Copy ▾ menu.
- Copy prices use the PDF's `$59.00` / `From $37.00` form; products with no price are left out of the
  copy while the PDF prints "-". Both intentional (`docs/phase18f-teams-copy.md`).
- Do not mix RC.2 with older builds on synced PCs.
- Single-file EXE: experimental; no bundled notices.
- QuestPDF distribution licence decision pending.
- Developer build scripts need PowerShell 7 (unchanged from 18C).
- Blocker policy: any shipped-code fix found during Windows validation becomes `0.1.0-rc.3`. These
  RC.2 bytes and hashes are never replaced under the same name.

## Release status

- **Technical: RELEASE BLOCKED, pending Windows validation.** No defect is open. Every build-host gate
  passes (tests, clean Release build, packaging validator, ZIP structure, path scan, notices). The
  acceptance gate needs the ZIP to launch on Windows and the Dashboard, overrides, PDFs, clipboard,
  Teams paste, scaling, logs, diagnostics and the scheduler to work there. None of that has run yet.
- **Distribution: LICENSE DECISION PENDING** (QuestPDF, above).

## Unreleased development since RC.2 — Phase 19A

Phase 19A (automatic refresh and live local propagation, `docs/phase19a-automatic-refresh.md`) changes
shipped code after RC.2. The RC.2 artifacts, hashes and Windows validation above are unchanged and
still describe the pre-19A candidate; they were not rebuilt. A build containing Phase 19A must be
packaged as a new release candidate (`0.1.0-rc.3` unless sequencing changes) and validated with the
Phase 19A Windows checklist. Schemas unchanged: DB 15, Draft 4, Snapshot 2, SyncSchemaVersion 1.
Deterministic tests at the Phase 19A commit: 2735/2735; Release build 0 warnings / 0 errors.

## Unreleased development since RC.2 — Phase 19B

Phase 19B (seasonal catalog and product lifecycle, `docs/phase19b-seasonal-catalog.md`) is a further
shipped-code change after RC.2: ended promotions and the end of SCarowinds no longer produce false
refresh errors, retired products leave current presentation automatically, and non-exclusive
Carowinds/SCarowinds/WinterFest season tags are supported internally. RC.2 artifacts are unchanged and
RC.3 was not packaged in this phase. The next candidate containing Phase 19A + 19B should be
`0.1.0-rc.3`, validated with the Phase 19A Windows checklist plus
`docs/phase19b-seasonal-catalog.md` §13. Schemas unchanged: DB 15, Draft 4, Snapshot 2,
SyncSchemaVersion 1. Deterministic tests at the Phase 19B commit: 2780/2780; Release build
0 warnings / 0 errors.

# Release manifest — 0.1.0-rc.3 (Phase 19C: Phase 19A automatic refresh + Phase 19B seasonal catalog)

**Status: built and validated on the build host. Windows validation PENDING.** The owner should run it
on the affected x64 PC with `docs/phase19c-rc3-windows-checklist.md`. No Windows PC was reachable
from the build session. Every Windows item below stays **NOT EXECUTED** until results come back; they
will be filled in here, not inferred.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.3`**, file version `0.1.0.3`, informational `0.1.0-rc.3+1744320378628fc9a00e402551e169feb06b7697`. Read from the packaged `Carowinds.Passdown.Desktop.dll`, the apphost `.exe` version resource and the single-file EXE; none contains `0.1.0-rc.2`, `0.1.0-rc.1` or `0.1.0.2` |
| Source commits (features) | `2c610fb` (Phase 19A) and `97de18f` (Phase 19B) |
| Release/version commit | `1744320`: `Directory.Build.props` only (`<Version>` and `<FileVersion>`). Artifacts were built from this commit |
| About this build | `0.1.0-rc.3 (1744320)` |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0.1 (Darwin), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 (local `dotnet tool` in a scratch folder, nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 (unchanged; no migration; no sync format change) |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `Carowinds Passdown Daily Refresh` task, database file name, sync product identity, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `97de18f`: **2780/2780**. Final at `1744320`: **2780/2780** = 125 Domain + 779 Application + 762 Infrastructure + 309 Documents + 767 Ui together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` alone (`-c Release --filter "Category!=LiveSmoke"`). No tests added |
| Build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors (before and after the version change) |
| Superseded | RC.2, RC.1 and the `0.1.0-dev` artifacts stay in `artifacts/releases/` under their own names, unmodified. RC.2 hashes were re-verified after this build: unchanged. None of them is nested in RC.3. Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.3-win-x64.zip` |
| Bytes | 73,547,365 |
| SHA-256 | `6f08a796473abee5d94efe4e7e0cee47b6830537b1856d023a962efc929980eb` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.3-win-x64\` |
| Content | 282 files / 282 entries, 154.2 MB uncompressed |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on the staging folder and on the final ZIP
  (282 files each), run by the real `Build-PortableRelease.ps1` under PowerShell 7.6.7 on macOS. The
  C# mirror (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`) also PASSES on the final ZIP. Nothing
  bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** CRC test clean. Exactly one top-level folder. 282 entries, no duplicates,
  no backslash names, no empty files. No `.DS_Store`, `__MACOSX`, `.pdb`, `.db`, `-wal`, `-shm`,
  `.log`, `.zip`, `.pdf`, `.png`, `.dylib`, `.so`, source files, test assemblies, or
  `0.1.0-dev`/`rc.1`/`rc.2` names. Extracted to a scratch folder outside `artifacts/`, it is
  byte-identical (`diff -rq`) to the staging folder. The executable, `README.txt`, `TESTER-GUIDE.txt`,
  both `Enable/Disable Automatic Refresh.cmd` files, the `tools\` scheduler scripts,
  `THIRD-PARTY-NOTICES.txt`, `licenses\` and the native libraries are all present. The `.exe` is
  `PE32+ (GUI) x86-64`.
- **Developer-path scan: PASS.** The checkout path, the repository folder name and the build user's
  name appear in no file (UTF-8 and UTF-16), including the single-file EXE. `/Users/` and `/home/`
  appear only in the documented Phase 18B vendor exceptions: `SQLitePCLRaw.batteries_v2.dll`,
  `SQLitePCLRaw.core.dll` and `SQLitePCLRaw.provider.e_sqlite3.dll` (`/Users/eric`), and
  `MicroCom.Runtime.dll` (`/home/kekekeks`). `C:\Users\` appears nowhere.
- **Third-party notices: PASS (preserved).** `THIRD-PARTY-NOTICES.txt` and `licenses\` are
  byte-identical to the repository sources, unchanged since Phase 18C (`5c0ddf2`). Shipped
  `deps.json` versions match the notices table exactly: Avalonia/Avalonia.Fonts.Inter 12.1.1, ANGLE
  2.1.27548.20260419, .NET runtime/Microsoft.Data.Sqlite.Core 10.0.11, SkiaSharp 3.119.4,
  HarfBuzzSharp 8.3.1.3, QuestPDF 2026.7.3, SQLitePCLRaw 2.1.12, CommunityToolkit.Mvvm 8.4.2,
  MicroCom.Runtime 0.11.6, Tmds.DBus.Protocol 0.94.1. No licence text was regenerated.
- **Known packaging note (docs only, not a code defect):** the `TESTER-GUIDE.txt` inside this ZIP
  still describes the Dashboard "Refresh" button that Phase 19A removed (step 3). Under the
  never-rebuild rule the RC.3 bytes are kept. The repository copy was corrected after packaging, so
  the next RC ships the fixed text. Testers are told to ignore that sentence in the checklist.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.3-win-x64.exe` |
| Bytes | 186,606,412 |
| SHA-256 | `915cc5488c734299d80a236a639808932e5006297a3906dea0164680bcc4ff72` |
| Checks | Built with the Phase 15F single-file command. Carries `0.1.0-rc.3+1744320…` and file version `0.1.0.3`. No checkout path or user name; only the SQLitePCLRaw `/Users/eric` and MicroCom `/home/kekekeks` vendor paths |
| Status | EXPERIMENTAL. Ships without `THIRD-PARTY-NOTICES.txt`/`licenses\` when distributed alone, so do **not** hand it out independently. The ZIP stays the distribution |

## QuestPDF licence

**Technical status:** QuestPDF 2026.7.3 is shipped, its licence texts are included, and
`LicenseType.Community` is selected in code. No legal conclusion is drawn here.

**DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Build-host evidence for Phase 19A / 19B behaviour (not Windows)

| Area | Evidence |
|---|---|
| Startup background refresh, 4-hour coordinator, startup fresh-skip, Deep Refresh clock reset, today/future auto-check, past-date silence, freshness indicator, offline failure state | Phase 19A deterministic tests (part of 2780) |
| Dashboard reactive propagation (attendance, pre-solds, ERT, gate overrides and resets) | Phase 19A deterministic tests |
| Duo ended offer | Live 2026-10-15: the official calendar answers "No dates found" for `SUBAF50`. The opt-in live test passes (result is NotPublished, not Error). Duo card absent from `daily-tickets` |
| Confirmed absence (2 successful misses), Error never counts, reactivation, SCarowinds → WinterFest, season overlap, History immutability, PDF/copy consistency, empty seasonal section | `SeasonalCatalogLifecycleTests`, `AssociateTeamsTextFormatterTests`, `AssociateViewModelTests` (PASS BY SIMULATION) |
| No hard-coded season dates | `ProductSeasonClassifier` takes no date input. Availability comes only from official source results |

## Windows validation

To be run with `docs/phase19c-rc3-windows-checklist.md` against the exact hashes above.

| Item | Result |
|---|---|
| Windows machine record, hash check (ZIP, EXE) | NOT EXECUTED (pending) |
| Windows Search stale-copy check | NOT EXECUTED (pending) |
| Direct ZIP launch, second launch, About = `0.1.0-rc.3 (1744320)`, running folder | NOT EXECUTED (pending) |
| Startup background refresh / duplicate suppression / 4-hour coordinator | NOT EXECUTED (pending) |
| Dashboard propagation: projected, pre-solds, ERT, Avenue, North Gate, reset | NOT EXECUTED (pending) |
| Today / future / past auto-check, rapid date switching | NOT EXECUTED (pending) |
| Check Online, Deep Refresh, freshness indicator | NOT EXECUTED (pending) |
| Offline startup, sleep/resume | NOT EXECUTED (pending) |
| Task Scheduler coexistence from the RC.3 folder | NOT EXECUTED (pending) |
| Duo no false failure; retirement after two checks | NOT EXECUTED on Windows (live source behaviour verified on the build host) |
| SCarowinds current behaviour, Conjuring, PDF/copy by selection | NOT EXECUTED (pending) |
| SCarowinds end-of-season, WinterFest | NOT EXECUTED LIVE: PASS BY SIMULATION |
| Retired-product presentation, dated NotPublished vs global | NOT EXECUTED (pending; PASS BY SIMULATION) |
| Classic / Compact / Modern PDF | NOT EXECUTED (pending) |
| Teams paste: pricing, operations, both | NOT EXECUTED (pending) |
| History immutability, old-draft compatibility | NOT EXECUTED (pending; PASS BY SIMULATION) |
| One-PC / two-PC Personal Sync | NOT EXECUTED / NOT EXECUTED (two-PC needs a second RC.3 PC) |
| App log, diagnostics export | NOT EXECUTED (pending) |
| Scaling 100% / 125% / 150%, US formatting | NOT EXECUTED (pending) |
| Single-file EXE launch | NOT EXECUTED (pending) |
| Packaging validator on Windows | NOT EXECUTED (PASS on macOS under PowerShell 7.6.7) |

## Known limitations

- All Windows and two-PC validation is outstanding, including the first Windows run of automatic
  refresh, reactive Dashboard updates and the lifecycle changes.
- The shipped `TESTER-GUIDE.txt` still mentions the removed Dashboard Refresh button (docs only;
  fixed in source for the next RC).
- Live SCarowinds end-of-season and WinterFest arrival cannot be observed yet; they are simulated
  only.
- Season tags are internal (not shown in the UI). Event products whose names do not mention the
  event stay unclassified.
- Single-file EXE: experimental; no bundled notices. QuestPDF distribution licence decision pending.
- Do not mix RC.3 with older builds on synced PCs.
- Blocker policy: any shipped-code fix found during Windows validation becomes `0.1.0-rc.4`. These
  RC.3 bytes and hashes are never replaced under the same name.

## Release status

- **Technical: RELEASE BLOCKED, pending Windows validation.** No defect is open. Every build-host gate
  passes: tests, clean Release build, packaging validator, ZIP structure, path scan, notices, and the
  live Duo source behaviour. The acceptance gate needs the ZIP to launch on Windows and the automatic
  refresh, reactive Dashboard, auto-check, freshness indicator, lifecycle presentation, PDF/Teams
  copy and the scheduler to work there. None of that has run yet.
- **Distribution: DISTRIBUTION LICENSE DECISION PENDING** (QuestPDF, above).
