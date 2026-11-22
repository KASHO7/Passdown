# Phase 32 — Official Pass Perks & Calendar Integration

Branch `feature/passdown-redesign`, started from `cffcbdc` (RC.11 release record: publication verified).
Version is still `0.1.0-rc.11` in source; RC.11 is published and frozen, so **any release containing this
work must be RC.12 or later**. No RC.8–RC.11 artifact was rebuilt or modified; nothing was pushed, tagged or
published. Unrelated staged files (`.DS_Store`, `.claude/skills/pdf-design/SKILL.md`) were left as they were.

Pass Perks is an operational reference: what Carowinds publicly advertises to passholders, with eligibility,
earning and redemption dates. It is **not** a guest's reward wallet and never says a particular pass has
earned or received anything.

## 1. Official sources

Inspected 2026-11-21 (all plain HTTP GETs of public pages; no login, no Playwright):

| Source | Used for | Required? |
|---|---|---|
| `https://www.sixflags.com/carowinds/perks-and-play` | The "{Month} Perks" heading, the reward list (an accordion: name, bold date line, description), featured visit-based campaigns before the heading, and the loading footnote | Yes |
| `https://www.sixflags.com/pass-perks-terms` | Terms revision line, program eligibility / ineligibility, visit counting ("one Park per calendar day"), visit-based timing ("at the conclusion of the promotional period"), the reward types the terms define (Loyalty, Visit-Based, Partnership, Giveaway) | No (warning) |
| `https://www.sixflags.com/perks-and-play` | FAQ answers: "Who is eligible to earn Pass Perks?" and the home-park redemption rule | No (warning) |

The park page and FAQ are read from their embedded structured CMS data (Next.js flight payload); the terms
page is server-rendered paragraphs and list items. Year-round passholder benefits on the same page (FREE
Parking, Bring-A-Friend discount tickets, everyday food/retail savings, lodging discount) are **not** Pass
Perks and are never read.

**Current published month (2026-11-21): November Perks.** Seven rewards: Earn 2 FREE Bring-A-Friend Tickets
(visit twice Nov 1–22, valid Dec 5 – Jan 3), BOGO 50% Off Second Night at Carowinds Camp Wilderness (book by
Nov 30, promo code NOVPASS, stays Oct 31 – Apr 25), BOGO 50% Off Soft Pretzel, BOGO 50% Off Churro, Save 20%
When You Spend $50+ On Retail, 20% Off Game Play (each Nov 1 – Nov 30), and Buy Two, Get One FREE Hot
Chocolate Mug (Nov 27 – Jan 3). The September content mentioned in the brief is no longer on the page. No
December rewards are published; the app shows December as "not verified", never as empty.

**Official sources disagree on eligibility:** the terms exclude Pre-K, Summer Fun, Haunt Season and Wild Card;
the FAQ excludes Pre-K, KidZPass and Knott's Scary Farm. Both statements are shown with their source; the app
never picks one.

## 2. Data model (`Domain/PassPerks`)

`OfficialPassPerk` is separate from season-pass benefits, promotions/deals and pricing. Fields (all optional
except identity, title, campaign and source): stable id `perk:{yyyy-MM}:{title slug}`, title, campaign
(month, year, official heading, year-inferred flag), reward type, category, official description paragraphs,
the perk's own date line, dated windows by role (redemption, qualifying visits, booking deadline, stay
window), required visits, pass types named in the perk, eligibility / usage-limit / location phrases (exact
official wording), restriction sentences, reward wording ("BOGO 50% Off"), promo code, discrepancies, source
URL, first seen, last verified, no-longer-listed-since, last-check-incomplete, and earlier official wordings.

`PassPerksProgramTerms` holds the program-wide sentences (eligibility, loading note, visit counting, home
park) with their sources, and computes the eligibility discrepancy.

## 3. Reward classifications

Only classifications the official terms define, from the perk's own wording:

| Type | Rule |
|---|---|
| Monthly perk (`ActivePassholder`) | An item in the "{Month} Perks" list. The page footnote says these are loaded to passholders active on the first day of the month. |
| Visit-based | The text states a visit count ("Visit twice", "3 qualifying visits"). |
| Loyalty credit | "Loyalty Reward" or a "$x credit". |
| Partner reward | "partner"/"partnership"/"courtesy of". |
| Giveaway | "giveaway"/"sweepstakes"/"enter to win". |
| Passholder offer (`Other`) | Anything else — e.g. a listed booking code (Camp Wilderness, NOVPASS). |

Categories (food & beverage, retail, games, lodging, admission, experience, other) come from the title.

## 4. Eligibility and earning

Rows and details distinguish: available to eligible passholders (no visit requirement stated); requires
qualifying activity ("Requires 2 qualifying visits", only when stated); earned-reward loading (program terms:
visit-based rewards are given at the end of the promotional period; monthly perks load on the 1st); the
redemption window; and expired / no longer listed. A visit requirement is never applied to a perk whose own
text does not state one. Pass types appear only when the perk names them; otherwise program eligibility
applies and is labelled "Program terms (apply to every perk)". Every details dialog ends with: these are
publicly advertised rewards — check the official Six Flags app (Wallet → Pass Perks) for an individual pass.

## 5. Dates and time zone

* The official page prints no years. A heading month is assigned the year that puts it nearest the check date
  (five months back to six ahead), so a stale "November Perks" page seen in January stays November 2026.
  Dates inside a perk get the year nearest the campaign month; an end before its start crosses into the next
  year ("November 27 - January 3" → Jan 3, 2027). Month and day are always official; an assigned year is
  flagged and the details dialog says so. An explicit year is used as written.
* Ranges are inclusive. Dates are operating dates in the app's Carowinds time zone (America/New_York), the
  same "today" the rest of the app uses. The official terms credit visits per **calendar day**, so a perk
  date is a whole local day; no time of day is invented.
* Separate earning and redemption windows are kept separately (Bring-A-Friend: visits Nov 1–22, redeem Dec 5 –
  Jan 3). A date needs an explicit month and day in the perk's own text; "November" alone, a page banner or a
  month heading never becomes a date.
* Status per operating date: no longer listed → expired (redemption end, or booking deadline when there is no
  redemption window) → not re-confirmed (last check incomplete or older than 48 h) → upcoming (qualifying or
  redemption starts later) → current.

## 6. Calendar marker rules

* Only perk-specific, confirmed dates place a marker; never a month heading or banner.
* Month-long redemption (exactly the campaign month, e.g. Nov 1 – Nov 30) gets no daily markers; it is listed
  under a collapsed "Valid all {month} (n)" summary in the selected-date panel.
* A window of 14 days or fewer (a limited weekend) marks every day; a longer one marks its first day
  ("Redemption begins") and last day ("Last day to redeem").
* Booking deadlines ("Last day to book") and the last qualifying day ("Last day for qualifying visits") are
  marked. Stay windows are not.
* Several perks on one date share one gift marker with a count; the date's panel lists all of them, each with
  why it is on that date and View details.
* A perk a complete check no longer lists contributes no marker after it was last listed.
* Park hours, shows, events, notes, passdown status and date interactions are unchanged.

## 7. Calendar visibility setting

Settings → Calendar → **Show Pass Perks on Calendar** (default on). Stored in a new local file
`calendar-display.json` under the app's local data folder — outside the install folder (updates keep it) and
outside the Personal Sync folder (never synced). Off hides markers and the selected-date list only; no perk
data is deleted and the Products list is unchanged. A missing or damaged file means "on".

## 8. Refresh and historical records

* `PassPerksSourceRegistration` is a catalog-scoped source on the **existing** orchestrator with the daily
  cadence (`SourceCategory.PassPerks`): startup catch-up, Check Online and the scheduled refresh. No new
  polling engine. Changing the selected date, the Calendar month or the Products month picker reads the
  cache only.
* A verified check merges into the last-known-good document. Unchanged perks keep their first-seen time; a
  changed wording keeps the previous wording with its listed period; a perk listed again clears its removal.
* Removal: only a **complete** check (month list found and every listed reward readable) marks a missing perk
  "no longer listed"; an incomplete check keeps it, flagged "not re-confirmed". Zero readable rewards is an
  error, never a confirmed empty month.
* A failed check records only its attempt and error; verified perks and program terms are never erased.
  Terms or FAQ pages that fail keep their last verified sentences.
* Perks are kept 400 days after they were last verified (month history in Products).

## 9. Database and sync compatibility

* **No migration; schema stays 18.** The cache is one row (`pass-perks.official`) in the existing Phase 31
  `official_reference_documents` table, which already separates last-known-good payload from last attempt.
  Pre-migration backup and rollback procedures are therefore unchanged; an RC.11 build opens the database
  normally (it ignores the unknown row).
* Drafts, notes, official pricing, Price History, deals, dynamic seasons, ERT sync, Personal Sync
  (`SyncSchemaVersion` 1) and Frozen History PDFs are untouched. Pass Perks are not synced — each computer
  retrieves the same public pages.
* Link allowlist: two exact corporate pages are added — `/pass-perks-terms` and `/perks-and-play` — as the
  official source of program rules. No other non-Carowinds Six Flags path is accepted.

## 10. UI

* **Products → Pass Perks** panel below Current Deals: month picker ("November 2026 (this month)", plus every
  observed month), last check / failure line, compact rows (name, type tag, requirement · confirmed dates ·
  status word when not current, View details), the individual-pass note and a "Program terms" link. Empty
  states: "Pass Perks for {month} have not been verified. Last successful check: …", "Unable to refresh;
  showing previously verified rewards (…)", "Pass Perks not checked yet …". An earlier month says "Earlier
  month — listed on the official page … Shown for reference; not the current list."
* **View details** reuses the Phase 31C dialog (Escape closes, focus returns, Tab cycles, Narrator names):
  reward, eligible passholders, how it is earned, reward loading, dates with official wording, locations,
  usage limits, conditions, sources that disagree, earlier wording, "Your pass", and official links.
  Program-wide statements are visibly labelled as program terms; assigned years are noted.
* **Calendar**: small gift marker (`PassdownGiftIconGeometry`, accent colour) next to the day number with a
  count for several perks; tooltip and the cell's automation name say "Pass Perk date: …". Selected-date panel
  "PASS PERKS" group.
* Existing theme styles only; light and dark both use theme brushes.

## 11. Deterministic tests

Fixtures `fixtures/phase32/` are trimmed copies of the three official pages captured 2026-11-21 (images,
navigation and SEO removed; structure and values unchanged). Variants are derived in tests. No historical
reward is hard-coded in production code.

| Project | Tests |
|---|---|
| Domain | `Phase32PassPerkTextTests` (year/month boundaries, date lines, visits, pass types, classification, categories, discrepancies, identity, changed terms), `Phase32PassPerkScheduleTests` (inclusive windows, upcoming, visit-based phases, booking deadline, removal, staleness, marker rules, multiple perks per day, inferred/heading dates never marked) |
| Application | `Phase32PassPerksTests` (merge, changed terms, verified removal vs incomplete page, new month as history, retention, terms gap-filling, registration last-known-good, empty observation never stored, month views, Calendar entries, JSON round trip) |
| Infrastructure | `Phase32PassPerksSourceTests` (official fixtures, visit-based campaign, monthly items, lodging, year-round benefits excluded, program terms and discrepancy, failed page, network error, missing structure, zero rewards, partial failure, optional page failures, later month, stale page, duplicate identities), `Phase32PassPerksStorageTests` (real SQLite at schema 18, failed check keeps data, changed terms through SQLite, settings file); opt-in `PassPerksSourceLiveSmokeTests` |
| UI | `Phase32PassPerksUiTests` (rows, details, links/allowlist, program terms, panel states, month history, failed refresh, Calendar markers/count/list/month-long summary/details request/setting on-off, setting persistence and failure, markup accessibility); `CompositionRootTests` (15 registrations, PassPerks catalog source); `Phase31CCompactProductsUiTests` (3 View details links) |

Visual harness: `tools/visual-fixtures` gains synthetic October perks (`Phase32Fixtures.cs`) and the
scenarios `Associate-PassPerks`, `Associate-Details-VisitPerk`, `Associate-Details-PerkTerms`,
`Calendar-PassPerks`, `Calendar-PassPerksMonthLong`, `Calendar-PassPerksOff`, `Settings-CalendarDisplay`.
Mac renders (light/dark, 960–1920 px) showed no clipping, horizontal overflow or buttons past the edge.

## 12. Windows testing checklist (not yet done — Mac renders are not Windows validation)

1. Products → Pass Perks lists the current month after Check Online; the month picker works with keyboard
   (Tab, Alt+Down, arrows, Enter) and Narrator reads "Pass Perks month".
2. Each row's View details opens the dialog; Escape closes it and focus returns to the link; official links
   open in the default browser (park page, terms, FAQ).
3. Calendar shows the gift marker only on confirmed dates; the tooltip and Narrator text say "Pass Perk date";
   a shared date shows a count and lists every perk.
4. Settings → Calendar → Show Pass Perks on Calendar: off hides markers immediately, on restores them; the
   choice survives an app restart and an in-app update.
5. 100%, 125% and 150% scaling: no clipped labels in rows, dialog, day cells or the selected-date panel.
6. Light and dark themes: marker and status text readable.
7. Disconnect the network and Check Online: the panel shows "Unable to refresh; showing previously verified
   rewards" and the list stays.

## 13. Known limitations

* Years are inferred from the month heading and check date because the official page omits them (flagged).
* Text rules are pattern-based on today's official wording; a reworded perk may lose a structured field (its
  official paragraphs are always kept and shown). The live smoke test catches structural changes.
* History starts with the first check that includes this build; earlier months (e.g. September 2026) were not
  captured and are not reconstructed.
* Individual guests' rewards (Six Flags app wallet) are intentionally out of scope.
* Only the Carowinds park page is read for rewards; other parks' perks are ignored.
* Windows DPI scaling and Narrator are untested until the checklist above is run on Windows.
