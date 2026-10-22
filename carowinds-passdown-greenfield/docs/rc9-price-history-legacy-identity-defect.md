# RC.9 defect — Gold Season Pass 2027 history missing on Computer B

Status: **fixed on `feature/passdown-redesign`, uncommitted, unreleased.** Version metadata is still
`0.1.0-rc.9`; RC.9 artifacts are untouched and must not be rebuilt. The fix ships only in a new release
candidate (RC.10) after separate release preparation.

## Observed (Windows 11 VM, RC.9 `e20d846`, DB 17)

| | Gold Season Pass — 2027 |
|---|---|
| Computer A (Kishan) | Aug 22 $89 sale · Sep 14 $99 · Oct 22 $99 sale |
| Computer B (CarowindsTestB) | Oct 22 $99 sale only |

Both `official-price-history/<device-id>.json` files hold 42 observations, including A's Aug 22 / Sep 14 /
Oct 22 Gold records. B shows Personal Sync attribution on other products.

## Root cause (confirmed by synthetic reproduction)

Export/import works as designed. Identical files are expected: each computer exports **everything it
knows** (own + merged), and relayed own observations are skipped on import (§10 of
`phase26-official-price-history-sync.md`). B does hold A's records with their original fields.

The defect is in the read-time Price History query:

1. **Computer A** checked the pre-redesign page (Aug 22), so its Gold Pass was stored under the legacy
   CMS-id key `SeasonPasses:id:550e3db6-5bad-45f8-8404-5d608c367617` ("2027 Gold Pass"). After the redesign,
   Phase 25 reconciliation *continues that legacy identity* — every later Gold check on A is filed under the
   same legacy key.
2. **Computer B** first checked after the redesign, had no legacy row, so it files Gold under the pass-offer
   key `SeasonPasses:pass-offer:carowinds|gold|season-pass|new-purchase|2027`.
3. `SeasonPriceHistoryService` matched imported observations to products by **exact key**. On B, A's
   observations stayed under the legacy key, which B's catalog does not contain, so they were listed as a
   separate *imported-only* product — always `IsActive: false`, so hidden unless **Include retired
   products** is on. B's visible Gold product showed only its own Oct 22 row.

The same applies to Prestige (and any tier A tracked before the redesign), and in reverse on A (B's
pass-offer observations become a hidden imported-only product there).

The draft-sync concurrent-edit conflict is a separate issue and is not touched here.

## Fix

`ImportedPassIdentityFolding` (Application/Catalog) + `SeasonPriceHistoryService`: at read time, an
imported key is folded into this computer's catalog product when both denote the same Season Pass
**new-purchase** offer with a stated year — the same condition reconciliation uses to continue a legacy
identity. It folds nothing when ambiguous: a yearless "Gold Pass", a different tier/year, disagreeing
legacy names, two legacy keys for one offer, or two catalog products for one offer. Renewals,
Memberships and initiation fees never fold.

Display only: `season_price_history`, `synced_price_observations`, `catalog_products`, the export file,
observation identities, current prices, drafts, notes, PDFs and Personal Sync are unchanged. The $89 keeps
its original kind (Sale), date and name — it is not reclassified as a renewal and nothing is backfilled.
No migration; DB stays 17; the file format stays 1.

## Tests (+17; deterministic total 4736)

- `Phase26LegacyPassIdentitySyncTests` (Infrastructure, 5): two disposable SQLite profiles, the real
  season-pass adapter, reconciliation and sync. Reproduced the defect before the fix (3 failing); proves B
  shows $89 Aug 22 + $99 Oct 22 under its one visible Gold product with original times and provenance,
  A and B show identical Gold history, no hidden duplicate product, stored rows/catalog/current price
  unchanged, Prestige/renewal/membership series unaffected.
- `ImportedPassIdentityFoldingTests` (Application, 12): both fold directions and every ambiguity guard.

## Evidence preservation on the VM (do first, before any further sync)

Quit the app in **both** Windows accounts. In an elevated PowerShell:

```powershell
$e = "C:\CarowindsEvidence\$(Get-Date -Format yyyyMMdd-HHmmss)"; New-Item -ItemType Directory $e | Out-Null
Copy-Item "C:\Users\Public\CarowindsSyncTest\Carowinds Operations Sync\official-price-history" "$e\official-price-history" -Recurse
Copy-Item "C:\Users\Kishan\AppData\Local\Carowinds Passdown" "$e\ComputerA-profile" -Recurse
Copy-Item "C:\Users\CarowindsTestB\AppData\Local\Carowinds Passdown" "$e\ComputerB-profile" -Recurse
Get-ChildItem $e -Recurse -File | Get-FileHash -Algorithm SHA256 | Export-Csv "$e\SHA256SUMS.csv" -NoTypeInformation
```

Run SQL only against the **copy**, read-only (`sqlite3 -readonly "<copy>\carowinds-passdown.db"`):

```sql
-- B: A's observations, under which key, with original fields
SELECT product_key, amount, price_kind, display_name, first_observed_at_utc, last_confirmed_at_utc,
       origin, contributor_device_id, contributor_sequence, imported_at_utc
FROM synced_price_observations WHERE product_key LIKE 'SeasonPasses:%gold%' OR display_name LIKE '%Gold%'
ORDER BY first_observed_at_utc;
-- Each computer: which key its own Gold series uses
SELECT product_key, sequence, amount, price_kind, display_name, first_observed_at_utc
FROM season_price_history WHERE display_name LIKE '%Gold%' ORDER BY product_key, sequence;
SELECT product_key, display_name, lifecycle_state FROM catalog_products WHERE category = 'SeasonPasses';
```

Expected if this root cause matches the VM: A's Gold rows under `SeasonPasses:id:550e3db6-…`, B's under
`SeasonPasses:pass-offer:…|gold|season-pass|new-purchase|2027`, and B's `synced_price_observations` holding
A's Aug 22 / Sep 14 / Oct 22 rows under the legacy key. On RC.9, B's Price History with **Include retired
products** on should show a second "Gold Season Pass — 2027 · Imported history" entry with A's rows.
Also check the app log for `skipped … observation(s) … failed verification` (expected: none).

## Windows revalidation (RC.10, NOT EXECUTED)

1. Preserve evidence as above; keep the RC.9 profiles as the starting state (or restore them from the copy).
2. Install RC.10 on both accounts (after its own release preparation and signing).
3. B → Price History, Include retired **off**: Gold Season Pass — 2027 shows Aug 22 $89 sale, Sep 14 $99,
   Oct 22 $99 sale, original dates, "Imported through Personal Sync" on A's rows; no "Imported history"
   duplicate with retired on. Same for Prestige.
4. A shows the same Gold rows; no duplicate either.
5. Associate → Products current Gold price and "last verified" unchanged on both.
6. Sync Now on both, repeatedly: no new observations merged, files unchanged (hash before/after).
7. Renewal, Membership (/month) and initiation fee remain separate products.
8. Drafts, notes, History PDFs unchanged; resolve the separate draft conflict independently.
