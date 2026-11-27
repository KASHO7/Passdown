---
paths:
  - "src/**/Retrieval/**"
  - "src/**/Refresh/**"
  - "src/**/Freshness/**"
  - "src/**/Catalog/**"
  - "src/**/PassPerks/**"
  - "tests/**/Retrieval/**"
  - "fixtures/**"
---

# Official retrieval

Only official Carowinds/Six Flags sources. Prefer structured API then HTTP/HTML. Playwright is not a current production dependency; do not reintroduce browser automation without demonstrated need and approval. Preserve exact-date verification, provenance, freshness, distinct source states, independent failures, last-known-good, bounded concurrency, timeouts/cancellation and coalescing. Accesso POST coalescing must account for the per-request nonce, not merge arbitrary POSTs. Keep shared transport separate from per-source interpretation. Date navigation, filtering and disclosure are local-only. No invented data or unmarked stale/inferred values. Maintain fixture tests; live tests opt-in. Read `docs/architecture.md` and relevant `docs/retrieval/` records.
