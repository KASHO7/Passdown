# Phase 26 — Official Price History Sync & Source Links

Status: **unreleased development after RC.8.** Version metadata is still `0.1.0-rc.8`; nothing was
packaged, pushed or published. RC.6/RC.7/RC.8 artifacts are untouched.

| | |
|---|---|
| Base | `3b4d043` (Phase 25) on `feature/passdown-redesign` |
| Schemas | **DB 17** (was 16 — one additive table), Draft 4, Snapshot 2, **SyncSchemaVersion 1 (unchanged)**, new independent price-history file format 1 |
| Deterministic tests | 4682/4682 (baseline 4538 + 144) on the macOS build host; Release build 0 warnings, 0 errors |
| Windows validation | **NOT EXECUTED** (checklist below) |

## 1. Root problem

Each computer records official Season Pass / Membership price history only from its own successful
official checks (`season_price_history`, Phase 20A/25). A computer that started collecting in August has
no record of April prices that another computer saw. Personal Sync (Phase 16) carried drafts, notes,
finalized History and ERT settings, but price history was explicitly local and unsynced. Separately, the
Phase 25 Memberships & Season Passes source line showed the official URL as plain text.

## 2. Existing storage audit

| Question | Finding |
|---|---|
| Table | `season_price_history` (DB 16, Phase 20A); `product_history` is a catalog audit log, not a price series |
| Shape | **Intervals**, not point observations: one row per advertised price state per product — `(product_key, sequence)` unique, `amount` REAL, `price_kind`, `display_price`, `price_qualifier`, `display_name`, `source_name`, `first_observed_at_utc`, `last_confirmed_at_utc`, `origin` (`Observed` / `Backfilled`) |
| Append-only? | Rows are never deleted or re-priced; only the newest row's `last_confirmed_at_utc` moves forward (`ConfirmAsync`) |
| Product identity | `CatalogProductKey` text (`SeasonPasses:pass-offer:carowinds\|gold\|membership\|monthly`, Phase 25; legacy `id:`/`url:`/`name:` keys) |
| Exact vs From | `price_kind` (`StandardCatalog`, `StartingFrom`, `Sale`, …); `Unknown` only for legacy/unclassified |
| Source URL | Not stored per interval; the catalog row's `LastEvidence.SourceUrl` carries it |
| Duplicates | Prevented by `(product_key, sequence)` and the recorder's read-latest/decide/write inside one `BEGIN IMMEDIATE` unit of work |
| Current price | **Not derived from history.** The current official price is `catalog_products.current_price_amount`, written by reconciliation; history is a parallel record of it |
| Freshness | Source status (`source_states`, verification info) is separate from history |
| Already synced? | No — catalog and price history were local-only |

Personal Sync audit (reused unchanged): user-chosen folder → `Carowinds Operations Sync/`; `manifest.json`
(SyncSchemaVersion 1, SyncSpaceId); per-record folders `drafts/ notes/ history/ monthly-ert/
ert-date-overrides/` enumerated **non-recursively, by name**; `AtomicFileWriter` (same-directory temp →
fsync → rename); `SyncFileReader`-style bounded reads; malformed files are counted and left untouched; one
in-process gate + `PersonalSyncProcessLock`; automatic coordinator with bounded backoff.

## 3. Eligible record types

Synchronized: every locally recorded `season_price_history` interval whose product is a tracked season
product — **Season Passes (new purchase), Season Pass renewal offers, Membership monthly rates, Membership
initiation fees, Season Pass Add-Ons, Military/First Responder season-tier passes**, and legacy
(pre-Phase 25) Season Pass rows — with a positive USD amount of at most two decimals, a source name, and
sane timestamps.

Never synchronized: manual draft/display-price overrides, custom products, unavailable options (they never
have a price state), dated admission/Fast Lane/haunt/Bring-A-Friend prices (never tracked as history;
current values are re-fetched), zero-dollar or implausible amounts, `ExactDated`/`ComparisonWas` kinds,
catalog rows, source status, notes beyond what Personal Sync already carries, secrets.

## 4. Product taxonomy

Unchanged from Phase 25: each `CatalogProductKey` is one series with one unit
(`PriceHistoryTrackingPolicy.UnitOf`). Gold Season Pass 2027 (`…|season-pass|new-purchase|2027`, one-time),
Gold Season Pass Renewal 2027 (`…|renewal|2027`, one-time), Gold Membership (`…|membership|monthly`,
per month) and Gold Membership Initiation Fee (`…|membership|initiation-fee`, one-time) are four keys and
four histories. The merge is always per product key, so they can never collapse. Every synced record
carries its `BillingUnit`, which the importer re-derives from the key and rejects on mismatch.

**Historical uncertainty.** Nothing is backfilled or reclassified. A legacy `$89` "Gold Pass" row stays
under its own legacy key with its original kind (often `Unknown`) and origin (`Backfilled`); it is never
re-labeled a renewal because today's page shows an `$89` renewal. Whether an earlier renewal was $5 lower
is not knowable from stored evidence and is not invented.

## 5. Observation identity

An **observation** = one interval as recorded by one computer (its *contributor*): every original field,
plus `ContributorDeviceId` (the existing Personal Sync device id) and `ContributorSequence` (the
interval's local sequence).

`ObservationId` = lowercase hex SHA-256 of the canonical JSON array

```
["carowinds-official-price-observation/v1", contributorDeviceId, contributorSequence, productKey,
 amount ("0.00", invariant), priceKind, displayPrice|null, priceQualifier|null, displayName,
 sourceName, firstObservedUtcTicks, origin]
```

(`OfficialPriceObservationIdentity`). It covers product, price type, amount and the original observation
context — never the amount alone, the name alone or the date alone. `LastConfirmedUtc` is excluded on
purpose: it is the one field allowed to move forward. `Currency` ("USD") and `BillingUnit` are derived and
validated, not hashed.

**Same price seen independently by two computers** (item 17): two separate pieces of evidence (different
contributor → different identity). They are never deleted or merged in storage; they display as **one
row** when they overlap (§7), so there is no visible duplicate.

## 6. Storage and migration decision (DB 16 → 17)

`season_price_history` cannot hold foreign rows: its `(product, sequence)` series *is* the local
recorder's state (the newest row is what the next official check extends or closes), so an imported
older interval there could masquerade as this computer's latest observation. Migration 17 therefore adds
one table and changes nothing else:

`synced_price_observations` — `observation_id` PK, contributor id/sequence, product key, `amount`
(canonical TEXT, so the identity recomputes exactly), kind, display price, qualifier, display name, source
name, `first_observed_at_utc`, `last_confirmed_at_utc`, origin, currency, billing unit,
`imported_at_utc` (arrival time — never shown as an official check time); index on `product_key`.

No row is copied or rewritten; tested against a synthetic RC.8-schema profile (every pre-existing table,
including a frozen History PDF blob hashed byte-for-byte, is identical after the upgrade). Migrations are
forward-only (existing policy): **an RC.8 build refuses a DB 17 profile**, so returning a computer to RC.8
requires the pre-upgrade backup.

## 7. Merge algorithm

Two layers, both order-independent.

**Storage (set union).** `SqliteSyncedPriceObservationRepository.MergeAsync`, one `BEGIN IMMEDIATE`
transaction per pass: unknown identity → insert; known identity with identical immutable content → move
`last_confirmed_at_utc` forward only (`max`); known identity with different stored content → **conflict**:
nothing written, stored row kept. There is no delete statement. Insert-if-absent + max is commutative,
associative and idempotent, so `merge(A, B) = merge(B, A)` and repeated exchange converges.

**Display (read-time, nothing stored).** `PriceHistoryTimelineMerge`: every local interval and imported
observation is a span. Spans are sorted by a total, content-only order (first observed, kind, amount, last
confirmed, names, identity). Walking that order, a span joins the current row when it has the same state
(amount **and** kind) and either overlaps in time or comes from a different contributor (another computer
seeing the same price is no evidence of a change). It stays a separate row only when it is the same
contributor's next interval after a gap — that contributor's recorded reappearance. With no imports the
output is exactly the pre-Phase 26 local history (tested).

Example: PC-A `$99 Apr 2–Jun 30`, `$104 Jul 1–Oct 20`; PC-B `$104 Aug 3–Oct 21`; PC-C `$104 Oct 5–Oct 21`
→ every PC shows `$99 Apr 2–Jun 30`, `$104 Jul 1–Oct 21`.

## 8. Idempotency, conflicts, deletion

- Re-importing the same observation, reordered input, many repeats, restarts and multiple sources: no new
  rows (tested 10 shuffled rounds with duplicates, restart over the same DB file, 5 extra quiet passes
  with byte-identical files and database).
- An incoming record whose content does not hash to its claimed identity (e.g. "same id, $79 instead of
  $99") is rejected (`IdentityMismatch`) and counted; a valid record whose identity is already stored with
  different content is an integrity conflict. Either way the local record is kept, no winner is chosen by
  time, nothing new is invented, and the offending file stays in the folder for investigation. A warning
  is logged with the file name, count and first reason (no prices or names).
- Absence is never deletion; there are no price-history tombstones. A stale or older copy of a file
  merges nothing and removes nothing. The product has no local price-history delete operation, so there
  is nothing to interact with.

## 9. Timestamps and current price

`FirstObservedUtc`/`LastConfirmedUtc` are always the contributor's original official check times;
`imported_at_utc` is stored separately. Merging never touches `season_price_history` or
`catalog_products`, so the current official price, its "last verified" time, source freshness and the
recorder's next decision are exactly what this computer's own checks produced. In the merged view an
imported older observation sorts by its own dates and cannot become the newest row unless it genuinely is
the most recent evidence; rows resting on another computer's checks are labeled (§12).

## 10. Transport and file format

```
Carowinds Operations Sync/
    official-price-history/
        <device-id>.json            # one per computer, written only by that computer
```

`OfficialPriceHistoryFile` — `{Format: "carowinds-official-price-history", FormatVersion: 1,
ExporterDeviceId, Observations: [...]}`, observations sorted by identity, enums as names, indented JSON.
Each computer's file holds **everything it knows** (its own observations plus everything it merged), so
the combined history survives one computer's file disappearing (tested). Files are named by device id,
so computers never overwrite each other.

Reading: top-level `*.json` only; names must be `<guid>.json` or a provider conflict copy keeping the GUID
as the leading segment (`<guid>-LAPTOP.json`, `<guid> (1).json`) — conflict copies are safe to read
because import is merge-only and validated per record. Paths are only ever taken from the directory
listing and checked to be directly inside the folder; content never names a path. Limits: 64 files per
pass, 8 MiB per file (checked before reading), 20,000 observations per file, more than 200 invalid records
→ whole file skipped. Text is data only (displayed, never interpreted); control characters are rejected.

Writing: content is serialized deterministically and compared with the existing file; it is rewritten via
`AtomicFileWriter` only when it changed (no sync-provider churn). No empty file is created on a computer
with nothing to share.

## 11. Versioning and backward compatibility

- **SyncSchemaVersion stays 1.** The envelope, the manifest and every existing folder are byte-for-byte
  unchanged; the new data is an additive, independently versioned channel (`FormatVersion` 1). A future
  incompatible change bumps `FormatVersion`; readers leave newer files untouched and report them.
- **RC.8 peers**: RC.8 enumerates only its five known folders by name and its temp cleaner only those
  folders, so it never reads, writes or deletes `official-price-history/` (tested with the unchanged pass
  and no price-history component). Drafts/notes/History/ERT keep syncing in both directions in a mixed
  folder.
- **Mixed-version guidance**: RC.8 and a Phase 26 build can safely share one folder — no corruption either
  way — but an RC.8 computer neither contributes nor receives price history. Upgrade every participating
  computer to get the full combined history. (The existing "use the same app version" note still applies
  to drafts.)

## 12. UI changes

- **Settings → Personal Sync**: description now lists "verified official price history"; one compact
  line explains it is merge-only and older versions neither send nor receive it. No new setup flow,
  account or sign-in.
- **Sync result / automatic status**: "Official price history: 28 new observations merged." — observations,
  never "price changes"; skipped records are said plainly. Counted separately from draft/note imports.
- **Season Price History**: refreshes in place when a merge lands (`DataChangeKind.OfficialPriceHistory`),
  keeping the selected product, search, price-type filter and "Include retired products"; rows resting on
  another computer's checks say "Imported through Personal Sync from another computer's official checks."
  (or "Collected on this computer and imported…"); a product known only through sync reads
  "… · Imported history". Units (`/month`) and original dates are unchanged. Dashboard and Calendar
  ignore this change kind; Associate is unaffected.

## 13. Clickable official source links

- **Associate → Products**, under the Season Passes and Memberships source note: **View Official
  Memberships & Season Passes Page** (generic "View Official Page" for other official pages). Price
  History offers the same link for the selected product when this computer recorded its source.
- The address is the product's recorded `LastEvidence.SourceUrl` — no hard-coded URL in any view.
- `OfficialSourceLinkPolicy` accepts only absolute `https` on `www.sixflags.com`/`sixflags.com` (Carowinds
  paths only) or `www.carowinds.com`/`carowinds.com`, default port, no user-info, DNS host; rejects
  `file:`, `javascript:`, `http:`, other hosts/lookalikes, IPs, relative/UNC/local paths. Checked when the
  link is built and again in the launcher.
- `AvaloniaExternalLinkLauncher` uses Avalonia's platform launcher (`TopLevel.Launcher.LaunchUriAsync`,
  ShellExecute of a URI on Windows) — no embedded browser, no command line built from strings. Opening
  changes nothing (no draft, observation, retrieval or sync). Failure shows "The official page couldn't
  be opened. Open your web browser and go to www.sixflags.com/carowinds/memberships-and-season-passes."
- Accessibility: a real `Button.linkAction` (Tab stop, Enter/Space, Fluent focus visual), accessible name
  = link text, help text = destination; theme-token colours for light and dark; no fixed width.

## 14. Offline, failure and performance behavior

Folder unavailable → the existing readiness gate reports it; the price-history step never runs, local
history and retrieval are unaffected. A locked/unreadable file or an unwritable own file is an I/O
failure, folded into the pass's `IoFailureCount` so the coordinator's existing bounded backoff retries it
(no loop). Malformed/oversized/newer files count as unreadable. Any recoverable exception (I/O, SQLite,
JSON) inside the step is caught; drafts/notes/History already ran first. Imports are one batched
transaction and one change notification per pass (1,000 observations tested), off the UI thread (sync
runs in the background coordinator).

## 15. Security

No new account, network endpoint or credential. Shared-folder content is untrusted: bounded, validated,
identity-checked, never executed or followed, never used to build paths. Only allowlisted HTTPS official
pages can be opened. Logs contain counts, file names and reasons only.

## 16. Tests (144 added)

| Assembly | Class | Covers |
|---|---|---|
| Application | `Phase26OfficialPriceObservationTests` (33) | identity determinism, every field, LastConfirmed excluded, independent same price, units per family, placeholders/dated/untracked excluded, legacy preserved, 16 rejection cases, deterministic file, wording |
| Application | `Phase26PriceHistoryTimelineMergeTests` (12) | local-only identical, older history extends backward, overlap = one row, cross-computer gap, remote reappearance, distinct values, Exact vs From, pass/renewal/membership/fee separation, order-independence + duplicates, 3-computer convergence, imported never newest, monthly series |
| Application | `Phase26OfficialSourceLinkPolicyTests` (20), `SyncPathServiceTests` (+11), `PersonalSyncCoordinatorTests` (+1) | allowlist; file naming/conflict copies/traversal; automatic status wording |
| Infrastructure | `Phase26OfficialPriceHistorySyncTests` (35) | three disposable computers + one shared folder: convergence, own tables untouched, repeated syncs (no rewrite), 4 sync orders, file loss, one notification, forward-only confirmation, current price/local series untouched, imported-only product, restart, 1,000-row batch, tampered identity, stored conflict, 9 damaged-file kinds, oversized, conflict copy/stray/temp/traversal, locked file, unwritable own file, transaction rollback, missing sidecar, simultaneous instances, read-during-write, stale view, per-device files, RC.8 → 17 upgrade |
| Infrastructure | `Phase26PersonalSyncCompatibilityTests` (7) | full Sync Now pass, notes alongside, RC.8 peer ignores folder, Phase 26 peer in RC.8-only folder, offline folder, unreadable file, temp cleanup |
| Infrastructure | `MigrationHardeningTests` (+1) | 16 → 17 upgrade |
| Ui | `Phase26PriceHistorySyncUiTests` (20) | sync wording, settings copy, provenance rows + `/month` + original dates, imported-only label, refresh keeps selection/filters, no refresh when closed/other changes, link open/failure/unsafe/recorded URL, markup accessibility, theme tokens |
| Ui | `Phase15ERealWindowLayoutTests` (+4, run-alone) | real window, Light/Dark × 1280/760: link is a Tab stop, Enter opens it, accent token, nothing clips |

Updated (premise changed, not weakened): `Phase23CAppearancePreferenceTests` now asserts migration 17 is
the synced-price table and no appearance migration exists.

Partitions (Release, `Category!=LiveSmoke`): Domain 303, Application 1267, Infrastructure 1045, Documents
469, Ui mixed 1446, OperationsCalendarViewTests 26, Phase15ERealWindowLayoutTests 113,
RealWindowStartupTimingTests 2, SpellCheckNavigationLifecycleTests 7, Phase21DMathEditorWindowTests 4 =
**4682**.

## 17. Multi-computer simulation (VERIFIED WITH SYNTHETIC DATA)

Disposable SQLite profiles PC-A (Apr–Oct), PC-B (Aug–Oct), PC-C (Oct) and one temp shared folder, with
2027 Gold/Prestige pass changes, Membership $6 → $7, initiation fee $25 → $20, renewal $89 → $84, the same
$104 seen by all three, and an ambiguous legacy `$89` Gold Pass. After A, B, C, A, B: all three hold the
same observation set and identical logical histories for every product; B shows A's April $99; repeated
passes add nothing; every sync order gives the same result; each profile's own tables (local history,
catalog, drafts, notes, History, settings) are byte-identical to before. Failure runs: offline folder,
write-while-read, conflict copy, corrupt/truncated/future-version files, locked file, rolled-back
transaction — no valid record lost in any.

## 18. Windows validation checklist (NOT EXECUTED)

Use real profiles only after backing them up. RC.8 cannot open a profile once a Phase 26 build migrated it.

1. On both computers, quit the app and copy `%LOCALAPPDATA%\Carowinds Passdown` to a dated backup.
2. Install a future build containing Phase 26 on both.
3. Configure the same Personal Sync folder on both (Settings → Personal Sync).
4. On Computer A, open Season Price History; note the oldest Gold Season Pass row.
5. On Computer B, note its (newer-only) history.
6. Sync Now on A. 7. Sync Now on B. 8. Sync Now on A again.
9. Both show the same rows for each product (dates and prices).
10. No duplicate rows; repeat Sync Now — "new observations merged" no longer appears.
11. Season Pass names are correct (Silver/Gold/Prestige, year).
12. Membership rows read "/month". 13. Initiation fees are their own product. 14. Renewal offers are their own product.
15. Imported rows keep their original First seen / Last confirmed dates and say "Imported through Personal Sync"; Associate "last verified" still shows this computer's own check.
16. Disconnect the network/OneDrive: the app, official checks and Price History keep working; sync reports the folder unavailable.
17. Repeat Sync Now several times: no change after the first.
18. Make the folder temporarily inaccessible (pause OneDrive / rename it): no errors beyond the status line; restore and sync again.
19. Appearance (System/Light/Dark) is still per computer.
20. Create a note on A; it appears on B; drafts still sync.
21. Associate → Products → Season Passes: "View Official Memberships & Season Passes Page" is reachable with Tab and Enter.
22. It opens https://www.sixflags.com/carowinds/memberships-and-season-passes in the default browser.
23. History: finalized PDFs open and are unchanged; Price History for local-only products is unchanged.

## 19. Known limitations

- Windows 11 / real OneDrive behavior (Files-On-Demand, conflict-copy naming, locks) is **NOT EXECUTED**;
  covered only by synthetic tests on macOS.
- Genuinely contradictory overlapping evidence (e.g. one computer cached an old page) is shown as recorded,
  in date order — not reconciled.
- If a computer's device id file is lost while its database survives, its observations are re-exported
  under a new contributor: extra stored evidence, no extra displayed rows.
- Two installations sharing one device id (a cloned profile) write the same file; each still imports the
  other's contributions via conflict copies, but this is not a supported setup.
- Imported observations are never removed (merge-only by design); the table is small (hundreds of rows).
- A real `$0.00` official price would not be synchronized (placeholder protection).
- RC.8 computers neither contribute nor receive price history; RC.8 cannot open a DB 17 profile.
- The source link appears only where this computer recorded a source URL; imported-only products have none.
