# Support / troubleshooting guide (Phase 7F)

Detailed version of `packaging/windows/TESTER-GUIDE.txt`'s "If something goes wrong" section, for
whoever actually fields a tester's report. Not shipped inside the release ZIP — repo/support-only.
Keep the tester-facing copy simple (spec Part M); this one can use the real vocabulary.

Ask for the diagnostic export ZIP (`Carowinds-Passdown-Diagnostics-YYYYMMDD-HHmm.zip`, produced by
the app's "Export Diagnostics" button) before doing anything else — `summary.txt`, `system.txt`,
`source-status.txt`, and the log excerpt inside it answer most of the questions below without
needing remote access to the tester's machine.

## App does not open

- Confirm the ZIP was fully extracted first (right-click → Extract All), not run directly from
  inside Windows Explorer's ZIP preview — Windows sometimes allows double-clicking a `.exe` inside
  an unextracted ZIP, but file-relative lookups (DLLs, `appsettings.json`) can behave oddly there.
- Confirm the target machine is Windows 11 x64. The release is `win-x64` self-contained; it will
  not run on `win-arm64` natively (Phase 7E's own test ran under x64 emulation on ARM64 successfully,
  but that is not the primary supported path — see `docs/release-strategy.md` Phase 7E caveats).
- Ask for the diagnostics export if the app got far enough to run (see above). If it crashes before
  the UI ever appears, diagnostics can't be exported from inside it — ask instead for a screenshot
  of the exact error dialog, or Windows Event Viewer's Application log around the time of the crash.

## Check Online fails

- This is not necessarily a bug. Confirm the tester still has their previously-saved/cached data —
  a failed check must never erase good local data (`docs/architecture.md`, freshness/last-known-good
  design). If prior data disappeared, that IS a release-blocking defect — escalate immediately with
  the diagnostics export.
- A genuine remote-source outage is expected to recover on its own; ask the tester to retry "Check
  Online" later rather than treating one failure as final.
- `source-status.txt` in the diagnostics export shows per-source last-success/last-attempt
  timestamps and consecutive failure counts — useful for telling "one source is down" from
  "everything is down" (network-wide) from "this one machine can't reach the internet at all."

## Automatic refresh does not seem to run

- Ask the tester to re-run `Enable Automatic Refresh.cmd` — it is always safe to re-run and will
  report whether it registered or updated the task.
- If the app folder was moved since the task was registered, the task still points at the OLD path
  until `Enable Automatic Refresh.cmd` is re-run from the NEW location (`docs/release-strategy.md`
  "Portable move / scheduled-task rebind").
- For a support-capable user, inspect directly:
  ```powershell
  Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh" | Get-ScheduledTaskInfo
  ```
  `LastTaskResult` should be `0` after a successful run. The diagnostic export's `summary.txt`
  reports whether the task is registered at all (existence only, via `schtasks /Query`), but not
  last-run detail — use the PowerShell command above, or `docs/scheduled-refresh-task-scheduler.md`,
  for anything more specific.
- Check `logs\scheduled-refresh-YYYYMMDD.log` under `%LOCALAPPDATA%\Carowinds Passdown` (also
  excerpted in the diagnostics export) for what the last scheduled run actually did.

## Official data status / data looks out of date (Phase 19A)

The Dashboard header and Associate screen show one quiet line:

- **"Official data checked 12:41 PM"** — every official source was verified at (or since) that time.
  "Checked" means verified, even if no value changed.
- **"Checking official data…"** — an automatic check (startup, every four hours) or a Deep Refresh is
  running. The app stays usable.
- **"Some official data couldn't be updated · Last checked …"** — one or more sources failed (names in
  the second line/tooltip). Last-known-good values are still shown. Usually a site hiccup or no internet.
- **"Official data not checked yet"** — a fresh install before the first check.

The app checks automatically at startup (skipped if everything was checked in the last hour, e.g. by the
6 AM scheduled task), every four hours while open, and quietly for a today/future Associate date whose
data is missing or stale ("Checking online for this date…"). Past dates are never checked automatically.
To check **now**, use **Check Online** (one date) or **Run Deep Refresh** (everything, including next
month's park hours) — both work at any time and reset nothing the user typed. Manual values (attendance,
pre-solds, ERT, gate overrides, price overrides) are never replaced by downloaded data.

The Dashboard no longer has a Refresh button: it updates itself whenever something is saved locally or
synced. If it ever shows "Unable to load today's operations summary.", use its **Try again** button.

Log lines to look for in `app-yyyyMMdd.log`: "Startup official-data check …", "Automatic official-data
refresh started/completed/partially failed/failed", "Associate date auto-check …", "Dashboard local data
invalidated". The diagnostics `summary.txt` has an "Automatic official-data refresh" section (last check,
next check estimate, sources not updated). After a network outage the next automatic attempt is four
hours after the failed one (no rapid retries) — press Check Online to retry sooner.

## PDF does not save

- Confirm the tester picked a destination they can actually write to (not a read-only network
  share, not a location Windows blocks without elevation).
- Confirm available disk space.
- The PDF pipeline itself (QuestPDF generation) is covered by `Carowinds.Passdown.Documents.Tests`;
  a save failure reported here is almost always a destination/permissions issue, not a generation
  defect — but escalate if the same destination works for other apps.

## Data appears missing

- Confirm the tester is looking at the correct date — the date picker, not "today" by default,
  drives which draft loads.
- `%LOCALAPPDATA%\Carowinds Passdown` is independent of wherever the app folder was extracted —
  moving/deleting the extracted app folder never touches it (Phase 7D). If data is missing after
  only moving/copying the app folder, that would be a real defect — escalate.
- Never delete or "reset" `%LOCALAPPDATA%\Carowinds Passdown` as a troubleshooting step; preserve it
  for diagnosis. If a full reset is ever genuinely needed, that is a deliberate manual support
  action (rename/back up the folder), not a normal user instruction.

## Dashboard shows "Unable to load today's operations summary."

### Known cause fixed after Phase 17E: rides-only Early Ride Time

- **Symptom:** the app opened, but the Dashboard (the default startup section) showed
  "Unable to load today's operations summary." Refresh did not help and restarting did not help.
  Associate, Calendar, Notes and History all worked. It happened on some PCs and not others.
- **Root cause:** Phase 17D switched Early Ride Time to rides only, so every monthly ERT
  configuration or date override saved since then has no start time (`StartTime = null`) while still
  being `Configured`. The Dashboard's ERT line
  (`Desktop/Presentation/OperationsDashboardPresentation.cs`) still assumed a `Configured` ERT always
  had a start time (`StartTime!.Value`). That threw `InvalidOperationException` ("Nullable object
  must have a value"), and the Dashboard view model reported it as the generic load error.
- **Why only some PCs:** the bug only fired on a PC whose local database had a rides-only ERT for
  **today's** month, or a rides-only override for today. ERT is saved per PC and can arrive through
  Personal Sync. PCs with no ERT for the month, an explicit "No ERT", or an older pre-17D record that
  still has a start time all loaded normally. Database age, migration path, Windows architecture
  (x64 or ARM64 emulation), SQLite contention and startup timing were not factors.
- **Why Refresh "did nothing":** Refresh worked. It started a fresh load each time, but that load
  hit the same data and failed the same way. Tests now confirm that Refresh recovers after a failed
  first load.
- **Fix:** if a `Configured` ERT has a start time (a legacy record), the Dashboard shows the same
  "Starts h:mm AM" text as before. Otherwise it shows the ride list, comma-joined, using the same
  rule as the Associate screen's Early Ride line. With no rides it shows "Configured". "None" and
  "Not configured" are unchanged.
- **Data repair:** none needed. The ERT rows were always valid, and nothing is migrated, rewritten
  or deleted. Old and incrementally upgraded databases are safe. Schema is unchanged
  (DB 15 / Draft 4 / Snapshot 2 / SyncSchemaVersion 1).
- **Regression tests:** `Ui.Tests/DashboardExistingDataLoadTests.cs` runs real migrations, real
  SQLite repositories and the real services/view model. It covers a fresh DB, a DB upgraded from
  schema 8 with a legacy v1 draft and a legacy finalized row, a rides-only monthly ERT, a rides-only
  override, NoERT, a legacy start time, a corrupt draft (still a controlled error), and
  failure-then-Refresh recovery. Also see
  `OperationsDashboardPresentationTests.Build_EarlyRideTimeConfigured*NoStartTime*` and
  `OperationsDashboardViewModelTests.LoadFailure_ThenReloadSucceeds_*` /
  `SqliteBusyOnFirstLoad_ThenReloadSucceeds_*`.
- **Real affected-machine validation:** NOT EXECUTED. No affected Windows PC was available to
  the fix author. When validating on one, do not delete `%LOCALAPPDATA%\Carowinds Passdown`. First
  back up a diagnostics export and the existing `.db`, then install the fixed build over the same
  data and confirm that the Dashboard opens, Refresh works, and the other screens and data are
  unchanged.

### If the Dashboard error appears for another reason

- The full exception (type and stack) is already written to the local app log by
  `OperationsDashboardViewModel` ("Failed to load today's operations dashboard summary.").
  Users see only the short message. Ask for the log, not a screenshot.
- Never delete or reset local data to clear this error. A genuinely corrupt draft row deliberately
  still produces this error rather than being "recovered", and the row is left untouched.

## Which copy is running? (Windows Search / stale portable copies, Phase 18D)

Symptom: opening `Carowinds.Passdown.Desktop.exe` from the current release folder works, but
launching "Carowinds Passdown" / "Carowinds Operations" from Windows Search behaves differently.

**Why this can happen (audited from source, not yet proven on Windows):** the release is a portable
ZIP — nothing registers a Start Menu entry, so Windows Search finds the app only through indexed
files/folders, the "recently opened" list, or shortcuts a user made by hand. Every build has the
same executable name and file description (`Carowinds.Passdown.Desktop`). Builds before 0.1.0-rc.1
also share the file version `0.1.0.0` and the folder name `Carowinds-Operations-0.1.0-dev-win-x64`,
so an older `-dev` copy (e.g. `Downloads\Carowinds-Operations-0.1.0-dev-win-x64 (1)`) looks identical
to every other `-dev` copy in search results. Since Phase 18E each release candidate has its own
version and folder name (`Carowinds-Operations-0.1.0-rc.1-win-x64`, `…-rc.2-…`), so a folder named
`…-0.1.0-dev-…` is always an older build than any RC. All copies share the same data folder (`%LOCALAPPDATA%\Carowinds Passdown`), so an
older copy opens the *same* data with *older* code. Do not assume Windows Search is at fault until
the checklist below proves which file it launched.

**How to tell (in the app):** Settings → **About this build** shows the app name, the version with
the source commit it was built from (e.g. `0.1.0-rc.1 (3cace78)`), and the folder it is running
from (`%USERPROFILE%\…` — the user name is never shown). The app log
(`logs\app-YYYYMMDD.log`) records the same version and full executable path at every start; an
exported diagnostics `summary.txt`/`system.txt` contains the version and the sanitized executable
path. Builds before Phase 18D do not have the About panel or the commit in diagnostics — their
absence is itself a sign of an older copy; a window titled "Carowinds Passdown" (not "Carowinds
Operations") is a pre-Phase-13G copy.

**Manual Windows check (Windows host required — NOT EXECUTED yet):**

1. Press Start and search "Carowinds Passdown", then "Carowinds Operations".
2. For each result, right-click → **Open file location** (for a shortcut, right-click the shortcut →
   Properties → **Target**).
3. Note the exact folder and the `.exe` it points at.
4. Compare it with the folder you extracted the current release ZIP into.
5. Launch the search result and the current-folder `.exe` explicitly, one at a time.
6. In each, open Settings → About this build and compare **Version** and **Running from**.
   (Alternatively: right-click each `.exe` → Properties → Details → **Product version** shows the
   same `0.1.0-rc.N+<commit>` value. *File version* is `0.1.0.N` for release candidate N, and
   `0.1.0.0` for every older `-dev` build.)
7. If they differ, the search result is a stale copy. Note its folder.
8. Also check the scheduled task, which can keep running an old copy:
   `(Get-ScheduledTask -TaskName "Carowinds Passdown Daily Refresh").Actions.Execute` — if it is not
   the current folder, re-run `Enable Automatic Refresh.cmd` from the current folder. The
   scheduled-refresh log now records the executable path of each run too.

**Cleaning up (manual, deliberate):** the app never deletes old folders or shortcuts. Remove an
old program folder only after confirming both: (a) it is not the copy you are running (About this
build → **Running from** points somewhere else), and (b) the scheduled task does not point at it
(step 8). Then close it and delete *that copy's folder*. Never delete `%LOCALAPPDATA%\Carowinds
Passdown` — it holds the shared database, drafts, notes and History for every copy. Unpin/delete any
hand-made shortcut that pointed at the removed folder.

**Mixing builds on synced PCs:** builds before 0.1.0-rc.1 do not know the North Gate / Avenue
override fields and may drop them if they re-save an Associate draft. Put every PC that uses Personal
Sync on the same RC before editing on any of them.

## Copy for Teams does nothing / shows "Couldn't copy to the clipboard" (Phase 18F)

The Associate screen's **Copy ▾** menu (Operations Summary, Today's Pricing, Operations + Pricing)
puts plain text on the Windows clipboard; paste it into Teams with Ctrl+V.

- "Couldn't copy to the clipboard. Try again." — another program briefly had the clipboard locked
  (common with clipboard managers or remote-desktop clipboard sync). Try again; nothing in the draft
  is lost. The technical error is written to the application log.
- "No pricing selected" — no product section (or no product) is selected for the PDF, so there is no
  pricing to copy. Select sections/products the same way as for the PDF.
- Copied prices or gate times look wrong — Copy uses exactly what is on screen for the open date, the
  same as the PDF would. It never checks online; use **Check Online** first if the data is old.
- Real Teams @mentions are not supported: typed "@Name" text pastes as plain text.

## Unsupported / newer database

- If the app fails to open citing a schema/version problem (`UnsupportedSchemaVersionException`,
  Phase 7D Part G), the tester's database was already touched by a newer app build than the one
  currently installed. Do not delete or downgrade the database — get the tester onto a matching or
  newer build instead.
- The database is left byte-for-byte unchanged when this happens (tested:
  `MigrationRunnerTests.Run_DatabaseVersionNewerThanAnyKnownMigration_...`) — there is no data-loss
  risk in leaving it alone while sorting out which build the tester should run.

## Corrupt database

- A database that fails to open as valid SQLite is left untouched by design (Phase 7D Part F) — the
  app fails loudly (a crash is acceptable) rather than silently deleting or replacing it.
- Preserve the file (`carowinds-passdown.db` under `%LOCALAPPDATA%\Carowinds Passdown`) for
  diagnosis; do not delete or overwrite it. Ask the tester to send a copy if remote diagnosis is
  needed — this is the one case where the diagnostics export deliberately does *not* help (Part D:
  diagnostics never include the database file itself), so a direct copy of the `.db` file is the
  right ask here specifically, not the diagnostics ZIP.

## Reading the diagnostics export

- `summary.txt` — version, database existence/schema version/size, automatic-refresh registration
  status. Since Phase 18D the version is the full `<version>+<commit>` build identity, e.g.
  `0.1.0-rc.1+3cace78…` (earlier
  builds printed `0.1.0.0` for every build) followed by `Executable (sanitized): %USERPROFILE%\…` —
  the folder the exporting copy ran from.
- `system.txt` — OS version, generation timestamp, the same version/sanitized executable path, and
  the sanitized `%LOCALAPPDATA%\Carowinds Passdown` path (never the tester's actual home-directory
  path).
- `source-status.txt` — one block per official data source: derived status (`Ok`/`Error`/
  `NeverChecked`), last successful check, last attempt, consecutive failure count. Local-only —
  generating it never performs a network check.
- `logs/scheduled-refresh-YYYYMMDD.log` — the tail of the most recent scheduled-refresh log file, if
  one exists. Already sanitized at the source (see "Log sanitization" below) — safe to read/share
  without further redaction.
- `logs/app-YYYYMMDD.log` (Phase 18B) — the last ≤200 lines of the most recent **interactive app**
  log, if one exists. Path-redacted (user/OneDrive folders become `%USERPROFILE%`, `%OneDrive%`, …).
  Never the full log history, the database, drafts, notes, sync payloads, or PDFs.

## Log sanitization (reviewed Phase 7F, no code change needed)

The scheduled-refresh log (`Carowinds.Passdown.Application.Refresh.ScheduledRefreshRunner`) only
ever writes: a start/completion line with the app version and timestamp, an aggregate summary count,
and per-failure lines carrying `SourceId`/`Category`/`RequestedDate`/`Status`/a plain user-facing
`Detail` string (e.g. "We couldn't check park hours right now.") — reviewed directly against
`Carowinds.Passdown.Application.Retrieval.RetrievalDiagnostics`, which documents that `Detail` is
always a static, non-alarming sentence supplied by the calling source adapter, never derived from an
exception message or a URL. The full exception (`SourceEvidence.TechnicalDetail`) is stored in the
database for developer troubleshooting but is **never** written to this log file or included in the
diagnostic export — confirmed by reading every log call site
(`ScheduledRefreshRunner.LogSummary`/`MigrationRunner`/`ScheduledRefreshHost`). No secrets, cookies,
full HTTP payloads, or draft/note content appear in this log by construction. Safe to read or share
as-is.

## Application log (Phase 18B)

The interactive app writes its own log, separate from the scheduled-refresh log:

| Log | File | Written by |
|---|---|---|
| Interactive app | `%LOCALAPPDATA%\Carowinds Passdown\logs\app-YYYYMMDD.log` | the normal desktop app |
| Scheduled refresh | `%LOCALAPPDATA%\Carowinds Passdown\logs\scheduled-refresh-YYYYMMDD.log` | the `--scheduled-refresh` task |

- One file per UTC day. Contents: startup (version, OS), database schema version after migrations,
  and any failure the screens already report — Dashboard/Calendar/Associate load, autosave, Check
  Online, Early Ride Time saves, Notes, History, Personal Sync passes/conflict counts/failures, Create
  PDF, Export Diagnostics — plus unhandled and unobserved exceptions.
- **Privacy:** only ids, dates, counts, categories, and statuses are logged — never draft text,
  Operational Note bodies, Daily Feature text, PDF content, sync payload JSON, cookies, auth material,
  or HTTP responses. Exception text is logged, but every line is path-redacted *before* it is written
  (Windows account name and OneDrive tenant folder do not reach the file).
- **Retention:** 14 days, for both logs. Pruning runs once at startup (the first line written) and
  deletes only files whose names are exactly `app-YYYYMMDD.log` (or, for the task,
  `scheduled-refresh-YYYYMMDD.log`). Nothing else in the folder is ever deleted.
- A logging failure (disk full, file locked) is ignored; it never stops the app.

## Temporary files the app cleans up (Phase 18B)

- **History "Open Final PDF"** writes `Carowinds-Operations-YYYY-MM-DD.view-<id>.pdf` to
  `%TEMP%\Carowinds Passdown\History\` (a new file per open, so a PDF viewer holding an older copy no
  longer blocks re-opening). Copies older than 24 hours are removed at startup and before each open;
  a failed open removes its file immediately. Only files with exactly these names in exactly that
  folder are ever deleted. "Save PDF Copy"/"Create PDF" never use temp files and never touch your
  chosen save location.
- **Personal Sync** removes its own interrupted-write leftovers (`<file>.tmp-<id>`) from the sync
  folder once they are over 24 hours old. Normal sync files are never touched.

## Dates, times, and prices always show US format (Phase 18B)

By design, the app shows "Saturday, September 19, 2026", "Sep 19, 2026", "10:30 AM", and "$55.00"
regardless of the PC's Windows region or display language. A UK, French, or German Windows setting
does not change month/day names, date order, AM/PM, or the dollar sign. This is intentional — not a
bug — and there is no setting to change it. Times of day for park operations remain Eastern Time.

## A product disappeared / seasonal products changed (Phase 19B)

Products come and go with the official site — that is normal, not an error:

- A product missing from **one** successful check is kept; after **two** consecutive successful checks
  without it, it is retired from the current product list, PDF and Teams copy. Nothing is deleted;
  History is unchanged. If it is published again later it comes back automatically.
- A failed check (no internet, site error) **never** removes a product.
- Ended limited-time offers (e.g. Bring-A-Friend Duo) and the end of SCarowinds no longer cause
  "Some official data couldn't be updated". If that message appears, a source genuinely failed.
- After SCarowinds ends, dates without SCarowinds show no SCarowinds section at all.

Log lines in `app-yyyyMMdd.log`: "Catalog product discovered", "missing from a successful … check
(candidate absence …)", "now inactive (retired from the current catalog)", "reactivated", "season tags
changed", "checked successfully with zero products". The diagnostics `summary.txt` has an "Official
catalog lifecycle" section (active / missing once / inactive / season-tag counts).

## Spell check (Phase 20A)

- Red wavy underlines in note/feature/reason fields are advice only — they never block saving, PDF,
  finalization, Teams copy or closing the app. Right-click a flagged word for suggestions, **Ignore**
  (this session) or **Add to dictionary** (your Windows custom dictionary).
- No underlines at all: Windows has no US English (or current-language) spelling dictionary installed
  (Settings → Time & language → Language), or the spell checker failed — the app log then has one line
  "Spell checking was turned off for this session…". Typing still works normally.
- Spell checking is local to Windows; nothing you type is sent anywhere or written to logs.
- Phase 20C: an underline should disappear as soon as you switch to another screen and come back when
  you return to the field. If a red squiggle stays visible on Dashboard / Calendar / Products, that is a
  regression — note the steps and export diagnostics.

## Historical park hours / "Historical data partially available" (Phase 20C)

- After startup, the app quietly fills in this calendar year's past park hours (newest first) from the
  official park-hours feed. The line under the Operations Calendar month shows "Loading this year's
  history…", "Historical data: N of M dates checked", "Historical data up to date" or "Historical data
  partially available". It is separate from "Official data checked …", which reflects today's and
  upcoming data only.
- **Partially available** is normal: the official feed keeps only a limited history (on 2026-10-16 it
  started at March 25), and the tooltip names the date before which hours are no longer published.
  Those dates are left empty — nothing is guessed. If the tooltip instead says some dates couldn't be
  checked (offline/outage), they are retried automatically the next time the app starts.
- It never delays Check Online, Deep Refresh or automatic checks (it steps aside immediately), never runs
  in the 6 AM scheduled task, and is never synced. Expected disk use: about 0.1–0.2 MB per year.
- App log lines start with "Historical backfill …" (start/resume/finished/yielded/paused). Diagnostics
  `summary.txt` has a "Historical park-hours backfill" section (target year, status, counts, boundary).

## Season pass price history (Phase 20A)

- Associate → Products → **Season pass price history** (or **Price history** on a Season Pass / Season
  Pass Add-On row). Daily tickets and dated Fast Lane intentionally have no history.
- "First seen" is the first successful official check that showed a price; the real change happened
  between the previous row's "last confirmed" date and that date.
- History starts from the first observation recorded by this install (an upgraded install includes
  its earlier official checks). Retired products: tick **Include retired products**.
- Each PC keeps its own history (not synced). The database is now schema 16 — older builds (RC.3 and
  earlier) will refuse to open it.
- Log lines: "Price history baseline created", "Price changed", "Price semantics changed". Diagnostics
  `summary.txt` has a "Season price history" section (counts only).

## Pricing on past dates and Military prices (Phase 21A)

- A past date (after 6:00 AM the next morning) shows only prices recorded for that date. "Historical
  pricing not available for this date." means the app never captured official prices for that day;
  official ticket calendars do not offer past dates, so it cannot be fetched now. This is not an error.
- Current products (e.g. SCarowinds, today's pass prices) are intentionally not shown on past dates.
- Military & First Responder shows the exact price for the selected date when the official store
  publishes one (e.g. `$55.00`), otherwise the official minimum (`From $40.00`).
- Dashboard → Open Passdown always opens today's date. Use the date picker or Calendar for other dates.
- January 1–3, 2026 have no park hours: the official feed no longer publishes dates before late March
  2026.
- Diagnostics `summary.txt` has a "Historical pricing sources" section; logs mention "Dashboard Open
  Passdown reset the Associate date" and "Pass validity year differs".
