# Phase 23B — Dashboard Shows layout and Fury banner branding

Unreleased development after `0.1.0-rc.7` (next candidate `0.1.0-rc.8`, not packaged here). Two visual
corrections reported from RC.7 testing on Windows: performance times in **Today's Shows** ran into the
venue column, and the Fury banner carried a generic bar-chart mark before **CAROWINDS OPERATIONS**.
Presentation only — no retrieval, parsing, normalization, persistence, PDF or schema change.

## 1. Starting baseline

| | Verified |
|---|---|
| HEAD / branch | `55e718f` on `feature/passdown-redesign` |
| Version | `0.1.0-rc.7` (`Directory.Build.props`, unchanged) |
| Deterministic tests | **4011/4011**, re-run from an untouched `git archive 55e718f` export: 221 Domain + 1089 Application + 852 Infrastructure + 463 Documents + 1281 Ui mixed + 26 + 66 + 2 + 7 + 4 run-alone |
| Release build | 0 warnings, 0 errors |
| Schemas | DB 16 (`Migration0016…` is the last), Draft 4, Snapshot 2, SyncSchemaVersion 1 |
| Unrelated staged files | `.DS_Store`, `.claude/.DS_Store`, `.claude/skills/.DS_Store`, `.claude/skills/pdf-design/SKILL.md` (and unstaged `../.DS_Store`, `docs/.DS_Store`) — left untouched and outside the commit |

## 2. Defect

On a busy day the Dashboard listed each show as one row: name (left), times (middle), venue (right). A
show with many performances (the reported case was the Camp Snoopy meet-and-greet) drew its times
straight through the venue column and, below 1920 px, past the panel's right edge. Reproduced with
synthetic data only (`Phase23BDashboardShowsFixtures`: a 14-performance meet-and-greet, 12- and
10-performance shows, long name, long venue, missing venue, late-night times).

## 3. Root cause

`DashboardView.axaml` row template: `Grid ColumnDefinitions="2*,3*,1.5*"` with the times inside a
`StackPanel Orientation="Horizontal"`. A horizontal StackPanel measures its children with **infinite
width**, so the times TextBlock's `TextWrapping="Wrap"` never engaged; it measured and drew its full
single-line width, and Grid does not clip, so the text painted over the venue cell. Measured on the old
layout (rendered bounds): times TextBlock wider than its 403/551/846 px column at 960/1280/1920 px.

## 4. Previous layout

One row per show, three proportional columns: name 274/372/569 px, times 403/551/846 px, venue
209/283/431 px (960/1280/1920 px windows). Short schedules looked fine; long ones collided.

## 5. New layout

Each show is a compact stacked entry with a thin top rule, no card:

```
Synthetic Camp Meet and Greet                 ← SemiBold, primary text
11:15 AM · 11:30 AM · 11:45 AM · 12:00 PM · +10 more   ← primary text; accent toggle
Synthetic Camp Gathering Spot                 ← muted metadata
```

Entries flow into equal columns (`Controls/ShowColumnsPanel`): as many as fit at ≥ 340 px, at most 3,
filled **row by row** in the existing schedule order, each line as tall as its tallest entry (an expanded
entry pushes the next line down; nothing overlaps). Measured cell widths: 2 × 423 px at 960, 3 × 381 px
at 1280, 3 × 594 px at 1920. Name, times and venue each get the full entry width and wrap inside it.
The freshness line ("Official schedule checked … · latest check failed") now wraps instead of trimming.

## 6. Disclosure

`ShowTimesDisclosure` (Presentation): at most **5** times → all shown, no control. More than 5 → first
**4** and `+N more` (N = total − 4, so never "+1 more"). The toggle reuses the Phase 22C `disclosureHeader`
look (accent, SemiBold, Fluent focus ring) as a `ToggleButton`, inline after the times; expanded it reads
**Show fewer** and lists every time, wrapping across lines in chronological order, venue still under it.
Mouse, Enter and Space toggle it; focus stays on it.

## 7. Time preservation

`ShowRowPresentation` gained `Key` and `Times` (one formatted entry per time, exactly what
`ShowTimes.FormatList` joins: distinct, operating-day order). `TimesText`, `EarlierText`, row order
(next performance, then name), the today/upcoming split, "+N earlier" and "Started earlier today" are
unchanged. No end times, durations, cancellations or time-zone conversion. Times use a no-break space
inside each time so "11:15" never wraps away from "AM".

## 8. Venue handling

Venue is its own muted line under the times; long venues wrap; a show without an official venue has no
venue line (no blank row, nothing invented).

## 9. Schedule status

Unchanged wording and logic: Scheduled, NoShows ("No shows scheduled today."), NotPublished, ParkClosed,
CouldNotCheck, NotChecked, all-started. Stale data shows its older checked time; a failed latest check
keeps the last-known-good rows with "· latest check failed". Only Scheduled produces entries.

## 10. Expansion state

`OperationsDashboardViewModel.ShowRows` wraps each row in `DashboardShowRowViewModel`. Expanded rows are a
`HashSet` of show keys (official id, or trimmed name when the feed has none) in memory only: kept across
the minute tick and local reloads, pruned when a show leaves the list, cleared when the operating date
changes. Never persisted, never synced, never triggers a data read, network call or draft save.

## 11. Fury banner

The three-`Rectangle` "ascending-track" mark and its StackPanel were removed from `MainWindow.axaml`;
nothing replaces it. Photograph, crop (`UniformToFill`), 72 px height, overlay, wordmark style, and
image/rights documentation unchanged.

## 12. Title alignment

`BrandWordmark` sits at `Margin="24,0"`, the same 24 px left edge as `PassdownPagePadding` and
`PassdownPageHeaderPadding` (page date and title). Measured: left edge 24 px, one text line and vertically
centered at 960, 1280 and 1920 px; no empty gap.

## 13. Window icon

`Icon="/Assets/logo.ico"` on the Window and `<ApplicationIcon>Assets\logo.ico</ApplicationIcon>` in the
csproj are separate from the banner and unchanged (guarded by tests).

## 14. Accessibility

- Toggle name names the show: "Show all 14 performance times for Synthetic Camp Meet and Greet" /
  "Show fewer performance times for …". Toggle pattern exposes On/Off (ToggleButton peer).
- The times text carries the full schedule as its accessible name ("Performance times for X: 11:15 AM,
  …"), so no time is hidden from a screen reader even while collapsed. Nothing appears on hover.
- Reading and visual order: name → times → toggle → "+N earlier" → venue; entries in schedule order.
- Colors are shared tokens only (`PassdownTextPrimaryBrush`, `PassdownTextSecondaryBrush` via `muted`,
  `PassdownAccentForegroundBrush`, `PassdownDividerBrush`), all contrast-checked in both themes by
  `Phase22ADesignFoundationTests`. Expansion is shown by text change ("+10 more" ↔ "Show fewer"), not color.
- Windows Narrator: **NOT EXECUTED** (steps in §19).

## 15. Screenshots

`tools/visual-fixtures` scenarios `DashboardShows-*` (Typical/Busy at top of page and scrolled to the
panel, BusyExpanded, One, NoShows, NotPublished, Stale, FailedLkg) × Light/Dark × 960×640, 1280×690,
1280×860, 1920×1000 — 80 before (`55e718f` code) and 80 after captures, reviewed by eye. Before: times
over venues on every busy capture, past the panel edge at ≤ 1280 px. After: no collisions, no clipping,
no horizontal scroll, times wrap when expanded, venues readable, toggles visible in both themes, banner
title aligned with no icon gap. A whole-app sweep (39 scenarios × 2 themes at 1280×860) found no
horizontal overflow or clipped button. Headless Skia on macOS is not Windows DPI validation.

## 16. Measured bounds (rendered, page coordinates)

Shows panel height, px (before → after). Today at a Glance moves by the same amount.

| Window | Typical (4 shows) | Busy (12 shows) | Busy, one row expanded |
|---|---|---|---|
| 960×640 | 124 → 166 | 334 → 408 | — → 421 |
| 1280×690 / 1280×860 | 124 → 166 | 306 → 294 | — → 307 |
| 1920×1000 | 124 → 166 | 279 → 280 | — → 280 |

Overlapping text rectangles inside the panel (before → after): busy 3 → 0 at 960 and 1280, 1 → 0 at
1920; single long show 1 → 0 at every size; typical 0 → 0. Text past the panel edge: busy yes → no at 960
and 1280. Above the fold: the panel starts at page y = 592 in every case (unchanged); viewports are
479/529/699/839 px, so it is below the fold at 960×640 and 1280×690 before and after. A typical day costs
42 px more (three short lines per entry instead of one row); a busy day is about the same height at ≥ 1280
px and 74 px taller at 960 px (two columns). The panel is not excessively tall.

## 17. Tests added (+58; 4011 → 4069)

- `Phase23BDashboardShowsTests` (mixed partition, +40): hidden-count rule (incl. never "+1 more"),
  no-break joining, id keys and name fallback, every upcoming time present, late-night order, earlier
  split, row order unchanged, empty/stale/failed-LKG states, collapsed/expanded text and accessible names,
  short lists not expandable, long names/venues, missing venue, multiple independent expansions,
  expansion surviving minute tick and reload, following the show id when order changes, removed show
  forgotten, new date collapsed, expanding never loads/checks online/saves a draft, markup reading order,
  toggle bindings, columns panel, untrimmed freshness, token-only styles, title-only banner, 24 px margin
  tokens, window/taskbar icon.
- `Phase15ERealWindowLayoutTests` (run-alone, +18): no overlap/clipping/horizontal scroll collapsed and
  fully expanded at 4 sizes × both themes, name→times→venue vertical order, row-by-row columns (2/3/3),
  expanded times wrap within the column, mouse click with Toggle state and name change, Enter/Space with
  focus kept, toggles only on long lists, banner title-only/24 px/one line/centered/window icon present.
- `Phase21CShowsMarkupTests`: Dashboard binding expectation updated to `ShowRows`.

## 18. Known limitations

- The "+N earlier" count for already-started performances stays a count (Phase 21C semantics); the full
  day is in Calendar → Shows.
- Fixed thresholds (5 / 4) rather than width-based: at narrow columns or large text scaling the four
  collapsed times may wrap to a second line (still no overlap).
- A wrapped "Show fewer" sits 6 px right of the times' left edge (toggle padding).
- Pre-existing, not changed here: the Dashboard "Today at a Glance" shows the Daily Feature as plain text,
  so math markup appears raw (`\(325\)`).
- Headless renders and fake-metric tests are not Windows DPI/Narrator validation.

## 19. Windows validation checklist (all NOT EXECUTED)

1. Busy official show day: Dashboard → Today's Shows lists every show; no text collides.
2. Long meet-and-greet schedule: four times then "+N more"; N = total − 4.
3. Venue on its own line under the times, never beside them.
4. Click "+N more": every time appears, wrapped, in order; label becomes "Show fewer"; click collapses.
5. Expanded list: times wrap inside the column; venue still visible.
6. Light mode: name, times, venue, toggle readable.
7. Dark mode: same.
8–10. 100%, 125%, 150% scaling at 960 px and maximized: no overlap, no horizontal scroll.
11. Tab reaches each "+N more"; focus ring visible; Enter and Space toggle; focus stays.
12. Narrator: entry reads name, times (full list), toggle "Show all N performance times for <show>,
    toggle button, off"; after Space, "on"/"Show fewer…"; venue last.
13. Banner shows "CAROWINDS OPERATIONS" only, left edge aligned with the page title, no gap, one line at
    960 px.
14. Window title-bar icon and taskbar icon unchanged.
15. Dashboard refresh (Check Online, minute tick): expanded rows stay expanded; data updates.
16. Calendar → Shows and Passdown → Shows unchanged.

## 20. RC.8 readiness

Development only. `Directory.Build.props` stays `0.1.0-rc.7`; RC.7 artifacts untouched; nothing packaged.
Schemas unchanged; no migration. Phase 23A save-failure behavior untouched (no Associate code changed;
a test confirms expansion never saves). Ready to be combined into `0.1.0-rc.8` after Windows validation.
