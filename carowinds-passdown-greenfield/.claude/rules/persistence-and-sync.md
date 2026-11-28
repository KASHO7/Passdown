---
paths:
  - "src/**/Storage/**"
  - "src/**/Persistence/**"
  - "src/**/PersonalSync/**"
  - "src/**/Associate/**"
  - "src/**/OperationalNotes/**"
  - "src/**/History/**"
  - "tests/**/PersonalSync/**"
  - "tests/**/Storage/**"
---

# Persistence and Personal Sync

Use SQLite and existing repository ports; no ORM, database replacement, schema or journal-mode change without demonstrated need and approval where compatibility changes. Preserve released migrations, verified backups, integrity checks and rejection of unsupported newer schemas. Keep live DB/WAL/SHM in local app data, never cloud-sync. Preserve last-known-good, official/manual separation, date-specific drafts, autosave retry/flush and conditional conflict-safe saves. Finalized snapshots/PDFs are immutable and never reconstructed from today's prices. Persistent notes are separate from frozen included copies.

Preserve sync file formats, revision/identity tracking, tombstones, ordering, conflict resolution and atomic writes. No silent conflict overwrite; device-local preferences stay local. Official price observations are separate from editable overrides; retired channels stay retired. Format changes require explicit compatibility analysis and approval. Use synthetic multi-device/offline/failure tests; consult `docs/personal-sync-architecture.md` and current architecture.
