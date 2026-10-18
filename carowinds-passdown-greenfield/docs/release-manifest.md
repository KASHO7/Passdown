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

## Unreleased development since RC.3 — Phase 20A

Phase 20A (spell check and Season Pass price history, `docs/phase20a-spellcheck-price-history.md`)
changes shipped code after RC.3. The RC.3 artifacts, hashes and Windows validation above are unchanged
and still describe the pre-20A candidate; they were not rebuilt. RC.3 was already built from `1744320`,
so a build containing Phase 20A must be packaged as **`0.1.0-rc.4`** and validated with the RC.3
checklist plus the Phase 20A checklist. Schema change: **DB 16** (new `season_price_history` table,
one-time backfill); Draft 4, Snapshot 2, SyncSchemaVersion 1 unchanged. Older builds refuse a DB-16
profile. Deterministic tests at the Phase 20A commit: 2893/2893; Release build 0 warnings / 0 errors.

# Release manifest — 0.1.0-rc.4 (Phase 20B: Phase 20A spell check + Season Pass price history)

**Status: built and validated on the build host. Windows validation PENDING.** No Windows PC was
reachable from the build session. Run `docs/phase20b-rc4-windows-checklist.md` on the affected x64
PC — **backup first** (§0). Every Windows item stays **NOT EXECUTED** until results come back.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.4`**, file version `0.1.0.4`, informational `0.1.0-rc.4+67a5dd6417eeb887d769b6011c9971f1770142ba` (read from the packaged `.exe`/`Desktop.dll` version resources and the single-file EXE; none contains `0.1.0-rc.3` or `0.1.0.3`) |
| Source commit (features) | `588cd93` (Phase 20A) |
| Release/version commit | `67a5dd6`: `Directory.Build.props` only. Artifacts were built from this commit |
| About this build | `0.1.0-rc.4 (67a5dd6)` |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0.1 (Darwin), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 (local `dotnet tool` in a scratch folder) |
| DB / Draft / Snapshot / SyncSchemaVersion | **16** / 4 / 2 / 1. DB 15 → 16 adds `season_price_history` with a one-time backfill. RC.3 and older refuse a DB-16 profile |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `Carowinds Passdown Daily Refresh`, database file name, sync product identity, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `588cd93`: **2893/2893**. Final at `67a5dd6`: **2893/2893** = 136 Domain + 842 Application + 773 Infrastructure + 309 Documents + 795 Ui together + 26 + 10 + 2 window classes alone. No tests added this phase |
| Build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors |
| Superseded | RC.3/RC.2/RC.1/dev artifacts untouched in `artifacts/releases/` (RC.3 hashes re-verified unchanged after this build). None nested in RC.4. Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.4-win-x64.zip` |
| Bytes | 73,696,526 |
| SHA-256 | `55f0babb84b82bbc0e3498806b1140916384bd6bb42e8a22a693fd6ed469b3fb` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.4-win-x64\` |
| Content | 282 files / 282 entries, 154.6 MB uncompressed — the same file list as RC.3 |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on staging and on the final ZIP (282 files
  each), run by the real `Build-PortableRelease.ps1`. C# mirror (`CAROWINDS_RELEASE_ZIP=…
  --filter RealReleaseZip`): PASS. Nothing bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** CRC clean; one top-level folder; no duplicates, backslash names or empty
  files; no `.DS_Store`, `__MACOSX`, `.pdb`, `.db`, `-wal`, `-shm`, `.log`, `.zip`, `.pdf`, `.png`,
  `.dylib`, `.so`, source, test assemblies, or dev/rc.1–rc.3 names. Extracted copy `diff -rq`-identical
  to staging. Executable (`PE32+ (GUI) x86-64`), README, TESTER-GUIDE (now without the removed
  Dashboard Refresh button), both `.cmd` helpers, `tools\` scheduler scripts, notices, `licenses\`
  present.
- **Developer-path scan: PASS.** Checkout path, repository folder name and build user name appear in
  no file (UTF-8/UTF-16), including the EXE. `/Users/` only in the documented SQLitePCLRaw vendor
  DLLs, `/home/` only in `MicroCom.Runtime.dll`; `C:\Users\` nowhere.
- **Third-party notices: PASS (preserved).** `THIRD-PARTY-NOTICES.txt` and `licenses\` byte-identical
  to the repository (unchanged since `5c0ddf2`). The shipped `deps.json` package set is **identical to
  RC.3** (44 packages, none added or removed): Phase 20A added **no** spell-check package or
  dictionary file — spell checking uses the Windows Spell Checking API through built-in COM interop.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.4-win-x64.exe` |
| Bytes | 187,097,932 |
| SHA-256 | `2f3b1b922fbe222c5216a83e5c3f450ca6a3a4a1dccef93d5679ef69113cf070` |
| Checks | Phase 15F single-file command. Carries `0.1.0-rc.4+67a5dd6…` and `0.1.0.4`. Only the vendor `/Users/eric`, `/home/kekekeks` paths |
| Status | EXPERIMENTAL. No notices when distributed alone — do **not** hand it out independently |

## QuestPDF licence

QuestPDF 2026.7.3 shipped with its licence texts; `LicenseType.Community` selected in code. No legal
conclusion drawn. **DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Build-host evidence for Phase 20A behaviour (not Windows)

| Area | Evidence |
|---|---|
| DB 15 → 16 on real data | A **copy** of the macOS dev profile DB (schema 15; live file never opened, its timestamp unchanged) migrated to 16. `season_price_history` created; 11 tracked products, 17 backfilled states from `product_history` (first observations 2026-08-22, changes seen 2026-10-15, e.g. All Season Fast Lane $499 → $599, 2027 Gold Pass $89 sale → $99). Finalized History snapshot + frozen PDF, drafts, catalog and product_history byte/row-identical before and after |
| Price history pipeline | Isolated scratch profile, real migrations + real `SeasonPassCatalogSource` on fixture HTML: baseline; 2 repeats → no new row, last confirmed advanced; network error → nothing written; $89 → $95 (+$6.00, +6.7%); repeat; $95 → $85 (-$10.00, -10.5%); Prestige missing twice → retired, its history intact and listed with "Include retired" |
| Spell check UI | Headless Skia render: underlines exactly under "opne"/"runing"; ERT, 9:00, Carowinds, WinterFest, Copperhead not flagged; suggestions returned. Uses a word-list backend — the Windows COM backend cannot run on macOS |
| Matrix (increase/decrease/sale/Exact↔From/error/NotPublished/reactivation/rename/concurrency/rollback) | PASS BY TEST — `SeasonPriceHistoryReconciliationTests`, `SeasonPriceHistorySqliteTests`, `Migration0016BackfillTests`, `PriceHistoryTests`, `SpellCheck*Tests` (part of 2893) |

## Windows validation

To be run with `docs/phase20b-rc4-windows-checklist.md` against the hashes above.

| Item | Result |
|---|---|
| Live-profile backup (`%LOCALAPPDATA%\Carowinds Passdown`) | NOT EXECUTED (required first) |
| Hash check, launch, second launch, About `0.1.0-rc.4 (67a5dd6)` | NOT EXECUTED |
| DB 15 → 16 migration on the real profile, data survives | NOT EXECUTED (PASS on build-host copy) |
| Spell check: underline, correct words, domain words, covered/excluded fields | NOT EXECUTED |
| Suggestions, Ignore, Add to dictionary, keyboard menu | NOT EXECUTED |
| Non-blocking (autosave/PDF/finalize/Teams/shutdown), privacy, performance | NOT EXECUTED (PASS BY TEST for non-blocking) |
| Spell-check failure behavior | NOT EXECUTED (PASS BY TEST) |
| Price History UI, row buttons, scope, backfill, baseline, search, retired | NOT EXECUTED (PASS on build host) |
| Repeat-price dedup via Check Online / Deep Refresh / automatic refresh | NOT EXECUTED (PASS on build host + tests) |
| Increase/decrease/sale/Exact↔From/error/NotPublished/reactivation/rename | PASS BY TEST (runtime NOT EXECUTED) |
| Phase 19A regressions (Dashboard propagation, auto-check, freshness) | NOT EXECUTED (tests pass) |
| Phase 19B regressions (Duo, SCarowinds, Conjuring) | NOT EXECUTED (tests pass) |
| PDF Classic/Compact/Modern, Teams copy | NOT EXECUTED (tests pass) |
| History immutability | NOT EXECUTED (PASS on build-host DB copy) |
| Personal Sync one-PC / two-PC | NOT EXECUTED / NOT EXECUTED |
| App log, diagnostics | NOT EXECUTED |
| Scaling 100/125/150%, keyboard/accessibility, non-US region | NOT EXECUTED |
| Single-file EXE launch | NOT EXECUTED |

## Known limitations

- All Windows validation is outstanding — most importantly the Windows spell-check COM path (never
  executed anywhere yet) and the real-profile 15 → 16 migration.
- Upgrading is one-way for older builds: keep the pre-RC.4 backup to return to RC.3.
- Price history is local per PC (not synced); a PC that is off during a short sale never records it.
- Backfilled intervals' "last confirmed" can be earlier than the true last check (the old table only
  recorded changes).
- Spell check: Windows only; `en-US` then the Windows UI language; Ignore is per session.
- Single-file EXE experimental; QuestPDF distribution decision pending; two-PC sync pending.
- Blocker policy: any shipped-code fix found during Windows validation becomes `0.1.0-rc.5`.

## Release status

- **Technical: RELEASE BLOCKED, pending Windows validation.** No defect is open and every build-host
  gate passes (tests, clean Release build, packaging validator, ZIP structure, path scan, notices,
  migration on a real-data copy). The acceptance gate needs the ZIP launched on Windows, the real
  15 → 16 migration, Windows spell checking and the Price History screen exercised there, and the
  19A/19B/PDF/Teams regressions re-run. None of that has run yet.
- **Distribution: DISTRIBUTION LICENSE DECISION PENDING** (QuestPDF).

---

# Unreleased after 0.1.0-rc.4 — Phase 20C (next candidate: `0.1.0-rc.5`, not yet packaged)

Phase 20C changed shipped code after RC.4 was built. RC.4 artifacts are unchanged and were not rebuilt;
the next package must be versioned **`0.1.0-rc.5`**. Details: `docs/phase20c-historical-backfill.md`.

| | |
|---|---|
| Changes | Spell-check underline no longer lingers over other screens after navigating away (RC.4 Windows finding); current-year historical park-hours backfill (background, lowest priority, resumable, ~0.1–0.2 MB/year); Operations Calendar live-updates from local changes; historical status line under the Calendar month |
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 — unchanged (no migration) |
| Deterministic tests | **2952/2952** = 136 Domain + 873 Application + 309 Documents + 782 Infrastructure + 807 Ui together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` alone (`-c Release --filter "Category!=LiveSmoke"`). The new real-Window class joins the documented run-alone partition (same headless thread-affinity issue when mixed) |
| Build | `dotnet build CarowindsPassdown.slnx -c Release` — 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in `docs/phase20c-historical-backfill.md` (S1–S6, H1–H11) plus the outstanding RC.4 list above |

**Packaged as `0.1.0-rc.5` in Phase 20D (below).**

# Release manifest — 0.1.0-rc.5 (Phase 20D: Phase 20C spell-check navigation + current-year history)

**Status: built and validated on the build host. Windows validation PENDING.** No Windows PC was
reachable from the build session. Run `docs/phase20d-rc5-windows-checklist.md` on the x64 PC used for
RC.4 — **backup first** (§0). Every Windows item stays **NOT EXECUTED** until results come back.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.5`**, file version `0.1.0.5`, informational `0.1.0-rc.5+10bf37dcd5f7b5b2d678903d4dc18c2ca30089c5` (read from the packaged `.exe`/`Desktop.dll` version resources and the single-file EXE; none contains `0.1.0-rc.4` or `0.1.0.4`) |
| Source commit (features) | `6498aa9` (Phase 20C) |
| Release/version commit | `10bf37d`: `Directory.Build.props` only. Artifacts were built from this commit |
| About this build | `0.1.0-rc.5 (10bf37d)` |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0.1 (Darwin 27.0.0), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 (local `dotnet tool` reused from the RC.4 scratch folder; a fresh download failed in this session) |
| DB / Draft / Snapshot / SyncSchemaVersion | **16** / 4 / 2 / 1 — unchanged from RC.4, no migration. RC.4 can still open an RC.5 profile |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `Carowinds Passdown Daily Refresh`, database file name, sync product identity, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `6498aa9`: **2952/2952**. Final at `10bf37d`: **2952/2952** = 136 Domain + 873 Application + 782 Infrastructure + 309 Documents + 807 Ui together + 26 + 10 + 2 + 7 real-Window classes alone. No tests added this phase |
| Build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors (before and after the version commit) |
| Superseded | RC.4/RC.3/RC.2/RC.1/dev artifacts untouched in `artifacts/releases/` (RC.4 hashes re-verified unchanged after this build). None nested in RC.5. Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.5-win-x64.zip` |
| Bytes | 73,739,645 |
| SHA-256 | `570c00df05a933a383ed69b7508ecd5e33ea22c5ee21096e0062e1c4145e3c25` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.5-win-x64\` |
| Content | 282 files / 282 entries, 162,179,275 bytes uncompressed — the same file list as RC.4 |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on staging and on the final ZIP (282 files
  each), run by the real `Build-PortableRelease.ps1`. C# mirror (`CAROWINDS_RELEASE_ZIP=…
  --filter RealReleaseZip`): PASS. Nothing bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** CRC clean; one top-level folder; no duplicates, backslash names or empty
  files; no `.DS_Store`, `__MACOSX`, `.pdb`, `.db`, `-wal`, `-shm`, `.log`, `.zip`, `.pdf`, `.png`,
  `.dylib`, `.so`, source, test assemblies, or dev/rc.1–rc.4 names. Extracted copy `diff -rq`-identical
  to staging. Executable (`PE32+ (GUI) x86-64`), `README.txt`, `TESTER-GUIDE.txt`, both `.cmd` helpers,
  `tools\` scheduler scripts, notices, `licenses\` present.
- **Developer-path scan: PASS.** Checkout path, repository folder name and build user name appear in
  no file (UTF-8/UTF-16), including the EXE. `/Users/` only in the documented SQLitePCLRaw vendor
  DLLs (`/Users/eric`), `/home/` only in `MicroCom.Runtime.dll` (`/home/kekekeks`); `C:\Users\` nowhere.
- **Third-party notices: PASS (preserved).** `THIRD-PARTY-NOTICES.txt` and `licenses\` byte-identical
  to the repository (unchanged since `5c0ddf2`). Shipped `deps.json` package set **identical to RC.4**
  (44 packages): Phase 20C added no dependency, so notice needs are unchanged.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.5-win-x64.exe` |
| Bytes | 187,364,172 |
| SHA-256 | `2bbf540cac7f4b1dc1f499c26829e6a21457440fa04bc622aee2e3a943a2c5ad` |
| Checks | Phase 15F single-file command. Carries `0.1.0-rc.5+10bf37d…` and `0.1.0.5`. Only the vendor `/Users/eric`, `/home/kekekeks` paths |
| Status | EXPERIMENTAL. No notices when distributed alone — do **not** hand it out independently |

## QuestPDF licence

QuestPDF 2026.7.3 shipped with its licence texts; `LicenseType.Community` selected in code. No legal
conclusion drawn. **DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Build-host evidence for Phase 20C behaviour (not Windows)

From `docs/phase20c-historical-backfill.md`; nothing new was run against the live profile in 20D.

| Area | Evidence |
|---|---|
| Spell-check lifecycle | `SpellCheckNavigationLifecycleTests` (7, real headless Window): underline removed on navigation, pending check cancelled, re-check on return, no duplicate handlers. Windows COM backend never executed |
| Backfill, live source | Isolated scratch-DB harness, real feed, production pacing: 206 requests / 295 s, 205 stored (140 Verified, 65 Closed), boundary 2026-03-24 found with one probe, simulated Check Online acquired the gate in 0 ms, second launch = 1 request and no new rows. Real profile DB untouched |
| Storage | 205 dates: 155,648 → 274,432 B (+118,784 B, ≈579 B/date); full-year projection ≈205 KB. Guarded by `Phase20CHistoricalBackfillSqliteTests` (< 2 KB/date, no raw venue marker, table/index count unchanged) |
| Priority, scope, resume, no-fabrication, offline, scheduled task, isolation | PASS BY TEST (Phase 20C +59 tests, part of 2952) |

## Windows validation

To be run with `docs/phase20d-rc5-windows-checklist.md` against the hashes above.

| Item | Result |
|---|---|
| Live-profile backup (`%LOCALAPPDATA%\Carowinds Passdown`), DB size before | NOT EXECUTED (required first) |
| Windows environment | NOT EXECUTED |
| Hash check, launch, second launch, About `0.1.0-rc.5 (10bf37d)`, running folder | NOT EXECUTED |
| Spell-check navigate-away (Dashboard / Calendar / Products) | NOT EXECUTED (PASS BY TEST) |
| Pending-check (< 350 ms) navigation | NOT EXECUTED (PASS BY TEST) |
| Navigate back re-checks; repeated navigation | NOT EXECUTED (PASS BY TEST) |
| Price History overlay underline (S6) | NOT EXECUTED — judgement required on Windows |
| Spell-check RC.4 regression (quick) | NOT EXECUTED |
| Current-year scope, newest first, month promotion | NOT EXECUTED (PASS BY TEST) |
| Current/future priority (Check Online, Deep Refresh yield) | NOT EXECUTED (PASS BY TEST; 0 ms gate acquisition on build host) |
| Calendar live update, historical status line | NOT EXECUTED (PASS BY TEST) |
| Operational freshness isolation | NOT EXECUTED (PASS BY TEST) |
| Resume / completed-date dedup | NOT EXECUTED (PASS on build host: 2nd run 1 request) |
| Source boundary / no fabrication | NOT EXECUTED (PASS on build host: boundary 2026-03-24, nothing stored for 404 dates) |
| Storage: DB before / after / dates / bytes per date | NOT EXECUTED (build host: 205 dates, ≈116 KB, ≈579 B/date) |
| Raw response storage | NOT EXECUTED (PASS BY TEST) |
| Responsiveness, shutdown during backfill | NOT EXECUTED |
| Offline backfill | NOT EXECUTED (PASS BY TEST) |
| 6 AM scheduled task current-only | NOT EXECUTED (PASS BY TEST) |
| Phase 19A regressions | NOT EXECUTED (tests pass) |
| Phase 19B regressions (Duo, SCarowinds, Conjuring) | NOT EXECUTED (tests pass) |
| Phase 20A Season Pass price history | NOT EXECUTED (tests pass) |
| PDF Classic/Compact/Modern, Teams copy | NOT EXECUTED (tests pass) |
| History immutability | NOT EXECUTED (tests pass) |
| Personal Sync impact (history not synced, SyncSchemaVersion 1) | PASS BY TEST; one-PC / two-PC runtime NOT EXECUTED |
| App log, diagnostics | NOT EXECUTED (PASS BY TEST for the diagnostics section) |
| Scaling 100/125/150%, keyboard/accessibility | NOT EXECUTED |
| Single-file EXE launch | NOT EXECUTED |

## Known limitations

- All Windows validation is outstanding, including the still-unexecuted Windows spell-check COM path
  and the RC.4 checklist items not yet recorded in this manifest (only the lingering-underline finding came back, fixed in 20C).
- Price History overlay: an underline in a visible Associate field may draw above the dimmed overlay
  (known, intentionally not fixed before RC.5; judge in S6).
- Official park-hours history before the feed's retention window (≈2026-03-24 and earlier when
  observed) cannot be obtained; those dates stay empty. The window is observed, not documented.
- Historical rows and Season Pass price history are local per PC (not synced).
- Single-file EXE experimental; QuestPDF distribution decision pending; two-PC sync pending.
- Blocker policy: any shipped-code fix found during Windows validation becomes `0.1.0-rc.6`.

## Release status

- **Technical: RELEASE BLOCKED, pending Windows validation.** No defect is open and every build-host
  gate passes (tests, clean Release build, packaging validator, ZIP structure, path scan, notices,
  version resources). The acceptance gate needs the ZIP launched on Windows, the spell-check
  navigation fix and the Price History overlay judged there, and the historical backfill, priority,
  resume, storage, scheduled task and 19A/19B/20A/PDF/Teams regressions exercised there.
- **TWO-PC SYNC VALIDATION PENDING.**
- **Distribution: DISTRIBUTION LICENSE DECISION PENDING** (QuestPDF).

# Unreleased development — Phase 21A (after RC.5)

Not a release. Phase 21A (`docs/phase21a-pricing-historical-integrity.md`) changes shipped code:
Military/First Responder exact dated pricing, historical selected-date pricing rule, pass-year identity
guard, Dashboard Open Passdown → today. RC.5 artifacts are unchanged. The next packaged candidate must
be **`0.1.0-rc.6`**; `Directory.Build.props` still says `0.1.0-rc.5` until that packaging phase.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged) |
| Deterministic tests | **3040/3040** (baseline 2952 at `6cd8d96`; +88 Phase 21A) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 21A doc |

# Unreleased development — Phase 21B (after RC.5, after Phase 21A)

Not a release. Phase 21B (`docs/phase21b-associate-navigation-toolbar.md`) changes shipped UI code:
Associate date header (‹ date › Today, Today/Yesterday/Tomorrow label), built-in `Calendar` popup with
click/Enter commit and Escape/outside-click dismissal, toolbar regrouped (Check Online, Create PDF ·
style, Copy ▾, More ▾ with PDF Style and Season Price History), Export Diagnostics moved to Settings →
Diagnostics. RC.5 artifacts are unchanged. The next packaged candidate is still **`0.1.0-rc.6`**;
`Directory.Build.props` still says `0.1.0-rc.5`. `TESTER-GUIDE.txt` and `README-portable-release.md`
now point to Settings → Diagnostics.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Deterministic tests | **3092/3092** (baseline 3040 at `50793de`; +52 Phase 21B) = 154 Domain + 908 Application + 804 Infrastructure + 311 Documents + 863 Ui together + 26 `OperationsCalendarViewTests` + 17 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 21B doc |

# Unreleased development — Phase 21C (after RC.5, after Phase 21B)

Not a release. Phase 21C (`docs/phase21c-official-show-schedules.md`) changes shipped code: official
date-specific show schedules (FIMS operating-hours feed `shows[]`) on the Dashboard ("Today's Shows"),
Operations Calendar (selected date), Associate (optional Shows section with per-show selection), and
Classic/Compact/Modern PDFs; finalized snapshots freeze printed shows. Teams Copy is unchanged (no
show information). RC.5 artifacts are unchanged. The next packaged candidate is still
**`0.1.0-rc.6`**; `Directory.Build.props` still says `0.1.0-rc.5`.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration — additive optional `Shows` properties omitted when unused) |
| Upgrade implications | New `dated_results` category `ShowSchedule` (like 21A's `DatedMilitaryFirstResponder`): do not run RC.5 against a profile used by RC.6 — **back up `%LOCALAPPDATA%\Carowinds Passdown` before upgrading**. Personal Sync: upgrade every PC sharing a sync folder before using Shows (an older build drops a draft's `Shows` if it re-saves that date). |
| Deterministic tests | **3227/3227** (baseline 3092 at `2452935`; +135 Phase 21C) = 163 Domain + 949 Application + 832 Infrastructure + 346 Documents + 885 Ui together + 26 `OperationsCalendarViewTests` + 17 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`) |
| Live smoke (opt-in) | `ShowScheduleSourceLiveSmokeTests` + `ParkHoursSourceLiveSmokeTests` passed 2026-10-16 from the dev host |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 21C doc |

# Unreleased development — Phase 21D (after RC.5, after Phase 21C)

Not a release. Phase 21D (`docs/phase21d-daily-feature-math.md`) changes shipped code: Daily Feature
math — a "∑ Math" symbol/template picker, `\( … \)` / `\[ … \]` LaTeX-style syntax (documented subset),
a live Associate preview, math-aware spell checking for that field, and vector formula rendering in
the Classic/Compact/Modern PDFs through one shared parser/layout pipeline. Ordinary Daily Feature text
is unchanged. Teams Copy is unchanged (no Daily Feature, no LaTeX, no shows). Adds the bundled
**STIX Two Math** font (SIL OFL 1.1, embedded in `Carowinds.Passdown.Documents.dll`, +1.5 MB) and its
notice (`THIRD-PARTY-NOTICES.txt` §8, `licenses\STIXTwoMath-OFL.txt`). No new NuGet packages. The
QuestPDF distribution-license decision remains unresolved (unchanged). RC.5 artifacts are unchanged.
The next packaged candidate is still **`0.1.0-rc.6`**; `Directory.Build.props` still says `0.1.0-rc.5`.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; math is stored as the ordinary Daily Feature text) |
| Upgrade implications | None beyond Phase 21A/21C. Older builds show/print the raw source (e.g. `\(\frac{1}{2}\)`) but never lose it; upgrade every PC sharing a sync folder to see formulas everywhere. |
| Deterministic tests | **3410/3410** (baseline 3227 at `2ca472a`; +183 Phase 21D) = 163 Domain + 1035 Application + 833 Infrastructure + 406 Documents + 917 Ui together + 26 `OperationsCalendarViewTests` + 17 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`) — `Phase21DMathEditorWindowTests` is a new real-Window class and runs alone like the others |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 21D doc |


**Phases 21A–21D packaged as `0.1.0-rc.6` in Phase 21E (below).**

# Release manifest — 0.1.0-rc.6 (Phase 21E: Phases 21A–21D combined)

**Mac-side automated verification complete. Windows validation PENDING.** No Windows PC was
reachable from the build session. Run `docs/phase21e-rc6-windows-checklist.md` — **backup first**
(§1) and read the cross-device Shows warning. Every Windows item stays **NOT EXECUTED** until
results come back.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.6`**, file version `0.1.0.6`, informational `0.1.0-rc.6+ef13a6d03aacc316b876ed2029b0fb0da09b6a16` (read from the packaged `.exe`, `Desktop.dll`, `Documents.dll` and the single-file EXE; none contains `0.1.0-rc.5` or `0.1.0.5`) |
| Starting HEAD / source commit (features) | `91a4a65` (Phase 21D) |
| Release/version commit | `ef13a6d`: `Directory.Build.props` + shipped `TESTER-GUIDE.txt` / `README-portable-release.md`. Artifacts were built from this commit (clean tree apart from the unrelated pre-staged `.DS_Store`/`.claude` files, which are not build inputs) |
| About this build | `0.1.0-rc.6 (ef13a6d)` + running executable path |
| Target | Windows 11 x64 (`win-x64`), self-contained, R2R |
| Build host | macOS 27.0.1 (Darwin 27.0.0), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 (local `dotnet tool` reused from an earlier scratch folder, nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | **16** / 4 / 2 / 1 — unchanged from RC.5, no migration |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `carowinds-passdown.db`, `Carowinds Passdown Daily Refresh`, `Carowinds Passdown Personal Sync`, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `91a4a65`: **3410/3410**. Final at `ef13a6d`: **3410/3410** = 163 Domain + 1035 Application + 833 Infrastructure + 406 Documents + 917 Ui together + 26 `OperationsCalendarViewTests` + 17 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release --filter "Category!=LiveSmoke"`). No tests added; live tests not counted |
| Build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors (before and after the version commit) |
| Superseded | RC.5/RC.4/RC.3/RC.2/RC.1/dev artifacts untouched in `artifacts/releases/` (RC.5 hashes re-verified unchanged after this build). None nested in RC.6. Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.6-win-x64.zip` |
| Bytes | 74,889,881 (RC.5: 73,739,645; +1,150,236) |
| SHA-256 | `04feee4b7b928f26dac982c7e0fa1700a058b454679b95db85bc3ed90bf1e381` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.6-win-x64\` |
| Content | 283 files / 283 entries, 164,549,199 bytes uncompressed (RC.5: 282 files, 162,179,275 bytes; +2,369,924). Only new file: `licenses\STIXTwoMath-OFL.txt` |

Size drivers (all of Phases 21A–21D, not only the font): `Carowinds.Passdown.Documents.dll`
12,070,912 → 13,787,136 (+1.64 MiB; embedded STIX Two Math is 1,518,116 bytes of that),
`Carowinds.Passdown.Desktop.dll` +282,624, `Carowinds.Passdown.Application.dll` +286,720.

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on staging and on the final ZIP (283 files
  each), run by the real `Build-PortableRelease.ps1`. C# mirror (`CAROWINDS_RELEASE_ZIP=…
  --filter RealReleaseZip`): PASS. Nothing bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** `unzip -t` CRC clean; one top-level folder; 283 unique entries; no
  backslash names; no zero-length files. Fresh extraction `diff -rq`-identical to staging; the
  extracted `.exe` SHA-256 equals the staged copy. Executable `PE32+ (GUI) x86-64`.
- **Forbidden content: PASS.** No `.DS_Store`, `__MACOSX`, `.pdb`, `.db`, `-wal`, `-shm`, `.log`,
  `.zip`, `.pdf`, `.png`, `.dylib`, `.so`, source, test assemblies, macOS/Linux runtime folders,
  `CLAUDE.md`/`AGENT_BUILD_SPEC.md`, or dev/rc.1–rc.5 names. No user data, drafts, notes, sync
  payloads, config, diagnostics or preview PDFs.
- **Developer-path scan: PASS.** Checkout path, repository folder name and build user name appear in
  no file (UTF-8/UTF-16), including the single-file EXE. `/Users/` only in the documented
  SQLitePCLRaw vendor DLLs, `/home/` only in `MicroCom.Runtime.dll`; `C:\Users\` nowhere.
- **Math renderer packaging: PASS (build host).** The published (win-x64 R2R) `Documents.dll` carries
  manifest resource `DailyFeatureMath.STIXTwoMath-Regular.ttf` (the exact name the code loads),
  byte-identical to the source font; the font bytes occur exactly once in the release folder and
  once in the single-file EXE (no duplication, no loose `.ttf`). No TeX, network or machine-specific
  dependency. The same-commit portable `Documents.dll` rendered all tester-guide expressions plus the
  nine required symbols with no issues, and the malformed sample fell back to readable text
  ("shown as typed"); the composed PDF was visually checked. The win-x64 R2R image itself cannot load
  on macOS, so rendering *from the published binary* on Windows remains a Windows check (§7/§8).
- **Third-party notices: PASS.** `THIRD-PARTY-NOTICES.txt` and `licenses\` byte-identical to the
  repository; §8 covers STIX Two Math 2.13 b171 under SIL OFL 1.1 with
  `licenses\STIXTwoMath-OFL.txt` (identical to the font's source OFL). Shipped `deps.json` package
  set **identical to RC.5** (44 packages; none added or removed), so no other notice changes were
  needed. No existing notice removed.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.6-win-x64.exe` |
| Bytes | 190,509,900 (RC.5: 187,364,172) |
| SHA-256 | `9987edcad06c7dfb5bdc51ccb996ef0852e62e3d7fee1c4245c9edeb8ab45aaa` |
| Checks | Phase 15F single-file command; publish output = exe + `LatoFont/OFL.txt` only. Carries `0.1.0-rc.6+ef13a6d…` and `0.1.0.6`; STIX font embedded once. Only the vendor `/Users/`, `/home/` paths |
| Status | EXPERIMENTAL. No notices when distributed alone — do **not** hand it out independently. Launch NOT EXECUTED |

## QuestPDF licence

QuestPDF 2026.7.3 shipped with its licence texts; `LicenseType.Community` selected in code. No legal
conclusion drawn; a technical PASS is not a licensing approval.
**DISTRIBUTION LICENSE DECISION: PENDING USER/ORGANIZATION CONFIRMATION**

## Compatibility and known risks

- **Schema numbers unchanged ≠ rollback safe.** RC.6 writes new cached categories
  (`DatedMilitaryFirstResponder`, `ShowSchedule`) and drafts may carry `Shows` and LaTeX source.
  Back up `%LOCALAPPDATA%\Carowinds Passdown` before first launch; do not run RC.5 against a profile
  RC.6 has used.
- **Cross-device Shows (SyncSchemaVersion still 1):** an older build that edits and saves a draft drops
  its show selections, and the loss syncs. Upgrade every PC sharing a sync folder before using Shows;
  back up the shared folder first. Older builds display/print LaTeX as raw source but do not lose it.
- Math Daily Feature paragraphs are drawn as shapes in PDFs (not selectable/searchable); plain text
  remains searchable.
- Historical pricing/show data before local recording cannot be reconstructed; it is shown as
  unavailable, never fabricated.

## Windows validation

To be run with `docs/phase21e-rc6-windows-checklist.md` against the hashes above.

| Item | Result |
|---|---|
| Profile and sync-folder backup | NOT EXECUTED (required first) |
| Hash check, extraction, launch, second launch, About `0.1.0-rc.6 (ef13a6d)`, running path, Windows Search stale copy | NOT EXECUTED |
| 21A Military/First Responder dated prices vs official store | NOT EXECUTED (PASS BY TEST) |
| 21A historical pricing, pass-year identity, Open Passdown → today | NOT EXECUTED (PASS BY TEST) |
| 21B date bar, calendar popup, toolbar, Settings → Diagnostics | NOT EXECUTED (PASS BY TEST) |
| 21C Dashboard / Calendar / Associate Shows | NOT EXECUTED (PASS BY TEST; live source smoke passed 2026-10-16 in Phase 21C) |
| 21C Classic / Compact / Modern PDF Shows | NOT EXECUTED (PASS BY TEST) |
| 21C Teams Copy exclusion | NOT EXECUTED (PASS BY TEST) |
| 21C frozen History shows | NOT EXECUTED (PASS BY TEST) |
| 21D math picker, preview, spell check, invalid input | NOT EXECUTED (PASS BY TEST) |
| 21D math font/glyphs, math PDFs | NOT EXECUTED on Windows (PASS on build host, see above) |
| Shows + math combined PDFs | NOT EXECUTED (PASS BY TEST) |
| Historical backfill, spell-check navigation, Season Price History regressions | NOT EXECUTED (PASS BY TEST) |
| Personal Sync one-PC | NOT EXECUTED (PASS BY TEST) |
| Personal Sync two-PC | **NOT EXECUTED — TWO-PC VALIDATION PENDING** |
| Scheduled refresh | NOT EXECUTED (PASS BY TEST) |
| Diagnostics/privacy | NOT EXECUTED (PASS BY TEST) |
| Scaling 100/125/150%, narrow, dark, keyboard | NOT EXECUTED |
| Experimental EXE | NOT EXECUTED |

## Release status

- **BUILD VERIFIED.** 3410/3410 deterministic tests, Release build 0/0, packaging validator (PS and
  C#), ZIP integrity, forbidden-content and developer-path scans, notices, version resources and
  hashes all pass on the build host. No open defect.
- **WINDOWS VALIDATION PENDING.** No Windows environment was available.
- **TWO-PC SYNC VALIDATION PENDING.**
- **DISTRIBUTION LICENSE DECISION PENDING** (QuestPDF).
- Next release decision: if Windows validation finds a shipped-code defect, fix it in source and ship
  **`0.1.0-rc.7`**; RC.6 artifacts are immutable. If Windows validation passes, RC.6 is the candidate
  for the installer/update work, subject to the QuestPDF decision.

# Unreleased development — Phase 22A (after RC.6)

Design foundation and dark mode: shared semantic color tokens (light + dark), typography/spacing/radius
scales, consolidated button/input/popup/panel/status styles (`Themes/PassdownTokens.axaml`,
`Themes/PassdownStyles.axaml`), reduced decorative blue, charcoal dark theme (no pure-black window),
fixed primary-button hover, accessible dark primary buttons. No layout rearranged, no operational
logic changed, PDF output unchanged. See `docs/phase22a-design-foundation.md`. RC.6 artifacts are
unchanged; `Directory.Build.props` still says `0.1.0-rc.6`. The next packaged candidate is
**`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration — styling only) |
| Upgrade implications | None. Stored data is unaffected. |
| Deterministic tests | **3479/3479** (baseline 3410 at `04a71c3`; +69 `Phase22ADesignFoundationTests`) = 163 Domain + 1035 Application + 833 Infrastructure + 406 Documents + 986 Ui together + 26 `OperationsCalendarViewTests` + 17 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). The new class uses no real Window and runs in the mixed Ui partition |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22A doc §19 |

# Unreleased development — Phase 22B (after RC.6)

Fury banner and application navigation ("Option A — Minimal Fury Banner"): a compact 72 px Fury 325
photographic banner with a charcoal-navy scrim and a single "CAROWINDS OPERATIONS" wordmark replaces
the navy brand band; the six-destination section strip is restyled through shared tokens (underline +
SemiBold + "Current page" status for the active item, neutral hover, width-stable labels, aligned to
the wordmark). Shell height 87 → 105 px (−18 px content height at every tested size). No page
redesigned, no routing/view-model/operational change, PDF output unchanged. See
`docs/phase22b-fury-banner-navigation.md`. RC.6 artifacts are unchanged; `Directory.Build.props` still
says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration — presentation only) |
| Upgrade implications | None. Stored data is unaffected. |
| New shipped asset | `Assets/Branding/fury325-banner.jpg` (1920×320, 135,816 bytes), embedded in `Carowinds.Passdown.Desktop.dll` |
| Deterministic tests | **3515/3515** (baseline 3479 at `d12ec88`; +25 `Phase22BShellNavigationTests`, +11 `Phase15ERealWindowLayoutTests`) = 163 Domain + 1035 Application + 833 Infrastructure + 406 Documents + 1011 Ui together + 26 `OperationsCalendarViewTests` + 28 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). The new class uses no real Window and runs in the mixed Ui partition; the run-alone set is unchanged (still five classes) |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22B doc §15 |
| Banner image rights | **BANNER IMAGE DISTRIBUTION RIGHTS PENDING** (photographer/license unknown; see the Phase 22B doc §16). Separate from the pending QuestPDF distribution-license decision |

# Unreleased development — Phase 22C (after RC.6)

Dashboard information hierarchy, navigation labels and PDF Shows ordering: the Dashboard is one
Operations Overview (park hours, a product-identified official admission price with its own freshness,
passdown status, operational notes, a compact Official Data summary whose per-source breakdown and Deep
Refresh are behind "View Source Details"; genuine source problems stay visible while collapsed), with
Today's Shows beneath. Navigation reads Dashboard · Calendar · Passdown · Notes · History plus an
accessible Settings gear. Newly generated PDFs print Shows as the final section in all three
templates; stored History PDFs and Teams Copy are unchanged. See
`docs/phase22c-dashboard-navigation-pdf-order.md`. RC.6 artifacts are unchanged; `Directory.Build.props`
still says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None. Stored data is unaffected; previously finalized History PDFs keep their original (Shows-before-pricing) order; PDFs generated or finalized from this build put Shows last. |
| Deterministic tests | **3601/3601** (baseline 3515 at `9dec1cb`; +24 `Phase22CDashboardAdmissionTests`, +19 `Phase22CPdfShowsOrderTests`, +34 `Phase22CDashboardOverviewTests`, +1 `Phase22BShellNavigationTests`, +8 `Phase15ERealWindowLayoutTests`) = 163 Domain + 1059 Application + 833 Infrastructure + 425 Documents + 1046 Ui together + 26 `OperationsCalendarViewTests` + 36 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). The run-alone set is unchanged (five classes). Known intermittent: `FinalizedPassdownProjectionTests.BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel` failed once in the baseline run at `9dec1cb` (byte comparison of two generations) and passed on every rerun |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22C doc §21 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |


# Unreleased development — Phase 22D (after RC.6)

Associate workspace refinement and operational-count formatting: the Associate screen separates
official from entered values in words ("Official", "Entered here", "Your setting"), keeps one
accent action (Create PDF) with Check Online as a quiet outlined control, shows official-data
freshness and the Draft save state as two distinct indicators, and collects every transient message
in one feedback stack visible on all sections. Projected attendance and pre-solds display with en-US
thousands separators (38,500 / 21,240) on the Dashboard, the Associate editors (on blur), History
detail and sync-conflict comparisons; PDFs and Teams Copy already grouped counts and are unchanged.
Season Price History's product list now paints the theme's popup surface. See
`docs/phase22d-associate-workspace-number-formatting.md`. RC.6 artifacts are unchanged;
`Directory.Build.props` still says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None. Stored counts stay plain integers in SQLite, draft JSON, sync payloads and finalized snapshots; frozen History PDFs are served byte-for-byte as before. |
| Deterministic tests | **3718/3718** (baseline 3601 at `d768056`; +43 `OperationalCountTests`, +26 `Phase22DCountFormattingTests`, +37 `Phase22DAssociateWorkspaceTests`, +11 `Phase15ERealWindowLayoutTests`; 7 existing assertions updated from ungrouped to grouped counts and one markup regex widened for an extra class) = 206 Domain + 1059 Application + 833 Infrastructure + 451 Documents + 1083 Ui together + 26 `OperationsCalendarViewTests` + 47 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes) |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22D doc §22 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |

# Unreleased development — Phase 22E (after RC.6)

Calendar, Operational Notes and History refinement: the Operations Calendar's selected-date panel groups
its sections and shows a compact Shows summary ("6 scheduled shows · 22 performances") with a
"View schedule" disclosure (collapsed by default, kept across date changes for the session, never a
network call, never another date's lineup); Operational Notes rows are three lines with a restrained
Important rule and created/updated times in the detail; History rows are two lines with a PDF tag and the
Open Final PDF action sits with the record identity. Associate → Products now labels advertised starting
prices "From $x" (same shared rule as the PDFs and Teams Copy). Dashboard "Open Notes" opens today's
passdown notes. See `docs/phase22e-calendar-notes-history.md`. RC.6 artifacts are unchanged;
`Directory.Build.props` still says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None. No stored value, snapshot or PDF changes; frozen History PDFs are served byte-for-byte as before; PDF/Teams price text is identical. |
| Deterministic tests | **3836/3836** (baseline 3718 at `7e7ca9b`; +15 `AdvertisedPriceTextTests`, +40 `Phase22ECalendarShowsTests`, +26 `Phase22ENotesHistoryTests`, +27 `Phase22EProductPriceLabelTests`, +10 `Phase15ERealWindowLayoutTests`; 3 existing tests intentionally updated — Calendar Shows markup and Dashboard Open Notes → today) = 221 Domain + 1059 Application + 833 Infrastructure + 451 Documents + 1176 Ui together + 26 `OperationsCalendarViewTests` + 57 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes). Known intermittent: `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender` failed once in the baseline run at `7e7ca9b` and passed on every rerun |
| Live smoke (opt-in) | not applicable (no retrieval change) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22E doc §21 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |

# Unreleased development — Phase 22E.1 (after RC.6)

Automatic published park schedule coverage. Deep Refresh (6 AM task, startup catch-up, background
maintenance, manual) now discovers the official park schedule about 12 months ahead with one request per
month, instead of one request per date to the end of next month. WinterFest and New Year's Eve main-gate
hours (official operating type 24 under a seasonal label) are now read as Verified rather than Not
Published. The Calendar shows December and later published dates without any date being opened. Show
schedules, historical backfill and Check Online scope are unchanged. See
`docs/phase22e1-published-park-schedule-coverage.md`. RC.6 artifacts are unchanged;
`Directory.Build.props` still says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`**.

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | First launch ≥ 4 h after the morning Deep Refresh (or the next scheduled task) runs one discovery Deep Refresh (about 20 Park Hours requests) and fills about 366 future dates (about 156 KB). Earlier NotPublished WinterFest dates become Verified. No draft, snapshot or PDF changes. |
| Deterministic tests | **3885/3885** (baseline 3836 at `458c527`; +19 `ParkHoursPublishedScheduleTests`, +23 `Phase22E1PublishedScheduleTests`, +7 `OfficialDataRefreshCoordinatorTests`) = 221 Domain + 1089 Application + 852 Infrastructure + 451 Documents + 1176 Ui together + 26 `OperationsCalendarViewTests` + 57 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Known intermittent `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender` failed once and passed on rerun |
| Live smoke (opt-in) | `ParkHoursSourceLiveSmokeTests` 2/2 passed 2026-10-17 (incl. the new 13-month month-form check) |
| Release build | 0 warnings, 0 errors |
| Windows validation | NOT EXECUTED — checklist in the Phase 22E.1 doc §16 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |


# Unreleased development — Phase 22F (after RC.6)

Settings, accessibility and final UI/UX QA, the last Phase 22 phase. Settings is one page:
Appearance, Data Refresh, Personal Sync, Diagnostics, About. Problems show in the error style, and a
mixed-version sync caution is visible. The Calendar never shows a previous date's details under a newly
selected date, including during rapid switching and after a failed read. Operational timestamps show
the Eastern wall clock with "ET" whatever the PC's zone; the PDF footer is also Eastern. Fixes: date-picker
adjacent days are muted, History gate times are labelled, Calendar day-cell hours wrap instead of
truncating, the Associate rail announces its current section, and progress bars are named. Adds
`tools/visual-fixtures` (synthetic screenshot harness, outside the solution). See
`docs/phase22f-settings-accessibility-final-qa.md` and the consolidated
`docs/phase22-windows-validation-checklist.md`. RC.6 artifacts are unchanged; `Directory.Build.props`
still says `0.1.0-rc.6`. The next packaged candidate is **`0.1.0-rc.7`** (not packaged here).

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None for data. Stored timestamps are unchanged; only their display is Eastern + "ET". Newly generated PDFs print the footer time in Eastern (identical on Eastern PCs); frozen History PDFs are never regenerated. |
| Deterministic tests | **3968/3968** (baseline 3885 at `f685b72`; +30 `Phase22FCalendarAndTimeTests`, +38 `Phase22FSettingsAccessibilityTests`, +6 `Phase22FGeneratedTimestampTests`, +9 `Phase15ERealWindowLayoutTests`; 23 existing assertions gained " ET", host-local expectations moved to Eastern, one markup binding updated) = 221 Domain + 1089 Application + 852 Infrastructure + 457 Documents + 1244 Ui together + 26 `OperationsCalendarViewTests` + 66 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes). Known intermittent `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender` failed once and passed 5/5 on rerun |
| Release build | 0 warnings, 0 errors |
| Visual QA | 232 synthetic after-captures (29 scenarios × Light/Dark × 960×640, 1280×690, 1280×860, 1920×1000) + before-captures from `f685b72`; no horizontal overflow or clipped button. Not DPI validation |
| Windows validation | NOT EXECUTED — `docs/phase22-windows-validation-checklist.md` (Narrator, 100/125/150% scaling included) |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |


# Unreleased development — Phase 23A (after RC.7)

Critical save-failure freeze fix. After a failed local save, changing the date, Check Online, a
persistent-note change, Create PDF or closing no longer loops forever on the UI thread (RC.1–RC.7
defect, RC.7 known limitation). Each of those actions now makes at most one save attempt; if it fails
the action is refused with an explanation, the edits stay on screen and a **Retry Save** button
appears. No automatic retry timer. Closing after a failure is held back once with an honest warning;
closing again exits. See `docs/phase23a-save-failure-freeze-fix.md`. RC.7 artifacts and hashes are
unchanged; `Directory.Build.props` still says `0.1.0-rc.7`. The next packaged candidate is
**`0.1.0-rc.8`** (not packaged here).

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None for data. New Retry Save button and one feedback line on the Associate screen. |
| Deterministic tests | **4011/4011** (baseline 3980 at `6dbdce8`/`9c474b8`; +29 `Phase23ASaveFailureLoopTests`, +2 `Phase23ASaveFailureSyncTests`; one `Phase22DAssociateWorkspaceTests` markup expectation updated) = 221 Domain + 1089 Application + 852 Infrastructure + 463 Documents + 1281 Ui together + 26 `OperationsCalendarViewTests` + 66 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes) |
| Release build | 0 warnings, 0 errors |
| Stress | 300 actions under a persistent failure: 301 save attempts (linear), 0 network/PDF/note calls, 9 ms |
| Windows validation | NOT EXECUTED — manual sequence in `docs/phase23a-save-failure-freeze-fix.md` §12 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |

# Unreleased development — Phase 23B (after RC.7, after Phase 23A)

Dashboard **Today's Shows** no longer lets long performance-time lists run into the venue column: each
show is a compact stacked entry (name, times, venue) in responsive columns, and a list of more than five
times shows four plus a keyboard-accessible **+N more** toggle that expands in place (in-memory state
only). The bar-chart mark before **CAROWINDS OPERATIONS** in the Fury banner was removed (title only,
aligned to the 24 px content margin); the window/taskbar icon is unchanged. Presentation only. See
`docs/phase23b-dashboard-shows-branding.md`. RC.7 artifacts and hashes are unchanged;
`Directory.Build.props` still says `0.1.0-rc.7`. The next packaged candidate is **`0.1.0-rc.8`** (not
packaged here).

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None for data. Dashboard Shows layout and banner only. |
| Deterministic tests | **4069/4069** (baseline 4011 at `55e718f`, re-verified; +40 `Phase23BDashboardShowsTests`, +18 `Phase15ERealWindowLayoutTests`; one `Phase21CShowsMarkupTests` binding expectation updated) = 221 Domain + 1089 Application + 852 Infrastructure + 463 Documents + 1321 Ui together + 26 `OperationsCalendarViewTests` + 84 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes) |
| Release build | 0 warnings, 0 errors |
| Visual QA | 80 before + 80 after synthetic `DashboardShows-*` captures (10 scenarios × Light/Dark × 4 sizes); overlapping text in the panel 3 → 0 (busy, ≤ 1280 px), 1 → 0 (1920 px); whole-app sweep at 1280×860 clean. Not DPI validation |
| Windows validation | NOT EXECUTED — checklist in `docs/phase23b-dashboard-shows-branding.md` §19 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |

# Unreleased development — Phase 23C (after RC.7, after Phases 23A and 23B)

Settings → **Appearance** is now a real choice: **Use Windows setting** (default, the RC.7 behavior),
**Light** or **Dark**. It applies immediately without restarting, is saved in
`%LOCALAPPDATA%\Carowinds Passdown\appearance.json` on this computer only (never synced, no database
change), and a saved Light or Dark is applied before the window opens. A missing or unreadable setting
falls back to Use Windows setting. Uses the existing Phase 22A palettes; Fury banner unchanged. See
`docs/phase23c-appearance-preferences.md`. RC.7 artifacts and hashes are unchanged;
`Directory.Build.props` still says `0.1.0-rc.7`. The next packaged candidate is **`0.1.0-rc.8`** (not
packaged here).

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| Upgrade implications | None for data. Upgraded installs have no `appearance.json` and keep following Windows. One new local file once the user picks an option. |
| Deterministic tests | **4173/4173** (baseline 4069 at `00f8b3e`, re-verified; +11 `AppearanceResolverTests`, +22 `FileAppearancePreferenceStoreTests`, +50 `Phase23CAppearancePreferenceTests`, +21 `Phase15ERealWindowLayoutTests`; one `Phase22FSettingsAccessibilityTests` expectation replaced) = 221 Domain + 1100 Application + 874 Infrastructure + 463 Documents + 1371 Ui together + 26 `OperationsCalendarViewTests` + 105 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes) |
| Release build | 0 warnings, 0 errors |
| Visual QA | 526 synthetic captures (forced Light/Dark at four sizes, System under simulated OS Light/Dark); 0 overflow, 0 clipped buttons; live switches with popups/overlays open. Not DPI validation |
| Windows validation | NOT EXECUTED — checklist in `docs/phase23c-appearance-preferences.md` §16 |
| Licensing | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** |

# Unreleased development — Phase 23D (after RC.7, after Phases 23A–23C)

GitHub Releases updater and post-update What's New. Settings → **Application Updates** checks the
configured repository (`KASHO7/Passdown`, from the git remote) for approved releases — signed with an
offline ECDSA P-256 key whose public half is bundled in the app — and, only after **Install Update** and
**Restart and Install**, downloads, verifies (signature, ZIP SHA-256, every file's SHA-256), stages and
installs through the new external `Carowinds.Passdown.Updater.exe` with journaled rollback. A failed draft
save postpones the update (Phase 23A). What's New opens once after a confirmed successful update and is
always available from Settings → About this build. See `docs/phase23d-github-updater.md`.
**Installation is enabled:** the production signing key `carowinds-release-1` is trusted since commit
`392d125` (public-key SHA-256 `8e0e1086…c969d3`; private key kept outside the repository on the build
Mac — back it up). Builds before that commit trust no key. RC.6/RC.7 artifacts and hashes are unchanged;
`Directory.Build.props` still says `0.1.0-rc.7`. Next packaged candidate: **`0.1.0-rc.8`** (not packaged).

| | |
|---|---|
| Schemas | DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1 (unchanged; no migration) |
| New release content | `Carowinds.Passdown.Updater.exe/.dll/.runtimeconfig.json/.deps.json` (required by `release-content-rules.json`); new release assets per version: `…-win-x64.update.json` + `.sig` beside the ZIP |
| New local files | `%LOCALAPPDATA%\Carowinds Passdown\updates.json` (local only); `%LOCALAPPDATA%\Carowinds Operations Updates\` (downloads, staging, requests, records); `.carowinds-update\` inside the app folder only during an install |
| Upgrade implications | RC.7 → RC.8 must be a manual ZIP install (RC.7 has no updater). From RC.8 on, in-app updates once a key is configured. No data change |
| Deterministic tests | **4437/4437** (baseline 4173 at `707864e`, re-verified; +49 Domain, +67 Application, +102 Infrastructure, +42 Ui mixed, +4 `Phase15ERealWindowLayoutTests`) = 270 Domain + 1167 Application + 976 Infrastructure + 463 Documents + 1413 Ui together + 26 `OperationsCalendarViewTests` + 109 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release`, `--filter "Category!=LiveSmoke"`). Run-alone set unchanged (five classes). No test contacts GitHub |
| Release build | 0 warnings, 0 errors |
| Build-host packaging | Updater cross-publishes as PE32+ GUI x64 with `asInvoker`; `Publish-Windows.ps1`/`Build-PortableRelease.ps1` not run (no PowerShell on host) |
| Windows validation | NOT EXECUTED — checklist in `docs/phase23d-github-updater.md` §12 |
| Licensing / distribution | **QuestPDF distribution license decision pending.** **Fury image distribution rights pending.** **Organisation approval for public GitHub distribution pending.** No release created or uploaded |

# Release manifest — 0.1.0-rc.7 (Phase 23: Phases 22A–22F combined)

**Built, packaged and artifact-verified on the macOS build host. Windows validation NOT EXECUTED.
Distribution NOT approved.** No Windows PC was reachable from the build session. Run
`docs/releases/rc7-windows-validation.md`, **backup first**. Release notes:
`docs/releases/rc7-release-notes.md`. Every Windows item stays **NOT EXECUTED** until results come back.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.7`**, file version `0.1.0.7`, informational `0.1.0-rc.7+6dbdce8c2450d9d89845badad13dad12c52ad951` (read from the packaged `.exe`, `Desktop`/`Documents`/`Application`/`Infrastructure`/`Domain` DLLs and the single-file EXE; none contains `0.1.0-rc.6` or `0.1.0.6`) |
| Starting HEAD | `347e097` (Phase 22G.2, research only), branch `feature/passdown-redesign`. Application lineage: `c608c7a` (Phase 22F) is the last production-code commit before Phase 23; `2383715`, `7a2befd`, `347e097` are research/documentation only and change no `src/` file |
| RC.7 source / version commit | **`6dbdce8`**: version bump, save-failure wording, deterministic PDF comparisons, shipped `TESTER-GUIDE.txt`. **Both artifacts were built from this commit** (tree otherwise clean apart from the unrelated pre-staged `.DS_Store`/`.claude` files and uncommitted docs, none of which are build inputs) |
| About this build | `0.1.0-rc.7 (6dbdce8)` + running executable path |
| Target | Windows 11 x64 (`win-x64`), self-contained, ReadyToRun, not trimmed, not single-file (primary) |
| Build host | macOS 27.0.1 (26A434), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 (local `dotnet tool` in a session scratch folder, nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | **16** / 4 / 2 / 1, unchanged from RC.6 (verified in source: latest migration `Migration0016…`, `AssociateDraftPayload.CurrentSchemaVersion = 4`, `FinalizedPassdownSnapshot.CurrentSchemaVersion = 2`, `SyncEnvelopeSerializer.CurrentSyncSchemaVersion = 1`). No migration |
| Legacy identities | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `carowinds-passdown.db`, `Carowinds Passdown Daily Refresh`, `Carowinds Passdown Personal Sync`, AssemblyVersion `0.1.0.0` |
| Deterministic tests | Baseline at `347e097`: **3968**. Final at `6dbdce8`: **3980/3980** = 221 Domain + 1089 Application + 852 Infrastructure + 463 Documents + 1250 Ui together + 26 `OperationsCalendarViewTests` + 66 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release --filter "Category!=LiveSmoke"`). Added: +6 `Phase23PdfGenerationTimestampTests` (Documents), +6 `Phase23SaveFailureMessagingTests` (Ui). Modified: 5 PDF byte comparisons, 1 Phase 22F save assertion, 1 Phase 22A chip-brush theory. Live tests not run or counted |
| Research tests (separate) | `tools/research/fims-ride-monitor`: 64/64 Python `unittest` tests passed. Not part of the application total; RC.7 does not depend on them |
| Build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors (before and after the version commit) |
| Superseded | RC.6/RC.5/RC.4/RC.3/RC.2/RC.1/dev artifacts untouched in `artifacts/releases/` (RC.6 SHA-256 re-verified unchanged after this build). Do not distribute |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.7-win-x64.zip` |
| Bytes | **75,149,722** (RC.6: 74,889,881; +259,841) |
| SHA-256 | **`6473d1c59cdab360a054d59dafac3834c0184b39ee93ccb5816268c3c852b811`** |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.7-win-x64\` |
| Content | 283 files / 283 entries, 165,015,237 bytes uncompressed (RC.6: 283 files, 164,549,199). **File list identical to RC.6** (relative paths). Size drivers: `Carowinds.Passdown.Desktop.dll` 4,227,072 → 4,636,672, `Carowinds.Passdown.Application.dll` 1,839,104 → 1,888,256 |
| Produced by | `packaging/windows/Build-PortableRelease.ps1` (real script, via `Publish-Windows.ps1`, which deletes `artifacts/publish/win-x64` first). Staging `artifacts/release-staging/Carowinds-Operations-0.1.0-rc.7-win-x64` was freshly created |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on staging and on the final ZIP (283 files
  each), run by the build script. C# mirror (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`,
  Infrastructure.Tests): PASS. Nothing bypassed. Not yet run on Windows.
- **ZIP structure: PASS.** `unzip -t` clean; one top-level folder; 283 unique entries; no backslash
  names; no zero-length files. Fresh extraction is `diff -rq`-identical to staging. Executable
  `PE32+ (GUI) x86-64`.
- **Runtime/native files present:** `coreclr.dll`, `hostfxr.dll`, `QuestPDF.dll`, `QuestPdfSkia.dll`,
  `qpdf.dll`, `e_sqlite3.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll`;
  `Carowinds.Passdown.Desktop.runtimeconfig.json`/`deps.json`. Configuration: `appsettings.json` is
  an embedded resource in `Desktop.dll` (Phase 15F), so it is correctly not a loose file.
- **Shipped helpers/docs present:** `README.txt`, `TESTER-GUIDE.txt` (RC.7 text), `THIRD-PARTY-NOTICES.txt`,
  `licenses\` (incl. `QuestPDF-LICENSE.txt`, `STIXTwoMath-OFL.txt`), `Enable/Disable Automatic
  Refresh.cmd`, `tools\Install-/Uninstall-ScheduledRefresh.ps1`.
- **Assets:** the Fury banner (`Assets/Branding/fury325-banner.jpg`, 135,816 bytes) occurs exactly once
  inside `Carowinds.Passdown.Desktop.dll` (Avalonia resource) and once in the single-file EXE; no loose
  copy. STIX Two Math occurs once in `Documents.dll` and once in the EXE. No loose `.ttf`/`.otf`.
- **Forbidden content: PASS.** No `.DS_Store`, `__MACOSX`, `.pdb`, `.db`, `-wal`, `-shm`, `.log`,
  `.zip`, `.pdf`, `.png`, `.py`, `.cs`, `.dylib`, `.so`, test assemblies, test reports, research
  scripts/outputs (`research`, `fims`), macOS/Linux runtime folders, `.git*`, `CLAUDE.md`,
  `AGENT_BUILD_SPEC.md`, or rc.6/dev names. No user data, drafts, notes, sync payloads, diagnostics or
  preview PDFs.
- **Developer-path scan: PASS.** Checkout path, repository folder name and build user name appear in no
  file (UTF-8/UTF-16), including the single-file EXE. `/Users/` only in the documented SQLitePCLRaw
  vendor DLLs, `/home/` only in `MicroCom.Runtime.dll` (and those same bytes inside the EXE);
  `C:\Users\` nowhere.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `artifacts/releases/Carowinds-Operations-0.1.0-rc.7-win-x64-singlefile.exe` |
| Bytes | **191,013,708** (RC.6 EXE: 190,509,900) |
| SHA-256 | **`caa32e3f4641f6d7cdb3010882247ba94360d4f0222742bc8b89df88e51802b0`** |
| Checks | Phase 15F single-file command into a freshly deleted `artifacts/publish/win-x64-singlefile`; output = exe + `LatoFont/OFL.txt` only (`.pdb` removed). Carries `0.1.0-rc.7+6dbdce8…` and `0.1.0.7`; Fury image and STIX font embedded once. `PE32+ (GUI) x86-64` |
| Naming | Renamed from RC.6's `…-win-x64.exe` to `…-win-x64-singlefile.exe` so it cannot be mistaken for the ZIP's main executable |
| Status | EXPERIMENTAL. No notices when distributed alone. Do **not** hand it out independently. Launch NOT EXECUTED |

## Frozen artifacts

Both RC.7 files are frozen for validation as recorded above (made read-only on the build host). They
must not be modified, rebuilt over or repacked under these names. A defect that needs a new binary
ships as **`0.1.0-rc.8`**.

## Phase 23 release-readiness audit

| Area | Finding | Classification |
|---|---|---|
| Failed-save messaging | A real persistence failure displayed "Unsaved changes", the same as a pending edit. Now **"Save failed — changes not saved"** (error background, reason in tooltip/Narrator help text); state stays until a save succeeds | Fixed in RC.7 (was nonblocking clarity defect) |
| Persistent save failure + date switch | `AssociateViewModel.FlushPendingAutosaveAsync` retries `while (_isDirty …)` with no bound or backoff. Reproduced on the build host: with saves failing, a date change ran 100% CPU for > 400 s with no progress. Also reached via Check Online and add/remove/replace operational note. Shutdown is already bounded (5 s). Pre-existing since RC.1 | **Known limitation** (owner decision: document in RC.7, fix in RC.8) + Windows validation item 7.7 caution |
| Intermittent PDF comparison | Root cause **established**: see below | Fixed (test comparison); no product defect |
| PDF layout and footer | Shows last in all three templates; footer Eastern; covered by Documents tests (463 pass) | Windows validation required (12.x) |
| Windows filesystem assumptions | Storage/sync/AppPaths code identical to RC.6 (`git diff ef13a6d` empty for `Infrastructure/Storage`, `Infrastructure/PersonalSync`, `Infrastructure/Preferences`, `AppPaths.cs`) | Windows validation required |
| Settings folder picker | Native picker only exercisable on Windows | Windows validation required (11.5) |
| Portable layout / self-contained runtime / native deps | Same 283-file inventory as RC.6; runtime and native DLLs present | Windows validation required (3.4) |
| Personal Sync schema compatibility | SyncSchemaVersion 1 unchanged; only `SyncConflictDetail` display formatting changed (grouped counts) | Known limitation (mixed-version Shows) |
| Fury image packaging | Embedded once, no loose file | Pass (build host); rights pending |
| QuestPDF license | See below | Release gate pending |

### Intermittent PDF test investigation

- **Test**: `DailyFeatureMathPdfTests.FinalizedSnapshot_KeepsTheSourceText_AndItsPdfMatchesTheLiveRender`
  (also, once, `FinalizedPassdownProjectionTests.BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel`).
- **Failure pattern**: occasional byte mismatch between two renders of the same model; passed on rerun.
- **Root cause**: QuestPDF writes `/CreationDate` and `/ModDate` from the wall clock at one-second
  resolution (the generator sets no document metadata). Two renders that straddle a second boundary
  differ in exactly two bytes, the seconds digit of each stamp. Reproduced deterministically by
  waiting for the next second between renders: 57,305 bytes each, 2 differing bytes, both inside those
  fields. The math test failed most often because its renders take longest.
- **Generated content was not incorrect.** Text, layout and every other byte are identical.
- **Fix**: `PdfInspector.WithoutGenerationTimestamps` zeroes only those two 14-digit values (same
  length, offsets unchanged) and throws if either field is missing. Applied to the five
  render-twice comparisons. `Phase23PdfGenerationTimestampTests` forces a second boundary every run
  (Classic/Compact/Modern), proves only digits change, and proves a real content difference is still
  detected. Production PDF output is unchanged.
- **Evidence**: 15 consecutive runs of the four affected classes (52 tests each, 45 forced boundary
  crossings in total): all passed. Full suite 3980/3980.
- **Remaining risk**: none known for these tests. Production PDFs still carry wall-clock metadata
  dates, as in every earlier RC (frozen PDFs are stored bytes and never regenerated).

## Upgrade compatibility (RC.6 → RC.7)

- No schema, migration, serializer, repository, sync-payload, sync-config or app-path change since
  RC.6 (`ef13a6d`): `git diff ef13a6d 6dbdce8` is empty for `Infrastructure/Storage`,
  `Infrastructure/PersonalSync`, `Infrastructure/Preferences`, `Application/Persistence`,
  `Application/Finalization`, `Application/OperationalNotes`, `AssociateDraftPayload*.cs`,
  `AppPaths.cs` and the scheduled-task scripts. RC.7 therefore reads and writes an RC.6 profile with
  byte-identical persistence code. The existing lifecycle/migration suites (`PortableLifecycleTests`,
  `MigrationHardeningTests`, sync tests) pass.
- Behavioral data change: the first RC.7 Deep Refresh (6 AM task or startup catch-up ≥ 4 h after the
  last one) fills about 366 future park-hours dates (about 20 requests, about 156 KB). Earlier
  NotPublished WinterFest dates become Verified. No draft, note, override, sync-config, snapshot or
  PDF change.
- Not executed: opening a real RC.6-used profile with the RC.7 binary on Windows (validation §4). A
  synthetic cross-binary run was not done on the build host because the persistence code under test
  is identical.

## QuestPDF licence

QuestPDF **2026.7.3** (`src/Carowinds.Passdown.Documents/Carowinds.Passdown.Documents.csproj`
`QuestPdfVersion`), `QuestPDF.Settings.License = LicenseType.Community` in
`AssociatePassdownPdfGenerator`. Licence text shipped as `licenses\QuestPDF-LICENSE.txt`. Its
"Community License Eligibility" excludes publicly traded companies and organisations above USD 1M
annual revenue. No owner or organisation approval for this distribution is recorded in the repository.
No legal conclusion drawn; a technical PASS is not a licensing approval.
**QuestPDF distribution approval pending.**

## Fury photograph rights

| | |
|---|---|
| Asset | `src/Carowinds.Passdown.Desktop/Assets/Branding/fury325-banner.jpg` (135,816 bytes, embedded in `Desktop.dll`) |
| Known provenance | Cropped from the photograph the product owner attached in the Phase 22B request (`1.webp`, 1920×1080, SHA-256 `29918441…fa97`); see `docs/phase22b-fury-banner-navigation.md` §3 |
| Rights status | Photographer, licence and redistribution permission undocumented |
| Intended recipients | Controlled technical testers only |
| Authorisation documented | No |

**Fury photograph redistribution rights pending.** No substitute image was used.

## Compatibility and known risks

- **Personal Sync mixed versions:** older versions may not understand the latest Show draft selections
  and may discard them on re-save; that loss syncs. Upgrade all PCs sharing a sync folder together and
  back up the shared folder first. The app does not technically prevent mixed-version writes.
- **Rollback:** back up `%LOCALAPPDATA%\Carowinds Passdown` before upgrading. Copying an older
  executable over newer binaries is not necessarily a safe rollback; restore the backup with the app
  closed instead.
- **Persistent save failure + date change freeze** (pre-existing, fix planned for RC.8). See audit.

## Windows validation

Record: `docs/releases/rc7-windows-validation.md`. **Every item NOT EXECUTED** (extraction, launch,
About RC.7, Fury banner, data path, no migration, no-SDK launch, 100/125/150% scaling, Narrator,
folder picker, Task Scheduler, OneDrive/two-PC sync, PDFs, Teams Copy, upgrade from RC.6, experimental
EXE).

## Release status

- **BUILT**: Release 0/0; 3980/3980 deterministic tests; research suite 64/64 separately.
- **PACKAGED**: ZIP + experimental EXE from `6dbdce8`; frozen.
- **ARTIFACT VERIFIED (build host)**: validator (PS + C#), integrity, inventory, versions, assets,
  forbidden-content and developer-path scans, RC.6 immutability.
- **WINDOWS VALIDATION PENDING.** **TWO-PC SYNC VALIDATION PENDING.**
- **DISTRIBUTION NOT APPROVED**: QuestPDF distribution approval pending; Fury photograph
  redistribution rights pending. Controlled technical testing only; not uploaded or published.

# Release manifest — 0.1.0-rc.8 (Phase 24: Phases 23A–23D combined)

**Built, packaged, hash-verified and manifest-signed on the macOS build host. Windows validation NOT
EXECUTED. Distribution NOT approved. Nothing pushed, uploaded or published.** Run
`docs/releases/rc8-windows-validation.md` (backup first). Release notes:
`docs/releases/rc8-release-notes.md`. Installation: `docs/releases/rc8-installation-guide.md`.

| | |
|---|---|
| Product (visible) | Carowinds Operations |
| Version | **`0.1.0-rc.8`**, file version `0.1.0.8`, informational `0.1.0-rc.8+a651e26582b2bf35aaf671890b7018cb58ab6f2e` (read from the packaged `Desktop`/`Application`/`Infrastructure`/`Domain`/`Documents`/`Updater` DLLs and the single-file EXE; no packaged file contains `0.1.0-rc.7` or `0.1.0.7`) |
| Starting HEAD | `633a431` (Phase 23D docs), branch `feature/passdown-redesign`; version `0.1.0-rc.7` |
| RC.8 source / version commit | **`a651e26`**: `Directory.Build.props` 0.1.0-rc.8 / 0.1.0.8, bundled What's New notes (`src/Carowinds.Passdown.Desktop/ReleaseNotes/release-notes.md`), shipped `TESTER-GUIDE.txt` and `README.txt`. **All RC.8 artifacts were built from this commit** after a full `bin`/`obj` clean (tree otherwise clean apart from the unrelated pre-staged `.DS_Store`/`.claude` files, which are not build inputs) |
| About this build | `0.1.0-rc.8 (a651e26)` + running executable path |
| Target | Windows 11 x64 (`win-x64`), self-contained, ReadyToRun, not trimmed; primary is multi-file (not single-file) |
| Build host | macOS 27.0.1 (26A434), Apple Silicon arm64; .NET SDK 10.0.400; PowerShell 7.6.7 as a local `dotnet tool` in the session scratchpad (nothing installed system-wide) |
| DB / Draft / Snapshot / SyncSchemaVersion | **16** / 4 / 2 / 1, unchanged from RC.7 (latest migration `Migration0016…`, `AssociateDraftPayload.CurrentSchemaVersion = 4`, `FinalizedPassdownSnapshot.CurrentSchemaVersion = 2`, `SyncEnvelopeSerializer.CurrentSyncSchemaVersion = 1`). No migration |
| Storage identity | Unchanged: `Carowinds.Passdown.Desktop.exe`, `%LOCALAPPDATA%\Carowinds Passdown`, `carowinds-passdown.db`, `appearance.json` location, `Carowinds Passdown Daily Refresh`, `Carowinds Passdown Personal Sync`, AssemblyVersion `0.1.0.0`. Updater-only local folders as introduced in Phase 23D |
| Deterministic tests | Baseline (Phase 23D): **4437**; measured with the RC.8 version applied (the bump changes no test): **4437/4437** = 270 Domain + 1167 Application + 976 Infrastructure + 463 Documents + 1413 Ui together + 26 `OperationsCalendarViewTests` + 109 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7 `SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` alone (`-c Release --filter "Category!=LiveSmoke"`). Phase 24 adds +2 `Phase24ReleaseCompatibilityTests` (Application): final **4439/4439** (Application 1169). Run-alone set unchanged (five classes). No test contacts GitHub; live tests not run or counted |
| Release build | `dotnet build CarowindsPassdown.slnx -c Release`: 0 warnings, 0 errors (before packaging, after a full clean, and at the documentation commit). `tools/update-signing` Release build: 0/0 |
| Superseded | RC.7/RC.6/older artifacts untouched in `artifacts/releases/`; RC.6 and RC.7 SHA-256 re-verified unchanged after the RC.8 build. Do not distribute superseded versions |

## Primary release: multi-file portable ZIP (RECOMMENDED)

| | |
|---|---|
| Artifact | `/Users/kashyap/Documents/Passdown/carowinds-passdown-greenfield/artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64.zip` |
| Bytes | **75,714,287** (RC.7: 75,149,722) |
| SHA-256 | **`6d753211ec0cca7393aac162b15d01220f3ae12a05e489a5316c71489c7f83ea`** |
| Architecture / runtime | win-x64, self-contained (`coreclr.dll`, `hostfxr.dll` included); `Carowinds.Passdown.Desktop.exe` and `Carowinds.Passdown.Updater.exe` are `PE32+ (GUI) x86-64` |
| Extracted folder | `Carowinds-Operations-0.1.0-rc.8-win-x64\` |
| Content | 287 files / 287 entries (no directory entries), 166,324,828 bytes uncompressed (RC.7: 283 files, 165,015,237). Relative to RC.7: **+4** `Carowinds.Passdown.Updater.exe` (365,056 B, SHA-256 `ca2662b1…406f`), `.dll` (207,360 B), `.runtimeconfig.json`, `.deps.json`; changed: `Desktop.dll`, `Application.dll`, `Infrastructure.dll`, `Domain.dll`, `README.txt`, `TESTER-GUIDE.txt`; nothing removed |
| Produced by | `packaging/windows/Build-PortableRelease.ps1` (real script, via `Publish-Windows.ps1`, which deletes `artifacts/publish/win-x64` and publishes the updater from source into a fresh temporary folder). Staging `artifacts/release-staging/Carowinds-Operations-0.1.0-rc.8-win-x64` freshly created |
| Frozen | Read-only on the build host |

- **Packaging validator (`Test-ReleasePackage.ps1`): PASS** on staging and on the final ZIP (287 files),
  run by the build script. C# mirror (`CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`): PASS.
- **ZIP structure: PASS.** `unzip -t` clean; one top-level folder; no backslash names; fresh extraction
  `diff -rq`-identical to staging.
- **Required content present:** main exe/dll/runtimeconfig/deps; the four updater files; QuestPDF
  (`QuestPDF.dll`, `QuestPdfSkia.dll`, `qpdf.dll`), Skia/HarfBuzz/ANGLE natives, `e_sqlite3.dll`;
  `README.txt`, `TESTER-GUIDE.txt`, `THIRD-PARTY-NOTICES.txt`, `licenses\` (incl. `QuestPDF-LICENSE.txt`,
  `STIXTwoMath-OFL.txt`, `Inter-OFL.txt`), `LatoFont\OFL.txt`, `Enable/Disable Automatic Refresh.cmd`,
  `tools\Install-/Uninstall-ScheduledRefresh.ps1`.
- **Embedded (not loose):** Fury banner (`fury325-banner.jpg`, 135,816 B) exactly once in
  `Desktop.dll`; fonts embedded as before (no loose `.ttf`/`.otf`); `appsettings.json` embedded with
  `Updates:Repository = KASHO7/Passdown`, channel `ReleaseCandidate`, trusted key `carowinds-release-1`
  (public key only); bundled What's New notes `Version: 0.1.0-rc.8`.
- **Forbidden content: PASS.** No `.pem`/private-key material (byte-window scan against the production
  private key: 0 matches in the extracted ZIP, the EXE and `src/`), no git credentials or GitHub tokens
  (`ghp_`, `github_pat_`), no `.git*`, `.DS_Store`, `__MACOSX`, `.pdb`, `.db`/`-wal`/`-shm`, logs, PDFs,
  test assemblies, fixtures, research/monitoring snapshots, `appearance.json`/`updates.json`/sync data,
  `.update.json`/`.sig`, or source files. The only "PRIVATE KEY" bytes are PEM label constants inside the
  .NET runtime's `System.Security.Cryptography.dll`.
- **Developer-path scan: PASS.** Build user name and checkout folder name appear in no packaged file.

## Single-file release (EXPERIMENTAL)

| | |
|---|---|
| Artifact | `/Users/kashyap/Documents/Passdown/carowinds-passdown-greenfield/artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64-singlefile.exe` |
| Bytes | **191,886,156** (RC.7 EXE: 191,013,708) |
| SHA-256 | **`9026dc93944b0a9a0476858bb71acd9d997696ef2eb00a0ee6d741401753c654`** |
| Checks | Phase 15F single-file command into a freshly deleted `artifacts/publish/win-x64-singlefile`; output = exe + `LatoFont/OFL.txt` (`.pdb` removed). `PE32+ (GUI) x86-64`; carries `0.1.0-rc.8+a651e26…`; Fury image embedded once; no rc.7 strings, no developer paths, no private-key bytes. Frozen read-only |
| Updates | **In-app installation is not available for this form.** It has no `Carowinds.Passdown.Updater.exe` or `Carowinds.Passdown.Desktop.dll` beside it, so `InstallationInspector` reports *NotPortableInstallation* and Settings shows *Manual update required* (Phase 23D design, no change needed). Update it manually by replacing the file with a newer one |
| Status | EXPERIMENTAL. No notices when distributed alone. Do **not** hand it out independently. Launch NOT EXECUTED |

## Update manifest (GitHub Releases metadata, Phase 23D format 1)

| | |
|---|---|
| Manifest | `artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64.update.json` — 49,119 B, SHA-256 `8940cff5baa1d927cf5dfc28eae7826da9a9ce868325a1e2d4878b88f1a8d8df` |
| Signature | `artifacts/releases/Carowinds-Operations-0.1.0-rc.8-win-x64.update.json.sig` — 206 B, SHA-256 `e8f1698ec7c3d999024db0cc438d46809de5a9cec5ed115033d10fe0e42f8aaa`; `ECDSA-P256-SHA256`, key ID `carowinds-release-1` |
| Contents | product `carowinds-operations`, version `0.1.0-rc.8`, channel `release-candidate`, `win-x64`, `publishedAtUtc 2026-10-18T12:18:47Z` (signing time; **not** published), package `Carowinds-Operations-0.1.0-rc.8-win-x64.zip` / 75,714,287 B / `6d753211…83ea` / root `Carowinds-Operations-0.1.0-rc.8-win-x64`, 287 file entries incl. the six required files |
| Produced by | `tools/update-signing` `create-manifest` (from the frozen ZIP) → `sign --key-id carowinds-release-1 --private-key ~/.carowinds-signing/carowinds-release-1.pem` → `verify --appsettings src/Carowinds.Passdown.Desktop/appsettings.json` = **VERIFIED** |
| Signing authorisation | Owner confirmed in this session (2026-10-18) that a second offline protected backup of the private key exists and authorised **local** signing only |
| Status | **SIGNED, NOT PUBLISHED.** Not uploaded, no tag, no GitHub Release. Only meaningful for a future in-app update *to* RC.8 from an updater-enabled build, which does not exist yet (RC.8 is the first); kept so the published asset set, if RC.8 is ever approved for GitHub, is exactly these frozen files |

**Negative checks (synthetic copies in the session scratchpad, CLI `verify`):** manifest text changed →
`InvalidSignature`; package SHA-256 changed → `InvalidSignature`; signed with a disposable key claiming
`carowinds-release-1` → `InvalidSignature`; disposable key with key ID `synthetic-unknown-key` →
`UnknownKey`; the production pair still `VERIFIED`. The disposable key was deleted afterwards. No command
printed key material.

## Signing identity

| | |
|---|---|
| Configured key | `carowinds-release-1`, ECDSA P-256, in `src/Carowinds.Passdown.Desktop/appsettings.json` → `Updates:TrustedSigningKeys` (trusted since `392d125`) |
| Public-key fingerprint | SHA-256(SubjectPublicKeyInfo) **`8e0e108689653f027f92b0e91122cf3e49dda206636378723dc7941ed2c969d3`** — computed independently from the bundled Base64 and from the private key's derived public half; identical |
| Private key | `~/.carowinds-signing/carowinds-release-1.pem` (folder `700`, file `600`), outside the repository, outside the publish/staging folders, never printed or copied |
| Backup | Owner-confirmed second offline protected copy (2026-10-18). Not verified by the build session |

## Updater security regression (automated, build host)

Covered by the passing suites (no bypass switch exists in `src/` — searched for skip/bypass/environment
overrides in the updater code paths):

| Rejection | Tests |
|---|---|
| Unsigned release / missing signature asset | `UnsignedOrInvalidlySignedRelease_IsNotApproved_AndOlderValidOneIsOffered`, `MissingRequiredAsset_IsIgnored` |
| Invalid signature / tampered manifest | `TamperedManifest_IsRejected`, `MalformedSignatureFile_IsRejected` |
| Wrong / unknown key | `SignatureFromAnUntrustedKey_IsRejected`, `NoTrustedKeys_RejectsEverything`, `InvalidConfiguredKeys_AreIgnored_NotTrusted` |
| Hash mismatch | `CorruptDownload_FailsHashCheck_AndLeavesNothingStaged`, `ModifiedFileContent_IsRejected`, `StagedFileTamperedAfterVerification_IsRejected` |
| ZIP path traversal / symlinks / duplicates | `HostileOrUnexpectedEntries_AreRejected_AndNothingIsLeftBehind`, `SymbolicLinkEntry_IsRejected`, `DuplicateEntry_IsRejected`, `SignedManifestWithUnsafeOrDuplicatePath_IsRejected` |
| Wrong architecture / product / channel | `SignedManifestForAnotherArchitecture_IsRejected`, `SignedManifestForAnotherProduct_IsRejected`, `ReleaseCandidateVersionClaimingStable_IsRejected` |
| Downgrade / same version | `OlderOrSameVersion_IsNeverOffered`, `IsInstallable_NeverDowngradesOrReinstalls`, `DowngradeWrongProductOrUnsafePathInRequest_IsRefused`, `Phase24ReleaseCompatibilityTests` (installed RC.8 ignores RC.7/RC.8, is offered RC.9; built version never offered itself) |
| Missing required files | `ManifestWithoutTheUpdater_IsRejected`, `MissingFile_IsRejected` |
| Unauthorised / protected location | `PermissionDenied_ChangesNothing`, `FolderThatBecameUnwritable_StopsBeforeTheUpdaterStarts`, `RequestOutsideTheRequestsFolder_IsIgnoredEntirely`, `UpdaterRunningFromAnotherFolderThanTheStagedOne_RefusesTheRequest` |

These are **TESTED WITH SYNTHETIC DATA** on macOS (fake process environment, POSIX permissions). Windows
file locking, ACLs, process restart and rollback are not exercised.

## Phase 23A–23D scope in RC.8 (build-host evidence only)

- **23A save-failure freeze:** at most one save attempt per action; failed state persists with Retry
  Save; date change / Check Online / note operations refused during persistent failure, edits retained;
  Personal Sync not marked successful; close warns once; no retry timer. ~5 s SQLite lock wait and
  "second close discards" limitations preserved. (`Phase23ASaveFailureLoopTests`, `…SyncTests`)
- **23B Dashboard Shows:** stacked name/times/venue, "+N more" in-memory expansion, wrapping, honest
  missing venue and source states, title-only Fury banner, unchanged window icon; FIMS retrieval
  untouched. (`Phase23BDashboardShowsTests`, real-window layout tests)
- **23C Appearance:** Use Windows setting (default) / Light / Dark, immediate, local `appearance.json`
  (unchanged location), never synced, applied before the first window. (`AppearanceResolverTests`,
  `FileAppearancePreferenceStoreTests`, `Phase23CAppearancePreferenceTests`)
- **23D updater:** public GitHub Releases discovery (`KASHO7/Passdown`, unauthenticated, prereleases
  included, SemVer ordering), signed manifest + SHA-256 verification, safe extraction, external updater,
  staging, restart, journaled rollback, data preservation, offline bundled notes, What's New once after a
  confirmed successful update only (clean/manual install never). No silent install, no auto-publishing,
  no SharePoint.

## Upgrade compatibility (RC.7 → RC.8)

- **Manual ZIP install required** — RC.7 has no updater and cannot upgrade itself.
- RC.8 is the first build that can later receive an approved, signed in-app update.
- A manual RC.8 install writes no install record, so What's New does not open automatically
  (`WhatsNewPolicy`); notes are available from Settings → About this build.
- No schema, serializer, sync-payload or app-path change (schemas 16/4/2/1). New local files only when
  used: `appearance.json`, `updates.json`, `%LOCALAPPDATA%\Carowinds Operations Updates\`.
- Not executed: opening a real or synthetic RC.7 profile with the RC.8 binary on Windows
  (`rc8-windows-validation.md` §8).

## QuestPDF licence

QuestPDF **2026.7.3**, `LicenseType.Community` in `AssociatePassdownPdfGenerator`; licence text shipped as
`licenses\QuestPDF-LICENSE.txt`. Its Community License Eligibility limits use to qualifying individuals,
learning/evaluation, certain non-profits/academia and organisations under USD 1M annual revenue, and
excludes publicly traded companies and public-sector entities. Whether the intended organisational
deployment qualifies has not been determined, and no organisational approval is recorded. No legal
conclusion is drawn. **Distribution approval pending.**

## Fury photograph rights

Unchanged from RC.7 (`fury325-banner.jpg`, 135,816 B, cropped from the owner-supplied image; photographer,
licence and redistribution permission undocumented). **Photograph redistribution rights pending.** Not
replaced.

## Public GitHub

`KASHO7/Passdown` is public. Nothing was pushed, tagged, uploaded or attached. A public release is a
separate action requiring explicit owner approval and resolution of the licensing, photograph and
organisation-policy gates (`docs/phase23d-github-updater.md` §14).

## Windows validation

Record: `docs/releases/rc8-windows-validation.md`. **Every item NOT EXECUTED** (first launch, save
failure, Dashboard Shows, appearance, updater, restart/rollback, RC.7 profile upgrade, operational
features, scaling, Narrator, experimental EXE). The real Windows updater test harness (test-configured
builds + signed synthetic releases in a separate private test repository) is **not prepared**.

## Frozen artifacts

The RC.8 ZIP, EXE, manifest and signature are frozen (read-only) as recorded above. They must not be
modified, rebuilt over or repacked under these names. A defect needing a new binary ships as
**`0.1.0-rc.9`**.

## Outstanding issues

1. Windows validation of everything (§ above).
2. Updater Windows harness: owner to choose a private test repository for synthetic signed releases.
3. QuestPDF Community-licence eligibility / organisational approval.
4. Fury photograph redistribution rights.
5. Organisation policy on distributing via a personal public GitHub repository.
6. Executables not Authenticode-signed; SmartScreen/Defender behaviour unknown.

## Release status

- **BUILT**: Release 0/0; 4437/4437 at the version commit, 4439/4439 with the Phase 24 tests.
- **PACKAGED**: ZIP + experimental EXE from `a651e26`; frozen.
- **HASH VERIFIED**: sizes and SHA-256 above; RC.6/RC.7 unchanged.
- **SIGNED**: update manifest, locally, `carowinds-release-1`; **NOT PUBLISHED**.
- **TESTED WITH SYNTHETIC DATA**: updater, signing, discovery, rollback (macOS).
- **WINDOWS VALIDATED: NO (NOT EXECUTED).**
- **DISTRIBUTION APPROVED: NO (PENDING)** — QuestPDF, photograph rights, organisation policy.
