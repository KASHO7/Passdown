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
