# Application lifecycle

Current-state guide — see [review baseline](../architecture.md).

Desktop `App.axaml.cs` is the composition root. Apply saved appearance before the window, check migrations before using storage, initialize services and show the main window. Start sync, refresh, backfill, update checking and (Phase 37C) the weather monitor after window setup; the weather monitor stops with the official-data refresh in the bounded shutdown; PDF engine warm-up is posted after window open. Keep required shutdown/update flushes and cancellation. Eager workspace construction preserves navigation state; measure Windows startup before deferring loads. The scheduled-refresh entry point shares service composition; consult actual `Program.cs` and current registrations before changing startup.

## Evidence and detailed implementation records

- [phase34a-repository-performance-audit.md](../phase34a-repository-performance-audit.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase23a-save-failure-freeze-fix.md](../phase23a-save-failure-freeze-fix.md)
- [phase23d-github-updater.md](../phase23d-github-updater.md)
