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

---

# Phase 11C — Dashboard integration for persistent Operational Notes

Phase 11C surfaces persistent Operational Notes (11A/11B) on the Operations Dashboard: a compact,
read-only "Active Operational Notes" panel answering "are there any persistent operational notes I
need to know about today?" This is strictly a **read-only summary integration** — no selection UI, no
"Include in today's passdown" affordance, and no code path that copies a persistent note's content
into the Associate draft. That remains Phase 11D's explicit, user-driven work.

## Two distinct note concepts, kept visually and architecturally separate (Part A/R/AF)

The Dashboard already had an "Operational Notes" preview sourced from today's Associate draft
(`DashboardNotesSummary`, Phase 10A). Phase 11C adds a second, unrelated concept —
`DashboardOperationalNotesSummary`, built from the persistent `operational_notes` table — and
deliberately never merges the two into one list or one heading. The existing panel was renamed from
"Operational Notes" to **"Passdown Notes"** specifically because the new panel needed the "Operational
Notes" name for the persistent concept (**"Active Operational Notes"**); nothing about
`AssociateDraftPayload.OperationalNotes` itself was renamed, only the Dashboard's own display heading
and its `AutomationProperties.Name` (`DashboardView.axaml`).

## Read model (Part B, `Application/Dashboard/OperationsDashboardSnapshot.cs`)

```csharp
public sealed record DashboardOperationalNoteItem(
    Guid Id, string Title, OperationalNoteCategory Category, OperationalNotePriority Priority,
    DateOnly EffectiveFrom, DateOnly? EffectiveThrough);

public sealed record DashboardOperationalNotesSummary(
    int Count, int ImportantCount, IReadOnlyList<DashboardOperationalNoteItem> Items, bool LoadFailed)
{
    public const int MaxPreviewCount = 3;
    public static DashboardOperationalNotesSummary Empty { get; } = new(0, 0, [], false);
}
```

`OperationsDashboardSnapshot` gained one new field, `ActiveOperationalNotes`. Deliberately no `Body`
(Part I — a dense summary surface, not the full detail Operational Notes Manager already shows) and
no pre-formatted display strings (Part B/K/L — the Desktop presentation layer owns all wording, same
separation every other snapshot field already follows). `Items` is a plain Application-layer type
(enums/`DateOnly`/`string`/`Guid` only) — no Avalonia dependency.

## Service dependency (Part C, `Application/Dashboard/OperationsDashboardService.cs`)

`OperationsDashboardService` gained one new constructor dependency, `IOperationalNotesService`
(already registered — no new DI registration was needed). `BuildActiveOperationalNotesAsync` makes
**exactly one** call to the existing `IOperationalNotesService.ListRelevantForDateAsync(operatingDate)`
per snapshot build (Part AK — no N+1 per rendered row, proven by
`OperationsDashboardServiceTests.LoadAsync_QueriesOperationalNotesExactlyOncePerLoad` wrapping the
real service and counting calls) and does not re-implement relevance or ordering — both come from
11A's `OperationalNoteRelevance`/`OperationalNoteOrdering.ByRelevance` transitively through that one
call (Part C/F).

## Eastern Today semantics (Part D)

Relevance is evaluated against the same `operatingDate` `BuildSnapshotAsync` already uses for every
other field — Dashboard's own Eastern "today" via `GetTodayAsync()` → `TodayEastern()`, never
`AssociateViewModel.SelectedDate`. `OperationsDashboardService` has no dependency capable of reading
Associate's selected date in the first place.
`OperationsDashboardServiceTests.GetTodayAsync_RelevantNote_UsesEasternTodayForOperationalNoteRelevance`
seeds a note effective for the Eastern calendar date near a UTC day boundary and asserts it appears,
mirroring the same late-UTC-clock technique Phase 10A's own Eastern-today test uses.

## Relevance and ordering (Part E/F)

Both are reused, never reimplemented: `IOperationalNotesService.ListRelevantForDateAsync` already
applies `OperationalNoteRelevance.IsRelevantForDate` (Status Active; `EffectiveFrom <= today`;
`EffectiveThrough is null or >= today`) and `OperationalNoteOrdering.ByRelevance` (Important first,
then `EffectiveFrom` ascending, then `CreatedAtUtc`, then `Id`). Upcoming, Expired, and Resolved notes
never appear — covered by
`LoadAsync_UpcomingNote_NotIncluded`/`LoadAsync_NoteEndingYesterday_NotIncluded`/`LoadAsync_ResolvedNote_NotIncluded`,
plus boundary cases (`LoadAsync_NoteEndingToday_Included`, a note starting today, a `null`
`EffectiveThrough`) and the Important-sorts-first case
(`LoadAsync_ImportantNoteSortsBeforeNormal_UsingSharedRelevanceOrdering`).

## Preview limit and overflow (Part H)

`DashboardOperationalNotesSummary.MaxPreviewCount = 3` — the Dashboard shows at most the first 3
relevant notes (in relevance order), while `Count` always carries the true total.
`DashboardOperationalNotesPresentation.Build` computes `HasOverflow`/`OverflowText` (`"+ N more active
notes"`) from `Count - Items.Count`, so a supervisor is never left thinking there are only 3 relevant
notes when more exist (`LoadAsync_MoreRelevantNotesThanPreviewLimit_BoundsItemsButKeepsTrueCount`,
`DashboardView_TenPlusActiveOperationalNotes_ShowsBoundedPreviewWithOverflow`).

## Body-preview decision (Part I)

**Title only**, plus category/priority/date-range metadata — no `Body` field on
`DashboardOperationalNoteItem` at all. This was the preferred option in the spec and was implemented
as-is: body content can be long, Operational Notes Manager already shows full detail, and the
Dashboard's density goal (Part AG) is better served by more rows of title-only content than by fewer
rows with wrapped body text.

## Important-priority presentation (Part J)

Important sorts first (via the shared ordering above) and is communicated with the word "Important"
as a text badge (`TextBlock.priorityText.important`, reusing the exact style class/brush
`OperationalNotesView` already established in 11B) — never red/alarm styling. A Normal-priority row
shows no priority text at all in the compact visual layout (matching the spec's own wireframe
example), but `DashboardOperationalNoteItemPresentation.AccessibleSummary` still names every note's
priority/category/title/date-range in text for automation (Part AH).

## Category/date-range presentation reuse (Part K/L)

`DashboardOperationalNoteItemPresentation.Build` reuses `OperationalNoteCategoryOption.For`/
`OperationalNotePriorityOption.For` (11B's centralized enum-to-label mapping) directly — no second
mapping. Date-range text reuses `OperationalNoteDateRangeFormatter`, extended with one new overload
taking primitive `EffectiveFrom`/`EffectiveThrough` fields (the original overload now delegates to it)
so the Dashboard's lightweight `DashboardOperationalNoteItem` — which carries no full `OperationalNote`
— can call the exact same formatter Operational Notes Manager uses, rather than a second, competing
date formatter.

## Empty and error states (Part M/AI)

`DashboardOperationalNotesPresentation.ShowEmptyState` is true only when the query succeeded and
returned nothing relevant ("No active operational notes for today."); `LoadFailed` is a distinct state
("Unable to load operational notes.") and the two are mutually exclusive by construction. A failure
inside `IOperationalNotesService.ListRelevantForDateAsync` is caught in
`OperationsDashboardService.BuildActiveOperationalNotesAsync` alone — one targeted try/catch, not a
general partial-result framework — so the rest of the Dashboard snapshot (park status, passdown
status, Data Status, etc.) still loads normally even if the local notes query throws
(`LoadAsync_OperationalNotesQueryFails_DegradesOnlyThatSectionWithoutFailingSnapshot`).

## Navigation: Open Operational Notes (Part O/P/Q)

`OperationsDashboardViewModel` gained `OpenOperationalNotesRequested` (event) and
`OpenOperationalNotesCommand`, mirroring `OpenHistoryCommand`'s shape exactly.
`MainViewModel` — the sole owner of top-level navigation — wires it to the existing
`ShowOperationalNotesSection()`, the same command the top-level nav bar's own "Operational Notes"
button already uses. This is deliberately **generic navigation**, not a "View Today's Active Notes"
deep link that would force `OperationalNotesViewModel.LifecycleFilter` to `Active` — that variant was
evaluated (Part Q) and deferred: forcing the filter would silently discard whatever filter/selection a
supervisor already had open. `OperationalNotesViewModel` is a singleton kept alive for the app's
lifetime (unchanged since 11B), so its filter/selection state survives the round trip automatically —
no new state-preservation code was needed, only the navigation wiring itself
(`DashboardOpenOperationalNotesCommand_PreservesExistingLifecycleFilter`).

## Reload / navigation-refresh behavior (Part U/V/W/X/Y/Z/AA)

`OperationsDashboardViewModel.ReloadCommand` is unchanged mechanically (still calls
`IOperationsDashboardService.GetTodayAsync()`), but because `BuildSnapshotAsync` now also recomputes
`ActiveOperationalNotes` from the same local query, pressing **Reload** naturally picks up anything
changed in Operational Notes Manager since the last load — a newly created relevant note, a resolved
note dropping out, or an expired note extended back into relevance — with no polling or event-bus
infrastructure added, the same pattern Phase 10C already established for "Reload picks up newly
finalized metadata." No automatic/event-driven refresh was added; a supervisor revisits Dashboard or
presses Reload to see a change made elsewhere, exactly like every other Phase 10 surface.

## No automatic passdown inclusion (Part S) — the critical regression guarantee

Loading or reloading the Dashboard, with any number of relevant persistent notes present, never reads
`AssociateDraftPayload.OperationalNotes`, never calls `IAssociatePassdownService.SaveAsync`, and never
touches a finalized snapshot or PDF.
`LoadAsync_PersistentNotesPresent_NeverMutatesAssociateDraftNotes` seeds both a saved Associate draft
notes list and a persistent note whose title could plausibly collide, then asserts the draft's own
preview lines are completely unaffected. There is no "Include"/"Add to Passdown"/"Use in Passdown"/
"Copy to Notes" button anywhere in this phase's UI (Part T) — the only action on a persistent note from
the Dashboard is **Open Operational Notes**, which only navigates.

## Data Status and History independence (Part AB/AC)

`DashboardDataHealthSummary`/`DashboardOverallDataStatus` (Phase 10D) are entirely unaffected —
`OperationsDashboardService.BuildDataHealth` was not touched, and persistent notes are never counted
as a retrieval source, given a confidence/freshness classification, or included in
`DashboardSourceStatusEntry`. `LatestFinalized`/History integration (Phase 10C) is likewise untouched —
no persistent-note field was added anywhere near `FinalizedPassdownSummary` or the finalization path.

## Local-only guarantee (Part AJ)

`IOperationalNotesService`/`SqliteOperationalNoteRepository` already had no HTTP/Playwright/browser
dependency (11A); `OperationsDashboardService`'s own no-refresh-dependency reflection test
(`Constructor_HasNoRefreshOrchestratorOrRetrievalDependency`) still passes unchanged with the new
constructor parameter added, since `IOperationalNotesService` itself carries no such dependency either.

## Performance (Part AK)

One `ListRelevantForDateAsync` call per Dashboard load, already bounded server-side by
`SqliteOperationalNoteRepository.ListRelevantForDateAsync`'s own status/date-range SQL filter (11A) —
never one query per rendered preview row. Verified directly
(`LoadAsync_QueriesOperationalNotesExactlyOncePerLoad`), not just implied by code inspection.

## Privacy (Part AD)

Note titles are intentionally shown in the Dashboard UI (an intended operational surface, same as
Operational Notes Manager) — but nothing about this phase touches
`SqliteDatabaseDiagnosticsProvider` or any log call; no title/body was added to any diagnostic or log
message anywhere in `OperationsDashboardService`/`OperationsDashboardViewModel`.

## Accessibility (Part AH)

`AutomationProperties.Name` values added/changed: `"Passdown Notes"` (renamed from `"Operational
Notes"`), `"Active operational notes"` (the new panel), `"Open operational notes"` (the new
navigation button). Each preview row carries a composed `AutomationProperties.Name` from
`DashboardOperationalNoteItemPresentation.AccessibleSummary` (e.g. `"Important. Equipment. South Gate
scanner #3 out of service. Aug 20 – Until resolved."`) so priority/category/title/date-range are always
available in text regardless of the compact visual layout that only shows "Important" for Important
notes. No new focusable control besides the one navigation button; the preview rows themselves are
plain `TextBlock`s inside a non-interactive `ItemsControl`, not tab stops.

## Desktop density / layout (Part AE/AG)

The Dashboard's prior `[Operational Notes | Latest Finalized]` two-column row was restructured to
`[Passdown Notes | Active Operational Notes]` on one row, with **Latest Finalized** moved to its own
full-width row directly below — the arrangement the spec itself suggested, chosen because it avoids
squeezing three panels into one row and keeps each preview row compact (category + optional
"Important" badge, title, date-range text — three short lines, not a card). At 1280×860 this adds one
row's worth of height versus Phase 10's layout; the page's existing `ScrollViewer` (unchanged) absorbs
that at 960×640.

## Schema (Part AO)

No migration was added. Database schema remains **10**; snapshot schema remains **1** — this
integration is entirely read-only against 11A's already-existing table.

## Visual validation (Part AN) — PARTIAL

Same constraint as every prior Phase 9/10/11 document: no pixel/bitmap screenshot renderer exists in
this codebase. Every representative state from the spec's list was exercised through Avalonia's real
XAML loader in `DashboardViewTests` instead: no active notes (empty state), one Important note, three
active notes (no overflow), 10+ active notes (bounded preview + overflow text), a 500-character title,
both note concepts populated together, a load-failure state, and a Data Status warning combined with
active notes. Recorded honestly as **PARTIAL**, matching every prior phase's precedent — actual
on-screen appearance should be spot-checked in a Windows 11 VM per `CLAUDE.md`'s platform guidance
before this ships.

## Deferred work (11D–11F, and evaluated-and-rejected items)

- **Phase 11D** — explicit, per-note, user-selected inclusion into an Associate Passdown draft. No
  code path toward this exists yet; `DashboardOperationalNoteItem`/`DashboardOperationalNotesSummary`
  carry no "included"/"selected" state of any kind.
- **Phase 11E** — category/priority filtering, carry-forward, further Operational Notes Manager UX
  polish.
- **Phase 11F** — final Phase 11 audit and release rebuild (the release ZIP was intentionally not
  rebuilt by this phase).
- A **"View Today's Active Notes" deep link** that would force `OperationalNotesViewModel.LifecycleFilter`
  to `Active` (Part Q) — evaluated and deferred in favor of generic "Open Operational Notes" navigation,
  which preserves whatever filter/selection state a supervisor already had.
- Alerts, an Operations Calendar, and the Manager workflow remain out of scope, unchanged from 11A/11B/
  `CLAUDE.md`.

## Tests added (Phase 11C)

- `Carowinds.Passdown.Application.Tests/Dashboard/OperationsDashboardServiceTests.cs` — 12 new cases:
  a relevant Active note appears; a note ending today is included, a note ending yesterday is not; an
  Upcoming note is excluded; a Resolved note is excluded even when its date range covers today;
  Important sorts before Normal via the shared ordering; the preview is bounded to `MaxPreviewCount`
  while `Count` keeps the true total; zero relevant notes reports an empty (non-failed) summary;
  Eastern-today resolution (independent of Associate's own date, which this service cannot read
  anyway); persistent notes present never mutate the Associate draft's own notes; a query failure
  degrades only the operational-notes section without failing the rest of the snapshot; and the
  relevance query runs exactly once per load.
- `Carowinds.Passdown.Ui.Tests/DashboardOperationalNotesPresentationTests.cs` (new file) — 10 cases:
  empty state, load-failed state (distinct from empty), centralized category/priority label reuse,
  null/non-null `EffectiveThrough` date-range text, overflow computation (present and absent),
  accessible-summary composition for Normal vs. Important notes, and an unbounded (presentation-layer)
  long title.
- `Carowinds.Passdown.Ui.Tests/DashboardViewTests.cs` — 9 new cases: no active notes, one Important
  note, three active notes (no overflow), 10+ notes (bounded preview + overflow), a long title, both
  note concepts populated together, a load-failure state, a Data Status warning combined with active
  notes, and the `OpenOperationalNotesCommand` navigation-event test; the existing automation-name
  assertion was updated for the `"Passdown Notes"`/`"Active operational notes"`/`"Open operational
  notes"` names.
- `Carowinds.Passdown.Ui.Tests/MainViewModelNavigationTests.cs` — 2 new cases: Dashboard's "Open
  Operational Notes" navigates to the Operational Notes section, and does so without disturbing an
  already-selected `LifecycleFilter`.
- `Carowinds.Passdown.Ui.Tests/TestSupport/FakeOperationsDashboardService.cs` — `DefaultSnapshot` now
  populates `ActiveOperationalNotes: DashboardOperationalNotesSummary.Empty`.

33 new tests total (1103 Phase 11B baseline + 33 = 1136: 12 Application + 21 Ui).

## Full validation (Phase 11C)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1136/1136** passed (66 Domain +
309 Application + 82 Documents + 317 Infrastructure + 362 Ui), run twice to confirm no flakiness, 0
failed, 0 skipped. No live internet was used or required.

---

# Phase 11D — Explicit inclusion of persistent Operational Notes into the Associate Passdown

Phase 11D is the bridge 11A's own documentation named up front: the one, explicit, user-driven path
from a persistent Operational Note (11A–11C) into one specific day's Associate Passdown. Every rule
11A–11C already established stays true — **persistent notes are never automatically included in any
daily passdown.** Selecting a note is a deliberate supervisor action taken from Associate → Notes,
never a byproduct of viewing the Dashboard or the Operational Notes Manager.

```
persistent note
    |
    v
relevant/available suggestion for the Associate-selected date
    |
    v
supervisor clicks "Add"                          <- Phase 11D (this document)
    |
    v
frozen copy becomes part of that day's Associate draft
    |
    v
manual daily notes + frozen copies together in the PDF/finalized snapshot
```

## Inclusion model — frozen per-draft copy, not a live reference (Part A)

`AssociateOperationalNoteSelection` (`Application/Associate/AssociateDraftPayload.cs`):

```csharp
public sealed record AssociateOperationalNoteSelection(
    Guid SourceOperationalNoteId,
    string Title,
    string? Body,
    OperationalNoteCategory Category,
    OperationalNotePriority Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    DateTimeOffset SelectedAtUtc);
```

`AssociateDraftPayload` gained one new field, `SelectedOperationalNotes: IReadOnlyList<AssociateOperationalNoteSelection>`.
`SourceOperationalNoteId` is retained only as source identity for duplicate-selection prevention (Part
H) — nothing in the Associate draft, PDF, or finalized snapshot ever re-reads the live
`operational_notes` row through it. Every other field is a plain copied value, frozen at the moment of
selection (Part B).

## Selection-time freeze and no silent resync (Parts B/C)

`AssociatePassdownService.SelectOperationalNoteAsync` (`Application/Associate/AssociatePassdownService.cs`)
is the one place a persistent note's content is ever copied: it re-reads the current
`OperationalNote` via `IOperationalNotesService.ListRelevantForDateAsync`, builds a new
`AssociateOperationalNoteSelection` from its current fields, and saves it into that date's draft. There
is no background sync, no scheduled refresh of an already-selected copy, and no code path that
re-derives a selection from its `SourceOperationalNoteId` after the fact. Editing, resolving, or
re-dating the source note afterward leaves every already-selected copy completely untouched — proved
directly by `AssociatePassdownServiceTests.SourceEditAfterSelection_DoesNotMutateSelectedCopy`/
`SourceResolveAfterSelection_DoesNotRemoveSelectedCopy`/`SourceDateChangeAfterSelection_DoesNotRemoveSelectedCopy`.

## Date-specific selections (Part D/Z)

Selections live entirely inside `AssociateDraftPayload.SelectedOperationalNotes` — a field of the
already per-`PassdownDate` `associate_drafts` row. There is no `OperationalNote.IsIncluded` or any
other global inclusion flag on the persistent-note table; a note selected for Aug 26 has no relationship
at all to whether it is selected for Aug 27. `AssociateViewModel` reloads both
`SelectedOperationalNotes` and `AvailableOperationalNoteSuggestions` on every date switch
(`LoadDateAsync` → `RefreshOperationalNoteSuggestionsAsync`), so switching dates can never leak one
date's selections into another's screen state — regression-tested end to end in
`AssociateViewModelOperationalNoteSelectionTests.SwitchingDate_ShowsOnlyThatDatesSelectionsAndSuggestions`.

## Relevant suggestions use the Associate-selected date, not Eastern "today" (Part E/F)

`IAssociatePassdownService.GetRelevantOperationalNoteSuggestionsAsync(operatingDate)` is a thin
pass-through to `IOperationalNotesService.ListRelevantForDateAsync(operatingDate)` — the same
relevance rule (`OperationalNoteRelevance.IsRelevantForDate`) and ordering (`OperationalNoteOrdering.ByRelevance`)
11A/11C already established, called with whatever date `AssociateViewModel.SelectedDate` currently is.
This is deliberately **not** Dashboard's `TodayEastern()` — `AssociatePassdownService` has no way to
read that value in the first place, and `GetRelevantOperationalNoteSuggestionsAsync_UsesAssociateSelectedDate_NotEasternToday`
proves a note relevant only 30 days out is offered when that future date is selected. Only Active,
in-range notes are ever offered as suggestions; Upcoming/Expired/Resolved notes never appear (inherited
directly from `OperationalNoteRelevance`/`ListRelevantForDateAsync`, not re-implemented).

## Duplicate prevention and idempotent selection (Part H)

`SelectOperationalNoteAsync` checks `draft.SelectedOperationalNotes.Any(s => s.SourceOperationalNoteId == operationalNoteId)`
before ever building a new frozen copy; a second `Select(noteId)` while already selected is a pure
no-op — one copy, never two (`SelectOperationalNoteAsync_CalledTwice_IsIdempotent`).

## Reselect after deselect (Part I)

Deselecting removes the frozen copy entirely (no tombstone). A later reselect of the same source note
is treated as a brand-new explicit decision: it copies whatever the source note says *at that later
moment*, not the earlier frozen text (`Reselect_AfterDeselect_CopiesCurrentSourceContent_NotTheOldFrozenCopy`).
This is deliberate, not an oversight — the whole point of an explicit re-selection is that the
supervisor is choosing the note's current content again.

## Validation on selection (Part O/P)

`SelectOperationalNoteAsync` throws `InvalidOperationException` when the id is unknown or not
currently relevant for the requested date — a defensive check against a stale/invalid id, since the
Associate → Notes UI only ever offers ids drawn from `GetRelevantOperationalNoteSuggestionsAsync`, not
a normal user-facing validation path. Selecting/deselecting never calls `IOperationalNotesService.UpdateAsync`/
`ResolveAsync`, never touches the source note's `UpdatedAtUtc`, and adds no inclusion state to it —
`SelectOperationalNoteAsync_NeverMutatesSourceNote` reloads the source note after a select+deselect
round trip and asserts its `UpdatedAtUtc`/`Status`/`ResolvedAtUtc` are byte-for-byte unchanged.

## Persistence strategy (Part J) and schema decision (Part K)

**Nested JSON inside the existing `AssociateDraft.PayloadJson`** — no new table, no new migration.
`AssociateDraftPayload` (like every other Associate draft field) is opaque JSON to
`SqliteAssociateDraftRepository`; adding `SelectedOperationalNotes` is exactly the same shape of change
Phase 8A's `PassdownSectionSelection` already made to this same payload. A dedicated child table keyed
by draft/date was considered and rejected: it would require a join wherever a draft is composed, add a
second write inside `SaveDraftAsync`'s single `INSERT ... ON CONFLICT`, and buy nothing this payload
doesn't already give for free (autosave already persists the whole payload atomically per date).

`AssociateDraftPayload.CurrentSchemaVersion` bumped **2 → 3** (Phase 8A's rename/section-selection bump
was 1 → 2). **Database schema (`PRAGMA user_version`) stays at 10** — no `Migration0011` was added,
because `associate_drafts.payload_json`/`schema_version` already exist and already tolerate an evolving
payload shape; the column types didn't change. `AssociatePassdownService.DeserializeDraft` backfills a
missing `SelectedOperationalNotes` property (absent in every pre-11D draft's JSON) to `[]`, mirroring
the exact pattern already used for `SectionSelection`/`DailyFeatureType` — proved by
`LegacyDraft_WithoutSelectedOperationalNotesProperty_DefaultsToEmpty` and the migration/backward-compat
tests below.

## Finalized snapshot schema decision (Part L) — stays at 1

`FinalizedPassdownSnapshot.OperationalNotes` was already a plain `IReadOnlyList<string>` — the exact
already-resolved display lines the PDF's Operational Notes panel renders (Phase 9's own "frozen
display values, not live references" design). `AssociatePassdownDocumentModelBuilder.BuildOperationalNotes`
now populates that same list with the manual daily notes first, then one formatted line per selected
persistent note (`"Persistent: <title> — <body>"`, or `"Persistent (Important): <title>"` for an
Important-priority selection with no body) — still a flat list of already-resolved strings, the exact
shape `FinalizedPassdownSnapshotBuilder.MapSnapshot` already copied unchanged. No new document-model or
snapshot field was needed, so `FinalizedPassdownSnapshot.CurrentSchemaVersion` **stays at 1** —
confirmed directly by `FinalizedPassdownSnapshotBuilderTests.Build_SelectedPersistentNote_FreezesIntoOperationalNotesList`.
The tradeoff, made deliberately: History's flat bulleted list does not carry a separate "Daily
Notes:"/"Included Operational Notes:" visual sub-heading the way the spec's own preferred PDF wireframe
showed — the `"Persistent"`/`"Persistent (Important)"` text prefix is the distinction that survives into
both the PDF and History, which the spec's own fallback text ("combine under one Operational Notes area
but preserve distinction visually") explicitly sanctions.

## Associate-layer service API (Part N)

```csharp
Task<IReadOnlyList<OperationalNote>> GetRelevantOperationalNoteSuggestionsAsync(DateOnly operatingDate, CancellationToken ct = default);
Task<AssociateWorkspace> SelectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken ct = default);
Task<AssociateWorkspace> DeselectOperationalNoteAsync(DateOnly operatingDate, Guid operationalNoteId, CancellationToken ct = default);
```

Added to `IAssociatePassdownService`/`AssociatePassdownService`, which gained one new constructor
dependency, `IOperationalNotesService` — the one, explicit, intentional bridge between the two Phase 11
subsystems. `AssociateViewModel`'s own constructor is **unchanged**: it still depends only on
`IAssociatePassdownService`/`IClock`/etc., with no `IOperationalNotesService` reference of its own,
preserving Phase 11B's `AssociateViewModel_HasNoOperationalNotesServiceDependency` regression test
exactly as written — every persistent-note operation the UI needs is reached through
`IAssociatePassdownService`, never a second dependency injected into the ViewModel. `Select`/`DeselectOperationalNoteAsync`
each perform a plain read-modify-write against the saved draft record (read → mutate `SelectedOperationalNotes`
→ `SaveDraftAsync` → recompose) rather than requiring the UI to copy fields or touch the repository
directly.

## Autosave integration (Part Q/R/AT/AU)

Selection does not ride the existing 800ms debounce timer — `AddOperationalNoteAsync`/`RemoveOperationalNoteAsync`
(`AssociateViewModel`) first call `FlushPendingAutosaveAsync()` (the same synchronous drain
`CheckOnlineAsync`/`CreatePdfAsync` already use before their own async work) so the Application-layer
read-modify-write always operates against exactly what is on screen, then call
`SelectOperationalNoteAsync`/`DeselectOperationalNoteAsync` directly — an immediate save, not a second
save-status system. The returned `AssociateWorkspace` is applied back through the same
`ApplyWorkspaceToFields` every load/Check-Online/save already uses, so `SaveStatusText`/`AutosaveState`
behave identically to any other draft change: selecting/deselecting a note is a real save, reflected in
the footer immediately. A stale in-flight select/deselect is guarded against a concurrent date switch
(`if (date != SelectedDate) return;`), mirroring the existing `_loadVersion` guard pattern.

## Associate Notes UI (Part S/T/U/V)

Added entirely inside **Associate Passdown → Notes** (`AssociateView.axaml`), never Dashboard and never
a new top-level screen. Three structurally distinct panels, top to bottom:

1. **Daily Passdown Notes** — the existing multiline manual-notes editor, renamed from "Operational
   Notes" to make room for the new persistent-note panels' own use of that name, with no behavior
   change (still `OperationalNotesText` → `AssociateDraftPayload.OperationalNotes`).
2. **Included Persistent Notes** — `SelectedOperationalNotes`, each row showing category/priority
   chips, title, an optional body preview, date-range text, and a **Remove** button. Empty state: "No
   persistent operational notes included."
3. **"Available for \<date\>"** — `AvailableOperationalNoteSuggestions` (already-selected sources
   excluded), each row showing category/priority chips, title, date-range text, and an **Add** button
   (no checkbox — selection autosaves immediately and has its own dedicated section, so a clear button
   action reads better than a checkbox that would imply toggling in place). Empty state: "No persistent
   operational notes are available for this date."

Manual notes and persistent-note selections are structurally independent view-model collections
(`OperationalNotesText` vs. `SelectedOperationalNotes`/`AvailableOperationalNoteSuggestions`); selecting
a persistent note never appends into the manual textbox and deselecting never removes manual text
(`AddAndRemove_NeverChangeManualOperationalNotesText`).

## Ordering decision (Part AP/AQ)

Selected notes are shown in **selection order** — the order a supervisor added them — because that
reflects their actual intent for the passdown, not a re-sort by relevance/priority every time the list
renders. `SelectOperationalNoteAsync` appends to the end of `SelectedOperationalNotes`
(`[.. draft.SelectedOperationalNotes, selection]`); nothing reorders that list afterward.
`SelectedOperationalNotes_PreserveSelectionOrder` proves two selections come back in the order they were
added, not creation order or alphabetical order. Available suggestions keep the existing shared
relevance ordering (`OperationalNoteOrdering.ByRelevance` via `ListRelevantForDateAsync`), unrelated to
selection order. No drag/drop or Up/Down reordering was added — deferred, per spec, to 11E if ever
needed.

## Selection limit decision (Part AR)

No artificial cap. `SelectedOperationalNotes` has no `MaxCount`-style constant anywhere in the model or
service layer — the existing one-page PDF overflow policy (`AssociatePassdownPreflightService`/the
generator's own page-count check) is the only backstop, exactly as it already is for manual notes and
every product category.

## Source-changed / source-resolved indicators — deferred (Parts W/X)

Not implemented in 11D, by design. `AssociateOperationalNoteSelection` carries no `SourceUpdatedAtUtc`
and there is no code path comparing a selected copy's frozen content against the source note's current
state. A selected copy remains exactly as selected even after its source is edited or resolved — this
is the load-bearing guarantee this phase exists to prove, not an oversight to fix later. Any
"source changed since you selected this" indicator is explicitly deferred to 11E.

## PDF presentation (Part AB/AC)

`AssociatePassdownDocumentModelBuilder.BuildOperationalNotes` combines both sources into the one
existing `AssociatePassdownDocumentModel.OperationalNotes` list `AssociatePassdownPdfGenerator.ComposeNotes`
already rendered — manual notes first (unlabeled, exactly as before), then one bulleted line per
selected persistent note, prefixed `"Persistent: "` or `"Persistent (Important): "`. No source
Guid/timestamp/internal status ever reaches the page
(`SelectedOperationalNotes_NeverExposesSourceIdOrTimestamps`/`SelectedOperationalNote_NeverExposesSourceIdInPdf`).
Gated by the same `PassdownSectionSelection.IncludeOperationalNotes` flag as before: hiding the
Operational Notes section hides both manual and selected content together. The existing one-page
overflow policy is unchanged and untouched — 0/1/3/many selections, a long title/body, and manual +
selected notes together all still render within the strict one-page layout or fail preflight exactly as
before (`AssociatePassdownPdfGeneratorTests`' new Phase 11D cases).

## Finalization (Part AD/AF/AG)

`FinalizedPassdownFinalizationService.FinalizeAsync` is completely unchanged by this phase — it still
builds `AssociatePassdownDocumentModel` exactly once via `AssociatePassdownDocumentModelBuilder.Build`
(which now includes `BuildOperationalNotes`'s combined list) and derives both the snapshot and the
frozen PDF from that single instance. Finalization never queries `IOperationalNoteRepository`/
`IOperationalNotesService` at all — the source of truth at finalization time is exclusively the already
saved `AssociateDraft.SelectedOperationalNotes`, read once when the workspace was composed before
`FinalizeAsync` was called. If a source `OperationalNote` row were ever unavailable/corrupt after
selection, finalization would still succeed unaffected, since nothing in the finalization path ever
looks it up again.

## History immutability (Part AE/AI)

`HistoryView`/`PassdownHistoryViewModel`/`IPassdownHistoryService` are untouched — History renders
`SelectedDetail.Snapshot.OperationalNotes` exactly as it already did, which now simply includes any
selected-note lines that were present at finalization time, frozen. No live link back to a persistent
note exists anywhere in History; a post-finalization edit to the source note, or to the day's draft
selections, can never change an already-finalized row (proved end to end in
`Phase11DIntegrationTests.SelectedNoteContent_SurvivesSourceEdit_ThroughReload_Pdf_AndFinalization`).

## Dashboard and Operational Notes Manager independence (Parts AJ/AK)

Dashboard's "Active Operational Notes" panel (11C) is entirely unmodified — no Add/Include button, no
checkbox, no code path capable of reaching `AssociateDraftPayload.SelectedOperationalNotes`. The
Operational Notes Manager (11B) is likewise unmodified — no global "Included" badge was added to any
row, since a single source note can be selected for Aug 26, not Aug 27, and selected again Aug 28,
making any single global flag misleading by construction.

## Privacy (Part AN)

No selected note's title/body, and no frozen `SelectedOperationalNotes` JSON, ever appears in a log
call — `AddOperationalNoteAsync`/`RemoveOperationalNoteAsync` log only the operational-note id and
operating date on failure (`_logger?.LogError(ex, "... {OperationalNoteId} for {OperatingDate}.", ...)`),
matching every other Phase 11 diagnostic-privacy convention. `SqliteDatabaseDiagnosticsProvider` never
queried `associate_drafts` before this phase and still doesn't — it only ever reports `PRAGMA
user_version`, file size, and WAL presence (unchanged since Phase 7F), so a selected note's content is
structurally unreachable from a diagnostics export regardless of what `SelectedOperationalNotes`
contains.

## Accessibility (Part AS)

`AutomationProperties.Name` values added: `"Included persistent operational notes"` and `"Available
persistent operational notes"` (the two new section headings, always present regardless of content), a
composed `AccessibleSummary` per row (e.g. `"Add. Important. Equipment. South Gate scanner #3 out of
service. Aug 20 – Until resolved."` / `"Remove. Important. Equipment. South Gate scanner #3 out of
service."`) so priority/category/title/date-range are always available in text, and per-row
`StringFormat`-templated names on the Add/Remove buttons themselves (`"Add note {0}"`/`"Remove note
{0}"`). Priority is communicated as the word "Important" (reusing the same restrained accent/weight
convention 11B/11C already established — never color alone), and every control (`Button`s inside plain
`ItemsControl`s) is keyboard-reachable with the FluentTheme default focus ring.

## Visual validation (Part BA) — PARTIAL

Same honestly-recorded limitation as every prior Phase 9/10/11 document: no pixel/bitmap renderer
exists in this codebase. `AssociateViewAccessibilityTests.AssociateView_WithSelectedAndAvailablePersistentNotes_ConstructsCleanly`
constructs the real `AssociateView` through Avalonia's XAML loader with two selected notes (Important +
Normal, one with a 180-character title and a 3500-character body) and one available suggestion — every
binding/`DataTemplate` path is proved valid, but on-screen density/spacing was not pixel-verified.
Structural/binding coverage: no persistent notes available, relevant suggestions available, one
selected, several selected, Important + Normal, long title/body, manual notes + several selected, and a
date switch are all exercised through `AssociateViewModelOperationalNoteSelectionTests`/
`AssociateViewAccessibilityTests`, but actual on-screen appearance should still be spot-checked in a
Windows 11 VM per `CLAUDE.md`'s platform guidance before this ships.

## Migration / backward compatibility (Part AO)

A pre-11D draft's persisted JSON has no `SelectedOperationalNotes` property at all; the plain
`JsonSerializer.Deserialize<AssociateDraftPayload>` call leaves it `null`, and `DeserializeDraft`
backfills it to `[]` — exactly the same presence-based pattern already used for `SectionSelection`/
`DailyFeatureType`/`IncludeHauntedAttractions`. No migration fabricates a selection; existing finalized
History rows remain readable unchanged (their `FinalizedPassdownSnapshot.OperationalNotes` was already
just a flat string list, so a pre-11D row round-trips through the same deserializer with zero
persistent-note lines, exactly as it always did).

## Deferred to 11E/11F

- Source-changed / source-resolved indicators on an already-selected note (Parts W/X).
- Manual drag/drop or Up/Down reordering of selected notes (Part AQ).
- Category/priority filtering in Operational Notes Manager, carry-forward, and further UX polish
  (unchanged from 11B/11C's own deferred list).
- The final Phase 11 audit and release rebuild (Phase 11F) — the release ZIP was intentionally not
  rebuilt by this phase.
- Alerts, an Operations Calendar, and the Manager workflow remain out of scope, unchanged from
  11A/11B/11C/`CLAUDE.md`.

## Tests added (Phase 11D)

- `Carowinds.Passdown.Application.Tests/Associate/AssociatePassdownServiceTests.cs` — 17 new cases:
  relevant/non-relevant/nonexistent source selection, content-copy-on-select, idempotent duplicate
  select, deselect (including safe no-op on a never-selected note), reselect-copies-current-content,
  source-edit/-resolve/-date-change-after-selection independence, date-specific selections (including
  cross-date isolation), selection-order preservation, legacy-draft backward compatibility, source-note
  non-mutation, no-refresh-orchestrator-dependency, manual-notes independence, and
  Associate-selected-date (not Eastern today) relevance.
- `Carowinds.Passdown.Documents.Tests/Associate/AssociatePassdownDocumentModelBuilderTests.cs` — 5 new
  cases: manual+selected combination and ordering, Important-priority prefix, selected-only (no manual
  notes), section-visibility gating, and no source id/timestamp exposure.
- `Carowinds.Passdown.Documents.Tests/Associate/FinalizedPassdownSnapshotBuilderTests.cs` — 1 new case
  proving the frozen snapshot content and the unchanged `CurrentSchemaVersion == 1`.
- `Carowinds.Passdown.Documents.Tests/Associate/AssociatePassdownPdfGeneratorTests.cs` — 6 new cases:
  zero/one/many selected notes, manual+selected together, a removed selection disappearing from a
  regenerated PDF, and no source id in the rendered page — all still within the strict one-page policy.
- `Carowinds.Passdown.Ui.Tests/AssociateViewModelOperationalNoteSelectionTests.cs` (new file) — 9
  cases: suggestion loading, empty available/selected states, Add/Remove commands moving notes between
  sections and persisting, survival across a date switch, cross-date isolation, manual-notes
  independence, and non-empty accessible summaries.
- `Carowinds.Passdown.Ui.Tests/AssociateViewAccessibilityTests.cs` — 1 new populated-state construction
  test (Important + Normal, long title/body, one suggestion), plus extended assertions in the existing
  automation-name and `StringFormat`-markup tests for the two new section names and the Add/Remove
  patterns.
- `Carowinds.Passdown.Ui.Tests/ScheduledRefresh/Phase11DIntegrationTests.cs` (new file) — 1 case: the
  realistic end-to-end scenario against a real temp-file SQLite database and the real Application/
  Infrastructure/Documents pipeline (create note → select for a date → edit source → reload → still
  frozen → PDF still frozen → finalize → History still frozen → live source reflects the edit).
- `Carowinds.Passdown.Ui.Tests/TestSupport/FakeAssociatePassdownService.cs` — extended with a scriptable
  per-date suggestion list and real select/deselect semantics against its existing per-date draft store
  (no new test cases of its own — a shared test double used by the cases above).

40 new tests total (1136 Phase 11C baseline + 40 = 1176: 17 Application + 12 Documents + 11 Ui).

## Schema / version report

- **Database schema: 10** — unchanged from 11A/11B/11C. `associate_drafts` already stores an opaque,
  evolving JSON payload; no new table or column was needed for `SelectedOperationalNotes`.
- **`AssociateDraftPayload.CurrentSchemaVersion`: 2 → 3** — the payload's own JSON shape gained one new
  property; `DeserializeDraft` backfills it to `[]` for every older persisted draft.
- **`FinalizedPassdownSnapshot.CurrentSchemaVersion`: 1 — unchanged.** Selected persistent notes are
  represented as additional formatted strings in the existing generic `OperationalNotes` list, not a new
  structured field.

## Full validation (Phase 11D)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1176/1176** passed (66 Domain +
326 Application + 94 Documents + 317 Infrastructure + 373 Ui), run twice to confirm no flakiness, 0
failed, 0 skipped. No live internet was used or required.

---

# Phase 11E — Operational Notes filtering, carry-forward awareness, and source-change awareness

Phase 11E refines the Operational Notes Manager and Associate → Notes screens built in 11A–11D
without touching the model, the opt-in inclusion rule, or the selection-time freeze guarantee. Every
Phase 11A–11D regression guarantee stays true: persistent notes are still never automatically
included in a daily passdown, a selected copy is still frozen at selection time, and nothing short of
an explicit new action (this phase adds exactly one — Replace with Current Version) can ever change
already-selected content.

## Category filter (Part A)

`OperationalNotesViewModel.CategoryFilter` (`OperationalNoteCategoryFilterOption`,
`Desktop/Presentation/OperationalNoteCategoryFilterOption.cs`) adds an "All" entry plus one option per
`OperationalNoteCategoryOption.All` entry — reusing 11B's centralized category labels directly rather
than a second mapping or a parallel enum (the option's `Value` is a nullable `OperationalNoteCategory`,
`null` meaning "All"). It composes with `LifecycleFilter` in `ApplyFilter()`, filtering the same
already-lifecycle-filtered `IEnumerable<OperationalNote>` — no separate pass, no re-sort.

## Priority filter (Part B)

`OperationalNotesViewModel.PriorityFilter` (`OperationalNotePriorityFilterOption`) mirrors the category
filter's shape exactly — "All" plus `Normal`/`Important`, reusing `OperationalNotePriorityOption`'s
labels. No new "severity" terminology was introduced; priority remains exactly what 11A defined it as
(presentation/sort only, never a notification trigger).

## Combined filter behavior (Part C/V)

Lifecycle + Category + Priority compose predictably — each is a plain `Where` clause applied in
sequence inside `ApplyFilter()` over the cached `_allNotes` list, never a competing re-implementation
of relevance/lifecycle. Filter *state* (which tab/category/priority is selected) persists for the
lifetime of the app session automatically, because `OperationalNotesViewModel` is already a singleton
kept alive across navigation (established in 11B/11C) — no new state-preservation code was needed, and
no filter state was added to the database (Part C's "preferred: in-memory only" was implemented as-is).
Dashboard's own default-startup section is unaffected. The header row (`OperationalNotesView.axaml`)
keeps the existing lifecycle tabs and adds `Category [All ▾]` / `Priority [All ▾]` plus a **Clear
filters** button inside the same `WrapPanel`, so it wraps cleanly at 960×640 rather than growing a
second fixed row (Part V).

## Filter reset (Part D)

`ClearFiltersCommand` resets Category and Priority back to "All" without disturbing the lifecycle tab
— the lifecycle tabs are primary navigation ("what's active right now"), not a secondary narrowing
filter, so clearing filters intentionally leaves the current lifecycle tab alone.
`HasActiveSecondaryFilters` (`CategoryFilter.Value is not null || PriorityFilter.Value is not null`)
gates the button's visibility. The two empty states stay distinct exactly as 11B defined them:
`ShowEmptyState` ("No operational notes yet.") when nothing exists at all, vs.
`ShowFilteredEmptyState` when notes exist but the current Lifecycle+Category+Priority combination
hides all of them — `FilteredEmptyStateMessage` appends "Try clearing filters." only in the latter
case when a secondary filter is active, so the two states read differently even when both are empty.

## Carry-forward awareness (Parts E/F)

`OperationalNoteCarryForwardFormatter` (`Desktop/Presentation/OperationalNotePresentation.cs`) is pure
derived presentation, layered on the already-computed `OperationalNoteLifecycleState` — it never
re-derives lifecycle/relevance itself, never duplicates a daily record, and adds no recurrence concept.
It produces one short phrase per lifecycle state, relative when the date is very close:

```
Active, EffectiveThrough null      -> "Until resolved"
Active, expires today              -> "Expires today"
Active, expires tomorrow           -> "Expires tomorrow"
Active, expires further out        -> "Through <date>"
Upcoming, starts tomorrow          -> "Starts tomorrow"
Upcoming, starts further out       -> "Starts <date>"
Expired                            -> "Expired <date>"
Resolved                           -> (no carry-forward text; the lifecycle badge already says everything relevant)
```

`CarryForwardText` was added to `OperationalNoteRowPresentation` and `OperationalNoteDetailPresentation`
(Manager list/detail) and to `AssociateOperationalNoteSuggestionPresentation` (Associate → Notes'
"Available for `<date>`" rows, satisfying Part T's "date relevance" column) — the same formatter in all
three places, so wording can never drift between screens. It was deliberately **not** added to already-
selected/frozen notes' presentation (`AssociateSelectedOperationalNotePresentation`): a frozen selection
was Active at selection time by construction and never re-evaluates its own lifecycle against a later
reference date, so a "carry-forward" phrase computed against "today" would be misleading there.

## Source-change awareness (Parts G-L) and the schema decision (Parts M/N)

`OperationalNoteSourceComparisonState` (`Application/Associate/OperationalNoteSourceComparison.cs`) is a
new, purely informational enum — `Unchanged | ContentChanged | Resolved | NoLongerApplicable | Missing`
— computed fresh on every `AssociatePassdownService.ComposeAsync` call, never persisted, and never
capable of mutating a selection's frozen fields itself. **Schema decision (Part M): no new field was
added to `AssociateOperationalNoteSelection`, and `AssociateDraftPayload.CurrentSchemaVersion` stays at
3.** `OperationalNoteSourceComparison.Compute` compares the frozen selection's own already-existing
Title/Body/Category/Priority/EffectiveFrom/EffectiveThrough fields directly against the live source
note — a `SourceUpdatedAtUtcAtSelection` timestamp was considered and rejected as both unnecessary
(direct field comparison is strictly more accurate — a note's `UpdatedAtUtc` can advance without any
displayed field changing) and unneeded for backward compatibility, since every field the comparison
needs already existed on schema 3. Precedence when multiple conditions could apply: source not found at
all → `Missing`; else source `Resolved` → `Resolved` (even if its content also changed); else source
`Active` but its date range no longer covers the selection's operating date → `NoLongerApplicable`; else
a field-by-field content comparison → `ContentChanged` or `Unchanged`.

`AssociateWorkspace` gained one new field, `SelectedOperationalNoteSourceStates` (a nullable
`IReadOnlyDictionary<Guid, OperationalNoteSourceComparisonState>`, defaulting to `null`/empty so every
pre-11E construction site of the record — five of them across production and test code — kept compiling
unchanged). `AssociatePassdownService.ComposeAsync` computes it with exactly one
`IOperationalNotesService.ListAsync()` call per compose (never per row, and never at all when nothing is
selected), the same "one query per load" performance discipline 11C's Dashboard integration established.

## Frozen-copy explanation (Part H)

Associate → Notes' "Included Persistent Notes" panel now shows one plain-language line above the list
— **"Included notes keep the version selected for this passdown."** — deliberately avoiding "snapshot"/
"detached entity" wording, shown once for the section rather than repeated per row.

## Source-changed action: Replace with Current Version (Parts I/O/P/R)

`IAssociatePassdownService.ReplaceOperationalNoteWithCurrentAsync(operatingDate, operationalNoteId)` is
the one new, deliberate, explicit path a selection's frozen content can ever change through. It re-reads
the source note's current content (via `IOperationalNotesService.ListAsync`, not the relevance-filtered
suggestion list — a supervisor may want to pull in a Resolved or no-longer-date-applicable source's
current text too) and overwrites the selection **in place at its existing list index**, so the
selection's position in the list is preserved rather than moving it to the end (Part R — verified by
`ReplaceOperationalNoteWithCurrentAsync_PreservesSelectionOrder`). It is a safe no-op when the note is
not currently selected, and throws only if the source note cannot be found at all. The Associate → Notes
UI offers **Replace with Current Version** only when `ShowReplaceAction` is true, which is only when
`SourceState == ContentChanged` — "Keep Included Version" needs no explicit control since doing nothing
already keeps it, matching the spec's own three-option framing collapsed to the two that need a button.
Mirrors `AddOperationalNoteAsync`/`RemoveOperationalNoteAsync` exactly in `AssociateViewModel`
(flush-pending-autosave, call the service, guard against a concurrent date switch, apply the returned
workspace) — it is a real, immediate save like every other selection change, not a second save system.

## Resolved source behavior (Part J)

When a selected note's source becomes Resolved, nothing is removed and nothing is auto-deselected. The
row shows **"Source note is now resolved."** with no Replace action (replacing from a resolved source
was evaluated and rejected — "Replace" exists to pull in a *content* update, not to re-affirm a
resolved note's stale text). The frozen copy remains part of the draft until the supervisor clicks
Remove.

## Source no-longer-relevant behavior (Part K)

When a selected note's source is edited so its effective date range no longer covers the operating date
the selection was made for, the row shows **"Source note no longer applies to this date."** — again
informational only, no auto-removal, no Replace action (there is no "current" applicable content to pull
in for *this* date).

## Source-missing behavior (Part L)

If the source note cannot be found at all (defensive-only, since 11A/11B never added a hard delete), the
row shows **"Source note unavailable."** The frozen copy remains fully usable — Associate draft
round-trip, PDF generation, and finalization never re-read the source row at any point (this was already
true from 11D; Phase 11E's own regression test
`ReplaceOperationalNoteWithCurrentAsync_NeverMutatesSourceNoteInPlace` plus the Application-layer
`OperationalNoteSourceComparisonTests.Compute_SourceIsNull_ReturnsMissing` cover it directly for this
phase's own new code paths).

## Backward compatibility (Part N)

Automatic, not a special case: an older selection (created under 11D, before 11E existed) carries the
exact same Title/Body/Category/Priority/EffectiveFrom/EffectiveThrough fields a brand-new selection
does, so `OperationalNoteSourceComparison.Compute` treats it identically —
`OperationalNoteSourceComparisonTests.Compute_BehavesIdenticallyForAnyAgeOfSelection_NoSpecialCasingNeeded`
proves this directly. There is no "Unknown/not available" state in the final enum, because there was
never a reason to need one once the schema stayed at 3.

## Selected-note reordering — deferred (Parts Q/R)

Evaluated and **deferred by design**. Up/Down buttons were the spec's own preferred shape if
implemented, but selection order already reflects a supervisor's actual intent (the order notes were
added, per 11D Part AP/AQ) and no concrete usability problem with that ordering surfaced during this
phase's review. Adding Up/Down now would be speculative UI for a need that has not been demonstrated;
revisit only if a future phase identifies a real ordering complaint. Selection order remains exactly
what 11D established (append-on-select, in-place update on Replace) and is unaffected by this decision.

## Search — deferred (Part W)

Evaluated and **deferred by design**, per the spec's own stated preference. Lifecycle + Category +
Priority filtering is sufficient for the note volumes this screen is expected to handle; adding text
search now would be speculative complexity (fuzzy matching, indexing) for an unconfirmed need.

## Available-suggestions and included-notes polish (Parts T/U)

Available suggestion rows already showed Title/Category/Priority/date-range compactly (11D); this phase
adds the same `CarryForwardText` "date relevance" hint used throughout the Manager screen, still no
body preview, still a single-line `Add` action. Included/selected rows gained the frozen-copy
explanation (Part H), the informational source-status line, and the conditional Replace action — kept
to one extra compact row per selection, not a second card.

## Remove wording (Part S)

Reviewed and tightened: the Remove button's `AutomationProperties.Name` changed from `"Remove note
{0}"` to **`"Remove {0} from this passdown"`**, and `AssociateSelectedOperationalNotePresentation.AccessibleSummary`
now leads with "Remove from this passdown." instead of the bare "Remove." — both make explicit that
this removes the note's *inclusion in this draft*, never the persistent source note itself (which stays
manageable only from Operational Notes Manager's own Resolve action).

## Dashboard polish (Part X)

Reviewed; no changes made. `DashboardView.axaml`'s Active Operational Notes title `TextBlock` already
carries `TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"` from Phase 11C, so a long
title clips predictably with an ellipsis rather than overflowing the card — no polish gap was found.

## Accessibility (Part Z) and keyboard workflow (Part AA)

New `AutomationProperties.Name` values: `"Category filter"`, `"Priority filter"`, `"Clear filters"`
(Operational Notes Manager); the Replace button's per-row `StringFormat`-templated name, `"Replace {0}
with current version"` (Associate → Notes), verified directly against the compiled markup in
`AssociateViewAccessibilityTests.ProductRowAndCustomItemTemplates_DeclareAccessibleNamePatterns` (the
same technique 11D used for Add/Remove, since per-row template names are not reliably realized in an
unattached headless container). Every source-comparison state (`SourceStatusText`) is plain text, never
color-only. Every new control is a plain `ComboBox`/`Button`, keyboard-reachable with FluentTheme's
default focus ring — no new mouse-only interaction was introduced anywhere in this phase.

## Desktop density: 1280×860 and 960×640 (Parts AB/AC)

Operational Notes Manager: the filter row's `WrapPanel` now carries five lifecycle tabs plus two
ComboBoxes plus a conditional Clear-filters button; at 1280×860 it fits on one line, and at 960×640 it
wraps onto a second line rather than clipping or hiding a control — the same `WrapPanel` mechanism the
lifecycle tabs already relied on. List/detail master-detail proportions are unchanged. Associate →
Notes: the new frozen-copy explanation and source-status/Replace row add at most two short lines per
selected note; the existing per-section `ScrollViewer`s absorb the added height at 960×640 exactly as
they already did for 11D's own content. No horizontal clipping, no invisible actions, and no truncated
status labels were introduced — every new status/label string is short and untrimmed by design.

## Windows scaling (Part AD)

**NOT EXECUTED.** This phase's development host is Fedora Linux per `CLAUDE.md`'s platform section; no
Windows 11 VM was available to inspect 100%/125%/150% scaling. Every new control uses the same
`DynamicResource`-based styling and relative sizing (`WrapPanel`, `ScrollViewer`, no fixed pixel
Width/Height beyond the existing `MinWidth` on the two new ComboBoxes) already established in 11A–11D,
which were themselves flagged the same way for Windows VM spot-checking before shipping.

## Privacy (Part AF)

No note title/body, frozen selection content, or source-comparison content was added to any log call.
`AssociateViewModel`'s existing `AddOperationalNoteAsync`/`RemoveOperationalNoteAsync` log patterns
(operational-note id + operating date only, on failure) were extended verbatim for the new
`ReplaceOperationalNoteAsync` — same shape, same fields, nothing new logged.
`SqliteDatabaseDiagnosticsProvider` is untouched by this phase, exactly as every prior Phase 11 sub-phase
left it.

## PDF / finalization regression (Part AG)

No Documents-layer code changed in this phase. `AssociatePassdownDocumentModelBuilder.BuildOperationalNotes`
already reads `AssociateDraftPayload.SelectedOperationalNotes` generically (11D); since
`ReplaceOperationalNoteWithCurrentAsync` writes its updated content into that exact same field through
the exact same `SaveDraftAsync` path every other selection change already uses, the existing 11D PDF/
finalization/History test coverage already proves the pipeline handles whatever content sits in that
list — no new Documents-layer test was needed to prove a code path that didn't change. What *was* added
at the Application layer: `ReplaceOperationalNoteWithCurrentAsync_AutosavesTheNewFrozenCopy` (the
replaced content survives a reload) and the "no automatic sync" regression theory
(`NoAutomaticSync_ReloadOrCheckOnline_NeverUpdatesFrozenSelection`, parameterized over both
`LoadAssociateDateAsync` and `CheckAssociateDateOnlineAsync`) proving a source edit is never silently
absorbed into a selection by anything other than the one explicit Replace action.

## Schema / version report (Part AJ)

- **Database schema: 10** — unchanged since 11A. No migration was added; nothing in this phase touches
  SQLite table shape.
- **`AssociateDraftPayload.CurrentSchemaVersion`: 3 — unchanged from 11D.** Part M's decision: source-
  change awareness is computed live from fields the schema already carried; no new JSON property was
  added, so no version bump and no new backward-compatibility branch were needed in `DeserializeDraft`.
- **`FinalizedPassdownSnapshot.CurrentSchemaVersion`: 1 — unchanged.** This phase adds no new frozen
  field to a finalized snapshot.

## Tests added (Phase 11E)

- `Carowinds.Passdown.Application.Tests/Associate/OperationalNoteSourceComparisonTests.cs` (new file) —
  13 cases: unchanged, each of the five single-field-changed content-change cases, Resolved (including
  precedence over a simultaneous content change), no-longer-applicable, missing, the
  any-age-of-selection backward-compatibility proof, and `ComputeAll`'s keyed-batch behavior.
- `Carowinds.Passdown.Application.Tests/Associate/AssociatePassdownServiceTests.cs` — 12 new cases:
  composed-workspace source states for Unchanged/ContentChanged/Resolved/NoLongerApplicable/no-
  selections, Replace updating the frozen copy (content, order preservation, safe no-op when not
  selected, autosave persistence, source-note non-mutation), and the parameterized
  reload-vs-check-online no-automatic-sync regression.
- `Carowinds.Passdown.Ui.Tests/OperationalNotesViewModelFilterTests.cs` (new file) — 28 cases: category
  filter defaults + each of the seven categories, priority filter defaults + each of two priorities +
  All, lifecycle+category, lifecycle+priority, category+priority, all three composed, the
  filtered-vs-no-notes-at-all empty-state distinction, `HasActiveSecondaryFilters`, Clear Filters,
  filter-state survival across Reload, and seven carry-forward label cases (until-resolved,
  expires-today, expires-tomorrow, through-date, starts-tomorrow, resolved-has-no-label, and the label
  appearing in the detail presentation too).
- `Carowinds.Passdown.Ui.Tests/AssociateViewModelOperationalNoteSelectionTests.cs` — 8 new cases: no
  source status when unchanged, each of the four informational source-comparison states surfacing the
  right text/Replace-visibility, Replace updating the frozen copy and autosaving, Replace never mutating
  the source list in place, and a reload alone never applying a Replace.
- `Carowinds.Passdown.Ui.Tests/OperationalNotesViewTests.cs` — 2 new cases (filtered-empty-state and
  category+priority-filter construction) plus three new automation-name assertions (Category filter,
  Priority filter, Clear filters) added to the existing critical-automation-names test.
- `Carowinds.Passdown.Ui.Tests/AssociateViewAccessibilityTests.cs` — 1 new case constructing all four
  source-comparison states at once (proving `ShowReplaceAction` is true only for ContentChanged) plus
  two new `StringFormat` markup assertions (Remove's updated wording, Replace's new pattern) added to
  the existing template-pattern test.
- `Carowinds.Passdown.Ui.Tests/TestSupport/FakeAssociatePassdownService.cs` — extended with
  `AllOperationalNotes` (mirrors `IOperationalNotesService.ListAsync`), `ReplaceOperationalNoteCalls`,
  and `ReplaceOperationalNoteWithCurrentAsync`; `BuildWorkspace` now computes
  `SelectedOperationalNoteSourceStates` via the same `OperationalNoteSourceComparison.ComputeAll` the
  real service uses (no duplicated comparison logic in the test double).

64 new tests total (1176 Phase 11D baseline + 64 = 1240: 25 Application + 39 Ui).

## Deferred to 11F (and evaluated-and-rejected items)

- Selected-note Up/Down reordering (Parts Q/R) — evaluated and deferred by design; no concrete need
  demonstrated.
- Text search across Operational Notes (Part W) — evaluated and deferred by design per the spec's own
  stated preference.
- The final Phase 11 audit and release rebuild (Phase 11F) — the release ZIP was intentionally not
  rebuilt by this phase.
- Alerts, an Operations Calendar, and the Manager workflow remain out of scope, unchanged from
  11A–11D/`CLAUDE.md`.
- Windows 100%/125%/150% scaling spot-checks — **NOT EXECUTED**, no Windows 11 VM available in this
  environment; flagged for pre-ship verification exactly as every prior Phase 9-11 document already
  flags its own Windows-scaling gap.

## Full validation (Phase 11E)

`dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
`dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1240/1240** passed (66 Domain +
351 Application + 94 Documents + 317 Infrastructure + 412 Ui), run twice to confirm no flakiness, 0
failed, 0 skipped. No live internet was used or required.

One unrelated, pre-existing flaky test was observed during the first full-suite run:
`FinalizedPassdownProjectionTests.BuildProjection_NormalDay_PdfBytesMatchDirectGenerationFromSameModel`
failed once with a single-byte difference consistent with a PDF metadata/creation-timestamp race between
two independent `Generate` calls in the same test (confirmed unrelated to this phase's changes — it
passed on an unmodified checkout of this branch, and passed consistently across three repeated isolated
runs afterward). No code in `Carowinds.Passdown.Documents` was touched by Phase 11E.

---

# Phase 11F — final audit and release closeout

Phase 11F is not a new feature phase — it audits Phase 11A–11E as one system, closes the one
verification gap the audit found, and rebuilds/re-verifies the Windows release ZIP. Full narrative,
per-part audit findings, and the release rebuild mechanics live in `docs/release-strategy.md` ("Phase
11F rebuild"); exact artifact metadata (SHA-256, sizes, entry counts, schema versions) lives in
`docs/release-manifest.md`. This section is the short summary.

## What was audited

Every Phase 11A–11E claim was re-checked directly against current code and tests (constructor
signatures, service/repository method bodies, migration registration, the real PDF/finalization/
History call graph), not just against each sub-phase's own prior documentation:

- **11A** — model/persistence/service: still local-only, no delete method, no Reopen, relevance/
  lifecycle/ordering logic unchanged and correctly reused everywhere else.
- **11B** — Notes Manager UI: still no Manager/Alerts/Calendar navigation, no Delete/Reopen, dirty-edit
  guard still disables other top-level sections mid-edit.
- **11C** — Dashboard integration: still read-only, still architecturally separate from the manual
  Associate-draft notes preview, still exactly one query per load.
- **11D** — explicit inclusion/frozen copy: confirmed, by direct code trace, that there is no code path
  from "note is relevant" to "note is in a passdown" that skips the explicit `SelectOperationalNoteAsync`
  call — Dashboard load, Associate load, Check Online, autosave, and scheduled refresh all leave
  `SelectedOperationalNotes` untouched.
- **11E** — filtering/carry-forward/source-awareness: filters remain deterministic and non-mutating;
  source comparison states remain informational except for the one explicit Replace action; no
  auto-sync or auto-remove path exists anywhere.

## The one gap found, and how it was closed

Part M of the Phase 11F audit brief specifically requires a scenario: select a persistent note into a
passdown, edit the source note afterward without explicitly replacing it, finalize, and confirm the
finalized snapshot/PDF still show the **old** frozen text (plus the mirror-image explicit-Replace
case, which should show the **new** text). This guarantee held in code, but was previously provable
only by composing two separately-tested facts (frozen selections never silently resync; finalization
only ever reads the already-saved draft) rather than by one direct test proving the full chain.

Closed by adding two tests to
`tests/Carowinds.Passdown.Documents.Tests/Associate/FinalizedPassdownSnapshotBuilderTests.cs`:
`Build_SelectedNoteSourceEditedWithoutReplace_SnapshotStillShowsFrozenOldText` and
`Build_SelectedNoteAfterExplicitReplace_SnapshotShowsNewText`. Both construct an
`AssociateOperationalNoteSelection` with the exact field shape `AssociatePassdownService.
SelectOperationalNoteAsync`/`ReplaceOperationalNoteWithCurrentAsync` produce, run it through the real
`FinalizedPassdownSnapshotBuilder`, and assert the resulting `OperationalNotes` text is the frozen-old
or explicitly-replaced-new text respectively — never a blend of the two. This was a test-coverage gap,
not a functional defect; no production code changed.

## Release rebuild result

- Full non-live suite: **1242/1242** (up from the Phase 11E baseline of 1240 by the two tests above),
  0 failed, 0 skipped, run clean twice before the fix and once again after it.
- Build: `dotnet build CarowindsPassdown.slnx` — 0 warnings, 0 errors.
- Schema versions confirmed unchanged in code: database schema **10**, `AssociateDraftPayload` schema
  **3**, `FinalizedPassdownSnapshot` schema **1**.
- Windows release ZIP rebuilt (`Carowinds-Passdown-0.1.0-dev-win-x64.zip`), independently verified via
  a byte-level `strings` search of the freshly published assemblies to contain Phase 11A–11E code
  (`OperationalNotesService`, `OperationalNotesViewModel`, `DashboardOperationalNotesSummary`,
  `AssociateOperationalNoteSelection`, `ReplaceOperationalNoteWithCurrentAsync`,
  `OperationalNoteSourceComparison`, `Migration0010CreateOperationalNotesTable` all present), and
  byte-for-byte re-inspected after an independent extraction. New SHA-256 recorded in
  `docs/release-manifest.md`; the prior (Phase 10F) artifact is explicitly marked superseded.
- `packaging/windows/TESTER-GUIDE.txt` and `packaging/windows/README-portable-release.md` updated to
  describe the Operational Notes Manager, the Dashboard's Active Operational Notes summary, and the
  explicit per-date inclusion/frozen-copy/Replace-with-Current-Version workflow.

## Confidence gaps carried forward honestly

No Windows host or VM was available in this session — the same limitation recorded for every prior
Phase 7/9/10 rebuild from this dev host. **NOT EXECUTED**: the focused Windows Operational Notes smoke
walkthrough, the live Windows finalization walkthrough (its automated equivalent now passes — see
above), and Windows 100%/125%/150% display-scaling validation. Raster/pixel visual validation remains
**PARTIAL** (construction-level real-XAML-loader checks pass; no bitmap/screenshot renderer exists in
this codebase). None of these gaps is presented as a false PASS; see `docs/release-strategy.md` "Phase
11F rebuild" → "Release confidence gaps" for the full table.

## Deferred, still

Reordering, text search, a Reopen operation, Alerts, an Operations Calendar, and the Manager workflow
all remain out of scope for Phase 11 and are not implied to exist by this closeout.

## Phase 11 complete

With the one verification gap closed and every other Phase 11A–11E requirement confirmed against
current code and a clean, twice-repeated full test run, **Phase 11 is complete**: persistent
Operational Notes, their explicit opt-in inclusion into a daily passdown, frozen-copy/source-change
behavior, and the Windows release artifact are all shipped and verified to the extent possible without
a Windows host. Release readiness verdict: **READY WITH ACCEPTED LIMITATIONS** (see
`docs/release-strategy.md` for the full verdict table).
