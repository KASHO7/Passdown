# Phase 9 — Passdown History & Finalization

Phase 9A established the immutable finalized-passdown domain/storage
model. Phase 9B added *automatic* finalization: a 6:00 AM Eastern
cutoff, evaluated by the existing scheduled task and the interactive
startup catch-up path, always before either ever runs a source refresh.
Phase 9C extends every finalized record to also freeze the exact PDF
bytes an associate would have printed at finalization time, atomically
alongside the structured snapshot. Phase 9D adds the read-only
Passdown History UI that browses those frozen records. Phase 9E is an
end-to-end integration validation pass over the whole 9A–9D pipeline.
Phase 9F (this document's current scope; see "Phase 9F — release
closeout" below) closes out Phase 9 as a shipped feature set: a final
requirements/immutability audit and the release rebuild. No phase
through 9F implements revisions, manual finalization, delete-history,
or touches the Manager workflow.

## Concept

```
editable Associate draft
    |
    v
finalization                     <- Phase 9A model; Phase 9B automates it
    |
    v
immutable historical snapshot
    |
    v
frozen PDF                       <- Phase 9C
    |
    v
Passdown History UI              <- Phase 9D (this document)
```

**Finalized passdown**: an immutable historical record of exactly what
associates received for one operating date. Distinct from
`AssociateDraft` (`src/Carowinds.Passdown.Application/Persistence/AssociateDraftPersistence.cs`),
which stays fully editable — Phase 9A does not change any Associate
editing behavior, lock any date, or add History navigation.

**Identity**: `PassdownDate` (a `DateOnly`, e.g. `2026-08-25`).

**Current rule**: one finalized record per `PassdownDate`, enforced by a
unique index (`ux_finalized_passdowns_passdown_date`) in addition to the
application layer's own check-before-insert
(`FinalizedPassdownFinalizationService`). A second finalization attempt
for an already-finalized date is a no-op (`FinalizationOutcome.AlreadyFinalized`),
not an overwrite or a second row.

**Future revisions**: explicitly not implemented. The schema uses a
surrogate `id` independent of `passdown_date` specifically so a future
revision design would not require redefining this table's identity —
but no revision chain, version number, or "current" flag exists yet.

## Snapshot: frozen display values, not live references

The finalized snapshot never stores a product id/key and re-resolves its
name/price from the current catalog later. It stores the already-resolved
display strings an associate actually saw — `"Single Day Admission"`,
`"$49.99"`, `"was $59.99"` — exactly as
`AssociatePassdownDocumentModel` (Documents project) already renders them
for the PDF. If official pricing, hours, or notes change tomorrow, an
already-finalized snapshot does not change with them.

This reuses the existing PDF projection rather than re-deriving a second
interpretation of "what the passdown contains":

```
AssociateWorkspace (Application)
    |
    v
AssociatePassdownDocumentModelBuilder.Build(...)   (Documents; unchanged)
    |
    +--> AssociatePassdownPdfGenerator              (existing PDF path; unchanged)
    |
    +--> FinalizedPassdownSnapshotBuilder            (new; Documents)
             |
             v
         FinalizedPassdownSnapshot (Application)
```

`IFinalizedPassdownSnapshotBuilder` is declared in Application (so
Application/Infrastructure persistence code never needs to reference
Documents), but its only real implementation lives in Documents, where it
can reuse `AssociatePassdownDocumentModelBuilder` directly. This keeps
history and the PDF from ever disagreeing about what was in a passdown,
without Application depending on Documents (see `docs/architecture.md`'s
dependency-direction rules, which Phase 9A does not change).

Fields captured: park hours (effective headline, badge, North Gate/Avenue
times, verification text, private-buyout details), projected attendance
and pre-solds, Early Ride Time details and participating attractions,
Daily Feature type/text, operational notes, every included product
category with each row's display name/effective price/comparison
price/qualifier/notes (official and custom products alike), the
"pricing not recently verified" footer flag, and the generation
timestamp text. Every list is defensively copied at build time
(`FinalizedPassdownSnapshotBuilder`) so a later mutation of the source
draft/workspace can never reach back into an already-built snapshot.

## Snapshot schema version

`FinalizedPassdownSnapshot.CurrentSchemaVersion = 1`
(`src/Carowinds.Passdown.Application/Finalization/FinalizedPassdownSnapshot.cs`).
This is **not** the app version — it identifies the structure of the
snapshot JSON stored in `FinalizedPassdown.SnapshotSchemaVersion`/
`finalized_passdowns.snapshot_schema_version`. There is exactly one
authoritative constant; bump it (and only it) when the snapshot shape
changes incompatibly, mirroring how `AssociateDraftPayload.CurrentSchemaVersion`
already works for drafts.

## Serialization and content hash

`FinalizedPassdownSnapshotSerializer` is the one place that turns a
snapshot into `SnapshotJson` and back. It uses an explicit, narrow
`JsonSerializerOptions`: enums serialize as stable names (not integers
that would silently renumber), Pascal-case property names, no
polymorphic type metadata, and only purpose-built sealed
records/enums in the graph — never a source DB entity serialized
directly.

`ComputeContentHash` is a lowercase-hex SHA-256 of the exact
`SnapshotJson` text — integrity/diagnostic metadata only, never used for
security or authentication. Deterministic serialization is what makes the
hash meaningful: the same logical snapshot content always produces the
same JSON text and therefore the same hash.

## Metadata

```
FinalizedPassdown
------------------
Id                        Guid, surrogate key
PassdownDate               DateOnly, unique
FinalizedAtUtc              DateTimeOffset (UTC)
SnapshotSchemaVersion       int
SnapshotJson                string (opaque to persistence)
ContentHash                  string (lowercase hex SHA-256)
Origin                       FinalizationOrigin (Scheduled | Manual | Recovery)
PdfBytes                     byte[]? (Phase 9C — the exact frozen PDF; null for a pre-9C row)
PdfSha256                    string? (Phase 9C — lowercase hex SHA-256 of PdfBytes)
PdfSizeBytes                 long? (Phase 9C — byte length of PdfBytes)
```

No `FinalizedBy`/account identity — accounts are Phase 16+, and there is
no authenticated user to record yet.

`FinalizedPassdown.HasFrozenPdf` (`PdfBytes is not null`) is the one
place code should check "does this record have an exact historical PDF"
rather than testing `PdfBytes`/`PdfSha256`/`PdfSizeBytes` individually —
all three are always populated together or all three are null (Phase
9A/9B rows predating this migration; see "Frozen PDF storage" below).

`FinalizedPassdownSummary` (`PassdownDate`, `FinalizedAtUtc`, `Origin`,
`HasFrozenPdf`, `PdfSizeBytes`) is a lightweight read model for a future
History UI listing, without deserializing every row's `SnapshotJson` or
loading its `PdfBytes` blob — `SqliteFinalizedPassdownRepository.ListAsync`
deliberately selects `pdf_sha256`/`pdf_size_bytes` only, never
`pdf_bytes`, so listing every finalized passdown never materializes
every row's PDF into memory at once.

## Repository contract

`IFinalizedPassdownRepository` (`Application/Persistence/FinalizedPassdownPersistence.cs`):
`GetByDateAsync`, `ExistsForDateAsync`, `AddAsync`, `ListAsync`. Deliberately
insert-only — no `Update`/`Delete` method exists. `SqliteFinalizedPassdownRepository`'s
`AddAsync` is a plain `INSERT` (no upsert); a second insert for an
already-finalized date is rejected by the table's unique index, surfacing
as a `SqliteException` rather than silently overwriting history.
Deleting/repairing a bad historical row, if ever necessary, is a
support/database concern, not a normal application operation.

## Migration

`Migration0008CreateFinalizedPassdownsTable` (schema version 8) creates
`finalized_passdowns` and its unique index. It does not touch any
existing migration or table.

`Migration0009AddFinalizedPassdownPdfColumns` (schema version 9, Phase
9C) additively adds `pdf_bytes BLOB`, `pdf_sha256 TEXT`, and
`pdf_size_bytes INTEGER` — all nullable at the database level, and it
does not rewrite Migration0008. Nullability is deliberate: a
development/test database can already contain Phase 9A/9B rows created
before this migration existed, and those rows must keep applying
cleanly with `NULL` PDF columns rather than the migration attempting (and
failing) to backfill an "exact" historical PDF it cannot honestly
produce — see "Frozen PDF storage" below.

## Frozen PDF storage (Phase 9C)

Every finalized passdown now freezes two things atomically in the same
row: the structured `SnapshotJson` (Phase 9A) and the exact PDF bytes an
associate would have received at finalization time (`PdfBytes`).
Storage is a SQLite `BLOB` on the same `finalized_passdowns` row, not an
external file path — this keeps one historical record fully
self-contained (portable app-folder moves and DB backups carry the PDF
with its snapshot automatically), and lets the insert stay one atomic
SQLite statement (see "Transactional creation" below). Measured
representative sizes for the current one-page Associate Passdown layout
are roughly 40–55 KB (`FinalizedPassdownProjectionTests`, Documents.Tests)
— comfortably within SQLite's BLOB capabilities; at a rough 50 KB/day,
365 finalized passdowns is on the order of 18 MB/year, so no
archival/compression scheme is needed at this scale.

**Same canonical state for both outputs.** `IFinalizedPassdownSnapshotBuilder.BuildProjection`
(`Application/Finalization/IFinalizedPassdownSnapshotBuilder.cs`) is the
one seam that produces both frozen artifacts. Its Documents
implementation (`FinalizedPassdownSnapshotBuilder.BuildProjection`)
builds `AssociatePassdownDocumentModelBuilder.Build(workspace, asOf)`
**exactly once**, then derives the snapshot (the same mapping `Build`
already used) and the PDF (`IAssociatePassdownPdfGenerator.Generate`)
from that single in-memory model — never two independent
recompositions of the workspace that could theoretically diverge. The
existing single-`Build`-returning-just-a-snapshot method is unchanged
and still covered by `FinalizedPassdownSnapshotBuilderTests`.

**PDF generator has no filesystem/dialog dependency.**
`AssociatePassdownPdfGenerator.Generate(AssociatePassdownDocumentModel)`
already returned in-memory bytes before Phase 9C (used by the existing
Create-PDF-and-save flow via `AssociatePassdownPdfExportService`); Phase
9C reuses that same method for finalization rather than adding a second
generation path. Scheduled/catch-up finalization never opens a save
dialog and never requires user interaction.

**PDF hash/size.** `PdfSha256` is a lowercase-hex SHA-256 of the exact
`PdfBytes` (same integrity/diagnostic-only convention as
`ContentHash`/`FinalizedPassdownSnapshotSerializer.ComputeContentHash` —
never used for security/authentication). `PdfSizeBytes` is
`PdfBytes.LongLength`, stored alongside so history/diagnostics never
need to re-measure the blob.

**Transactional creation now includes the PDF.**
`FinalizedPassdownFinalizationService.FinalizeAsync` calls
`BuildProjection` (building the model, the snapshot, and the PDF),
serializes the snapshot, computes both hashes, and only then constructs
the complete `FinalizedPassdown` (all ten fields, PDF included) and
calls `IFinalizedPassdownRepository.AddAsync` once. If building the
model, generating the PDF, serializing, or hashing throws, nothing is
written — a PDF-generation failure (e.g. content that cannot fit
`AssociatePassdownPdfGenerator`'s strict one-page layout) is
indistinguishable from any other pre-insert failure to every existing
caller. This is exactly the same "everything in memory before the one
`AddAsync`" guarantee Phase 9A established for the snapshot alone; Phase
9C does not add a second write.

**Failure still blocks scheduled refresh.** Because PDF generation now
happens inside `FinalizeAsync`, a PDF-generation failure propagates out
of `ScheduledPassdownFinalizer.FinalizeEligibleAsync` exactly like any
other Phase 9A/9B failure already did, so `ScheduledRefreshHost`/
`StartupCatchUpRefresh` still never proceed to a source refresh for that
invocation (Phase 9B's ordering/failure-blocks-refresh behavior is
unchanged — see `ScheduledRefreshHostTests.RunAsync_FinalizationThrows_ReturnsFatalFailureExitCode_AndNeverInvokesRefreshRunner`
and `ScheduledPassdownFinalizerTests.FinalizeEligibleAsync_PdfGenerationFailsForEligibleDate_ThrowsAndWritesNoRecord`).

**Pre-9C rows are never backfilled.** A row finalized before this
migration has `PdfBytes`/`PdfSha256`/`PdfSizeBytes` all `null` and
`HasFrozenPdf == false`. Nothing regenerates a PDF for it: doing so
would use the *current* `AssociatePassdownPdfGenerator` styling and
possibly-changed product/catalog data, so it would not be the exact
historical PDF and must never be labeled as one (spec Part J). The
repository reads such a row without error; only the read model's
`HasFrozenPdf` distinguishes it from a complete post-9C row.

**Repository/read-model shape.** `GetByDateAsync` returns the full row,
PDF included — appropriate for the "one record" detail read a future
History UI's "open this PDF" action needs. `ListAsync` never selects
`pdf_bytes`, only `pdf_sha256`/`pdf_size_bytes`, keeping a full-history
listing cheap regardless of how many rows exist.

**PDF retrieval seam for Phase 9D.**
`IFinalizedPassdownPdfRetrievalService.GetPdfAsync(passdownDate)`
(`Application/Finalization/FinalizedPassdownPdfRetrieval.cs`) is the
smallest operation a future History UI needs: it reads
`IFinalizedPassdownRepository.GetByDateAsync` and returns `PdfBytes`,
a suggested filename (`Carowinds-Passdown-yyyy-MM-dd.pdf`), and the
hash/size — nothing else, and it depends on nothing that could generate
a PDF (no `IAssociatePassdownPdfGenerator`/`IFinalizedPassdownSnapshotBuilder`
reference exists on this type), so "retrieval never regenerates" is true
by construction, not just by test coverage. Registered in DI
(`App.axaml.cs`) but not wired to any UI yet.

**Diagnostics stay sanitized.** `SqliteDatabaseDiagnosticsProvider`
never queried `finalized_passdowns` before Phase 9C and still doesn't —
it only reports `PRAGMA user_version`, file size, and WAL presence.
`SqliteDatabaseDiagnosticsProviderTests` adds a regression test seeding a
finalized row with a distinctive secret snapshot-note string and PDF
marker bytes and asserting neither ever appears in the reported
`DatabaseDiagnostics`.

## Automatic finalization (Phase 9B)

**Order — finalize before refresh.** Both entry points that already run
`IScheduledRefreshRunner` now finalize first, and a finalization failure
blocks that invocation's refresh:

- `ScheduledRefreshHost.RunAsync` (`Carowinds.Passdown.Desktop.exe --scheduled-refresh`,
  the same Windows Task Scheduler task as Phase 6 — Phase 9B adds no
  second task, no second CLI flag, and does not touch the 6:00 AM
  trigger itself): acquire lock → run migrations → finalize eligible
  passdowns → run scheduled refresh → exit/logging.
- `StartupCatchUpRefresh.RunAsync` (Phase 6B's non-blocking startup
  catch-up): the same ordering, guarded by the same
  `FileScheduledRefreshLock`, resolved through the same
  `IScheduledPassdownFinalizer`. A failure here is caught by the
  existing outer catch (never propagates — the window still opens) but
  still prevents the refresh runner from being invoked for that catch-up
  attempt.

Interactive "Check Online" (`AssociatePassdownService.CheckAssociateDateOnlineAsync`)
is untouched and stays a plain user-triggered single-date refresh — it
does not route through `IScheduledPassdownFinalizer` and does not
finalize anything. If a user checks a date online after its cutoff but
before the scheduled/catch-up finalizer has run, nothing about
finalization semantics changes; the next scheduled or catch-up
invocation still finalizes from whatever is locally stored at that time.

**Eligibility** (`ScheduledPassdownFinalizer.FinalizeEligibleAsync`,
`Application/Finalization/ScheduledPassdownFinalizer.cs`) — a
`PassdownDate` is eligible only when all three hold:

1. `FinalizationEligibility.EligibleForFinalizationAt(date) <=` the
   injected `IClock.UtcNow` (its cutoff has passed).
2. `IAssociateDraftRepository.ListSavedDraftDatesAsync()` includes the
   date — i.e. a draft row was actually saved for it. This resolves
   Phase 9A's open decision below: "a workspace can be loaded for this
   date" (true for *every* date — a missing draft simply composes
   `AssociateDraftPayload.CreateDefault()`) is **not** treated as
   eligibility; only a persisted row is.
3. `IFinalizedPassdownRepository.ExistsForDateAsync(date)` is `false`
   (not already finalized).

No draft row for a date means no historical record is ever fabricated
for it — the finalizer simply never considers a date that never appears
in `ListSavedDraftDatesAsync()`.

**Missed-run / multi-day recovery.** Every eligible date is finalized in
one pass, oldest first: `FinalizeEligibleAsync` reads every saved draft
date, filters to eligible ones, sorts ascending, and finalizes each in
order. A machine that misses several days (e.g. off from 8/24 through
8/28) finalizes 8/24, 8/25, 8/26, 8/27 in that order in the single
invocation that next runs, rather than only "yesterday". `FinalizedAtUtc`
on every resulting row is the actual completion time (`IClock.UtcNow`
when the pass ran), never a fabricated nominal 6:00.

**Origin** (`FinalizationOrigin.Scheduled` vs `.Recovery`) — deterministic
from calendar dates alone, never from Task Scheduler's own missed-run
reporting: a `PassdownDate`'s cutoff always falls on `PassdownDate + 1
day` (America/New_York calendar date). If that equals today's Eastern
calendar date (from the same `IClock.UtcNow`), the origin is `Scheduled`;
if it is an earlier date, the origin is `Recovery`. It can never be a
later date, since only already-eligible dates reach this check.
`FinalizationOrigin.Manual` is not produced anywhere in Phase 9B — no
manual finalization UI/command exists yet (Phase 9B Part W).

**Failure blocks refresh.** If `FinalizeEligibleAsync` throws for an
eligible date (a build/serialize/hash/repository failure inside
`FinalizedPassdownFinalizationService.FinalizeAsync`),
`ScheduledRefreshHost` logs it and returns the existing `FatalFailure`
exit code (2) *without ever calling* `IScheduledRefreshRunner` for that
invocation — no new exit code was added. The next scheduled/catch-up
invocation retries finalization from the same unchanged pre-refresh local
state, since refresh never ran to alter it.

**Safe skips continue to refresh** — none of the following is a failure;
finalization simply reports zero/partial work and the refresh step still
runs normally:

- no saved draft is eligible yet (nothing has crossed cutoff);
- every eligible date was already finalized by an earlier run;
- there are no saved drafts at all.

**Duplicate/race safety.** `FileScheduledRefreshLock` still serializes
every scheduled/catch-up invocation, so two finalization passes cannot
run concurrently in practice. As defense in depth underneath that lock,
nothing changed here: `ExistsForDateAsync` is checked before finalizing
each date, `FinalizedPassdownFinalizationService.FinalizeAsync` re-checks
via `GetByDateAsync` before its own insert (Phase 9A), and the database's
unique index on `passdown_date` remains the final backstop — none of
these were removed or weakened to make room for the scheduler.

**Summary/logging.** `FinalizeEligibleAsync` returns a
`ScheduledFinalizationSummary` (`EligibleCount`, `FinalizedCount`,
`AlreadyFinalizedCount`, and the list of newly finalized
dates/origins). `ScheduledRefreshHost.LogFinalizationSummary` logs one
concise line with those counts (only when `EligibleCount > 0`, to avoid
noise on the common "nothing to finalize" run), plus one
`Finalized passdown {date} ({origin})` line per newly finalized date from
inside the finalizer itself. Never logs snapshot JSON or any
associate-entered note text.

## No-draft eligibility — resolved in Phase 9B

`AssociatePassdownService.LoadAssociateDateAsync` always returns a
composed `AssociateWorkspace` for any date, whether or not an
`AssociateDraft` row was ever saved for it (a missing draft simply
composes `AssociateDraftPayload.CreateDefault()`). This means "a
workspace can be loaded for this date" is **not** a meaningful "there is
real content to finalize" signal.

Phase 9B resolves this exactly as Phase 9A recommended: a date is treated
as having eligible source content only when
`IAssociateDraftRepository.ListSavedDraftDatesAsync()` includes it — i.e.
an associate actually saved something for that date — never merely
because the date is loadable (every date is). See "Eligibility" above for
the full rule.

## Date/time semantics

- `PassdownDate` — `DateOnly`.
- `FinalizedAtUtc` — `DateTimeOffset`, always UTC, matching the existing
  `SqliteConvert`/`IClock` conventions used by `AssociateDraft.UpdatedAt`
  and every other timestamped table.
- Future cutoff — `FinalizationEligibility.EligibleForFinalizationAt(passdownDate)`
  (`Application/Finalization/FinalizationEligibility.cs`) computes
  6:00 AM **America/New_York** on the day after `passdownDate`, correctly
  applying the EST/EDT offset for whichever side of a DST transition that
  particular date falls on (same approach as the existing
  `EasternTimeZone` helper). Phase 9B's `ScheduledPassdownFinalizer` is
  the caller: it compares this against `IClock.UtcNow` for every saved
  draft date to decide eligibility (see "Automatic finalization" above).

`DateTime.Now`/`DateTime.UtcNow` are not used anywhere in the Phase 9A/9B
code; all timestamps flow from `IClock`/the composed `AssociateWorkspace`'s
own `asOf`/`finalizedAt` parameters.

## Immutability enforcement

- **Domain/model**: `FinalizedPassdown` and `FinalizedPassdownSnapshot`
  (and every nested snapshot type) are `sealed record`s with no public
  setters implying ordinary mutation.
- **Repository**: insert-only API (no `Update`/`Delete`).
- **Database**: unique index on `passdown_date`.
- **Application**: `FinalizedPassdownFinalizationService.FinalizeAsync`
  checks `GetByDateAsync` before ever building/inserting, so an
  already-finalized date is a no-op rather than a second write.
- **Builder**: every list-typed field is defensively copied
  (`.ToList()`) when the snapshot is built, so later mutation of the
  source draft/workspace object cannot reach back into an
  already-returned snapshot instance.

No SQLite triggers — the insert-only repository plus the
check-before-insert application invariant plus the unique index is
enough for Phase 9A.

## Transactional creation

`FinalizedPassdownFinalizationService.FinalizeAsync` builds the
projection (document model built once — see "Frozen PDF storage"
above), serializes the snapshot, and computes both the snapshot content
hash and the PDF hash entirely in memory before ever calling
`IFinalizedPassdownRepository.AddAsync`. If building the model,
generating the PDF, serializing, or hashing throws, no row is written.
The subsequent `AddAsync` is a single `INSERT` statement (now with the
PDF columns included), which SQLite itself commits atomically — there is
no window in which a partially-written finalized row (e.g. a snapshot
with no PDF) can exist.

## Not implemented in Phase 9A (by design)

- The 6:00 AM automatic finalization job/scheduler — added in Phase 9B.
- Windows Task Scheduler wiring for finalization — Phase 9B reuses the
  existing Phase 6C task/trigger rather than adding one.
- The Passdown History UI/screen.
- Reading a finalized snapshot back into any UI.
- Frozen PDF storage (`pdf_bytes` column and the code that populates it) — added in Phase 9C.
- Revisions/version chains for a `PassdownDate` finalized more than once.
- Any change to the Manager workflow.
- Locking/read-only behavior for a finalized date's Associate draft — the
  draft stays fully editable after finalization, exactly as before.
- Automatic snapshotting on save, on PDF export, or on date switch.

## Not implemented in Phase 9B (by design)

- Manual finalization UI or command — `FinalizationOrigin.Manual` exists
  for future use only; nothing in Phase 9B produces it.
- A second Windows Task Scheduler task, a second CLI flag, a Windows
  service, or a second process-level lock — Phase 9B reuses
  `Carowinds.Passdown.Desktop.exe --scheduled-refresh`,
  `FileScheduledRefreshLock`, and the existing 6:00 AM trigger unchanged.
- Routing interactive "Check Online" through the scheduled finalizer.
- Frozen PDF blob storage — added in Phase 9C. Revisions, History UI, and
  the Manager workflow are still deferred to Phase 9D/later.

## Not implemented in Phase 9C (by design)

- The Passdown History UI/screen — Phase 9D (see below).
  `IFinalizedPassdownPdfRetrievalService` exists as the retrieval seam
  Phase 9D calls, but Phase 9C itself does not wire it to any UI.
- Revisions/version chains for a `PassdownDate` finalized more than once.
- Manual finalization UI/command — `FinalizationOrigin.Manual` still has
  no producer.
- Any change to the Manager workflow.
- Backfilling `PdfBytes` for pre-9C rows — deliberately never done; see
  "Frozen PDF storage" above.
- Any change to the 6:00 AM cutoff or the finalize-before-refresh
  ordering Phase 9B established — Phase 9C only adds PDF generation
  inside the existing `FinalizeAsync` call, it does not resequence
  anything around it.

## Passdown History UI (Phase 9D)

A read-only screen that lets a supervisor browse every finalized
passdown, view its exact frozen content, and open/save its exact frozen
PDF. **Read-only** is load-bearing: History never edits finalized
content, never regenerates a PDF, never resolves historical prices from
the current catalog, never refreshes the website, and never mutates a
historical row. No delete-history, no manual finalization, and no
Manager workflow were added — those stay deferred exactly as before.

**Navigation placement.** History is a top-level app-shell section, a
sibling of the existing Associate Passdown screen — not a subsection
inside `AssociateView`'s own Overview/Products/Notes rail, which would
make it look like part of the editable workflow. `MainWindow.axaml`
gained a slim horizontal section strip ("Associate Passdown" / "History")
below the brand band; `MainViewModel` (`Carowinds.Passdown.Desktop.ViewModels`)
gained an `AppSection SelectedSection` property and
`ShowAssociateSectionCommand`/`ShowHistorySectionCommand`. Both
`AssociateViewModel` and the new `PassdownHistoryViewModel` are
constructed once, at composition-root time, and stay alive for the
app's whole lifetime — `MainWindow.axaml` keeps both `AssociateView` and
`HistoryView` mounted simultaneously and only toggles `IsVisible`, so
switching sections never recreates Associate's in-progress edit/autosave
state and never touches `AssociateViewModel.SelectedDate` (spec Parts
O/P/T; `MainViewModelNavigationTests`).

**Application-layer read model.** `IPassdownHistoryService`
(`Application/History/PassdownHistoryService.cs`) is the seam the UI
uses: `ListAsync()` is a thin pass-through of
`IFinalizedPassdownRepository.ListAsync` (metadata only, never a PDF
blob); `GetAsync(date)` reads one full row via `GetByDateAsync` and
returns a `PassdownHistoryDetail` (`Summary` +
`FinalizedPassdownSnapshot? Snapshot` + `string? UnavailableReason`).
`GetAsync` never throws for a bad historical row: if
`SnapshotSchemaVersion` is newer than
`FinalizedPassdownSnapshot.CurrentSchemaVersion`, or
`FinalizedPassdownSnapshotSerializer.Deserialize` throws on malformed
JSON, `Snapshot` is `null` and `UnavailableReason` carries a plain-English
explanation while `Summary` (including `HasFrozenPdf`) is still returned
— the metadata row and PDF actions stay usable even when the structured
detail can't be read (`PassdownHistoryServiceTests`).

**Desktop read model / view model.** `PassdownHistorySummaryPresentation`
(`Desktop/Presentation/PassdownHistorySummaryPresentation.cs`) formats one
list row's display text ("August 25, 2026" / "Finalized Aug 26, 2026 at
6:02 AM" / "Scheduled"/"Recovery" / "PDF available") from
`FinalizedPassdownSummary` metadata only — it never touches the frozen
snapshot, so rendering the list never deserializes every row
(`FinalizedText` uses `EasternTimeZone`, matching the app's existing
Eastern wall-clock convention and the 6:00 AM cutoff itself).
`PassdownHistoryViewModel` (`Desktop/ViewModels/PassdownHistoryViewModel.cs`)
owns `HistoryItems`, `SelectedItem`/`SelectedDetail`,
loading/error state for both the list and the detail, and the
`OpenFinalPdfCommand`/`SavePdfCopyCommand` async commands. It depends
only on `IPassdownHistoryService`, `IFinalizedPassdownPdfRetrievalService`,
`IPdfSaveFilePicker` (the same picker Associate's Create PDF already
uses), and the new `IFinalizedPdfLauncher` — no
`IAssociatePassdownService`, no refresh orchestrator, no PDF generator,
so "History never regenerates a PDF or calls the network" is true by
constructor signature, not just by test coverage
(`PassdownHistoryViewModelTests`).

**Detail rendering — frozen, never live.** `HistoryView.axaml` binds its
detail sections (Park Hours, Attendance, Early Ride Time, Daily Feature,
product categories/rows, operational notes) directly to
`SelectedDetail.Snapshot.*` — every one of those values is already a
resolved display string/number from `FinalizedPassdownSnapshot`, never
recomputed from `AssociateWorkspace`, the current product catalog, or
any live park-hours calculation. Selecting a History row never loads an
`AssociateWorkspace` at all.

**PDF actions.** `Open Final PDF` and `Save PDF Copy` both start by
calling `IFinalizedPassdownPdfRetrievalService.GetPdfAsync` (Phase 9C;
reads stored bytes only, never generates). Before acting on the result,
`PassdownHistoryViewModel` verifies the retrieved byte length matches
`PdfSizeBytes` and its SHA-256 matches `PdfSha256` (spec Part U) — a
mismatch surfaces a support-facing error and never opens/saves/"repairs"
anything. `Save PDF Copy` reuses the existing `IPdfSaveFilePicker`
abstraction (no new save-picker type). `Open Final PDF` uses the new
`IFinalizedPdfLauncher` (`Desktop/Services/IFinalizedPdfLauncher.cs`);
its production implementation, `TempFileFinalizedPdfLauncher`, writes
the exact bytes to a per-filename temp file under the OS temp directory
and opens it with `Process.Start(UseShellExecute = true)`, i.e. whatever
the OS has associated with `.pdf`. It reuses the same temp path per
suggested filename (overwriting it) rather than minting a new file every
open, and deliberately never deletes the file afterward — the OS viewer
may still be reading it.

**Pre-9C / `HasFrozenPdf == false` records.** The structured detail still
renders normally (it comes from `SnapshotJson`, independent of the PDF
columns). `Open Final PDF`/`Save PDF Copy` are hidden and replaced with
"Exact historical PDF unavailable" text; nothing calls
`IAssociatePassdownPdfGenerator` or any other PDF-generation path for
these rows.

**Unknown/future snapshot schema and malformed JSON.** Handled entirely
in `IPassdownHistoryService.GetAsync` (see above) — the History screen
never crashes for one bad row. `HistoryView.axaml` shows the row's
metadata (date, finalized time, origin, PDF badge) plus
`DetailErrorMessage` in place of the structured sections, and PDF
actions remain available whenever `HasFrozenPdf` is true.

**List/BLOB loading efficiency.** The list path is
`HistoryView` → `PassdownHistoryViewModel.HistoryItems` →
`IPassdownHistoryService.ListAsync` → `IFinalizedPassdownRepository.ListAsync`,
which (Phase 9C) already selects `pdf_sha256`/`pdf_size_bytes` only,
never `pdf_bytes`. Selecting one row calls `GetAsync`/`GetByDateAsync`
(the single-row query) for that date's structured detail; a PDF action
separately calls `GetPdfAsync` for that date only. No code path loads
every row's PDF bytes at once.

**Empty state.** When `IPassdownHistoryService.ListAsync` returns no
rows (and the load didn't fail), the list column shows: "No finalized
passdowns yet." plus a one-line explanation of the 6:00 AM cutoff — no
fabricated sample rows.

**Read-only enforcement.** `HistoryView.axaml` contains no `TextBox`,
no include/exclude `CheckBox`, no Reset Override/Remove Custom Product,
no autosave indicator, no Check Online, no date-edit control, no Save
Draft, and no finalization control — only read-only text, chips, and the
two PDF-copy actions. A "Finalized — Read Only" badge sits in the detail
header. `PassdownHistoryViewModel` defines no delete/update/finalize
command (`PassdownHistoryViewModelTests`).

**Tests.** `Carowinds.Passdown.Application.Tests/History/PassdownHistoryServiceTests.cs`
covers the read model (newest-first, current-schema detail, newer-schema
handling, malformed JSON, legacy no-PDF rows). `Carowinds.Passdown.Ui.Tests`
adds `PassdownHistoryViewModelTests`, `HistoryViewTests`, and
`MainViewModelNavigationTests` (empty state, PDF-action gating,
integrity-mismatch handling, History/Associate date independence,
navigation-preserves-Associate-state, no mutating command, no
refresh/generator dependency) plus a `CompositionRootTests` addition
resolving the new DI registrations.

## Phase 9E — integration validation

Phase 9E adds no new production behavior. It is an integration-validation pass over
the complete Phase 9A–9D pipeline (draft → automatic finalization → refresh →
History), using real temp-file SQLite databases and real Application/
Infrastructure/Documents services end to end — never in-memory fakes for the
pieces being validated — plus a review of what unit-level Phase 9A–9D coverage
already proved. New tests live in
`tests/Carowinds.Passdown.Ui.Tests/ScheduledRefresh/Phase9EIntegrationTests.cs`
(nine integration tests) and as additional construction-level cases appended to
`tests/Carowinds.Passdown.Ui.Tests/HistoryViewTests.cs` (four new cases). No
revisions, manual finalization, delete-history, or Manager-workflow behavior was
added; the 6:00 AM cutoff and finalize-before-refresh ordering are unchanged.

**End-to-end pipeline (Part A/B).** `FullPipeline_RepresentativePassdown_...`
seeds a saved draft with manual/effective park hours, attendance/pre-solds, a
Daily Feature, an operational note, a representative official catalog product
with a comparison price, and a custom product; advances a `FakeClock` to
06:01 Eastern the following day; runs the real `ScheduledRefreshHost`; and
verifies the finalized row, its frozen PDF, and — reading back through the real
`PassdownHistoryService`/`FinalizedPassdownPdfRetrievalService` over the same
SQLite file, not fakes — that the History listing, detail snapshot, and
retrieved PDF bytes/hash/size all agree with what was just finalized. The same
test then mutates the current catalog price, park hours, and draft (notes/Daily
Feature) and re-reads the finalized record and History detail: the content
hash, PDF hash, PDF bytes, and every frozen field are byte-for-byte unchanged.
**PASS.**

**Missed-run recovery (Part C).** `MissedRunRecovery_MultiDayDowntime_...`
saves drafts for four consecutive dates, pre-finalizes one of them, sets the
clock to a fourth day later, and runs the real scheduled host once: every
eligible date finalizes, oldest first, the pre-existing record is left alone
(not duplicated), History lists all four newest-first, origins are `Recovery`
for the older missed dates and `Scheduled` for the one whose cutoff falls on
the current Eastern calendar date, and the refresh runner is invoked exactly
once after every recovery finalization completes. **PASS** (unit-level
recovery/origin coverage in `ScheduledPassdownFinalizerTests` already existed;
this adds the real-SQLite, real-History-read-back version).

**No-draft / empty-date safety (Part D).** Already fully covered at the unit
level (`ScheduledPassdownFinalizerTests.FinalizeEligibleAsync_NoSavedDraft_...`,
`..._LoadableButUnsavedWorkspace_...`) — a loadable-but-never-saved workspace
(every date) is never treated as eligible content; only a persisted draft row
is. No new test needed. **PASS.**

**Failure blocks refresh, then retry succeeds (Part E).**
`RealPdfGenerationFailure_BlocksRefresh_PreservesState_ThenSucceedsOnRetryAfterFixed`
uses a *real* failure — not a simulated/fake one — by saving a draft with
40 custom products and six long operational notes, which the real
`AssociatePassdownPdfGenerator`'s strict one-page layout genuinely rejects. The
scheduled host returns `FatalFailure`, no finalized row is written, the refresh
runner is never invoked, and the pre-refresh draft is unchanged. Re-saving the
draft with normal representative content and re-running the same host then
succeeds and the refresh runner is invoked. **PASS.**

**Duplicate/idempotency (Part F).**
`DuplicateInvocation_SameEligibleDate_ProducesOneRowAndSkipsOnSubsequentRuns`
runs the real scheduled host three times for the same already-finalized date:
one row (the same `Id` every time), one History entry, and the refresh runner
still runs on every invocation (a duplicate-finalization skip is not a
failure). `DuplicateInsert_BypassingApplicationCheck_UniqueIndexRejectsSecondRow`
proves the database's unique index remains the final defense even if the
application-level check-before-insert is bypassed. **PASS.**

**History scale / BLOB efficiency, and malformed/legacy tolerance (Parts G/H/R).**
`HistoryAtScale_MetadataOnlyListing_OneRowDetailAndPdfLoad_ToleratesBadRows`
seeds 40 real finalized rows plus one legacy pre-9C row (no PDF columns), one
malformed-JSON row, and one future-schema-version row (all via raw SQL, exactly
as a real historical/corrupted row would exist) — 43 rows total. `ListAsync`
still returns every row's metadata, newest-first, without throwing on the three
bad rows (the repository's list query has never selected `pdf_bytes` — Phase
9C — so scale here is a correctness/tolerance property, not a performance
regression risk). Selecting one normal date loads only that date's detail/PDF.
The legacy row's detail still renders with `HasFrozenPdf=false` and PDF
retrieval returns null (never regenerates); the malformed and future-schema
rows return `Snapshot=null` with a support-facing `UnavailableReason` and never
throw. **PASS.**

**Startup catch-up end-to-end (Part K).**
`StartupCatchUp_MissedCutoff_FinalizesAndHistoryAppears` drives the real
`StartupCatchUpRefresh.RunAsync` (not `ScheduledRefreshHost`) against a real
database with an eligible saved draft: finalization runs, History gains the
row, and the refresh runner is invoked once. **PASS.**

**Overlap (Part L).** `Overlap_LockAlreadyHeld_NeverFinalizes_NeverRefreshes`
manually acquires a real `FileScheduledRefreshLock` on a given path (simulating
a concurrently running scheduled/catch-up process), then runs
`ScheduledRefreshHost` with a lock factory pointed at the same path: the host
returns `Overlap`, never touches the finalizer, and never invokes refresh — no
duplicate finalization is possible while another holder has the lock.
`FileScheduledRefreshLockTests` (Phase 6B) already covers the lock primitive in
isolation; this is the Phase 9 finalization-specific case. **PASS.**

**6:00 AM Eastern boundary, including a DST transition (Part M).**
`DstBoundary_SpringForwardTransition_CutoffCorrectAtOrchestrationLevel` picks a
passdown date whose cutoff (`2026-03-08 06:00 America/New_York`) falls on the
actual 2026 spring-forward transition day, confirms the cutoff offset is
already EDT (-4), and runs the real scheduled host both one second before and
exactly at that cutoff instant: not-yet-finalized, then finalized. Existing
unit-level coverage (`FinalizationEligibilityTests`,
`ScheduledPassdownFinalizerTests.FinalizeEligibleAsync_DstAwareCutoffRemainsCorrect`)
already proved EDT-vs-EST offsets generally; this is the orchestration-level
check on an actual transition date the spec asked for. **PASS.**

**History UI visual validation (Part N).** No pixel/bitmap screenshot renderer
exists in this codebase (`Avalonia.Headless` is used for real-XAML-loader
construction checks only, deliberately without `Avalonia.Headless.XUnit` — see
`HeadlessTestSetup.cs`) — there was nothing to reuse for actual pixel
inspection, and building one was out of scope for a validation phase. What
Phase 9E adds is construction-level coverage for the states Phase 9D's
`HistoryViewTests` didn't yet exercise: a Recovery-origin record, a no-PDF
legacy record, a malformed/unsupported-schema detail (`Snapshot=null`,
`UnavailableReason` set), and dense content (six long operational notes, a long
Daily Feature, 25 product rows). All four construct through the real XAML
loader without error. Layout inspection at 1280×860 vs. `MainWindow`'s
`MinWidth="960"`/`MinHeight="640"` and no-scroll/clipping checks were **not**
pixel-verified — this is a genuine, honestly-recorded limitation, not a claimed
pass. **PARTIAL** (construction-level: PASS; pixel/visual-usability: NOT
EXECUTED).

**Windows manual validation (Part O).** No Windows host was available this
session (same limitation recorded for every prior Windows-validation section in
this repo, including `docs/release-manifest.md`). **NOT EXECUTED.**

**History/Associate navigation review (Part P).** Reviewed `MainWindow.axaml`
directly: History is a top-level section strip sibling to "Associate Passdown"
(not nested inside `AssociateView`'s Overview/Products/Notes rail), the active
section is visually obvious (accent underline + bold + accent foreground via
`Classes.active`), there is no Manager entry point, and both views stay
mounted/alive simultaneously so switching sections never disturbs Associate's
in-progress state (already covered by `MainViewModelNavigationTests`). No
layout change was needed. **PASS.**

**Dense/long passdown (Part Q).** `AssociatePassdownPdfGeneratorTests` and
`FinalizedPassdownProjectionTests` (Documents.Tests, pre-existing) already
prove the overflow policy end to end at the Documents layer: valid dense
content still fits one page, and content that doesn't fit is rejected outright
(never truncated/corrupted) — this phase's own
`RealPdfGenerationFailure_...` test (Part E, above) exercises that same
rejection through the *real* end-to-end pipeline, and `HistoryView_DenseContent_...`
(Part N, above) proves a dense-but-valid finalized record's History detail
renders all 25 product rows without truncation. **PASS.**

**Pre-9C compatibility (Part R).** Covered end-to-end by the legacy row in the
Part G/H/R scale test above (through the real `PassdownHistoryService` and
`FinalizedPassdownPdfRetrievalService`, not just the repository as
`SqliteFinalizedPassdownRepositoryTests` already covered). **PASS.**

**Migration/upgrade integration (Part S).** `PortableLifecycleTests` (already
existing, Infrastructure.Tests) covers schema 1→9 and schema 8→9 (the Phase 9C
PDF-column addition, preserving a pre-9C finalized row with null PDF columns).
Schema 7→8 (the `finalized_passdowns` table's own creation atop a database that
already has other populated tables) was not separately isolated as its own
test — `Migration0008CreateFinalizedPassdownsTable` is a pure additive `CREATE
TABLE`, the same shape as every other additive migration in this codebase, and
carries materially lower risk than the schema-9 PDF-column addition already
covered; not adding a redundant test for it here. `MigrationRunnerTests`
already covers the newer-than-known-version downgrade guard generically (Phase
7D), unaffected by Phase 9. **PASS.**

**Diagnostic export privacy (Part T).** Already fully covered —
`SqliteDatabaseDiagnosticsProviderTests.GetAsync_DatabaseContainsFinalizedPassdownWithSensitiveContent_NeverSurfacesItInDiagnostics`
seeds a finalized row with a distinctive secret note and secret PDF marker
bytes and asserts neither, nor any property containing "Pdf"/"Snapshot"/
"OperationalNote", appears in the reported diagnostics. No new test needed.
**PASS.**

**Release impact (Part U).** `docs/release-manifest.md`'s current artifact
(`Carowinds-Passdown-0.1.0-dev-win-x64.zip`, SHA-256
`eb44854b11ca3e280c814a97d64cfeee8538aa37287c9d88cd5a7717af8b7b31`) predates all
of Phase 9 (its recorded test count, 725/725, is from the Phase 8A/8B rebuild).
**This release ZIP is now stale** — it contains none of Phase 9A–9E's
finalization/History/frozen-PDF-storage code or the `finalized_passdowns`
table/schema-9 migration. No release was rebuilt in Phase 9E (per scope: Phase
9F owns the rebuild, new SHA-256, and release-manifest update). Any Windows
validation artifact produced before that rebuild would need to be labeled
test-only, not a release build; none was produced this phase (Part O above).

**Tests added.** 13 new tests: 9 in `Phase9EIntegrationTests.cs` (Parts A, B,
C, E, F ×2, G/H/R combined, K, L, M) and 4 appended to `HistoryViewTests.cs`
(Parts N/H/R/Q). No existing test was removed, skipped, or weakened.

**Final validation (Part X).** `dotnet build CarowindsPassdown.slnx` — 0
warnings, 0 errors. `dotnet test CarowindsPassdown.slnx --filter
"Category!=LiveSmoke"` — **858/858** passing (66 Domain + 221 Application + 82
Documents + 287 Infrastructure + 202 Ui), run twice to confirm no flakiness. No
live internet was used or required.

**Remaining limitations, honestly recorded:**
- History UI: no pixel/screenshot-based visual verification exists in this
  codebase; only real-XAML-loader construction checks (Part N above).
- Windows manual validation: not executed — no Windows host available (Part O).
- Migration 7→8 in isolation (finalized_passdowns table creation atop a
  populated pre-existing database) was assessed as low-risk rather than
  separately tested (Part S).
- The release ZIP/manifest is stale relative to HEAD; rebuilding it is
  explicitly Phase 9F's scope, not Phase 9E's.

Phase 9 is **not** being marked fully complete by this document — Phase 9F
(release rebuild) and any findings from an eventual Windows pass remain open.

## Persistence additions (Phase 9B)

`IAssociateDraftRepository.ListSavedDraftDatesAsync()`
(`Application/Persistence/AssociateDraftPersistence.cs`) — every saved
draft's `OperatingDate`, ascending, without deserializing any
`PayloadJson`. `SqliteAssociateDraftRepository`'s implementation is a
plain `SELECT operating_date FROM associate_drafts ORDER BY
operating_date ASC`. No migration was needed — `associate_drafts`
already existed (Phase 4A/Migration0006).

`IFinalizedPassdownFinalizationService` (registered in DI since Phase
9A) is now actually called, by `ScheduledPassdownFinalizer`
(`Application/Finalization/ScheduledPassdownFinalizer.cs`, also
registered as `IScheduledPassdownFinalizer` in DI). The coordinator
itself depends only on `IAssociateDraftRepository`,
`IAssociatePassdownService`, `IFinalizedPassdownRepository`,
`IFinalizedPassdownFinalizationService`, and `IClock` — no HTTP,
Playwright, `IRefreshOrchestrator`, or Windows Task Scheduler API.

## Phase 9F — release closeout

Phase 9F closes out Phase 9 as a shipped feature set. It adds no new production behavior — it is a
final requirements audit of 9A–9E as one system, an immutability re-check, a release rebuild, and a
documentation checkpoint. See `docs/release-strategy.md` "Phase 9F rebuild" for the full audit
(requirements matrix, immutability/order/schema/privacy/navigation audits) and
`docs/release-manifest.md` for the resulting artifact's exact identity.

**Audit outcome.** Every Phase 9A–9E claim in this document was checked directly against current
code and a fresh full test run rather than trusted from prior summaries alone: the repository stays
insert-only (no `Update`/`Delete` method exists on `IFinalizedPassdownRepository`, no `UPDATE`/
`DELETE` against `finalized_passdowns` exists anywhere in the codebase), `HistoryView.axaml` exposes
only `OpenFinalPdfCommand`/`SavePdfCopyCommand` and no mutating control, the finalize-before-refresh
ordering and 6:00 AM Eastern cutoff are unchanged, the current schema version is 9
(`Migration0008CreateFinalizedPassdownsTable` + `Migration0009AddFinalizedPassdownPdfColumns`,
additive atop unchanged Migrations 0001–0007), and diagnostics still never surface snapshot/PDF/note
content. No defect was found; no code change was needed.

**Validation.** `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **858/858**, identical to
Phase 9E's count (no test added, removed, or weakened).

**Release rebuild.** The release ZIP that Phase 9E correctly flagged as stale
(`docs/phase9-finalization.md` Phase 9E "Release impact") has been rebuilt from this exact commit.
The freshly published Desktop/Application/Infrastructure/Documents assemblies were checked directly
for Phase 9A–9D-only symbols (a byte-level `strings` search, not just file timestamps) to confirm
this is not a stale reuse of the Phase 7/8 artifact — see `docs/release-manifest.md` for the exact
symbol list and the resulting SHA-256/size/file-count. `packaging/windows/TESTER-GUIDE.txt` and
`packaging/windows/README-portable-release.md` were updated with a short, plain-language description
of Passdown History (they had none before Phase 9F, since History did not exist when they were last
written).

**Remaining gaps, honestly recorded (not fixed this phase, not blockers):**
- History pixel-level Windows visual validation — no bitmap/screenshot renderer exists in this
  codebase; only real-XAML-loader construction-level checks (unchanged since Phase 9E).
- Native Windows PDF open/save (Open Final PDF / Save PDF Copy) — not executed, no Windows host
  available.
- Task Scheduler behavior with Phase 9 finalization inserted ahead of refresh — not executed on a
  real Windows machine; stood in for by the real-SQLite, real-host `Phase9EIntegrationTests`.
- Carried forward from Phase 7E/7F: native x64 (vs. ARM64 emulation) Windows validation, and
  self-contained-runtime confidence on a machine with no .NET pre-installed.

**Phase 9 complete.** 9A (immutable model) through 9F (release closeout) are done, audited, and
shipped in the rebuilt artifact identified in `docs/release-manifest.md`. Intentionally **not**
implemented, and not implied to exist by anything above:
- revisions / version chains for a `PassdownDate` finalized more than once,
- manual finalization (`FinalizationOrigin.Manual` has no producer),
- delete-history (no delete control or command exists anywhere in the app),
- the Manager workflow (deferred per CLAUDE.md "Current scope decision").

Phase 10 has not started.
