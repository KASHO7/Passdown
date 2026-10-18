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
dotnet run -c Release --project tools/visual-fixtures -- <outDir> [label] [scenarioFilter|*] [all|main]
```

- `outDir`: where PNGs and `<label>-geometry.txt` are written. Use a scratch folder, not the repository.
- `label`: file-name prefix (default `shot`), for example `before` and `after`.
- `scenarioFilter`: case-insensitive substring of a scenario name, such as `Calendar` or `Settings`.
  Use `*` for all scenarios.
- `all` (default) renders 960×640, 1280×690, 1280×860 and 1920×1000. `main` renders only 1280×860.
  Every size is rendered in both Light and Dark.

`<label>-geometry.txt` records, for each capture, the page's vertical scroll, any horizontal overflow,
and any button pushed past the window's right edge. When Today's Shows is present it also records the panel's
page top and height, where Today at a Glance starts, the entry width and column count, and how many text
or button rectangles inside the panel overlap.

## Scenarios

| Area | Scenarios |
|---|---|
| Dashboard | busy operating day: 38,500 projected and 21,240 pre-solds, verified Park Ticket, partial source failure (Fast Lane unavailable, Promotions stale), Important note, shows, Daily Feature math |
| Dashboard Shows (Phase 23B) | `DashboardShows-*`: typical and busy days (page top and scrolled to the panel), busy with one list expanded, one show, no shows, not published, stale, failed latest check with last-known-good — 10:45 AM ET on the operating date |
| Calendar | busy day with Shows collapsed and expanded, WinterFest (Dec 5, noon–9 PM), New Year's Eve (3 PM–1 AM), historical (Sep 12), Not Published (Mar 2027), stale hours, show check failed, a date change held mid-load, a failed local read |
| Associate | Overview, Products (exact, From, Military/First Responder, override, stale), Notes, Shows, autosave failure, Season Price History overlay |
| Popups | date picker, Copy menu, More menu, Math picker |
| Notes / History | populated and empty |
| Settings | sync off, syncing and up to date, conflict review, folder unavailable with sync error and a Diagnostics export failure |

To compare against an earlier commit, export that commit with `git archive` into a scratch folder,
copy this folder into it, and run it there. This changes nothing in the repository.
