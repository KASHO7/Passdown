# Visual fixtures (synthetic screenshot harness)

Phase 22F. Renders the real Carowinds Operations views to PNG with Avalonia Skia under the headless
platform, using the `Carowinds.Passdown.Ui.Tests` fakes and the synthetic data in `Fixtures.cs`.

- No database, no network, no desktop lifetime, no real profile. Every value is invented; show names
  start with "Synthetic".
- Not part of `CarowindsPassdown.slnx`. The normal build and the deterministic test count never depend
  on it.
- Renders are evidence for human review, not pixel assertions. Headless Skia on macOS or Linux is **not**
  Windows DPI validation. 100%, 125% and 150% scaling must still be checked on Windows.

## Run

```bash
dotnet run -c Release --project tools/visual-fixtures -- <outDir> [label] [scenarioFilter|*] [all|main] [forced|system|all]
```

- `outDir`: where PNGs and `<label>-geometry.txt` are written. Use a scratch folder, not the repository.
- `label`: file-name prefix (default `shot`), for example `before` and `after`.
- `scenarioFilter`: case-insensitive substring of a scenario name, such as `Calendar` or `Settings`.
  Use `*` for all scenarios.
- `all` (default) renders 960×640, 1280×690, 1280×860 and 1920×1000. `main` renders only 1280×860.
  `shell` (Phase 35A) renders 1920×1080, 1536×960, 1280×720 and 960×640.
  An explicit comma-separated list such as `1280x720,960x640` (Phase 35G) renders only those sizes.
  Every size is rendered in both Light and Dark.
- Appearance (Phase 23C): `forced` (default) renders the Light and Dark preferences, each against the
  opposite simulated OS appearance, and keeps the `Light`/`Dark` file names. `system` renders the System
  preference under a simulated OS Light and OS Dark (`SystemOsLight`/`SystemOsDark`). `all` does both.
  Themes go through the real `AppearanceThemeController` with an in-memory preference store; the test
  machine's own appearance and the real `appearance.json` are never used.

**Memory (Phase 35G).** Every scenario creates a full app window. A single process that renders many scenarios keeps
growing: the first full four-size run reached about 18 GB. A fresh appearance controller per scenario removed the
largest cause (closed windows held by a shared controller), but one process still reached about 5.8 GB after 223
captures. For large runs start **one process per scenario** (the scenario filter is a substring) and watch memory, for
example:

```bash
for name in Dashboard35E-Typical P35F-Settings; do dotnet run -c Release --no-build --project tools/visual-fixtures -- /tmp/vf "$name" "$name" shell; done
```

Each such process peaks at a few hundred MB.

**Process limits (Phase 36B).** The harness enforces the memory guidance instead of relying on it:

- It refuses to start (exit code 2) when the filter, sizes and appearance modes would take more than **48
  captures** in one process — `*` with every size is about 1,200. The message names the planned count.
- It stops before the next window (exit code 3) once the process working set passes **3,072 MB**, after writing
  `<label>-geometry.txt` for the captures already taken.
- For one deliberate run, raise a limit with `CAROWINDS_VISUAL_FIXTURES_MAX_CAPTURES` or
  `CAROWINDS_VISUAL_FIXTURES_MAX_WORKING_SET_MB` (positive whole numbers; anything else is exit code 2).

The limits never change the images: before/after renders in the same minute are byte-identical (Phase 36B).

`<label>-geometry.txt` records, for each capture, the page's vertical scroll, any horizontal overflow,
and any button pushed past the window's right edge. When Today's Shows is present it also records the panel's
page top and height, where Today at a Glance starts (none since Phase 35E), the entry width and column count, and how many text
or button rectangles inside the panel overlap.

## Scenarios

| Area | Scenarios |
|---|---|
| Dashboard | busy operating day: 38,500 projected and 21,240 pre-solds, verified Park Ticket, partial source failure (Fast Lane unavailable, Promotions stale), Important note, shows, Daily Feature math |
| Dashboard (Phase 35E follow-up) | `Dashboard35E-*`: the approved Admissions Overview — typical day with today's dated price list and the month's Pass Perks (top and scrolled to the end), sidebar collapsed, nothing checked yet / no draft, stale hours and prices with a failed show check and an unreadable Pass Perks cache, long names with a finalized passdown, keyboard focus on Open Passdown, Source details expanded. Synthetic data in `Phase35EDashboardFixtures.cs` |
| Dashboard Shows (Phase 23B) | `DashboardShows-*`: typical and busy days (page top and scrolled to the panel), busy with one list expanded, one show, no shows, not published, stale, failed latest check with last-known-good — 10:45 AM ET on the operating date |
| Calendar | busy day with Shows collapsed and expanded, WinterFest (Dec 5, noon–9 PM), New Year's Eve (3 PM–1 AM), historical (Sep 12), Not Published (Mar 2027), stale hours, show check failed, a date change held mid-load, a failed local read |
| Associate | Overview, Products (exact, From, Military/First Responder, override, stale), Notes, Shows, autosave failure, Season Price History overlay |
| Products compact (Phase 31C) | `Associate-Products-Scroll700`/`-Scroll1400` (synthetic Current/Upcoming/Expired deals and Important Details from `Phase31CFixtures.cs`), `Associate-Details-*`: the View details dialog for a bundle deal, a deal whose sources disagree, the Gold pass (grouped terms, inferred date) and an add-on with no recorded terms |
| Popups | date picker, Actions menu (Phase 35B: Print, Save PDF, Copy for Teams, PDF style, preview, Check Online, Price History), Math picker |
| Notes / History | populated and empty |
| Settings | sync off, syncing and up to date, conflict review, folder unavailable with sync error and a Diagnostics export failure |
| Shell (Phase 35A) | `Shell-*`: sidebar expanded / collapsed on Dashboard and Passdown, Calendar, Settings, keyboard focus on a destination (expanded and collapsed); `Settings-Animations*`: Interface animations On, Off and Windows reduced motion. Sidebar state and motion choice are in memory only |
| Workspace (Phase 35B) | `Workspace-*`: Passdown toolbar, section tabs and Overview sheet with the preview — sidebar expanded / collapsed, preview hidden, a focused input, a focused section tab, Products tab selected, manual hours + private buyout + gate override open, the Early Ride Time editor open, and a long Daily Feature scrolled to the end |
| Shows / Notes (Phase 35D) | `P35D-*`: Shows & Entertainment default, a 16-time schedule expanded, sidebar collapsed, preview hidden; Passdown Notes default, a persistent note included, sidebar collapsed, preview hidden, long daily notes with blank lines; the Actions dropdown open; `P35D-Date-*` the compact date selector on today (no Today button) and on another date (Today shown). Synthetic persistent notes come from `Phase35DFixtures.cs` (in memory) |
| Settings long path (Phase 35G) | `G35-Settings-LongPath`: Settings → Personal Sync with a very long synthetic folder path (`SyncFixture.LongPath`), to check wrapping without horizontal overflow |
| Appearance (Phase 23C) | `Appearance-Settings`, `Appearance-SaveFailed`; `LiveSwitch-*`: the theme flipped with Settings, Dashboard Shows expanded, Calendar Shows expanded, Products, Season Price History, date picker, Actions or Math picker already open (captured after the switch), and a simulated OS switch |

To compare against an earlier commit, export that commit with `git archive` into a scratch folder,
copy this folder into it, and run it there. This changes nothing in the repository.
