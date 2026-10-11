# Phase 18A — Comprehensive Reliability, Security, Privacy, and Release Hardening Audit

Status: **18A complete; 18B complete** — REL-004 fixed in 18B (§13); REL-003, REL-005, REL-007,
PRIV-002, PERF-001, QUAL-001, PKG-002 fixed in 18B (§14). Windows / two-PC OneDrive validation remains
**NOT EXECUTED**.

This document records a whole-application audit of Carowinds Operations (internal name
Carowinds.Passdown) and the targeted fixes made during it. It is not a certification. A passing test
suite is not proof of security, and nothing here claims the application is "secure" in an absolute
sense. The supported claim is:

> **No known Critical or High findings remain within the audited scope.** Seven Medium/Low/Quality
> defects were confirmed and fixed with failing-first regression tests; the remaining Medium/Low items
> are listed below with an ordered 18B plan.

---

## 1. Scope

In scope: every project under `src/` (Domain, Application, Infrastructure, Documents, Desktop), all 15
SQLite migrations, all JSON (de)serialization boundaries, Personal Sync (import, export, conflicts,
path construction, folder switching), diagnostics export, logging, retrieval/HTTP, command-line
parsing, scheduled refresh and its process locks, Task Scheduler scripts, the packaging scripts, and
the existing release ZIP/EXE in `artifacts/releases/` (inspected, not rebuilt).

Out of scope / explicitly not done: the deferred Manager workflow (not implemented, not audited as a
feature), Phase 14 Alerts, any product feature or screen redesign, and rebuilding the release
artifacts (per the phase instructions).

## 2. Baseline (verified before any change)

| Item | Value |
|---|---|
| Source commit audited | `8671e92` (Phase 17F) + `163c53c` (release rebuild, docs/artifacts only); includes `399d40c` Dashboard ERT fix |
| Branch | `feature/passdown-redesign` |
| Working tree at start | Only pre-existing unrelated user changes: `.DS_Store` files and a staged `.claude/skills/pdf-design/SKILL.md`. Not touched by this phase. |
| .NET SDK | 10.0.400 |
| Target framework | `net10.0` (all projects, `Directory.Build.props`) |
| RID / publish | `win-x64`, self-contained, `PublishReadyToRun=true`; ZIP primary, single-file EXE secondary (`packaging/windows/Publish-Windows.ps1`) |
| App version | `0.1.0-dev` |
| DB / Draft / Snapshot / SyncSchemaVersion | 15 / 4 / 2 / 1 |
| Release build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: 0 warnings, 0 errors |
| Deterministic tests | **2182/2182** (116 Domain + 642 Application + 210 Documents + 545 Infrastructure + 669 Ui = 631 together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests`, each run alone per the documented partition) |
| Host | macOS (development); **no Windows host was available** |

## 3. Threat / failure model

This is a local, single-user, per-Windows-account desktop tool with no server, no accounts, no
credentials, and no inbound network surface. The realistic adversaries/failure sources are: corrupt or
unexpected data (OneDrive conflict copies, truncated writes, a different app version, hand-edited files),
a buggy or hostile file placed in the user's own sync folder, unexpected remote HTML/JSON from the
official sources, and Windows environmental conditions (AV scanners, locked files, disk full,
non-US regional settings). Internet-server threat models (auth bypass, multi-tenant isolation, CSRF,
etc.) do not apply.

| # | Boundary | Trust | Parsing | Filesystem access that can result | Failure if mishandled | Current defenses |
|---|---|---|---|---|---|---|
| A | Official HTTPS endpoints (sixflags.com, accessoticketing.com, cloudfront park-hours) | Semi-trusted (authentic but uncontrolled content) | — | None | Bad data shown | HTTPS-only constant hosts; no cert bypass; shared `HttpClient`; 15 s timeout covering body read |
| B | Remote HTML/JSON responses | Untrusted content | System.Text.Json DTOs, regex/HTML parsers, invariant culture | None (written to SQLite only via parameters) | Wrong/missing prices; LKG overwritten | Exact-date confirmation; per-source failure keeps last-known-good; fixture contract tests |
| C | Local SQLite DB | Trusted (app-owned, `%LOCALAPPDATA%`) | Typed column reads | — | Crash on corrupt DB | Corrupt DB never replaced (tests); newer-schema refusal; per-migration transactions |
| D | User-entered draft/note data | Trusted user, arbitrary text | Numeric fields parsed invariant, non-negative | None (never a path) | Bad PDF layout | Parameterized SQL; PDF overflow handling; note length limits |
| E | Paths from file/folder dialogs (PDF save, diagnostics save, sync folder) | User-chosen | `Path.GetFullPath` | Write to the chosen location only | Write failure | Picker-scoped; sync folder may not overlap `%LOCALAPPDATA%\Carowinds Passdown` |
| F | Personal Sync directory | **Untrusted** (any device/user/OneDrive can write) | Strict canonical filename parse | Writes only to app-owned subfolders under the configured root | Path traversal; sync DoS; data corruption | All paths composed from typed `DateOnly`/`Guid`/int ids, never from file content (§10). **Hardened in 18A** (SEC-001..003) |
| G | Sync envelope JSON | **Untrusted** | STJ, no polymorphism, `required` members, string enums | None beyond F | Crash; wrong-entity write | Envelope identity must match filename; **payload validation added in 18A** |
| H | Files changed externally (OneDrive, other device) | Untrusted | As G | As F | Silent overwrite | Revision/hash conflict model; tombstones never inferred from absence |
| I | Generated PDFs | App-produced | — | `%TEMP%\Carowinds Passdown\History`, user-chosen save path | Stale/locked temp file | Fixed date-based filename; SHA-256 + size verified before open/save |
| J | Diagnostics ZIP | Leaves the machine | — | Fixed entry names only | Privacy leak | No DB/drafts/notes/PDFs (existing tests); **log-path redaction added in 18A** (PRIV-001) |
| K | Windows Task Scheduler | OS | — | Runs app with `--scheduled-refresh` | Elevation/injection | Per-user, `Interactive`, `Limited`, no password, fixed task name |
| L | Packaging/helper scripts | Developer/user-run | PowerShell/CMD | Registers one task | Command injection | No user-typed input; `%~dp0` quoted; `-LiteralPath`; `-ExecutionPolicy Bypass` per-process only |
| M | Command-line arguments | Local user | Exact match of one flag | None | Wrong mode | Anything else → exit code `InvalidUsage`; no shell invocation |
| N | Local log files | App-owned | Read back for diagnostics tail | `%LOCALAPPDATA%\...\logs` | Privacy | Only ids/dates/counts logged; exceptions may embed paths → redacted on export (PRIV-001) |
| O | Imported/legacy persisted data | Trusted-but-old | Migrate-on-read draft deserializer | — | Crash on legacy shape | Presence-based backfill; 18A upgrade matrix tests |
| P | App configuration/resources | Embedded | `appsettings.json` | DB file name under `%LOCALAPPDATA%` | DB in wrong place | Embedded, not user-editable; DB path not UI-configurable |

## 4. Methodology

1. Baseline verification (build + full deterministic partition) before any edit.
2. Surface mapping (§3), then source review of each surface, reading actual code paths rather than
   grepping for patterns alone.
3. Automated checks: `dotnet list package --vulnerable --include-transitive`, `--deprecated`,
   `--outdated`; repository-wide credential pattern scan; static signal search (TODO/FIXME, empty and
   broad catches, pragmas, hard-coded paths, `async void`, `DateTime.Now`, culture-sensitive
   parse/format, dynamic SQL).
4. Adversarial/fault tests written **before** fixes: each fix below was verified by reverse-applying
   the source change, rebuilding, and confirming the new test fails, then re-applying.
5. Release artifact inspection (ZIP listing, PE header, embedded debug paths).
6. Full deterministic partition and clean Release build after all fixes.

## 5. Findings table

Severity per the phase rubric. "Fixed" means fixed in 18A with a failing-first regression test.

| ID | Severity | Area | Finding | Evidence | Action | Status |
|----|----------|------|---------|----------|--------|--------|
| SEC-001 | MEDIUM | Personal Sync import | A syntactically valid envelope with a structurally bad payload (`"Payload": null`, null required members, null `SnapshotJson`) threw `InvalidOperationException`/`ArgumentException`/`SqliteException` outside the `catch (JsonException)` and **aborted the whole sync pass**. Because every pass re-reads the same file, all later imports and **all exports** stopped until the file was removed by hand (persistent sync DoS from one file). | `PersonalSyncService` per-entity `catch (JsonException)` only; pre-fix test runs showed `InvalidOperationException: The requested operation requires an element of type 'Object'…`, `ArgumentNullException (snapshotJson)`, `UNIQUE constraint failed: operational_notes.id` | `SyncPayloadValidator` (pure) + decode-failure classification; non-object payload rejected; such files counted Malformed and left untouched | **Fixed** |
| SEC-002 | MEDIUM | Personal Sync import | Note / monthly-ERT / ERT-override payload identity was never checked against the file's identity. `notes/<A>.json` carrying a payload for note `B` rewrote **B** (bypassing B's own conflict detection); `monthly-ert/2026-09.json` carrying `2027-01` wrote January 2027. Undefined integer enum values (`"Category": 99`) and blank titles were persisted. `0000-05.json` was accepted as a month id (later `new DateOnly(0,…)` crash in conflict listing). | `ProcessNoteEnvelopeAsync`/`ProcessMonthlyErtEnvelopeAsync`/`ProcessErtDateOverrideEnvelopeAsync` used `remotePayload.ToDomain()` keyed by payload id; `SyncEntityIdentity.TryParseMonthlyErtId` had no year floor | Payload Id/Year+Month/OperatingDate must equal the file identity; enums must be defined; note title/body limits mirror local validation; year ≥ 1 | **Fixed** |
| SEC-003 | LOW | Personal Sync resources | Sync JSON and history PDFs were read fully into memory with no size bound (`ReadAllTextAsync`/`ReadAllBytesAsync`); a multi-GB accidental/hostile file in the shared folder could exhaust memory on every pass. | `SyncFileReader`, history import | 16 MiB JSON / 64 MiB PDF limits (real files are KB–~1 MB); oversized → Malformed, file untouched | **Fixed** |
| REL-001 | LOW | Migrations | `MigrationRunner` read `user_version` once before the loop. If the interactive app and a `--scheduled-refresh` process started together after an upgrade, the loser re-applied a migration the winner had just committed; a non-idempotent step (Migration0009 `ALTER TABLE ADD COLUMN`) then threw and aborted that process's startup. | Reproduced: `SqliteException: duplicate column name: pdf_bytes` | Re-read `user_version` inside the `BEGIN IMMEDIATE` transaction and skip already-committed steps | **Fixed** |
| REL-002 | LOW | SQLite | Connection string was built by concatenation (`$"Data Source={path};…"`); a path containing `;`/`=` was parsed as extra keywords. Not reachable with a normal `%LOCALAPPDATA%`, but a redirected profile folder could break startup. | Reproduced: `ArgumentException: Connection string keyword 'weird folder' is not supported` | `SqliteConnectionStringBuilder` | **Fixed** |
| REL-006 | LOW | Personal Sync identity | `FileDeviceIdProvider` treated a transient read failure of an **existing** `device-id.txt` (AV/backup scanner lock, permissions hiccup) like a missing file: it minted and persisted a new DeviceId. The device's own exported files then looked "remote", turning every pending local edit/deletion into a spurious sync conflict. | `TryReadExisting` returned false on `IOException`/`UnauthorizedAccessException`; reproduced with an exclusive handle | Existing-but-unreadable → retryable `IOException`, id never replaced (missing/unparseable still regenerates). Doc updated in `personal-sync-architecture.md` §16B.1 | **Fixed** |
| PRIV-001 | LOW | Diagnostics ZIP | The last 200 scheduled-refresh log lines were copied verbatim. Logged exceptions (`IOException`, `UnauthorizedAccessException`) embed full paths, i.e. the Windows account name and OneDrive tenant folder. | `FileScheduledRefreshLogProvider` returned raw lines; `FileLoggerProvider` appends `exception.ToString()` | `DiagnosticPathRedactor`: known per-user roots → `%LOCALAPPDATA%`/`%APPDATA%`/`%USERPROFILE%`/`%TEMP%`/`%OneDrive*%`, then generic `X:\Users\<name>` and `/Users|/home/<name>` patterns | **Fixed** |
| PKG-001 | LOW | Release binaries | Every shipped `Carowinds.Passdown.*.dll` embedded the build machine's absolute PDB path (`/Users/<developer>/Documents/Passdown/...`) — a username/folder disclosure and a non-reproducible byte difference between build machines. | `strings` on the released ZIP's DLLs | Release-only `PathMap` (repo root → `/_/`) in `Directory.Build.props`; Debug unchanged | **Fixed** (takes effect on next release build; existing ZIP/EXE not rebuilt per phase rules) |
| BUG-001 | QUALITY | Operations Calendar | Daily-admission price used the machine culture's `"C"` format; an English (UK)/German region format showed `£54.99`/`54,99 €` for a USD price. Every other price uses invariant `$0.00`. | `OperationsCalendarPresentation.BuildDailyAdmissionPriceText` | Use `PriceFormatting.Format` | **Fixed** |
| REL-004 | MEDIUM | Sync ↔ open editor | After a sync pass imports a remote change to a draft (or note) that is **currently open** in the Associate (or Notes) editor, nothing reloads the view model. The next local edit autosaves the stale in-memory payload over the imported one, the export pushes it, and the other device then accepts it as a safe update — the remote edit is lost **without** a conflict. Requires the same date/note to be open on one device while edited on another. | No sync-completion → view-model reload path (`App.axaml.cs`, `AssociateViewModel`); `SaveDraftAsync` is unconditional | 18B, per product decision "auto-reload clean editors; preserve dirty editors and require explicit conflict resolution" — see §13 | **Fixed (18B)** |
| REL-003 | LOW | Atomic writes | `AtomicFileWriter` (sync files, config, preferences) and `FileDeviceIdProvider` write the temp file without `Flush(flushToDisk: true)` before the rename. A power loss shortly after can leave a zero-length/garbage final file on NTFS (rename is durable, data may not be). Process-crash atomicity is fine. | `File.WriteAllTextAsync` + `File.Move` | Write via `FileStream` + `Flush(true)` (or `FileOptions.WriteThrough`) | **Fixed (18B)** — §14.1 |
| REL-005 | QUALITY | Diagnostics | The interactive app logs only to the console provider (no console for a WinExe), so sync/load/PDF errors in normal use leave no persistent trace; the diagnostics ZIP contains scheduled-refresh logs only. | `App.axaml.cs` `AddConsole()`; file logger only in `Program.BuildScheduledRefreshServiceProvider` | Add the file logger (separate file name) to the interactive host, with PRIV-001 redaction on export | **Fixed (18B)** — §14.2 |
| REL-007 | LOW | Sync folder hygiene | A crash between temp write and rename leaves `<name>.tmp-<guid>` files in the shared OneDrive folder forever (ignored by importers, but synced to every device). | `AtomicFileWriter` temp naming | Best-effort cleanup of own stale `*.tmp-*` older than N hours at pass start | **Fixed (18B)** — §14.3 |
| PRIV-002 | LOW | History temp PDFs | "Open Final PDF" writes the frozen PDF to `%TEMP%\Carowinds Passdown\History\Carowinds-Operations-<date>.pdf` and never removes it; re-opening the same date while a viewer holds the file locked (common with Acrobat on Windows) fails with "could not be opened". | `TempFileFinalizedPdfLauncher` | Unique per-open file name + age-based cleanup | **Fixed (18B)** — §14.4 |
| PERF-001 | LOW | Sync history import | Every pass re-reads and SHA-256-hashes **every remote** history PDF before checking whether the same record (same id + content hash) already exists locally. ~365 records/year × ~0.5 MB ≈ 180 MB read per pass by end of season. | `ProcessHistoryEnvelopeAsync` ordering | Short-circuit when local id + content hash already match — but this changes when a later-corrupted remote PDF is reported, so needs a deliberate decision | **Fixed (18B)** — §14.5 |
| QUAL-001 | QUALITY | Culture | 15 UI date/time formats (`"MMMM yyyy"`, `"dddd, MMMM d, yyyy"`, …) use the machine culture, so a non-English Windows region shows mixed-language month/day names. PDFs, persistence, sync, and parsing are invariant. | Desktop presentation `ToString("MMMM…")` without culture | Decide whether the UI is pinned to en-US (one startup line) — product decision | **Fixed (18B)** — owner decision: pin presentation to en-US, §14.6 |
| PKG-002 | INFO | Packaging | `Build-PortableRelease.ps1` forbidden-pattern sanity check covers `*.git*`, `*.db*`, `CLAUDE.md`, `AGENT_BUILD_SPEC.md` but not `*.log`, `*.cs`, `.DS_Store`, `sync-config.json`, `device-id.txt`. The staging folder is built only from publish output, so this is defense-in-depth. Released ZIP inspected clean. | Script lines 158-165 | Extend the deny list | **Fixed (18B)** — §14.7 |
| INFO-001 | INFO | Dependencies | No vulnerable packages (direct or transitive). Only `xunit` 2.9.3 (test-only) is flagged "Legacy" → xunit.v3. Patch updates exist (Avalonia 12.1.4, MS.Extensions/Sqlite 10.0.12, QuestPDF 2026.9.1); none is security-flagged. | `dotnet list package` output (§6) | Do not auto-upgrade; take patch updates in a normal maintenance phase | Info |
| INFO-002 | INFO | Time | Associate PDF "generated" stamp uses `DateTimeOffset.Now`/`ToLocalTime()` (machine wall clock) rather than `IClock`+Eastern. Correct on an Eastern-time PC; documented semantics unchanged. | `AssociateViewModel.CreatePdfAsync`, `AssociatePassdownDocumentModelBuilder.FormatTimestamp` | No change (do not alter wall-clock semantics without evidence) | Info |
| INFO-003 | INFO | Architecture | PDF hash/size verification for History open/save lives in `PassdownHistoryViewModel.TryValidate` (presentation) rather than the Application retrieval service. Correct today. | `PassdownHistoryViewModel` | Consider moving into `FinalizedPassdownPdfRetrievalService` when next touched | Info |

Counts: **CRITICAL 0 · HIGH 0 · MEDIUM 3** (all fixed: SEC-001, SEC-002 in 18A; REL-004 in 18B) **· LOW 10**
(all fixed: SEC-003, REL-001, REL-002, REL-006, PRIV-001, PKG-001 in 18A; REL-003, REL-007, PRIV-002,
PERF-001 in 18B) **· QUALITY 3** (all fixed: BUG-001 in 18A; REL-005, QUAL-001 in 18B) **· INFO 4**
(PKG-002 fixed in 18B; one new INFO, PKG-003, opened in 18B — §14.9).

## 6. Area results

| # | Area | Result | Notes |
|---|---|---|---|
| 1 | Dependency vulnerabilities | **PASS** | Advisory feed (api.nuget.org) reachable and queried: no vulnerable packages in any project, direct or transitive. |
| 2 | Deprecated dependencies | PASS (INFO) | Only test-only `xunit` 2.9.3 family ("Legacy"). Not shipped. |
| 3 | Secrets | **PASS** | 698 tracked files scanned for key/token/password/private-key/connection-string/JWT/cookie patterns. Only hits are docs/tests describing the *absence* of credentials and test canaries. The Accesso `com-accessopassport-*` headers are public storefront identifiers, not credentials. No rotation required. |
| 4 | SQL injection | **PASS** | Every `CommandText` reviewed. All values parameterized. The only interpolations are compile-time `const` column lists and the integer `PRAGMA user_version = {int}`. No LIKE queries. No user/sync/remote value reaches SQL text. |
| 5 | Deserialization | **PASS AFTER FIX** | System.Text.Json only; no `TypeNameHandling`, `$type`, `JsonDerivedType`, `BinaryFormatter`, or Newtonsoft. Default max depth 64 bounds recursion. Envelope uses `required` members and string enums. Gaps: payload nulls/undefined enums/identity mismatch (SEC-001/002). |
| 6 | Personal Sync path safety | **PASS** | No path is ever built from file *content*. Every path is `SyncPathService` composing typed `DateOnly` (`yyyy-MM-dd`, invariant), `Guid` (`D`), or int year/month; directory listings are non-recursive and only exact canonical names are processed. `../`, absolute, UNC, alternate separators, and DeviceId/SyncSpaceId values never reach a path. |
| 7 | Sync malicious input | **PASS AFTER FIX** | SEC-001/002/003. 17 adversarial tests. |
| 8 | Sync conflict correctness | PASS with one MEDIUM deferred | Existing two-device harness (≈130 tests) covers revisions, tombstones-never-from-absence, dual finalization, hash validation, SyncSpaceId folder switch, idempotence. REL-004 (open-editor stale overwrite) is outside the reconciliation engine. |
| 9 | Cloud-database invariant | **PASS** | DB path = `%LOCALAPPDATA%\Carowinds Passdown\<embedded DatabaseFileName>`, not UI-configurable; `SyncFolderValidator` rejects the sync root being inside, equal to, or a parent of that folder (case-insensitive on Windows, drive roots included). Symlink/junction pointing the sync folder at `%LOCALAPPDATA%` is not resolved (would require the user to create one deliberately) — LOW residual risk, noted. OneDrive Known Folder Move does not redirect `%LOCALAPPDATA%`. |
| 10 | Filesystem safety | PASS (LOW items deferred) | User strings never become file names (PDF/diagnostics names are date/timestamp only). Atomic temp+rename used for sync/config/prefs (REL-003 durability gap, REL-007 orphan temp files deferred). |
| 11 | Atomic writes | PARTIAL | Process crash at any point: final file is either old or new (rename is atomic, temp in the same directory). Power loss: see REL-003. |
| 12 | Diagnostics privacy | **PASS AFTER FIX** | ZIP contains exactly: `summary.txt` (version, DB exists/schema/size/WAL flag, task status), `system.txt` (OS string, 64-bit flag, the literal token `%LOCALAPPDATA%\Carowinds Passdown`), `source-status.txt` (per-source status/timestamps/failure counts), `logs/scheduled-refresh-<date>.log` (last ≤200 lines, now path-redacted). No DB, drafts, notes, Daily Feature text, PDFs, sync payloads, DeviceId, HTTP bodies, or credentials (existing canary tests + PRIV-001). |
| 13 | Logging privacy | **PASS** | All 60+ `ILogger` calls reviewed: only ids, dates, counts, statuses, outcomes. No note/draft/override/payload/PDF/HTTP-body content, no cookies/headers. Exception text may contain paths → redacted on export (PRIV-001). |
| 14 | ZIP / archive | PASS / extraction **NOT APPLICABLE** | Diagnostics entry names are constants; the app never extracts archives. |
| 15 | Network / TLS | **PASS** | All endpoints are hard-coded `https://` official hosts; no `http://`; no certificate callbacks; default handler (no HTTPS→HTTP redirect following); no user-controlled host or path segments; per-request 15 s timeout including body read; caller cancellation distinguished from timeout. |
| 16 | Remote input validation | PASS | Parsers use invariant culture; exact-date confirmation preserved; failures keep last-known-good (existing fixture contract tests). No response-size cap (official sources only — INFO). |
| 17 | HTTP resources | PASS | One shared `HttpClient`; responses disposed; bounded concurrency via `SemaphoreSlim`; results in `ConcurrentBag`; no retry loop. |
| 18 | Command line | **PASS** | Only exactly `--scheduled-refresh` is accepted; anything else (unknown, duplicate, extra) → `InvalidUsage` exit code; no shell invocation. |
| 19 | Task Scheduler / scripts | PASS (static) — Windows **NOT EXECUTED** | Per-user, `LogonType Interactive`, `RunLevel Limited`, no stored password, fixed task name, `Unregister` only that name. CMD helpers take no arguments, quote `%~dp0`-derived paths, no delayed expansion; `-ExecutionPolicy Bypass` is per-process. Paths with spaces/special characters: static review only. |
| 20 | Process locks | **PASS** | `FileShare.None` OS-held locks (released by the OS on process death; a stale lock *file* never blocks). Refresh lock used by scheduled host, startup catch-up, Associate and Dashboard Check Online; sync has its own independent lock. |
| 21 | Async / fire-and-forget | PASS | No `async void`. Fire-and-forget sites (`_ = TriggerRunAsync`, debounce/retry/periodic loops) are inside the coordinator, which catches and logs every non-cancellation exception and coalesces overlapping runs. |
| 22 | UI load/retry/error state | PASS | Dashboard, Calendar, Associate, Notes, History, Sync Settings all reset loading state in `finally`, clear stale errors on retry, and Calendar/Associate guard against stale results with a load version. |
| 23 | Legacy/null data | PASS AFTER FIX | Local legacy drafts are migrated on read; the unvalidated path was remote sync payloads (SEC-001/002). |
| 24 | User input validation | PASS | Attendance/pre-solds: non-negative invariant `int`; prices: non-negative invariant `decimal` (overflow rejected); note title/body limits; free text not over-restricted. |
| 25 | PDF robustness | **PASS** (macOS) | New tests: 1,500-char unbreakable token, 400-char product name, emoji/CJK/Arabic/accents, control and bidi characters, `int.MaxValue` counts, `decimal.MaxValue` price — all three templates generate in bounded time without dropping text. Windows font fallback **NOT EXECUTED**. |
| 26 | Finalization / history integrity | **PASS** | One finalization per date (unique index); repository has no update/delete API; exact PDF bytes stored with SHA-256 + size, verified before open/save; listing never selects `pdf_bytes`; synced records inserted via an immutable import path. |
| 27 | Timezone / date | PASS (INFO-002) | Business dates use `IClock` + `America/New_York` (resolved via ICU on Windows 10 1903+/11). No `DateTime.Today`. |
| 28 | Culture | **PASS AFTER FIX** / QUAL-001 deferred | Persistence, sync, hashing, retrieval parsing, PDFs invariant. BUG-001 fixed and tested under en-GB and de-DE. |
| 29 | Resources / leaks | PASS | Connections/commands/readers in `using`; long-lived subscriptions created once per app lifetime; `CancellationTokenSource`s disposed. |
| 30 | Performance / extreme data | PASS with PERF-001 deferred | Sync passes are O(N files) with per-entity indexed lookups. |
| 31 | Packaging cleanliness | **PASS AFTER FIX** (PKG-001) | Current ZIP: 258 files, no PDB, no `.so`/`.dylib`, no DB/logs/diagnostics/sync data/source/fixtures. Only win-x64 natives. |
| 32 | Single-file EXE | Structural PASS; launch **NOT EXECUTED** | Valid PE32+ x86-64 GUI. Remains secondary/experimental. |
| 33 | Windows-specific validation | **NOT EXECUTED** | No Windows host/VM. Nothing here is inferred as a Windows PASS. |
| 34 | Affected-PC Dashboard validation (399d40c) | **NOT EXECUTED** | No affected PC available. |
| 35 | Migrations | **PASS AFTER FIX** | Versions 1-15 strictly monotonic and additive (`IF NOT EXISTS` everywhere except 0009's `ALTER`); each step in its own transaction; newer-than-supported DB refused; corrupt DB never replaced; no migration fabricates data. New matrix: fresh, 7, 8, 11, 13, 14 → 15 preserving a user draft; mid-migration failure rolls back only that step and is retryable; REL-001 race fixed. |

## 7. Changes made in 18A

| File | Change |
|---|---|
| `src/.../Application/PersonalSync/SyncPayloadValidator.cs` (new) | Pure structural/identity validation for each sync payload type + decode-failure classification |
| `src/.../Infrastructure/PersonalSync/PersonalSyncService.cs` | Use the validator for drafts, notes, monthly ERT, ERT overrides, history; non-object draft payload rejected; history PDF size bound |
| `src/.../Infrastructure/PersonalSync/SyncFileReader.cs` | `MaxJsonFileBytes` (16 MiB), `MaxPdfFileBytes` (64 MiB); oversized → Malformed |
| `src/.../Application/PersonalSync/SyncEntityIdentity.cs` | Monthly ERT id requires year ≥ 1 |
| `src/.../Infrastructure/Storage/MigrationRunner.cs` | Re-read `user_version` inside the write transaction |
| `src/.../Infrastructure/Storage/SqliteConnectionFactory.cs` | `SqliteConnectionStringBuilder` |
| `src/.../Infrastructure/PersonalSync/FileDeviceIdProvider.cs` | Never regenerate on a transient read failure |
| `src/.../Application/Diagnostics/DiagnosticPathRedactor.cs` (new) | Path redaction for exported text |
| `src/.../Infrastructure/Diagnostics/FileScheduledRefreshLogProvider.cs`, `Desktop/App.axaml.cs` | Redact log tail; explicit factory registration |
| `src/.../Desktop/Presentation/OperationsCalendarPresentation.cs` | Invariant USD price formatting |
| `Directory.Build.props` | Release-only `PathMap` |
| `docs/personal-sync-architecture.md` | DeviceId read-failure behavior updated |

No schema change, no migration, no payload/snapshot/sync schema version change: **DB 15 / Draft 4 /
Snapshot 2 / SyncSchemaVersion 1 unchanged**. Sync file format unchanged (only *acceptance* of
malformed input tightened; everything the app itself writes still validates).

## 8. Tests added (52)

| Project | File | Tests | Purpose |
|---|---|---|---|
| Infrastructure | `PersonalSync/PersonalSyncAdversarialInputTests.cs` | 17 | SEC-001/002/003: null payloads, null members, undefined enums, identity-mismatched note/ERT/override, null history snapshot, oversized file; each also proves an unrelated valid import and a local export still happen in the same pass. 15 failed pre-fix; 2 are deliberate controls (normal-size file; a non-member enum *string*, already rejected). |
| Infrastructure | `Storage/MigrationHardeningTests.cs` | 8 | Upgrade matrix fresh/7/8/11/13/14 → 15 with data preservation and no fabrication; failed-migration rollback + retry; REL-001 concurrent-runner race (failed pre-fix with `duplicate column name`). |
| Infrastructure | `Storage/SqliteConnectionFactoryBusyTimeoutTests.cs` (`SqliteConnectionFactoryPathQuotingTests`) | 1 | REL-002 |
| Infrastructure | `PersonalSync/FileDeviceIdProviderTests.cs` | 1 | REL-006 |
| Infrastructure | `Diagnostics/FileScheduledRefreshLogProviderTests.cs` | 2 | PRIV-001 (explicit roots + this machine's profile) |
| Infrastructure | `Packaging/ReleaseBinaryPathPrivacyTests.cs` | 1 | PKG-001 (Release builds only) |
| Application | `Diagnostics/DiagnosticPathRedactorTests.cs` | 6 | PRIV-001 rules |
| Application | `PersonalSync/SyncEntityIdentityTests.cs` | 1 | SEC-002 year-0 id |
| Documents | `Associate/Templates/PathologicalContentTests.cs` | 12 | PDF robustness, 4 scenarios × 3 templates (coverage; no defect found) |
| Ui | `OperationsCalendarPresentationTests.cs` | 3 | BUG-001 under en-GB/de-DE/en-US |

## 8a. Final validation

| Item | Result |
|---|---|
| Release build | `dotnet build CarowindsPassdown.slnx -c Release --no-incremental`: **0 warnings, 0 errors** |
| Deterministic tests | **2234/2234** = 116 Domain + 649 Application + 222 Documents + 575 Infrastructure + 672 Ui (634 together + 26 `OperationsCalendarViewTests` + 10 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests`, each run alone — the unchanged documented partition for the known Avalonia headless cross-test flake, which was not touched) |
| Fail-before/pass-after | Verified for every fix by reverse-applying the source change, rebuilding, and running the new tests |

## 9. Residual risk

- **REL-004** is the most important open item: a silent cross-device overwrite is possible when the
  same draft/note is open on one PC while edited on another. Data is not corrupted, but one device's
  edit can be lost without a conflict prompt.
- Power-loss durability of sync/config files (REL-003).
- Everything Windows-specific is unvalidated on real Windows in this phase: Task Scheduler behavior,
  paths with spaces/special characters, OneDrive client behavior, standard-user/no-admin, read-only
  destinations, AV/SmartScreen, DPI 100/125/150, sleep/resume, single-file EXE launch, and the
  affected-PC Dashboard ERT validation.
- A user who deliberately creates a junction from the sync folder into `%LOCALAPPDATA%` is not
  detected (canonical-target resolution not performed).
- No response-size cap on official HTTP responses (trusted hosts; INFO).

## 10. Windows validation status

**NOT EXECUTED** for every item in phase §38 and §39 — no Windows host or VM was available. The
following remain required before treating Windows behavior as validated: Task Scheduler install/
uninstall from a path with spaces and `&`/`%` characters; standard user; launch from Downloads;
OneDrive-backed sync folder with two real devices; read-only/disconnected destinations; DPI scaling;
Defender/SmartScreen observation; single-file EXE launch; and the rides-only ERT Dashboard fix on an
originally affected PC with its existing `%LOCALAPPDATA%\Carowinds Passdown` data preserved.

## 11. Ordered Phase 18B remediation plan

Items 1–8 are complete (§13, §14); only item 9 remains.

1. ~~**REL-004** (MEDIUM)~~ — **fixed in 18B** (§13).
2. ~~**REL-003**~~ — durable temp write (`Flush(true)`) in `AtomicFileWriter` and `FileDeviceIdProvider`.
3. ~~**REL-005**~~ — interactive-app file log (separate file), included in diagnostics with PRIV-001 redaction.
4. ~~**PRIV-002**~~ — unique temp PDF names for "Open Final PDF" + age-based cleanup.
5. ~~**REL-007**~~ — clean up this device's own stale `*.tmp-*` files in the sync folder.
6. ~~**PERF-001**~~ — avoid re-hashing already-imported remote history PDFs every pass (decide corruption-reporting semantics first).
7. ~~**QUAL-001**~~ — decide whether to pin the UI culture to en-US. *Product decision.*
8. ~~**PKG-002**~~ — extend the release staging deny list.
9. Windows validation pass (§10) on a real Windows 11 x64 machine, including the affected-PC Dashboard check.

## 12. Recommendation

**READY WITH KNOWN LOW-RISK ITEMS** for continued internal use on the existing release — with the
explicit caveat that REL-004 (MEDIUM) should be fixed in 18B before Personal Sync is relied on across
two actively-used PCs, and that no Windows validation was performed in this phase. The 18A fixes take
effect in the next release build; the current ZIP/EXE were deliberately not rebuilt.

## 13. Phase 18B — REL-004 remediation (open editors vs. Personal Sync)

**Product rule (owner decision):** auto-reload clean editors; preserve dirty editors and require
explicit conflict resolution; never silently overwrite a synced change from an already-open editor.

**Scope:** the three editors that write sync-tracked data — the Associate draft (autosave), the Early
Ride Time panel (monthly default, date override, remove override), and the Operational Notes editor.
Read-only screens (Dashboard, Calendar, History) are unaffected.

**Mechanism.**

1. *Every editor save is conditional.* Each editor remembers the canonical Personal Sync content hash
   of the version it loaded (`AssociateWorkspace.DraftContentHash`,
   `IErtConfigurationService.ContentHashOf`, the note as opened). The save
   (`SaveEditorDraftAsync`, `Save*FromEditorAsync`/`RemoveOverrideFromEditorAsync`,
   `UpdateFromEditorAsync`) runs read-compare-write inside one `BEGIN IMMEDIATE` unit of work. If the
   stored version still matches (or already equals what is being written), it is a normal save. If
   not, the editor's content is still saved as this device's version, and
   `OpenEditorConflictRecorder` preserves the version found in storage as the "other device" side of an
   ordinary sync conflict (provenance from the sync state's last remote device/revision) and marks the
   entity Conflicting — so export pauses and the existing Keep This Device / Use Other Device / Accept
   Deletion resolution applies unchanged. A note deleted underneath its editor is re-created from the
   editor and the deletion recorded as the conflict's other side.
2. *Every sync import is atomic.* `PersonalSyncService`'s per-entity read-decide-write for drafts,
   notes, monthly ERT and ERT overrides now runs under the same write lock, so an editor save can no
   longer land between an import's read and its write (the reverse race, which previously let an
   import overwrite a just-committed editor save).
3. *Clean editors reload.* `ISyncLocalChangeNotifier` publishes the entities a sync pass (or a Use
   Other Device / Accept Deletion resolution) changed locally, after commit. The Associate screen
   reloads the selected date only when it has no unsaved edits and no save in flight (re-checked after
   the read); an open ERT panel keeps unsaved values and is re-filled only when unchanged. The Notes
   screen reloads its list only when no note is being edited, deferring the reload until the edit is
   saved or cancelled. The notification is a convenience; correctness never depends on it (step 1).

**User-visible change:** when a conflict is detected the screen shows "…changed on another device while
you were editing. Your edits are saved on this computer and the other version is kept too — choose
which to keep under Personal Sync conflicts." No data is discarded in any path.

**Schema/versions:** none changed (DB 15 / Draft 4 / Snapshot 2 / Sync 1). No sync file format change.

**Tests (11, `Ui.Tests/OpenEditorSyncProtectionTests.cs`)** — two real SQLite devices, real services,
real sync passes, the real Associate/Notes view models: dirty draft → conflict with both versions,
export paused, folder unchanged; Use Other Device restores and the clean editor reloads; Keep This
Device publishes the editor's version; clean draft editor auto-reloads and its next edit is not a
conflict and keeps the other device's change; draft created remotely while the editor had none;
import-vs-editor-commit race; dirty ERT panel → conflict; clean ERT panel re-filled; dirty note editor
→ conflict; note deleted underneath → re-created + deletion conflict; notes list reload deferred while
editing. All 11 fail under the pre-18B behavior (services without the unit of work/recorder, import
without the lock, no reload handler) and pass 8/8 repeated runs after the fix.

**Also fixed (test-only):** `PersonalSyncCoordinatorTests.PeriodicIntervalElapsing_TriggersAnAdditionalPass_AndReArmsItself`
was flaky on the 18A baseline (5 of 12 isolated runs failed on commit `a9903e5`): it asserted the
periodic delay had been re-armed before the coordinator's loop had done so. It now waits for the
re-armed delay; 15/15 isolated runs pass. No production change.

**Guard tests updated:** the two `OperationalNotesService` constructor-shape tests now sanction the two
new persistence-only dependencies (still no network/retrieval dependency).

**Validation:** Release build 0 warnings / 0 errors; **2245/2245** deterministic tests (116 Domain +
649 Application + 222 Documents + 575 Infrastructure + 683 Ui = 645 together + 26 + 10 + 2 alone, same
documented partition).

**Windows validation:** NOT EXECUTED (no Windows host). Real two-PC OneDrive validation of this flow
remains part of the outstanding Windows/OneDrive validation pass.

## 14. Phase 18B — remaining remediation and US presentation culture

Baseline before 18B work (commit `8a6ca0d`): Release build 0 warnings / 0 errors; **2256/2256**
deterministic tests in Release (116 Domain + 649 Application + 222 Documents + **586** Infrastructure +
645 Ui together + 26 + 10 + 2 alone). §13 recorded 2245 / 575 Infrastructure; the actual Infrastructure
count at that commit is 586 (585 in Debug — one test is Release-only), so §13's total was undercounted
by 11. No change was made to reconcile it; the numbers here are measured.

### 14.1 REL-003 — power-loss-safe small-file writes
`AtomicFileWriter`: `CreateNew` temp in the destination folder → write → flush →
`Flush(flushToDisk: true)` → close → `File.Move(overwrite: true)`; temp deleted on any failure.
`FileDeviceIdProvider` now uses it too (sync, config, device id, PDF-template preference, manifest,
envelopes, history pairs). **Guarantee:** after a crash or power loss the destination is the complete
old or the complete new version. **Residual:** the rename is not forced to disk, so a write completed
seconds before a power loss can roll back to the previous complete version (no corruption); closing it
would need Windows P/Invoke (`MOVEFILE_WRITE_THROUGH`) or a directory fsync .NET does not expose — not
done. Details: `docs/personal-sync-architecture.md` §18B.1.

### 14.2 REL-005 — interactive app log
`%LOCALAPPDATA%\Carowinds Passdown\logs\app-YYYYMMDD.log` (UTC day), added only on the real desktop
lifetime path (never in the scheduled-refresh host or headless tests). Logs startup, schema version,
migration failure, unhandled/unobserved exceptions, plus every existing view-model/sync failure log.
Every line is path-redacted before it is written; logging failures are swallowed. Retention **14 days**
for both logs, pruned at startup, matching only `^<prefix>-\d{8}\.log$`. Export Diagnostics adds
`logs/app-YYYYMMDD.log`: the last ≤200 lines of the newest app log, redacted. Privacy rules unchanged
from §6 #13 (ids/dates/counts/statuses only).

### 14.3 REL-007 — stale sync temp cleanup
`SyncTempFileCleaner`, run at each pass start: only the app-owned sync folder and its five subfolders
(non-recursive), only exact `<canonical name>.tmp-<32 hex>` / `.sync-probe-<32 hex>.tmp` names, only
direct children by canonical path, no symlink/junction folders or files, only files ≥ **24 h** old.
Adversarial tests cover `..`, rooted, UNC, alternate-separator, nested, symlinked folder/file, locked
file; the symlink guards were proven necessary (removing them fails two tests).

### 14.4 PRIV-002 — temporary PDFs
The only temp-PDF path in the app is History "Open Final PDF" (generation is in memory; Save writes
straight to the picker's file). `HistoryPdfTempFileStore`: unique `…yyyy-MM-dd.view-<32 hex>.pdf` per
open in `%TEMP%\Carowinds Passdown\History`; deleted on failed/cancelled write or a launch that throws;
swept by age (≥ **24 h**) at startup and before each open; only own names (plus the pre-18B fixed name)
directly in that folder; symlinked folder not swept. A null `Process` from the shell is *not* treated as
"nothing opened it" (an already-running viewer receives the file without a new process), so that file is
left for the sweep rather than deleted from under the viewer.

### 14.5 PERF-001 — History import re-reads
Skip only when the local record matches id + content hash + PDF SHA-256/size (blob-free query), the
sync state records that exact remote envelope (device + revision + hash) as already validated and not
in conflict, the remote length matches, and in-session length/timestamp are unchanged. Anything else is
fully read and hashed. Representative run (365 × 500 KB): steady-state pass **182.5 MB → 0 bytes** read;
315–361 ms → 111–118 ms on a local SSD (OneDrive not measured). Trade-off documented: same-length,
same-timestamp in-place alteration of the shared copy of an already-held record is no longer reported
(the local immutable copy is unaffected). Details: `personal-sync-architecture.md` §18B.3.

### 14.6 QUAL-001 — US presentation culture (owner decision)
`Carowinds.Passdown.Domain.PresentationCulture` is the single named culture
(`CultureInfo.GetCultureInfo("en-US")`, built-in data, no user overrides) plus `FormatUsd`; every
user-facing date/time/price formatter uses it, and `Program.Main` makes it the process default for
framework controls. **Defects fixed** (machine culture leaked through): Calendar month title, selected
date, day-cell accessibility date, compact hours (AM/PM), finalized/saved timestamps; History row date and
finalized stamp; Dashboard Deep Refresh status/coverage; save status; park-hours times and category
status timestamps; Personal Sync "Last synced" and conflict "Changed on another device on …"; sync
conflict detail AM/PM (lost entirely on de-DE/fr-FR), custom-product prices (`$54,99`), counts and
note date ranges (ISO → `Sep 19, 2026`); the "Use override for {date}" XAML `StringFormat`; diagnostics
"MB" decimal separator. Already-invariant formatters (PDF, Dashboard header, notes, retrieval messages,
ERT conflict labels) were routed through the same helper. **Machine formats made explicitly invariant:**
the Park Hours request URL date and the PDF/diagnostics file names (a th-TH Buddhist-calendar default
would otherwise have produced year 2569). Storage, sync wire format, `yyyy-MM-dd` file identities,
round-trip timestamps and parsing are unchanged; time zones unchanged (operational Eastern; the
pre-existing local-wall-clock stamps, INFO-002, unchanged). No localization framework, no setting.

### 14.7 PKG-002 — release content validation
`packaging/windows/release-content-rules.json` + `Test-ReleasePackage.ps1` (run on staging and on the
ZIP by `Build-PortableRelease.ps1`) + C# mirror `ReleasePackageValidator` in tests. Required/forbidden
lists and the developer-path scan are in `docs/release-strategy.md` "Phase 18B". The PowerShell script
was **not executed** (no `pwsh` on this host); the C# mirror against the existing pre-18A ZIP reports
only the known PKG-001 leak, and the Release-build scan of our own assemblies is clean.

### 14.8 Regression evidence
- REL-004: `OpenEditorSyncProtectionTests` 11/11 (clean editor reload, dirty editor keeps edits,
  explicit conflict, autosave can't overwrite, ERT monthly/override/remove, Notes).
- 18A: adversarial sync input, migration hardening/race, semicolon DB path, locked DeviceId,
  diagnostics redaction, Release PDB-path privacy, BUG-001 USD — all green.
- Test-only fix: a `MemberData` theory in the Ui assembly deterministically triggered the documented
  Avalonia headless thread-ownership failure in the isolated real-`Window` partitions; the new culture
  tests use `InlineData` instead (comment in the file). The documented partition itself is unchanged.

### 14.9 Remaining findings
- **PKG-003 (INFO, new):** no consolidated third-party notices file for MIT/BSD/Apache dependencies;
  only `LatoFont/OFL.txt` ships. Licensing/product decision.
- REL-003 residual (rename durability), PERF-001 trade-off — documented above, accepted.
- INFO-001/002/003 unchanged.
- All Windows-specific validation and real two-PC OneDrive validation: **NOT EXECUTED**.

### 14.10 Validation
Release build `--no-incremental`: **0 warnings, 0 errors**. Deterministic tests **2476/2476** = 116
Domain + 661 Application + 226 Documents + 741 Infrastructure + 694 Ui together + 26 + 10 + 2 alone
(+220 over the measured 2256 baseline). DB 15 / Draft 4 / Snapshot 2 / SyncSchemaVersion 1 unchanged;
no migration; no sync file format change. Release ZIP/EXE not rebuilt.

Tests added: `AtomicFileWriterFaultTests` (25), `SyncTempFileCleanerTests` (25), `InteractiveAppLogTests`
(17), `HistoryPdfTempFileStoreTests` (16), `ReleasePackageValidationTests` (57 incl. 1 Release-only,
1 opt-in), 14 History PERF/integrity + 1 temp-cleanup integration test in `PersonalSyncServiceTests`,
`DiagnosticReportBuilderTests` (+2), `SyncConflictDetailCultureTests` (10), `PdfUsPresentationCultureTests`
(4), `UsPresentationCultureTests` (49), repository metadata assertion (+0, extended).


## 15. Phase 18C — release candidate packaging

Source commit `5c0ddf2`. Release-validation phase only: no feature, business-rule, sync, or schema
change. Full record and hashes: `docs/release-manifest.md` "Phase 18C".

### 15.1 PKG-003 — third-party notices (resolved)

The release now ships `THIRD-PARTY-NOTICES.txt` (index: component, version, license, copyright, where
the full text is) and `licenses\` (full texts copied unchanged from the shipped NuGet packages; sources
kept in `packaging/windows/`). Scope = what the win-x64 publish actually contains, determined from
`project.assets.json` for `win-x64`, each package's nuspec license/copyright, and the LICENSE/NOTICE
files inside the packages. The Inter font's copyright line was read from the font files embedded in
`Avalonia.Fonts.Inter.dll` (the package has no license file). Lato's `LatoFont\OFL.txt` is unchanged and
referenced from the index.

Validation (`release-content-rules.json`, enforced by `Test-ReleasePackage.ps1` and the C# mirror): the
notice files are required; any file matching `noticeFilePatterns` must be ≥ 100 bytes and contain no
placeholder token (`TODO`, `TBD`, `{{`, `<insert`, …, case-sensitive); every required file must be
non-empty; a ZIP must have no duplicate entries. A test also checks, offline, that `packaging/windows/
licenses/` and the required list match exactly and that every `licenses\…` path named in the index
exists.

Open owner question (documented, not decided here): QuestPDF Community License eligibility — see the
manifest.

### 15.2 PKG-004 (LOW, new, fixed) — Desktop assembly embedded the build machine's path

`Test-ReleasePackage.ps1`, run on a real release build for the first time, failed: the shipped
`Carowinds.Passdown.Desktop.dll` contained `/Users/<developer>/…/obj/Release/net10.0/win-x64/
Carowinds.Passdown.Desktop.pdb`. Cause: Avalonia's `CompileAvaloniaXamlTask` rewrites the assembly
after `csc` and records the absolute PDB path, so the 18A PKG-001 `PathMap` never applied to this one
assembly. The 18A/18B regression test only scanned Infrastructure.Tests' output folder, which does not
contain `Desktop.dll`. Every earlier release (including 17F) shipped this path.

Fix: Desktop Release builds use `<DebugType>none</DebugType>` (no PDB, so no debug-directory path).
PDBs were never shipped, so users lose nothing; Debug builds are unchanged. Regression:
`DesktopAssemblyPathPrivacyTests` (Ui.Tests, Release-only) — confirmed failing with the old setting and
passing with the fix.

### 15.3 Test count correction

18B's "2476" included Infrastructure.Tests' 11 opt-in `LiveSmoke` tests. With the established
`Category!=LiveSmoke` partition, 18B's commit measures **2465/2465**. 18C: +19 → **2484/2484**.

### 15.4 Not executed

All Windows validation (launch, affected-PC Dashboard, existing data, US formatting under a non-US
region, app log, diagnostics, PDFs, temp PDF cleanup, Task Scheduler, Personal Sync one-PC and two-PC,
single-file EXE) — no Windows machine available. Recommendation: **RELEASE BLOCKED pending Windows
validation** (no open defect).
