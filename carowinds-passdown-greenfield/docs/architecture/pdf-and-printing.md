# PDF and printing

Current-state guide — see [review baseline](../architecture.md).

QuestPDF generates shared document bytes; PDFium rasterizes live preview and supports native Windows printing. `PassdownPdfPreviewViewModel` captures workspace state, schedules background work and rejects stale results. Phase 34B-1 immediate leading refresh, 200 ms trailing quiet window, serialized scheduled generations and delayed Updating indicator supersede the original fixed 450 ms debounce. Workspace replacement refreshes immediately; hidden preview cancels pending work. Phase 34B-2 warms the engine and renders directly into the bitmap. Phase 34C reduced embedded Lato faces with PDF equality checks. Preserve one generation pipeline and latest-edit print/save/finalization.

**Weather Outlook (Phase 37F).** `AssociatePassdownDocumentModel.Weather` (a `PdfWeatherOutlook`, or null) is set by `AssociatePassdownDocumentModelBuilder.Build(workspace, generatedAt, weather)` only when the outlook's `OperatingDate` equals the workspace's; `IAssociatePassdownPdfExportService` has a weather overload (its default implementation prints none). Each template, only when `Weather` is set, wraps the operations rows beside it in a row with a fixed right-hand column: Classic beside caption through Pre-solds (220 pt; beside hours and gates only when Private Buyout details print), Modern beside the hours headline and metadata row (196 pt; the four metadata columns narrow), Compact beside the hours and attendance lines (204 pt). Early Ride keeps the full width. `PdfWeatherOutlookComponent` measures each template's levels with `DynamicContext.CreateElement` and draws the first whose natural size fits the column and the template's height budget (the fixed height of the rows beside it), else nothing — so the header never grows and the weather-free layout code path is unchanged (bytes equal to a document without weather). Icons are local SVG strings (QuestPDF `SvgImage`, parsed once per document) in the template ink with white masks; no image files, fonts or emoji; text uses the embedded Lato faces. Finalization (`FinalizedPassdownSnapshotBuilder`) never passes weather.

## Evidence and detailed implementation records

- [phase33-live-pdf-preview-printing.md](../phase33-live-pdf-preview-printing.md)
- [phase34b1-preview-responsiveness.md](../phase34b1-preview-responsiveness.md)
- [phase34b2-general-performance-optimization.md](../phase34b2-general-performance-optimization.md)
- [phase34c-repository-cleanup.md](../phase34c-repository-cleanup.md)
- [phase37f-compact-pdf-weather-outlook.md](../phase37f-compact-pdf-weather-outlook.md)
