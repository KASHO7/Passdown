# Phase 31 — Dynamic Operating Seasons and Official Deals

Branch `feature/passdown-redesign`, started from `f5469d1` (RC.10 release record). Version is still
`0.1.0-rc.10` in source: **any release containing this work must use a new version (RC.11 or later)**.
No RC.8/RC.9/RC.10 artifact was rebuilt or modified, nothing was pushed, tagged or published.

## 1. Root cause of each reported problem

All four were reproduced against the live official pages on 2026-11-21 and against a read-only copy of
this Mac's dev database (last refreshed 2026-10-15). The tester's Windows profile was not inspected.

| # | Problem | Precise cause |
|---|---------|---------------|
| 1 | "Include in Passdown" options did not adapt to the season | **Static seasonal category + no event data at all.** The only seasonal option ("SCarowinds / Haunt") was a fixed checkbox, always shown. The app had no official event-schedule source, and `ProductSeasonClassifier` deliberately never uses a date, so nothing could know which event applies to the selected date. |
| 2 | SCarowinds still appeared under Products after its season | **Failed parser → stale last-known-good, plus no season awareness.** Since the 2026 Black Friday cards appeared, every Promotions refresh ended in `Error` (see 3). An error never counts as "confirmed absent", so the cached `SCarowinds Bundles` Promotions row (last verified 2026-10-15) stayed `Active` forever and the section resolver filed it under SCarowinds as a current product. Nothing compared it with the event's end date. |
| 3 | Deals section not populating | **Outdated website parser.** `daily-tickets` (and the new campaign pages) now publish a "Black Friday Deal" card that sells through *payment options* (Silver Pass $69 / Good Any Day Ticket $69) and has no `productv2-card-price` element. The legacy all-or-nothing markup parser treats a titled card without a price as malformed and throws, so `PromotionsCatalogSource` returned `Error` for the whole source: nothing updated, nothing new appeared, and the catalog-level error was never surfaced on the Products screen (only dated categories had status banners). |
| 4 | Active Black Friday promotion missing | Same parser failure, plus (a) the $69 lives only in the card's payment options, which were not modelled; (b) the `black-friday-deal` campaign page was never fetched (only `special-offers` and `daily-tickets`). A latent bug would have surfaced next: the old digits-only `ParseAmount` turns "3rd Night FREE" into **$3** (the Camp Wilderness card). |

Not causes: date filtering, product identity, UI filtering or refresh orchestration were working as
designed; selected-date navigation never retrieved online (unchanged).

## 2. Official sources used

| Source | Used for | Requests per check |
|---|---|---|
| `https://www.sixflags.com/carowinds/events` | Event listing: each event's CMS `_id`, title, `showTimesSchedule[].startDate/endDate`, official `overrideText` ("Select Days, November 7-22, 2026") | 1 (required) |
| `…/events/scarowinds`, `…/events/oktoberfest`, `…/events/winterfest` | Event pages: dates for events no longer on the listing (SCarowinds after it ends) and their own "Select Dates/Nights" wording | 3 |
| Seasonal links on the listing (e.g. `…/events/new-years-eve-at-winterfest`) | Same | ≤ 3 more (cap 6 event pages) |
| `https://www.sixflags.com/carowinds/special-offers`, `…/daily-tickets` | Offer cards (required, unchanged URLs) | 2 |
| Campaign tabs linked from `special-offers` (today `…/black-friday-deal`) | Discovered from the page's own `productGroupTabs`, never hard-coded | ≤ 3 |
| Existing official park-hours results (already cached per date) | Whether the selected date is an operating day | 0 extra |

All data is read from the pages' embedded structured CMS data (the Next.js flight payload the page
renders from), preferred over scraping markup per the retrieval rules. The homepage was inspected; its
"Hurry! Black Friday Deal Ends 11/27" banner is also on the campaign page, so it is not fetched
separately (no extra crawling). No Playwright.

## 3. Season-date resolution

`Domain/Seasons` (pure, no clock):

* An `OfficialOperatingEvent` is one official occurrence: kind (SCarowinds, Oktoberfest, WinterFest,
  New Year's Eve, Other — from the brand word in the official title only), the official inclusive
  date range, `IsSelectDates` (official wording says "Select Days/Dates/Nights"), the official date
  wording, and the source page.
* `SeasonResolver.Resolve(selectedDate, schedule, parkDay)` uses the **selected operating date**, not
  today. For each seasonal event whose official range covers the date:
  * park officially closed that day → *Not operating that day* (not offered);
  * park officially open → *Scheduled*;
  * park hours not known and the event is select-dates → *Within select-date range* (never assumed
    to be an event day).
* Overlaps are supported (SCarowinds' last night = Oktoberfest's first day, Nov 7; WinterFest + New
  Year's Eve on Dec 31). Days without a named event say so. Ranges crossing the year boundary
  (WinterFest Nov 27, 2026 – Jan 3, 2027) are plain date comparisons.
* Non-seasonal listings (education days, group programs, "WinterFest Theatre Arts Day") are stored but
  never treated as a season.
* A missing schedule is *unknown*, never "no event": every seasonal question then answers "unknown"
  and nothing is hidden. Merging a newer check keeps occurrences that dropped off the listing, so past
  dates keep their historical event.
* The schedule is a last-known-good document; freshness ("checked …", "last check failed; showing the
  schedule verified …", "not checked yet") is shown with it.

Verified 2026 fixtures: SCarowinds select nights Sep 11 – Nov 7, 2026; Oktoberfest select days
Nov 7–22, 2026; WinterFest select dates Nov 27, 2026 – Jan 3, 2027.

## 4. Associate Passdown

* The "Include in Passdown" panel now starts with **Official events for {selected date}**: each
  applicable official event with its official wording and day status, "Next: WinterFest begins …",
  "SCarowinds 2026 ended …", and schedule freshness.
* The existing seasonal control is the **SCarowinds / Haunt section toggle** (a section control; event
  products are reclassified into that section by name). It is shown when SCarowinds applies to the
  date, when official SCarowinds prices exist for the date (past SCarowinds dates keep their
  relevance), or when the schedule is unknown. Otherwise it is not offered and a short line explains
  why. A **saved "included" choice outside SCarowinds is never removed**: the checkbox stays visible
  with "SCarowinds is not running on this date (…). Your saved choice is kept; …".
* Visibility is computed only when a date loads, never from a live edit, so unticking never makes a
  control vanish mid-edit. No option is ever auto-selected; saved selections are untouched (new drafts
  keep the Phase 17D default: SCarowinds not included).
* The three Copy options and the PDF layout are unchanged; the only output change is that a
  past-season product (below) no longer prints.

## 5. Product filtering rules

* Current products: unchanged sections from the official catalog.
* **Past-season offers**: a current-catalog product tagged with exactly one seasonal event (e.g.
  "SCarowinds Bundles") is listed under *Past-season offers — not current* when **both** the event's
  latest official occurrence (started on or before today) has ended **and** no successful official
  check has listed the product since that end date. A product re-listed after the event ended is
  still on sale and stays current; an event product sold before its event starts stays current;
  regular-season products naming an event, multi-event products, unknown schedules, dated products and
  custom entries are never demoted. Applied only to the current pricing timeframe; historical dates
  keep exactly their recorded evidence.
* Past-season rows are not exported (PDF/Copy) and not editable, but their stored catalog records,
  lifecycle state, price history, product ids and any saved per-product override are untouched (the
  override is carried through autosave unchanged).
* Once the repaired Promotions refresh runs twice without listing "SCarowinds Bundles", the existing
  §7.3 lifecycle archives it normally (two confirmed absences).
* New purchases, renewals, memberships, product identities and legacy Season Pass Price History
  identity folding are unchanged (no change to `PassOffer`, reconciliation or price history).

## 6. Deal discovery and parsing

* `PromotionsCatalogSource` now reads the pages' structured `product_v2` offer documents
  (`guestFacingTitle`, `beforePriceText`, `currentPrice`, `currentPriceModifier`,
  `strikethroughPrice`, `savings`, `highlight`, `description`, `featuredBenefits`,
  `paymentOptions[]`, `taxesAndFeesDisclosure`, `buyNowLink.keyword`). Legacy markup parsing remains
  the fallback for a page without structured data.
* Catalog rows keep their established identities (`data-ab-product` id, else accesso keyword). A card
  that sells through payment options becomes one row per option named "{card}: {option}" from the
  official titles, identified by the option's own keyword. A card without any price has no catalog row
  but is still a deal.
* A card with no readable title is reported *unreadable* (catalog: unconfirmed key, so it is not
  counted absent; deals: kept with its previous details). The two core pages are still required; a
  campaign page failing is a warning and its offers are kept, not expired.
* Reusable promotion model (`Domain/Promotions/OfficialPromotion`): stable id (`offer:{official id}`),
  title, category, advertised price (amount only when explicit), regular price only from the official
  struck-through price, savings text, price options, purchase deadline / sales start (each with its
  exact official text), page-banner campaign end kept separately, redemption window, validity notes,
  terms, fee disclosures (never mixed into price), discrepancies, every official source URL, first
  seen / last verified, and an unreadable flag.
* Prices are read strictly (`OfficialOfferText.ParseCurrencyAmount`): one explicit `$` amount or a
  plain number; "3rd Night FREE" and "Free" stay text, never $3 or a guessed $0. This also replaced the
  legacy markup parser's digits-only parse.
* Dates: offer-specific wording first ("Book by November 30, 2026"). A page-level banner ("Hurry! Black
  Friday Deal Ends 11/27") applies only to offers whose own title/highlight names the same campaign,
  is stored as `CampaignEnd` (year inferred from the day it was observed, flagged), and when it
  disagrees with the offer's own terms both are kept and the discrepancy is shown.
* The same offer on several pages is merged once with every source link.
* Status (`PromotionStatusEvaluator`): *Expired* when its own purchase deadline passed, when a later
  successful check no longer lists it, or when a campaign end passed with no re-listing since;
  *Upcoming* only for an explicit future sales start; *Unknown/previously verified* when not
  re-verified within 48 h or unreadable at the last check; otherwise *Current*. Status is recomputed on
  every read, so a deadline passing moves an offer out of Current Deals without any refresh.

## 7. Black Friday results (official pages, 2026-11-21 — dated examples)

| Offer | Result |
|---|---|
| Black Friday Deal (Silver Pass) | Season pass deal; options: Silver Pass **$69**/ea (keyword `WinterSilverSP2027`), Good Any Day Ticket $69/ea ("$0 more for Silver"); no offer-specific deadline; campaign banner "Ends 11/27" → Nov 27, 2026 (year inferred); "Use by 1/3/27" kept as a term of the free Bring-A-Friend bonus; processing fee kept apart. Catalog row "Black Friday Deal: Silver Pass" $69. |
| 2027 Silver Pass Bundle | Bundle **$159** ("Only"), regular $287 (struck-through, never the price), "Save $128", campaign banner Nov 27, 2026; seen on special-offers and black-friday-deal (merged). |
| Resorts Black Friday Deal (Camp Wilderness) | Lodging; price text "3rd Night FREE" (no numeric price); purchase deadline **Book by November 30, 2026** (offer terms); stays **Nov 2, 2026 – Dec 30, 2027**; discrepancy shown against the "Ends 11/27" banner. |
| Also current | Bring-A-Friend Ticket (from $40, gate $80), Free Senior Admission ("Free", visits thru Jan 3, 2027), Park Ticket (as low as $50). |
| SCarowinds Bundles | Not on any official page → absent from the verified catalog; shown as past-season until archived. |

## 8. Refresh behavior

* The event schedule is a new catalog-scoped (date-independent) registration on the **existing**
  orchestrator and daily cadence (`FreshnessPolicyCatalog`): it runs at startup catch-up, Check
  Online and the scheduled refresh with the existing request coordination — no new polling engine.
* Deals ride the existing Promotions check (same requests); `CatalogSourceRegistration` stores the
  deals document after reconciliation.
* Selected-date navigation reads cached documents only (asserted in tests).
* A verified check merges into the last-known-good document; a failed check records only its attempt
  and error (`official_reference_documents.last_attempt_*`), never replacing verified data. A verified
  check that no longer lists an offer expires it; an unreadable card or unreachable campaign page does
  not.
* Last-checked and latest-error are shown for both the event schedule and Current Deals.

## 9. Data / schema changes

* **Migration 18 (additive)**: `official_reference_documents` (one row per source: success payload /
  time / URL / detail, and last attempt status / time / detail / technical detail / URL). No existing
  table, row, product id or price-history record changes; RC.8 → 18 upgrade test proves every prior
  row is preserved. Forward-only like every migration: an older build (RC.10 and earlier) refuses a
  version-18 database with its unsupported-schema message; rollback uses the automatic pre-migration
  backup (Phase 27).
* New `SourceCategory.OperatingEvents` (source state + freshness only; never a product row).
* New `ProductSeasonTags.Oktoberfest` flag (classification only, never persisted).
* Draft payload, finalized snapshot, PDF schema, Personal Sync (`SyncSchemaVersion` 1) and price
  history are unchanged. The new table is a local cache and is not synced. Frozen History PDFs are
  stored bytes and are not regenerated.

## 10. UI changes

* Associate → Include in Passdown: "Official events for {date}" block (event, official wording, day
  status, next/ended, freshness) and the season-aware SCarowinds / Haunt control with explanations.
* Products: a **Current Deals** panel at the top — title, campaign badge, category, price / regular /
  savings, purchase options, dates (purchase by, page banner, use/visit window, or "No purchase
  deadline is stated"), warnings (discrepancies, unreadable), collapsible terms, status + last
  verified, and a clickable link per official page. Collapsible *Upcoming*, *Previously verified — not
  re-confirmed* and *Expired and past offers* groups. Empty states: "Deals not checked yet",
  "No current offers verified on the official Carowinds pages", "Last check failed; showing previously
  verified offers", "Offer has expired — …", and "Event schedule not available yet".
* Products: collapsible *Past-season offers — not current* list.
* Existing theme resources/styles only (`panel`, `inset`, `categoryBand`, `sourceTag`, `muted`,
  `fieldWarning`, `linkAction`); dark/light unchanged. Every new control has text and an accessible
  name; status never relies on colour alone.

## 11. Tests

Deterministic fixtures in `fixtures/phase31/` are trimmed copies of the official pages captured
2026-11-21 (images/SEO/rich text removed; structure and values unchanged); variants (changed price,
malformed dates, untitled card, missing campaign page, failed core page) are derived in the tests.

| Project | New tests |
|---|---|
| Domain | `Phase31SeasonResolverTests`, `Phase31SeasonalProductAvailabilityTests`, `Phase31OfficialOfferTextTests`, `Phase31PromotionStatusTests` |
| Application | `Phase31SeasonsAndDealsTests` (workspace season, past-season products, draft untouched, deals merge/grouping, both registrations, last-known-good, JSON) |
| Infrastructure | `Phase31OperatingEventsSourceTests`, `Phase31PromotionsDealsTests`, `Phase31OfficialReferenceDocumentTests` (store semantics + RC.8 → 18 upgrade); opt-in `OperatingEventsSourceLiveSmokeTests` |
| Documents | `Phase31PastSeasonPdfTests` |
| Ui | `Phase31SeasonsAndDealsUiTests` (presentations, view model, autosave keeps past-season override, markup bindings) |

Coverage of the requested cases: Oktoberfest 2026; SCarowinds ending; WinterFest beginning and
crossing into 2027; overlapping and non-event days; future and historical selected dates; Black Friday
Silver Pass; bundle; Camp Wilderness; different purchase vs redemption deadlines; changed and expired
deals; missing/malformed/stale content; incorrect price matches and empty prices; refresh failures and
last-known-good; duplicates across pages; existing product identities; draft/PDF compatibility.
Existing Product, Price History, History, PDF and Personal Sync suites all still pass.

Final deterministic count: **4878** (RC.10 baseline 4752; +126).

## 12. Known limitations

* Carowinds currently publishes **no separately priced Oktoberfest or WinterFest products** on the
  pages the app reads, so those seasons show as event context only; when an official product names
  the event, it is tagged and appears in its section automatically. Whether Oktoberfest/WinterFest
  should get their own print sections is a product decision (not implemented).
* "Select dates" are confirmed per day only through official park hours already cached for that date;
  without them the date is shown as "within select dates, not confirmed".
* A year-less banner date ("Ends 11/27") is resolved to its next occurrence from the day it was seen
  and labelled as inferred.
* The event-page slugs `scarowinds`, `oktoberfest`, `winterfest` are fixed starting points (the URLs
  provided for this phase); further seasonal pages are discovered from the listing. If Carowinds
  renames a page, a warning is recorded and the listing still supplies listed events.
* Structured-data parsing depends on the page's embedded CMS fields; a structural change produces an
  `Error` (events listing) or an unreadable-card warning (offers), never invented data.
* The past-season list is informational; there is no per-row "show anyway" override.

## 13. Results

Clean `dotnet build -c Release`: succeeded, 0 errors. Deterministic partition (`--filter "Category!=LiveSmoke"`,
the five real-Window Ui classes run alone):

| Project / partition | Passed |
|---|---|
| Domain | 361 |
| Application | 1300 |
| Infrastructure | 1103 |
| Documents | 471 |
| Ui (mixed) | 1491 |
| Ui OperationsCalendarViewTests / Phase15ERealWindowLayoutTests / RealWindowStartupTimingTests / SpellCheckNavigationLifecycleTests / Phase21DMathEditorWindowTests | 26 / 113 / 2 / 7 / 4 |
| **Total** | **4878**, 0 failed |

Opt-in live smoke (not part of the build) run once on 2026-11-21: `PromotionsCatalogSourceLiveSmokeTests` and
`OperatingEventsSourceLiveSmokeTests` both passed against the official site.

Updated guard tests (intentional changes): source-registration count 13 → 14 and families (+OperatingEvents),
migration list/version 17 → 18, appearance "no new migration" guard now expects 18, Phase 21A pricing-category
guard excludes OperatingEvents.

## 14. Windows validation checklist (flag: run on the Windows 11 VM)

1. Install the next build over RC.10 with a real profile; confirm the pre-migration backup
   (`backups/…schema-17-to-18…`) is created and the app opens (schema 18).
2. History: open two frozen History PDFs from before the upgrade — byte-identical, unchanged.
3. Check Online on today's date: Dashboard/Associate complete; no new error toasts.
4. Associate, today (in Oktoberfest select days or later): "Official events for …" shows the correct
   event(s), official wording and "checked …" time; SCarowinds / Haunt is not offered (note shown).
5. Open a draft that had SCarowinds / Haunt ticked (pre-upgrade): checkbox still ticked, mismatch note
   shown, nothing else changed; untick/re-tick autosaves normally.
6. Select a past SCarowinds date (e.g. Oct 17, 2026) with recorded prices: SCarowinds event and option
   shown; recorded prices unchanged.
7. Select Dec 12, 2026 and Dec 31, 2026: WinterFest (and New Year's Eve) shown; no network activity on
   date change.
8. Products: Current Deals lists the Black Friday Silver Pass ($69 options), Silver Pass Bundle ($159,
   regular $287), Camp Wilderness (3rd Night FREE, Book by Nov 30, stays Nov 2, 2026 – Dec 30, 2027,
   discrepancy note); each "Official page: …" link opens the default browser.
9. Products: "SCarowinds Bundles" appears only under Past-season offers; not in the PDF or any Copy
   option output.
10. Disconnect the network, Check Online: Deals and event schedule show "Last check failed; showing
    previously verified …"; nothing disappears.
11. After Nov 27/30, 2026 (or with the clock moved forward on a test VM): the Silver Pass and bundle
    leave Current Deals ("Offer has expired — …") without a refresh; Camp Wilderness leaves after Nov 30.
12. Personal Sync: sync with a second computer; drafts/History/Price History sync as before; no new
    files in the shared folder for deals/events.
13. Scheduled refresh (Task Scheduler) completes; diagnostics show the "Event Schedule" source.
14. Dark and light themes: new panels readable at 100/125/150 % scaling; keyboard Tab reaches every
    new link and expander.
