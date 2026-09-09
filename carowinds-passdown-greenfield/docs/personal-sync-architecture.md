# Phase 16A — Personal OneDrive Sync: Architecture

Status: **design only**. No synchronization code exists yet. Nothing in this document changes
current persistence, startup, or UI behavior. See §O below and the phase 16A task for the
constraints this document was written under.

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
