# Phase 9 — Passdown History & Finalization

Phase 9A (this document's current scope) establishes the immutable
finalized-passdown domain/storage model only. It does not schedule
automatic finalization, does not build the History UI, does not implement
revisions, and does not touch the Manager workflow.

## Concept

```
editable Associate draft
    |
    v
finalization
    |
    v
immutable historical snapshot   <- Phase 9A (this doc)
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

## No-draft eligibility — open decision for Phase 9B

`AssociatePassdownService.LoadAssociateDateAsync` always returns a
composed `AssociateWorkspace` for any date, whether or not an
`AssociateDraft` row was ever saved for it (a missing draft simply
composes `AssociateDraftPayload.CreateDefault()`). This means "a
workspace can be loaded for this date" is **not** a meaningful "there is
real content to finalize" signal — Phase 9B must not treat every date the
app has ever displayed as eligible for automatic finalization.

The recommended (not yet implemented) interpretation for Phase 9B:
treat a date as having "eligible source content" only when
`IAssociateDraftRepository.GetAsync(date)` returns a non-null row — i.e.
an associate actually saved something for that date — rather than merely
being loadable. Phase 9A does not enforce or encode this rule anywhere;
it is documented here as the open decision Phase 9B needs to resolve
before wiring up the 6:00 AM job, per spec Phase 9 Part P.

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
  `EasternTimeZone` helper). This is a pure, tested helper only — nothing
  calls it yet. Phase 9B owns actually evaluating "is it past the cutoff"
  against the real clock and scheduling the job.

`DateTime.Now`/`DateTime.UtcNow` are not used anywhere in the Phase 9A
code; all timestamps flow from the composed `AssociateWorkspace`'s own
`asOf`/`finalizedAt` parameters, supplied by the caller (a test, or in
the future, the Phase 9B scheduler via `IClock`).

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

- The 6:00 AM automatic finalization job/scheduler.
- Windows Task Scheduler wiring for finalization.
- The Passdown History UI/screen.
- Reading a finalized snapshot back into any UI.
- Frozen PDF storage (`pdf_bytes` column and the code that populates it).
- Revisions/version chains for a `PassdownDate` finalized more than once.
- Any change to the Manager workflow.
- Locking/read-only behavior for a finalized date's Associate draft — the
  draft stays fully editable after finalization, exactly as before.
- Automatic snapshotting on save, on PDF export, or on date switch.

`IFinalizedPassdownFinalizationService` is registered in DI
(`App.axaml.cs`) but nothing in the running app calls it — it exists so
Phase 9B has a single, already-tested call to make.
