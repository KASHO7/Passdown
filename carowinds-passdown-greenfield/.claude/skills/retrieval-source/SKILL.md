---
name: retrieval-source
description: Add or modify an official Carowinds/Six Flags retrieval source (park hours, show schedules, operating events, dated admission/Fast Lane/haunted/special tickets, catalog passes, add-ons, promotions, Pass Perks) or the shared HTTP transport, cache and refresh path. Use for any retrieval or freshness work.
---

# Official retrieval source

Follow `.claude/rules/retrieval.md`, `AGENT_BUILD_SPEC.md` §6–§7,
`docs/architecture/retrieval-and-refresh.md` and the relevant `docs/retrieval/*.md` source record.

## 1. Official sources only

Use only official Carowinds/Six Flags endpoints and pages. Never use search snippets, third-party
sites or guessed values. If it is unclear whether an endpoint is official and public, ask the
product owner — that is a product/data decision.

## 2. Current architecture (preserve it)

- Contracts live in `src/Carowinds.Passdown.Application/Retrieval` (`IOfficialDataSource<TPayload>`,
  `SourceResult`, payloads, `RetrievalRequest`); status values are
  `Carowinds.Passdown.Domain.Retrieval.RetrievalStatus`.
- Implementations live in `src/Carowinds.Passdown.Infrastructure/Retrieval/Sources`; shared HTTP
  transport in `Retrieval/Http` (`RetrievalHttpClient`, `CoalescingRetrievalHttpClient`). Shared
  Accesso transport/evidence is in `AccessoDatedSourceSupport`; keep source-specific mapping in each
  source.
- Retrieval is HTTP-based: structured API first, then HTTP/HTML parsing. Playwright/browser
  automation is not a production dependency; the dormant `Retrieval/Browser` seam stays as-is. Do
  not reintroduce browser automation without demonstrated need and approval.
- One source failing must not break others; each source has bounded concurrency, timeouts and
  cancellation; each result persists independently as it completes.

## 3. Request coalescing

`CoalescingRetrievalHttpClient` shares concurrent equivalent official requests; failures are never
cached. POSTs coalesce only when endpoint, every header and the body are equivalent; for the Accesso
calendar the comparison ignores only the client-generated per-request nonce (`request_token`,
`AccessoKeywordCalendarCoalescing.KeyBody`). Never merge arbitrary POSTs. Each source still
interprets the shared response for its own requested date. Do not add caches or retries that change
request volume without measurement (`.claude/rules/performance.md`).

## 4. Exact-date verification (dated products)

Dated admission, Fast Lane, haunted and special dated tickets must confirm that the official
response is for the requested date before reading names or prices. Reject a response that fell
back to today or another date. "Starting at" marketing prices are never exact-date prices.

## 5. Honest status and evidence

Use the distinct `RetrievalStatus` values; `SourceResult` carries a payload only when verified, so
closed, not-published, unavailable or error results never carry stand-in values. Keep full evidence
(source, URL, requested and confirmed date, time checked in `America/New_York`, status, details,
strategy). Staleness is a presentation/freshness concern, not an adapter result.

## 6. Last-known-good and local-only navigation

A failed retrieval never overwrites verified data; last success and last attempt are stored
separately. Manual draft edits never modify official cache. Selecting a date, navigating, filtering
or disclosure are local-only; online retrieval happens only through scheduled refresh or explicit
Check Online. Dated and catalog data stay in their separate cache scopes with configured freshness.

## 7. Fixtures and tests

Add synthetic, sanitized fixtures under `fixtures/<source>/` covering applicable cases (open,
closed, not published, exact dated products, marketing price, seasonal present/absent,
renamed/removed product, partial/broken page). Contract tests run offline in
`tests/Carowinds.Passdown.Infrastructure.Tests`; `Category=LiveSmoke` tests are opt-in only (see the
`build-and-test` skill). Never add cart items or submit sign-in/payment forms during live checks.

## 8. Documentation

Update the matching `docs/retrieval/*.md` record and the retrieval architecture guide when sources,
transport, coalescing or freshness change; update `AGENT_BUILD_SPEC.md` only for approved product
behavior changes. Close with the `phase-review` skill.
