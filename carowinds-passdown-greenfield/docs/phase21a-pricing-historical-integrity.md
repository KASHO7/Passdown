# Phase 21A — Pricing accuracy, historical integrity and pass-year identity

Development phase after RC.5 (`0.1.0-rc.5`). Shipped code changed; the next packaged candidate must
be `0.1.0-rc.6`. Schema versions are unchanged: DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1.

Live official-source checks were made on 2026-10-16 (Eastern). Every amount below is an observation
from that day, not a constant — no price appears in production code.

## A. Military / First Responder dated pricing

### What the official flow does

| Step | Official surface | Observed |
|---|---|---|
| Marketing page | `https://www.sixflags.com/carowinds/military-discounts` | Card "Military & First Responder Tickets", "Starting From $40", Gate Price $80, Buy Now keyword `MilitaryWizard`. Also a "2027 Military Gold Pass" card (keyword `FallMilitaryGoldSP2027`). |
| Buy Now | Accesso store (`cf-ca.buy.sixflags.com`) opened in place on the page | "Select a date to begin!" calendar, then "Fall Military Ticket" and "Fall First Responder Ticket" with that date's price. Oct 16: $55.00 each; Oct 17: $60.00 each; gate price $80 struck through. |
| ID.me | After choosing products | The page itself says "You will receive a login prompt from ID.me after selecting your product(s)". |

So the date-specific price is shown **before** ID.me, to anonymous visitors. The calendar data comes
from the same anonymous `getkeywordcalendar` endpoint every other dated source already uses, keyword
`MilitaryWizardDailyTickets` (the "Wizard + DailyTickets" pattern Daily Admission and Bring-A-Friend
follow). A direct anonymous request returned exactly the store's amounts (Oct 16 $55.00, Oct 17
$60.00). Nothing past that point was touched: no ID.me, no login, no cookie, cart, quantity step or
purchase. The `MilitaryWizard` keyword itself answers "No dates found".

Response facts (horizon Oct 16 2026 – Jan 3 2027, 39 dates):

- Separate **Military** and **First Responder** packages; on every observed date both had the same
  price.
- Package names vary: "Fall Military Ticket", "Military Ticket", and on some dates both, at the same
  amount.
- `fee_amount` was 0; `tax_amount` is separate. `retail_amount` (pre-tax, what the store shows) is
  read; no fee or quantity is mixed in.
- Past dates, closed dates and beyond-horizon dates answer "No dates found".
- Daily amounts ranged $40–$60 across the horizon; "Starting From $40" is the minimum, not any
  particular date's price.

### Implementation

- New dated category `SourceCategory.DatedMilitaryFirstResponder`, adapter
  `DatedMilitaryFirstResponderSource` (primary single-day request, horizon fallback, past dates never
  requested), daily freshness like every dated source, registered in the refresh orchestrator.
- Per eligibility, a price is kept only when **all** of that eligibility's packages for the date agree.
  Differing amounts mean no single exact price — that eligibility is reported without one (never a
  minimum). The stable display names are "Military Ticket" / "First Responder Ticket".
- Status semantics: Verified (exact price), NotPublished (beyond horizon / nothing published),
  Unavailable (past date, a date the store does not sell, or every eligibility ambiguous), Error
  (transport, HTTP, malformed, date mismatch, no recognized package). Unavailable is a normal result
  on the Dashboard (like Bring-A-Friend), never a verification issue.
- Evidence records the source name, official page URL, requested/confirmed date, Eastern check time,
  status and strategy. Logs never include response bodies.

### Precedence for a selected date (composition)

1. Exact amounts recorded for that date, and Military = First Responder, and the catalog card exists →
   the "Military & First Responder Tickets" row switches **in place** to the exact amount
   (`PriceKind.ExactDated`, qualifier dropped, official $80 gate comparison kept). Its identity is the
   catalog row's, so include/override draft choices survive.
2. Exact amounts that differ, or no catalog card → one exact row per eligibility ("Military Ticket",
   "First Responder Ticket"), filed under Military & First Responder (Daily Admission section); the
   catalog minimum row is removed.
3. No exact amount for that date (never checked, other date, NotPublished, Unavailable, Error) → the
   catalog row is untouched: **From $40.00**. A marketing minimum is never presented as exact.

The 2027 Military Gold Pass row is never touched by daily pricing, and a daily Military price never
enters Season Price History (Phase 20A scope unchanged). PDF and Teams copy read the same composed
row, so they print `$55.00` vs `From $40.00` identically.

## B. Historical selected-date pricing

### Root cause

Operations Calendar → View Date Pricing routes to Associate → Products for the date.
`AssociatePassdownService` composed every date from stored dated results for that date **plus the
entire current catalog** (Season Passes, Add-Ons, Promotions, Military card). So an April date showed
October's SCarowinds promotions, today's pass prices and today's "From" Military minimum.

### Rule

A date becomes **historical** once it is eligible for finalization (6:00 AM Eastern the next day).
For a historical date the composition keeps only official rows whose price was recorded for that
exact date (`DateSpecific` and `ApplicableDate == date`):

- stored exact-date results (Daily Admission, Fast Lane, SCarowinds, BAF Duo, Military) — kept;
- catalog rows that had an exact dated value merged in (Bring-A-Friend, Military) — kept, with the
  catalog's comparison price cleared (it is today's card, not the date's);
- every other current-catalog row — withheld (`WithheldCurrentCatalogProductCount`);
- custom products from the date's own draft — kept (they are manual entries, not official pricing).

Not evidence: appearing in today's catalog, today's price, seasonal tags, lifecycle state, a price
without a date, any month rule. When nothing qualifies the Products section shows
**"Historical pricing not available for this date."**; otherwise it says it is showing prices recorded
for that date. Park hours are composed independently and still show.

Today, future dates and the overnight window before 6:00 AM are unchanged. The scheduled finalizer
uses `LoadAssociateDateForFinalizationAsync`, which does not apply the rule, so the frozen History
snapshot and PDF are built exactly as before. Frozen History is never mutated. Viewing a past date
is local-only — no HTTP, no new background work.

### Expired promotions

A recorded exact Bring-A-Friend price for a past date stays visible on that date even after the
catalog card retired (official package name, Promotions section, exact price, no comparison). An
offer never recorded for a date is never reconstructed. Phase 19B lifecycle is unchanged — this only
changes what a historical *view* composes.

### Source audit (live 2026-10-16)

| Source | Class | Finding |
|---|---|---|
| Daily Admission (`SnapWizardDailyTickets`) | B | No past dates ("No dates found" for open Oct 10–11); stored exact results usable |
| Fast Lane (`FastLaneOnly`) | B | Same |
| SCarowinds / Haunt (`HAPWizard`) | B | Same |
| Bring-A-Friend (`BAFWizardDailyTickets`) | B | Same |
| BAF Duo (`SUBAF50`) | B | Same (offer ended) |
| Military/First Responder (`MilitaryWizardDailyTickets`) | B | Same; new in this phase, so history starts now |
| Promotions, Military card, Season Passes, Add-Ons (catalog pages) | C | Current offering only; pass/add-on price changes are kept in Season Price History |

A = retrieves past dates, B = stored observations only, C = current only, D = unknown. No official
historical pricing source was found, so **no historical pricing backfill** was built. Third-party
archives and aggregators were not used. Diagnostics summaries list these classes.

## C. Pass-year identity

Live season-pass cards carry distinct official Sanity ids and the year in the name ("2027 Silver/Gold/
Prestige Pass"); the Military pass's keyword includes the year (`FallMilitaryGoldSP2027`). No 2026
pass was ever observed by this app, so whether the CMS reuses an id across years cannot be proven.

Identity precedence (official id > url > source + name) is unchanged. Narrow guard in
`ProductReconciliationService`: for pass-family categories (Season Passes, Add-Ons, Military/First
Responder), when the stored product under the observed key states a **different** pass year than the
observation, the observation is filed under `{key}|pass-year:{year}`. The stored product keeps its
key, name, history and timeline and retires via the normal two-absence rule. Pass year comes only
from an explicit standalone four-digit year in the official name (`PassValidityYear`), never the
observation date (a 2027 pass sold in October 2026 is a 2027 pass). No year, or two different years,
is unknown — never the current year, and never a split. Renames within one year keep identity.

No migration: existing keys never change, no row is moved or duplicated, and `product_key` is
already TEXT. Personal Sync does not carry catalog rows. Frozen History is untouched.

## D. January 1–3, 2026 park hours

The official feed (`d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30`) answered HTTP 404
"Not Found" for 2026-01-01, -02, -03, for the `202601` and `202512` month files, and for every date
through 2026-03-24; 2026-03-25 onward is served. CloudFront reports "Error from cloudfront" (no
object). The official WinterFest and park-hours pages now describe only the 2026–27 season (through
January 3, 2027). Conclusion: the official source no longer exposes those dates (retention), not a
season-boundary or parameter issue in the app. Phase 20C already targets January 1 onward (calendar
year, regardless of which WinterFest season a date belongs to), detects the boundary from the feed's
own 404, and stores nothing for those dates. No hours were invented or inferred; the boundary is not
hard-coded.

## E. Dashboard → Open Passdown

Dashboard's Open Passdown now always opens today's Eastern date (`AssociateViewModel.SelectEasternToday`,
read from the clock at click time, so midnight rollover is honored). It goes through the normal date
path (unsaved edits on the previous date are flushed first) and does nothing when today is already
selected. Dashboard Open Notes, Calendar Open Passdown / View Date Pricing and History keep their
explicit dates. The reset is logged.

## Windows validation checklist (RC.6)

Mark anything not run as NOT EXECUTED.

- Military: today's exact price matches the official store; a different future date shows its own
  price; a date with no exact price (closed/beyond horizon) shows "From $x"; disconnect network →
  previous exact price or From, never Error masquerading as a price.
- Historical: April date shows no SCarowinds/current catalog products; a date with recorded pricing
  shows only those prices with the "Showing prices recorded…" notice; a date without shows "Historical
  pricing not available for this date."; hours still show; an old promotion appears only where it was
  recorded.
- Pass year: Season Price History shows 2026 and 2027 passes separately (if both exist); repeated
  refresh adds no duplicates.
- Park hours: January 1–3, 2026 show no hours; no invented times.
- Dashboard: Open Passdown always lands on today's Eastern date; picking another date in Associate
  still works; History opens the requested record.

## Limitations

- Exact Military pricing begins accumulating with this release; earlier past dates show no Military
  price.
- Past-date pricing exists only for dates the app retrieved while they were current or future.
- If Military and First Responder ever list differing packages for one eligibility on a date, that
  eligibility shows no exact price (truthful, but less specific).
- Pass-year separation is defensive; it has not been observed live because no 2026 pass was recorded.
