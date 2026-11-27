# Desktop UI

Current-state guide — see [review baseline](../architecture.md).

Avalonia MVVM uses a persistent shell and once-created workspaces. `MainViewModel` controls navigation; `AssociateViewModel` owns draft state and coordinates the four Passdown tabs and PDF companion. `PriceEditorViewModel` owns the anchored price edit lifecycle. Phase 35 introduced shared tokens/styles, collapsible sidebar, appearance/motion controllers, native navy Windows 11 title bar with high-contrast fallback, compact operating-date selector, Calendar marker styling and list/detail Notes/History and Settings. The Dashboard follows the approved Admissions Overview (Phase 35E-3, `2ec6261`): gate card, passdown, shows, today's dated pricing, operational notes, cached Pass Perks and Official Data; `DashboardView` code-behind switches the gate grid and columns by width (layout only). Phase 35G removed the unused Phase 23B `ShowColumnsPanel` and the retired Fury 325 banner asset; tests assert they stay retired. Visual review uses `tools/visual-fixtures` (one scenario per process; headless renders are not Windows evidence). Preserve keyboard access, local-only presentation actions and latest-edit/date safety.

## Evidence and detailed implementation records

- [phase35-unified-ui-modernization.md](../phase35-unified-ui-modernization.md)
- [phase35b-passdown-workspace-redesign.md](../phase35b-passdown-workspace-redesign.md)
- [phase35c-products-pricing-redesign.md](../phase35c-products-pricing-redesign.md)
- [phase35d-shows-notes-shared-polish.md](../phase35d-shows-notes-shared-polish.md)
- [phase35e-dashboard-calendar-redesign.md](../phase35e-dashboard-calendar-redesign.md)
- [phase35f-operational-notes-history-settings.md](../phase35f-operational-notes-history-settings.md)
- [phase35g-final-uiux-polish-readiness.md](../phase35g-final-uiux-polish-readiness.md)
