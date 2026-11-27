# Data and persistence

Current-state guide — see [review baseline](../architecture.md).

SQLite schema 18 is the recorded baseline; migrations and backups protect upgrades. Keep database and WAL/SHM files local, outside cloud-sync. Official cache retains last-known-good; drafts are date-specific and conflict-safe. Finalized snapshots and stored PDFs remain immutable; Operational Notes are independent from frozen included copies. Device-local settings use atomic local files and are not synced. Weather (Phase 37B, not activated) has its own disposable local file, `weather-cache.json` under the local application-data folder, written by `FileWeatherCacheStore` with the shared atomic writer: format version 1, at most 256 KB, no SQLite table or migration, never synced, never in drafts, History, PDFs or release packages; an unreadable, oversized or unknown-version file is ignored. The application does not write it yet. Synchronous SQLite work was deliberately not broadly offloaded in Phase 34B-2; Windows contention evidence is needed.

## Evidence and detailed implementation records

- [personal-sync-architecture.md](../personal-sync-architecture.md)
- [phase23a-save-failure-freeze-fix.md](../phase23a-save-failure-freeze-fix.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase35f-operational-notes-history-settings.md](../phase35f-operational-notes-history-settings.md)
