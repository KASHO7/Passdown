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
  status.
- `system.txt` — OS version, generation timestamp, the sanitized `%LOCALAPPDATA%\Carowinds Passdown`
  path (never the tester's actual home-directory path).
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

