# Phase 1 discovery — official source behavior

Isolated, ad hoc HTTP checks (not part of the production solution) against
the official sources listed in AGENT_BUILD_SPEC.md §5.1, run from this dev
host on 2026-08-20. This documents actual observed behavior — nothing here
is guessed or carried over from the old application (spec §20).

No previous application source tree exists on this machine to compare
against, so all findings below are independent (see the Phase 1 plan's
Context section).

## Reachability and timing

| Source | URL | HTTP status | Response time |
|---|---|---|---|
| Park hours | `https://www.sixflags.com/carowinds/park-hours` | 200 | ~0.9s |
| Buy/checkout shell | `https://cf-ca.buy.sixflags.com` | 200 | ~0.5–1.2s |
| Carowinds catalog | `https://www.sixflags.com/carowinds` | 200 | ~0.8s |

All three are reachable and fast from this host. Actual figures will vary
on the target Windows office network and should be re-measured there
before treating these as representative.

## `sixflags.com/carowinds/*` — Next.js + Sanity CMS

The park-hours and catalog pages are a Next.js **App Router** site. A plain
HTTP GET returns full HTML including the React Server Component payload,
streamed inline via `self.__next_f.push(...)` `<script>` tags — this is
**not** a client-only shell; a lot of structured content is present in the
raw response with no browser execution required.

Confirmed present in the raw HTML, keyed with explicit Sanity `_type`
values (`parkHours`, `activitiesDay`, `activitiesDuration`,
`showTimeSchedule`, `promoBars`, …):

- Page SEO/metadata for the `parkHours` document (ids, timestamps, no
  per-date calendar).
- Special-event activity schedules (e.g. a "Summer BLAST! Labor Day
  Fireworks Show" entry with day-of-week `availableTimes`).
- Promo-bar entries with real `dateWithTimezone` values (several dated
  2026-03 through 2027-01), used for marketing countdown copy, not gate
  hours.

**Not found** in the raw HTML: a per-date operating-calendar array (open
time / close time keyed by specific date) for the park itself. Searched
for `open`/`close`/`gateOpen`/`gateClose`/`calendarDay`/`operatingHours`
keys and every ISO date token in the payload — the date tokens present are
all promo/event dates, not a day-by-day hours grid. The actual calendar
grid a visitor sees on `/park-hours` is most likely populated by a
separate client-side fetch after hydration (page evidence: `park` object
carries `accessoAttribute`/`accessoBaseUrl` fields, suggesting an Accesso-
hosted hours/ticketing backend, currently `null` on this page — not yet
confirmed).

**Implication for Phase 3:** a direct HTTP GET + a parser for the
`self.__next_f.push` RSC payload can cheaply retrieve CMS metadata,
promos, and special-event schedules without Playwright. It cannot yet
retrieve the actual day-by-day park hours calendar — that requires a real
browser network trace to identify the specific client-side API call before
any direct-HTTP adapter can be written for it. Do not guess the endpoint;
observe it.

## `cf-ca.buy.sixflags.com` — Angular SPA behind Cloudflare

The checkout root returns a minimal Angular application shell (`<title>`
tag confirms Angular; ~24 KB response, no product or price data present).
The response is served through Cloudflare (`server: cloudflare`,
`cf-cache-status: HIT`, `__cf_bm` bot-management cookie set).

**Implication for Phase 3:**
- Product names, prices, and calendar selection state are populated
  entirely client-side after Angular bootstraps. A plain HTTP GET cannot
  retrieve exact-date product data here — Playwright (or a discovered
  backend API called by the Angular app) is required, matching spec §6.4's
  "prefer direct endpoints, keep browser retrieval as fallback" guidance —
  for this specific source, direct retrieval is not currently viable
  without further investigation.
- The Cloudflare bot-management cookie means automated access should be
  designed for realistic pacing/retries rather than tight polling, and
  failures here may sometimes be bot-mitigation rather than a genuine
  source outage — worth distinguishing in adapter error detail (spec
  §5.3's "technical error detail for logs").
- The spec's SnapWizard/FastLaneOnly/HAPWizard checkout variants were not
  individually probed: the spec does not give their exact paths, the root
  shell is identical Angular for any deep link, and guessing URLs is
  against working rules. Phase 3 should identify these through a real
  browsing session (Playwright), then verify with the exact-date checks in
  spec §5.4.

## Baseline timings

For both domains, plain-HTTP GET response time was under 1.3s from this
host. This is not a useful baseline for Playwright-driven flows (page
load + hydration + calendar interaction), which Phase 3 should measure
separately once a real navigation flow exists.
