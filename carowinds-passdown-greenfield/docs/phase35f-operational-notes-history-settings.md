# Phase 35F — Operational Notes, Passdown History and Settings redesign

Status: **implemented locally — awaiting Windows review.** Not pushed, tagged or published. Phase 35E Dashboard remains
outstanding (approved reference not supplied), so 35E stays open. Phase 35G not started.

| | |
|---|---|
| Starting commit | `8de9453` (35E record; Calendar `5ea4d0f`; 35D follow-up `01d3553` present). Baseline re-run: 5,803 / 5,803 |
| Implementation commit | `8967fcc` (35F-1) |
| Release / version | RC.13 frozen; source `<Version>` still `0.1.0-rc.13`; test build shows `0.1.0-rc.14-LOCALTEST.phase35f` |
| Schema / sync / documents | schema 18, Personal Sync format, finalized snapshots and PDF pipeline unchanged |

## 1. References used
`~/Downloads/carowinds-phase35-approved-design-references/`: operational notes HTML + 3 PNGs, history HTML + 3 PNGs,
settings original-icon and motion HTML + PNGs (dark, compact, sync). Settings follows the motion prototype (latest).
No reference missing for these screens. Dashboard reference still missing. No prototype data, dates or JS state copied.

## 2. Screen by screen
- **Operational Notes**: page header + New Note; filter bar (new in-memory search over title/text/category, lifecycle
  segments, Category, Priority, Clear filters); list card (title + lifecycle pill, ≤140-char text preview, category ·
  priority, schedule) and detail card (eyebrow, title, tags, Edit/Resolve, inline resolve confirmation, effective dates,
  resolved time, Note details, persistent-note explanation, record times). Create/edit is an in-page dialog over a scrim;
  page disabled behind it; Escape/×/Cancel discard; focus into Title and back. Same commands, validation, conflict-safe save.
- **History**: header; filter bar (metadata-only search: date forms, finalized time, origin, PDF status; All/Scheduled/
  Recovery/Manual-when-present; Year); list card; read-only detail with Open Final PDF / Save PDF Copy beside the identity
  (stacked under it below 600 px via ContainerQuery), read-only note, Operating day stats, Daily feature, Products (stored
  prices), Shows, Operational notes. Filters never read storage; Calendar's route clears filters that would hide its record.
- **Settings**: section list (8 entries) scrolling one page of group cards; rows with control at right; theme radios side by
  side (Left/Right and Up/Down); Interface animations, Show Pass Perks, Enable Personal Sync as ToggleSwitches bound to the
  same settings (`AppearanceSettingsViewModel.AreAnimationsOn` = existing On/Off choice; Windows reduced motion still
  overrides without changing it); Passdown Preferences renamed Passdown Defaults; Data Refresh information-only.

## 3. Intentional deviations
Reopen not offered (service has no reopen; spec Part R deferred it). History search does not look inside snapshots
(list must not deserialize records). No "Official-source details"/"Sync diagnostics" dialogs or Check Online in Settings
(not existing Settings features). Sync status shows the coordinator's real state, never a fixed "Connected".

## 4. Shared styles
`PassdownStyles.axaml` Phase 35F section: pageHeader, filterBar, searchBox, segment, workspaceCard, listHeader, statePill,
eyebrow, detailTitle, noticeBox, modalScrim, modalDialog. Token `PassdownSearchIconGeometry`. Earlier screens unaffected
(full suite green).

## 5. Tests
5,826 / 5,826, 0 skipped (Domain 462, Application 1,378, Documents 484, Infrastructure 1,319, Ui mixed 1,949,
OperationsCalendarView 30, Phase15ERealWindowLayout 137, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7,
Phase21DMathEditorWindow 4, Phase33PreviewWindow 54). Clean Release build 0/0. New: `Phase35FNotesHistorySettingsTests` (20)
+ 3 real-window tests (Settings section list at 1280/960; editor focus/Escape). Updated to the approved design (intent kept):
35A animations markup and keyboard test (switch), 23C radio count and Tab targets, 15E row-height bounds (+36 notes, +32
history), radios side by side, 22E notes row (pill allowed), 22F gate labels / 175 px column / 860 MaxWidth, 31D title,
32 switch, 15E tab order (ToggleSwitch).

**Incident:** a `<LineBreak/>` inline in the Notes detail made the headless test font stack loop (~63 GB before it was
killed). Removed; a test now forbids LineBreak in these views; UI runs used a 3 GB testhost kill guard.

## 6. Performance (Mac headless Skia, scratch harness, development evidence only; before → after, two runs)
Notes open with 200 notes ~380–400 → ~400–590 ms (noisy); lifecycle filter 24–27 → 28–32 ms; select a visible row
2.7 → 3.1 ms (a jump that scrolls realizes richer rows: ~20 ms); editor open+cancel 11 → 18 ms; search keystroke 22–33 ms;
heap over 200 filter switches stable. History open (300) 23–25 → 35–37 ms; select 2 → 4 ms; search 4 ms. Settings open
58–59 → 64–69 ms. No added storage reads or HTTP.

## 7. Windows test build
`artifacts/local-test/RC14-Phase35F-LOCALTEST/LOCAL-ONLY-RC14-Phase35F-LOCALTEST-win-x64.zip`, 76,197,042 bytes, 308 files,
SHA-256 `e067899ff87995bba39889228497fccdf7b23772040ed980148ff9b43409133b`, from `8967fcc`. Script validation and
`RealReleaseZip` passed; 10,720 earlier artifact files byte-identical. Checklist: `docs/releases/rc14-phase35f-localtest-windows-checklist.md`.

**Windows validation: NOT EXECUTED.** 125/150 % DPI, high contrast and Windows performance are checklist items.

## 8. Phase 35G readiness
Ready once the owner signs off 35E Calendar, 35F on Windows, and the Dashboard (needs its approved reference).
