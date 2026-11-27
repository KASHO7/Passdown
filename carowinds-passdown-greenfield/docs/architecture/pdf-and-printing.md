# PDF and printing

Current-state guide — see [review baseline](../architecture.md).

QuestPDF generates shared document bytes; PDFium rasterizes live preview and supports native Windows printing. `PassdownPdfPreviewViewModel` captures workspace state, schedules background work and rejects stale results. Phase 34B-1 immediate leading refresh, 200 ms trailing quiet window, serialized scheduled generations and delayed Updating indicator supersede the original fixed 450 ms debounce. Workspace replacement refreshes immediately; hidden preview cancels pending work. Phase 34B-2 warms the engine and renders directly into the bitmap. Phase 34C reduced embedded Lato faces with PDF equality checks. Preserve one generation pipeline and latest-edit print/save/finalization.

## Evidence and detailed implementation records

- [phase33-live-pdf-preview-printing.md](../phase33-live-pdf-preview-printing.md)
- [phase34b1-preview-responsiveness.md](../phase34b1-preview-responsiveness.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase34c-repository-cleanup.md](../phase34c-repository-cleanup.md)
