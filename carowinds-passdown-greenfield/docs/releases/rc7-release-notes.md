# Carowinds Operations 0.1.0-rc.7 — release notes

Release candidate for controlled technical testing on Windows 11 x64.

| Milestone | Status |
|---|---|
| Built | **Yes**: Release build 0 warnings / 0 errors; 3980/3980 deterministic tests |
| Packaged | **Yes**: portable ZIP (primary) + experimental single-file EXE |
| Artifact verified | **Yes, on the macOS build host only** (integrity, inventory, versions, embedded assets, packaging validator) |
| Windows tested | **No**: `docs/releases/rc7-windows-validation.md` is entirely NOT EXECUTED |
| Distribution approved | **No**: QuestPDF distribution approval pending; Fury photograph redistribution rights pending |

Artifacts, sizes and SHA-256: `docs/release-manifest.md` "0.1.0-rc.7". Built from commit `6dbdce8`.

## What changed since RC.6

RC.7 packages Phases 22A–22F plus the small Phase 23 release corrections. The database, draft,
snapshot and sync formats are unchanged (DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1), so there is
no migration.

### UI/UX redesign (22A, 22B, 22F)

- One shared light/dark design system. Neutral light surfaces, charcoal dark surfaces, blue accents,
  and clearer contrast and hierarchy. Popups, tooltips and overlays use the same surfaces and follow a
  live Windows theme switch.
- **Fury 325 banner**: a compact photographic header with "CAROWINDS OPERATIONS", embedded in the app
  (no loose image file).
- Short navigation: **Dashboard · Calendar · Passdown · Notes · History**, plus a trailing Settings
  gear. Fully keyboard accessible, with a stable shell layout that wraps instead of clipping.
- **Settings** is one organized page: Appearance, Data Refresh, Personal Sync, Diagnostics and About
  this build. Problems show in the error style, and a mixed-version sync caution is visible.

### Dashboard (22C)

- A unified **Operations Overview** with park hours, the official daily Park Ticket reference, and
  attendance / pre-solds.
- Official-data status is compact and collapsible. Errors and stale sources stay visible while it is
  collapsed.

### Associate Passdown (22D, 22E)

- Refined date bar (previous/next, date popup, Today) and action toolbar (Check Online, Create PDF,
  Copy ▾, More ▾).
- Improved Overview, Products, Notes and Shows. Official and manual values are clearly distinguished.
- **Number formatting**: attendance and pre-solds are grouped with commas (38,500) on screen, in PDFs
  and in Teams Copy. Grouping is applied when you leave the field, so the cursor never jumps while
  you type.
- **Pricing presentation**: starting prices read "From $x". Exact prices print plain.
- Season Price History follows the theme.
- **Save failure wording (Phase 23)**: when a local save genuinely fails, the Draft chip now says
  **"Save failed — changes not saved"** on an error background, with the reason in its tooltip and
  Narrator help text. Before this change it said "Unsaved changes", the same as an ordinary pending
  edit. Autosave, dirty state, retry on the next change, and sync-conflict handling are unchanged.

### Calendar, Shows, Notes and History (22E, 22F)

- Calendar Shows are collapsed to a one-line summary; "View schedule" expands the full schedule.
- Correct Calendar → Passdown and Notes navigation for the selected date.
- Selecting another date never shows the previous date's details, including during rapid switching.
- Denser, clearer Notes and History rows. History gate times are labeled.

### Twelve-month park hours (22E.1)

- Deep Refresh (6 AM task, startup catch-up, manual) discovers the official park schedule about
  **twelve months ahead** with one request per month (about 20 requests in total), instead of one
  request per date.
- **WinterFest and New Year's Eve** operating hours are recognized as Verified (including NYE closing
  after midnight). Before this change they showed as Not Published.
- The main gate is identified by its stable official ID. Today and the near term are refreshed first.

### Eastern time and accessibility (22F)

- Operational timestamps show the Eastern wall clock with "ET" whatever the PC's time zone. Newly
  generated PDF footers are also Eastern.
- Accessibility: named icon buttons and progress bars, "Current page" / "Current section"
  announcements, visible focus, Enter/Space activation, muted adjacent-month days in the date picker.

### PDFs (22C)

- **Shows moved to the end** of all three templates (Classic, Compact, Modern), after Operational
  Notes. Frozen History PDFs are never regenerated and stay exactly as finalized.

## Not in this release

Phase 22G (FIMS data capability audit, historical ride data check) and the standalone
`tools/research/fims-ride-monitor` research tool are **research only**. They are not part of the
application, not in the ZIP or EXE, and RC.7 does not depend on them. RC.7 has **no** Special Events
display, maze hours, Ride Schedule History, intraday ride monitoring, live ride status, downtime
reporting, or new FIMS retrieval sources.

## Known limitations

- **A persistent save failure followed by a date change can freeze the app.** This has existed since
  RC.1. While every save attempt keeps failing (for example a read-only or full disk), changing the
  Passdown date, Check Online, or adding/removing/replacing an operational note retries the save
  without limit. The app stops responding and must be closed with Task Manager. Closing the window is
  not affected (it gives up after 5 seconds). Edits that could not be saved are lost either way.
  Planned fix: RC.8. Testers: restore write access before doing any of those actions.
- Math Daily Feature paragraphs are drawn as shapes in PDFs (not selectable or searchable).
- Historical pricing and show data from before local recording cannot be reconstructed. It is shown as
  unavailable, never invented.
- Not yet validated on Windows: rendering, 100/125/150% DPI scaling, Narrator, the native folder
  picker, Task Scheduler, OneDrive and two-PC sync.
- The single-file EXE is experimental. It carries no license notices or refresh helpers, so it must
  not be distributed on its own.

## Upgrade precautions

- **Back up `%LOCALAPPDATA%\Carowinds Passdown` before the first launch** (app closed, no 6 AM task
  running).
- Extract RC.7 into a **new** folder. Do not extract over RC.6.
- Same storage location, database filename (`carowinds-passdown.db`), scheduled-task name
  ("Carowinds Passdown Daily Refresh") and Personal Sync conventions as RC.6. If the app folder moves,
  run "Enable Automatic Refresh.cmd" again from the new folder.
- **Rollback**: copying an older executable over newer binaries is not necessarily a safe rollback.
  Unchanged schema numbers do not mean every newer value is understood by an older build. To go back,
  restore the pre-upgrade backup with the app closed, then run the older build.

## Personal Sync mixed-version warning

Older versions may not understand the latest Show draft selections and may discard them on re-save.
That loss then syncs to every PC. **Upgrade all PCs sharing a sync folder together**, and back up the
shared folder first. The app does not technically prevent an older version from writing to the sync
folder.

## Distribution status

For controlled technical testing only. Do not upload, publish or distribute it widely.

- **QuestPDF distribution approval pending.** QuestPDF 2026.7.3 runs with `LicenseType.Community`.
  Whether the intended use qualifies under the Community License has not been confirmed.
- **Fury photograph redistribution rights pending.** The photographer, license and permission to
  redistribute the embedded photograph are undocumented.
