# Phase 35G — Final UI/UX polish, regression validation and RC.14 readiness

Status: **implemented locally — awaiting final Windows validation.** Not pushed, tagged, signed or published. RC.14 is
**not** prepared for publication: the source version is still `0.1.0-rc.13` and needs a separate authorization to bump.

| | |
|---|---|
| Branch | `feature/passdown-redesign` |
| Starting commit | `cfc1bbd` (Phase 35E-4 Dashboard record) |
| Ending commits | `c4fe167` — Phase 35G-1 (source, tests, visual fixtures, tester guide); Phase 35G-2 — this record, the Windows checklist and the Phase 35 plan update |
| Reviewed source baseline | `c4fe167`; `src`, `tests`, `tools`, `packaging` and `Directory.Build.props` clean at build and test time |
| Remote (local tracking ref, not re-fetched) | `origin/feature/passdown-redesign` = `256a1c9`; HEAD was 2 commits ahead at the start. Remote currency not verified |
| Release status | RC.13 published and frozen (tag `v0.1.0-rc.13` → `d784c60`; local ZIP / manifest / signature SHA-256 `c4e1fd29…11d5d` / `be3ddaec…724c` / `566de4d6…8ce0`, identical to `docs/releases/rc13-release-readiness.md`). No `rc.14` tag exists |
| Source version | `0.1.0-rc.13` (`Directory.Build.props`) — unchanged |
| Database schema | 18 — no migration, storage code unchanged since RC.13 (`git diff cfebdc9` empty for `Infrastructure/Storage`) |
| Personal Sync | `SyncSchemaVersion` 1, draft payload 4, finalized snapshot 2 — sync code unchanged since RC.13 |
| Working tree | the owner's uncommitted documentation reorganization (`AGENT_BUILD_SPEC.md`, `CLAUDE.md`, `docs/architecture.md`, untracked `docs/architecture/`, `.claude/rules/`, `.claude/skills` edits, staged `.DS_Store` / `pdf-design/SKILL.md`) was left untouched and is not part of these commits |

## 1. Preflight — verified facts

| Claim in the brief | Evidence | Result |
|---|---|---|
| Branch `feature/passdown-redesign`, HEAD `cfc1bbd` | `git` | confirmed |
| Dashboard implementation `2ec6261` | `git log` | confirmed |
| 35A–35F commits present | `296807e`…`beba3f8` (35A), `1677781`/`bad86da` (35B), `051cc4e`/`bfbdd18` (35C), `65c230a`/`d7d6848`/`7066e08` (35D), `01d3553`/`62d2aca` (35D date selector), `5ea4d0f`/`8de9453` (35E Calendar), `8967fcc`/`256a1c9` (35F), `2ec6261`/`cfc1bbd` (35E Dashboard) | confirmed |
| 5,885 passing tests | re-run on `cfc1bbd` before any change: **5,885 / 5,885, 0 skipped** (Domain 462, Application 1,396, Documents 484, Infrastructure 1,319, Ui mixed 1,983, OperationsCalendarView 30, Phase15ERealWindowLayout 144, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 54) | confirmed |
| Zero Release build warnings/errors | baseline Release build: 0 errors (its warning count was not captured); final clean Release build (`bin`/`obj` deleted, `--no-incremental`): 0 warnings, 0 errors | confirmed at close |
| Schema 18, Personal Sync unchanged | source constants and empty diffs since RC.13 | confirmed |
| RC.13 published and frozen; RC.14 not published | tag, local frozen hashes, no rc.14 tag | confirmed |
| Local-test packaging | `Build-PortableRelease.ps1 -LocalTestLabel … -LocalTestVersion …` (frozen-release guard, marker file) | used as documented |
| UI test / screenshot infrastructure | `tools/visual-fixtures` (Skia headless), real-window run-alone classes, `tools/perf-benchmarks` | reused |

## 2. Phase 35 Windows validation status

Only what the repository records or the product owner reported is listed. Claude ran no Windows test.

| Milestone | Local candidate | Windows status |
|---|---|---|
| 35A shell, tokens, motion | `RC14-Phase35A-LOCALTEST` | **Owner-confirmed on Windows** (recorded in the 35B/35C/35D records) |
| 35B Passdown workspace | `RC14-Phase35B-LOCALTEST` | **Owner-confirmed** |
| 35C Products & Pricing | `RC14-Phase35C-LOCALTEST` | **Owner-confirmed** |
| 35D Shows, Notes, navy title bar, chevrons | `RC14-Phase35D-LOCALTEST` | **Owner-confirmed** |
| 35D compact date selector (`01d3553`) | `RC14-Phase35D1-LOCALTEST` | **Not independently confirmed** — no record of a separate Windows run |
| 35E Calendar (`5ea4d0f`) | `RC14-Phase35E-LOCALTEST` | **Reported as Windows-tested in the 35G brief only**; no repository record or per-item report. Treated as owner-reported, not evidenced |
| 35F Operational Notes, History, Settings | `RC14-Phase35F-LOCALTEST` | **Not executed / not reported** |
| 35E Dashboard follow-up (`2ec6261`) | `RC14-Phase35E-DASHBOARD-LOCALTEST` | **Not executed / not reported** |
| 35G (this phase) | `RC14-Phase35G-LOCALTEST` | **Not executed** — checklist `docs/releases/rc14-phase35g-localtest-windows-checklist.md` |

Distinctions kept throughout: *automated tests passed* (Mac) · *Mac visual and performance checks passed* (headless
Skia, development evidence) · *Windows manual tests passed* (only the four owner-confirmed rows) · *Windows manual tests
not executed* (everything else). No final RC.14 installer/updater validation has been done.

## 3. UI inventory and audit matrix

Method: source review of every view/style, a mechanical sweep of all view markup (accessible names, literal colours,
motion scoping, timers, stale wording), about 1,000 real Avalonia renders (§6), the full deterministic suite and the
timing harness (§8). Headless renders are not Windows DPI evidence.

| Area | Appearance | Navigation | Responsive | Accessibility | Theme | Motion | Data correctness | Performance | Tests | Known limitations |
|---|---|---|---|---|---|---|---|---|---|---|
| Shell (sidebar) | consistent tokens | same 6 commands, Notes-edit guard | icons-only below 1,100 px | names, landmark, focus-visible | L/D tokens | width/opacity only under `Window.motion` | — | toggle ≈ one layout | 15E, 22B, 35A | collapse animation width only |
| Native navy title bar | DWM caption colours | native | native | native commands | not over high contrast | none | — | — | 35D | Windows 11 only; verified on Windows in 35D |
| Dashboard | approved reference | 8 routed actions, none online | 1000 / 680 px content breakpoints | every button named; status in words | L/D | none | exact dated prices only; no invented metrics | first render +≈20 ms vs 35F (35E record) | 35E Dashboard | Daily Feature math shown as source text |
| Calendar | approved | Open Passdown, ‹ › | fixed widths | cell names: date, hours, status, selected, perks | L/D | none | Eastern operating date; markers by type + shape | §8 | 35E Calendar, 32A | — |
| Passdown Overview | approved | tabs, date selector | toolbar fits 960 | named inputs | L/D | none | autosave/date guards unchanged | §8 | 35B, 35D | — |
| Products & Pricing | approved | one popover | popover repositions | Enter/Esc/focus restore | L/D | popover fade under motion | override separate from official; decimal only | §8 | 35C (31) | text overrides not supported (schema) |
| Shows | approved | expand per row | — | toggle names | L/D | none | official schedule only | §8 | 35D | — |
| Passdown Notes | approved | Manage notes | — | named | L/D | none | inclusion ≠ resolve | — | 35D | — |
| Operational Notes | approved | dialog, filters | list/detail | dialog focus/Esc | L/D | scrim fade under motion | conflict-safe save | §8 | 35F | **date-picker placeholder fixed in 35G** |
| History | approved | filters local only | ContainerQuery < 600 px | named | L/D | none | immutable; stored PDF only | §8 | 35F, History tests | search is metadata-only |
| Settings | approved | section list | — | switches named | L/D | — | same settings | §8 | 35F, 22F, 23C | **Data Refresh wording fixed in 35G** |
| Dialogs, popovers, menus | shared styles | Esc/close | stay on screen | names | L/D | overlay fade only | — | — | 35B/C/F | — |
| Live PDF companion | unchanged pipeline | page/zoom/fit | divider | named | L/D | none | latest selected date | §8 | 33, 34B | print is raster (unchanged) |

Mechanical sweep results: every `Button`/`ToggleButton`/`CheckBox`/`ComboBox`/`TextBox`/`ListBox` in `Views/*.axaml` has
an accessible name or visible text; **no literal hex or named colours** in any view; every `Transitions` setter is
under a `Window.motion` selector (plus the `Window.noMotion` reset); no `KeyFrame` animation; the only timer is the
existing one-minute Dashboard shows tick. Observation, not changed: page-title sizes differ by design — Dashboard 22
(approved reference), Notes/History/Settings 20 (`PassdownFontSizePageTitle`), Passdown/Calendar 17 (compact
workspace headers from their references); several screens use literal 11.5/12/12.5 px sizes equal to the caption,
subsection and metadata tokens. Changing either would reopen approved designs without a concrete defect.

## 4. Defects found and corrected

| # | Defect | Evidence | Fix | Tests |
|---|---|---|---|---|
| D1 | Operational Notes editor: the empty **Effective through** picker (disabled while "Until resolved" is ticked — the default for a new note) showed Avalonia's raw format watermark `<MMM d, yyyy>`; Effective from would too when empty | 35F renders, Light and Dark, every size | `PlaceholderText`: "Until resolved" while ticked, otherwise "Choose a date" (`OperationalNotesViewModel.EditorEffectiveThroughPlaceholder`, presentation only; the placeholder never becomes a date) | `Phase35GPolishTests` (view model + every `CalendarDatePicker` in the views has a worded placeholder) |
| D2 | Settings → Data Refresh "Status and Check Online" said Check Online is "in the Passdown's official source details" — that area was removed in 35B; Check Online is in the Passdown **Actions** menu | source review | wording now names Official Data on the Dashboard and the Passdown's Actions menu | `Phase35GPolishTests` (wording, plus the Dashboard card and Actions menu item it names exist) |
| D3 | Packaged `TESTER-GUIDE.txt` still described the pre-35 UI (Fury 325 banner, horizontal navigation and gear, "More ▾"/"Copy ▾", Operations Overview, Draft chip, "Passdown Preferences") | text review | steps 6, 8, 9, 11, 12 rewritten for the current screens (version header and install text left for the RC.14 bump) | existing `TesterGuidePackagingTests` |

Previously reported items re-verified (no regression found): navy title bar code path unchanged since 35D (Windows check
1.2–1.4); original icon `logo.ico` byte-identical (`e866bc82…61bdb4f`); dropdown chevron geometry (35D tests); compact
date selector and Today only off today (35D tests); Passdown toolbar fit at 960 (real-window tests); price popover
anchoring and edge repositioning (35C tests + renders); Shows long-schedule expansion (renders); Notes selection
(renders); Calendar marker distinctions (type + shape + legend); Dashboard without fabricated metrics (35E tests);
Settings switch/section styles (renders).

## 5. Dead-code clean-up

| Item | Investigation | Action |
|---|---|---|
| `Controls/ShowColumnsPanel.cs` (Phase 23B multi-column Dashboard shows panel) | no XAML, runtime, binding, resource, design-time, visual-fixture or reflection use since 35E-3; only two tests asserting the Dashboard does **not** use it | **removed** (108 lines); the two negative assertions stay valid |
| `Assets/Branding/fury325-banner.jpg` (135,816 B) | unreferenced since 35A; kept only until the 35A shell was accepted on Windows (it was); the 35A plan (§6.9, §13) listed its removal for 35G | **removed**; three tests that pinned the asset as embedded were replaced by "retired banner not embedded, no photo asset, no reference" contracts (`Phase22BShellNavigationTests`, `Phase34CEmbeddedAssetTests`) |
| Style classes with no current user (`heroPanel`, `statusInfo`, `statusNotPublished`, `statusUnavailable`, `statusBadge.info`/`.success`) | part of the Phase 22A shared status vocabulary, pinned as the design system by `Phase22ADesignFoundationTests` | kept (not dead UI; removing would weaken a design-system contract) |
| Old sidebar / left-rail / menu styles, Calendar templates, duplicate tokens, converters, prototype controls | sweep of every `PassdownStyles` selector against all views and code: no orphaned rail/strip/banner selectors remain (removed in 35A/35B); all five converters are referenced | nothing further to remove |

No AssociateViewModel refactor; no compatibility or migration path touched.

## 6. Visual regression renders

Real Avalonia views rendered with `tools/visual-fixtures` (headless Skia, synthetic data, never the real profile —
its file timestamps were unchanged after every run). **Mac renders are not Windows DPI evidence**; 100 / 125 / 150 %
scaling, high contrast and Narrator are Windows checklist items 13–14.

| Run | Coverage | Result |
|---|---|---|
| Full run, all 153 scenarios, Light and Dark (stopped at the owner's request after it used ≈18 GB — §8) | **831 captures**: every scenario at 1920 × 1080 and 1536 × 960, about a third at 1280 × 720 | reviewed by eye (sample below); its geometry report is written only at the end, so it was lost when the process was stopped |
| Per-scenario runs (one process each; stopped at the owner's request to save time) | **170 geometry-checked captures** at all four sizes, Light and Dark: every `Shell-*`, `Workspace-*` and `Settings-Animations*` scenario, the Operational Notes editor and the new long-path Settings scenario | 168 with no horizontal overflow and no button past the window edge. Flag: `Workspace-LongContentScrolled` at 1280 × 720 reports 20 px of horizontal extent in the Daily Feature text box's own scroll viewer; the capture shows the text wrapped, no sideways scroll bar and nothing clipped (not at 960, 1536 or 1920) — recorded for the Windows check, not treated as a defect |
| Not freshly rendered at 960 × 640 / 1280 × 720 | Dashboard, Calendar, Products, Shows, Notes, History and most Settings scenarios | covered by the existing real-window layout tests (`Phase15ERealWindowLayoutTests`, `Phase33PreviewWindowTests`, `OperationsCalendarViewTests`: reachability, no clipping, no overlap, no horizontal scroll at 960 × 640 through 1920 × 1080; all passing) and by the 35B–35F records' own four-size renders |

Reviewed by eye (Light and Dark): Dashboard, Calendar with Pass Perks, Passdown Overview, Products with the price popover
open, Shows with a 16-time schedule expanded, Operational Notes editor, History detail, Settings (incl. a long sync
path at 960 × 640). Defect found: D1 (§4), fixed and re-rendered. Other cases asked for in the brief and where they
are covered: preview hidden / collapsed sidebar (`Workspace-*`, `Shell-*`), empty states (`Notes-Empty`,
`History-Empty`, `Dashboard35E-NothingYet`), long text (`Dashboard35E-LongText`, `Workspace-LongContentScrolled`),
many items (`Associate-Products-Scroll1400`, Dashboard busy shows), dense Calendar months (`Calendar-Busy`,
`Calendar-PassPerksGrid`), maximized windows (sizes only — window chrome is not rendered headless).

**Reference comparison:** no approved reference file is stored in the repository (they live in the owner's
`~/Downloads`); 35G did not reopen any approved design, so no new comparison was made. Each screen's comparison with its
reference is in its own phase record (35B–35F).

## 7. Theme and motion

Verified by source and existing tests (Phase 35A/23C suites, all passing): Light, Dark and System; per-device
`appearance.json`; Interface animations default **On** only when the file is missing/unreadable; explicit Off removes
every transition (styles exist only under `Window.motion`); Windows Animation effects Off overrides On without
rewriting the saved choice (re-read on activation); theme changes write only `appearance.json`. No new timer, transition
or looping motion in 35G. Windows Animation effects, high contrast and Narrator remain Windows checklist items.

## 8. Performance and memory (Mac, development evidence only)

All figures: macOS Arm64, headless Avalonia + Skia, synthetic data, no real profile (profile file timestamps
unchanged). **Not Windows measurements.**

**Phase 34 protections — `tools/perf-benchmarks all`, second of two runs, loose Lato files removed from the harness
output; compared with the Phase 34C record (same tool):**

| Benchmark | Phase 34C | 35G (`c4fe167`) | Verdict |
|---|---|---|---|
| First PDF cold, generate / first preview work (median of 7) | 76.3 / 82.6 ms | 75.9 / 82.1 ms | unchanged |
| First PDF after background warm-up | 10.2 / 13.4 ms | 10.5 / 13.8 ms | unchanged — warm-up intact |
| Warm-up cost on its background thread | 79.9 ms | 80.6 ms | unchanged |
| Preview edit → page image, busy / 80 notes (leading edge + quiet window) | 16.6 / 40.9 ms | 11.9 / 27.7 ms | no regression (faster on this run) |
| Direct-to-bitmap render, busy / 80 notes | 0.65 / 2.63 ms, 1.3 / 1.8 KB per render | 0.67 / 2.65 ms, 1.3 / 1.8 KB | identical — direct rendering intact |
| Price History product switch via reader, 0 / 2,000 / 20,000 synced | 0.12 / 0.14 / 0.53 ms | 0.13 / 0.14 / 0.49 ms | unchanged — snapshot reader intact |
| Price History open via reader, 0 / 2,000 / 20,000 | 3.3 / 10.7 / 90.4 ms | 3.1 / 10.0 / 87.7 ms | unchanged |
| Official POSTs for a 7-day window | 21 → 9 coalesced | 21 → 9 coalesced | identical — Accesso coalescing intact (rules untouched) |
| Print band loop per 600 DPI page, busy / 80 notes | 64.8 / 104.4 ms, 225 MB | 64.7 / 103.5 ms, 225 MB | unchanged |

**UI timings — scratch harness (not committed): the real `MainWindow` + `MainViewModel` over the visual-fixtures
synthetic data at 1280 × 860; each step = action → dispatcher drained → layout → rendered frame; two runs (run 1 /
run 2, medians).** These are a snapshot of the final Phase 35 UI; they are not directly comparable with numbers from
earlier phases' differently built harnesses.

| Measure | Run 1 | Run 2 |
|---|---|---|
| Startup: fixture + window + first frame, cold / warm median of 10 | 1,133 / 209 ms | 1,033 / 258 ms |
| First visit: Calendar · Passdown · Operational Notes · History · Settings | 90 · 97 · 43 · 19 · 63 ms | 70 · 76 · 38 · 26 · 36 ms |
| Return navigation (median, any section) | 2.4–4.4 ms | 2.1–4.1 ms |
| Calendar next month (incl. local month load) / date selection | 77.8 / 63.1 ms | 72.0 / 60.5 ms |
| Passdown tab switch / Products open / price popover open | 2.2 / 2.6 / 4.8 ms | 1.7 / 1.6 / 3.9 ms |
| Text edit → preview image / checkbox → preview image / date switch → preview image (inline scheduler, no quiet window: processing cost only) | 17.7 / 16.4 / 21.7 ms | 16.0 / 15.4 / 18.5 ms |
| Preview page navigation | 5.2 ms | 4.8 ms |
| Operational Notes selection (7 notes) / History search keystroke | 1.9 / 3.8 ms | 1.7 / 6.0 ms |

**Memory (retained = managed heap after three full collections; allocated = total bytes allocated):**

| Loop | Result | Reading |
|---|---|---|
| Shows "View times" expand/collapse, 500 then 1,500 more | 500: −2.8 MB (run 1) / +9.4 MB (run 2); next 1,500: **+0.8 MB** both runs; 15–16 MB allocated per 500 | plateaus — first-cycle realization and GC noise, **not** growing retention. The earlier "≈6 MB after 500" observation is the same first-cycle effect |
| Keyboard focus moved across 12 controls, 500 then 1,500 more | +14.3 MB, then +40.4 MB (linear, ≈28 KB per focus change); ≈115 KB allocated per change | the known headless focus-visual growth recorded before Phase 35C (≈25 MB / 500 then) — pre-existing, not introduced by Phase 35. Windows long-session check is checklist item 20 |
| Full navigation through all six sections, 1,000 cycles (isolated probe) | 85 → 167 MB after the first 200 (all pages realized once), then **+6 MB per 200** cycles | **same on RC.13** (`cfebdc9`, identical probe): 51 → 114 MB, then +6.3 MB per 200. Pre-existing and unchanged by Phase 35; the ≈50 MB higher floor is the richer Phase 35 pages |
| Closed scenario windows in the screenshot harness | every closed window stayed reachable (40 / 40, ≈12 MB each) → the first full render run reached ≈18 GB and was stopped | **harness defect, largest cause fixed in 35G-1**: one shared appearance controller per process; each Settings view model subscribes for the app's lifetime. With a controller per scenario, windows in the probe are collected (managed heap flat at 36 MB over 20 fixtures). A full single-process run still grew to ≈5.8 GB after 223 captures (other per-scenario state, not investigated further), so the final renders ran **one process per scenario** (peak a few hundred MB) and the README now says so. The app creates one controller and one main window per session, so this is not a production leak |

Investigated earlier observations: **35F distant-note selection ≈20 ms** — Notes code unchanged since 35F apart from the
placeholder; the 7-note fixture selects in < 2 ms, the 200-note jump was not re-measured (no new evidence either way).
**Dashboard first render +≈20 ms** — Dashboard unchanged in 35G; attribution in the 35E record stands. **Intermittent
Ui test-assembly stall** — did not recur in the two full deterministic runs of this phase (baseline and final, with
`--blame-hang-timeout 180s`); frequency remains "seen once in 35E". No performance rewrite was made.

## 9. Operational data, PDF and History integrity

No file under `src/Carowinds.Passdown.Documents`, `Infrastructure/Storage`, `Infrastructure/PersonalSync`,
`Application/PersonalSync`, `Infrastructure/Retrieval` or `src/Carowinds.Passdown.Updater` changed since RC.13
(`cfebdc9`); 35G changed none of them. Phase 35 business-facing source changes are limited to the Dashboard snapshot
(read-only pricing selector and cached Pass Perks, 35E) and presentation. The deterministic suite covers official /
stale / unpublished / unavailable / failed / manual distinctions, override Save/Cancel/Use Official, numeric-only
overrides, Current Deals, Pass Perks markers and month-long summary, Price History, Eastern operating dates, notes
inclusion independence, autosave and recovery, Personal Sync, finalized History immutability (stored PDF opened/copied,
never regenerated), all three templates and the three Copy options, preview stale-date protection and printing view
models. Windows printing itself is checklist item 16.

## 10. Automated tests and build

| Gate | Result |
|---|---|
| Baseline on `cfc1bbd` (before any change) | **5,885 / 5,885**, 0 skipped |
| Final (tree = `c4fe167` source/tests), after deleting every `bin`/`obj` | **5,888 / 5,888, 0 failed, 0 skipped** — Domain 462, Application 1,396, Documents 484, Infrastructure 1,319, Ui mixed **1,986** (+3), OperationsCalendarView 30, Phase15ERealWindowLayout 144, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 54 |
| Clean Release build (`CarowindsPassdown.slnx`, `--no-incremental`) | **0 warnings, 0 errors** |
| Memory guard (kill any testhost above 3 GB) | never triggered |
| New tests | `Phase35GPolishTests` (3): editor placeholder in words and change notification; every `CalendarDatePicker` in the views has a worded placeholder; Settings Data Refresh names the real Check Online / Official Data locations |
| Replaced tests (intent kept, nothing skipped) | `Phase22BShellNavigationTests.BannerImage_IsEmbeddedInTheDesktopAssembly` + `BannerAsset_IsASingleReasonablySizedPanoramicJpeg` → `RetiredBannerImage_IsNoLongerEmbedded` + `DesktopSources_ContainNoPhotoAsset_AndNoMarkupOrCodeReferencesTheRetiredBanner`; `Phase34CEmbeddedAssetTests.BrandingBanner_IsStillAnEmbeddedResource` → `RetiredBrandingBanner_IsNoLongerEmbedded` (count unchanged: 3 → 3) |
| Covered areas (all in the partition above) | real-window UI classes, PDF generation and rendering (all templates), History immutability, Personal Sync, pricing and Price History, Pass Perks, official sources, migrations, backup/recovery, updater/signature, packaging |
| `RealReleaseZip` against the 35G ZIP | passed |
| Live-website tests (`LiveSmoke`) | NOT EXECUTED (opt-in by design) |

## 11. Windows local test package

| | |
|---|---|
| Source commit | `c4fe167` (marker `LOCAL-ONLY-TEST-BUILD.txt` names it; no uncommitted-source note) |
| Command | `Build-PortableRelease.ps1 -LocalTestLabel RC14-Phase35G-LOCALTEST -LocalTestVersion 0.1.0-rc.14-LOCALTEST.phase35g` (PowerShell 7.6.10 local tool on the macOS build host) |
| ZIP | `artifacts/local-test/RC14-Phase35G-LOCALTEST/LOCAL-ONLY-RC14-Phase35G-LOCALTEST-win-x64.zip` |
| Size / files | **76,140,069 bytes** (72.6 MB); **308 files** (169,472,551 bytes uncompressed) |
| SHA-256 | **`e599cbdf133c16af12feb31ed339b7602e6e704f9079cee34d8eb3064b4ed079`** |
| Shown as | `0.1.0-rc.14-LOCALTEST.phase35g` (source `<Version>` still `0.1.0-rc.13`) |
| Validation | script content validation on staging folder and ZIP (308 files) passed; `RealReleaseZip` passed; no database, WAL/SHM, draft, History, key, `.update.json` or `.sig` in the ZIP; packaged tester guide is the 35G text |
| Earlier artifacts | all **6,827** earlier files under `artifacts/releases` and `artifacts/local-test` (RC.13 frozen files included) re-hashed byte-identical after the build |

Checklist: `docs/releases/rc14-phase35g-localtest-windows-checklist.md`. Production update assets were not created.

## 12. Architecture and specification review

- **Product specification (`AGENT_BUILD_SPEC.md`): reviewed — no change required.** 35G fixes wording and a
  placeholder; no business rule, workflow, default or acceptance criterion changes. The file is part of the owner's
  uncommitted reorganization and was not edited.
- **Architecture documentation: reviewed — no architectural change.** No dependency, DI, state ownership, data
  contract, retrieval, persistence, sync, PDF, lifecycle, threading, configuration, security or packaging change.
  `ShowColumnsPanel` and the banner are not mentioned by any current-state document. The current-state documents are
  in the owner's uncommitted, actively edited reorganization (modified today), so they were **not** edited. The two
  stale Dashboard sentences reported in 35E are **already corrected** there. Suggested update when that work is
  committed — `docs/architecture.md` line 3, replace "Phase 35G not started in this baseline." with: "Phase 35G
  (final polish and RC.14 readiness) implemented locally — record `docs/phase35g-final-uiux-polish-readiness.md`;
  final Windows validation pending."
  Optionally in `docs/architecture/desktop-ui.md`: "The unused Phase 23B `ShowColumnsPanel` and the retired Fury 325
  banner image were removed in Phase 35G."
- **Phase 35 plan** (`docs/phase35-unified-ui-modernization.md`): status line and §13 35G entry reconciled (historical
  sections untouched).

## 13. RC.14 release readiness (not published)

| Item | State |
|---|---|
| Phase 35 components | 35A–35F + Dashboard + 35G all implemented and committed locally |
| Outstanding Windows validation | 35D date selector (unconfirmed), 35E Calendar (brief-reported only), 35F, Dashboard, 35G — all covered by the single 35G checklist |
| Release asset convention | three files: `Carowinds-Operations-0.1.0-rc.14-win-x64.zip`, `.update.json`, `.update.json.sig` (GitHub prerelease `v0.1.0-rc.14`, not latest) |
| Version bump (after Windows approval and authorization only) | `Directory.Build.props` `<Version>0.1.0-rc.14</Version>`; `ReleaseNotes/release-notes.md` (version line must match — `BundledReleaseNotes_MatchTheBuiltVersion`); `packaging/windows/TESTER-GUIDE.txt` header and rc.13 file names; `packaging/windows/README-portable-release.md` version text |
| What's New | draft below; goes into `release-notes.md` with the bump |
| README / tester guide | tester guide UI steps updated in 35G; version text at bump |
| Packaging | `Build-PortableRelease.ps1` production mode from the clean bump commit (frozen guard active), `Test-ReleasePackage.ps1`, `RealReleaseZip` |
| Signing | `carowinds-release-1` key outside the repository; sign manifest; verify with the app's `appsettings.json`; tamper checks |
| Upgrade test | RC.13 → RC.14 in-app update in `CarowindsTest` (procedure as RC.13 record §8, with rc.13 → rc.14; draft, note and Price History kept; `pre-update-0.1.0-rc.13-to-0.1.0-rc.14…` backup) |
| Rollback | RC.14 has no schema change, so a user can return to the RC.13 ZIP with the same data folder; restore the pre-update backup if anything is wrong. Published assets are never replaced — a fix ships as rc.15 |
| Frozen RC.13 integrity | re-hashed after the 35G build: unchanged (§11) |

### Draft RC.14 What's New

> **Version: 0.1.0-rc.14 — Title: A redesigned, easier-to-use Carowinds Operations**
>
> - **New look and navigation**: a navy title bar with the original icon and a left sidebar (Dashboard, Calendar,
>   Passdown, Operational Notes, History, Settings) that can collapse to icons.
> - **Cleaner Passdown workspace**: a compact date selector, Create PDF and one Actions menu, section tabs and the live
>   PDF preview beside your work.
> - **Products & Pricing**: grouped rows with official and display prices side by side, and a small price editor next to
>   each price (Enter saves, Esc cancels, Use official price resets).
> - **Shows and Notes**: select-all / clear, expandable long schedules, and clearer daily and persistent notes.
> - **Redesigned Dashboard** (Admissions Overview) and **Operations Calendar** with clearer Pass Perks markers.
> - **Operational Notes, History and Settings**: search and filters, a read-only History detail with Open Final PDF /
>   Save PDF Copy, and Settings grouped into sections.
> - **Light and Dark** consistency, optional subtle **interface animations** (respecting Windows Animation effects), and
>   keyboard and screen-reader refinements.
> - **Compatibility**: no database change; drafts, notes, History, Price History, Personal Sync and PDF styles work as
>   before. No new official data sources, no pricing-rule changes, and PDF contents are unchanged.

## 14. Known issues and blockers

- **Windows validation not executed** for 35F, the Dashboard, the 35D date selector, the 35E Calendar (brief-reported
  only) and 35G — the 35G checklist covers all of them. 125 % / 150 % DPI, Narrator, high contrast, Animation effects,
  printing and Windows performance are Windows-only checks.
- Pre-existing, unchanged by Phase 35 (§8): retained memory grows slowly with keyboard focus changes and section
  navigation in the headless harness (same rate on RC.13). Worth a Windows long-session observation (checklist 20);
  no fix attempted without Windows evidence.
- The Daily Feature is shown as entered on the Dashboard (math markup not rendered) — unchanged known limitation.
- Price overrides remain numeric only (text overrides would need a schema/sync decision).
- Page-title sizes differ by approved design (§3).
- The current-state architecture documents are in the owner's uncommitted reorganization; the suggested 35G wording
  (§12) is not yet applied there.
- **Blockers for publication:** Windows acceptance of the 35G candidate; owner authorization for the RC.14 version
  bump, signing, push, tag and publication.

## 15. Remaining steps before publication

1. Owner runs `docs/releases/rc14-phase35g-localtest-windows-checklist.md` on Windows (`CarowindsTest`) and reports
   PASS/FAIL per section; fix any FAIL in a follow-up local candidate.
2. Owner commits (or asks to commit) the documentation reorganization, including the suggested 35G wording (§12).
3. Explicit authorization to prepare RC.14: version bump commit (§13), full deterministic suite and clean Release build
   on it, What's New text approved.
4. Production build, package validation, manifest signing and verification; re-hash frozen artifacts.
5. RC.13 → RC.14 in-app update test on Windows.
6. Separate explicit authorization to push, tag `v0.1.0-rc.14` and publish the three assets as a prerelease.

Phase 35G implemented locally. Awaiting final Windows validation and separate authorization before publishing RC.14.
