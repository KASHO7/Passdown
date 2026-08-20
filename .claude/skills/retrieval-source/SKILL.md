---
name: retrieval-source
description: Add or modify an official Carowinds/Six Flags data-source adapter (hours, dated tickets, Fast Lane, haunted attractions, passes, add-ons, promotions). Use for any retrieval/caching work in Infrastructure.
---

# Retrieval source adapter

Follow AGENT_BUILD_SPEC.md §5 (data principles), §6 (retrieval/cache), and the
`IOfficialDataSource` shape in §16.

## 1. Confirm the source is official

Only use official Carowinds/Six Flags endpoints (§5.1, §87–95). Never use
search snippets, third-party ticket sites, or guessed values. If unsure
whether a candidate endpoint is official/public, ask the user — this is a
genuine product/data decision, not a code decision.

## 2. Isolate the adapter

One adapter per source, implementing `IOfficialDataSource`
(`SourceId`, `Scope`, `Freshness`, `RetrieveAsync`). A broken source must not
break others (§6.4 last line, §13).

Prefer direct HTTP/JSON over Playwright (§6.4). Only fall back to Playwright
when no dependable direct endpoint exists, and then:
- one browser per refresh job, reused context, bounded concurrency
- block unneeded images/video/analytics/fonts
- readiness conditions instead of fixed sleeps
- bounded retries with jitter, independent per-source timeout
- reliable cleanup/cancellation

## 3. Classify dated vs. catalog data

Decide which cache table the result belongs to (§6.2):
- `dated-results` — keyed by date + source/category (hours, dated tickets,
  Fast Lane, haunted attractions).
- `catalog-results` — keyed by normalized product + category (passes,
  add-ons, promotions).

Never store catalog data per-date. Assign the correct freshness cadence from
the §6.1 table (daily / 7-day / 14-day) as configuration, not a hard-coded
comparison.

## 4. Exact-date verification (dated products only)

For date-specific admission, Fast Lane, and haunted-attraction products
(§5.4):
1. Open the official checkout calendar.
2. Select the requested date.
3. Confirm the selected date from page state/heading/label/response.
4. Only then read product names and prices.
5. Reject the response if the checkout fell back to today or another date.

Label "Starting at" marketing prices separately — they are never exact-date
prices.

## 5. Set the result status honestly

Use exactly one of: `Verified`, `Closed`, `NotPublished`, `Unavailable`,
`Error` (`Stale` is applied at presentation/domain level, not by the
adapter). Never substitute placeholder hours or another date's price for a
missing value (§5.2).

Retain full evidence on every result (§5.3): source name, URL, requested
date, confirmed date, time checked (`America/New_York`), status, human
detail, technical error detail, retrieval strategy used.

## 6. Last-known-good discipline

A failed retrieval must never overwrite previously verified data (§6.7,
CLAUDE.md "Persistence"). Save last success and last attempt separately.
Save each source's result as it completes, independently of other sources
(§6.5 last line).

## 7. Add fixtures and contract tests before trusting the adapter

Add/update HTML/JSON fixtures under `fixtures/` covering, as applicable
(§14.2): open date, closed date, not-published future date, exact dated
products, Fast Lane Ultimate/reserve examples, "Starting From" marketing
price, seasonal (SCarowinds) products present/absent, renamed/removed
product, partial/broken page. No private company information in fixtures.

Parser/contract tests run against these fixtures and must not require
internet access or gate the normal build (CLAUDE.md "Testing";
AGENT_BUILD_SPEC.md §14.3). Live smoke tests are a separate opt-in tier —
see the `build-and-test` skill.

## 8. Verify

- Run the Infrastructure contract tests for this source (see `build-and-test`
  skill).
- If live-checking, use the opt-in live smoke filter only — never add a cart
  item or submit payment/sign-in forms (§14.3).
