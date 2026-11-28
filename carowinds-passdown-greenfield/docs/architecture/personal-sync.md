# Personal Sync

Current-state guide — see [review baseline](../architecture.md).

File-based exchange is distinct from the live SQLite database. Preserve existing formats, atomic writes, identities, revision/conflict handling and immutable finalized records. Synced official price observations are distinct from overrides; preserve ERT conflict resolution. Morning Message sync is retired and its old files remain untouched. Device-local appearance, motion, navigation and preview preferences are not synchronized. Phase 34B-2 deferred transient sharing-violation retries; existing full-disk HResult checks are not sharing-violation retries.

## Evidence and detailed implementation records

- [personal-sync-architecture.md](../personal-sync-architecture.md)
- [phase31b-product-details-price-history-ert-sync.md](../phase31b-product-details-price-history-ert-sync.md)
- [morning-message-rollback.md](../morning-message-rollback.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
