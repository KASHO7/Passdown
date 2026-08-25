# Phase 9 — Passdown History & Finalization

Phase 9A established the immutable finalized-passdown domain/storage
model. Phase 9B added *automatic* finalization: a 6:00 AM Eastern
cutoff, evaluated by the existing scheduled task and the interactive
startup catch-up path, always before either ever runs a source refresh.
Phase 9C (this document's current scope) extends every finalized record
to also freeze the exact PDF bytes an associate would have printed at
finalization time, atomically alongside the structured snapshot. No
phase through 9C builds the History UI, implements revisions, or
touches the Manager workflow.

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
frozen PDF                       <- Phase 9C (this document)
    |
    v
Passdown History UI              <- Phase 9D
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

- The Passdown History UI/screen — Phase 9D. `IFinalizedPassdownPdfRetrievalService`
  exists as the retrieval seam Phase 9D will call, but nothing wires it
  to any UI yet.
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
