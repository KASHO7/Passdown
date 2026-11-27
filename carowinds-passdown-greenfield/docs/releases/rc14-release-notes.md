# RC.14 GitHub release notes — DRAFT (not published)

Draft body for the GitHub prerelease **"Carowinds Operations v0.1.0-rc.14"** (tag `v0.1.0-rc.14`, prerelease, not
latest). Same format as the published RC.13 body. **Not published; publication is not authorized.** Before posting,
re-check the hash line against `docs/releases/rc14-release-readiness.md` and remove this header block. Paste
everything below the line.

---

# Carowinds Operations RC.14

RC.14 is the Phase 35 interface redesign. Every screen has been refreshed; the data, pricing rules, official sources,
PDF engine and printing pipeline are unchanged.

## A Redesigned Experience

- Navy Windows title bar with the original app icon.
- New left navigation sidebar (Dashboard, Calendar, Passdown, Operational Notes, History, Settings) that collapses to
  icons; in a narrow window it shows icons only.
- Cleaner, more compact layouts built on shared styling, with consistent Light and Dark appearance.

## Updated Dashboard

- The Admissions Overview organizes park hours, gate information (Avenue Entrance, North Gate, Early Ride Time), today's
  Passdown status, today's official dated pricing, Shows, Operational Notes and the publicly advertised Pass Perks.
- Official Data status with expandable source details; Check Online stays a deliberate action.

## Improved Passdown Workspace

- Compact toolbar with a compact date selector (‹ ›, a small calendar, and Today when you are on another date).
- Section tabs: Overview, Products, Shows, Notes.
- Create PDF plus one Actions menu (Print, Save PDF, Copy for Teams, Show/Hide Preview, PDF Style, Check Online, Season
  Price History).
- The live PDF preview stays beside the editor while you switch sections.

## Easier Products & Pricing

- Grouped, compact product rows with official and display prices shown separately.
- Display prices are edited in a small popover anchored to the price: Enter saves, Esc cancels, "Use official price"
  removes the override. Display prices remain numeric, as before.
- Search, Details, Season Price History, Current Deals and Pass Perks are retained.

## Clearer Shows and Notes

- Shows: Select all / Clear and expandable long schedules ("View times").
- Notes: improved daily-note editing; including a persistent Operational Note in a Passdown never edits or resolves it.

## Redesigned Operations Calendar

- Easier-to-scan month grid and a cleaner selected-date panel.
- Distinct markers (shape and legend, not colour alone) for Pass Perks qualifying visits, redemption periods and booking
  deadlines, plus month-long summaries.

## Better Operational Notes and History

- Operational Notes: searchable list and detail layout with clearer New and Edit dialogs.
- History: read-only finalized Passdowns with search and filters. Open Final PDF and Save PDF Copy use the original
  stored PDF; nothing is regenerated.

## Organized Settings

- A section list: Appearance, Passdown Defaults, Calendar, Data Refresh, Personal Sync, Diagnostics, Application Updates
  and About.

## Appearance and Accessibility

- Light, Dark or Use Windows setting; an optional Interface animations setting (on by default). When Windows "Animation
  effects" is off, the app stays still without changing your saved choice.
- Accessible names and visible keyboard focus across the redesigned screens.

## Compatibility

- No database change from RC.11, RC.12 or RC.13 (schema 18). Personal Sync format unchanged — still update every
  computer that shares a sync folder.
- Existing drafts, Notes, Price History, Personal Sync data, finalized History PDFs and PDF styles (Classic, Compact,
  Modern) remain compatible.
- Pass Perks remain Carowinds' publicly advertised program. They do not mean any individual guest's reward has been
  earned or loaded.

## Known limitations

- Printed output is a high-resolution image of each page (unchanged since RC.12).
- The Daily Feature appears as entered on the Dashboard (math is drawn in the PDF).
- Display-price overrides are numbers only.
- An installation of a LOCALTEST pre-release test build will not be offered RC.14 in-app; extract the ZIP instead.

## Install / update

- **From RC.13 (or RC.12 / RC.11):** Settings → Application Updates → Check for Updates → Install Update → Restart and
  Install. The updater checks the signature and every file, and backs up the database first.
- **By hand:** download `Carowinds-Operations-0.1.0-rc.14-win-x64.zip`, extract to a new folder and run
  `Carowinds.Passdown.Desktop.exe`. Windows 11 x64; nothing else to install.
- SHA-256 (ZIP): `15f07778e3c6d74e25da6534feb8f859a523fb99a344ee2b7189b8f29fdaa278`
- The `.update.json` / `.update.json.sig` files are the signed update manifest used by the in-app updater.

## Verification

- Automated: 5,893 deterministic tests passed, 0 skipped; clean Release build, 0 warnings.
- Windows: the final Phase 35 local test build was accepted by the product owner on Windows. The RC.13 → RC.14 in-app
  update has not been run yet; it needs the published release assets and is checked right after publication.

Source commit: `3d603f0`. Prerelease, Windows x64 portable.
