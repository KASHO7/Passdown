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
11A   persistent model/persistence/service foundation
11B   Operational Notes Manager UI (this document's Phase 11B section, below)
11C   Dashboard active-note integration
11D   explicit user-selected inclusion into Associate Passdown
11E   carry-forward/filtering/UX polish
11F   final audit/release rebuild
```

---

# Phase 11B — Operational Notes Manager UI

Phase 11B builds the supervisor-facing screen for browsing, creating, editing, and resolving
persistent Operational Notes (Phase 11A's foundation). It is a CRUD-style management surface except
there is **no hard delete** — resolving preserves the note as a historical record, exactly as 11A's
`IOperationalNoteRepository`/`IOperationalNotesService` already guarantee. Phase 11B does not touch
Dashboard, Associate Passdown, or History, does not add an "Include in today's passdown" workflow, and
does not require a new database migration — schema stays **10**, snapshot schema stays **1**.

## Navigation placement (Part A)

`AppSection` (`MainViewModel.cs`) gained one value — `OperationalNotes` — placed between
`AssociatePassdown` and `History`:

```
Dashboard | Associate Passdown | Operational Notes | History
```

The user-facing label is **"Operational Notes"**, not "Notes Manager" — matching how 11A's own
documentation and this screen's own header already refer to the feature. No `Manager`/`Alerts`/
`Calendar` value was added (`MainViewModelNavigationTests.AppSection_ContainsOperationalNotesAndNoManagerAlertsOrCalendar`
asserts the exact four-value enum).

## Default startup (Part B)

Dashboard remains `SelectedSection`'s default (`AppSection.Dashboard`), unchanged by this phase.
Regression-tested by `MainViewModelNavigationTests.SelectedSection_StillDefaultsToDashboard_WithOperationalNotesAdded`.

## `OperationalNotesViewModel` (Part C)

Depends on exactly `IOperationalNotesService` and `IClock` — the same two-dependency shape
`OperationalNotesService` itself uses, and mirrors `AssociateViewModel`'s own
`EasternTimeZone.ConvertFromUtc(clock.UtcNow)` convention for "today" (`ReferenceDate`), read fresh on
every load/filter pass rather than cached once. It never queries SQLite directly, never recomputes
relevance/lifecycle/ordering/validation itself — every one of those is reused from
`OperationalNoteLifecycle`, `OperationalNoteOrdering`, and the service's own validation
(`OperationalNotesServiceIntegrationTests` exercises the real service + an in-memory repository
end-to-end specifically to prove this composition works, not just that the view model's own logic is
internally consistent).

## Manager layout (Part D/AE/AF)

A Grid-based master/detail screen (`OperationalNotesView.axaml`): a header band, a filter/action row
(`[All][Active][Upcoming][Expired][Resolved]` tabs + `+ New Note`), then a 340px note list beside a
detail/editor column. At 1280×860 the list and detail/editor are visible simultaneously with no
page-level scroll; each column has its own `ScrollViewer` for overflow. At 960×640 the same Grid
reflows to the same proportions with per-column scrolling rather than clipping. Visual language (navy/
blue accents, compact chip badges, restrained borders, no shadows/gradients/emoji) mirrors
`HistoryView`/`DashboardView`'s existing self-contained `UserControl.Styles`, per the UI Polish skill.

## Default/list filtering (Part F/G/H/I)

Lifecycle filter tabs: **All | Active | Upcoming | Expired | Resolved**, default **Active** — the
operationally useful default, with every other bucket one click away (including `All`, deliberately
included rather than deferred, since it costs nothing extra once the four specific buckets already
exist). Filtering is applied in `OperationalNotesViewModel.ApplyFilter` purely by calling
`OperationalNoteLifecycle.Derive` over the already-`ByDefaultListOrder`-sorted cached list — no
competing UI sort, no re-derivation of relevance rules.

**Category and Priority filters are deliberately deferred to Phase 11E.** Five lifecycle tabs plus a
category dropdown plus a priority toggle would not fit this screen's compact filter row without either
wrapping awkwardly or growing the header — lifecycle state is the one dimension a supervisor needs
immediately ("what's active right now"); category/priority are already visible per-row and don't need
a second filtering axis for an initial release.

## Create flow (Part J)

`+ New Note` (`NewNoteCommand`) opens a create editor with: Title (required, ≤200 chars), Body
(optional, ≤4000 chars), Category (required, closed `OperationalNoteCategoryOption` list — no "not
set" entry, since Category itself has none), Priority (required, `Normal`/`Important`), Effective From
(required, `CalendarDatePicker`, defaults to today), and Effective Through (optional, with an explicit
**"Until resolved"** checkbox — checked by default — that clears/disables the date picker rather than
requiring a far-future placeholder date). `Id`/`CreatedAtUtc`/`UpdatedAtUtc`/`ResolvedAtUtc` are never
exposed — they remain system-managed by `OperationalNotesService`.

## Edit flow (Part K)

Selecting a note shows read-only detail first (`ShowReadOnlyDetail`); an explicit **Edit** button
switches to the editor (`ShowEditor`), pre-populated from the selected note. This is the "reduce
accidental edits" pattern the spec prefers, and is a clean fit for this screen since (unlike Associate
Passdown's always-editable draft) a persistent note is not being actively worked on continuously.

## Save/Cancel (Part L)

**Save** validates through `IOperationalNotesService.CreateAsync`/`UpdateAsync` (the sole authority),
reloads the list afterward, and re-selects the saved note — so the just-created/edited note becomes (or
remains) the selection, unless the edit itself moved it out of the currently active filter (e.g.
extending an Expired note's range while viewing the Expired tab — see Part P below), in which case it
correctly drops out of view exactly like a successful Resolve does. **Cancel** discards every unsaved
editor field and returns to the prior read-only detail/list state without calling the service at all.

## Validation presentation (Part M)

Two conditions get immediate client-side feedback before any service call, matching the spec's own
examples verbatim: a blank title → **"Enter a title."**; Effective Through before Effective From →
**"Effective Through cannot be before Effective From."** Any other `ArgumentException`/
`ArgumentOutOfRangeException` the service still throws (e.g. an over-length title) is mapped to one
generic **"This note could not be saved. Check the fields and try again."** — never a stack trace,
exception type name, or raw SQLite error. The service's validation rules themselves are never
duplicated; only these two specific early checks exist client-side.

## Resolve behavior (Part N)

**Resolve** is a two-step inline confirmation, not a modal dialog: clicking **Resolve**
(`RequestResolveCommand`) shows "Mark this operational note resolved? This preserves the note but
removes it from the Active list." with **Yes, Resolve**/**Cancel** buttons in place of the normal
action row; confirming (`ConfirmResolveCommand`) calls `IOperationalNotesService.ResolveAsync`,
reloads, and lets the note drop out of the current filter if it no longer matches (e.g. leaving the
Active tab). No **Reopen** action exists, matching 11A's own deferred-by-design decision.

## Resolved-note presentation (Part O)

**Resolved notes are read-only in 11B**, deliberately: no Edit or Resolve control appears
(`SelectedDetail.IsResolved` hides the entire action row), and the detail panel instead shows
**Resolved** plus its timestamp (`ResolvedAtText`). This was a deliberate choice, not an oversight —
since no Reopen operation exists yet, presenting Edit/Resolve controls on an already-resolved note
would invite an editor that has nothing meaningful to do.

## Expired-note behavior (Part P)

Expired is a **derived** presentation state (`OperationalNoteLifecycleState.Expired`), never a
persisted one — an expired note's `Status` stays `Active` in the database, and this screen never writes
`Status = Resolved` for it. Expired notes remain fully editable; extending `EffectiveThrough` forward
through the normal edit flow moves the note back to `Active` on save, exactly as the spec's own example
describes. Covered by `OperationalNotesViewModelTests.ExpiredNote_CanBeEditedToExtendEffectiveThrough`.

## Upcoming-note behavior (Part Q)

Upcoming notes are fully editable and are never presented as inactive or an error state. The date-range
formatter (`OperationalNoteDateRangeFormatter`) shows **"Starts \<date\>"** for an Upcoming note instead
of a from/through range, since the range hasn't begun yet.

## Important-priority presentation (Part R)

Important is communicated primarily through **text** ("Important" appears as the Priority label
itself, both in the list row and the detail chip row), reinforced with restrained weight/accent
(`TextBlock.priorityText.important`, `Border.chipAccent` — the same blue "chip accent" brush already
used elsewhere in the app, not an alarm-style red). Priority is never presented via color alone.

## Category/date-range presentation (Part S/T)

`OperationalNoteCategoryOption`/`OperationalNotePriorityOption` are the single, centralized places that
map the closed enums to user-facing labels (e.g. `GuestServices` → "Guest Services") — reused
identically by the create/edit ComboBoxes and by the list/detail presentation builders, so the mapping
can never drift between the two. `OperationalNoteDateRangeFormatter` produces friendly text only
("Aug 26 – Aug 30", "Aug 26 – Until resolved", "Starts Aug 30") — never a raw ISO value — and includes
the year only when it differs from the reference date's year, since a bare "Aug 26" becomes ambiguous
once a note can span a year boundary.

## Empty states (Part U)

Two distinct empty states, both handled in `OperationalNotesViewModel`/`OperationalNotesView`:
**no notes at all** ("No operational notes yet. Create a note for information that should remain
visible across days." plus its own `+ New Note` button) versus **notes exist but none match the current
filter** (e.g. "No active operational notes.", one message per lifecycle bucket via
`FilteredEmptyStateMessage`).

## Local-only guarantee (Part V)

Reload (`ReloadCommand`) calls only `IOperationalNotesService.ListAsync()` — the same local-only
service 11A already proved has no HTTP/Playwright/browser dependency. It is not coupled to Check
Online and never polls automatically.

## Unsaved-changes handling (Part W/X)

The simplest option that still prevents silently discarding work, deliberately not a global
dirty-state framework or a modal dialog: while `EditorMode` is `Create` or `Edit`
(`OperationalNotesViewModel.IsEditing`), **Reload**, **New Note**, **Edit**, and **Resolve** are all
disabled via their own `CanExecute`, and the note list itself is disabled (`ListBox.IsEnabled` bound to
`!IsEditing`) so a different row cannot even be clicked. The same signal is reused one level up for
top-level navigation (Part X): `MainViewModel` subscribes to `OperationalNotesViewModel.IsEditing`
changing and disables **Dashboard**/**Associate Passdown**/**History**'s own `CanExecute`
(`CanLeaveOperationalNotesSection`) whenever the Operational Notes section is both currently shown and
mid-edit — a Notes-specific guard, not a redesign of the app shell's navigation. A supervisor must
explicitly Save or Cancel before any other section becomes reachable again.
Covered by `MainViewModelNavigationTests.DirtyOperationalNotesEditor_DisablesOtherTopLevelSectionButtons`.

## Associate Passdown separation (Part Y)

`OperationalNotesViewModel` has no dependency on `AssociateViewModel`/`IAssociatePassdownService`, and
`AssociateViewModel`'s own constructor has no `IOperationalNotesService` dependency
(`MainViewModelNavigationTests.AssociateViewModel_HasNoOperationalNotesServiceDependency` — the same
constructor-reflection pattern 11A already used in the opposite direction). Creating, editing, and
resolving persistent notes never alters the current Associate draft, never triggers an Associate
autosave, and (since there is no code path capable of reaching `AssociateDraftPayload.OperationalNotes`
at all) can never appear in an Associate PDF or a finalized History snapshot. Regression-tested by
`MainViewModelNavigationTests.OperationalNoteOperations_NeverChangeAssociateDraft`.

## Dashboard separation (Part AA)

`DashboardView`/`OperationsDashboardViewModel`/`OperationsDashboardService` are entirely unmodified by
this phase — the Dashboard continues to show only today's Associate Operational Notes preview.
Persistent notes gain no Dashboard surface until Phase 11C.

## History separation (Part AB)

`HistoryView`/`PassdownHistoryViewModel`/`IPassdownHistoryService` are entirely unmodified. Finalized
History remains exactly as Phase 9 designed, with no reference to the Notes Manager or any
`OperationalNote.Id`.

## Privacy (Part AC)

Every log call in `OperationalNotesViewModel` logs only the note's `Id` — never `Title`/`Body` — e.g.
`"Operational note saved: {NoteId}"` / `"Operational note resolved: {NoteId}"`, matching 11A's own
diagnostic-privacy convention.

## Accessibility (Part AD)

Every control the spec calls out carries an `AutomationProperties.Name`: the top-level section button
("Operational Notes section"), the note list ("Operational notes list"), New Note, each lifecycle
filter tab, Note title/body inputs, Category/Priority selectors, Effective From/Through, Save, Cancel,
Edit, and Resolve (plus its own Confirm/Cancel Resolve pair). All controls are plain `Button`/`TextBox`/
`ComboBox`/`CalendarDatePicker`/`CheckBox`/`ListBox` — every one keyboard-reachable with FluentTheme's
default focus ring, and status (lifecycle, Important) is always presented as text, never color-only.
Verified by `OperationalNotesViewTests.OperationalNotesView_ExposesCriticalAutomationNames`.

## Desktop density (Part AE)

Designed primarily for 1280×860 (list + detail/editor simultaneously visible, minimal page-level
scroll); reflows acceptably to 960×640 via the same Grid proportions with per-column `ScrollViewer`
scrolling rather than clipped or unreadable fields.

## Visual validation (Part AM)

**PARTIAL.** This environment has no raster/Skia renderer wired into the headless Avalonia test host
(only `Avalonia.Headless`'s null-render backend), so pixel-level rendering cannot be captured or
compared here — consistent with the spec's own instruction not to claim a pixel PASS without one.
Structural/binding validation is thorough: every representative state (empty list, multiple Active
notes, an Important note selected, Upcoming/Expired/Resolved detail, the create editor, the edit
editor, a validation error, a very long title/body) constructs cleanly through Avalonia's real XAML
loader in `OperationalNotesViewTests`, proving every binding, converter, and `DataTemplate` path is
valid. Actual on-screen appearance should be spot-checked in a Windows 11 VM per `CLAUDE.md`'s platform
guidance before this ships.

## Schema/snapshot-schema confirmation (Part AL)

No new migration was added. Database schema remains **10** (`Migration0010CreateOperationalNotesTable`,
unchanged since 11A); snapshot schema remains **1**. This phase is UI-only on top of 11A's existing
persistence.

## Deferred work

- 11C: Dashboard active-note integration.
- 11D: explicit user-selected inclusion into an Associate Passdown draft — **persistent notes are still
  never automatically included in any daily passdown**; no "Include in today's passdown" affordance
  exists anywhere in this phase.
- 11E: category/priority filtering, carry-forward, further UX polish.
- 11F: final audit and release rebuild (the release ZIP was intentionally not rebuilt by this phase).
- A Reopen operation for a resolved note, Alerts, an Operations Calendar, and the Manager workflow all
  remain out of scope, as established in 11A/`CLAUDE.md`.

## Tests added (Phase 11B)

- `Carowinds.Passdown.Ui.Tests/OperationalNotesViewModelTests.cs` — 25 cases covering load/default
  filter/filter switching, list ordering, selection, New Note/Edit/Save/Cancel for both create and
  edit, both client-side validation messages, resolve + resolved-note read-only state, expired-note
  edit/extend, upcoming-note edit, local-only reload, both empty states, Important/category/date-range
  presentation, and the dirty-editor command-disabling guard.
- `Carowinds.Passdown.Ui.Tests/OperationalNotesServiceIntegrationTests.cs` — the view model driven
  against the *real* `OperationalNotesService` plus an in-memory `IOperationalNoteRepository` (create/
  edit/resolve persistence, reload visibility, date-driven lifecycle, expired-stays-Active, filtering
  never mutates, and a constructor-reflection no-network-dependency check).
- `Carowinds.Passdown.Ui.Tests/OperationalNotesViewTests.cs` — headless XAML-loader construction for
  the empty state, a populated list, the create/edit editors, resolved/expired detail, long content,
  and the full automation-name assertion.
- `Carowinds.Passdown.Ui.Tests/MainViewModelNavigationTests.cs` — extended with the four-value
  `AppSection` assertion, Dashboard default retained, every pairwise navigation through Operational
  Notes, Associate-date/History-selection preservation across it, the Associate-draft-separation
  regression, the `AssociateViewModel` constructor-reflection guarantee, and the dirty-editor
  top-level-navigation guard.
- `Carowinds.Passdown.Ui.Tests/CompositionRootTests.cs` — added
  `BuildServiceProvider_ResolvesOperationalNotesViewModel`.
- `Carowinds.Passdown.Ui.Tests/TestSupport/FakeOperationalNotesService.cs` and
  `InMemoryOperationalNoteRepository.cs` — new test doubles for the above.

## Full validation (Phase 11B)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — 1103/1103 passed (66 Domain +
297 Application + 82 Documents + 317 Infrastructure + 341 Ui, up from 11A's 1054 baseline by the 49
tests listed above), 0 failed, 0 skipped. No live internet was used or required.

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
