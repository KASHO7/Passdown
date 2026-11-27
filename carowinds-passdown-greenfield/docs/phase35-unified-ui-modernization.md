# Phase 35 — Unified Desktop UI/UX Modernization

Status: **35A (shared shell and design tokens) approved by the product owner after Windows testing. 35B (Passdown workspace) implemented locally — awaiting Windows review; record: `docs/phase35b-passdown-workspace-redesign.md`. 35B and 35C (Passdown workspace; Products & Pricing and the anchored price popover) Windows-tested and confirmed by the product owner. 35D (Shows, Passdown Notes, navy title bar, dropdown chevron alignment) Windows-confirmed; its compact date-selector follow-up is awaiting Windows review; record: `docs/phase35d-shows-notes-shared-polish.md`. 35E Operations Calendar and (as a separate follow-up, `2ec6261`) the Dashboard implemented locally — both awaiting Windows review, so 35E is not yet complete; record: `docs/phase35e-dashboard-calendar-redesign.md`. 35F (Operational Notes, History, Settings) implemented locally — awaiting Windows review; record: `docs/phase35f-operational-notes-history-settings.md`. 35G not started.**

Design input: `carowinds-next-phase-uiux-handoff.md` (2026-11-22, outside the repository; read in full).
This is an incremental Avalonia modernization of the existing application, not a rewrite. The HTML
prototypes are visual references only; no HTML, JavaScript state handling or illustrative data is migrated.

## 1. Phase number — evidence

| Check | Result |
|---|---|
| Branch / HEAD at start | `feature/passdown-redesign` @ `cfebdc9` ("RC.13 release record: publication verified"), level with `origin` |
| Newest completed phase | **34C** (`a569401`, docs `f8ed1c5`; `docs/phase34c-repository-cleanup.md`) |
| "Phase 35" anywhere in `docs/`, `src/`, `tests/`, `AGENT_BUILD_SPEC.md` or any commit message | none |
| Next unallocated phase | **35**. Subphases follow the established letter convention: **35A** shell (this milestone), 35B–35G later milestones |

Historical phases, release records and their numbering are untouched.

## 2. Baseline

| Item | Value |
|---|---|
| Release status | **RC.13 published and frozen** — GitHub prerelease `v0.1.0-rc.13`, annotated tag → `d784c60`, record `docs/releases/rc13-release-readiness.md`. Any binary change ships as rc.14 |
| Source version | `0.1.0-rc.13` (`Directory.Build.props`) — not changed by this phase |
| Database schema | 18 (no migration since RC.11) |
| Personal Sync | `SyncSchemaVersion` 1; draft payload schema 4; finalized snapshot schema 2 |
| Deterministic tests | **5,512 / 5,512, 0 skipped — re-run on `cfebdc9` before any change** (Domain 462, Application 1,369, Documents 484, Infrastructure 1,281, Ui mixed 1,751, OperationsCalendarView 26, Phase15ERealWindowLayout 117, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 9) |
| Build | Release, 0 warnings, 0 errors (re-run) |
| Working tree | pre-existing unrelated changes left untouched: `.DS_Store` files, staged `.claude/skills/pdf-design/SKILL.md`, staged `tools/**/.DS_Store` |

## 3. Existing shell and theme architecture (inspected)

| Area | Today |
|---|---|
| Window | `Views/MainWindow.axaml`: native OS title bar (`Title="Carowinds Operations"`, `Icon="/Assets/logo.ico"`), 1280×860, min 960×640 |
| Brand | 72 px Fury 325 photo banner with "CAROWINDS OPERATIONS" wordmark (Phase 22B/23B) |
| Navigation | horizontal `WrapPanel` strip of `Button.appSectionItem` (Dashboard, Calendar, Passdown, Notes, History) + trailing Settings gear with update dot (Phase 22B/22C/23D) |
| Navigation state | `MainViewModel.SelectedSection` (`AppSection`), one `Show…SectionCommand` per destination; Notes-editing guard via `CanExecute`; all six pages constructed once and toggled by `IsVisible` (page state survives navigation) |
| Tokens | `Themes/PassdownTokens.axaml` (Phase 22A): theme dictionaries Light/Dark, typography, spacing 4/8/12/16/24, radii 3/4/6/8, status colors, Fluent overrides, accent `#1E5AA8` |
| Styles | `Themes/PassdownStyles.axaml`: opt-in classes (panel, primaryAction, secondaryAction, linkAction, statusBadge, appSectionItem, disclosure, …) |
| Appearance | `AppearanceThemeController` + `FileAppearancePreferenceStore` (`appearance.json`, atomic, local only, not synced) + `AppearanceSettingsViewModel` (System / Light / Dark radios in Settings → Appearance) |
| Other local preference files | `associate-passdown-template.json`, `passdown-defaults.json`, `calendar-display.json`, `pdf-preview-layout.json` — one small atomic JSON file per preference via `AtomicFileWriter`, under `%LOCALAPPDATA%`, never in the sync folder |
| Animation | **none** in the app's own XAML; no animation preference exists. Fluent's built-in control transitions (button press scale, scroll bars) are active |
| Reduced motion | not detected today. Avalonia 12.1.1 exposes no reduced-motion API, and its Win32 `WndProc` hook (`Win32Properties`) is internal, not public (checked by reflection) |
| Icon | `src/Carowinds.Passdown.Desktop/Assets/logo.ico` is **byte-identical** (SHA-256 `e866bc82…61bdb4f`) to the user-supplied `ChatGPT Image Aug 27, 2026, 02_00_21 PM.ico`. It is already the window, taskbar and EXE icon |

## 4. Design references

| Reference | Status |
|---|---|
| `carowinds-next-phase-uiux-handoff.md` | **available** (Downloads; identical copy in iCloud "Carowinds Operations") |
| Original icon `.ico` / `.png` | **available** (Downloads) — identical to the repository's `logo.ico` |
| `carowinds_products_refined_prototype.html` + 3 PNGs | **available for 35C** (2026-11-26, `~/Downloads/carowinds-phase35-approved-design-references/`): HTML + `products_preview` + `products_compact_preview`; `price_popover_preview.png` missing — the popover was taken from the HTML prototype (see the 35C record) |
| `carowinds_shows_refined_prototype.html` + 3 PNGs | **missing** |
| `carowinds_notes_refined_prototype.html` + 3 PNGs | **missing** |
| `carowinds_calendar_refined_prototype.html` + 4 PNGs | **missing** |
| `carowinds_operational_notes_refined_prototype.html` + 3 PNGs | **missing** |
| `carowinds_history_refined_prototype.html` + 3 PNGs | **missing** |
| `carowinds_settings_original_icon_prototype.html` + PNG | **missing** |
| `carowinds_settings_motion_prototype.html` + 2 PNGs | **missing** |
| `carowinds_settings_refined_prototype.html` + 3 PNGs | **missing** |
| Approved Dashboard / Passdown Overview references | Dashboard: `~/Downloads/carowinds_dashboard_phase35_reference.html` + PNG (recovered 2026-11-26, used in 35E-3); Passdown Overview: used in 35B |
| `carowinds_operations_prototype.html` (Downloads) | present but **not approved** — the handoff says not to treat it as final. Not used |

Searched: the Passdown tree, `~/Downloads`, `~/Desktop`, `~/Documents` (depth 6) and Spotlight.

**Consequence.** 35A proceeds from the handoff's written contracts (§2 approved direction, §5.1 shell, §6 icon,
§7 motion), which fully specify the shell, tokens and motion setting. No screen-specific layout is built in 35A.
**Before 35B–35F each screen's approved prototype/screenshot must be supplied**; a milestone whose reference is
essential and missing stops at that point rather than inventing a design.

## 5. Screen-to-Avalonia mapping

| Approved area | Avalonia target | Milestone |
|---|---|---|
| Shell: title area, sidebar, tokens, motion | `MainWindow.axaml`, `MainViewModel`, `Themes/*.axaml`, appearance/motion services | **35A** |
| Passdown Overview + PDF companion | `AssociateView.axaml` (Overview), `PassdownPdfPreviewView.axaml`, `AssociateViewModel`, `PassdownPdfPreviewViewModel` | 35B |
| Products & Pricing + price popover | `AssociateView.axaml` (Products), `ProductRowViewModel`, `ProductCategorySectionViewModel`, `CustomProductRowViewModel`, `DealItemViewModel`, `PassPerksPanelViewModel` | 35C |
| Shows, Passdown Notes | `AssociateView.axaml` (Shows/Notes), `AssociateShowRowViewModel`, `AssociateOperationalNoteRowViewModel` | 35D |
| Dashboard, Calendar | `DashboardView.axaml`, `OperationsCalendarView.axaml` and their view models | 35E |
| Operational Notes, History, Settings | `OperationalNotesView.axaml`, `HistoryView.axaml`, `PersonalSyncSettingsView.axaml` | 35F |

## 6. 35A design decisions

1. **Title area: keep the native Windows title bar** (original icon + "Carowinds Operations", nothing else) and
   remove the 72 px photo banner. The native bar guarantees minimize / maximize / restore / close, Snap Layouts,
   drag, double-click-maximize and per-monitor DPI with no custom code. A dark title bar in Dark appearance is
   expected (Avalonia.Win32 implements `SetFrameThemeVariant`) but is unverified until the Windows checklist (item A4). A custom extended-client title bar cannot be
   validated on the macOS build host and risks exactly the behaviors the brief requires to stay native. The
   name is not repeated inside the window (no second wordmark). If the Windows review wants the title merged
   into the app surface, that is a separate, Windows-tested change.
   **35D update:** the product owner approved the navy title bar from the reference after Windows review of 35A–35C.
   It is done without custom chrome: the native title bar stays and only its colours change through DWM caption
   attributes (Windows 11+, never over high contrast) — see `docs/phase35d-shows-notes-shared-polish.md` §5.
2. **Sidebar** replaces the horizontal strip: Dashboard, Calendar, Passdown, Operational Notes, History; Settings
   pinned at the bottom (with the existing update dot). Expanded ≈208 px with icon + label; collapsed 56 px icon
   only. No "Admissions"/"Operations" heading, no role label.
3. **Same view models, same commands.** `SelectedSection`, the six `Show…SectionCommand`s, their `CanExecute`
   Notes-editing guard and the six once-constructed pages are unchanged. Collapsing is pure presentation and
   cannot reset a page or start a refresh.
4. **Sidebar state persists per device** in a new local file `navigation-sidebar.json` (expanded by default).
5. **Interface animations** (`interface-animations.json`, new): `On` / `Off`; missing or unreadable file = **On**.
   Effective motion = preference On **and** the operating system does not request reduced motion. The saved
   choice is never rewritten because of the OS setting. Not part of Personal Sync.
6. **Windows reduced motion** = the "Animation effects" switch (Settings → Accessibility → Visual effects), read
   with `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` — the value behind `UISettings.AnimationsEnabled`.
   Read at startup and again whenever the main window is activated (changing the Windows setting means leaving
   the app, so returning re-reads it; no polling, no timers). Non-Windows hosts report "not detected" and the
   in-app choice decides; Settings says so. Windows-only code lives in Infrastructure.
7. **Motion mechanism.** Transitions are declared only in styles scoped under the window class `motion`. With
   motion off the class is absent, so no transition exists (not merely zero-length). Fluent's own button
   press-scale transition is also removed when motion is off. Commands, data and layout never wait on a
   transition: the sidebar's state changes immediately; only its width/label opacity ease.
8. **Tokens are refined, not replaced.** The Phase 22A palette, accent `#1E5AA8` (≈ the suggested `#2866AD`, with
   higher contrast) and type scale stay. Added: navigation surface/indicator brushes, the brand navy
   `#17324D`, control heights, sidebar dimensions, icon geometries, motion durations and easing.
9. **The Fury 325 photograph** stays in the repository (unreferenced) until the 35A Windows review accepts the
   new shell, so rollback is a revert; its removal is listed for 35G.

## 7. Proposed tokens and shared controls (35A)

- Navigation: `PassdownNavigationBackgroundBrush`, `PassdownNavigationActiveIndicatorBrush`,
  `PassdownSidebarExpandedWidth` 208, `PassdownSidebarCollapsedWidth` 56, `PassdownNavigationItemHeight` 36.
- Brand: `PassdownBrandNavyColor` `#17324D` (theme-invariant identity colour).
- Controls: `PassdownControlHeight` 32, `PassdownControlHeightCompact` 28, `PassdownIconButtonSize` 32,
  `PassdownIconSize` 16.
- Motion: `PassdownMotionFast` 120 ms (hover/focus/toggle), `PassdownMotionPanel` 170 ms (sidebar),
  `PassdownMotionOverlay` 150 ms (dialogs/popovers, used from 35B), easing `CubicEaseOut`.
- Icons (original 24-unit geometries, Avalonia `PathIcon`, no icon package): dashboard, calendar, passdown,
  notes, history, sidebar toggle; the existing gear for Settings.
- Shared styles: `Button.navItem` (+ `.active`), `Button.iconAction`, `Border.navSidebar` (+ `.collapsed`),
  motion-scoped hover/press brush transitions for primary/secondary/link/nav buttons. Inputs, combo boxes,
  checkboxes, tooltips, flyouts and status styles were reviewed: already token-driven and consistent; unchanged
  in 35A beyond motion scoping.

## 8. Milestone 35A work items (one local commit each)

| # | Work | Main files |
|---|---|---|
| 35A-1 | This plan | `docs/phase35-unified-ui-modernization.md` |
| 35A-2 | Motion preference: Application enum/resolver/store interface; Infrastructure file store + Windows animation-effects reader; Desktop `InterfaceMotionController` + system source | `Application/Preferences`, `Infrastructure/Preferences`, `Infrastructure/Windows*`, `Desktop/Services`, `App.axaml.cs` |
| 35A-3 | Sidebar preference store + `MainViewModel` collapse state/command | `Application/Preferences`, `Infrastructure/Preferences`, `MainViewModel` |
| 35A-4 | Shell: sidebar XAML, tokens, styles, icons, banner removal; superseded Phase 22B/23B/15E banner/strip assertions replaced with sidebar equivalents | `MainWindow.axaml(.cs)`, `Themes/*.axaml`, Ui tests |
| 35A-5 | Settings → Appearance → Interface animations (On/Off + OS override explanation) | `AppearanceSettingsViewModel`, `PersonalSyncSettingsView.axaml` |
| 35A-6 | Skia visual review renders, docs, local-only Windows test build | `tools/visual-fixtures`, docs |

## 9. Risks

| Risk | Grade | Mitigation |
|---|---|---|
| Existing tests pin the banner/strip (Phase 22B, 23B, 15E) | Medium | Each superseded assertion is replaced by an equivalent sidebar contract (destinations, order, commands, accessible names, landmark, no literal colors, active state by shape + weight + `ItemStatus`); retirements listed in §12 |
| Real-window layout at 960×640 / 150 % | Medium | Collapsed width 56 px; content keeps its own scroll; real-window tests at 960×640, 1280×720/690 |
| Transition scoping leaks (animation while Off) | Low | Tests assert no `Transitions` on shell elements without the `motion` class |
| Windows SPI read wrong / unavailable | Low | Pure resolver tested; P/Invoke isolated, returns "unknown" on failure; Windows checklist item |
| Theme regressions | Low | Tokens parsed per theme in tests; contrast measured for new brushes |
| Preview/perf regression | Low | No change to preview pipeline; Phase 34 preview tests run; startup timing test |
| Schema/sync drift | None expected | Two new local files only; tests assert they are outside the sync folder and never exported |

## 10. Regression requirements

Full deterministic partition green (count recorded at close); clean Release build 0/0; Phase 33/34 preview,
print and save tests; Personal Sync, migration, backup, updater and packaging tests; PDF tests unchanged;
no change to `Directory.Build.props` version, migrations, sync payloads or document templates.

## 11. Windows screenshot checkpoints (35A)

Expanded and collapsed sidebar; light and dark; animation On, Off, and Windows "Animation effects" off;
1920×1080, 1536×960, 1280×720, 960×640; 100 / 125 / 150 % scaling; keyboard focus on navigation; Settings →
Appearance → Interface animations; maximize / restore / snap; Notes-editing navigation guard.

## 12. Rollback

Each work item is one local commit; reverting 35A-4 restores the banner shell (the photo asset is kept); 35A-2/3/5
add only new local files that older builds ignore. No schema, sync or document change to undo.

## 13. Future milestones — plan only (not started)

Each has its own implementation and review gate and needs its approved reference first.

- **35B Passdown workspace** (implemented locally — `docs/phase35b-passdown-workspace-redesign.md`) — compact header (date, quiet Draft/Saved state, one primary Create PDF, one Actions
  menu after a workflow check of high-frequency actions), Overview as one surface with subtle dividers, section
  tabs, real PDF companion pane (existing resizable divider, view modes, zoom, page navigation). All Copy options,
  Print, Save PDF and the Phase 33/34 preview pipeline unchanged; no second debounce.
- **35C Products & Pricing** (implemented locally — `docs/phase35c-products-pricing-redesign.md`) — compact grouped rows; official value and effective display value as text; anchored,
  keyboard-accessible price popover (Enter = Save, Escape = Cancel, Use Official Price clears the override, focus
  restored, repositions near edges, one at a time) committing through the existing override command/validation.
  Details, History, custom items, Current Deals and Pass Perks preserved.
- **35D Shows and Passdown Notes** (implemented locally — `docs/phase35d-shows-notes-shared-polish.md`) — Shows sheet
  with live selection count, Select All / Clear and expandable long schedules; Notes sheet separating daily notes
  (one printed note per non-empty line) from persistent-note inclusion checkboxes that never resolve or edit the note;
  plus two shared fixes approved after the 35A–35C Windows review: aligned dropdown chevrons (fixed-coordinate
  12×12 glyph, Stretch None) and the navy native title bar (DWM caption colours). Commits `65c230a`, `d7d6848`;
  local test build `RC14-Phase35D-LOCALTEST` (SHA-256 `6b67cc89…a6b890b`) — Windows-tested and confirmed by the product owner.
  Follow-up `01d3553`: compact date selector ("Nov 26, 2026", no badge, Today only off today's Eastern date); build
  `RC14-Phase35D1-LOCALTEST` (SHA-256 `d0bc89d3…191fdeb7`), Windows validation not executed.
- **35E Dashboard and Calendar** — approved Dashboard styling without fabricated metrics; Calendar month grid with
  open/closed, draft/finalized and the three Pass Perks marker kinds plus month-long summary.
  **Calendar implemented locally** (`docs/phase35e-dashboard-calendar-redesign.md`): toolbar and fixed month heading,
  flush month card, draft/finalized-only cell line, per-kind marker colours, in-page Show Perks (shared preference),
  labelled selected-date panel with pinned actions; local test build `RC14-Phase35E-LOCALTEST`, Windows validation not
  executed. **Dashboard implemented locally as a follow-up** (`2ec6261`) from the recovered approved reference
  (`carowinds_dashboard_phase35_reference.html` + PNG): gate-operations card, passdown, shows, today's dated pricing,
  operational notes, cached Pass Perks and Official Data; local test build `RC14-Phase35E-DASHBOARD-LOCALTEST`
  (SHA-256 `0702c01f…dbd2ee`), Windows validation not executed.
- **35F Operational Notes, History, Settings** — list/detail and filters; immutable History with stored-PDF
  Open / Save Copy; grouped Settings sections.
- **35G Consistency and readiness** — cross-screen accessibility, DPI, dark mode and motion-off audit, Windows
  performance measurements, remove the unused Fury asset, release preparation through the normal RC process.

## 14. Test changes (35A) — nothing weakened silently

Deterministic total **5,512 → 5,619** (+107): Application 1,369 → 1,378, Infrastructure 1,281 → 1,309,
Ui mixed 1,751 → 1,804, Phase15ERealWindowLayout 117 → 134; Domain, Documents and the other run-alone classes unchanged.
No test was skipped or deleted without a replacement that keeps its intent:

| Earlier test (phase) | Replaced by / changed to | Why |
|---|---|---|
| 22B `SettingsGear_IsATrailingAccessibleIconItem…` | `Settings_IsPinnedToTheBottomRow…`, `Toggle_IsAnAccessibleIconAction…` | Settings moved to the sidebar bottom with a label |
| 22B `Navigation_IsAWrappingLandmark…` | `Navigation_IsOneLandmark_WithNoScrollingOrDecorativeHeading` | vertical list; same single landmark, adds "no heading / role label" |
| 22B `Banner_ShowsOneWordmark…`, 23B `Banner_IsTitleOnly…` | `TitleArea_IsTheNativeTitleBar…`, `TitleArea_IsTheNameOnly…` | banner removed; still "name only, no duplicate" |
| 22B `BannerImage_IsABundledRelativeResource…` | `ShellMarkup_NeverReferencesAUrlOrDeveloperPath` | same no-URL / no-developer-path rule |
| 22B `BannerPhotograph_IsDecorative…` | `EveryItem_HasADecorativeIcon_AnIndicatorSlot_AndOneLabel` | icons are the decorative elements now |
| 22B `BannerTokens_KeepTheHeaderCompact` | `SidebarTokens_KeepTheShellCompact`, `MotionTokens_StayWithinTheApprovedUpperBounds` | new shell geometry |
| 22B `BannerOverlay_GuaranteesWordmarkContrast…` | `NavigationColours_MeetContrastTargets` (Light, Dark) | contrast measured for the sidebar's text and indicator |
| 22B `BannerBrushes_AreThemeInvariant…` (×3) | `BrandNavy_IsThemeInvariantIdentity` | retired banner brushes; brand navy is the identity colour |
| 22B `ItemLabels_ReserveTheSemiBoldWidth…` | `ItemLabels_TrimRatherThanWrap…` | a vertical list cannot move neighbours sideways; it must not grow taller |
| 15E strip/banner geometry, keyboard, theme, photo, title, content-host tests (10 methods) | sidebar equivalents in the same class (reachability at 6 sizes, fixed geometry, active state with indicator, Tab order incl. toggle and `:focus-visible`, Settings at bottom, theme tokens, native title bar, content beside sidebar, labels only, live appearance switch) | same contracts in the new geometry |
| 22A token lists | banner/strip keys → sidebar, control-size and brand-navy keys | retired tokens |
| 23C `Controller_DependsOnlyOn…` | allowed dependency list + `InterfaceMotionController` (local, device-only) | still no sync / database / refresh dependency |
| 23C selector count, two Tab/Shift+Tab tests | theme group still exactly 3 options; Tab now reaches the adjacent Interface animations group on its checked option | new adjacent radio group |
| Release `MainWindow_ShellRetains…Passdown…Label`, 22C section marker | label is now a sidebar `TextBlock`; renamed style section | markup moved |

New coverage: interface-animations store and Windows reader (Infrastructure), resolver truth table (Application),
controller/settings view model (`Phase35AInterfaceMotionTests`), shell state (`Phase35AShellStateTests`), and real-window
collapse/expand, focus, narrow-window compaction, resize at 1920/1536/1280/1100/960, motion On/Off/reduced, rapid
navigation, immediacy and the Settings control (`Phase15ERealWindowLayoutTests`, Phase 35A section).

## 15. 35A results

| Gate | Result |
|---|---|
| Commits | `296807e` plan · `4e49f8f` motion preference · `355d3c2` sidebar state · `25a8ade` shell · `552904f` Settings |
| Deterministic suite (Release, LiveSmoke excluded, six run-alone classes) | **5,619 / 5,619, 0 failed, 0 skipped** |
| Clean Release build (bin/obj removed, `--no-incremental`) | 0 warnings, 0 errors |
| Database schema / Personal Sync / document templates / version | unchanged — no file under migrations, sync payloads, `Carowinds.Passdown.Documents` or `Directory.Build.props` changed since `cfebdc9`; schema 18, `SyncSchemaVersion` 1, `0.1.0-rc.13` |
| New local files | `interface-animations.json`, `navigation-sidebar.json` beside `appearance.json` (atomic, never synced, never exported) |
| Icon | repository `logo.ico` is byte-identical to the supplied original; unchanged as window, taskbar and EXE icon |
| Real profile during renders/benchmarks | file timestamps compared before/after every run: unchanged |

**Performance (macOS Arm64, headless Skia — development evidence, not Windows results).** Baseline = `cfebdc9`
exported with `git archive`; two alternating rounds each, second round shown.

| Measure | Baseline | 35A |
|---|---|---|
| Window build + show + first frame, 1280×860 (cold / warm median) | 897 / 148 ms | 888 / 140 ms |
| Navigate to a section + first frame (median, Dashboard · Calendar · Passdown · Notes · History · Settings) | 2.4 · 3.0 · 6.4 · 1.8 · 1.6 · 1.8 ms | 2.6 · 2.9 · 3.9 · 1.9 · 1.7 · 2.2 ms |
| Sidebar toggle + first frame (Dashboard / Passdown with PDF preview) | — | 4.4 / 9.9 ms (one layout; width never animated) |
| Preview edit → page image (busy / 80 notes, median, both rounds) | 16.6, 16.4 / 41.0, 39.5 ms | 16.7, 16.1 / 40.9, 41.8 ms |
| PDF page render, direct path (busy / 80 notes) | 0.64 / 2.53 ms | 0.64 / 2.60 ms |

**Visual review renders** (real Avalonia views, Skia headless, synthetic data; `tools/visual-fixtures`, size mode
`shell`): expanded and collapsed sidebar on Dashboard and Passdown, Calendar, Settings, keyboard focus on a destination
(expanded and collapsed), and Settings → Interface animations On / Off / Windows reduced — each in Light and Dark at
1920×1080, 1536×960, 1280×720 and 960×640. Geometry report: no horizontal overflow and no button past the window edge
in any capture. Headless renders show the client area only (no title bar) and are not Windows DPI evidence.

**Differences from the approved references.** The refined HTML prototypes and review PNGs were not available (§4), so
35A follows the handoff's written contracts. Known differences to review on Windows: (1) the title area is the native
Windows title bar rather than a custom in-window bar (§6.1); (2) the sidebar has no in-window brand mark; (3) below a
1,100 px window width the sidebar shows icons only and its toggle is unavailable; (4) the Passdown page still has its own
Overview / Products / Shows / Notes rail beside the sidebar — the handoff assigns that to 35B; (5) page headers are each
page's existing header, unchanged until their milestones.

**Known limitations.** Windows reduced motion is re-read when the main window is activated (Avalonia 12.1.1 offers no
public window-message hook); a change made while the app stays focused is picked up on the next activation. On
non-Windows hosts it is not detected and Settings says the in-app choice alone decides. Fluent's built-in scroll-bar
expansion transitions are not removed by Off. Hover transitions are covered for nav, icon, primary, secondary and link
buttons; other screen-specific controls gain motion only in their own milestones.

**Windows test build** (local only, not a release): `artifacts/local-test/RC14-Phase35A-LOCALTEST/LOCAL-ONLY-RC14-Phase35A-LOCALTEST-win-x64.zip`,
75,990,322 bytes, SHA-256 `4b39cfcd7572345a566b00b019dca5f00097c708c15bcac47d7ada21e0cb54af`, 308 files, built from
`552904f`, shown as `0.1.0-rc.14-LOCALTEST.phase35a`. `RealReleaseZip` validation passed; all 152 earlier release and
local-test artifacts re-hashed unchanged. Checklist: `docs/releases/rc14-phase35a-localtest-windows-checklist.md`.

**Windows validation: NOT EXECUTED.** 35A is not Windows-validated until the product owner runs the checklist.
