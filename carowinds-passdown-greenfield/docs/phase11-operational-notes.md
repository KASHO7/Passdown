# Phase 11 — Persistent Operational Notes

Phase 11A (this document's current scope) builds the persistent Operational Notes **foundation**:
the domain model, persistence, and Application-layer service — not the Notes Manager UI, not
Dashboard integration, and not inclusion in a daily passdown. Later Phase 11 subphases (11B–11F,
outlined below) build those in. No phase through 11A adds a Manager UI screen, changes the
Dashboard, or copies persistent-note content into any Associate draft or finalized History record.

## Purpose

A persistent Operational Note is a **supervisor-managed note that may span multiple days** —
distinct from a daily Associate Passdown's own Notes entry, which is scoped to one operating
date's draft and is frozen into History at finalization. A persistent note exists independently of
any specific day: "South Gate scanner #3 out of service" is true across a five-day repair window,
not just for today.

## Critical inclusion rule — read this before building anything on top of 11A

**Persistent Operational Notes are never automatically included in a daily Associate passdown.**

```
persistent note
    |
    v
appears as an available/relevant suggestion for a given operating date
    |
    v
a supervisor explicitly chooses whether to include it
    |
    v
chosen content becomes part of that day's Associate draft
    |
    v
finalized passdown freezes exactly what was selected
```

There is no code path in this phase — and there must never be one added silently in a later
phase — that goes straight from "this note is relevant today" to "this note's text is now in the
Associate draft." Phase 11D is where an explicit, user-driven selection UI performs that copy.
Until 11D ships, `IOperationalNotesService.ListRelevantForDateAsync` is read-only: nothing consumes
its result to mutate `AssociateDraftPayload.OperationalNotes` or any other draft state.

## Model (Part A–C, `Application/OperationalNotes/OperationalNote.cs`)

```csharp
public sealed record OperationalNote(
    Guid Id,
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    OperationalNoteStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);
```

- **Identity (Part B):** `Id` is a `Guid`, independent of title/body/date — a note can be edited
  over time without becoming "a different note."
- **Title/Body (Part C):** `Title` is required, trimmed, bounded to `OperationalNote.MaxTitleLength`
  (200 characters). `Body` is optional (`null` when blank), bounded to
  `OperationalNote.MaxBodyLength` (4000 characters) — generous enough for genuine operational
  detail, not a hard UX-breaking limit.
- **Out of scope (Part AB):** attachments, comments, authors/permissions, recurrence rules,
  notifications, assignments, acknowledgements, cloud sync. None of these exist in the model, and
  none should be added without a fresh product decision.

## Categories (Part D)

A closed enum, not free text:

```
General | Staffing | Admissions | GuestServices | Equipment | Events | Operations
```

This is the authoritative list for Phase 11A. Adding a category later is a deliberate product
decision, not a routine change.

## Priority (Part E)

```
Normal | Important
```

`Critical` was considered and rejected — no operational use case distinct from `Important` was
identified, and Phase 11 is not the Alerts system (no notification is ever triggered by priority).
Priority only affects presentation/sort order (see "Ordering" below).

## Effective date range (Part F)

`EffectiveFrom : DateOnly` (required), `EffectiveThrough : DateOnly?` (optional — `null` means
"until resolved"). `EffectiveThrough`, when present, must be on or after `EffectiveFrom`
(enforced by `OperationalNotesService`'s create/update validation). Both are date-only; no
timezone conversion is applied to them, matching how `PassdownDate` is already treated elsewhere
in this codebase.

## Status / resolution (Part G, Q, R)

Persisted status is deliberately two values: `Active` | `Resolved`. Resolving is an explicit
action (`IOperationalNotesService.ResolveAsync`), never automatic — see "No automatic expiration
mutation" below. A resolved note is **not deleted**; it remains a historical record with
`ResolvedAtUtc` set. `ResolveAsync` is idempotent: resolving an already-resolved note returns it
unchanged rather than overwriting the original `ResolvedAtUtc`.

**No Reopen operation exists in 11A** (Part R) — this was evaluated and deliberately deferred; a
resolved note cannot currently be moved back to `Active` through the service. If a future phase
needs this, it is a new, explicitly-named operation, not a repurposing of `UpdateAsync`.

## Pin/important decision (Part H)

**No `IsPinned` field was added.** `Priority = Important` already covers "this should stand out"
for Phase 11A's scope; a separate pin flag would duplicate that semantic without a distinct use
case. Revisit only if a future phase identifies a genuinely different meaning for "pinned" than
"important."

## Derived lifecycle state (Part Y)

```csharp
public enum OperationalNoteLifecycleState { Upcoming, Active, Expired, Resolved }

public static class OperationalNoteLifecycle
{
    public static OperationalNoteLifecycleState Derive(OperationalNote note, DateOnly operatingDate);
}
```

Layered on top of the simpler persisted `Status`, not a replacement for it — this is what lets
"manually resolved" and "date range ended on its own" stay distinguishable (see next section).
Implemented because it is expected to be directly useful for Phase 11B's list UI; covered by unit
tests in `OperationalNoteTests`.

## No automatic expiration mutation (Part X)

When a note's `EffectiveThrough` date passes, **nothing mutates its persisted `Status`.** It stays
`Active` in the database; only the derived `OperationalNoteLifecycleState` reports it as `Expired`
for a given `operatingDate`. There is no scheduled cleanup job. This preserves a real distinction a
supervisor will want later: "I marked this resolved" vs. "its date range simply ended."

## Relevance (Part J)

```csharp
public static class OperationalNoteRelevance
{
    public static bool IsRelevantForDate(OperationalNote note, DateOnly operatingDate) =>
        note.Status == OperationalNoteStatus.Active
        && note.EffectiveFrom <= operatingDate
        && (note.EffectiveThrough is null || operatingDate <= note.EffectiveThrough);
}
```

This is the **one authoritative rule**. `SqliteOperationalNoteRepository.ListRelevantForDateAsync`
pushes the same status/date-range filter into SQL (so a relevance query never loads every resolved
or out-of-range note), and `OperationalNotesService.ListRelevantForDateAsync` re-applies this exact
C# predicate over the SQL result as defense-in-depth — correctness never depends solely on the SQL
translation being right. Resolved notes, future notes (before `EffectiveFrom`), and expired notes
(past `EffectiveThrough`) are never relevant. Covered by `OperationalNoteTests` and
`OperationalNotesServiceTests`.

## Ordering (Part W)

Two distinct, documented, tested orderings in `OperationalNoteOrdering`:

- **`ByDefaultListOrder`** (general "browse all notes"): Active before Resolved; within Active,
  Important before Normal; then newest-created first; then `Id` as a final deterministic
  tie-breaker. Used by `IOperationalNotesService.ListAsync`.
- **`ByRelevance`** ("relevant for this operating date"): Important first; then `EffectiveFrom`
  ascending; then `CreatedAtUtc` ascending; then `Id` as a final deterministic tie-breaker. Used by
  `IOperationalNotesService.ListRelevantForDateAsync`.

## Persistence (Parts K, L, M)

`operational_notes` table (`Migration0010CreateOperationalNotesTable`, schema version **10**,
following schema 9 from Phase 9C — no historical migration was modified):

```sql
CREATE TABLE operational_notes (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    body TEXT,
    category TEXT NOT NULL,
    priority TEXT NOT NULL,
    effective_from TEXT NOT NULL,
    effective_through TEXT,
    status TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    resolved_at_utc TEXT
);

CREATE INDEX ix_operational_notes_status_effective_from ON operational_notes (status, effective_from);
```

One composite index — on `(status, effective_from)`, the pair every relevance query filters on
first — deliberately not a separate index per column, to avoid over-indexing a small local
database. The `finalized_passdowns` snapshot schema (`FinalizedPassdownSnapshot.CurrentSchemaVersion`)
is unrelated and unchanged — it stays at **1**.

`IOperationalNoteRepository` (`Application/OperationalNotes/OperationalNotePersistence.cs`):
`GetByIdAsync`, `ListAsync`, `AddAsync`, `UpdateAsync`, `ListRelevantForDateAsync`. Deliberately
**no delete method** — a resolved note remains a historical record; correcting/removing a bad row,
if ever necessary, is a support/database concern, matching the same convention
`IFinalizedPassdownRepository` already established. `SqliteOperationalNoteRepository` is the one
implementation.

## Application service (Part N–Q)

```csharp
public interface IOperationalNotesService
{
    Task<OperationalNote> CreateAsync(OperationalNoteDraft draft, CancellationToken ct = default);
    Task<OperationalNote> UpdateAsync(Guid id, OperationalNoteDraft draft, CancellationToken ct = default);
    Task<OperationalNote> ResolveAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OperationalNote>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OperationalNote>> ListRelevantForDateAsync(DateOnly operatingDate, CancellationToken ct = default);
}
```

`OperationalNotesService` depends on exactly `IOperationalNoteRepository` and `IClock` — no SQLite
detail, no UI presentation logic, no HTTP/refresh/PDF/Avalonia dependency
(`OperationalNotesServiceTests.Constructor_DependsOnlyOnRepositoryAndClock` proves this by
reflecting over the actual constructor, the same pattern
`OperationsDashboardServiceTests.Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`
established in Phase 10A).

`OperationalNoteDraft` (Title/Body/Category/Priority/EffectiveFrom/EffectiveThrough) is the
caller-supplied input for create/update — it deliberately carries no `Id`, `Status`, or timestamp,
so a caller can never bypass service semantics (e.g. supplying an arbitrary `CreatedAtUtc`, or
resolving a note through `UpdateAsync` instead of the explicit `ResolveAsync`).

**Create:** validates the draft, sets `Status = Active`, and sources `CreatedAtUtc`/`UpdatedAtUtc`
from `IClock.UtcNow` — never `DateTime.Now`/`DateTime.UtcNow`.

**Update:** re-validates the draft; preserves `Id`, `CreatedAtUtc`, `Status`, and `ResolvedAtUtc`;
advances `UpdatedAtUtc`. Throws `InvalidOperationException` for an unknown `Id`.

**Resolve:** sets `Status = Resolved`, `ResolvedAtUtc`/`UpdatedAtUtc = IClock.UtcNow`; idempotent
on an already-resolved note (returns it unchanged rather than re-stamping); throws
`InvalidOperationException` for an unknown `Id`; never deletes the row.

**Validation:** `Title` required (non-blank) and ≤ `OperationalNote.MaxTitleLength`; `Body` (when
present) ≤ `OperationalNote.MaxBodyLength`; `EffectiveThrough`, when supplied, must be on or after
`EffectiveFrom`. Violations throw `ArgumentException`/`ArgumentOutOfRangeException` — the same
convention `AssociateProductIdentity`/`FinalizedPassdownSnapshotSerializer` already use elsewhere
in this codebase, not a new bespoke validation-exception type.

## Explicit separation from Associate daily notes (Part U)

`OperationalNote` and `AssociateDraftPayload.OperationalNotes` are two unrelated types in two
unrelated tables. Nothing in this phase's code path can reach from one to the other:
`OperationalNotesService`'s constructor has no `IAssociatePassdownService`/`IAssociateDraftRepository`
dependency (see the reflection test above), and `OperationalNoteDraft` has no `PassdownDate` or
`AssociateDraft`-shaped field (`OperationalNotesServiceTests.OperationalNoteDraft_HasNoFieldCapableOfTargetingAnAssociateDraftOrDate`).
Existing Associate Notes behavior — autosave, PDF, finalization, History — is completely unchanged
by this phase; no file under `Application/Associate`, `Application/Finalization`, or `Documents`
was modified.

## Finalized-History independence (Part T)

Finalized History remains immutable and independent of this new table. No `OperationalNote.Id` (or
any other reference to a persistent note) is written into a `FinalizedPassdown` row in this phase.
The eventual Phase 11D design freezes a **copied display string** into that day's draft/snapshot
when a supervisor selects a persistent note for inclusion — never a live reference back to the
mutable `operational_notes` row. `FinalizedPassdownSnapshot.CurrentSchemaVersion` stays at 1; this
phase did not need to change it.

## Local-only guarantee (Part Z)

`OperationalNotesService`/`SqliteOperationalNoteRepository` have no `HttpClient`, browser, or
retrieval dependency of any kind — create/list/update/resolve are all local SQLite operations.
Proven by the same constructor-reflection test referenced above.

## Diagnostic privacy (Part AA)

`SqliteDatabaseDiagnosticsProvider` never queried `operational_notes` before this phase and still
doesn't — it only reports `PRAGMA user_version`, file size, and WAL presence, exactly as it already
did for `finalized_passdowns`. A regression test
(`SqliteDatabaseDiagnosticsProviderTests.GetAsync_DatabaseContainsOperationalNoteWithSensitiveContent_NeverSurfacesItInDiagnostics`)
seeds a note with distinctive secret title/body text and asserts neither appears in the reported
diagnostics, mirroring the equivalent Phase 9C test for finalized-passdown content.

## DI registration (Part AG)

Registered in `App.axaml.cs` alongside the rest of the composition root:

```csharp
services.AddSingleton<IOperationalNoteRepository, SqliteOperationalNoteRepository>();
services.AddSingleton<IOperationalNotesService, OperationalNotesService>();
```

`Migration0010CreateOperationalNotesTable` is appended to the existing migration list. No
ViewModel or View is registered — there is nothing for a UI to bind to yet (Part AH).

## What Phase 11A does *not* implement

- The Operational Notes Manager UI (create/edit/resolve screens) — Phase 11B.
- Any Dashboard surface for persistent notes — Phase 11C.
- Any code path that copies a persistent note's content into an Associate draft, automatically or
  otherwise — Phase 11D, and even then only via an explicit per-note user selection.
- Carry-forward/filtering/UX polish beyond the ordering rules above — Phase 11E.
- A Reopen operation for a resolved note (Part R) — deferred, not built.
- `IsPinned` (Part H) — evaluated and deliberately omitted; `Priority = Important` covers it.
- Alerts, notifications, or an Operations Calendar — out of scope for all of Phase 11.
- The Manager workflow — still deferred per `CLAUDE.md`.
- Any change to Associate Notes, Dashboard notes preview, or finalized History.

## Planned subphases

```
11A   persistent model/persistence/service foundation (this document)
11B   Operational Notes Manager UI
11C   Dashboard active-note integration
11D   explicit user-selected inclusion into Associate Passdown
11E   carry-forward/filtering/UX polish
11F   final audit/release rebuild
```

## Tests added

- `Carowinds.Passdown.Application.Tests/OperationalNotes/OperationalNoteTests.cs` — relevance
  (boundary/before/after/resolved), derived lifecycle state (all four values, including
  "expired but status stays Active"), and both `OperationalNoteOrdering` rules including a
  deterministic-tie-break case.
- `Carowinds.Passdown.Application.Tests/OperationalNotes/OperationalNotesServiceTests.cs` —
  create/update validation (blank title, over-length title/body, invalid/valid date ranges,
  null-body, null-`EffectiveThrough`), `IClock`-sourced timestamp behavior, update preserving
  identity/`CreatedAtUtc`/Status, resolve semantics and idempotency, resolve/update on an unknown
  Id, relevance listing (including "expired note is not auto-resolved" and "no delete"), the
  constructor-reflection no-network/no-Associate-dependency guarantee, and the
  Associate-draft-separation regression tests.
- `Carowinds.Passdown.Infrastructure.Tests/Storage/Repositories/SqliteOperationalNoteRepositoryTests.cs`
  — round-trip of every field (including nullable `EffectiveThrough`/`Body`, every category, every
  priority, exact timestamps), update, resolve-via-update, list (including ordering), relevance
  query, no-delete-method check, reopened-connection persistence, and a SQL-injection-shaped
  content round-trip proving parameter binding.
- `Carowinds.Passdown.Infrastructure.Tests/Storage/PortableLifecycleTests.cs` — extended
  `AllMigrations`/`CurrentSchemaVersion` (10) and the historical-upgrade schema-version-history
  assertion; added `Schema9ToSchema10_CreatesOperationalNotesTable_AndPreservesExistingFinalizedAndDraftData`
  (new table/index shape, existing `associate_drafts`/`finalized_passdowns` rows untouched, new
  table immediately usable through the real repository).
- `Carowinds.Passdown.Infrastructure.Tests/Diagnostics/SqliteDatabaseDiagnosticsProviderTests.cs` —
  added the operational-note privacy regression test described above.
- `Carowinds.Passdown.Ui.Tests/CompositionRootTests.cs` — added
  `BuildServiceProvider_ResolvesOperationalNotesServices`.

## Full validation

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — see the Phase 11A commit's
final report for the exact count. No live internet was used or required.
