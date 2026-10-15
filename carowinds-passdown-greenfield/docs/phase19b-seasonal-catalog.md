# Phase 19B — Seasonal catalog and product lifecycle

Goal: products appear and disappear naturally across Carowinds, SCarowinds and WinterFest without a
normal seasonal catalog change ever looking like a retrieval failure — especially now that Phase 19A
refreshes official data automatically every four hours.

Starting point: `2c610fb` (Phase 19A). Schemas unchanged: DB 15, Draft 4, Snapshot 2,
SyncSchemaVersion 1.

## 1. What was actually wrong (audit)

The catalog lifecycle (spec §7.3, Phase 2/17A) was already correct and is **kept, not replaced**:

| Concept | Existing implementation | Phase 19B term |
|---|---|---|
| Active / Published | `ProductLifecycleState.Active` | Active |
| Missing once | `PendingConfirmation` (still shown) | candidate absence |
| Retired after confirmed absence | `Archived` via `CatalogProductLifecycle.ConfirmAbsent` | Retired / inactive |
| Source explicitly flagged gone | `Archived` via `MarkExplicitlyUnavailable` | Retired / inactive |
| Authoritative end date passed | `Expired` (no adapter supplies a date yet) | Retired / inactive |
| Reappears | `ConfirmPresent` → `Active`, outcome `Reactivated` | Reactivated |
| Retrieval failure | `RetrievalStatus.Error` — rejected by every lifecycle transition | Error |

**Confirmed-absence threshold: 2 consecutive successful checks** (`CatalogProductLifecycle.ConfirmedAbsenceThreshold`,
now public for logging/tests, value unchanged). One successful check without the product moves it to
`PendingConfirmation` (still shown); the second archives it. An `Error` never advances the count, and
a non-`Verified` catalog result is never reconciled at all (`CatalogSourceRegistration`).

The real defect was in the **dated accesso keyword sources**. When a keyword has nothing published
anywhere from today onward, the official `getkeywordcalendar` endpoint answers
`status: "FAILED"`, `error_msg: "No dates found that meet requirements"` for the whole horizon window.
Every dated adapter mapped that to `Error`:

- **Bring-A-Friend Duo** (`SUBAF50`) — live-confirmed 2026-10-15: the offer has ended, and every
  comprehensive refresh recorded `dated-bring-a-friend-duo.official` as failed, so Phase 19A's indicator
  showed "Some official data couldn't be updated" permanently.
- **SCarowinds / Conjuring** (`HAPWizard`/`THECONJURING`) — would do the same from the day after
  SCarowinds ends until next season is published (the adapter's own comment called this branch
  "reachable in practice").
- Ordinary Bring-A-Friend, Fast Lane and dated admission had the identical branch.

## 2. Fix: "no dates found" is a successful answer

`AccessoKeywordCalendarService.IsNoDatesFound` recognises exactly that live-observed answer. For a
window that starts today, all five dated accesso sources (admission, Fast Lane, haunted/Conjuring,
Bring-A-Friend, Bring-A-Friend Duo) now return **`NotPublished`** (a successful completion) instead of
`Error`. `NotPublished` was chosen over `Unavailable` because it never degrades the Dashboard headline
and means "nothing published/offered", for every category.

Still `Error`, unchanged: transport failure, non-2xx, malformed JSON, missing `SERVICE`, any **other**
`FAILED` message (e.g. the live-observed "A month value must be passed…" contract failure), an `OK`
response with no entries (never observed live), unparseable dates, a date mismatch, unusable packages.
No price is fabricated and no synthetic product is created — `NotPublished` carries no payload, which
clears that date's last-known-good value exactly as any other successful "no product" answer does.

Catalog sources needed no change: an empty-but-valid page is already `Verified` with zero products
(drives normal absence), and a parser/transport failure is `Error` (no reconciliation).

## 3. Source inventory by lifecycle role

| Source | Role | Disappearing product handling |
|---|---|---|
| Promotions (`special-offers` + `daily-tickets`) | A. catalog/discovery | generic confirmed absence → retire |
| Season passes, season-pass add-ons, general add-ons | A. catalog/discovery | generic confirmed absence → retire |
| Military/First Responder | A. catalog (+ C. specific keyword) | generic confirmed absence → retire |
| Park hours | B. dated | Closed / NotPublished (unchanged) |
| Dated admission, dated Fast Lane | B. dated pricing | per-date; **horizon "no dates" now NotPublished** |
| Dated Bring-A-Friend (`BAFWizardDailyTickets`) | C. optional specific-product, dated | **horizon "no dates" now NotPublished** |
| Dated Bring-A-Friend Duo (`SUBAF50`) | C. optional specific-product, dated (limited-time) | **horizon "no dates" now NotPublished** |
| Dated haunted + Conjuring | D. seasonal (SCarowinds), dated | **horizon "no dates" now NotPublished**; single-night "no dates" already meant "none that night" |

No source was added and no official site was newly adopted.

## 4. Season tags — non-exclusive product metadata

`Domain.Catalog.ProductSeasonTags` is a `[Flags]` set: `None`, `Carowinds`, `SCarowinds`, `WinterFest`.
A product may carry zero, one or several; there is **no global "current season" mode** and nothing is
suppressed because of a tag.

`Application.Catalog.ProductSeasonClassifier` (deterministic, evidence-based, computed at composition
time, **never persisted**, so a later re-classification can never alter a finalized snapshot):

1. **Official source** (`SourceCategory`): dated admission, dated Fast Lane, season passes, add-ons,
   Military/First Responder → `Carowinds` (regular-park official pages); dated haunted (SCarowinds
   event page keywords, incl. Conjuring) → `SCarowinds`.
2. **Event brand in the official name**: "SCarowinds" → `SCarowinds`; "WinterFest" → `WinterFest`.
   This is the same established rule `ProductDisplaySectionResolver` already used for "SCarowinds
   Bundles" (now shared from one place). Additive to (1), so a regular-park product naming an event
   carries both tags.

Promotions, the dated Bring-A-Friend categories and custom products have no source-level season
signal; without an event name they stay **unclassified** (`None`) rather than guessed — e.g. the live
"Tricks and Treats Family Pack" is unclassified. **No calendar month or hard-coded season date is an
input anywhere**; tags never decide availability — official publication does.

Exposed as `AssociateProduct.SeasonTags` (computed). **Not shown in the UI** this phase: product rows
are already grouped into sections, and an extra badge would add clutter without helping an associate
(spec §10/§47). Internal uses: logging, diagnostics counts, transition metadata.

## 5. Expected seasonal behaviour

- **Limited promotion ends** (e.g. Duo): its catalog card disappears from a successful page check →
  pending → retired on the second check; its dated keyword answers `NotPublished`. Refresh stays
  fully successful. Live 2026-10-15: the Duo card is already absent from `daily-tickets`, so an
  existing install retires it within two automatic checks (≈4–8 h) with no user action.
- **SCarowinds ends**: SCarowinds-named catalog cards (e.g. "SCarowinds Bundles") retire through the
  same generic absence lifecycle; the haunted source answers `NotPublished` for every date, and the
  Associate screen omits the empty SCarowinds section (see §7). No manual switch, no row deletion.
- **WinterFest begins**: new products are discovered by the existing catalog sources as soon as they
  are published on a page this app already reads; a "WinterFest" name tags them `WinterFest`. No
  dedicated WinterFest source exists, and none was added.
- **Overlap** (regular + SCarowinds, regular + WinterFest) is the normal case: everything currently
  published is shown, whatever its tags.
- **Reappearance next season**: the same catalog identity (official id > URL > source+category+name,
  unchanged) is reactivated — no duplicate row; `FirstSeenAt` is preserved. Caveat (pre-existing,
  documented in `catalog-source-inventory.md`): if the official site issues a *new* id for next year's
  product, it is genuinely a new identity.

## 6. Date-specific availability vs global lifecycle

Kept separate. A dated `NotPublished`/`Unavailable` for one date only affects that date's dated result
(and, for SCarowinds, whether that date shows a section). It never touches `catalog_products`.
Global retirement comes only from catalog-source absence across the threshold. Tested by
`SeasonalCatalogLifecycleTests.DateSpecificNotPublished_DoesNotRetireTheGlobalCatalogProduct_OnlyCatalogAbsenceDoes`.

## 7. Presentation

- **Associate**: `ComposeCatalogProducts` already skipped `Archived`/`Expired` — retired products
  disappear automatically. New: `CategoryStatusPresentation.Build` returns no banner for an
  event-only seasonal category (`ProductSeasonClassifier.IsEventOnlyCategory` — today only SCarowinds'
  haunted source) when its state is `NotPublished`, so no empty "SCarowinds — Not published" heading.
  Errors, stale data, not-checked and in-season dark nights (`Unavailable`) still show their banner.
  Non-seasonal `NotPublished` banners (e.g. a far-future Fast Lane date) are unchanged.
- **PDF / Today's Pricing copy**: no change needed. Both are built from the same composed workspace and
  group only rows that exist, so a retired product and an empty section vanish from both together;
  a reactivated product returns in both. No "Retired"/"Ended" label is printed. No clipboard-specific
  lifecycle logic.
- **Catalog view**: there is no separate catalog-management screen; the normal UI is active/current
  only. Inactive rows stay queryable in `catalog_products`/`product_history` and are counted in
  diagnostics.

## 8. History and drafts

- **Finalized History** is frozen JSON + PDF bytes and is never recomposed; retiring a product later
  leaves the snapshot and PDF byte-identical (tested). Past dates' dated results are not refreshed and
  remain under existing retention.
- **Open/saved drafts** that referenced a now-retired product load safely: the product is simply not
  offered as a current row; the stored draft is **not rewritten on load**. Its stale
  `ProductOverrides` entry is dropped the next time the user edits and the draft autosaves (the editor
  rebuilds overrides from current rows — existing behaviour). If the product is reactivated before
  that, its saved include/price override applies again. Finalized data is never touched.

## 9. Phase 19A integration and local invalidation

No new loop or bus. The 4-hourly comprehensive refresh runs catalog reconciliation (which retires or
reactivates) and dated checks (which now answer `NotPublished` instead of `Error`); when the refresh
finishes the existing `DataChangeKind.OfficialData` change is published and the Dashboard/Associate
re-read local state. The freshness indicator stays "Official data checked …" across a normal seasonal
transition and still reports "Some official data couldn't be updated" for a real failure (tested end
to end through `RefreshOrchestrator` + `OfficialDataStatus`).

## 10. Logging and diagnostics

`ProductReconciliationService` (optional `ILogger`) logs, per transition only: discovered (with season
tags), candidate absence, retired, expired, reactivated, season tags changed, and a catalog source
that succeeded with zero products. Product key + official display name + source name only — no HTTP
bodies, query values or user content; unchanged products are silent.

`ReconciledProduct` now also carries `DisplayName`, `SeasonTags` and `PreviousSeasonTags`
(`SeasonTagsChanged`), so a future "what appeared / disappeared / changed season" view can be built
from a reconciliation pass plus `product_history` without new persistence (spec §26 — no such UI
built).

Diagnostics `summary.txt` gains "Official catalog lifecycle": active, missing once, inactive, and
active season-tag counts (counts only).

## 11. Personal Sync, install, upgrade

Catalog lifecycle remains local, official-source-derived state; nothing new is synced
(SyncSchemaVersion 1). A fresh install builds the active catalog from current sources alone. An
existing database loads unchanged; previously active products that are no longer published retire
through normal reconciliation. No migration, no reset, nothing deleted.

## 12. Limitations

- WinterFest and next-season SCarowinds transitions were simulated deterministically, not observed
  live (they are months away). Classification of WinterFest products depends on the official name
  containing "WinterFest" or on a future dedicated source.
- Event-themed products whose official name does not name the event (e.g. "Tricks and Treats Family
  Pack") stay unclassified by design.
- Season tags are not displayed in the UI.
- A retired product's draft override persists in an untouched saved draft until the next edit (by
  design — loading never rewrites a draft).

## 13. Windows checklist for RC.3 (Phase 19A + 19B)

Run on Windows 11 x64 with an existing RC.2 profile copy (back it up first). Mark each PASS / FAIL /
NOT EXECUTED.

| # | Check | Expected | Status |
|---|---|---|---|
| 1 | Start the app; wait for the startup check | "Checking official data…" then "Official data checked h:mm" | NOT EXECUTED |
| 2 | Leave open ≥ 4 h (or check the log) | "Automatic official-data refresh completed" ~4 h later | NOT EXECUTED |
| 3 | After a comprehensive check, read the indicator | No "Some official data couldn't be updated" caused by the ended Bring-A-Friend Duo | NOT EXECUTED |
| 4 | Diagnostics export → `source-status.txt` | `dated-bring-a-friend-duo.official` status Ok (not Error) | NOT EXECUTED |
| 5 | After two automatic checks, Associate → Promotions | No "Bring-A-Friend Duo" row; log shows "candidate absence" then "now inactive" | NOT EXECUTED |
| 6 | Associate product list for today | Only currently published products; no dead rows with error badges | NOT EXECUTED |
| 7 | SCarowinds section for a current SCarowinds night | Section present with haunted products incl. Conjuring when published | NOT EXECUTED |
| 8 | SCarowinds for a date after the season (e.g. Nov 15) | No empty SCarowinds heading (Associate, PDF, Teams copy) | NOT EXECUTED |
| 9 | Generate PDF and "Copy Today's Pricing" for the same date | Same products/sections in both; no retired products | NOT EXECUTED |
| 10 | Open an existing History entry from before the change | Unchanged content and frozen PDF | NOT EXECUTED |
| 11 | Diagnostics `summary.txt` | "Official catalog lifecycle" counts present; no product names | NOT EXECUTED |
| 12 | Disconnect network; let a check run | "Some official data couldn't be updated"; values still shown; nothing retired | NOT EXECUTED |
| 13 | Reactivation | Covered by deterministic tests only (no debug fixture path in the shipped app) | NOT EXECUTED |
| 14 | Live SCarowinds → WinterFest transition | Not observable until the seasons change; covered by deterministic simulation | NOT EXECUTED |
