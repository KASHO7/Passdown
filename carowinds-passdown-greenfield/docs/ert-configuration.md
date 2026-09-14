# Early Ride Time (ERT) configuration — Phase 17B

Replaces the Phase 13F derived-from-park-hours behavior (`EffectiveHours.NorthGateOpening`, i.e.
"park opening minus 30 minutes") with a persistent, user-authored monthly default plus an optional
per-date override. Real-world Early Ride Time is an operational setting that stays the same for an
entire month and changes month to month, with occasional single-date exceptions (maintenance, a
delayed opening, a temporary attraction swap) — it has never actually tracked park opening time, so
the old derivation was a plausible-looking but incorrect proxy.

Baseline this phase was built against: Phase 17A complete, DB schema 13, Draft schema 3, Snapshot
schema 1, SyncSchemaVersion 1, 1980/1980 deterministic tests.

## 1. The monthly model

`MonthlyErtConfiguration` (`src/Carowinds.Passdown.Application/Ert/MonthlyErtConfiguration.cs`) is
keyed by `Year` + `Month` together (`YearMonthKey`, invariant `yyyy-MM`) — September 2026 and
September 2027 are independent records; a month is never keyed by month-number alone. Fields:
`State` (`Configured` | `NoErt`), `StartTime` (`TimeOnly?`), `Rides` (`IReadOnlyList<string>`),
`Note` (`string?`), `ModifiedUtc`.

### NotConfigured vs. NoErt

These are deliberately distinct states, spelled out because they are easy to conflate:

- **NotConfigured** — no row exists for this year/month at all. The associate has not yet told the
  app anything about this month's ERT. Never fabricated from park hours or any other source (spec
  Part 6/34/35) — a month with no row always resolves to `EffectiveErtKind.NotConfigured`.
- **NoErt** — a row exists, and it explicitly says "no Early Ride Time this month." This is a
  positive, user-confirmed statement, not an absence. Once a month is marked `NoErt`, the app never
  re-prompts for it (spec Part 11).

A configuration can never simultaneously be `NoErt` and carry a start time/ride list —
`ErtConfigurationService.SaveMonthlyAsync`/`SaveOverrideAsync` normalize this server-side (NoErt
always clears start time and rides, regardless of what a caller supplied), rather than rejecting a
possibly-malformed remote sync payload outright.

## 2. Date overrides

`ErtDateOverride` (same file's sibling, `ErtDateOverride.cs`) is keyed by `OperatingDate`
(`DateOnly`), with the identical `State`/`StartTime`/`Rides` shape plus `Reason` instead of `Note`.
An override may exist even when no monthly configuration exists for its month at all (spec Part
38.I) — an exceptional date can be known before the rest of the month is configured, and the
resolver never requires a monthly record as a precondition for an override.

## 3. Precedence

The one authoritative rule, `ErtPrecedenceResolver.Resolve` (`src/Carowinds.Passdown.Domain/Ert/ErtPrecedenceResolver.cs`,
pure, no I/O):

1. A date override, if one exists for the date — wins outright, regardless of whether a monthly
   configuration exists.
2. Otherwise, the month's default, if one exists.
3. Otherwise, `NotConfigured`.

`Application.Ert.EffectiveErtResolver` is the only place a date override and a monthly
configuration are ever loaded together (one repository call each, per spec Part 43's "at most one
monthly lookup and one override lookup per resolution"). Every consumer — the Associate screen, the
Dashboard, the PDF, and the finalized snapshot builder — reads the same
`AssociateWorkspace.EarlyRideTime` value, resolved once in `AssociatePassdownService.ComposeAsync`;
none of them re-resolves or re-derives precedence independently (spec Part 4's "one authoritative
resolver... never duplicate precedence logic").

## 4. Time format and ride identity

ERT time entry reuses `Domain.Hours.OperationalTimeParser`/`OperationalTimeFormatter` exactly — the
same grammar as Manual Hours/Private Buyout (`9`, `9am`, `9:00`, `930am`, `10:30 PM`, etc.). No
second parser was introduced.

Rides are free text (`IReadOnlyList<string>`), matching the pre-existing
`EarlyRideTimeDetails.ParticipatingAttractions` precedent — no attraction catalog with stable IDs
exists anywhere in this codebase to key against, and inventing one was out of scope for this phase
(spec Part 10/36). `ErtConfigurationService` trims, drops blank entries, and de-duplicates
case-insensitively on save.

## 5. Persistence

Two new SQLite tables (`Migration0014CreateMonthlyErtConfigurationsTable`,
`Migration0015CreateErtDateOverridesTable`; DB schema 13 → 15):

```
monthly_ert_configurations
    year_month TEXT PRIMARY KEY   -- "yyyy-MM"
    year INTEGER
    month INTEGER
    state TEXT
    start_time TEXT               -- "HH:mm:ss", nullable
    rides_json TEXT                -- JSON string array
    note TEXT                      -- nullable
    modified_utc TEXT

ert_date_overrides
    operating_date TEXT PRIMARY KEY  -- "yyyy-MM-dd"
    state TEXT
    start_time TEXT
    rides_json TEXT
    reason TEXT
    modified_utc TEXT
```

Both migrations are purely additive — no existing table or column changes. `IMonthlyErtRepository`/
`IErtDateOverrideRepository` each expose a `DeleteAsync` used only as the Personal Sync tombstone
primitive (mirroring `IOperationalNoteRepository.DeleteAsync`); the product-facing "Remove override"
action also calls `ErtDateOverride`'s `DeleteAsync` directly (an override removal is a genuine
deletion, not a state toggle), while the monthly `DeleteAsync` is sync-only — the product-facing "no
ERT this month" is always the `NoErt` state, never row absence.

## 6. Personal Sync

`MonthlyErt` and `ErtDateOverride` were added as new `SyncEntityType` values — no `SyncSchemaVersion`
bump (adding an enum member is not an envelope shape change; confirmed by the same precedent
`FinalizedPassdown`'s own addition in Phase 16E set). Each gets its own sync payload with its own
`CurrentSchemaVersion = 1`, independent of every other entity's payload version
(`MonthlyErtSyncPayload`/`ErtDateOverrideSyncPayload` in `Application/PersonalSync/`).

Both are mutable, tombstone-capable, single-JSON-blob entities — architecturally identical to
Operational Notes, so `PersonalSyncService`'s import/export methods for them
(`Import/ExportMonthlyErtsAsync`, `Import/ExportErtDateOverridesAsync`) are close mirrors of
`Import/ExportNotesAsync`, reusing the same entity-agnostic `SyncReconciliation.Decide` precedence
function untouched.

**A real bug found and fixed while adding these two types**: `PersonalSyncService.ExportPendingTombstonesAsync`
previously had a hardcoded two-branch `if (entityType == AssociateDraft) {...} else {...}` to resolve
a tombstone's target file path and payload schema version — the `else` branch assumed
`OperationalNote`. Adding a third and fourth entity type without fixing this would have silently
misparsed a `MonthlyErt`/`ErtDateOverride` entity id as a Note GUID. This was generalized into a real
`switch (entityType)` covering all four mutable/tombstone-capable types before wiring the new sync
paths in.

Similarly, `PersonalSyncConflictResolutionService`'s `ResolveKeepLocalAsync`/`ResolveUseRemoteAsync`/
`ResolveAcceptDeletionAsync`/`BuildSummaryAsync` previously assumed exactly Draft-or-Note via the same
two-branch pattern. All four were extended to dispatch on all four mutable entity types (
`FinalizedPassdown` remains explicitly refused, unchanged from Phase 16E — immutable History has no
Keep/Use/Accept resolution model). `MonthlyErt`/`ErtDateOverride` conflicts resolve exactly like
`AssociateDraft` conflicts — whole-record replace, one record per key — with display labels such as
`"September 2026 Early Ride Time"` / `"Sep 19, 2026 Early Ride Time override"`.

## 7. Conflict behavior

Identical floor behavior to drafts/notes (spec Part 22): a genuine concurrent edit on two devices is
never silently auto-merged or auto-picked. Both `MonthlyErt` and `ErtDateOverride` conflicts surface
through the same Keep-This-Device / Use-Other-Device / Accept-Deletion resolution flow already built
for drafts/notes in Phase 16D — no new conflict-resolution UX was invented.

## 8. Migration / existing-user behavior

No monthly ERT record is fabricated from any prior park-hours-derived data on upgrade (spec Part 34)
— both new tables start empty for every existing user, and every month reads as `NotConfigured`
until the associate explicitly configures it. `AssociateDraftPayload.CurrentSchemaVersion` bumps
3 → 4 for removing the old free-text `EarlyRideTime` field (`EarlyRideTimeDetails`, which only ever
carried supplementary text — the actual start time was always derived elsewhere, never persisted in
the draft). A pre-17B draft's `"EarlyRideTime"` JSON property is simply ignored on deserialize;
nothing is migrated forward from it, matching the explicit "never fabricate persisted monthly
intent from old data" instruction.

## 9. Finalization / immutability

`FinalizedEarlyRideTimeSnapshot` (`Application/Finalization/FinalizedPassdownSnapshot.cs`) gained new
fields (`Kind`, `Source`, `StartTimeText`, `Rides`, `Note`) **alongside** the original pre-17B fields
(`Enabled`, `Details`, `ParticipatingAttractions`), rather than replacing them. `Kind` is nullable
specifically so `null` unambiguously marks a pre-17B historical row (read via the legacy fields,
unchanged display) versus a genuine post-17B `NotConfigured` resolution (read via the new fields).
`FinalizedPassdownSnapshot.CurrentSchemaVersion` bumps 1 → 2 for this additive shape change.
Finalizing freezes whatever `AssociateWorkspace.EarlyRideTime` resolved to at that exact moment —
because the resolver is the only source of ERT and nothing else in the finalize path recomputes it,
later monthly/override edits can never alter an already-finalized snapshot by construction, with no
extra guard needed.

## 10. Draft semantics

The monthly configuration is never copied into every draft — `AssociatePassdownService.ComposeAsync`
re-resolves live on every load via `IEffectiveErtResolver`, so a non-finalized draft always reflects
the current monthly/override state automatically (spec Part 16), and there are never 30 duplicated
monthly records lying around.

## 11. Known limitations

- Ride names remain free text with no stable identity — this matches the pre-existing
  `ParticipatingAttractions` precedent and was an explicit scope boundary for this phase (spec Part
  10/36), not an oversight.
- No monthly-ERT-specific UI beyond the Associate Overview screen's inline Configure/Override panels
  — no calendar badges, no Manager workflow (both explicitly deferred).
- The PDF's `NotConfigured` state prints "Not configured for {Month Year}" rather than omitting the
  section — a deliberate choice (spec Part 13) since this is an Associate-to-Associate operational
  document, not guest-facing, and ops staff need to know ERT was never set up for the month rather
  than have the section silently vanish.
