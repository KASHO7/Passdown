# Phase 16A — Personal OneDrive Sync: Architecture

Status: Phase 16A below is **design only**, unchanged since it was written. Phase 16B (see the
section appended after it) implements the reusable local foundation this design calls for — device
ID, local sync configuration, OneDrive suggestion, folder validation, the sync path/envelope/manifest
contracts, atomic writes, and a small settings screen; no entity is exported or imported yet. **Phase
16C** (appended at the end of this document) implements the first real record-level sync — Associate
drafts and Operational Notes, both directions, with conflict detection (never silent auto-resolve).
Finalized History/PDFs, deletions/tombstones, and conflict-resolution UX all remain deferred to later
phases. Nothing in any of these three phases changes normal (non-sync) persistence, startup, or
workflow behavior when sync is off or unavailable.

Baseline this audit was performed against: Phase 15 complete, DB schema 11, Draft schema 3,
Snapshot schema 1, 1674/1674 non-live tests passing, application local-first.

## 0. Goal

Let a single person who uses this app on more than one Windows machine (e.g. a desktop at a
support office and a laptop) keep their **own** Associate drafts, Operational Notes, and finalized
History in sync, using their existing personal OneDrive folder as the transport. This is not
multi-user collaboration, not a server, and not a backup product — it is one person's data
following them between their own devices.

Non-negotiable, repeated here because it drives every decision below:

- Each device keeps its own local SQLite database. `carowinds-passdown.db` (and its `-wal`/`-shm`
  files) never lives inside a OneDrive-synced folder.
- No SQLite file, WAL/SHM file, or log file is ever synchronized.
- No Microsoft Graph API, no OAuth, no Microsoft account. The Microsoft OneDrive desktop client is
  the only thing that talks to Microsoft; this app only reads/writes plain files in a local folder
  that OneDrive happens to also be watching.

## A. Data audit and classification

Audited by reading every migration (`src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0001`
through `Migration0011`) and the repository/persistence-port pair for each table.

| Data | Table / source | Class | Rationale |
|---|---|---|---|
| Associate draft (manual hours, private buyout, attendance, ERT, Daily Feature, product overrides, custom products, section selection, selected Operational Notes) | `associate_drafts` ([Migration0006CreateAssociateDraftsTable.cs](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0006CreateAssociateDraftsTable.cs)) | **USER-AUTHORED — sync** | Entirely hand-entered per [AssociateDraftPayload.cs](../src/Carowinds.Passdown.Application/Associate/AssociateDraftPayload.cs); spec already guarantees it "must never modify official cached values," so it has no dependency on retrieved data at rest. |
| Operational Notes | `operational_notes` ([Migration0010CreateOperationalNotesTable.cs](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0010CreateOperationalNotesTable.cs)) | **USER-AUTHORED — sync** | Supervisor-managed, persistent, mutable rows independent of any one date's draft. |
| Selected-Operational-Note copies embedded in a draft (`AssociateOperationalNoteSelection`) | inside `associate_drafts.payload_json` | **USER-AUTHORED — sync (as part of the draft)** | [AssociateDraftPayload.cs:74-91](../src/Carowinds.Passdown.Application/Associate/AssociateDraftPayload.cs) — these are frozen, per-draft copies made at selection time, never re-read from the live note. They sync as an inseparable part of the draft payload; there is no separate "note inclusion" table to design identity for. |
| Finalized passdown snapshot | `finalized_passdowns` snapshot columns ([Migration0008](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0008CreateFinalizedPassdownsTable.cs)) | **IMMUTABLE USER HISTORY — sync** | Insert-only (`SqliteFinalizedPassdownRepository.AddAsync` has no `ON CONFLICT`), one row per date, content-hashed. |
| Frozen PDF bytes | `finalized_passdowns.pdf_bytes/pdf_sha256/pdf_size_bytes` ([Migration0009](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0009AddFinalizedPassdownPdfColumns.cs)) | **IMMUTABLE USER HISTORY — sync** | Same row/identity as the snapshot; travels with it, not separately. |
| Catalog products | `catalog_products` ([Migration0004](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0004CreateCatalogProductsTable.cs)) | **PUBLIC/RETRIEVABLE CACHE — do not sync** | Populated only from official retrieval adapters; re-derivable per device by refreshing. |
| Product history | `product_history` ([Migration0005](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0005CreateProductHistoryTable.cs)) | **PUBLIC/RETRIEVABLE CACHE — do not sync** | Append-only audit trail of the cache above; same provenance. |
| Dated retrieval results (hours, admission, Fast Lane, haunted, etc.) | `dated_results` ([Migration0003](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0003CreateDatedResultsTable.cs)) | **PUBLIC/RETRIEVABLE CACHE — do not sync** | Explicitly a last-known-good cache of official public data (spec §6.7); every device can re-fetch it. |
| `source_state` | ([Migration0002](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0002CreateSourceStateTable.cs)) | **DEVICE/OPERATIONAL STATE — do not sync** | Per-device retrieval scheduling (last attempt, next due, failure count) — meaningless, and actively harmful (would desync each device's own retry cadence), if copied between machines. |
| `deep_refresh_status` | ([Migration0011](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0011CreateDeepRefreshStatusTable.cs)) | **DEVICE/OPERATIONAL STATE — do not sync** | Single-row, per-machine record of the most recent Deep Refresh run on *this* machine. |
| Refresh lock | `FileScheduledRefreshLock` ([FileScheduledRefreshLock.cs](../src/Carowinds.Passdown.Infrastructure/Concurrency/FileScheduledRefreshLock.cs)) — filesystem lock file, not a DB row | **DEVICE/OPERATIONAL STATE — do not sync** | Exists specifically to serialize concurrent refresh attempts *on one machine*; syncing it across devices would be actively wrong (a lock is not a distributed lock here). |
| Logs | `FileLoggerOptions.LogDirectory` = `AppPaths.LocalApplicationDataDirectory\logs` ([FileLoggerOptions.cs](../src/Carowinds.Passdown.Infrastructure/Logging/FileLoggerOptions.cs)) | **DEVICE/OPERATIONAL STATE — do not sync** | Per-machine diagnostic output; may contain local paths/machine-specific detail. |
| Scheduler state (Windows Task Scheduler registration, `ScheduledTaskPlan`) | [`ScheduledRefresh/`](../src/Carowinds.Passdown.Desktop/ScheduledRefresh/) | **DEVICE/OPERATIONAL STATE — do not sync** | Registered against one machine's Task Scheduler; not a data record at all. |
| Manager drafts | `manager_drafts` table ([Migration0007](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0007CreateManagerDraftsTable.cs)) | **out of scope for 16A** | Table exists from schema history but `SqliteManagerDraftRepository` is never registered in `App.axaml.cs` DI — no code path writes to it (Manager workflow deferred per CLAUDE.md). Nothing to sync today; revisit if/when the Manager workflow is built. |

This matches the prompt's expected direction exactly; nothing in the schema forced a deviation.
One addition the prompt's checklist didn't separately name: `schema_version`/`snapshot_schema_version`
columns and the `content_hash` column on `finalized_passdowns` — both are core to the sync design
(§D, §G) and travel with their owning row, not as independent sync entities.

## B. Identity audit

| Entity | Existing identity | Stable across devices? | Mutable? | Created ts | Modified ts | Deletion semantics | Dependencies |
|---|---|---|---|---|---|---|---|
| Associate draft | `operating_date` (DateOnly), PK of `associate_drafts` ([AssociateDraft record](../src/Carowinds.Passdown.Application/Persistence/AssociateDraftPersistence.cs)) | **Yes, reuse as-is.** One draft per calendar date is already the domain's own identity; two devices editing "2026-09-09" both mean the same logical record. | Yes — `SaveAsync` is a full upsert/replace of `payload_json`, no field-level diffing (`SqliteAssociateDraftRepository.cs:29-50`). | Not tracked. Only `updated_at_utc` exists. | `updated_at_utc` (`DateTimeOffset`), set on every save. | No delete method exists at all today. | Embeds frozen copies of selected Operational Notes (see A above); no FK relationship in SQL. |
| Operational Note | `id` (`Guid`), PK of `operational_notes` | **Yes, reuse as-is.** Already a random GUID assigned at creation, exactly the shape a sync entity ID needs. | Yes — `UpdateAsync` is a full row replace. | `created_at_utc`. | `updated_at_utc`. | No delete method; "deletion" today is `status = Resolved` ([OperationalNote.cs:38-42](../src/Carowinds.Passdown.Application/OperationalNotes/OperationalNote.cs)) — Active/Resolved is a lifecycle state, not a tombstone, and a resolved note is still a live row forever. | None. |
| Finalized passdown | `id` (`Guid`), PK; `passdown_date` has a separate unique index ([Migration0008](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0008CreateFinalizedPassdownsTable.cs)) | **Yes, reuse `id`.** `passdown_date` is the natural business key (one finalization per date) but `id` is already the surrogate sync-friendly key the schema's own comment says was chosen for exactly this kind of future-proofing. | **No — insert-only** (`SqliteFinalizedPassdownRepository.AddAsync` has no `ON CONFLICT`; a second insert for the same date throws on the unique index). | `finalized_at_utc`. | N/A — immutable. | N/A — never deleted by any code path today. | Carries `content_hash` = SHA-256 of the canonical snapshot JSON (`FinalizedPassdownSnapshotSerializer.ComputeContentHash`) — this is exactly the "same identity, same content ⇒ dedupe" signal §G needs, already computed today for integrity, not sync. |
| Frozen PDF | Same row as its Finalized passdown (`pdf_bytes`/`pdf_sha256`/`pdf_size_bytes` columns added by [Migration0009](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0009AddFinalizedPassdownPdfColumns.cs)) | **Yes — tied to the FinalizedPassdown `id`, not a separate identity.** No column suggests otherwise; PDF columns are nullable specifically because pre-9C rows never had one. | No — same immutability as the row it lives in. | Same as owning row. | N/A | Same as owning row. | Must never be synced independently of its snapshot row — a PDF without its snapshot (or vice versa) is a corrupt sync unit. |

No existing ID needs replacing. Every syncable entity already has a stable, device-independent
identity (a `DateOnly` or a `Guid`); Phase 16 sync IDs are these IDs, not new ones.

## C. Device identity

No `DeviceId` concept exists anywhere in the codebase today (confirmed: no hits for `DeviceId`,
`InstallationId` under `src/`). Design, not implemented in 16A per the phase instructions'
preference for design-only:

- A `Guid` (`Guid.NewGuid()`), generated once the first time sync is ever configured on a device
  (not at first app launch — a device that never enables sync never needs one).
- Persisted as a single opaque value, e.g. `Settings/device-id.txt` (or a `device_identity` table —
  a single-row table matching the existing `deep_refresh_status` pattern) under
  `AppPaths.LocalApplicationDataDirectory`. Not derived from machine name, Windows username, email,
  or any hardware identifier — a fresh random GUID carries no personally identifying information by
  construction.
- Purely a **tie-breaker and provenance tag** for conflict resolution (§G) and for excluding a
  device's own writes when re-reading the sync folder — it is never displayed to the user as an
  account identity and never leaves the local OneDrive folder.
- If the local file/table is lost (e.g. reinstall), a new DeviceId is generated. This is safe: the
  conflict rules in §G do not depend on a DeviceId being long-lived, only on it being distinct
  between concurrently-editing devices at any one point in time.

## D. Sync envelope

Two constraints shaped this more than the illustrative struct in the phase prompt:

1. Every syncable entity **already carries its own schema version** (`AssociateDraftPayload.SchemaVersion`,
   `FinalizedPassdownSnapshot.SchemaVersion`) and its own opaque, versioned JSON payload. The sync
   envelope must never re-version that payload — it wraps it.
2. Timestamps alone are not sufficient for the draft conflict case (§G) — clock skew between two
   Windows machines is a real, unauditable risk the design must not silently trust.

```
SyncEnvelope
{
    SyncSchemaVersion   int          // version of THIS envelope shape, independent of
                                      // AssociateDraft.SchemaVersion / FinalizedPassdownSnapshot.SchemaVersion
    EntityType          string       // "AssociateDraft" | "OperationalNote" | "FinalizedPassdown"
    EntityId            string       // the entity's OWN existing identity (§B) — a DateOnly text
                                      // ("2026-09-09") for AssociateDraft, a Guid.ToString() for the others
    DeviceId            Guid         // which device produced this revision (§C)
    Revision            int          // monotonically increasing PER (EntityId, DeviceId) — not a
                                      // wall-clock value; see §G for why this exists alongside ModifiedUtc
    ModifiedUtc         DateTimeOffset // for tie-breaking display/ordering only, never as the sole
                                      // arbiter of "which write wins" (§G)
    IsDeleted           bool         // tombstone flag (§G) — never inferred from file absence
    PayloadSchemaVersion int         // copy of the wrapped entity's own SchemaVersion, so a reader
                                      // can migrate the payload without opening it first
    Payload             object       // the entity's existing JSON shape, byte-for-byte what
                                      // AssociateDraftPayloadSerializer / FinalizedPassdownSnapshotSerializer
                                      // already produce — never re-shaped by the sync layer
    ContentHash         string?      // present only for FinalizedPassdown — reuses the existing
                                      // FinalizedPassdownSnapshotSerializer.ComputeContentHash value,
                                      // not a new hash scheme
}
```

Notes:

- `SyncSchemaVersion` starts at `1`. It changes only when the *envelope* shape changes (e.g. a new
  required field), completely independent of `carowinds-passdown.db`'s schema version (currently
  11) and of each payload's own schema version. Three independent version numbers, three independent
  reasons to bump them — conflating any two was the single biggest risk this design deliberately
  avoids.
- Operational Notes need no `ContentHash` (they're mutable, so content-hash dedup doesn't apply the
  way it does to immutable History) and no `PayloadSchemaVersion` distinct from the envelope, since
  `OperationalNote` has no independent JSON payload today — its fields map directly onto envelope
  fields, unlike the two blob-payload entities.
- `Payload` for `FinalizedPassdown` is the snapshot JSON plus the PDF, which do not fit the same
  "one JSON blob" shape as a draft — see §E, where the PDF is stored as a sibling file rather than
  base64-inflated inside the envelope JSON.

## E. OneDrive folder layout

Chosen for the actual data model above: three distinct entity kinds with three distinct
identities (a date, a note GUID, a finalized-passdown GUID), one of which (History) carries a
large binary sibling (the PDF) that should not be inflated ~33% via base64 into a JSON envelope.

```
Carowinds Operations Sync/
    manifest.json                       # {DeviceId, SyncSchemaVersion, format notes}
    drafts/
        2026-09-09.json                 # one file per operating date — EntityId IS the filename
        2026-09-10.json
    notes/
        3fa85f64-5717-4562-b3fc-2c963f66afa6.json   # one file per Operational Note GUID
    history/
        2026-09-09.json                 # SyncEnvelope + FinalizedPassdownSnapshot JSON (small)
        2026-09-09.pdf                  # sibling frozen PDF, named identically to its record
```

Why not a `pdfs/` folder alongside `history/`: keeping `2026-09-09.json`/`2026-09-09.pdf` as
siblings under `history/` means an atomic pair-write (§F) only ever needs to reason about one
directory, and a reader can tell at a glance whether a History record's PDF made it across without
cross-referencing two directory listings.

Requirements check:

- **Deterministic**: filename = entity identity, always. No incrementing counters, no timestamps in
  filenames (which OneDrive conflict-copies would only make worse — see next point).
- **Inspectable**: every file is either human-readable JSON or a normal PDF; a person can open
  `notes/<guid>.json` in Notepad and understand it.
- **Versionable**: `manifest.json` records `SyncSchemaVersion`; each envelope also records its own,
  so a folder can be read by a future version of the app without a manifest round-trip per file.
- **Atomic-write friendly**: filenames are stable, so temp-file-then-rename (§F) always targets a
  known final name.
- **Resistant to OneDrive conflict copies**: OneDrive's own conflict-copy behavior
  (`name-DEVICENAME.json`) only triggers when two clients write the *same filename* at nearly the
  same wall-clock instant without one having synced the other's version first. Per-record files
  minimize the blast radius of that (only one date's draft is affected, never the whole dataset),
  but it does not eliminate the *need* for the envelope's own `Revision`/`DeviceId` conflict
  handling in §G — OneDrive's conflict copy is a filesystem-level accident-avoidance net, not this
  app's conflict resolution.
- **No live SQLite, no logs/cache**: nothing here is a `.db`/`.wal`/`.shm`/`.log` file; the folder
  is exclusively sync-domain files this design owns.

## F. Atomic writes

Standard write-temp-then-rename, adapted for the one thing OneDrive actively works against — it
uploads a file the instant it sees the final name change, so the write must be indistinguishable
from "already finished" the moment the OS sees it:

```
1. Write full content to <target>.tmp-<random-suffix>  (same directory as the final file —
                                                          NOT a system temp folder, so the
                                                          rename in step 3 is same-volume)
2. Flush + close the file handle
3. File.Move(tmp, target, overwrite: true)   // atomic on both NTFS and the target OneDrive
                                              // folder is itself just an NTFS folder — OneDrive
                                              // only reacts to the filesystem event stream, it
                                              // does not intercept individual write() calls
4. (History only) write the .pdf sibling using the same temp-then-rename, BEFORE renaming the
   .json into place — a reader must never observe a .json without its .pdf already present
```

Platform assumptions: Windows 11 x64 target, NTFS-formatted OneDrive folder (the default and
practically the only supported configuration for the OneDrive desktop client) — `File.Move` with
`overwrite: true` on the same volume is atomic at the OS level, which is what makes this safe
against a half-written file being picked up mid-write. This assumption would need re-checking if a
non-NTFS OneDrive mount point were ever supported, which is not planned.

> **Phase 18B (REL-003) update — power-loss durability.** Step 2 is now "write, flush, then
> `FileStream.Flush(flushToDisk: true)` (`FlushFileBuffers` on Windows / `fsync` on Unix), then close".
> The data is therefore on stable storage *before* the rename can exist, so after a power loss the
> target is either the complete previous version or the complete new version — never zero-length or
> partial. **Residual risk:** the rename itself is not forced to disk (.NET has no portable directory
> fsync; on Windows it would need `MoveFileEx(MOVEFILE_WRITE_THROUGH)` P/Invoke, deliberately not
> added). A power loss within the filesystem's metadata-commit window (typically seconds) after a
> write completed can roll the target back to its previous complete version; sync redoes the export
> (its synced state was not recorded), and a settings change is simply re-saved. See §18B below.

Never write progressively into the final filename — the earlier written-content requirement in the
phase prompt is the load-bearing rule here: OneDrive uploads on filesystem change events, not on
"file looks done," so a partially-written final-name file is a real risk without the temp+rename
step.

## G. Conflict model

Per the phase prompt's explicit instruction: **no single "newest timestamp wins" rule for
everything.** Each entity gets its own rule, grounded in what §B found about its actual mutability.

### Finalized History (immutable)

- Identity: `id` (Guid). Content: `ContentHash` (already computed today, §B).
- Same `id` + same `ContentHash` seen from two devices ⇒ **the same record, trivially deduplicate**
  — this is the expected common case (Device A finalizes, syncs, Device B later finalizes nothing
  for that date and just receives A's record).
- Same `id`... is actually never expected to collide across devices for *different* content, because
  `id` is a `Guid.NewGuid()` minted once at finalization on one device — two devices cannot both
  mint the same `id` for logically different finalizations. The real collision case is **same
  `passdown_date`, different `id`, different `ContentHash`** — i.e., both devices independently
  finalized the same operating date (e.g. both were online and both ran the 6:00 AM scheduled
  finalizer, or a manual finalize raced a scheduled one on two machines). This is a **conflict, not
  a merge**: History must never silently overwrite or pick a "winner." Surface both records to the
  user (a future 16E-era UI concern) with clear provenance (`FinalizationOrigin`, `finalized_at_utc`,
  which device) and require an explicit choice, or — simpler for the first release — treat it as a
  hard error surfaced at sync time ("two finalizations exist for 2026-09-09; sync paused for this
  date, resolve manually") rather than ever picking one automatically. 16A does not decide the UI;
  it decides that automatic silent resolution is off the table.
- Local `finalized_passdowns.passdown_date` already has a `UNIQUE` index (Migration0008) — this
  local invariant is exactly what makes the dual-finalization scenario detectable, not something
  sync needs to invent.

### Operational Notes (mutable, record-level)

- Identity: `id` (Guid), already stable across devices (§B).
- `Revision` (envelope field, per `(EntityId, DeviceId)`) plus `ModifiedUtc` together, not
  `ModifiedUtc` alone: when Device A and Device B have each seen the same last-synced revision of a
  note and then both edit it before either sees the other's edit, the sync engine must detect this
  as a genuine concurrent edit rather than "whoever's clock reads later wins," since two Windows
  machines' clocks are not asserted to be in sync anywhere in the current codebase.
- Detection rule: a device accepts an incoming envelope for a note only if the incoming envelope's
  base revision (the revision it was edited from) matches what that device last saw as
  "synced" for that note. If it does not match, both edits exist concurrently — the record-level
  granularity (whole note fields, not the associate-draft-style single JSON blob) means a simple
  per-field merge is *possible* in a later phase (16D), but 16A's floor behavior is: keep both, mark
  one a pending-conflict copy, never drop either silently. `status = Active/Resolved` is itself just
  a field under this same rule — a "resolve" on Device A racing an "edit the body" on Device B is a
  concurrent edit like any other, not a special case.

### Drafts (mutable, hardest case — explicitly called out by the prompt)

- Identity: `operating_date` (a `DateOnly` — reused as-is, §B). Content: the entire
  `AssociateDraftPayload` as one JSON blob — there is no field-level granularity today
  (`SqliteAssociateDraftRepository.SaveAsync` always replaces the whole `payload_json`).
- Because the local repository itself has no partial-update path, sync cannot invent field-level
  merging for drafts without first changing local persistence (out of scope for 16A, and arguably
  for 16B/C too — this is a real product question, see §Unresolved below). The realistic floor
  behavior for the same reasons as Operational Notes: whole-draft, revision-tracked,
  concurrent-edit detection.
- Same-date edits on two devices before either has seen the other's sync:
  - If the two payloads are byte-identical after canonicalization, no conflict — just converge.
  - If they differ, this is a genuine concurrent edit of one date's draft. **Do not silently pick
    either.** The floor behavior: keep the device's own local copy authoritative for that device's
    UI (so nobody's screen changes underneath them mid-edit), write the incoming remote version
    alongside it as a labeled alternate ("a draft for this date was also edited on \<other
    device\>, at \<time\> — Keep mine / Use theirs / (later phase: merge fields)"), and require an
    explicit choice before the date is considered "synced" again. This mirrors the History rule
    (never auto-resolve) but for a mutable entity instead of an immutable one.
  - This is very likely a place where the eventual UX wants smarter defaults (e.g. "last edited
    wins, but only if the other device saw your edit first" — a proper vector-clock-lite scheme).
    16A's contribution is establishing that `ModifiedUtc` alone must never be that default; the
    exact UX is deferred to 16D and flagged in §Unresolved below.

### Deletions

- No entity in the current schema has a real delete path (associate drafts have no delete method at
  all; Operational Notes only have a `Resolved` status, not a delete; Finalized passdowns are
  never deleted by any code). This actually simplifies 16A: **there is currently nothing to design
  local-delete-propagation for**, because the local app cannot delete these things today.
- The design still needs a tombstone concept for the sync layer itself, because a future local
  "delete" UI is plausible and because a sync record could legitimately need to represent
  "this device's copy of the sync folder no longer has this file" without that meaning "delete it
  everywhere" (see Bootstrap, §M, and Offline, §H — a device that has never synced before must not
  be able to delete anything by mere absence).
- Rule: `IsDeleted` is a field the *envelope* carries, set only by an explicit local action that
  writes a new revision with `IsDeleted = true` — it is never inferred from "the file is missing
  from the OneDrive folder" (a missing file might mean OneDrive hasn't finished uploading it yet,
  or the folder was moved, or sync was reconfigured) or from "the file was there before but isn't
  now" during a scan. A tombstone is itself a normal envelope + `Revision` bump, subject to the
  exact same conflict rule as any other edit — a delete racing an edit is a concurrent edit, not a
  special case that skips detection.

## H. One-device-first behavior

Nothing above requires OneDrive to exist for the app to function, because nothing above is
implemented yet — that is exactly the property to preserve into 16B+. The design keeps this true
by construction: sync is a background writer/reader of a folder the app is told about (§K); if that
folder does not exist, is not configured, or vanishes mid-session, every existing local operation
(launch, edit draft, save, generate PDF, refresh, finalize, use History) reads/writes SQLite
exactly as it does today and never blocks on, or fails because of, the sync folder. This is a
constraint on 16B's engine (it must be an optional, fully-decoupled writer that observes local
saves rather than something the save path calls through), not something 16A needs to prove with
code — there is no code yet to violate it.

## I. Security / privacy

Audited what would end up in a sync record if built as designed above:

- Associate draft payload: manual hours, attendance numbers, ERT text, Daily Feature text, product
  price overrides, custom product entries, and frozen note-selection copies. All of this is
  operational content the associate already intends for the printed Passdown — no more sensitive
  synced than printed.
- Operational Note: title/body/category/priority — same category of content, already meant to be
  read by other associates.
- Finalized snapshot + PDF: exactly what was printed and handed out. No hidden fields.
- None of the above ever contains: credentials, auth tokens (there are none in this app — no
  Microsoft account, no API keys), Windows username, or a hardware identifier. `DeviceId` (§C) is a
  bare random GUID with no reverse-lookup meaning.
- Logs are explicitly excluded from sync (§A) specifically because `FileLoggerOptions` output could
  contain local file paths or other machine-specific diagnostic detail not meant to leave the
  device.
- No encryption or authentication is being built in 16A (explicitly out of scope). The
  confidentiality boundary is whatever the user's own OneDrive account/folder permissions already
  provide — this app does not add or subtract from that boundary, and does not claim to.

## J. Storage estimate

Measured, not invented, where a real sample existed; stated as an assumption otherwise.

- **Draft JSON**: A real (if nearly empty — one custom product entry, no overrides, no notes)
  draft row from a local dev database measured **495 bytes** of `payload_json`. A fuller day (a
  manual-hours override, a private buyout with details text, 5–10 product overrides, 2–3 custom
  products, 2–3 selected Operational Notes each carrying their own title/body) is estimated at
  **1.5–3 KB** based on the shape of `AssociateDraftPayload` (§B) — every field is short text,
  short numbers, or small lists, nothing large.
- **Operational Note**: title + body are the only free-text fields; assuming a realistic body of a
  few sentences, **~0.5–1.5 KB** per note as JSON.
- **Finalized snapshot**: structurally a superset of one day's rendered product/park-hours/notes
  content (`FinalizedPassdownSnapshot`, §B) — estimated **3–6 KB** (larger than a draft because it
  spells out every rendered product row and category, not just overrides).
- **Frozen PDF**: measured directly — two real generated PDFs in this repo
  (`artifacts/passdown-preview.pdf`, `artifacts/passdown-preview-polished.pdf`) are **57–58 KB**
  each.

One operating date's full History record ≈ 60–65 KB (PDF-dominated). One draft ≈ 1–3 KB. Assuming
the park operates roughly 200–300 days/year and one draft + one finalization per operating day
(Operational Notes accrue separately and more slowly — assume ~50/year, generously):

| Horizon | Drafts (~2.5 KB × days) | History+PDF (~62 KB × days) | Notes (~1 KB × count) | Total (≈) |
|---|---|---|---|---|
| 1 year (~250 days) | ~625 KB | ~15.5 MB | ~50 KB | **~16 MB** |
| 3 years | ~1.9 MB | ~46.5 MB | ~150 KB | **~49 MB** |
| 5 years | ~3.1 MB | ~77.5 MB | ~250 KB | **~81 MB** |

Frozen PDFs dominate entirely, as expected. Even 5 years of daily History easily fits any personal
OneDrive quota (free tier is 5 GB); no compaction/archival strategy is needed for 16A or in the
foreseeable future. This is an estimate built from one real draft sample and two real PDFs, not a
measured multi-year corpus — treat the per-year figures as order-of-magnitude, not precise.

## K. OneDrive location strategy

No environment-variable or hardcoded-path detection exists in the codebase today (confirmed: no
references to OneDrive anywhere under `src/`). Design for 16B+:

- **Do not hardcode** `C:\Users\<name>\OneDrive`. The OneDrive desktop client sets environment
  variables at install time — `OneDrive` (personal) and `OneDriveConsumer`/`OneDriveCommercial` —
  that can be read (`Environment.GetEnvironmentVariable("OneDrive")`) to **suggest** a default, not
  to assume one exists (a machine without OneDrive installed, or with it installed after this app,
  will have none of these set).
- **User selection remains authoritative.** A folder picker under a future *Settings → Personal
  Sync → Choose OneDrive Folder* screen lets the user point at any folder — inside their OneDrive
  root, inside a subfolder, or (for testing) anywhere else entirely; the app does not need to
  validate that the chosen folder is "really" OneDrive, since OneDrive syncing a folder is a
  property of the folder's location relative to the OneDrive client's configured roots, not
  something this app can authoritatively detect.
- **Remember the choice** once made, the same way `AppPaths.LocalApplicationDataDirectory` is a
  fixed local convention today — a single config value (e.g. `Settings/sync-folder-path.txt`, or a
  row in a small local-only settings store) under `AppPaths.LocalApplicationDataDirectory`. This
  config value is itself device/operational state (§A) and must never be synced.
- Detecting whether a chosen folder is actually being synced by a running OneDrive client (vs. a
  plain local folder the user picked by mistake) is a nice-to-have health check for a later phase,
  not a 16A concern — 16A does not implement UI at all.

## L. Multiple devices — scenario walkthroughs

Modeled against the design above (no code executed; this is a design-time trace):

1. **Device A and Device B both edit the same draft while OneDrive is offline/delayed, then
   reconnect.** Each device's local SQLite save proceeds unaffected (§H) and bumps that device's
   own `Revision` for that date. When OneDrive eventually delivers both files, the sync engine sees
   two envelopes for `EntityId = 2026-09-09` with different `DeviceId`s and diverging content from
   a common base revision — the exact concurrent-edit case in §G's Drafts rule. Neither is
   discarded; the user is asked to choose (or a later phase's field-merge applies where safe).

2. **Device A finalizes; Device B receives the finalized record later.** Device A's local
   `finalized_passdowns` insert is unaffected by sync. The sync engine writes `history/2026-09-09.json`
   + `.pdf` once, after the local commit succeeds (never before — a sync write must never be the
   thing that makes finalization "real"). Device B, whenever it next reads the sync folder (online
   immediately, or after being offline for days — see next scenario), sees a new `EntityId` it does
   not have locally and inserts it via the same insert-only path `SqliteFinalizedPassdownRepository.AddAsync`
   already uses — no schema change needed for the "receive a finalized record" direction, only a new
   caller.

3. **Device B was offline for several days.** Nothing time-sensitive breaks: every sync file is
   self-describing (its own `EntityId`, `Revision`, `ModifiedUtc`) and folder-scanned, not delivered
   as a change-log Device B must have been listening to continuously. A multi-day gap is
   indistinguishable, from the sync engine's point of view, from Device B simply never having synced
   before (§M) — it is a full reconciliation against everything currently in the folder either way.

The design converges in every scenario **without ever replacing a device's local database wholesale**
— each device only ever applies individual entity-level inserts/updates driven by envelope
comparisons, matching how `SqliteAssociateDraftRepository`/`SqliteOperationalNoteRepository`/
`SqliteFinalizedPassdownRepository` already expose per-entity operations, never a bulk-replace API.

## M. Existing-data bootstrap

Both directions were audited, since the phase prompt specifically calls out not assuming an empty
second database:

- **First device to enable sync, already has months/years of local data.** On enabling, walk every
  local `associate_drafts` row (`ListSavedDraftDatesAsync` already exists for exactly this kind of
  full enumeration, `AssociateDraftPersistence.cs:29`), every `operational_notes` row (`ListAsync`),
  and every `finalized_passdowns` row (`ListAsync` — deliberately excludes the PDF blob per row,
  §B, so a full-history bootstrap listing stays cheap; `GetByDateAsync` is then called once per
  record that actually needs to be written to `history/`), and write one sync file per record with
  `Revision = 1` and this device's `DeviceId`. This is a pure export — no local data is modified,
  matching CLAUDE.md's persistence invariant that manual/local writes must never be perturbed by
  this kind of operation.
- **Second device enables sync and also already has months/years of local, unrelated-to-Device-A
  data.** This is the harder, correctly-flagged-as-nontrivial case: Device B's existing drafts for
  dates Device A never touched are pure gains (straightforward two-way insert), but any date, note,
  or finalized record that exists **on both devices independently** (e.g. both devices already had
  a draft for 2026-09-09 before sync ever existed) must go through the exact same conflict
  evaluation as §G's live-editing case — bootstrap is not exempt from conflict detection just
  because it's a one-time event. Concretely: Device B's bootstrap treats every one of its own local
  records as if it had just been edited at `Revision = 1` under its own `DeviceId`, and reconciles
  against whatever Device A already wrote to the sync folder using the identical rules in §G (byte-
  identical after canonicalization ⇒ silent converge; differs ⇒ surfaced conflict, never
  auto-picked). No special "bootstrap mode" logic is needed beyond seeding each local record's
  starting revision — the same conflict resolver handles both "actively editing" and "just turned
  sync on" uniformly.

## N. Future phase boundaries

The prompt's proposed 16B–16G split held up against everything the audit found; no restructuring
needed. Refined with what this audit actually surfaced:

- **16B** — Settings/Personal Sync folder picker + OneDrive-env-var suggestion (§K); DeviceId
  generation/storage (§C); the sync engine skeleton (folder layout §E, atomic writes §F, manifest);
  no entity sync wired yet — proves the plumbing (write/read a file safely) in isolation.
- **16C** — Associate drafts + Operational Notes, one-way-safe both directions, **conflict
  detection only** (flag, don't auto-resolve) — the floor behavior from §G, no merge UI yet.
- **16D** — Conflict *resolution* UX for drafts/notes (the "Keep mine / Use theirs" surface hinted
  at in §G) + the tombstone/deletion model (§G) — this is also the natural place to revisit whether
  local draft persistence needs field-level granularity, since whole-blob replace (§B) is the
  current ceiling on how smart 16D's merge can ever be without a local schema change.
- **16E** — Finalized History + frozen PDFs, using the insert-only/dedupe-by-content-hash rule from
  §G (the simplest of the three conflict models, saved for after the harder two are proven).
- **16F** — Automatic background sync + status UX (sync folder health, last-synced indicator,
  pending-conflict badge).
- **16G** — Multi-device resilience hardening + release: the offline-for-days case (§L.3), bootstrap
  on a second non-empty device (§M), and whatever conflict-UX rough edges 16D/16E's real usage
  surfaces.

## Unresolved product decisions requiring owner input

These are genuine product/UX calls, not something derivable from the current spec or schema — the
kind CLAUDE.md reserves for the product owner:

1. **Draft conflict UX**: when the same date was edited on two devices, is "keep mine, offer
   theirs as a labeled alternate" (§G) the right first-release behavior, or should the app attempt
   an automatic field-level merge sooner (e.g. "attendance numbers differ, but nothing else does —
   auto-merge those two fields") even though that requires a local schema change beyond 16A's
   scope?
2. **Dual-finalization conflict UX**: if two devices both finalize the same date (§G), should the
   app surface both PDFs to the user for a manual pick, or hard-fail sync for that date until a
   human intervenes? Both are internally consistent with "never auto-resolve History"; which is
   less disruptive to real day-to-day use is a judgment call.
3. **Manager drafts**: `manager_drafts` (Migration0007) exists in the schema but is dead code today
   (Manager workflow deferred per CLAUDE.md). Should 16B–16G's design silently extend to it once/if
   the Manager workflow ships, or does Manager data need its own sync-scope decision at that time?

## Schema impact

None. No migration was added or changed in 16A; DB schema remains 11, Draft schema remains 3,
Snapshot schema remains 1. Every identity, envelope, and folder-layout decision above reuses
existing columns/keys.

## Code changes

None. This phase produced documentation only, per the phase instructions' preference for
design-only proof over a functioning prototype — every design question above (identity reuse,
envelope shape, conflict rules) was answerable by reading the existing schema/serializers directly,
with no ambiguity that would have required executable code to resolve.

---

# Phase 16B — Personal Sync: Local Foundation (Implementation)

Implements the reusable plumbing 16A designed: DeviceId, local sync configuration, OneDrive
suggestion, folder validation, the deterministic path/envelope/manifest contracts, atomic file
writes, a safe reader, and a small Settings screen. **No entity is synced** — `drafts/`, `notes/`,
and `history/` files are never written or read by anything in this phase; only `manifest.json` is
ever created, and only once sync is explicitly enabled with a folder that already validated. All new
code lives under `PersonalSync/` in Application, Infrastructure, and Desktop.

## 16B.1 DeviceId

[`IDeviceIdProvider`](../src/Carowinds.Passdown.Application/PersonalSync/IDeviceIdProvider.cs) /
[`FileDeviceIdProvider`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/FileDeviceIdProvider.cs).
One plain-text file, `device-id.txt`, holding a `Guid.ToString("D")`, directly under
`AppPaths.LocalApplicationDataDirectory` (flat, no `Settings/` subfolder — see "Deviations" below).
`GetOrCreateDeviceId()` reads and `Guid.TryParse`s the file; a missing file or unparsable content
falls through to generating a fresh `Guid.NewGuid()` and persisting it via the same temp-then-rename
write `FileDeviceIdProvider` uses internally. (Phase 18A REL-006: a read failure on a file that
*exists* — `IOException`/`UnauthorizedAccessException`, e.g. a scanner holding it — now propagates as
a retryable `IOException` and never replaces the existing id; see `docs/phase18-comprehensive-audit.md`.) Lazily created on first call — nothing calls this at normal startup unless the Settings
screen (or, from 16C on, the sync engine) is actually resolved, matching 16A §C's "not at first app
launch" intent closely enough that a device that never opens Settings never gets a DeviceId file.

## 16B.2 Sync configuration

[`SyncConfiguration`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConfiguration.cs) /
[`FileSyncConfigurationStore`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/FileSyncConfigurationStore.cs).
`{ PersonalSyncEnabled: bool, SyncRootPath: string? }` as one small JSON file, `sync-config.json`,
same flat-file convention as DeviceId. `LoadAsync` never throws — a missing, malformed, or unreadable
file all return `SyncConfiguration.Disabled`, so a corrupt local settings file can never block normal
app startup. No credential, Microsoft account identifier, or token field exists on this type by
construction (verified by a test that greps the written JSON for "Token"/"Password"/"Credential").
Writes go through `AtomicFileWriter` (§16B.10).

## 16B.3 OneDrive suggestion

[`IOneDriveLocationSuggestionProvider`](../src/Carowinds.Passdown.Application/PersonalSync/IOneDriveLocationSuggestionProvider.cs) /
[`EnvironmentOneDriveLocationSuggestionProvider`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/EnvironmentOneDriveLocationSuggestionProvider.cs).
Reads `OneDrive`, `OneDriveConsumer`, and `OneDriveCommercial` via
`Environment.GetEnvironmentVariable`, returning the distinct, non-empty values found (order:
`OneDrive`, then `OneDriveConsumer`, then `OneDriveCommercial`). No variable set is not an error —
returns an empty list. Never hardcodes a path. `Environment.GetEnvironmentVariable` is portable, so
this is directly unit-tested on this (non-Windows) development host by setting/clearing the same
variable names — no Windows-only API is involved, and no seam/abstraction beyond the interface was
needed for testability.

## 16B.4/§14 Settings UI

New "Settings" top-level app-shell section (`AppSection.Settings`, last in the nav strip), backed by
[`PersonalSyncSettingsViewModel`](../src/Carowinds.Passdown.Desktop/ViewModels/PersonalSyncSettingsViewModel.cs)
and [`PersonalSyncSettingsView.axaml`](../src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml).
Controls, exactly per spec §4: an "Enable Personal Sync" checkbox (two-way bound, persists
immediately on toggle), the configured folder path (or "No folder chosen yet."), a "Choose Folder…"
button, and a status chip. A device-ID panel is also shown (read-only, for support/troubleshooting
use — matches 16A §C's "never displayed as an account identity" by only ever showing the bare GUID).
No "Last sync time"/"Sync now"/conflict count/device list — those are explicitly out of scope per
spec §14. The ViewModel loads eagerly in its constructor (`_pendingLoad = LoadAsync()`), mirroring
`OperationsDashboardViewModel`'s own fire-and-forget-with-awaitable-`PendingLoad` pattern so tests can
await it deterministically instead of racing it. `IPersonalSyncSettingsService` (below) is the one
seam between this ViewModel and everything else; `ISyncFolderPicker`/`AvaloniaSyncFolderPicker`
mirrors `IPdfSaveFilePicker`'s existing ViewModel/Avalonia split for the native folder picker
(`IStorageProvider.OpenFolderPickerAsync`).

Folder-picker behavior itself (the native OS dialog) is **NOT EXECUTED** — no Windows host is
available this session, and headless Avalonia has no real storage-provider backend to drive. Every
other piece of this screen (load, toggle, status computation, folder-chosen callback) is covered by
ViewModel-level tests against a `FakeSyncFolderPicker`/`FakePersonalSyncSettingsService`, per spec
§16's "use focused ViewModel/service tests ... mark Windows-only picker behavior NOT EXECUTED."

## 16B.5–6 Selected-root semantics and folder validation

[`SyncPathService.GetSyncFolderPath`](../src/Carowinds.Passdown.Application/PersonalSync/SyncPathService.cs)
is the one place `<selected>/Carowinds Operations Sync/` is constructed — every other path in that
type, and every filesystem write in this phase, derives from it, so no ViewModel or service builds
this path independently.

[`ISyncFolderValidator`](../src/Carowinds.Passdown.Application/PersonalSync/SyncFolderValidationResult.cs) /
[`SyncFolderValidator`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/SyncFolderValidator.cs).
Two outcomes beyond `Valid`, deliberately distinguished:

- **Invalid** (structural, will not resolve itself): an empty/unparsable path, or a path that
  resolves inside `AppPaths.LocalApplicationDataDirectory` — checked as an exact match, a descendant
  (covers both "is the DB's directory" and "is the DB file itself," since the DB always lives directly
  inside that directory), or an ancestor (covers selecting a parent of the app's local data folder).
  Case-insensitive comparison on Windows, case-sensitive elsewhere.
- **Unavailable** (may resolve itself without picking a different folder): `Directory.CreateDirectory`
  or a write-then-delete probe file inside the owned subfolder throws `IOException`/
  `UnauthorizedAccessException`.

Validation only ever creates the app-owned subfolder and a transient probe file it immediately
deletes — it never touches `drafts/`, `notes/`, or `history/`, and never writes `manifest.json`
itself (that is `SyncManifestService`'s job, only called once validation already returned `Valid`).

## 16B.7 Sync path service

[`SyncPathService`](../src/Carowinds.Passdown.Application/PersonalSync/SyncPathService.cs) — pure
string composition, no filesystem access, matching 16A §E exactly:

```
<selected>/Carowinds Operations Sync/
    manifest.json
    drafts/<yyyy-MM-dd>.json          # DateOnly, invariant culture
    notes/<guid>.json                 # Guid.ToString("D"), lowercase-hyphenated
    history/<yyyy-MM-dd>.json
    history/<yyyy-MM-dd>.pdf
```

Both date and GUID formats are fixed/invariant regardless of host locale. Since every filename is
derived from a typed `DateOnly`/`Guid` rather than free text, there is no path-traversal surface to
sanitize against. Exact-mapping tests exist for every path (`SyncPathServiceTests`).

## 16B.8–9 Sync envelope and serialization

[`SyncEnvelope`](../src/Carowinds.Passdown.Application/PersonalSync/SyncEnvelope.cs) implements 16A
§D's contract field-for-field (`SyncSchemaVersion`, `EntityType`, `EntityId`, `DeviceId`, `Revision`,
`ModifiedUtc`, `IsDeleted`, `PayloadSchemaVersion`, `Payload`, `ContentHash`), with `required` members
so a missing field fails deserialization loudly (`SyncEnvelopeSerializer` throws `JsonException`,
turned into a structured `Malformed` result by the safe reader once something actually calls it —
see §16B.11). `Payload` is `System.Text.Json.JsonElement`, kept fully opaque — the envelope layer
never interprets or re-shapes it. `SyncEnvelopeSerializer.CurrentSyncSchemaVersion = 1`.

**16B implements this contract and its round-trip serialization only — no envelope is ever written to
or read from a sync-folder file yet.** `drafts/`, `notes/`, and `history/` stay untouched; wiring a
real entity through this envelope onto disk is 16C's job.

`SyncEnvelopeSerializer`'s JSON options mirror `FinalizedPassdownSnapshotSerializer` exactly
(`WriteIndented: true` for inspectability, `PropertyNamingPolicy: null` so property names stay
Pascal-case, `JsonStringEnumConverter` so `EntityType` serializes as a name, not an integer).

## 16B.11 Safe reader

[`SyncFileReadResult<T>`/`SyncFileReadStatus`](../src/Carowinds.Passdown.Application/PersonalSync/SyncFileReadResult.cs)
(Application, pure) / [`SyncFileReader`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/SyncFileReader.cs)
(Infrastructure, does the I/O). One generic method,
`ReadJsonAsync<T>(path, options, isSupportedVersion, ct)`, used today only for `SyncManifest`
(§16B.12) but written to be reusable for any versioned sync-folder file from 16C on. Five outcomes,
never a raw exception:

| Status | Cause |
|---|---|
| `Success` | File exists, parses, and `isSupportedVersion` accepts it. |
| `Missing` | `File.Exists` is false. |
| `Malformed` | `JsonException` during deserialize, or deserialized to `null`. |
| `UnsupportedVersion` | Parsed successfully but `isSupportedVersion` rejects it (a newer schema version than this app understands). |
| `IoError` | `IOException`/`UnauthorizedAccessException` while reading (locked file, permission denied, etc.). |

No corrupt file is quarantined or deleted — recovery UX is explicitly deferred (16D+ per spec §11).

## 16B.12 Manifest

[`SyncManifest`/`SyncManifestSerializer`](../src/Carowinds.Passdown.Application/PersonalSync/SyncManifest.cs)
(Application, the shape) / [`SyncManifestService`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/SyncManifestService.cs)
(Infrastructure, `EnsureManifestAsync`). Fields: `SyncSchemaVersion`, `Product` (constant string
`"Carowinds Passdown Personal Sync"`), `CreatedUtc`. `EnsureManifestAsync`:

- **Missing** → writes a new manifest (current schema version, current UTC from `IClock`) via
  `AtomicFileWriter`, reports `Ready`.
- **Success** (existing, version ≤ current) → reports `Ready`, does **not** rewrite the file.
- **Malformed** → reports `Malformed`, leaves the file untouched (never auto-repaired).
- **UnsupportedVersion** (existing, version > current) → reports `UnsupportedVersion`, leaves the
  file untouched.
- **IoError** (from the read, or from the write attempt on the Missing path) → reports `Unavailable`.

Only ever called by `PersonalSyncSettingsService` after `ISyncFolderValidator` already returned
`Valid` for an *enabled* configuration — never during a disabled-sync or not-yet-configured load,
per spec §12's "not during normal app startup with sync disabled."

## 16B.10 Atomic file writer

[`AtomicFileWriter`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/AtomicFileWriter.cs) —
write-temp-then-rename exactly per 16A §F: the temp file (`<name>.tmp-<32-hex-guid>`) is created in
the same directory as the final target (parent directory created first if needed), written in full,
then `File.Move(tempPath, path, overwrite: true)`. A `try/catch` around the whole write-then-move
deletes the temp file on any failure (including cancellation — `OperationCanceledException` is caught
like any other exception here) before rethrowing, so a failed or cancelled write never leaves an
orphan temp file *from that attempt*; a stale temp file left behind by a different, already-crashed
process is simply ignored by both the writer (each attempt uses its own random suffix) and the reader
(which only ever looks at the exact final filename). Both `WriteTextAsync` (used by every 16B caller)
and `WriteBytesAsync` (unused in 16B; kept for 16E's PDF sibling writes) are exposed. Used today by
`FileSyncConfigurationStore`, `FileDeviceIdProvider` (its own small inline variant, string content
only — see Deviations), and `SyncManifestService`.

## Privacy verification

Grepped every field on `SyncConfiguration`, `SyncManifest`, and `SyncEnvelope`, and the settings
screen's own display code: no username, machine name, email, OneDrive account identity, credential,
token, or hardware identifier is written or shown anywhere. `DeviceId` is the only device identifier,
a bare `Guid`, shown on the Settings screen only as itself (no reverse-lookup metadata attached). A
dedicated test (`FileSyncConfigurationStoreTests.SaveAsync_DoesNotStoreAnyCredentialLikeField`) greps
the actual written JSON for "Token"/"Password"/"Credential" as a standing regression guard.

## Deviations from Phase 16A

- **Flat files, not `Settings/device-id.txt`**: 16A §C's illustrative path used a `Settings/`
  subfolder; this phase instead put `device-id.txt` and `sync-config.json` directly under
  `AppPaths.LocalApplicationDataDirectory`, matching the existing precedent
  (`FileScheduledRefreshLock.DefaultPath` is already a flat file there, not nested). No product
  behavior differs — this is a same-directory naming choice, not a design change.
- **No `ISyncManifestService` interface / no `IAtomicFileWriter` interface**: both are `static`
  utility classes rather than DI-injected ports. Nothing in 16B (or, as far as this phase can see,
  16C+) needs to substitute a different implementation of "write a temp file then rename it" or
  "create/validate manifest.json" — they're pure filesystem utilities exercised directly against a
  real temp directory in tests (the same pattern `FileScheduledRefreshLockTests` already established
  for `FileScheduledRefreshLock`), not seams a ViewModel test needs to fake.
- **(Superseded in Phase 18B)** `FileDeviceIdProvider` now uses `AtomicFileWriter.WriteText` (a
  synchronous entry point over the same durable implementation). Original 16B note:
  **`FileDeviceIdProvider` does not call `AtomicFileWriter`**: `IDeviceIdProvider.GetOrCreateDeviceId()`
  is synchronous by design (a settings-screen-open-time read, not something worth an async ceremony
  for), while `AtomicFileWriter` is async throughout; rather than block on async (`.GetAwaiter().GetResult()`)
  to reuse it, `FileDeviceIdProvider` inlines the same three-line temp-then-rename shape directly with
  synchronous `File` calls. The duplication is small (write-temp, `File.Move(overwrite: true)`) and
  deliberate, not an oversight.
- **`PersonalSyncSettingsService` composes rather than exposing each piece separately to the UI**:
  16A didn't specify a settings-orchestration service; one was added (`IPersonalSyncSettingsService`)
  so the ViewModel has exactly one dependency to call instead of five, and so the "only touch the
  manifest when enabled+valid" rule (§12) lives in one place instead of being the ViewModel's
  responsibility to get right.

## Platform limitations — NOT EXECUTED

No Windows 11 host or VM was available this session (same limitation as every prior rebuild since
Phase 7A). **NOT EXECUTED**: the native Avalonia folder-picker dialog itself, and any interaction
between this app's folder validation and a real, running Windows OneDrive desktop client (e.g.
observing an actual "Available offline"/"Files On-Demand" placeholder file, or a folder mid-sync).
`EnvironmentOneDriveLocationSuggestionProvider`'s environment-variable reads are platform-portable and
were exercised directly; only the "does a real OneDrive installation actually set these variables to
a valid, syncing folder" half is unverified. All filesystem primitives (`AtomicFileWriter`,
`SyncFileReader`, `SyncFolderValidator`) were exercised against a real temp directory on this
(macOS/Darwin) development host, including a Unix-permission-based unwritable-folder test
(`SyncFolderValidatorTests.ValidateAsync_UnwritableFolder_ReturnsUnavailable_NotInvalid`) that is
itself skipped on Windows, where the same behavior will need re-verification via an ACL-denied folder
instead — recorded as a Windows-validation follow-up, not fabricated as passing.

## Schema impact

None. No SQLite migration was added; DB schema remains 11, Draft schema remains 3, Snapshot schema
remains 1 (confirmed: `Migration0011CreateDeepRefreshStatusTable` is still the last migration; no
`Migration0012` exists). Every 16B state (DeviceId, sync configuration) lives in flat local files
under `AppPaths.LocalApplicationDataDirectory`, per CLAUDE.md's "prefer no SQLite schema change" and
16A §C/§K's own design.

## Tests added (Phase 16B)

`SyncPathServiceTests`, `SyncEnvelopeSerializerTests` (Application.Tests/PersonalSync);
`AtomicFileWriterTests`, `SyncFileReaderTests`, `FileDeviceIdProviderTests`,
`FileSyncConfigurationStoreTests`, `EnvironmentOneDriveLocationSuggestionProviderTests`,
`SyncFolderValidatorTests`, `SyncManifestServiceTests`, `PersonalSyncSettingsServiceTests`
(Infrastructure.Tests/PersonalSync); `PersonalSyncSettingsViewModelTests`
(Ui.Tests/PersonalSync); plus one `MainViewModelNavigationTests` addition for the new Settings
section and two existing-test updates for the section count changing from 5 to 6
(`AppSection_ContainsOperationsCalendarAndOperationalNotesAndSettingsAndNoManagerOrAlerts`,
`Phase15ERealWindowLayoutTests.MainWindow_SectionStrip_EveryButtonReachableWithinWindowBounds`).
71 new tests total.

## Issues deferred to 16C/16D

- Actual entity export/import (`drafts/`, `notes/`, `history/` files) — 16C.
- Conflict detection/resolution UX (16A §G) — 16C (detection) / 16D (resolution).
- Tombstone/deletion propagation (16A §G) — 16D.
- Bootstrap for a device with existing local data (16A §M) — 16C/16G.
- Real Windows-host validation of the folder picker and OneDrive interaction — first Windows session
  with this branch available.

## Phase 16C readiness

Every piece 16C needs already exists and is tested in isolation: `SyncPathService` for exact record
paths, `SyncEnvelope`/`SyncEnvelopeSerializer` for the wrapper shape, `AtomicFileWriter`/
`SyncFileReader` for safe writes/reads, and `IPersonalSyncSettingsService` for "is sync currently
enabled and pointed at a valid, manifest-initialized folder" — the one question 16C's entity-sync
engine needs answered before it does anything. No blocking gap identified.

---

# Phase 16C — Personal Sync: Associate Drafts and Operational Notes (Implementation)

Implements the first real record-level sync on top of 16B's foundation: Associate drafts and
Operational Notes now move between two devices through the shared sync folder, with genuine
concurrent-edit conflicts detected and never silently resolved. Finalized History/PDFs, deletions/
tombstones, and conflict-resolution UX all remain out of scope (see "Deferred to 16D" below).

## 16C.1 Sync metadata / revision model

New Application-layer type, [`SyncEntityState`](../src/Carowinds.Passdown.Application/PersonalSync/SyncEntityState.cs),
persisted by [`ISyncEntityStateRepository`](../src/Carowinds.Passdown.Application/PersonalSync/SyncEntityState.cs)
(SQLite implementation:
[`SqliteSyncEntityStateRepository`](../src/Carowinds.Passdown.Infrastructure/Storage/Repositories/SqliteSyncEntityStateRepository.cs)).
One generic table, keyed by `(EntityType, EntityId)`, instead of adding sync columns to
`associate_drafts`/`operational_notes` (spec's explicit preference). Fields and exact semantics:

| Field | Meaning |
|---|---|
| `LocalRevision` | A local, monotonically increasing counter bumped once per local save of this entity (via `ISyncChangeTracker`) or once per applied remote update. This is the integer written into an exported envelope's `Revision` — never a timestamp, never reset. |
| `SyncedRevision` / `SyncedContentHash` | The `LocalRevision` and canonical content hash as of the last point this device confirmed its local content matched the sync folder — either because it just exported that exact content, or because it just imported/converged on a remote update. `SyncedRevision = 0` and `SyncedContentHash = null` together mean "never synced" — the bootstrap-candidate state. |
| `LastRemoteDeviceId` / `LastRemoteRevision` | The envelope identity of the last remote change this device successfully incorporated (applied or found identical) — lets a re-scan of an unchanged file recognize it as already-seen without re-comparing content. |
| `ConflictState` / `ConflictRemoteDeviceId` / `ConflictRemoteRevision` / `ConflictRemoteContentHash` / `ConflictDetectedAtUtc` | Set only when an incoming remote envelope's content differs from *both* the current local content and `SyncedContentHash` — a genuine concurrent edit (§16C.6). Left untouched (never advanced) while conflicting, so the same unresolved conflict is consistently re-detected on every pass rather than silently dropped or auto-resolved. Never carries the conflicting payload itself, only enough identity/fingerprint to describe it. |

**Why not `ModifiedUtc` alone**: two Windows machines' clocks are never asserted to be in sync
anywhere in this codebase (16A §G). `LocalRevision`/`SyncedRevision`/content-hash comparison gives a
real, clock-independent answer to "has this device's copy diverged since it last agreed with the
sync folder" — the exact question 16A §G identified as needing more than a timestamp.

**Why content hash is still the actual source of truth for dirtiness/conflict, not `LocalRevision`
alone**: `LocalRevision` only needs to be *some* meaningful monotonic integer for the envelope's
`Revision` field (16A §D's contract). The real dirty/conflict decision always compares canonical
content hashes (`SyncContentHash`, below) against `SyncedContentHash`. This means even a save that
bypasses `ISyncChangeTracker` (e.g. a direct repository write in a test) cannot cause data to go
permanently unsynced — the next sync pass's hash comparison still catches the real divergence; only
the envelope's own `Revision` number would undercount that edit, a cosmetic gap, not a correctness
one.

## 16C.2 Database migration

[`Migration0012CreateSyncEntityStateTable`](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0012CreateSyncEntityStateTable.cs) —
DB schema **11 → 12**. Purely additive: one new table, `sync_entity_state`, primary-keyed on
`(entity_type, entity_id)`. Draft schema remains 3; Snapshot schema remains 1. No existing table
(`associate_drafts`, `operational_notes`, `finalized_passdowns`, caches) is altered. Verified against
an existing DB 11 with pre-existing draft/note/History rows
(`PortableLifecycleTests.Schema11ToSchema12_CreatesSyncEntityStateTable_AndPreservesExistingDraftNoteAndHistoryData`)
and against the legacy-app-data-folder-name compatibility path
(`Phase13GAppDataRenameCompatibilityTests`) — both now target DB 12. A fresh database also
initializes correctly straight to schema 12 (`SqliteRepositoryTestFixture`, used by every repository
test in this phase).

## 16C.3 Entity types and centralized identity

`SyncEntityType` (already defined in 16B: `AssociateDraft`, `OperationalNote`, `FinalizedPassdown`)
remains the one enum — no free-form entity-type strings anywhere in 16C's code. New
[`SyncEntityIdentity`](../src/Carowinds.Passdown.Application/PersonalSync/SyncEntityIdentity.cs)
centralizes the one mapping between an entity's own domain identity and its `SyncEnvelope.EntityId`
string:

- `SyncEntityIdentity.ForDraft(DateOnly)` / `TryParseDraftId(string, out DateOnly)` — invariant
  `yyyy-MM-dd`, never timezone-converted (16C §18).
- `SyncEntityIdentity.ForNote(Guid)` / `TryParseNoteId(string, out Guid)` — lowercase-hyphenated
  `"D"` format, matching `SyncPathService`'s own filename convention (16C §19).

An envelope whose `EntityType` doesn't match the folder it was found in, or whose `EntityId` doesn't
match its own filename, is rejected as malformed rather than trusted (see §16C.7) — this is the
"unknown/mismatched entity type rejected safely" requirement in practice, since 16C only ever scans
`drafts/` and `notes/` (never `history/`, where a `FinalizedPassdown`-typed envelope would live).

## 16C.4 Draft export/import and the public-data boundary

**Payload**: reuses `AssociateDraftPayload`/`AssociateDraftPayloadSerializer` unchanged —
`PayloadSchemaVersion` in the envelope is `AssociateDraftPayload.CurrentSchemaVersion` (3 today). The
exported envelope's `Payload` is written with the exact same (default, non-string-enum) JSON options
`AssociatePassdownService.SaveDraftAsync` already uses for `payload_json`, so
`AssociateDraftPayloadSerializer.Deserialize` (including its legacy-schema migration-on-read logic)
can read an imported envelope's payload with zero special-casing — one deserializer, one migration
path, for local storage and sync alike.

**Public-data boundary (spec §21/§22, audited carefully)**: `AssociateDraftPayload` is already,
by construction, the *only* thing persisted for a draft — no retrieved/cached catalog or dated-result
data has ever lived inside it (16A §A/§J's own audit already established this before 16C existed). 16C
does not change what gets serialized into `payload_json`; it only wraps that exact same, already-pure
payload in a `SyncEnvelope`. There is no code path in `PersonalSyncService` that reads from
`IDatedResultRepository`/`ICatalogProductRepository` at all — confirmed by inspection of
[`PersonalSyncService.cs`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/PersonalSyncService.cs),
which only depends on `IAssociateDraftRepository`, `IOperationalNoteRepository`,
`ISyncEntityStateRepository`, `IPersonalSyncSettingsService`, `IDeviceIdProvider`, and `IClock`.

**Export** (`ExportDraftsAsync`): for every locally-saved draft date
(`IAssociateDraftRepository.ListSavedDraftDatesAsync`), skip if its sync state is `Conflicting`
(§16C.6); otherwise compute the canonical content hash and compare to `SyncedContentHash` — write
`drafts/<yyyy-MM-dd>.json` via `AtomicFileWriter` only if they differ, then update
`LocalRevision`/`SyncedRevision`/`SyncedContentHash` together. This naturally implements bootstrap
(§16C.9): an entity with no prior `SyncEntityState` row has `SyncedContentHash = null`, which never
equals any real hash, so it is always exported the first time a sync pass sees it.

**Import** (`ImportDraftsAsync` → `ProcessDraftEnvelopeAsync`): for every file in `drafts/` whose
filename parses as an exact `yyyy-MM-dd.json` (§16C.7), read and validate the envelope, then delegate
the actual accept/reject decision to the pure `SyncReconciliation.Decide` function (§16C.6). Applying
an import writes the full `AssociateDraft` row (`operating_date`, `schema_version`, `payload_json`,
`updated_at_utc = envelope.ModifiedUtc`) via the existing `IAssociateDraftRepository.SaveAsync` upsert
— no new persistence path for drafts, no bulk-replace API, one entity at a time (16A §L's own
constraint).

## 16C.5 Note export/import and the note sync payload

Operational Notes had no independent JSON payload before this phase (16A §D). New
[`OperationalNoteSyncPayload`](../src/Carowinds.Passdown.Application/PersonalSync/OperationalNoteSyncPayload.cs) /
`OperationalNoteSyncPayloadSerializer` — schema version **1** — carries every user-authored lifecycle
field needed to reproduce the same note on another device: `Id`, `Title`, `Body`, `Category`,
`Priority`, `EffectiveFrom`, `EffectiveThrough`, `Status` (Active/Resolved), and all three timestamps.
No deletion field exists because the domain itself has no delete path (16A §B/§G) — "resolved" is
itself just a normal field under this payload, synced like any other (spec §20's "sync that state
normally, do not invent deletion behavior").

Export/import mirror drafts exactly (`ExportNotesAsync`/`ImportNotesAsync` →
`ProcessNoteEnvelopeAsync`), using `IOperationalNoteRepository.AddAsync` for a genuinely new remote
note (identity preserved exactly — the imported `Guid` is never replaced, spec §19) and `UpdateAsync`
for a safe remote update to an existing local note. Selected-note copies embedded inside a draft
(`AssociateOperationalNoteSelection`) are not synced as independent entities — spec §19 is explicit
that they are part of the draft payload, and 16C's draft export already carries them as-is inside
`AssociateDraftPayload.SelectedOperationalNotes`.

## 16C.6 Content hashing and conflict detection

[`SyncContentHash`](../src/Carowinds.Passdown.Application/PersonalSync/SyncContentHash.cs) computes a
SHA-256 hash over a **fresh, compact, fixed-option re-serialization** of the strongly-typed payload —
never the raw file/column text. This matters concretely: an envelope file is written with
`WriteIndented: true` (16B §9), so hashing `envelope.Payload.GetRawText()` directly would differ from
hashing the same content as stored compactly in `payload_json`, even though the data is identical.
Both the local and remote side of every comparison go through the same
`AssociateDraftPayloadSerializer.Deserialize` (drafts) or `OperationalNoteSyncPayload.FromDomain`
(notes) → `SyncContentHash.Compute*Hash` pipeline, so formatting differences and legacy-schema
migration are both normalized away before comparison — sync metadata itself never affects the hash.

[`SyncReconciliation.Decide`](../src/Carowinds.Passdown.Application/PersonalSync/SyncReconciliation.cs)
is the one pure decision function (no filesystem, no repository access) behind every import:

```
if envelope.DeviceId == our own device      -> Ignored (our own file, read back)
if envelope already incorporated            -> Ignored (idempotent re-scan)
if local entity does not exist              -> RemoteOnlyImport
if local hash == remote hash                -> Converged (byte-identical, nothing to apply)
if local hash == last-synced hash           -> SafeRemoteUpdate (local hasn't diverged — safe to apply)
otherwise                                   -> Conflict (never applied automatically)
```

The `Conflict` branch covers both the "steady-state concurrent edit" case (16A §G's worked example:
device B imported revision 4, edited locally, device A also produced revision 5) *and* the "first-ever
contact between two independently-created copies" bootstrap case (§16C.9) — one rule, not two, exactly
as the phase brief asked for ("no special bootstrap mode needed beyond seeding revision"). A conflict
never overwrites local data and never overwrites the remote file: the entity is simply excluded from
that pass's export step (§16C.4/§16C.5) until it resolves — either a future 16D merge, or the
contents happening to converge on their own, at which point the very next pass's `Converged` branch
clears the conflict automatically (no code path treats "identical after canonicalization" as a
conflict to begin with).

## 16C.7 Malformed files, unexpected filenames, and missing files

`PersonalSyncService` only ever enumerates `*.json` inside `drafts/`/`notes/` and only treats a file
as canonical if its filename (minus extension) parses exactly as `SyncEntityIdentity.TryParseDraftId`/
`TryParseNoteId` expects — an OneDrive conflict-copy filename like `2026-09-09-DESKTOP1.json` fails
that parse and is silently skipped, never imported as a duplicate entity and never deleted (§16C
requirement 28; verified by
`PersonalSyncServiceTests.RunAsync_UnexpectedOneDriveConflictCopyFilename_IsIgnored_NeverImportedOrDeleted`).
A canonical-named file that fails to parse as a `SyncEnvelope`, or whose `EntityType`/`EntityId`
doesn't match its own filename, is counted as `MalformedCount` and skipped — the rest of the pass
continues (verified by
`RunAsync_MalformedOneRecordAmongValidOnes_ProcessesTheOthersAndReportsMalformedCount`). A file that
was previously imported and later disappears from the folder is never treated as a deletion signal —
there is no "previously seen" list at all; each pass is a full, independent reconciliation against
whatever currently exists (verified by
`RunAsync_PreviouslySeenRemoteFileLaterDisappears_NeverDeletesOrTombstonesTheLocalNote`).

## 16C.8 Local change tracking (the "mark dirty" hook)

[`ISyncChangeTracker`](../src/Carowinds.Passdown.Application/PersonalSync/SyncChangeTracker.cs) is the
one hook every local save goes through: `AssociatePassdownService.SaveDraftAsync` and
`OperationalNotesService.CreateAsync`/`UpdateAsync`/`ResolveAsync` each call
`MarkLocalChangeAsync(entityType, entityId)` **after** their own repository write already succeeded —
never inside the same SQLite transaction, matching spec §7's "save SQLite first, sync metadata
second, never in the same write." Both constructors take `ISyncChangeTracker?` as an **optional**
trailing parameter defaulting to `null` (a no-op when absent) — this is what lets every pre-16C
caller/test that constructs these services directly keep working unmodified, and is exactly why local
save behavior with sync disabled is provably identical to Phase 15/16B (a `null` tracker performs zero
extra I/O). `OperationalNotesService.ResolveAsync` on an already-resolved note (an idempotent no-op)
correctly does **not** call the tracker again — no genuine change occurred, so no reason to bump the
revision.

## 16C.9 Bootstrap (first device, and a non-empty second device)

No separate "bootstrap mode" exists in the code — it falls out of the same algorithm used for every
sync pass (spec explicitly permits and expects this):

- **First device, already has months of local data, sync folder empty**: every local draft/note has
  no `SyncEntityState` row yet, so `SyncedContentHash` is `null`, which never matches any computed
  hash — the export step above (§16C.4/§16C.5) exports all of them, one file write + one metadata
  upsert per entity, fully resumable (an interruption after N of M entities leaves the first N marked
  synced and the rest still pending for the next run — never one giant transaction). Verified by
  `Bootstrap_FirstDeviceWithExistingLocalData_ExportsEverythingOnFirstSyncPass`.
- **Second device, already has its own independent local data**: for a date/note that exists only on
  one side, the normal `RemoteOnlyImport` (§16C.6) picks it up as a pure gain
  (`Bootstrap_SecondDeviceWithNonOverlappingExistingData_MergesBothWaysWithoutConflict`). For a
  date/note that exists **on both** devices independently (never having synced before): if the
  content happens to be identical, `Converged` fires and both sides silently agree
  (`Bootstrap_SecondDeviceWithIdenticalOverlappingData_ConvergesSilently_NoConflict`); if it differs,
  `Conflict` fires — neither side is overwritten, exactly as the steady-state conflict case
  (`Bootstrap_SecondDeviceWithOverlappingDivergentData_DetectsConflict_NeverAutoPicksAWinner`).

## 16C.10 `PersonalSyncService` orchestration and "Sync Now"

[`IPersonalSyncService`](../src/Carowinds.Passdown.Application/PersonalSync/IPersonalSyncService.cs) /
[`PersonalSyncService`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/PersonalSyncService.cs)
(Infrastructure — reuses `IPersonalSyncSettingsService` for readiness/folder/manifest checks rather
than re-implementing that logic). One `RunAsync()` call:

1. Acquire a local, in-process, non-blocking gate (`SemaphoreSlim(1,1).WaitAsync(0)`) — a second
   concurrent call in the same process returns `PersonalSyncOutcome.AlreadyRunning` immediately rather
   than queuing or racing (spec §27; deliberately *not* `IScheduledRefreshLock`, which exists to
   serialize retrieval refreshes, not sync passes — cross-device simultaneity is expected and handled
   by the revision/conflict logic above, not a shared lock).
2. `IPersonalSyncSettingsService.LoadAsync()` — if sync is off, not configured, or the folder/manifest
   isn't `Ready`, return the corresponding `PersonalSyncResult` immediately; nothing is touched.
3. Import drafts, then notes (§16C.4/§16C.5).
4. Export drafts, then notes.
5. Return a structured [`PersonalSyncResult`](../src/Carowinds.Passdown.Application/PersonalSync/IPersonalSyncService.cs):
   `ImportedCount`, `ExportedCount`, `UnchangedCount` (converged/self-authored/already-seen), the
   deliberately-never-silent `ConflictCount`, `MalformedCount`, `IoFailureCount`.

**UI (spec §25/§26)**: `PersonalSyncSettingsViewModel` gained one `SyncNowCommand`
(`CanExecute`: not busy and `Status == Ready`) and a `SyncResultSummary` string, rendered via new
[`PersonalSyncResultPresentation`](../src/Carowinds.Passdown.Desktop/Presentation/PersonalSyncResultPresentation.cs)
(e.g. `"Synced: 3 imported, 2 exported, 1 unchanged. 1 conflict detected — not resolved
automatically."`). No automatic/background cadence exists anywhere in 16C — the only trigger is this
one manual button. No initial-bootstrap-on-enable auto-run was added either (deliberately: the phase
brief marks it optional, and a user's first "Sync Now" click after enabling already performs the
exact same bootstrap export as any other pass — see §16C.9 — so no separate code path was needed).

## 16C.11 Idempotence

Verified directly (`ScenarioF_RepeatedSyncPassesWithNoChanges_AreIdempotent`,
`RunAsync_ImportingTheSameEnvelopeTwice_NeverDuplicatesOrInflatesRevision`): repeated import of an
unchanged remote file produces `Ignored`/`Converged` (never a duplicate row, never an inflated
`LocalRevision`), and repeated export of an unchanged local entity is skipped entirely once
`SyncedContentHash` already matches the current content — no file rewrite, no metadata churn.

## 16C.12 Failure / retry behavior

An I/O failure exporting one entity (simulated in tests by pre-creating a directory at the target file
path, forcing `AtomicFileWriter`'s rename-into-place to throw `IOException`) is caught, counted in
`IoFailureCount`, and leaves that entity's `SyncEntityState` untouched — since dirtiness is decided by
content-hash comparison, the entity is automatically retried (and this time succeeds) on the very next
pass, with no separate "retry queue" needed
(`RunAsync_IoFailureDuringOneExport_LeavesTheEntityDirtyForRetry_AndOtherExportsStillSucceed`). A
folder that is missing/offline/locked is reported via `PersonalSyncOutcome.FolderUnavailable`/
`ConfigurationError` without touching any local table — a local draft save made while the folder is
unavailable still succeeds normally
(`RunAsync_FolderUnavailable_ReportsFolderUnavailable_AndLocalDraftStillSaves`).

## 16C.13 Concurrent-sync protection

A `SemaphoreSlim(1, 1)` instance field on `PersonalSyncService` (registered as a DI singleton) blocks
a second concurrent `RunAsync()` call in the same process, returning `AlreadyRunning` rather than
queuing behind the first (spec §27). Deliberately not shared with `IScheduledRefreshLock` — that lock
protects retrieval refreshes on one machine; a second device syncing "at the same time" is not a race
this lock is meant to prevent, and is instead handled entirely by the revision/conflict logic above.

## 16C.14 Privacy verification

Inspected the actual JSON written to the shared sync folder across a full test pass containing both a
draft and a note (`ExportedSyncFiles_NeverContainMachineNameUsernameOrLocalPaths`): no occurrence of
`Environment.UserName`, `Environment.MachineName`, the local AppData-equivalent path, or the strings
"Token"/"Password"/"Credential" (case-insensitive), matching 16B's existing standing privacy guard for
`SyncConfiguration`/`SyncManifest`/`SyncEnvelope`. `DeviceId` remains the only device identifier, a
bare random `Guid`.

## 16C.15 Existing-user migration and fresh-DB result

- **Existing DB 11 upgrade**: `PortableLifecycleTests.Schema11ToSchema12_...` seeds a real DB 11 with
  a draft, a note, and a finalized/History row through the real repositories, upgrades it to DB 12,
  and confirms every pre-existing row is untouched and the new `sync_entity_state` table is empty and
  immediately usable. `Phase13GAppDataRenameCompatibilityTests` (legacy `%LOCALAPPDATA%` folder name)
  is likewise upgraded to and verified against DB 12.
- **Fresh DB**: every repository test in this phase (`SqliteRepositoryTestFixture`) runs against a
  brand-new database that migrates straight through to schema 12 in one pass; no separate "empty
  upgrade" code path exists to diverge from the "existing data" path.
- **Sync remains disabled unless previously configured**: the migration only ever creates the empty
  `sync_entity_state` table — it never reads or writes `SyncConfiguration`, so a device's
  enabled/disabled sync setting (16B, a flat file, not a DB row) is completely unaffected by this
  schema change.

## 16C.16 Two-device simulation results

[`PersonalSyncServiceTests`](../tests/Carowinds.Passdown.Infrastructure.Tests/PersonalSync/PersonalSyncServiceTests.cs)
implements the harness spec §31 calls for — `LocalStoreA`/`LocalStoreB` as two independent real SQLite
databases (via `CreateDevice`), `SharedSyncFolder` as one real temp directory both point at, no real
OneDrive client involved. All required scenarios pass:

| Scenario | Result |
|---|---|
| A. A creates draft → B imports | PASS |
| B. A creates note → B imports (Guid preserved) | PASS |
| C. Non-overlapping changes on two devices converge | PASS |
| D. Concurrent same-draft edit → conflict, neither side overwritten | PASS |
| E. Concurrent same-note edit → conflict, neither side overwritten | PASS |
| F. Repeated runs → stable/idempotent | PASS |
| G. Offline B reconnects later → picks up latest state directly | PASS |
| Bootstrap: first device, existing data | PASS |
| Bootstrap: second device, non-overlapping existing data | PASS |
| Bootstrap: second device, overlapping divergent data → conflict | PASS |
| Bootstrap: second device, overlapping identical data → converges | PASS |
| Sync disabled / folder unavailable | PASS |
| Malformed file among valid ones | PASS |
| I/O failure during export + retry | PASS |
| Duplicate/repeated import | PASS |
| OneDrive conflict-copy filename ignored | PASS |
| Missing previously-seen file ≠ deletion | PASS |
| Local edit after clean import, then remote advances → conflict | PASS |
| Privacy: no machine/user/path leakage | PASS |

20 tests, 20 passing.

## Tests added (Phase 16C)

- `SyncReconciliationTests`, `SyncContentHashTests`, `SyncEntityIdentityTests`,
  `SyncChangeTrackerTests` (Application.Tests/PersonalSync) — pure decision-logic and fingerprinting
  coverage, no filesystem or database.
- `PersonalSyncServiceTests` (Infrastructure.Tests/PersonalSync) — the two-device harness above.
- `SqliteSyncEntityStateRepositoryTests` (Infrastructure.Tests/Storage/Repositories).
- `Schema11ToSchema12_...` addition to `PortableLifecycleTests`; migration-list/version-constant
  updates to `Phase13GAppDataRenameCompatibilityTests` and `SqliteRepositoryTestFixture`.
- Sync-tracker-hook coverage added to `AssociatePassdownServiceTests` and `OperationalNotesServiceTests`
  (Application.Tests), including the "no tracker configured" backward-compatibility case.
- `PersonalSyncSettingsViewModelTests` additions for `SyncNowCommand` (Ui.Tests).
- `FakePersonalSyncService` (Ui.Tests/TestSupport).

Two pre-existing architectural-invariant tests were **updated, not removed**, to reflect the
sanctioned new `ISyncChangeTracker?` dependency: `OperationalNotesServiceTests.Constructor_...`
(Application.Tests) and `OperationalNotesServiceIntegrationTests.RealServiceConstructor_...`
(Ui.Tests) — both still assert no HTTP/PDF/refresh-orchestrator/Associate-draft dependency exists;
only the expected parameter count changed from 2 to 3.

## Final test count

1810/1810 deterministic (non-`LiveSmoke`) tests passing, run per-project
(`Domain.Tests` 105, `Application.Tests` 517, `Infrastructure.Tests` 449, `Documents.Tests` 114,
`Ui.Tests` 625). Up from the Phase 16B baseline of 1745/1745. `dotnet build CarowindsPassdown.slnx`:
0 warnings, 0 errors.

A pre-existing, previously-documented (since Phase 6D — see `docs/phase12-operations-calendar.md`,
`docs/release-strategy.md`) Avalonia headless cross-test parallelism flake was observed when running
the *entire* `Ui.Tests` project's 625 tests in one `dotnet test` invocation (a non-deterministic
subset of `RealWindowStartupTimingTests`/`Phase15ERealWindowLayoutTests`/`OperationsCalendarViewTests`
— real-`Window`-constructing tests unrelated to any Phase 16C file — intermittently throw "The calling
thread cannot access this object because a different thread owns it"). Per CLAUDE.md, this flake was
not "fixed" (out of scope, pre-existing). It was isolated instead: filtering to just those three test
classes passes cleanly and repeatably (38/38), and filtering everything else in `Ui.Tests` passes
cleanly and repeatably (587/587) — 587 + 38 = 625, the full project total, with zero real failures
attributable to this phase's changes. No test whose name contains "PersonalSync" ever appeared in any
flaky failure list across multiple repeated runs.

## Known limitations

- No manual merge/resolution UI for a detected conflict — 16D's job. A conflicted entity's own local
  copy remains fully usable; only that one entity's export is paused until it resolves.
- No automatic/background sync cadence — "Sync Now" is the only trigger.
- No deletion/tombstone propagation — the domain has no delete path for drafts or notes today, so
  there is nothing to propagate; a future local-delete feature will need a 16D-era tombstone design.
- Finalized History/PDFs are not synced.
- The real Windows-host OneDrive desktop client interaction (a live "Available offline"/Files-On-Demand
  placeholder, or a folder mid-upload) remains unverified on this (macOS/Darwin) development host —
  same limitation carried forward from 16B; all filesystem primitives were exercised against a real
  temp directory here, exactly as in 16B.
- The reconciliation model tracks only the single most-recently-incorporated remote device per entity
  (`LastRemoteDeviceId`/`LastRemoteRevision`) rather than a revision per `(EntityId, DeviceId)` pair —
  sufficient and correct for this phase's two-device personal-sync scope (a third device's envelope
  still falls through correctly to content-hash comparison, just without the "already seen" fast-path
  optimization; see `SyncReconciliationTests.Decide_NewRevisionFromADifferentRemoteDevice_...`). A
  later phase supporting more than two devices per person should revisit this if the lack of the
  fast-path ever becomes a measurable cost.

## Deferred to 16D

- Conflict *resolution* UX (Keep mine / Use theirs / field-level merge).
- Tombstone/deletion model and propagation.
- Revisiting whether local draft persistence needs field-level granularity (currently whole-blob
  replace, per `SqliteAssociateDraftRepository.SaveAsync` — unchanged by 16C).

## Phase 16D readiness

`SyncEntityState`'s conflict fields already carry everything a resolution UI needs to render "a draft
for this date was also edited on `<device>` at `<time>`" (spec's own worked example, 16A §G):
`ConflictRemoteDeviceId`, `ConflictRemoteRevision`, `ConflictRemoteContentHash`,
`ConflictDetectedAtUtc`, plus the local entity's own current content for comparison. No blocking gap
identified for 16D to build on.

---

# Phase 16D — Personal Sync: Explicit Conflict Resolution and Deletion/Tombstone Propagation

Implements two capabilities on top of 16C's detection-only floor: (1) explicit, user-driven resolution
of a draft/note conflict, surviving restart and a temporarily-unavailable sync folder; (2) an explicit
tombstone/deletion model, propagated safely in both directions and never inferred from a missing file.
The one invariant behind every decision below: **never silently lose user-authored data** — a conflict
is only ever resolved by an explicit user choice, and a deletion is only ever represented by an
explicit, versioned tombstone envelope.

## 16D.1 Persisted conflict model

16C's `sync_entity_state.Conflict*` columns record only identity/fingerprint (remote device, revision,
content hash) — enough to *detect* a conflict and skip exporting over it, but not enough to *resolve*
one after a restart, since the conflicting remote file might have changed again, moved, or the sync
folder might be unreachable by the time a user opens the conflict screen. Phase 16D adds a second,
dedicated table, [`sync_conflicts`](../src/Carowinds.Passdown.Infrastructure/Storage/Migrations/Migration0013CreateSyncConflictsTable.cs)
(one new table — spec's explicit preference over per-entity-type conflict tables), holding the **full
conflicting remote envelope**, keyed by `(entity_type, entity_id)` — at most one row per entity, matching
`sync_entity_state`'s own single-conflict-per-entity model:

```
sync_conflicts
    entity_type, entity_id                          -- PK, same identity as sync_entity_state
    remote_device_id, remote_revision                -- which device/revision this conflict is against
    remote_modified_at_utc
    remote_is_deleted                                -- true for a delete-vs-edit conflict (§16D.5)
    remote_payload_schema_version, remote_payload_json  -- the full payload, opaque; null when remote_is_deleted
    remote_content_hash
    detected_at_utc
    newer_remote_device_id, newer_remote_revision,
    newer_remote_detected_at_utc                     -- see §16D.4 "remote changes again while pending"
```

[`SyncConflictRecord`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConflictRecord.cs) is the
Application-layer shape; [`ISyncConflictRepository`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConflictRecord.cs)/
[`SqliteSyncConflictRepository`](../src/Carowinds.Passdown.Infrastructure/Storage/Repositories/SqliteSyncConflictRepository.cs)
its persistence — the same `IUnitOfWork?`-optional-parameter pattern `SqliteCatalogProductRepository`
already established (Phase 3G), so a resolution's local-entity-change and conflict-clearing can commit
atomically (§16D.7). `sync_entity_state.Conflict*` is **unchanged and still used** for its original
purpose — the cheap "is this entity mid-conflict, skip exporting it" gate every sync pass already
checks; `sync_conflicts` is purely additive alongside it, not a replacement.

DB schema **12 → 13** (`Migration0013CreateSyncConflictsTable`). Draft schema remains 3; Snapshot schema
remains 1. Verified against an existing DB 12 with pre-existing draft/note/history/sync-entity-state rows
(`PortableLifecycleTests.Schema12ToSchema13_CreatesSyncConflictsTable_AndPreservesExistingDraftNoteHistoryAndSyncEntityStateData`)
and against the legacy-app-data-folder-name compatibility path (`Phase13GAppDataRenameCompatibilityTests`)
— both now target DB 13. A fresh database initializes straight to schema 13
(`SqliteRepositoryTestFixture`, used by every repository test in this phase).

## 16D.2 Conflict lifecycle

Conceptually **None → Detected → (resolved back to None)**. In code, `SyncEntityState.ConflictState`
keeps its 16C name (`SyncConflictState.None`/`Conflicting`) rather than being renamed to `Detected` —
`Conflicting` *is* the "Detected" state; renaming an already-shipped, already-tested enum member for a
purely cosmetic difference was rejected as unnecessary churn. There is no separate persisted "Resolving"
state: a resolution command (`ResolveKeepLocalAsync`/`ResolveUseRemoteAsync`/`ResolveAcceptDeletionAsync`)
is one synchronous attempt against the `Conflicting` precondition — a failed attempt (sync folder
unavailable) simply leaves the entity `Conflicting`, safely retryable by calling the same command again
(§16D.7/§16D.10). One entity has at most one current unresolved conflict, exactly as the phase brief
requires — `sync_conflicts`' primary key enforces this structurally.

## 16D.3 Conflict list and detail (UI)

New Application-layer types
([`SyncConflictSummary`/`DraftConflictDetail`/`NoteConflictDetail`/`ConflictFieldComparison`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConflictDetail.cs))
and the owning service,
[`IPersonalSyncConflictResolutionService`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConflictDetail.cs) /
[`PersonalSyncConflictResolutionService`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/PersonalSyncConflictResolutionService.cs)
(Infrastructure — owns validation, persistence, revision/rebase changes, export, and conflict clearing;
no ViewModel implements any of this, per spec §19):

- `GetConflictsAsync()` → one `SyncConflictSummary` per unresolved conflict — `DisplayLabel` is a
  human string ("Sep 9 Draft", `Operational Note: "Gate 3 scanner down"`), never a raw GUID.
- `GetDraftConflictDetailAsync(date)` / `GetNoteConflictDetailAsync(noteId)` → a focused, summarized
  local-vs-remote comparison — `DraftConflictComparer`/`NoteConflictComparer` build a small, fixed list
  of `(Label, LocalValue, RemoteValue)` rows (Manual Hours, Private Buyout, Projected Attendance,
  Pre-Solds, Early Ride Time, Daily Feature, Product Overrides count, Custom Products, Selected
  Operational Notes for drafts; Title/Body/Category/Priority/Effective Dates/Status for notes) —
  deliberately not a raw-JSON dump (spec §5/§6).
- `PersonalSyncSettingsViewModel` (Desktop) gained `Conflicts`/`ConflictCount`/`HasConflicts`/
  `IsReviewingConflicts`/`SelectedConflictDetail`/`ConflictActionMessage` plus
  `ReviewConflictsCommand`/`OpenConflictCommand`/`CloseConflictsCommand`/`CloseConflictDetailCommand`/
  `KeepLocalCommand`/`UseRemoteCommand`/`AcceptDeletionCommand` — pure UI coordination, every actual
  decision delegated to `IPersonalSyncConflictResolutionService`.
  [`PersonalSyncSettingsView.axaml`](../src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml)
  extends the existing Settings screen in place (no new Window/dialog — spec §27's "do not turn Settings
  into a large sync management console," and §33's "avoid increasing real Avalonia Window-test surface
  unless the actual conflict UI cannot be tested adequately via ViewModel tests," which it can): a
  conflict-count callout + "Review Conflicts" button appears only when `HasConflicts`; reviewing shows a
  list of rows, each with a "Review" button; selecting one shows the field comparison and the three
  action buttons, gated by `IsRemoteDeletion` (`UseRemoteCommand` hidden for a remote deletion; spec §15
  "do not call this Use Other Device if the remote version is a deletion").

## 16D.4 Remote changes again while a conflict is pending

Handled inside `PersonalSyncService.RecordConflictAsync` (shared by drafts and notes): on a *new*
conflict, the full remote envelope is persisted as-is. On a subsequent sync pass, if a `sync_conflicts`
row already exists for that entity:

- the exact same remote `(DeviceId, Revision)` re-appears → no-op (consistent re-detection, not new
  activity);
- a *different* remote `(DeviceId, Revision)` appears → the **original** `RemoteDeviceId`/`RemoteRevision`/
  `RemotePayloadJson`/`RemoteContentHash` fields are left untouched (never silently replaced, spec §10);
  only `NewerRemoteDeviceId`/`NewerRemoteRevision`/`NewerRemoteDetectedAtUtc` are updated, and the
  conflict detail UI surfaces this via `HasNewerRemoteActivity`/`ConflictDetailPresentation.NewerActivityNote`.

Resolving the original conflict (either action) clears the `sync_conflicts` row entirely; the *next*
sync pass then re-evaluates against whatever is currently in the folder — if that's the newer remote
revision the user was warned about, it goes through the exact same detection logic again (possibly a
fresh conflict, possibly a safe update, depending on what changed).

## 16D.5 Tombstone/deletion model

`SyncEnvelope.IsDeleted` (already defined since 16A/16B) is now actually used. A tombstone envelope for
drafts/notes has `Payload` = JSON `null` (§11's "payload may be null/empty if safe and clearly
versioned") and `PayloadSchemaVersion` = the entity's current payload schema version (so a reader always
knows what schema *would* apply if it weren't a tombstone) — never inferred from a missing file.
`SyncEntityIdentity`/`SyncPathService` are unchanged: a tombstone is written to the exact same canonical
path (`drafts/<date>.json`, `notes/<guid>.json`) a content envelope would use — there is no separate
tombstone folder or filename convention.

[`SyncContentHash.TombstoneMarker`](../src/Carowinds.Passdown.Application/PersonalSync/SyncContentHash.cs)
(`"TOMBSTONE"`, never a valid 64-hex-char SHA-256 digest) is the sentinel stored in
`sync_entity_state.SyncedContentHash` once this device's understanding of an entity's deletion is
confirmed-synced — reusing the *existing* dirty-comparison mechanism for deletion state, so **no new
schema column was needed** to track "is this entity's deletion already synced." A key invariant this
enables without extra bookkeeping: **`sync_entity_state` row exists + no domain row exists ⟺ this
device considers the entity deleted** (whether via a local delete or an incorporated remote tombstone) —
this single fact, always derivable from data already being persisted, is what `SyncReconciliation.Decide`
and the tombstone-export loop both key off of.

## 16D.6 `SyncReconciliation.Decide` — tombstone-aware decisions

Four new [`SyncImportDecision`](../src/Carowinds.Passdown.Application/PersonalSync/SyncReconciliation.cs)
values extend 16C's five, all still produced by the one pure, filesystem-free decision function:

| Decision | When |
|---|---|
| `RemoteDeletionAlreadyAbsent` | Remote envelope is a tombstone; local already has no row for it (never had one, or already incorporated an earlier deletion). No-op; `LastRemoteDeviceId/Revision` updated only if a `sync_entity_state` row already exists, so a re-scan stays cheap. |
| `RemoteDeletionSafeApply` | Remote envelope is a tombstone; local exists and has **not diverged** from the last confirmed-shared content (§14 SAFE case) — delete locally. |
| `RemoteDeletionConflict` | Remote envelope is a tombstone; local exists and **has diverged** since the last shared baseline (or there never was one) — a delete-vs-edit conflict (§14 CONFLICT case). Never resolved automatically. |
| `LocalDeletionConflict` | This device considers the entity deleted (`sync_entity_state` row exists, no domain row); an incoming **non-deletion** envelope arrives — resurrection risk (§17). Never silently resurrected, never silently dropped; always a conflict. |

`RemoteDeletionConflict` and `LocalDeletionConflict` both route through the same
`PersonalSyncService.RecordConflictAsync` helper as an ordinary edit-vs-edit `Conflict` — the only
difference is `RemoteIsDeleted` on the persisted `SyncConflictRecord`. Which "direction" a conflict is
(remote deleted vs. local deleted) is never separately stored — it is always derived live at resolution
time from whether the local domain row currently exists, since that can only ever mean one of those two
things once a `sync_entity_state` row exists at all.

## 16D.7 "Keep This Device"

`PersonalSyncConflictResolutionService.ResolveKeepLocalAsync`:

1. Load the persisted conflict; a missing conflict (already resolved by a prior call, or converged on
   its own) returns `NoConflictFound` — treated as success, not an error (idempotence, §21).
2. Read the **current live** local content (never a frozen snapshot from conflict-detection time — the
   user may have kept editing locally while the conflict sat unresolved).
3. Build a new envelope from it — content envelope if the local entity exists, tombstone envelope if it
   doesn't (the "keep my deletion" mirror case) — with a freshly bumped `LocalRevision`.
4. **Export first, persist second**: `AtomicFileWriter` writes the envelope to the canonical path *before*
   any database state changes. If the write fails (folder unavailable, §7/§32), the method returns
   `ExportUnavailable` and touches **nothing** in SQLite — the conflict remains exactly as it was,
   safely retryable by calling the same command again.
5. Only after a successful export: one `IUnitOfWork` transaction updates `sync_entity_state` (new
   revision, new synced-content-hash, conflict fields cleared) and deletes the `sync_conflicts` row
   together — spec §20's "local entity changes + sync metadata/conflict-state updates should be atomic."

## 16D.8 "Use Other Device"

`ResolveUseRemoteAsync` — refuses (`WrongActionForConflict`) if the conflict is actually a remote
deletion (spec §15: don't call this action on a deletion; use Accept Deletion instead — enforced in code,
not just UI copy, so a ViewModel/test bug can't silently do the wrong thing). Otherwise, one
`IUnitOfWork` transaction: deserializes the persisted `RemotePayloadJson`, upserts it locally (draft:
`SaveAsync`, always an upsert; note: `AddAsync` if the local row is absent — the mirror "local deleted,
remote wins" case — else `UpdateAsync`, both preserving the original `Guid`/`OperatingDate` identity
exactly), sets `sync_entity_state.SyncedContentHash` to the conflict's own `RemoteContentHash`, and
deletes the `sync_conflicts` row. No file export happens — the remote file is already the correct
canonical content — which is also what makes "avoid immediately re-exporting the old local version"
(§8) automatic: `SyncedContentHash` now equals current content, so the next export pass sees nothing
dirty.

## 16D.9 "Accept Deletion"

`ResolveAcceptDeletionAsync` — refuses (`WrongActionForConflict`) if the conflict is *not* actually a
remote deletion. Otherwise: one `IUnitOfWork` transaction deletes the local domain row (if any),
marks `sync_entity_state` as tombstoned (`SyncedContentHash = TombstoneMarker`, revision bumped,
`LastRemoteDeviceId/Revision` set to the conflict's), and clears `sync_conflicts`. No file export — the
remote's tombstone file is already canonical.

## 16D.10 Local deletion and tombstone export

Repository-level primitives only —
[`IAssociateDraftRepository.DeleteAsync`](../src/Carowinds.Passdown.Application/Persistence/AssociateDraftPersistence.cs)/
[`IOperationalNoteRepository.DeleteAsync`](../src/Carowinds.Passdown.Application/OperationalNotes/OperationalNotePersistence.cs),
each a plain `DELETE FROM ... WHERE`, `IUnitOfWork?`-optional like every other write in this phase. A
"genuine local delete" is the same two-step pattern every other local write already follows (spec §7):
`repository.DeleteAsync(id)`, then the *existing* `ISyncChangeTracker.MarkLocalChangeAsync(entityType,
entityId)` — no new tracker method was needed, since bumping `LocalRevision` is exactly the same
operation regardless of whether the underlying change was an edit or a delete; dirtiness is always
decided by content-hash comparison at export time, and §16D.5's export loop naturally treats a
domain-row-absent, non-tombstone-marker `sync_entity_state` row as "dirty for a tombstone export."

`PersonalSyncService.ExportPendingTombstonesAsync` (shared by drafts/notes' export step) walks every
`sync_entity_state` row of that type not covered by the normal domain-row export loop, skips anything
mid-conflict or already synced-as-deleted, and writes a tombstone envelope for the rest — independent of
whether the local domain row still exists (spec §13.4: "export tombstone independently of local entity
existence"). An export I/O failure is counted and leaves the state untouched, retried next pass (§13's
"deletion remains local, tombstone remains pending").

## 16D.11 Real draft/note deletion wiring — audited, none exists

Per spec §12, the actual product was audited before writing any of the above:
`IAssociateDraftRepository`/`IOperationalNoteRepository` had **no delete method at all** before this
phase (`SqliteOperationalNoteRepositoryTests` even had a standing architectural-invariant test asserting
exactly that, now updated — see §16D.16). Operational Notes' only lifecycle transition is
`Active → Resolved`, which is explicitly **not** deletion (spec's own "archive/inactive is NOT
automatically deletion"). **No delete UI was manufactured to exercise tombstones** — the repository
`DeleteAsync` methods above exist solely as sync infrastructure (needed so an *imported* remote tombstone
can actually be applied locally, and so the primitives are ready and fully tested for whenever/if a
genuine product delete feature is added later), and are exercised only directly, by tests, per spec
§23's explicit allowance to "test tombstone/reconciliation primitives directly without inventing product
UI." No `PersonalSyncSettingsView`/`AssociateView`/`OperationalNotesView` control calls either
`DeleteAsync` method.

## 16D.12 Resurrection prevention

Because `sync_entity_state` row-exists-but-domain-row-absent is the one, sufficient signal for "this
device considers the entity deleted" (§16D.5), no separate bookkeeping was needed to prevent
resurrection:

- **A deletes, B imports the tombstone, B syncs again**: B's own export loop no longer sees the entity
  (no domain row, and the tombstone-export loop skips it — `SyncedContentHash` already equals
  `TombstoneMarker`). B never re-exports the pre-delete content it used to have
  (`Deletion_D_StaleDeviceThatDidNotEditDoesNotResurrectAfterOwnSyncPass`).
- **C was offline before a deletion, reconnects later, never edited its own copy**: `Decide` sees
  `localEntityExists = true` (C still has its old copy) and the remote envelope `IsDeleted = true`; C's
  local content still equals its `SyncedContentHash` from before (C never touched it) →
  `RemoteDeletionSafeApply` — C deletes locally, converges, never resurrects
  (`Deletion_C_StaleOfflineDeviceReconnectsAfterDeletion_SafelyAppliesDeletion_NeverResurrects`).
- **C did edit before the tombstone arrived**: `RemoteDeletionConflict`, not silent deletion —
  identical to the two-device case (§16D.6).
- **The mirror case — A deletes, B explicitly resolves A's tombstone with "Keep This Device" (an
  intentional resurrection with a new authoritative revision), A syncs again**: A still considers the
  entity deleted (`sync_entity_state` row, no domain row) and B's incoming envelope is live content, not
  a deletion → `LocalDeletionConflict` on **A's own side too**. This is deliberate, not a gap: an
  explicit resolution on one device does not silently override the other device's own explicit deletion
  without that device's user also confirming — resurrection is never silent on *either* side. A must
  call `ResolveUseRemoteAsync` itself to accept B's resurrection before both sides converge
  (`Deletion_F_KeepLocalAfterDeleteConflict_ResurrectsIntentionallyWithNewAuthoritativeRevision`).

## 16D.13 Missing file and conflict-copy behavior — unchanged from 16C

Still true, and now doubly load-bearing since a tombstone could otherwise be confused with a missing
file: a canonical file that disappears between a directory listing and its read, or a file that
previously existed and later vanishes, is **never** treated as a deletion signal — there is no
"previously seen" list at all, each pass is a full independent reconciliation
(`Deletion_I_MissingCanonicalFile_IsNeverTreatedAsDeletion`, reusing 16C's own
`RunAsync_PreviouslySeenRemoteFileLaterDisappears_...` coverage). A malformed tombstone (unparseable
JSON, or an `EntityType`/`EntityId` mismatch) is counted in `MalformedCount` and skipped — never
destructive (`Deletion_J_MalformedTombstoneFile_DoesNothingDestructive`). An OneDrive conflict-copy
filename is still silently ignored regardless of whether it happens to contain a tombstone or content
envelope (16C's filename-parsing gate runs before any envelope is even read).

## 16D.14 Tombstone retention

Retained **indefinitely** — no garbage collection of tombstone files or `sync_entity_state`/
`sync_conflicts` rows was implemented (spec §16's explicit instruction, given there is no authoritative
multi-device registry to safely base a GC policy on). Storage impact is negligible (a tombstone envelope
is a few hundred bytes of JSON, same order of magnitude as a normal draft/note envelope — see 16A §J).

## 16D.15 Transaction safety, idempotence, and failure behavior

Every resolution action that changes both a local domain row and `sync_entity_state`/`sync_conflicts`
does so inside one `SqliteUnitOfWork` (via the already-registered `IUnitOfWorkFactory`) — commit-or-
rollback together, spec §20. The one thing deliberately **outside** that transaction is the cloud
filesystem write in `ResolveKeepLocalAsync` — it happens *before*, and only a successful write unlocks
the subsequent DB transaction, so "DB succeeds, export fails" (the failure mode spec §20 calls out) can
never happen; the only possible failure mode is "export fails, DB untouched," which is exactly the safe,
retryable state required.

Idempotence (§21), verified directly:
- Resolving an already-resolved conflict a second time returns `NoConflictFound` rather than throwing,
  duplicating a revision, or resurrecting a record
  (`Resolution_ResolvingTheSameConflictTwice_SecondCallIsIdempotent_NoConflictFound`).
- Repeated tombstone import is a no-op after the first (`Deletion_B_RepeatedTombstoneImport_IsIdempotent`).
- Calling `ResolveUseRemoteAsync`/`ResolveAcceptDeletionAsync` against the wrong conflict shape is
  refused before any state changes (`Resolution_UseOtherDeviceOnARemoteDeletionConflict_IsRefused_...`,
  `Resolution_AcceptDeletionOnANonDeletionConflict_IsRefused_...`).

## 16D.16 Privacy

Re-audited every new field added in this phase (`SyncConflictRecord`, the tombstone envelope shape, the
new UI presentation types): no username, machine name, email, account identity, credential, token, path,
or hardware identifier. `RemotePayloadJson` carries the same category of already-audited operational
content 16A §I already cleared (manual hours, attendance, note text) — nothing new. The existing
`ExportedSyncFiles_NeverContainMachineNameUsernameOrLocalPaths` privacy guard (16C) continues to pass
unmodified against the new tombstone-capable export path.

## 16D.17 Existing-user migration and fresh-DB result

- **Existing DB 12 upgrade**: `PortableLifecycleTests.Schema12ToSchema13_CreatesSyncConflictsTable_AndPreservesExistingDraftNoteHistoryAndSyncEntityStateData`
  seeds a real DB 12 (draft, note, finalized/History row, and a `sync_entity_state` row) through the real
  repositories, upgrades to DB 13, and confirms every pre-existing row — including the 16C-era
  `sync_entity_state` row — is untouched, and the new empty `sync_conflicts` table is immediately usable.
  `Phase13GAppDataRenameCompatibilityTests` (legacy `%LOCALAPPDATA%` folder name) is likewise upgraded to
  and verified against DB 13.
- **Fresh DB**: every repository test in this phase (`SqliteRepositoryTestFixture`) migrates straight to
  schema 13 in one pass.
- **Sync configuration untouched**: the migration only creates the empty `sync_conflicts` table — a
  device's enabled/disabled sync setting (a flat file, not a DB row) is unaffected.

## 16D.18 Two-device conflict and deletion simulations

Extended [`PersonalSyncServiceTests`](../tests/Carowinds.Passdown.Infrastructure.Tests/PersonalSync/PersonalSyncServiceTests.cs)'s
existing `LocalStoreA`/`LocalStoreB`/`SharedSyncFolder` harness (now also constructing
`ISyncConflictRepository`/`IPersonalSyncConflictResolutionService`/`IUnitOfWorkFactory` per device; a
"restart" is simulated by calling `CreateDevice` again with the same on-disk database file and a fresh
clock/service graph — real objects, real SQLite persistence, nothing mocked). Every scenario the phase
brief calls out by letter:

| Conflict scenario (§22) | Result |
|---|---|
| A. A/B edit same draft → conflict, full remote payload persisted | PASS |
| B. A Keep Local → B converges | PASS |
| C. A Accept Remote → A converges, never re-exports the discarded version | PASS |
| D. Restart before resolving → conflict and full payload survive | PASS |
| E. Sync folder unavailable during resolution → retry works, conflict never lost | PASS |
| F. Remote changes again while pending → original stays stable, newer activity flagged | PASS |
| Note equivalents (conflict detection, Keep Local, convergence) | PASS |

| Deletion scenario (§23) | Result |
|---|---|
| A. A deletes → B imports deletion | PASS |
| B. Repeated tombstone import idempotent | PASS |
| C. Stale offline device reconnects, never edited → safe apply, never resurrects | PASS |
| D. Stale device's own next sync pass never re-exports pre-delete content | PASS |
| E. B locally edits before receiving tombstone → conflict | PASS |
| F. Keep Local after delete conflict → intentional resurrection with new revision; other device also requires its own explicit confirmation (§16D.12) | PASS |
| G. Accept Deletion removes local record | PASS |
| H. Tombstone/delete conflict survives restart | PASS |
| I. Missing canonical file does nothing | PASS |
| J. Malformed tombstone does nothing destructive | PASS |

## 16D.19 Sync Now behavior — unchanged

`PersonalSyncSettingsViewModel.SyncNowCommand` still calls `IPersonalSyncService.RunAsync()` exactly as
16C wired it; the only addition is a `RefreshConflictsAsync()` call afterward so the conflict count
reflects any new conflicts that pass just detected. An unresolved conflict continues to pause only that
one entity's export (16C behavior, unchanged) — unrelated records still sync every pass, and the
conflict screen is never auto-opened (spec §26 — the user must click "Review Conflicts").

## Tests added (Phase 16D)

- `SyncReconciliationTests` — 7 new cases for the four tombstone-aware decisions (Application.Tests).
- `SqliteSyncConflictRepositoryTests` — round-trip, tombstone-null-fields, upsert-replaces, delete,
  list-across-types, reopened-connection (Infrastructure.Tests).
- `DeleteAsync` coverage added to `SqliteAssociateDraftRepositoryTests`/`SqliteOperationalNoteRepositoryTests`.
- `PersonalSyncServiceTests` — 6 conflict-resolution scenarios (A-F) + 1 note equivalent, 10 deletion
  scenarios (A-J), 4 resolution guard-rail/idempotence tests (Infrastructure.Tests).
- `PortableLifecycleTests.Schema12ToSchema13_...` migration test; `Phase13GAppDataRenameCompatibilityTests`
  updated to target DB 13.
- `PersonalSyncSettingsViewModelTests` — 6 new tests for conflict count/list/detail/resolution UI
  coordination (Ui.Tests) — ViewModel-level only, per spec §33's preference; the native picker/real-Window
  rendering of the new conflict panel itself remains **NOT EXECUTED** on this (macOS/Darwin) host, same
  limitation carried since 16B.
- `FakePersonalSyncConflictResolutionService` (Ui.Tests/TestSupport).
- One pre-existing architectural-invariant test, `SqliteOperationalNoteRepositoryTests.Repository_ExposesNoDeleteMethod`,
  was **updated, not removed**, to `Repository_ExposesExactlyOneDeleteMethod_ForSyncTombstonesOnly_AndNoRemoveMethod` —
  it now asserts the one intentional exception (`DeleteAsync`, sync-only) while still guarding against any
  casual "Remove"-style method.

## Final test count

1857/1857 deterministic (non-`LiveSmoke`) tests passing, run per-project
(`Domain.Tests` 105, `Application.Tests` 524, `Infrastructure.Tests` 483, `Documents.Tests` 114,
`Ui.Tests` 631). Up from the Phase 16C baseline of 1810/1810. `dotnet build CarowindsPassdown.slnx`:
0 warnings, 0 errors.

The same pre-existing, previously-documented (since Phase 6D) Avalonia headless cross-test parallelism
flake observed in 16C recurs here, now also intermittently involving `OperationsCalendarViewTests`
alongside `RealWindowStartupTimingTests`/`Phase15ERealWindowLayoutTests` when the full multi-project
suite or the full `Ui.Tests` project runs with default parallelism — confirmed non-deterministic (two
consecutive full-project runs produced two different failure sets) and confirmed unrelated to this
phase's changes (isolating the three real-`Window`-constructing classes together passes 38/38 repeatably;
isolating everything else passes 593/593 repeatably; 38 + 593 = 631, the full total, zero real failures
attributable to Phase 16D). Per CLAUDE.md, not fixed — out of scope, pre-existing, and explicitly called
out as such by the Phase 16D brief itself.

## Known limitations

- The mirror "local deleted, remote resurrects" case always requires **both** devices to make an
  explicit choice (§16D.12) — there is no one-sided "just converge" shortcut, even though this makes the
  UX one extra click heavier than the more common "remote deleted, local edited" direction. This was a
  deliberate safety choice under ambiguous spec guidance, not an oversight — see §16D.12's worked example
  and the reasoning in the final report.
- The conflict detail comparison is a fixed, curated field list (spec-sanctioned "summarized, not raw
  JSON"), not a generic recursive diff — a future draft-payload field that isn't in `DraftConflictComparer`'s
  list wouldn't automatically show up in the comparison (it would still be correctly preserved/applied by
  Keep Local/Use Other Device, which always operate on the full persisted payload, never the summarized
  view).
- No automatic/background sync cadence — still "Sync Now" only, unchanged from 16C (16F's job).
- Finalized History/PDFs are not synced and have no conflict/tombstone model yet (16E's job) — the
  generic `sync_conflicts`/tombstone infrastructure built here was kept entity-agnostic at the
  `SyncEntityType` level specifically so 16E can extend it without another schema redesign, but no
  `FinalizedPassdown`-specific wiring exists yet.
- The real Windows-host OneDrive desktop client interaction, and the native conflict-panel's actual
  on-screen rendering, remain unverified on this (macOS/Darwin) development host — same limitation
  carried forward from every prior Personal Sync phase.

## Deferred to 16E

- Finalized History/PDF sync, using the insert-only/dedupe-by-content-hash rule from 16A §G.
- Any conflict/tombstone model for `FinalizedPassdown` (the infrastructure here is ready to extend, per
  above, but nothing is wired).

## Deferred to 16F

- Automatic/background sync cadence (timer, startup sync, save-triggered sync, filesystem watcher,
  scheduled sync) — 16D remains manual-trigger ("Sync Now") only, per explicit instruction.
- Richer sync status UX (last-synced indicator, device list) beyond the minimal conflict count/review
  surface added here.

## Phase 16E readiness

The tombstone/conflict primitives (`SyncEnvelope.IsDeleted`, `SyncReconciliation.Decide`'s four
deletion-aware branches, `sync_conflicts`) are all keyed by the existing `SyncEntityType` enum, which
already includes `FinalizedPassdown` (defined since 16B, unused until now) — 16E can register a fourth
entity type's import/export loop against the same `SyncReconciliation.Decide`/`ISyncConflictRepository`/
`sync_conflicts` machinery without another schema migration for conflict/deletion support specifically
(though History's own insert-only/content-hash dedup rule, per 16A §G, is a different — simpler —
conflict model than drafts/notes', so 16E will still need its own decision path, not `Decide` reused
verbatim). No blocking gap identified.

---

# Phase 16E — Personal Sync: Finalized History and Frozen PDFs (Implementation)

Implements the last deferred entity kind: finalized passdowns and their frozen PDFs now move between
two devices through the shared sync folder, using the insert-only/content-hash-dedup conflict model
16A §G and 16D's own readiness note called for — deliberately not drafts/notes' revision-tracked,
resolvable conflict model. History remains immutable and read-only in every respect; sync only ever
inserts a complete, byte-identical frozen record, never edits or regenerates one.

## 16E.1 Entity type and identity

No new `SyncEntityType` member was needed — `FinalizedPassdown` has existed on the enum since 16B.
[`SyncEntityIdentity.ForFinalizedPassdown(Guid)`/`TryParseFinalizedPassdownId`](../src/Carowinds.Passdown.Application/PersonalSync/SyncEntityIdentity.cs)
mirrors `ForNote` exactly (lowercase-hyphenated `"D"` Guid format). **The primary sync `EntityId` is
`FinalizedPassdown.Id` (the existing surrogate Guid) — never `PassdownDate`.** This was audited, not
assumed: `PassdownDate` is unique only *within one device's own* `finalized_passdowns` table
(`ux_finalized_passdowns_passdown_date`); two devices can independently finalize the same date with two
different `Id`s (§16E.6 below), so date can never serve as cross-device identity. No existing History
identity was changed to make this work — `Id` was already the app's own surrogate key (16A §B).

## 16E.2 Sync payload

[`FinalizedPassdownSyncPayload`/`FinalizedPassdownSyncPayloadSerializer`](../src/Carowinds.Passdown.Application/PersonalSync/FinalizedPassdownSyncPayload.cs) —
schema version **1** — carries `Id`, `PassdownDate`, `FinalizedAtUtc`, `SnapshotSchemaVersion`,
`SnapshotJson` (the exact, already-frozen `FinalizedPassdownSnapshotSerializer` text, reused
byte-for-byte), `ContentHash`, `Origin`, and `PdfSha256`/`PdfSizeBytes`. The frozen PDF bytes themselves
are never embedded in this payload (16A §D/§E) — they travel as the sibling `.pdf` file. `FromDomain`
throws if called on a record with no frozen PDF (`HasFrozenPdf == false`, a legacy pre-9C row) — the
export loop guards this before ever constructing a payload, so the throw is a defensive invariant, not
expected reach in practice.

## 16E.3 ContentHash audit result

Audited exactly what `FinalizedPassdown.ContentHash` covers before using it for anything (spec's
explicit "do not assume"): `FinalizedPassdownSnapshotSerializer.ComputeContentHash` hashes **the
snapshot JSON only** — it has never covered the PDF. This is authoritative and reused as-is for
snapshot/record identity comparisons (dedupe, immutable-conflict, dual-finalization detection) —
no new hash scheme for the snapshot. For the PDF, `FinalizedPassdown.PdfSha256` — already computed by
`FinalizedPassdownFinalizationService` since Phase 9C for local integrity/diagnostics — is reused as
the sync layer's PDF integrity check (§7's "if PDF integrity requires a separate binary hash, add one
cleanly to the sync payload/envelope metadata" — the existing column already *is* that hash; nothing
new was added to `FinalizedPassdown`'s own schema). `SyncEnvelope.ContentHash` (defined since 16A/16B,
unused until now) carries the snapshot content hash for every finalized-passdown envelope.

## 16E.4 File naming and path collision fix

**Audited before relying on it (spec §6's explicit instruction), and it failed**: 16A/16B's original
`GetHistoryJsonFilePath`/`GetHistoryPdfFilePath` design used `<date>.json`/`<date>.pdf` alone (illustrative
in 16A §E, implemented but never wired to any writer in 16B). Since identity is the Guid, not the date
(§16E.1), and two devices can independently finalize the same date, that naming would silently let one
device's export overwrite the other's file for the same date — exactly the collision spec §6 warns
about. Both methods were changed (never wired to any real writer before this phase, so this was a
pre-release correction, not a breaking change to a shipped format) to
[`SyncPathService.GetHistoryJsonFilePath(root, date, id)`/`GetHistoryPdfFilePath(root, date, id)`](../src/Carowinds.Passdown.Application/PersonalSync/SyncPathService.cs),
producing `history/<yyyy-MM-dd>-<guid-D>.json`/`.pdf` — deterministic, collision-proof (Guid-suffixed),
and still inspectable (date remains the leading, human-readable segment). `TryParseHistoryFileName`
is the exact inverse, rejecting anything that isn't precisely `yyyy-MM-dd-<guid>` (47 characters) —
an OneDrive conflict-copy name, a stray file, or a hypothetical pre-16E date-only name all fail to
parse and are left untouched, mirroring 16C §28's "unexpected filename ignored" rule. Centralized
entirely in `SyncPathService`, per spec §6.

## 16E.5 Repository additions

Two additions to [`IFinalizedPassdownRepository`](../src/Carowinds.Passdown.Application/Persistence/FinalizedPassdownPersistence.cs)
(SQLite implementation:
[`SqliteFinalizedPassdownRepository`](../src/Carowinds.Passdown.Infrastructure/Storage/Repositories/SqliteFinalizedPassdownRepository.cs)),
neither requiring a schema change (both are new queries over existing columns):

- `GetByIdAsync(Guid id)` — the import path's own lookup; needed to tell "a local record with this
  exact remote id already exists" apart from "a local record exists for this date under a different
  id" (the dual-finalization case). `GetByDateAsync` alone cannot make this distinction.
- `ListForSyncAsync()` → `FinalizedPassdownSyncMetadata(Id, PassdownDate, ContentHash)` — the cheap
  per-pass enumeration the export loop walks (mirrors `ListAsync`'s own "never select `pdf_bytes`/
  `snapshot_json` for a listing" rationale, §4).

## 16E.6 Reconciliation — a dedicated, simpler decision path

[`FinalizedPassdownSyncDecision`/`FinalizedPassdownSyncReconciliation.Decide`](../src/Carowinds.Passdown.Application/PersonalSync/FinalizedPassdownSyncReconciliation.cs) —
a **separate** pure function from drafts/notes' `SyncReconciliation.Decide`, exactly as 16D's own
Phase-16E-readiness note anticipated ("a different — simpler — conflict model... not `Decide` reused
verbatim"). No revision tracking, no safe-update/tombstone branches — insert-only content never
changes once written, so the entire decision collapses to:

```
if envelope.DeviceId == our own device                                -> Ignored
if a local record exists with this exact id
    same ContentHash                                                  -> Converged (dedupe)
    different ContentHash                                             -> ImmutableConflict   (§13)
else if a local record exists for the same PassdownDate (different id) -> DualFinalizationConflict (§14)
else                                                                    -> RemoteOnlyImport
```

`PersonalSyncService.ProcessHistoryEnvelopeAsync` supplies the looked-up facts (`GetByIdAsync`, and
`ExistsForDateAsync` only when `GetByIdAsync` returned nothing — never both lookups on the common
same-id path) and acts on the decision.

## 16E.7 Import pipeline and integrity gates

Before `Decide` is ever called, `ProcessHistoryEnvelopeAsync` runs a fixed sequence of structural
checks, each a hard reject (never partially applied, never crashing the pass):

1. Filename parses as canonical `yyyy-MM-dd-<guid>.json` (§16E.4) — else silently skipped (unexpected
   filename, §28-style).
2. Envelope `EntityType == FinalizedPassdown`, `EntityId` matches the filename's guid, `IsDeleted ==
   false` (History has no tombstone model, §20), `ContentHash` present — else `MalformedCount++`.
3. Self-authored (`envelope.DeviceId == this device`) — counted as `HistoryUnchangedCount`, returns
   immediately (never even looks for the PDF).
4. Payload deserializes; its `Id`/`PassdownDate`/`ContentHash` all agree with the filename and envelope
   — else `MalformedCount++` (never trusts one source over another).
5. `SnapshotJson` deserializes as a `FinalizedPassdownSnapshot`, and re-hashing it via
   `FinalizedPassdownSnapshotSerializer.ComputeContentHash` matches the claimed `ContentHash` — else
   `MalformedCount++` (corrupt/mismatched snapshot text never trusted).
6. Companion `.pdf` file exists — else `HistoryIncompleteCount++` (§16 — an expected, retried-later
   race, never a failure or conflict) and the JSON is left completely untouched.
7. PDF reads without an `IOException`/`UnauthorizedAccessException` — else `IoFailureCount++`.
8. PDF's actual SHA-256 and byte length match the payload's `PdfSha256`/`PdfSizeBytes` — else
   `HistoryConflictCount++` (§17 — corrupt/hash-mismatched PDF is treated as needing manual review,
   same bucket as a genuine content conflict; never imported, never overwrites a local copy, remote
   files left untouched).

Only after all eight gates pass does `FinalizedPassdownSyncReconciliation.Decide` run.

## 16E.8 Remote-only import — a dedicated immutable path

`RemoteOnlyImport` calls `finalizedPassdownRepository.AddAsync(payload.ToDomain(pdfBytes))` directly —
**never** `IFinalizedPassdownFinalizationService.FinalizeAsync`, which would recompute a snapshot/hash
from live `AssociateWorkspace`/catalog/hours data and could mutate timestamps or content (spec §11's
explicit "do not invoke normal finalization if it would mutate timestamps/content"). `AddAsync` already
*is* the dedicated immutable-import path 16A/16C established for drafts' `RemoteOnlyImport` case
too — no new repository method was needed for this, only a new caller supplying pre-validated frozen
bytes exactly as originally produced. A `SqliteException` from the underlying unique-date constraint
(a defensive-only race between the pre-checks and the insert) is caught and counted as an I/O failure,
never crashing the pass — retried fresh next time.

## 16E.9 Export pipeline and ordering

`ExportHistoryAsync` walks `ListForSyncAsync()` every pass (this **is** bootstrap — no separate
bootstrap code path, §10; a never-synced record has no `SyncEntityState` row, so its hash trivially
never matches `SyncedContentHash`). For each record not already confirmed in sync and not mid-conflict:

```
write PDF (AtomicFileWriter.WriteBytesAsync, temp-then-rename)
    ↓
write JSON envelope LAST (AtomicFileWriter.WriteTextAsync, temp-then-rename)
```

matching spec §8/§24 exactly — a reader must never observe a JSON envelope whose PDF isn't already
fully present; the presence of a valid JSON record means its PDF is guaranteed complete. A record with
no frozen PDF at all (`HasFrozenPdf == false`, a legacy pre-9C row) is skipped entirely — never
regenerated from current data (§4/§10's explicit "only if the original frozen PDF is genuinely missing
and existing product behavior explicitly supports recovery" — no such recovery behavior exists today,
so the row is simply left perpetually un-exportable, a known, narrow limitation).

Top-level `PersonalSyncService.RunAsync` order (§24, extending 16C/16D's existing drafts→notes
ordering): **Import drafts → Import notes → Import History → Export drafts → Export notes → Export
History.** History is placed last in each half deliberately — it is the largest payload (PDF-dominated)
and the one entity kind with no possibility of a local mutation racing the pass (immutable), so nothing
is gained by prioritizing it ahead of the smaller, higher-frequency draft/note records.

## 16E.10 Finalization hook — deliberately none added

Per spec §22's explicit "if current generic metadata can infer unsynced History during enumeration,
avoid adding unnecessary hooks": no change was made to
[`FinalizedPassdownFinalizationService`](../src/Carowinds.Passdown.Application/Finalization/IFinalizedPassdownFinalizationService.cs)
or `ISyncChangeTracker`. `ExportHistoryAsync`'s own per-pass enumeration (§16E.9) already discovers a
newly-finalized record on the very next "Sync Now" — its `SyncEntityState` row (or lack of one)
naturally reflects "never synced" without any write-time notification. This is a deliberate asymmetry
from drafts/notes (which *do* have a `MarkLocalChangeAsync` hook, 16C §8) — those are mutable and can
change many times without a new insert to enumerate against, so their dirtiness needs an explicit nudge;
History is insert-only, so a full per-pass scan is already exactly as cheap and always current.

## 16E.11 Same-id/different-content — immutable conflict (spec §13)

Never expected in real operation (two devices cannot independently mint the same `Guid.NewGuid()`), but
handled defensively per spec's explicit instruction not to assume it can't happen. `ImmutableConflict`
routes through the *same* generic `PersonalSyncService.RecordConflictAsync` helper 16D built for
drafts/notes (entirely reused, no changes) — keyed by the shared `Id`'s own `EntityId`, which also
naturally pauses that record's own future export (the existing "never export over
`SyncConflictState.Conflicting`" gate in `ExportHistoryAsync`, same mechanism as drafts/notes). No
"Keep This Device"/"Use Other Device" resolution exists for this conflict shape (§16E.14).

## 16E.12 Same-date/different-id — dual-finalization conflict (spec §14)

The realistic conflict case, verified with a dedicated two-device test
(`History_I_TwoDevicesIndependentlyFinalizeSameDate_...`). Deliberately keyed by the **remote**
record's own `EntityId`, not the local record's: the local finalized passdown for that date is a
legitimate, distinct, correctly-identified record and must keep syncing normally (verified: it exports
in the very same pass the conflict is detected, §16E.9's per-pass export walk is entirely independent
of the disputed remote entity's `SyncEntityState` row). Only the disputed *remote* record is marked
conflicting — it is never inserted locally, and the remote device's own files are never touched. Both
source artifacts survive untouched on their respective devices' exported copies in the shared folder
(different Guid-suffixed filenames, §16E.4, so they were never at risk of colliding in the first place).

## 16E.13 Persisted conflict model — no new table

Both conflict shapes (§16E.11/§16E.12) reuse the existing 16D `sync_conflicts` table/
`ISyncConflictRepository` entirely unmodified — `RemoteDeviceId`, `RemoteContentHash`,
`RemotePayloadJson` (the full `FinalizedPassdownSyncPayload` JSON, carrying the disputed record's own
`Id`/`PassdownDate`/hash for a future admin view), `DetectedAtUtc`, and the "newer remote activity"
fields all apply as-is. `RemoteIsDeleted` is always `false` for a History conflict (no tombstone model,
§16E.15) — the field simply goes unused for this entity type rather than needing a new one. No schema
migration was needed; DB schema remains **13**.

## 16E.14 No resolution actions for History conflicts (spec §13/§30)

[`PersonalSyncConflictResolutionService`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/PersonalSyncConflictResolutionService.cs)
explicitly refuses (`ConflictResolutionOutcome.WrongActionForConflict`) all three resolution actions —
`ResolveKeepLocalAsync`/`ResolveUseRemoteAsync`/`ResolveAcceptDeletionAsync` — when called with
`SyncEntityType.FinalizedPassdown`, before any of the existing draft/note branching runs (defense in
depth: that branching has no third case and would otherwise misinterpret a finalized-passdown Guid as
a note id). `GetConflictsAsync()` — the 16D mutable-entity conflict review list — filters
`FinalizedPassdown` conflicts out entirely, rather than surfacing an entry with no safe action to offer
against it. History conflict visibility in 16E is the `PersonalSyncResult.HistoryConflictCount` line in
the Sync Now result only (§16E.16) — a dedicated admin-facing review/resolution surface is explicitly
deferred beyond 16E, matching spec §30's "do not build complicated immutable-history resolution UI in
16E."

## 16E.15 Tombstones/deletion — audited, none exists or was added

Per spec §20's explicit audit instruction: `IFinalizedPassdownRepository` has no delete method, no code
path resolves/archives a finalized passdown, and no product UI offers deleting one. **No History
deletion UI or tombstone-export logic was added.** `ExportPendingTombstonesAsync` (16D, shared by
drafts/notes) is never called for `SyncEntityType.FinalizedPassdown` — History's export path
(`ExportHistoryAsync`) is a separate method with no tombstone step at all. An incoming envelope with
`IsDeleted == true` for a `FinalizedPassdown` entity is rejected as malformed (§16E.7 step 2) rather
than acted on — the generic tombstone machinery exists at the `SyncEntityType` level (16D built it to
be entity-agnostic) but is never invoked for History, exactly as 16D's own Phase 16E-readiness note
anticipated.

## 16E.16 `PersonalSyncService`/Sync Now integration

[`PersonalSyncResult`](../src/Carowinds.Passdown.Application/PersonalSync/IPersonalSyncService.cs)
gained five additive, defaulted fields — `HistoryImportedCount`, `HistoryExportedCount`,
`HistoryUnchangedCount`, `HistoryConflictCount`, `HistoryIncompleteCount` — alongside the existing
generic counts (a History event also increments the matching generic counter, e.g. a History import
counts in both `HistoryImportedCount` and `ImportedCount`, so the top-line "N imported" total stays
correct without a caller needing to add two numbers). `HistoryIncompleteCount` (§16) is the one bucket
counted nowhere else — a JSON-without-PDF race is neither a failure nor a conflict.
[`PersonalSyncResultPresentation.Format`](../src/Carowinds.Passdown.Desktop/Presentation/PersonalSyncResultPresentation.cs)
appends up to three clauses when relevant: `"N history items synced."`, `"N history conflict(s) —
requires manual review."`, `"N history items waiting for their PDF."` — omitted entirely when there was
no History activity, matching spec §30's illustrative example format. One broken History record (a
malformed file, a corrupt PDF, an integrity/dual-finalization conflict) never blocks draft/note sync in
the same pass, and vice versa — each entity kind's import/export loop catches and counts its own
I/O failures independently (verified: `History_M_MalformedJsonAmongValidHistoryRecords_...` and the
existing drafts/notes isolation tests both still pass unmodified).

## 16E.17 Privacy

Extended the existing standing privacy guard
(`ExportedSyncFiles_NeverContainMachineNameUsernameOrLocalPaths`) to include a finalized History record
in the same sync pass it already exercises for drafts/notes — still zero occurrences of
`Environment.UserName`, `Environment.MachineName`, the local AppData-equivalent path, or
"Token"/"Password"/"Credential" (case-insensitive) anywhere in the exported JSON. `FinalizedPassdownSnapshot`
content (park hours, attendance, ERT, Daily Feature, product rows, operational notes text) is exactly
the same category of already-printed operational content 16A §I cleared for drafts — nothing new.
`DeviceId` remains the only device identifier, a bare `Guid`.

## 16E.18 Storage measurement

Measured directly (not estimated) from a realistic multi-category snapshot serialized through the real
`FinalizedPassdownSnapshotSerializer`: compact snapshot JSON ≈ **1.3 KB**; the full indented sync
envelope wrapping it (`FinalizedPassdownSyncPayload` + `SyncEnvelope` overhead) ≈ **2.9 KB**. Frozen
PDFs are unchanged by this phase and still measure **57-58 KB** per 16A §J's direct measurement (this
phase never touches PDF generation). One History sync item ≈ 2.9 KB (JSON) + ~58 KB (PDF) ≈ **~61 KB**
— materially unchanged from 16A §J's original ~60-65 KB estimate; no update to the multi-year
projection table is needed.

## 16E.19 Two-device simulation results

Extended [`PersonalSyncServiceTests`](../tests/Carowinds.Passdown.Infrastructure.Tests/PersonalSync/PersonalSyncServiceTests.cs)'s
existing harness (`TestDevice` now also carries `IFinalizedPassdownRepository`, constructed the same
way as every other per-device repository). A `BuildFinalizedPassdown` helper builds a real,
fully-deserializable `FinalizedPassdownSnapshot` through the actual production serializer/hasher, plus
a small fake PDF with a matching SHA-256 — exactly what finalization would have produced, without
needing a full `AssociateWorkspace`. No change-tracker call is needed for History (§16E.10 — no hook
exists), so tests call `AddAsync` directly.

| Scenario | Result |
|---|---|
| A. A finalizes → Sync → B imports exact frozen snapshot text and byte-identical PDF | PASS |
| C. Repeated sync passes → idempotent, no re-export/re-import | PASS |
| E. First device with existing local History → bootstraps on first sync pass | PASS |
| F. Both devices already hold the identical finalized record → converges silently, no conflict | PASS |
| H. Same id, different content hash → immutable conflict; neither side overwritten; export paused | PASS |
| I. Two devices independently finalize the same date → dual-finalization conflict surfaced; both artifacts preserved; local record still syncs normally | PASS |
| J. JSON arrives before its PDF → not imported; PDF arrives later → import then succeeds | PASS |
| K. Orphan PDF with no JSON → ignored, never imported, never deleted | PASS |
| L. Corrupt/hash-mismatched PDF → rejected, never imported, local never overwritten | PASS |
| M. Malformed History JSON among valid records → processes the others, reports `MalformedCount` | PASS |
| Conflict-resolution refusal: Keep Local/Use Remote/Accept Deletion all refused for `FinalizedPassdown`; excluded from the mutable-entity conflict list | PASS |

Scenarios B (byte-identical PDF) and D (offline-then-later-import) are folded into scenario A's own
assertions (byte-for-byte `PdfBytes` equality; B only ever calls `Sync.RunAsync()` after A has already
exported, which is exactly "B was offline, A finalized, B later imports" — no separate code path
distinguishes "offline" from "hasn't synced yet"). Scenario N (sync folder unavailable → local History
remains usable) is not duplicated for History specifically — `RunAsync`'s early `NotReady`/`Disabled`
short-circuit (16C, unchanged) returns before any entity-specific import/export method (drafts, notes,
*or* History) is ever invoked, so the existing `RunAsync_FolderUnavailable_...`/
`RunAsync_SyncDisabled_...` tests already cover History's share of this guarantee without a duplicate.

## 16E.20 Failure/retry behavior

- Remote import `AddAsync` failure (defensive `SqliteException` catch, §16E.8): counted as an I/O
  failure, `SyncEntityState` left untouched, retried fresh next pass.
- PDF write success, JSON write failure (§9): caught, counted, state left dirty — next pass rewrites
  both (PDF overwrite is safe/atomic) and succeeds.
- PDF write failure before JSON (§9): the JSON write is never reached (sequential, PDF first) — no
  partial pair is ever observable remotely.
- Missing PDF companion, corrupt/hash-mismatched PDF, malformed JSON: none of these ever touch
  `finalized_passdowns` or `sync_entity_state` — always safely retryable, verified directly (§16E.19
  scenarios J/L/M).
- An unrelated draft/note import or export failure never blocks History's own import/export step in
  the same pass, and vice versa (§16E.16) — each is caught and counted independently.

## 16E.21 Existing-user migration and fresh-DB result

**No SQLite migration was added.** Both new `IFinalizedPassdownRepository` methods
(`GetByIdAsync`/`ListForSyncAsync`) are new queries over columns that have existed since Migration0008/
0009; `sync_entity_state`/`sync_conflicts` (12/13) already support `FinalizedPassdown` as a
`SyncEntityType` value with no schema change. DB schema remains **13**; Draft schema remains **3**;
Snapshot schema remains **1** — all three phase-completion targets from the spec's baseline are met
without a bump. Every existing repository/migration test (`PortableLifecycleTests`,
`Phase13GAppDataRenameCompatibilityTests`, `SqliteRepositoryTestFixture`) continues to pass unmodified,
confirming a fresh DB and an upgraded DB 12→13 (already tested in 16D) both work correctly with the new
methods available.

## 16E.22 Tests added (Phase 16E)

- `FinalizedPassdownSyncPayloadTests`, `FinalizedPassdownSyncReconciliationTests` (Application.Tests/
  PersonalSync) — payload round-trip/invariant and pure decision-logic coverage, no filesystem or
  database.
- `SyncEntityIdentityTests`/`SyncPathServiceTests` additions for `ForFinalizedPassdown`/
  `TryParseFinalizedPassdownId` and the Guid-suffixed History path/filename-parsing round-trip
  (Application.Tests/PersonalSync).
- `SqliteFinalizedPassdownRepositoryTests` additions for `GetByIdAsync`/`ListForSyncAsync`
  (Infrastructure.Tests/Storage/Repositories).
- `PersonalSyncServiceTests` — 11 new History scenarios (§16E.19) plus an extension of the existing
  privacy guard to include a finalized record (Infrastructure.Tests/PersonalSync).
- `PersonalSyncResultPresentationTests` — new file; the History status-line clauses, singular/plural
  wording, and "omitted when no History activity" case (Ui.Tests/PersonalSync).
- `InMemoryFinalizedPassdownRepository` and the two `CountingFinalizedPassdownRepository` test doubles
  (Calendar/Dashboard service tests) updated with the two new interface methods — mechanical, no
  behavior change to what those tests actually exercise.

## Final test count

1900/1900 deterministic (non-`LiveSmoke`) tests passing, run per-project (`Domain.Tests` 105,
`Application.Tests` 546, `Infrastructure.Tests` 499 excluding 11 pre-existing `LiveSmoke`-tagged
retrieval tests — 510 total including them, unchanged in count/behavior by this phase —
`Documents.Tests` 114, `Ui.Tests` 636). Up from the Phase 16D baseline of 1857/1857 (Application +22,
Infrastructure +16, Ui.Tests +5). `dotnet build CarowindsPassdown.slnx`: 0 warnings, 0 errors.

The same pre-existing, previously-documented (since Phase 6D) Avalonia headless cross-test parallelism
flake recurs here when the full `Ui.Tests` project runs with default parallelism — confirmed unrelated
to this phase's changes: isolating the three real-`Window`-constructing classes
(`RealWindowStartupTimingTests`/`Phase15ERealWindowLayoutTests`/`OperationsCalendarViewTests`) together
passes 38/38 repeatably; isolating everything else in `Ui.Tests` passes 598/598 repeatably; 38 + 598 =
636, the full project total, zero real failures attributable to Phase 16E. Per CLAUDE.md, not fixed —
out of scope, pre-existing, and explicitly called out as such by the Phase 16E brief itself (spec §35).

## Known limitations

- A legacy pre-9C finalized passdown with no frozen PDF (`HasFrozenPdf == false`) is never exported —
  there is no recovery/regeneration behavior in the product to build a payload from, matching spec
  §4/§10's explicit "do not regenerate... unless existing product behavior explicitly supports
  recovery" (it doesn't). It is simply skipped every pass, forever un-exportable; this is a narrow,
  pre-existing-data-only limitation, not a regression.
- No admin-facing review/resolution UI for a `FinalizedPassdown` conflict (immutable-integrity or
  dual-finalization) — visibility is the `HistoryConflictCount` line in the Sync Now result only
  (§16E.14/§16E.16), exactly per spec §30's explicit deferral.
- No automatic/background sync cadence — still "Sync Now" only, unchanged from 16C/16D (16F's job).
- The real Windows-host OneDrive desktop client interaction remains unverified on this (macOS/Darwin)
  development host — same limitation carried forward from every prior Personal Sync phase.

## Deferred to 16F

- Automatic/background sync cadence (timer, startup sync, save-triggered sync, filesystem watcher,
  scheduled sync).
- Richer sync status UX (last-synced indicator, device list) beyond the minimal counts/conflict-count
  surface that exists today.
- Any admin-facing resolution surface for `FinalizedPassdown` conflicts, should product direction ever
  call for one (not committed to — §16E.14's refusal is a deliberate, not merely temporary, design
  choice pending an explicit future/admin decision per spec §13).

## Phase 16F readiness

Every entity kind (drafts, notes, History) now has a working, tested manual "Sync Now" pass with its
own appropriate conflict model. 16F's automatic-cadence work has a single integration point to hook —
`IPersonalSyncService.RunAsync()` — already proven safe to call repeatedly/idempotently for all three
entity kinds; no entity-specific readiness gap was identified for 16F.

---

# Phase 16F — Personal Sync: Automatic Sync and Status UX (Implementation)

Makes Personal Sync run automatically while the app is open, on top of the completed manual sync
engine — `IPersonalSyncService.RunAsync()` itself is untouched (still the one record-reconciliation
entry point, still safe to call repeatedly/concurrently across processes/devices); this phase adds
**one new thing above it**: WHEN it runs, and how that is shown to the user. Manual "Sync Now" remains
available and now routes through the same coordinator as every automatic trigger, so it gets the same
overlap protection for free.

## 16F.1 Coordinator architecture

[`IPersonalSyncCoordinator`/`PersonalSyncCoordinator`](../src/Carowinds.Passdown.Application/PersonalSync/IPersonalSyncCoordinator.cs) —
one Application-layer singleton (no filesystem/SQLite access of its own; every dependency is an
existing interface — `IPersonalSyncService`, `IPersonalSyncConflictResolutionService`, `IClock`), owning
**when** a pass runs. `PersonalSyncService` (Infrastructure) remains completely unaware this type
exists — it still just answers "run one pass" when asked, exactly as 16C–16E left it. Per spec §8's
explicit instruction, no timer/semaphore is spread across ViewModels: `PersonalSyncSettingsViewModel`
only ever calls `RequestSync`/`SyncNowAsync` and renders `CurrentStatus`/`StatusChanged`.

Internals: one `SemaphoreSlim` (`_runGate`) ensures at most one physical call into
`IPersonalSyncService.RunAsync()` is in flight from this coordinator at a time; a single `bool
_runRequestedAgain` flag (not a queue/counter) implements the coalescing rule in §16F.9; independent
`CancellationTokenSource`s for the debounce timer, the retry timer, and the coordinator's own lifetime
let each be cancelled without disturbing the others (e.g. `SyncNowAsync` cancels a pending debounce/retry
without touching the periodic loop).

## 16F.2 Startup-sync behavior

`IPersonalSyncCoordinator.Start()` is called once, in `App.axaml.cs`'s `OnFrameworkInitializationCompleted`,
**after** `desktop.MainWindow` is already assigned (so the window is already visible/interactive) and
**before** `StartCatchUpRefresh` — `Start()` itself is synchronous and returns immediately; the startup
sync pass it fires runs as a detached background task exactly like the existing startup catch-up refresh
already did (Phase 6B). If the sync folder is unavailable, the pass simply reports `FolderUnavailable`
and schedules a retry (§16F.5) — nothing here blocks or delays the first usable window. Never resolved
(and therefore never started) by the headless `--scheduled-refresh` process
(`Program.BuildScheduledRefreshServiceProvider` reuses the same `App.BuildServiceProvider()` composition
root, but nothing in that headless path ever calls `.Start()` — spec §31's "Personal Sync operates only
while the interactive app is running").

## 16F.3 Local-change trigger behavior

`AssociatePassdownService.SaveDraftAsync`/`OperationalNotesService`'s existing `ISyncChangeTracker.MarkLocalChangeAsync`
hook (Phase 16C §7) is unchanged in both callers. What changed:
[`AutomaticSyncChangeTracker`](../src/Carowinds.Passdown.Application/PersonalSync/AutomaticSyncChangeTracker.cs)
decorates the plain `SyncChangeTracker` — after the dirty-metadata write succeeds, it calls
`IPersonalSyncCoordinator.RequestSync(SyncTriggerReason.LocalChange)`. Wired only in
`App.axaml.cs`'s composition root (`SyncChangeTracker` itself stays registered under its own concrete
type so the decorator can depend on it); nothing in the Application services changed. A tracker call
that is skipped entirely (a direct repository write bypassing the Application service, already a
documented, accepted gap since 16C) simply never requests a pass either — no new gap introduced.

## 16F.4 Debounce/coalescing behavior

`PersonalSyncCoordinator.DefaultDebounceInterval` = **5 seconds**. Chosen relative to the Associate
screen's own 800ms autosave debounce (Phase 4C): 5 seconds comfortably outlasts a realistic burst of
several autosaves from continued typing/toggling, so "edit → autosave → edit → autosave" collapses into
one sync pass "shortly afterward," per spec §4's own example — not one sync pass per autosave. Only
`SyncTriggerReason.LocalChange`/`Finalization` are debounced; every other reason (`Startup`, `Periodic`,
`Retry`, `Manual`) dispatches promptly, since each already has its own natural spacing.

## 16F.5 Finalization trigger behavior

No hook was added to `FinalizedPassdownFinalizationService`/`ScheduledPassdownFinalizer` (consistent
with 16E §10's own "avoid unnecessary hooks" precedent — History dirtiness is still decided by content-hash
comparison during `ExportHistoryAsync`'s per-pass enumeration, never a write-time notification). Instead,
[`StartupCatchUpRefresh.RunAsync`](../src/Carowinds.Passdown.Desktop/Startup/StartupCatchUpRefresh.cs)
(the one place finalization can happen inside the *interactive* process — the headless
`--scheduled-refresh` process finalizes in a separate process with no coordinator at all) calls
`IPersonalSyncCoordinator.RequestSync(SyncTriggerReason.Finalization)` immediately after
`IScheduledPassdownFinalizer.FinalizeEligibleAsync` reports `FinalizedCount > 0`. Uses `GetService` (not
`GetRequiredService`) — the coordinator is an interactive-app-only concern (§31), so a minimal/headless
service provider that never registered Personal Sync at all must not have its finalize-then-refresh
sequence broken by an unrelated missing registration (this was caught by, and fixed against,
`Phase9EIntegrationTests.StartupCatchUp_MissedCutoff_FinalizesAndHistoryAppears`, which builds exactly
such a minimal provider). A finalization that happens only in the headless scheduled process is still
picked up — just via the interactive app's next startup/periodic pass rather than an immediate trigger,
since History export is a full, cheap per-pass scan regardless of *why* a record became dirty.

## 16F.6 Periodic cadence

`PersonalSyncCoordinator.DefaultPeriodicInterval` = **5 minutes**. Order-of-magnitude per spec §6
("every few minutes, not every few seconds") — frequent enough to notice a remote change or recover a
missed local trigger within a normal work session, infrequent enough that it is never the dominant cost
of running the app (each no-op pass is a handful of cheap content-hash comparisons, §16F.15). Re-arms
itself after every tick (including after a triggered pass completes) for the app's entire running
lifetime, cancelled only by `StopAsync()`.

## 16F.7 Filesystem-watcher decision

**Not added, by design** — exactly as 16E's own "Deferred to 16F" note anticipated. Periodic (§16F.6) +
event-triggered (§16F.3/§16F.5) sync converges without one: every entity kind's dirtiness is decided by
content-hash/revision comparison during enumeration, not by "did a file-system event fire," so a missed
or noisy OneDrive filesystem event (partial writes, conflict-copy intermediate states, sync-client churn)
can never cause a record to be silently skipped — the next periodic tick re-evaluates everything fresh
regardless. Reasons against, per spec §7: OneDrive's own event stream is noisy/transient around partial
uploads and conflict copies, and a watcher adds real complexity (debouncing filesystem events, handling a
watcher's own failure modes) for a benefit periodic + event-triggered sync already delivers.

## 16F.8 Retry/backoff behavior

Classification happens in `PersonalSyncCoordinator.ApplyResult` (spec §10/§28), mapping
`PersonalSyncResult` to a `PersonalSyncAutomationState` and deciding whether the outcome is **transient**
(retry) or not:

| Outcome | Transient? | Resulting state |
|---|---|---|
| `Disabled` | No | `Off` |
| `NotConfigured` | No | `NotConfigured` |
| `FolderUnavailable` | **Yes** | `Offline` |
| `ConfigurationError` (malformed/unsupported manifest) | No | `Error` |
| `Completed`, history/mutable conflicts present | No | `HistoryConflictAttention` / `ConflictAttention` |
| `Completed`, `IoFailureCount > 0` (some transient per-file I/O failures) | **Yes** | `Pending` |
| `Completed`, clean | No | `UpToDate` |

Only the two transient rows schedule a retry — a conflict or a malformed/unsupported configuration will
never be fixed by retrying, so retrying would only be noise (spec §10's "do not spam retries indefinitely
if retry cannot resolve them," §15's "do not repeatedly auto-resolve anything"); those states are instead
picked up again by the next periodic sweep or an explicit user action (resolve the conflict, fix the
folder). Backoff schedule (`PersonalSyncCoordinator.DefaultBackoffSchedule`): **15s, 30s, 1min, 2min,
5min**, exactly the spec's own example — capped at the last (5min) step for any further consecutive
failure, never busy-looping. Reset to the first step (`_consecutiveTransientFailures = 0`) on the very
next non-transient outcome, success or otherwise.

## 16F.9 Overlap/coalescing behavior

Every automatic trigger funnels through `PersonalSyncCoordinator.TriggerRunAsync`, which tries to acquire
`_runGate` without blocking (`WaitAsync(0)`). If busy, it sets `_runRequestedAgain = true` and returns —
never a second parallel call into `IPersonalSyncService.RunAsync()`. The pass currently holding the gate,
once it finishes, checks that flag itself and — if set — clears it and runs exactly one more pass before
releasing the gate; a further trigger arriving *during* that follow-up pass sets the flag again for
*its own* one more follow-up. This produces "run #1 active → local save occurs → one follow-up run after
#1," never an unbounded queue (spec §9), verified directly by
`LocalChangeDuringActiveSync_ProducesExactlyOneFollowUpPass_NeverParallel`.

## 16F.10 Offline/unavailable behavior

`PersonalSyncAutomationState.Offline` maps to the text **"Sync unavailable"** with detail **"Changes are
saved locally and will sync automatically once the folder is reachable again."** — deliberately no
"lost"/"failed"/"error" language (spec §11), verified by
`OfflineStatus_NeverUsesAlarmingDataLossLanguage`. Local persistence was never on this code path to begin
with (§16F.1's core invariant, unchanged since 16A) — Offline is purely a *sync* status, never a save
status. `HasPendingChanges` is `true` in this state; the retry schedule (§16F.8) picks the folder back up
automatically the moment it becomes reachable again, no manual intervention required.

## 16F.11 Status model

[`PersonalSyncAutomationState`/`PersonalSyncAutomationSnapshot`](../src/Carowinds.Passdown.Application/PersonalSync/PersonalSyncAutomationSnapshot.cs) —
a new, richer status layered **on top of**, never replacing, the existing 16B `PersonalSyncStatus`
(Off/NotConfigured/Ready/FolderUnavailable/ConfigurationError — still answers "is the folder/manifest
itself configured and reachable"). States: `Off`, `NotConfigured`, `Syncing`, `UpToDate`, `Pending`,
`Offline`, `ConflictAttention`, `HistoryConflictAttention` (kept **distinct** from `ConflictAttention` per
spec §16 — never mistaken for an ordinary mutable conflict), `Error`. Metadata carried alongside:
`IsSyncRunning`, `LastSuccessfulSyncUtc`, `LastAttemptUtc`, `HasPendingChanges`, `ConflictCount`,
`HistoryConflictCount`, `LastResultSummary`, `LastErrorMessage`, `RetryAttempt`. Nothing here is persisted
across a restart beyond what already lives in `sync_entity_state`/`sync_conflicts`/`sync-config.json` —
`PersonalSyncAutomationSnapshot` itself resets to `Initial` on every process start and is rebuilt fresh by
the startup pass, matching spec §12's "do not persist unnecessary transient status unless needed across
restart" (nothing here is needed across restart; §13's `LastSuccessfulSyncUtc` is display-only, not a
reconciliation input, so losing it across a restart is cosmetic, not a correctness gap).

## 16F.12 Pending-state semantics

`HasPendingChanges` is `true` only for `Pending`/`Offline` — never merely because a pass *ran* (spec §14's
explicit "do not claim Up to date merely because a sync attempt ran if conflicts remain/exports
failed/folder unavailable"). A clean `Completed` result with zero conflicts and zero I/O failures is the
only path to `UpToDate`.

## 16F.13 Last-successful-sync behavior

[`PersonalSyncAutomationStatusPresentation.BuildLastSyncedText`](../src/Carowinds.Passdown.Desktop/Presentation/PersonalSyncAutomationStatusPresentation.cs)
renders **"Last synced: 2:41 PM"** — local display time, same `ToLocalTime():h:mm tt` formatting
`SaveStatusPresentation` already established for the Associate autosave footer (Phase 4C), not a new
convention. Null (line hidden) until the very first successful pass. Purely display metadata — never an
input to any reconciliation decision (16A §D's own warning against trusting wall-clock ordering still
holds; this value is never read by `SyncReconciliation`/`FinalizedPassdownSyncReconciliation`).

## 16F.14 Mutable-conflict / History-conflict status

The coordinator re-queries `IPersonalSyncConflictResolutionService.GetConflictsAsync()` after every pass
(cheap DB read) rather than trusting only that pass's own counters — so a conflict detected several
passes ago and still unresolved keeps showing correctly even on a pass that found nothing new.
`SyncEntityType.FinalizedPassdown` conflicts are split into `HistoryConflictCount`; everything else into
`ConflictCount`. Existing 16D conflict review/resolution UX (Keep This Device/Use Other Device/Accept
Deletion) is unchanged and remains the only way to clear a mutable conflict — the coordinator never
touches conflict resolution itself (spec §15's "do not repeatedly auto-resolve anything").

## 16F.15 Settings UX result

Additive only, per spec §17's "keep it compact": one new chip (`AutomationStatusPresentation.Text`) plus
an optional one-line detail and a "Last synced" line, inserted between the existing readiness chip/detail
and the "Sync Now" button in
[`PersonalSyncSettingsView.axaml`](../src/Carowinds.Passdown.Desktop/Views/PersonalSyncSettingsView.axaml).
No new screen, no device list, no monitoring dashboard. "Sync Now" now calls
`IPersonalSyncCoordinator.SyncNowAsync()` instead of `IPersonalSyncService.RunAsync()` directly (§16F.24).

## 16F.16 Enable/disable behavior

**No coordinator-level special-casing was needed.** The periodic loop keeps ticking regardless of the
enabled toggle — but each tick's call into `IPersonalSyncService.RunAsync()` itself already returns
`Disabled` near-instantly (the existing 16C guard, unchanged) whenever `PersonalSyncEnabled` is false, so
disabling sync makes every subsequent automatic/periodic/retry pass a cheap no-op, never actual file/DB
work, without the coordinator needing to know *why*. Turning it back on makes the very next pass (the
next periodic tick, or the next local-change/manual trigger) resume normal reconciliation — nothing
about local data, conflicts, tombstones, or `DeviceId` is ever reset by toggling this (all of that lives
in SQLite/flat config files this toggle never touches).

## 16F.17 Folder-switch behavior / 16F.18 SyncSpaceId

**Audited and fixed** — this was the one genuinely release-critical gap spec §19/§20 called out.
[`SyncManifest.SyncSpaceId`](../src/Carowinds.Passdown.Application/PersonalSync/SyncManifest.cs) (a
`Guid`, default `Guid.Empty` for backward JSON compatibility) is a stable identity for one sync
folder/manifest — generated once by `SyncManifestService.WriteNewManifestAsync` for a brand-new folder,
and **backfilled once** into any pre-16F manifest `SyncManifestService.EnsureManifestAsync` reads with the
empty default (preserving `CreatedUtc`/`SyncSchemaVersion`, rewritten exactly once via the same
`AtomicFileWriter` every other manifest write uses).
[`SyncConfiguration.LastKnownSyncSpaceId`](../src/Carowinds.Passdown.Application/PersonalSync/SyncConfiguration.cs)
(nullable `Guid`, also defaulted for back-compat) is the local, per-device record of which sync universe
this device last reconciled against — itself device state, never synced, exactly like `SyncRootPath`.

`PersonalSyncService.ReconcileSyncSpaceAsync` runs at the top of every `RunAsync` pass, once the folder is
confirmed `Ready`:

- **First-ever sync** (`LastKnownSyncSpaceId` is `null`): adopts the current folder's `SyncSpaceId`, no
  reset — nothing has been synced to anything yet.
- **Same `SyncSpaceId` as last time**: no-op, the overwhelming common case on every normal pass.
- **A genuine mismatch** (switched to a different, possibly-empty folder — including switching back to an
  old folder that was itself reset in between): `ISyncEntityStateRepository.ClearAllAsync()` +
  `ISyncConflictRepository.ClearAllAsync()` wipe every entity's local sync bookkeeping (both new,
  §16F.19), then every record is re-evaluated fresh through the *normal* reconciliation rules on the very
  same pass — never a blind re-export (the new folder may already hold equivalent content from another
  device that used it first) and never a silent "already synced" skip (the exact bug this fixes: without
  this check, a record's `SyncedContentHash` from the *old* folder would still match the current local
  content, so the export loop would wrongly conclude "already shared" against a folder it was never
  actually written to).

Verified directly (`PersonalSyncServiceTests`): a first sync adopts the id with no reset; switching to a
different empty folder re-exports an already-"synced" record instead of skipping it; switching back to
the original folder reconciles safely with no spurious conflict and the record still present. Deliberately
never `DeviceId` reused as `SyncSpaceId` (spec's explicit warning) — one identifies a folder, the other a
device; two devices sharing one folder must never look like "the folder changed."

## 16F.19 First-enable / bootstrap behavior

Unchanged from 16C/16E: `ExportDraftsAsync`/`ExportNotesAsync`/`ExportHistoryAsync` already *are* the
bootstrap path (a never-synced record has no `SyncEntityState` row, so it trivially exports on the first
pass that finds it) — no separate "first enable" code path was added or needed. What 16F adds on top is
only that this first pass now runs automatically (the startup trigger, §16F.2) instead of requiring a
manual "Sync Now" click; the toggle itself (`PersonalSyncSettingsViewModel.SetEnabledAsync`) is unchanged
and still returns immediately (persists the flag, no bootstrap work performed synchronously) — the
coordinator picks up the change on its next periodic tick or the next `RequestSync` call.

## 16F.20 Remote-change periodic import

Composition of two already-independently-tested things: `PersonalSyncCoordinatorTests.PeriodicIntervalElapsing_TriggersAnAdditionalPass_AndReArmsItself`
proves the coordinator calls `IPersonalSyncService.RunAsync()` on the periodic cadence with no local
trigger involved; the existing Phase 16C/16E `PersonalSyncServiceTests` two-device scenarios (A exports →
B's `RunAsync()` imports) prove that call itself correctly imports a remote-only change. No new dedicated
end-to-end "coordinator + real two-device harness" test was written combining both — a reasonable,
declared scope boundary given the two halves are each already proven; flagged here rather than silently
assumed.

## 16F.21 Shutdown behavior

**No bounded final sync was added — by design**, matching spec §23's explicit permission. `App.axaml.cs`'s
`AttachShutdownFlush` now also calls `IPersonalSyncCoordinator.StopAsync()` (cancels the periodic loop and
any pending debounce/retry timer) immediately after the existing autosave flush, before `desktop.Shutdown()`
actually closes the window. `StopAsync()` deliberately does **not** wait for a sync pass that happens to be
mid-flight — only for the periodic-loop task's own delay to observe cancellation, which is immediate. An
interrupted pass's own file writes are safe regardless (atomic temp-then-rename, unchanged since 16A §F) —
a half-written temp file is simply abandoned, never observed as final by anything. Pending local changes
(never lost — local persistence always completed before any sync trigger fires, §16F's own core invariant)
sync on next startup (§16F.22).

## 16F.22 Startup-after-pending-exit / 16F.23 Retry-across-restart

Both work **without any new state** — the existing 16C content-hash/revision model already makes this
recoverable: an edit made just before exit already updated `SyncEntityState.LocalRevision`/local content
in SQLite (via `SaveDraftAsync`'s existing "SQLite first, sync metadata second" ordering, unchanged) even
if the coordinator's debounced sync pass never got to run before the process closed. On next launch,
`Start()`'s own startup trigger (§16F.2) runs a normal pass — `ExportDraftsAsync`/`ExportNotesAsync` see the
same "local content hash ≠ `SyncedContentHash`" dirtiness they always would, with no notion of "this was
pending across a restart" needed at all. The same logic covers a failed export (folder was unavailable at
exit): the record's `SyncEntityState` was never advanced to reflect a successful export, so the next
startup pass (now with the folder reachable) retries it automatically, no manual intervention required.

## 16F.24 Multiple-instance protection

[`PersonalSyncProcessLock`](../src/Carowinds.Passdown.Infrastructure/PersonalSync/PersonalSyncProcessLock.cs) —
audited first: no existing single-app-instance enforcement exists anywhere in this codebase (confirmed:
no `Mutex`/"SingleInstance" hits under `src/` before this phase), so *something* new was needed per spec
§26's explicit "do not assume it cannot happen." Mirrors `FileScheduledRefreshLock`'s own proven approach —
one exclusively-held (`FileShare.None`) local lock file — rather than a named `Mutex`: a `Mutex`'s
ownership is thread-affine (`ReleaseMutex` must run on the exact thread that acquired it), which a
long-lived `async` sync pass cannot guarantee across its many `await` suspension points (this was actually
tried first and failed with `ApplicationException: Object synchronization method was called from an
unsynchronized block of code` under real test conditions — the file-lock approach has no such
requirement). Held only for the duration of one `PersonalSyncService.RunAsync()` pass — acquired
immediately after the existing in-process `SemaphoreSlim` gate succeeds, released via the same `finally`.
A second process attempting to hold it while the first still does gets `PersonalSyncResult.AlreadyRunning()`
back — the same, already-existing outcome a second concurrent in-process call gets. Never the shared
OneDrive folder itself (spec's explicit prohibition) and never `FileScheduledRefreshLock`'s own file
(sharing it would make a sync pass wait on an unrelated, possibly long-running scheduled *retrieval*
refresh). Verified directly (`PersonalSyncProcessLockTests`, via reflection against a test-only explicit
path, never the shared real `AppPaths.LocalApplicationDataDirectory` location).

## 16F.25 Manual Sync Now / 16F.26 Result classification

`PersonalSyncSettingsViewModel.SyncNowAsync` now calls `IPersonalSyncCoordinator.SyncNowAsync()` instead
of `IPersonalSyncService.RunAsync()` directly (§16F.15). That method cancels any pending debounce/retry
timer (bypasses debounce, §27), then does a **blocking** `_runGate.WaitAsync()` (not the non-blocking
`WaitAsync(0)` every automatic trigger uses) — if a pass is already running, Manual waits for it to finish
and then always runs one guaranteed fresh pass of its own before returning, never in parallel and never
silently coalesced away without the user seeing a real result
(`SyncNowAsync_WhileAPassIsActive_WaitsThenRunsOneFreshPass_NeverInParallel`). Result classification
(§16F.8's table) is unchanged/reused for both automatic and manual passes — never a single flattened
"Failed" (spec §28); `PersonalSyncResultPresentation.Format` (16C, unchanged) still renders the
manual-sync summary line shown after a click.

## 16F.27 Logging/privacy behavior

`PersonalSyncCoordinator` logs at pass start/finish (`LogInformation`: reason, outcome, import/export/
unchanged/conflict/malformed/I-O-failure **counts** only), a coalesced-follow-up note (`LogDebug`), and an
unexpected-exception/conflict-refresh-failure warning (`LogWarning`, exception object only — no payload).
Never logs draft/note/PDF/snapshot content, credentials, usernames, or full JSON payloads — consistent
with every prior Personal Sync phase's own logging discipline (16C §29 established this; nothing new here
weakens it). `DeviceId`/`SyncSpaceId` may appear in Infrastructure-layer logs/state as bare GUIDs
(unchanged, 16A §C/16F.18) — never a username, machine name, email, or local AppData path.

## 16F.28 No-retrieval-coupling verification

Grepped `PersonalSyncCoordinator`/`AutomaticSyncChangeTracker`/`StartupCatchUpRefresh`'s new code for any
reference to `IRefreshOrchestrator`/`RefreshMode`/`IScheduledRefreshRunner`/HTTP/Playwright: none exist.
The coordinator's only dependencies are `IPersonalSyncService`, `IPersonalSyncConflictResolutionService`,
and `IClock` — no public-data retrieval type is reachable from it at all. `StartupCatchUpRefresh`'s
existing refresh-runner call (`IScheduledRefreshRunner`) and the new Personal Sync trigger are two
independent statements in sequence, not a shared code path — a Personal Sync failure/exception is caught
entirely inside `IPersonalSyncCoordinator.RequestSync` (fire-and-forget, never throws to its caller) and
can never prevent or alter the refresh-runner call that follows it, and vice versa. Confirms spec §30
("Personal Sync automatic cadence must remain independent of Check Online/Deep Refresh/scheduled-refresh/
source freshness/park-hours retrieval") holds by construction, not merely by absence of a bug found.

## 16F.29 Loop prevention

No new loop-prevention logic was added — the existing content-hash/revision semantics (16C/16E, unchanged)
already make one impossible, and automatic cadence does not change that: a device's own periodic re-scan
of a file it just exported hits the same `envelope.DeviceId == deviceId` self-skip (History) or
`SyncedContentHash` match (drafts/notes) it always would on any repeated `RunAsync()` call, manual or
automatic. Automatic cadence only means `RunAsync()` is called *more often*, never that it is called with
different inputs that could produce a different (looping) outcome.

## 16F.30 No-op performance / storage impact

No new measurement was needed beyond what 16C–16E already established and this phase's own tests confirm
by construction: a periodic tick against nothing-changed content is exactly the same cheap
content-hash-comparison-then-skip path every existing "already in sync — no unnecessary rewrite" test
(§6/§15 of 16C/16E) already covers — automatic cadence adds *more calls* to that same cheap path, not a
new expensive one. No full PDF re-hash/rewrite happens on a no-op pass (unchanged: `ExportHistoryAsync`
only re-hashes/writes when `SyncedContentHash` doesn't already match). `PersonalSyncCoordinator`'s own
per-pass overhead beyond the `RunAsync()` call itself is one `GetConflictsAsync()` DB read and a handful
of in-memory comparisons/allocations — no filesystem I/O of its own outside of `RunAsync()`/`ReconcileSyncSpaceAsync`.

## 16F.31 Schema/config impact / 16F.32 Existing-user upgrade / 16F.33 Fresh-install result

**No SQLite migration** — DB schema remains **13** (unchanged since 16D). `SyncSpaceId`/`LastKnownSyncSpaceId`
(§16F.18) live entirely in the existing flat JSON files (`manifest.json`, `sync-config.json`), each already
tolerant of unknown/missing fields via ordinary `System.Text.Json` optional-parameter defaults — no
version bump was needed for either file format. An existing 16B–16E user's `sync-config.json` (no
`LastKnownSyncSpaceId` field) and `manifest.json` (no `SyncSpaceId` field) both deserialize successfully
with the new fields defaulting to `null`/`Guid.Empty`; the very next sync pass backfills the manifest's
`SyncSpaceId` once and records it as this device's `LastKnownSyncSpaceId` with **no reset** (the
"first-ever-sync" branch in §16F.18 — a pre-16F device has necessarily already synced against this exact
folder before, so nothing is treated as a folder switch). `DeviceId`, the selected folder, all sync
metadata (`sync_entity_state`), all conflicts (`sync_conflicts`), and all tombstones are preserved exactly
as before — verified by construction (no migration touches any of those tables/files) and by the existing
16B–16E upgrade-path tests continuing to pass unmodified. A fresh install / first-ever-enable
(`SyncConfiguration.Disabled`, no manifest) proceeds exactly as 16B always did, now simply followed by an
automatic startup/periodic cadence instead of requiring a manual click.

## 16F.34 Tests added

- `PersonalSyncCoordinatorTests` (Application.Tests/PersonalSync, 18 tests) — startup trigger, debounce
  coalescing, overlap/one-follow-up, clean-success → `UpToDate`, retry/backoff increase-then-cap, backoff
  reset on success, manual bypass (idle and while-active), periodic re-arming, `StopAsync` cancelling the
  periodic loop and a pending debounce, mutable/History conflict status distinction, `Disabled`/
  `NotConfigured` passthrough, `StatusChanged` firing. Uses `ManualDelayScheduler` (new,
  Application.Tests/TestSupport) so every timing assertion is deterministic — no test sleeps for a real
  interval (spec §32).
- `SyncManifestServiceTests` additions (Infrastructure.Tests) — a new manifest gets a non-empty
  `SyncSpaceId`; an existing manifest returns the same id on every call; a pre-16F manifest is backfilled
  exactly once, preserving `CreatedUtc`.
- `PersonalSyncServiceTests` additions (Infrastructure.Tests) — first-ever sync adopts the folder's
  `SyncSpaceId` with no reset; switching to a different empty folder re-exports an already-"synced"
  record instead of silently skipping it (the release-critical §19/§20 scenario); switching back to the
  original folder reconciles safely with the record still present and no spurious conflict.
- `PersonalSyncProcessLockTests` (new, Infrastructure.Tests) — acquire-when-free, a second acquire while
  held returns null, a second acquire after release succeeds — via reflection against a test-only path,
  never the shared real per-user AppData location.
- `PersonalSyncSettingsViewModelTests` additions (Ui.Tests) — the coordinator's current status is applied
  immediately on construction; every `PersonalSyncAutomationState` renders its expected chip text;
  `LastSyncedText` is null before any successful sync; the `Offline` detail text never uses alarming
  data-loss language; "Sync Now" routes through and is counted by the coordinator, not the raw sync
  service (two existing tests updated accordingly). `FakePersonalSyncCoordinator`/
  `FakePersonalSyncService`/`FakePersonalSyncConflictResolutionService` (Application.Tests/TestSupport)
  and `FakePersonalSyncCoordinator` (Ui.Tests/TestSupport) are new/reused fakes.
- `SyncChangeTrackerTests`' in-file `InMemorySyncEntityStateRepository` gained `ClearAllAsync` — mechanical,
  required by the `ISyncEntityStateRepository` interface addition (§16F.18), no behavior change to what
  that test file actually exercises.

## 16F.35 Final deterministic test count / build result

**1938/1938** deterministic (non-`LiveSmoke`) tests passing — up from the Phase 16E baseline of
1900/1900 (Domain 105 unchanged, Application 546 → 564 [+18], Infrastructure 499 → 508 [+9: 6 folder-switch/
manifest + 3 `PersonalSyncProcessLockTests`], Documents 114
unchanged, Ui.Tests 636 → 647 [+11: 598 → 609 in the non-real-Window group, 38 real-Window tests
unchanged]). `dotnet build CarowindsPassdown.slnx`: **0 warnings, 0 errors**. The same pre-existing,
previously-documented (since Phase 6D) Avalonia headless cross-test parallelism flake — unrelated to this
phase — was worked around using the established isolated strategy (the three real-`Window`-constructing
classes run together: 38/38 repeatably; everything else in `Ui.Tests` runs together: 609/609 repeatably;
38 + 609 = 647, the full project total). Not fixed, per CLAUDE.md — out of scope, pre-existing.

## 16F.36 DB/Draft/Snapshot versions

DB schema **13** (unchanged). Draft schema **3** (unchanged). Snapshot schema **1** (unchanged). No
migration was added — see §16F.31.

## 16F.37 Docs updated

This document. Also worth noting for a future reader: `SyncManifest`/`SyncConfiguration`'s XML doc
comments were extended in place to describe `SyncSpaceId`/`LastKnownSyncSpaceId` rather than duplicating
that explanation only here.

## Known limitations

- No dedicated end-to-end test combines the coordinator's periodic trigger with a real two-device shared
  folder in one test (§16F.20) — each half is proven independently; a combined test is a reasonable next
  step but was not required to validate the behavior, which is a straightforward composition of two
  already-tested pieces.
- `PersonalSyncProcessLock` uses a fixed, non-configurable production path
  (`AppPaths.LocalApplicationDataDirectory\personal-sync.lock`) — correct and sufficient for its actual
  purpose (one lock per device/profile), but means `PersonalSyncService` itself cannot be unit-tested for
  cross-process behavior without reflection against an explicit test path, which is what
  `PersonalSyncProcessLockTests` does.
- `LastSuccessfulSyncUtc`/`LastAttemptUtc`/retry-attempt count reset to defaults on every process
  restart (§16F.11) — cosmetic only ("Last synced" briefly shows nothing until the next pass completes),
  never a correctness gap, since none of it is a reconciliation input.
- Real Windows-host/OneDrive-client validation (does a genuine, running OneDrive desktop client actually
  pick up and propagate these automatic writes the way this design assumes) remains unverified on this
  (macOS/Darwin) development host — the same standing limitation carried forward from every prior Personal
  Sync phase, explicitly reserved for 16G.

## Deferred to 16G

- Real Windows/OneDrive two-device validation (every prior phase's own standing deferral).
- Any admin-facing resolution surface for a `FinalizedPassdown` conflict, should product direction ever
  call for one (16E §14's deliberate, not merely temporary, deferral — unchanged by this phase).
- A combined coordinator-plus-two-device-harness automated test (§16F.20/Known limitations) — nice-to-have
  hardening, not required for correctness.
- Any Manager-workflow Personal Sync scope (out of the current release per CLAUDE.md; unrelated to 16F).

## Phase 16G readiness

Automatic sync, retry/backoff, and status UX are all implemented and tested against the fake-clock/fake-
scheduler harness this phase introduces. What remains genuinely unverified across every Personal Sync
phase to date — real Windows 11 hosts with a real, running OneDrive desktop client, actually observing
this design's file-layout/atomic-write/conflict-copy assumptions against production OneDrive behavior —
is exactly what 16G exists for; nothing in 16F narrows that scope further, and nothing in 16F introduces a
new gap that needs closing before that validation can begin.

# Phase 16G — Final Real-World Validation and Release Closure

**Environment constraint, stated up front rather than discovered partway through:** this phase's session
ran on a macOS/Darwin (arm64) host with no Windows 11 machine or VM, no Microsoft OneDrive desktop client,
and no OneDrive account reachable from this environment — the same non-Windows-host limitation every prior
Personal Sync phase (16A-16F) and every prior Windows-packaging phase (7A-15F) has carried, but this time
squarely inside the one phase whose entire purpose is real-Windows/real-OneDrive validation. Per this
phase's own explicit instruction ("If two physical/VM Windows environments are unavailable, use the
strongest realistic setup possible and clearly mark anything not actually executed" / "Do not fabricate
PASS"), **every item in this phase's task list that requires a running Windows OneDrive client, a native
Windows folder picker, real DPI scaling, SmartScreen/Defender, or two physical/virtual devices is marked
`NOT EXECUTED` below** — not attempted, not simulated, not inferred from code review. What this phase
*could* verify directly — build/test/release-artifact correctness, and the one documented Phase 16F test
gap — was verified directly and is marked accordingly. The scope of this pass (automated validation +
doc/artifact closure, with all real-device items explicitly deferred) was confirmed with the product owner
before starting, rather than silently assumed.

## 16G.1 Combined coordinator + two-device automated test (closes the §16F.20 gap)

The one documented Phase 16F gap — §16F.20 "no dedicated end-to-end 'coordinator + real two-device
harness' test was written combining [the coordinator's periodic trigger] and [a real two-device shared
folder]" — is closed by
[`PersonalSyncCoordinatorEndToEndTests`](../tests/Carowinds.Passdown.Infrastructure.Tests/PersonalSync/PersonalSyncCoordinatorEndToEndTests.cs)
(new, `Infrastructure.Tests/PersonalSync/`, 2 tests):

- `DraftEditedOnDeviceA_PropagatesToDeviceB_ThroughCoordinatorLocalChangeAndPeriodicTriggers` — two real,
  independent `PersonalSyncCoordinator` instances, each wrapping its own real `PersonalSyncService`/SQLite
  database, both pointed at one shared temp folder. Device B's coordinator is started first and left
  running; Device A's coordinator debounces a local draft edit (`SyncTriggerReason.LocalChange`, real 5s
  interval, elapsed via a deterministic test scheduler — never a real sleep) and exports it. The test then
  asserts Device B has **not yet** imported the change (no manual trigger, no direct `RunAsync()` call
  bypassing the coordinator), elapses Device B's own periodic interval, and only then asserts the draft
  landed in Device B's local database with the correct payload and zero conflicts.
- `NoteCreatedOnDeviceA_PropagatesToDeviceB_ThroughStartupTriggersOnBothSides` — the same shared-folder
  harness, exercised through both coordinators' one-time startup trigger instead, with an Operational Note
  (stable GUID asserted preserved) instead of a draft.
- Both tests use a local `TestDelayScheduler` (Infrastructure.Tests) rather than reusing
  `Application.Tests`' internal `ManualDelayScheduler` — that type is `internal` to a different test
  assembly with no `InternalsVisibleTo` grant to `Infrastructure.Tests` (confirmed by inspection before
  writing this test); duplicating the ~20-line scheduler was simpler and lower-risk than adding a new
  cross-assembly visibility grant for one test file.
- Run 5 times in isolation and as part of the full suite: 100% deterministic, no observed flake (unlike the
  pre-existing, unrelated Avalonia headless cross-test issue — see §16G.6).

This composes the two previously-independent proofs (`PersonalSyncCoordinatorTests`'s fake-service timing
proof, `PersonalSyncServiceTests`'s real-two-device direct-`RunAsync()` proof) into one real, coordinator-
driven, two-device scenario — the gap's own wording exactly. Real Windows/OneDrive-client validation
(§16G.2-§16G.35 below) remains a materially different, still-outstanding kind of proof this test does not
and cannot substitute for — it proves the app's own scheduling/reconciliation logic is correct against a
shared folder; it says nothing about how a real, running OneDrive desktop client behaves against that same
folder (partial uploads, Files On-Demand placeholders, conflict-copy filenames, sync client timing).

## 16G.2-16G.35 Real Windows/OneDrive-client validation — NOT EXECUTED

Every one of this phase's items requiring an actual Windows 11 OneDrive desktop client, two physical/
virtual Windows devices, native Windows folder picker interaction, real DPI scaling, Windows Task
Scheduler execution, SmartScreen/Defender observation, or double-clicking a built `.exe`/`.cmd` on Windows
is **NOT EXECUTED** this phase, for the environment reason stated above — not simulated, not inferred, not
partially claimed. This covers (using this document's own §-numbering for traceability against the phase
task list): real OneDrive sign-in and folder setup; first-device bootstrap and second-device initial join
against a real synced folder; A→B and B→A automatic draft/note propagation timing on real devices; real
finalized-History/PDF sync with byte-identical-PDF verification across two real machines; offline/
reconnect behavior against a real paused/disconnected OneDrive client; closed-app startup recovery and
remote-change-while-closed behavior on real hardware; concurrent draft/note conflict creation and
Keep-This-Device/Use-Other-Device resolution on two real devices; tombstone deletion (also structurally
**NOT APPLICABLE** — no product delete UI exists to drive it manually; see 16D's own note); dual-
finalization conflict on two real devices; folder-switch validation against a real OneDrive client; Files
On-Demand placeholder/"Always keep on this device" behavior; OneDrive conflict-copy-filename observation;
pause/resume; multiple local app instances and crash/lock recovery on a real Windows machine; real-time
observation of debounce/periodic/retry cadence against a live OneDrive client; manual Sync Now on Windows;
Settings status UX at 100/125/150% scaling; the native Avalonia/Windows folder picker; a real inspection of
an actual synced OneDrive folder's contents; actual sync-folder size from real usage; real Windows startup
timing for both release forms; SmartScreen/Defender first-launch behavior; Windows icon/titlebar rendering
and 100/125/150% scaling across every screen; and live Windows Task Scheduler registration/run.

**Nothing above was skipped by oversight.** Everything that *is* verifiable without a Windows/OneDrive
environment — the code paths these behaviors depend on, the release artifacts that would be tested, and
the automated test suite that exercises the underlying logic — was exercised this phase and is recorded in
§16G.1, §16G.3-16G.6 below. A concise procedure for a human with real Windows machines and an actual
OneDrive account to complete the outstanding items is in §16G.7.

## 16G.3 Scheduled-refresh regression — verified by code audit (not execution)

Section 36's concern ("Personal Sync must not have affected public-data scheduled refresh") is checkable
without Windows execution: a repo-wide search for scheduled-task registration call sites
(`New-ScheduledTaskAction`/`ScheduledTaskPlan.TaskName`/`RegisterTask`) across `src/` and
`packaging/windows/*.ps1` turns up exactly one task identity, `"Carowinds Passdown Daily Refresh"`
(`ScheduledTaskPlan.cs`, `Install-ScheduledRefresh.ps1`, `Uninstall-ScheduledRefresh.ps1`,
`docs/scheduled-refresh-task-scheduler.md`) — no second, Personal-Sync-specific scheduled task exists
anywhere in the codebase, and nothing under `PersonalSync/` (Application, Infrastructure, or Desktop)
references `ScheduledTaskPlan`, `Install-ScheduledRefresh.ps1`, or Windows Task Scheduler at all (§16F.28
already confirmed the reverse direction — no retrieval/scheduler coupling from Personal Sync). This is a
**PASS by static verification** for "no new Personal Sync scheduled task" and "Deep Refresh still
independent"; actually running the existing (unchanged) scheduled task on a real Windows host remains
**NOT EXECUTED**, carried forward from every prior phase (last real execution: Phase 6D, per
`docs/scheduled-refresh-task-scheduler.md`'s "Result — manually validated by the user on Windows 11").

## 16G.4 Release candidates built and validated (structurally, not by launching on Windows)

Built fresh from the current working tree (post-16F) on this macOS dev host, mirroring
`packaging/windows/Publish-Windows.ps1`/`Build-PortableRelease.ps1` exactly (no `pwsh` available on this
host, same limitation as every prior rebuild — the underlying `dotnet publish`/staging/zip steps were run
directly, matching those scripts' own logic line for line):

- **Multi-file ZIP** (primary, unchanged designation): `dotnet publish ... -r win-x64 --self-contained true
  -p:PlaywrightPlatform=win-x64` → 258 files staged, `.pdb`s removed, `tools/`
  (`Install-ScheduledRefresh.ps1`/`Uninstall-ScheduledRefresh.ps1`) + both `.cmd` helpers + `README.txt` +
  `TESTER-GUIDE.txt` added, forbidden-pattern check clean (no `.git`, `.db`/`-wal`/`-shm`, `CLAUDE.md`,
  `AGENT_BUILD_SPEC.md`, `.pdb`, `.DS_Store`), zipped, extracted to a separate scratch directory and
  `diff -rq`'d byte-for-byte identical against the staging folder, `.exe` SHA-256 inside the extraction
  independently matched the staged copy.
- **Single-file EXE** (experimental/deferred, unchanged designation — see §16G.5 for why it is not
  promoted this phase): `dotnet publish ... -p:PublishSingleFile=true -p:PublishReadyToRun=true
  -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -p:PlaywrightPlatform=win-x64` →
  publish output contains exactly the same two files as every prior single-file rebuild
  (`Carowinds.Passdown.Desktop.exe` + `LatoFont/OFL.txt`, no loose `appsettings.json`, no loose font
  files), confirmed a valid `PE32+ executable (GUI) x86-64, for MS Windows` via `file`, forbidden-pattern
  check clean.
- Exact artifacts and hashes: see `docs/release-manifest.md`'s new Phase 16G entry — these are the exact
  bytes any future Windows-host validation pass should test, not a re-derived or re-built copy.
- **What this validates**: publish correctness, packaging content/hygiene, ZIP round-trip integrity — all
  independent of a Windows host. **What this does not validate**: whether either artifact actually
  launches, renders, or runs Personal Sync correctly on real Windows 11 — that remains **NOT EXECUTED**
  (§16G.2), same as every release candidate since Phase 7A.

## 16G.5 Single-file promotion decision: not promoted this phase

Per this phase's own explicit instruction ("Do not promote without evidence"): the single-file candidate
was **not** launched, exercised, or observed on a real Windows host this phase (no Windows environment was
available — §16G.2), so there is no new evidence to weigh against the standing Phase 15F decision. **The
multi-file ZIP remains primary; the single-file EXE remains experimental/deferred**, unchanged from Phase
15F. This is a "no new evidence" outcome, not a re-affirmed rejection — the single-file build itself
continues to publish cleanly and pass every structural check available without Windows execution (§16G.4);
promoting it still requires an actual Windows launch, which remains outstanding.

## 16G.6 Automated regression suite

Baseline before this phase: **1938/1938** deterministic tests (§16F.35), `dotnet build`: 0 warnings/0
errors. After adding `PersonalSyncCoordinatorEndToEndTests` (§16G.1, +2 tests):

- `dotnet build CarowindsPassdown.slnx` — **0 warnings, 0 errors.**
- `dotnet test CarowindsPassdown.slnx --filter "Category!=LiveSmoke"` — **1940/1940** deterministic
  (Domain 105 unchanged, Application 564 unchanged, Documents 114 unchanged, Infrastructure 508 → 510
  [+2, this phase's new test file], Ui.Tests 647 unchanged), using the same established isolation strategy
  as every phase since 16D/15F for the pre-existing, unrelated Avalonia headless cross-test flake (the
  three real-`Window`-constructing classes — `OperationsCalendarViewTests`,
  `Phase15ERealWindowLayoutTests`, `RealWindowStartupTimingTests`, 38 tests — run together cleanly in
  isolation, 38/38 repeatably; everything else in `Ui.Tests` runs together cleanly, 609/609 repeatably;
  when both groups share one test-host process, 11-12 of the 38 intermittently fail with the same
  documented `System.InvalidOperationException: The calling thread cannot access this object because a
  different thread owns it.` signature, confirmed this phase to be the identical, pre-existing symptom —
  not a new or disguised failure). No deterministic test failed in any configuration this phase.
- The new test file itself was additionally run 5 times in isolation with zero failures (§16G.1) — the
  real-SQLite-backed two-coordinator scenario is not itself a source of the flake above (it constructs no
  Avalonia `Window`).

## 16G.7 Manual validation procedure for a future Windows/OneDrive pass

Not executed this phase (§16G.2); recorded here as the concrete next step for whoever has access to real
Windows 11 machines and a personal OneDrive account. Uses the exact artifacts hashed in §16G.4/
`docs/release-manifest.md`'s Phase 16G entry — do not rebuild before testing unless a real defect is found
and fixed, in which case rebuild and re-hash before shipping.

1. On two Windows 11 machines (or one machine + one VM) signed into the **same** personal OneDrive
   account: extract the ZIP (or copy the single-file EXE) on each, launch, go to Settings → Personal Sync,
   choose the OneDrive parent folder, verify the app creates/uses `Carowinds Operations Sync` under it,
   enable Personal Sync.
2. Follow this phase's task items 3-27 and 33-36 in order (bootstrap → second-device join → bidirectional
   draft/note edits → finalization/PDF sync → offline/reconnect → closed-app recovery → conflicts →
   tombstone [structurally not applicable, no product delete UI] → dual-finalization → folder switch →
   Files On-Demand → pause/resume → conflict-copy → multiple instances → crash/lock recovery → cadence
   timing → manual Sync Now → status UX at 100/125/150% → native folder picker → privacy file inspection →
   real sync-folder size → scheduled-refresh regression on real Windows).
3. Record actual PASS/FAIL per item using this document's existing terminology
   (`PASS`/`PASS AFTER FIX`/`PARTIAL`/`NOT EXECUTED`/`NOT APPLICABLE`/`FAIL`), fix only genuine release
   blockers found (this phase's own §30 list is still the right bar), then rebuild/re-hash/re-document
   before shipping.
4. If Files On-Demand or the folder picker (the two items with a standing history of Windows-only
   surprises — §16B "Platform limitations", §16F "Known limitations") reveal a real defect, that is this
   procedure's most likely source of a genuine release blocker; everything else in this design has already
   been proven correct at the logic level by the automated suite (§16G.6) and prior phases' two-device
   harness tests.

## 16G.8 End-user Personal Sync instructions (final)

Concise instructions for a nontechnical user setting up Personal Sync between their own Windows machines —
kept deliberately free of sync-engine internals, per this phase's explicit instruction:

1. Sign into OneDrive on each Windows computer (the normal Microsoft OneDrive desktop client — no
   Carowinds Operations account, no separate login).
2. Open Carowinds Operations.
3. Go to **Settings → Personal Sync**.
4. Choose the OneDrive parent folder (usually your OneDrive root).
5. Turn Personal Sync on.
6. Wait for the status to show **Up to date**.

Do this on every Windows computer you use Carowinds Operations on, signed into the same OneDrive account
each time — your drafts, Operational Notes, and finalized History will then follow you between them.
Optional, if your OneDrive client supports it: right-click the **Carowinds Operations Sync** folder inside
OneDrive and choose **Always keep on this device**, so your synced records stay available even if OneDrive
is briefly offline (this phase could not confirm this recommendation against a real Files On-Demand
client — §16G.2 — so treat it as a reasonable default, not a verified requirement).

What this means in practice: each computer still keeps its own local database — nothing about how the app
works day-to-day changes. Only the records you create yourself (drafts, notes, finalized History) are
stored in your OneDrive folder; nothing else is. The app keeps working normally if OneDrive is temporarily
unavailable — your work saves locally either way, and syncs automatically once OneDrive is reachable again.

## 16G.9 Release-blocker review

No release blocker (per this phase's own §30 list — data loss, silent conflict overwrite, frozen-PDF byte
change, folder-switch data skip, startup blocking on OneDrive, local saves failing when OneDrive is
unavailable, placeholder-handling corruption, stale process lock, folder-picker failure, or SQLite/log data
leaking into the sync folder) was found this phase, because none of the checks capable of finding most of
those (items requiring a real running OneDrive client) could be executed (§16G.2). The combined automated
test (§16G.1) found no defect — Device B correctly received Device A's change only through its own
coordinator-owned trigger, with zero conflicts, on the first attempt. This phase therefore fixed nothing
beyond adding the one test that closes the §16F.20 gap; no production code changed.

## 16G.10 Phase 16 status

**Not formally closed.** Every automatable validation, code-gap closure, release-artifact build, and
documentation update in this phase's scope is complete (§16G.1, §16G.4, §16G.6, this section and §16G.8).
This phase's own success criteria explicitly require real Windows/OneDrive behavior to be validated "where
environment permits" — this session's environment permitted none of it (§16G.2), so the criteria
("two-device sync works bidirectionally," "conflicts never silently lose data," "native Windows folder
selection works," etc., as actually *observed on real hardware*) remain unverified, not satisfied by
inference from passing automated tests. **Phase 14 remains on hold, unstarted, per CLAUDE.md — unaffected
by this phase's scope or findings.** The manual procedure in §16G.7, run by someone with real Windows
machines and a OneDrive account, is what would move Phase 16 from "code and automated-test complete" to
"formally complete" per this document's own bar.

---

# Phase 17B — Personal Sync: Monthly Early Ride Time and Date Overrides (Implementation)

Adds two new sync entity types — `MonthlyErt` (`yyyy-MM` identity) and `ErtDateOverride` (`yyyy-MM-dd`
identity) — for the persistent monthly Early Ride Time configuration and optional per-date override
introduced this phase (full product/domain design in `docs/ert-configuration.md`; this section covers
only the sync-specific decisions).

**SyncSchemaVersion stays at 1.** Adding two new `SyncEntityType` enum members is not an envelope
shape change — the same precedent set when `FinalizedPassdown` was added in Phase 16E. Each new type
gets its own independent payload schema version (`MonthlyErtSyncPayload.CurrentSchemaVersion = 1`,
`ErtDateOverrideSyncPayload.CurrentSchemaVersion = 1`), unrelated to any other entity's payload
version or to the envelope's own version.

Both new types are mutable, tombstone-capable, single-JSON-blob entities — architecturally identical
to Operational Notes (§16C), not to Drafts (date-keyed like a draft, but with no field-level
granularity concerns since a monthly config or override is already a small, wholesale-replaced
record) and not to FinalizedPassdown (mutable, unlike immutable History). `PersonalSyncService`'s
`Import/ExportMonthlyErtsAsync` and `Import/ExportErtDateOverridesAsync` are close mirrors of
`Import/ExportNotesAsync`, reusing the same entity-agnostic `SyncReconciliation.Decide` (§16A/§16D)
and the same Keep-This-Device/Use-Other-Device/Accept-Deletion conflict resolution flow (§16D) with no
new conflict semantics.

**A real bug found and fixed while wiring these in**: `ExportPendingTombstonesAsync`'s tombstone
path-resolution and `PersonalSyncConflictResolutionService`'s four resolution methods both had a
hardcoded two-branch `if (AssociateDraft) {...} else {...}` that implicitly assumed the `else` branch
meant `OperationalNote` — true when only two mutable entity types existed, but a real risk once a
third and fourth were added (a `MonthlyErt`/`ErtDateOverride` entity id would have been silently
misparsed as a Note GUID). Both were generalized into explicit `switch`/`if`-chains covering all four
mutable entity types before the new import/export paths were wired in — this was a correctness fix
required by adding the new types, not an optional cleanup.

Folder layout adds two new sibling folders under the existing sync root:

```
Carowinds Operations Sync/
    monthly-ert/<yyyy-MM>.json
    ert-date-overrides/<yyyy-MM-dd>.json
```

## Schema impact

DB schema 13 → 15 (`Migration0014CreateMonthlyErtConfigurationsTable`,
`Migration0015CreateErtDateOverridesTable`, both purely additive). Draft schema 3 → 4 (removes the
obsolete free-text `EarlyRideTime` field — see `docs/ert-configuration.md` §8). Snapshot schema 1 → 2
(additive new fields on `FinalizedEarlyRideTimeSnapshot` — see `docs/ert-configuration.md` §9).
SyncSchemaVersion remains 1.

## Phase 18B — Open editors and sync (REL-004)

Rule: **auto-reload clean editors; preserve dirty editors and require explicit conflict resolution;
never silently overwrite a synced change from an already-open editor.** Editor saves (Associate draft,
Early Ride Time panel, Operational Notes editor) are conditional on the content hash of the version the
editor loaded and run under the same `BEGIN IMMEDIATE` lock that each sync import's read-decide-write
now takes. A mismatch saves the editor's content locally and records the stored version as an ordinary
sync conflict (`OpenEditorConflictRecorder`), pausing export until Keep This Device / Use Other Device
/ Accept Deletion. `ISyncLocalChangeNotifier` lets clean editors reload after an import or resolution.
Full design, scope and tests: `docs/phase18-comprehensive-audit.md` §13.

# Phase 18B — Personal Sync durability, temp hygiene, and History import cost

## 18B.1 Durable small-file writes (REL-003)

`AtomicFileWriter` is now the single write path for every small persistent file the app owns:
sync envelopes, history PDF/JSON pairs, the manifest, `sync-config.json`, `device-id.txt`, and
`associate-passdown-template.json`. Sequence: create `<name>.tmp-<32 hex>` with `FileMode.CreateNew`
in the destination directory → write the full content → flush → **`Flush(flushToDisk: true)`** →
close → `File.Move(temp, target, overwrite: true)`. Any failure (including cancellation) deletes the
temp file before rethrowing; text is UTF-8 without BOM, byte-identical to the pre-18B output.

**Guarantee actually achieved:** a process crash *or a power loss* at any instant leaves the target as
a complete old or complete new version. A temp file is never read as authoritative (readers only open
exact canonical names) and never adopted by a retry (each attempt mints a new name).

**Not guaranteed:** that a write which returned successfully survives a power loss in the next few
seconds — the directory entry update (the rename) is left to the filesystem's own journal commit. The
consequence is a rollback to the previous complete version, never corruption. Forcing it would need
Windows-only P/Invoke (`MOVEFILE_WRITE_THROUGH`) or a Unix directory fsync, which .NET does not expose
portably; judged not worth the native complexity for files that are regenerated/re-exported on the
next pass. Windows behaviour is by design-analysis only — **NOT EXECUTED** on a Windows host.

Tests: `AtomicFileWriterFaultTests` (fault injection before temp creation, mid-write with a genuinely
partial temp on disk, after a complete flushed temp but before replacement, a failing replacement,
retry beside a leftover temp, last valid envelope still reads as valid after a failed replacement).

## 18B.2 Stale sync temp-file cleanup (REL-007)

`SyncTempFileCleaner.CleanStaleTempFiles(root)` runs at the start of every sync pass (best-effort,
never throws). It deletes a file only if **all** hold:

- it is directly inside the app-owned `Carowinds Operations Sync` folder or one of its `drafts`,
  `notes`, `history`, `monthly-ert`, `ert-date-overrides` subfolders (non-recursive; the user's
  selected root and everything else in OneDrive are never enumerated);
- its canonical path's parent is exactly that folder, and neither the folder nor the file is a
  symbolic link/junction (`LinkTarget`; OneDrive Files-On-Demand placeholders are not links);
- its name is exactly `<canonical sync file name>.tmp-<32 lowercase hex>` for *that* folder (the final
  name must round-trip through `SyncPathService`), or the folder validator's own
  `.sync-probe-<32 hex>.tmp` in the sync folder;
- its last write is at least **24 hours** old — orders of magnitude longer than any real write, and
  tolerant of clock skew between devices, so an in-progress write on any device is never deleted.

Locked/vanished candidates (OneDrive racing the cleanup) are skipped. Legitimate envelopes, PDFs, the
manifest, conflict copies, and user files never match. Tests: `SyncTempFileCleanerTests` (every
folder, legitimate files kept, age threshold, unrecognized names, outside-subtree files, symlinked
subfolder/sync folder/file, locked file, `..`/rooted/UNC/alternate-separator containment checks) and
`PersonalSyncServiceTests.SyncPass_RemovesOnlyStaleTempFiles_FromTheSharedFolder`.

## 18B.3 History import no longer re-reads every PDF (PERF-001)

Before 18B every pass read and SHA-256-hashed every remote history PDF. Now a remote PDF is left
unread only when **every** condition holds (`PersonalSyncService.CanSkipPdfRevalidation`):

1. a local record with the same id and content hash exists, and the remote JSON declares exactly the
   local frozen PDF's SHA-256 and size (from a blob-free metadata query — `pdf_bytes` is never loaded);
2. this device's sync state records that exact remote envelope (device id + revision + content hash)
   as imported/converged — state that is only written after a successful byte validation — and the
   entity is not in conflict;
3. the remote PDF's current length equals the declared size;
4. if this process already validated the file, its length and last-write time are unchanged since.

Timestamp/length are only ever reasons to **re**-validate, never proof of identity. A remote record not
yet held locally is always fully read and hashed, so a forged timestamp/length cannot get unverified
bytes imported. A folder switch (SyncSpaceId change) clears sync state, so everything is re-validated.

**Deliberate trade-off:** if the *shared copy* of a record this device already holds is later altered
in place with identical length and timestamp, that is no longer reported as a history conflict (the
local immutable copy is unaffected and is never replaced). Any length change, any timestamp change
during the session, any JSON hash/identity/revision change is still re-validated and reported as before.

Measured (macOS dev host, local SSD, warm cache; 365 records × 500 KB synthetic PDFs, Release build):

| | PDF bytes read per steady-state pass | elapsed (5 runs) |
|---|---|---|
| without skip (pre-18B behaviour) | 182,500,000 | 315–361 ms |
| with skip | 0 | 111–118 ms |

The import pass itself is unchanged (182.5 MB, ~0.86 s). Elapsed time on a real OneDrive folder
(Files-On-Demand may need to download a dehydrated PDF just to hash it) is expected to benefit more
but was **not measured** (no Windows/OneDrive host).

## Phase 18D — North Gate / Avenue overrides in the draft

`AssociateDraftPayload` gains two optional per-date fields, `NorthGateOpeningOverride` and
`AvenueOpeningOverride` (nullable `TimeOnly`; null = use the calculated default). They are ordinary
draft content, so they travel inside the existing `AssociateDraft` sync entity — no new entity type,
envelope field, or `SyncSchemaVersion` change (still 1); Draft schema stays 4, DB 15.

- **Hash stability:** both properties are omitted from JSON while null, so a draft without overrides
  serializes — and therefore content-hashes — exactly as before 18D. Upgrading does not re-export
  every draft or raise false conflicts.
- **REL-004:** unchanged mechanism; covered on the real two-device stack
  (`OpenEditorSyncProtectionTests.Phase18D_*`): overrides sync; a clean editor reloads them; a
  dirty editor keeps its local value and records an explicit conflict holding the remote one.
- **Conflict review:** the draft comparison now shows "North Gate Override" and "Avenue Override"
  ("Default" when none is stored).
- **Mixed versions:** a pre-18D build ignores the fields and drops them if it re-saves that date's
  draft (then syncs that version). Run Phase 18D (or later) on every PC that shares a sync folder.

# Phase 26 — Official price history (merge-only sidecar channel)

Full design: `docs/phase26-official-price-history-sync.md`. Summary for this document's model:

- **New folder** `official-price-history/` beside the existing five, one `<device-id>.json` per computer,
  written only by that computer (`SyncPathService.GetOfficialPriceHistoryFilePath`). Not a `SyncEnvelope`:
  an independently versioned file (`FormatVersion` 1) holding every verified official price observation the
  computer knows — its own and those it merged — sorted by identity.
- **Conflict model**: none of §G's revision/conflict machinery applies. Price observations are immutable
  evidence identified by a content hash (`OfficialPriceObservationIdentity`); import is set union with a
  forward-only `last_confirmed`. Same identity with different content is rejected and logged, never resolved;
  nothing is added to `sync_conflicts` or the conflict review screen.
- **Deletions**: none. Absence is never deletion; no tombstones for price history.
- **Envelope/manifest**: unchanged; `SyncSchemaVersion` stays 1. RC.8 enumerates only its known folders and
  never touches the new one; its temp cleaner is folder-scoped too.
- **Pass order**: runs after drafts, notes, History and ERT import/export in `PersonalSyncService.RunAsync`;
  recoverable failures are counted (I/O → `IoFailureCount`, retried by the coordinator's backoff; unreadable
  files → `MalformedCount`) and never block the other entities.
- **Local store**: `synced_price_observations` (DB 17); this computer's `season_price_history` and
  `catalog_products` are never written by sync. The live database remains outside the sync folder.

