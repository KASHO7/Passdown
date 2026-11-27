# Retrieval and refresh

Current-state guide — see [review baseline](../architecture.md).

Infrastructure adapters retrieve official structured API/HTTP/HTML data; no production Playwright package. Application orchestrates freshness, bounded refresh and independent persistence. Exact-date verification, provenance, source states and last-known-good remain mandatory. Phase 34B-2 added official POST coalescing accounting for Accesso request nonce (not identical raw bodies); Phase 34C consolidated shared Accesso transport/evidence without changing source-specific mapping. A dormant browser-session seam remains intentionally. Date switches do not retrieve online.

## Evidence and detailed implementation records

- [phase31-dynamic-seasons-and-deals.md](../phase31-dynamic-seasons-and-deals.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase34c-repository-cleanup.md](../phase34c-repository-cleanup.md)
