# Phase 9 — Passdown History & Finalization

Phase 9A established the immutable finalized-passdown domain/storage
model. Phase 9B (this document's current scope) adds *automatic*
finalization: a 6:00 AM Eastern cutoff, evaluated by the existing
scheduled task and the interactive startup catch-up path, always before
either ever runs a source refresh. Neither phase builds the History UI,
implements revisions, or touches the Manager workflow. Phase 9B does not
add frozen PDF storage — see "PDF storage" below.

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
```

No `FinalizedBy`/account identity — accounts are Phase 16+, and there is
no authenticated user to record yet.

`FinalizedPassdownSummary` (`PassdownDate`, `FinalizedAtUtc`, `Origin`) is
a lightweight read model for a future History UI listing, without
deserializing every row's `SnapshotJson`.

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
existing migration or table. No PDF blob column yet — see below.

## PDF storage — deferred to Phase 9C

Phase 9A does not add a `pdf_bytes`/`pdf_blob` column. The migration's
fields match the spec's minimal example (`id`, `passdown_date`,
`finalized_at_utc`, `snapshot_schema_version`, `snapshot_json`,
`content_hash`, `origin`) with nothing extra. Reasoning: a later
`ALTER TABLE finalized_passdowns ADD COLUMN pdf_bytes BLOB NULL` is
itself a safe, additive, forward-only migration under the existing
`MigrationRunner` model, and adding the column now would leave an unused
nullable column with no code ever writing or testing it through Phase
9A/9B. The table's identity (`Id`/`PassdownDate`/unique index) does not
change when that column arrives, so nothing about Phase 9A's design is
invalidated by deferring it. SQLite BLOB storage is expected to be more
than adequate for a one-page PDF (tens to low hundreds of KB) stored
alongside its own metadata/snapshot row, transactionally, rather than an
external file path that can silently disappear if a folder moves — that
remains the intended Phase 9C direction.

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

`FinalizedPassdownFinalizationService.FinalizeAsync` builds the snapshot,
serializes it, and computes its content hash entirely in memory before
ever calling `IFinalizedPassdownRepository.AddAsync`. If building,
serializing, or hashing throws, no row is written. The subsequent
`AddAsync` is a single `INSERT` statement, which SQLite itself commits
atomically — there is no window in which a partially-written finalized
row can exist. (Phase 9C's PDF blob is expected to become another column
on the same single-row insert, not a second statement, preserving this
guarantee.)

## Not implemented in Phase 9A (by design)

- The 6:00 AM automatic finalization job/scheduler — added in Phase 9B.
- Windows Task Scheduler wiring for finalization — Phase 9B reuses the
  existing Phase 6C task/trigger rather than adding one.
- The Passdown History UI/screen.
- Reading a finalized snapshot back into any UI.
- Frozen PDF storage (`pdf_bytes` column and the code that populates it).
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
- Frozen PDF blob storage, revisions, History UI, and the Manager
  workflow — same as Phase 9A, still deferred to Phase 9C/9D/later.

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
