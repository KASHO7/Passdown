# Phase 33 — Persistent live PDF preview and direct printing

Status: implemented and tested on the macOS development host. **Windows printing and Windows rendering have
NOT been validated yet** — see the local Windows checklist
(`docs/releases/rc12-phase33-localtest-windows-checklist.md`). Not pushed, tagged or published; no RC.12.

## What the associate gets

- The Associate Passdown body is now **editor | divider | PDF preview**. The preview shows the actual
  generated Passdown PDF and stays in place while moving between Overview, Products, Shows and Notes (only
  the editor's section body changes; the preview is never rebuilt by navigation).
- The preview updates by itself shortly (≈0.45 s) after editing stops.
- The preview toolbar: **Print**, **Save PDF**, previous/next page with "n / N", zoom out / zoom % / zoom in,
  **Fit width**, **Fit page**, refresh, **Hide Preview**. Keyboard: Tab reaches every control; on the pane
  Ctrl+Plus / Ctrl+Minus zoom, Ctrl+0 fit width, Ctrl+9 fit page, Page Up / Page Down change page.
- The Associate action bar gains **Print** (always present, so printing works with the preview hidden) and
  **Show Preview / Hide Preview**. Create PDF, Copy and More are unchanged.
- The divider can be dragged (or moved with the arrow keys when focused). The preview's visibility, width,
  and zoom/fit choice are remembered on this computer.

## UI architecture

| Piece | Where | Role |
|---|---|---|
| `PassdownPdfPreviewViewModel` | Desktop/ViewModels | Debounce, generation tokens, document cache, page/zoom state, Print, Save PDF, layout preferences. No Avalonia controls; Avalonia types only via `IImage`. |
| `PassdownPdfPreviewView.axaml` | Desktop/Views | The pane: header + wrapping toolbar + page viewer + status overlay. Code-behind only reports viewport size and DPI scaling. |
| `AssociateView.axaml` | Desktop/Views | Body grid `*, 6, preview`. The existing `140,*` rail+section grid is untouched inside column 0. Code-behind applies the view model's resolved column widths and commits the divider width on release. |
| `PdfPreviewLayout`, `PdfPreviewZoom` | Desktop/Presentation | Pure rules: minimum editor 660 DIP (rail included), minimum preview 340 DIP, narrow threshold 1006 DIP, width clamping, zoom steps 25 %–400 %, fit calculations. |
| `AssociateViewModel` | Desktop/ViewModels | Owns `Preview`. Supplies the capture (current fields applied to the loaded workspace in memory) and notifies it on every edit (`ScheduleAutosave`), workspace change, template change and date change. |

Layout modes: **Split** (wide), **EditorOnly** (preview hidden, or a narrow window), **PreviewOnly** (narrow
window after Show Preview — the preview temporarily replaces the editor; Hide Preview returns). Narrow-window
toggling is transient and never rewrites the saved Show/Hide choice.

## PDF rendering library and licensing

**PDFium** (Google/Foxit's PDF engine, the one in Chrome), prebuilt by the *pdfium-binaries* project and
consumed from NuGet:

| Package | Version | Purpose |
|---|---|---|
| `bblanchon.PDFium.Win32` | 159.0.8138 (published 2026-11-04) | `runtimes/win-x64/native/pdfium.dll` (7.8 MB) — the only file a win-x64 publish ships |
| `bblanchon.PDFium.macOS`, `bblanchon.PDFium.Linux` | same | development hosts and tests only (RID-specific publish excludes them) |

- License: PDFium **BSD-3-Clause**; bundled third-party code under Apache-2.0, BSD, MIT, FreeType License,
  IJG, libpng, zlib, ICU and similar permissive licenses; NuGet packaging Apache-2.0; build scripts MIT.
  **No commercial license, no copyleft, no runtime fee.** Texts ship in `licenses/PDFium-LICENSE.txt`,
  `licenses/pdfium-binaries-LICENSE.txt` and `licenses/PDFium-dependencies/*.txt` (17 files, copied unchanged
  from the official `chromium/8138` release archive, whose `pdfium.dll` is byte-identical to the NuGet one —
  SHA-256 `71a3e63e…891821943`). `THIRD-PARTY-NOTICES.txt` section 14; `release-content-rules.json` requires
  `pdfium.dll` and every license file.
- Built **without V8/JavaScript and without XFA** (`args.gn`: `pdf_enable_v8 = false`, `pdf_enable_xfa =
  false`), so no PDF script can run. It only ever opens bytes this application generated in memory.
- Offline: no network, no web service, nothing uploaded.
- Our wrapper: `Infrastructure/PdfOutput/PdfiumNative.cs` (13 `LibraryImport` functions from
  `fpdfview.h`) and `PdfiumPdfRasterizer.cs`. PDFium is not thread-safe, so every call holds one
  process-wide lock; documents are safe to use from any thread. The PDF bytes are copied to unmanaged memory
  for the document's lifetime and freed on dispose.
- Alternatives rejected: re-creating the PDF in XAML (forbidden; drifts from the real output);
  `Windows.Data.Pdf` (WinRT — would make the app Windows-TFM-only and untestable on the dev hosts);
  MuPDF (AGPL); Ghostscript (AGPL, and `gs*.dll` is already forbidden by the release rules); PdfiumViewer /
  Docnet (unmaintained wrappers over the same engine); PDFtoImage (couples to a SkiaSharp version that must
  match Avalonia's).
- Package-size impact: +7.8 MB uncompressed (`pdfium.dll`) + ~150 KB license texts.

## Printing architecture

`IPdfPrintService` (Application) — one implementation per platform, chosen in the composition root:

- **Windows — `WindowsPdfPrintService`** (Infrastructure/PdfOutput, `[SupportedOSPlatform("windows")]`):
  1. Opens the PDF with PDFium (a damaged document fails before any dialog).
  2. Shows the standard Windows print dialog **`PrintDlgEx`**, owned by the app window: printer choice,
     **Preferences** (paper size, orientation, duplex, colour/quality as the driver offers), **copies** and
     **collate** (driver-handled via `PD_USEDEVMODECOPIESANDCOLLATE`), **page range** (All / Pages "1-2,3").
     Selection and Current Page are hidden (they mean nothing here). Nothing prints unless the user presses
     Print.
  3. `StartDoc` runs on the UI thread, so a "print to file" driver's own Save prompt (Microsoft Print to PDF)
     is modal over the app; cancelling it is reported as a normal cancel.
  4. Pages are spooled on a background thread: each page is rasterized by PDFium **at the printer's own
     resolution (capped at 600 DPI)** with PDFium's print flag, in horizontal bands of ≤ 16 MB, converted to
     24-bit DIB rows and sent with `StretchDIBits`. Placement is **actual size (100 %)** on the physical sheet
     when the page fits (Letter on Letter: the PDF's own margins and page breaks are exactly preserved),
     otherwise uniformly shrunk to fit and centered (e.g. A4, or landscape). `PrintLayoutMath` holds the pure
     arithmetic and is unit-tested on every host.
  5. Errors are mapped to plain-language results: no printer installed / printer not found → *Unavailable*;
     `StartDoc`/`StartPage`/`EndPage` failures → *Failed* (the job is aborted, nothing more is sent);
     anything unexpected is caught and reported — printing never crashes the app.
- **macOS / Linux dev hosts — `UnsupportedPlatformPdfPrintService`**: explains that printing is available on
  Windows and to use Save PDF.

Raster (not vector) printing was chosen deliberately: it is PDFium's best-tested path, it produces exactly
what the preview shows, and at printer resolution text is crisp. (PDFium's direct-to-HDC GDI path is the
future alternative if a printer driver mishandles large bitmaps.)

Print safety: never the default printer silently; never automatic on preview changes; never saves,
finalizes or marks anything; never alters the draft; no temporary files (the PDF lives only in memory and
the spooler); cancelled dialogs and unavailable printers are handled quietly.

## PDF byte consistency

There is **one document per editor state**, `PassdownPdfDocument` (bytes, date, template, page count,
content version, generated-at, fingerprint):

- Generated by the existing pipeline — `IAssociatePassdownPdfExportService.GeneratePdf` (the same service
  Create PDF and finalization use) — from the editor's current fields applied in memory to the loaded
  workspace (`WithDraftApplied`, exactly what Copy for Teams already uses). Unsaved edits are included
  without saving them.
- The preview rasterizes those bytes. **Print and Save PDF hand on the very same byte array**
  (`EnsureLatestDocumentAsync` regenerates first only when the editor changed since — e.g. an edit still in
  the debounce window — so what prints is always the latest state).
- Create PDF keeps its established workflow (flush pending edits, generate, native save dialog) and the same
  suggested file name (`Carowinds-Operations-yyyy-MM-dd.pdf`, now shared from one helper). Both call the one
  export service; there is no second PDF pipeline.
- Templates: the preview, Print and Save PDF always use `SelectedTemplateOption` — the same value Create PDF
  and the More → PDF Style menu use. All three templates (Classic, Compact, Modern) work; the code never
  enumerates templates, and a test iterates `Enum.GetValues<AssociatePassdownTemplate>()` so a future
  template is covered automatically. The preview never changes the saved template preference.

## Refresh and debounce

- `NotifyContentChanged()` bumps a content version. While the preview is shown, it (re)starts a
  **450 ms** debounce; typing therefore regenerates once after the user pauses, not per keystroke.
- Hooks: every edit (`ScheduleAutosave` — attendance, pre-solds, hours/gate overrides, private buyout,
  Daily Feature, products and price overrides, custom products, Include in Passdown switches, shows, notes
  text), every workspace replacement (date load, Check Online, official-data reload, operational-note
  add/remove/replace, ERT changes), template changes, and date changes. An autosave of edits the preview
  already shows does **not** regenerate.
- Duplicate work is avoided: an unchanged version reuses the current document; a regenerated document whose
  content fingerprint (SHA-256 of the bytes with `/CreationDate` and `/ModDate` neutralized) matches the
  one shown keeps the page image, page number and zoom — no flicker.
- No polling: nothing runs unless the editor changed or the user acted.

## Stale-render handling

- Every generation takes a token (`_generation`). A result whose token is no longer the latest is never
  displayed (and its native document is disposed). A document is installed only if its content version is
  not older than the installed one. Page re-renders (navigation/zoom) carry their own token.
- **Date change** (`NotifySelectedDateChanged`): the page is cleared and disposed immediately, the cached
  document dropped, in-flight work invalidated, and the pane says "Loading this date's preview…". The
  capture refuses to build anything until the workspace on screen is the selected date's. Print/Save during
  that moment are refused with "Try again in a moment" — another date's PDF can never be printed or saved as
  this date's.
- A failed generation or render shows an error with **Try Again** in the pane; the editor stays fully usable
  and the draft is untouched. A render failure still leaves the generated document available to Print/Save.
- The pane shows "Updating…" while a replacement is generated, over the current date's previous page.

## Preview preferences

`pdf-preview-layout.json` in `%LOCALAPPDATA%\Carowinds Passdown` (beside `appearance.json` etc.):
`IsPreviewVisible`, `PreviewPaneWidth` (DIP), `FitMode` (FitWidth / FitPage / Custom), `Zoom`. Written
atomically; a missing, corrupt or out-of-range value falls back to its own default (shown, 480 DIP, fit
width, 100 %). **Not synced** (outside the Personal Sync folder; Personal Sync formats unchanged), **no
database table, no migration** (schema stays 18). The saved width is a preference: every time it is applied
it is clamped to the window actually available, so a width saved on a large monitor never clips the editor
on a smaller one; the stored preference itself is only rewritten when the user moves the divider.

## Performance (macOS dev host, Apple Silicon, warm, median of 15)

| Document | Generate (QuestPDF) | Fingerprint | Open | Render page, fit-width 480 DIP @100 % / @150 % |
|---|---|---|---|---|
| Normal day, Classic / Compact / Modern (1 page, 36–44 KB) | 3.5 / 2.6 / 2.6 ms | < 0.1 ms | < 0.1 ms | 2.4 / ≈2.1 ms |
| 80 long notes (2 pages, 46–53 KB) | 21 / 20 / 15 ms | < 0.1 ms | < 0.1 ms | ≈4 / ≈4.5 ms |

One displayed page at 150 % scaling is a 906 × 1172 bitmap (4.1 MB); only the current page is held. The
first generation after launch is slower (JIT/QuestPDF warm-up). **Windows numbers are not measured** — the
checklist asks the tester to note responsiveness.

## Resource disposal

- At most one rasterized page image and one open PDFium document are held. Replacing either disposes the
  old one; a superseded render disposes its own results.
- Hiding the preview cancels pending work and releases the page image and PDFium document (the generated
  byte array is kept so an unchanged Print/Save does not regenerate).
- Printing opens its own PDFium document and disposes it, the printer DC and the dialog's DEVMODE/DEVNAMES
  handles in `finally` blocks.

## Automated tests (+95; deterministic total 5,347)

| File | Tests | Covers (Phase 33 list numbers) |
|---|---|---|
| `Ui.Tests/Phase33PdfPreviewViewModelTests.cs` | 37 | 1, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25; duplicate avoidance, fingerprint, render failure, preflight failure, date change mid-generation |
| `Ui.Tests/Phase33AssociatePreviewIntegrationTests.cs` | 16 | 4, 5, 6, 7, 8, 9, 20, 26, 27; attendance/Daily Feature edits, autosave does not regenerate, section navigation does not reset, message cleared on date change |
| `Ui.Tests/Phase33RealPdfRenderingTests.cs` | 6 | 2, 3 (real QuestPDF × real PDFium, every template, page count/size cross-checked with PdfPig), multi-page, invalid bytes, dispose, concurrency, 25 byte identity |
| `Ui.Tests/Phase33PreviewMarkupTests.cs` | 6 | placement outside sections, one shared Print command, toolbar completeness, accessible names, shortcuts, tokens/literal colors, white paper in both themes |
| `Ui.Tests/Phase33PreviewWindowTests.cs` (**run alone**) | 9 | real-window layout at 1920/1440/1280, section switching keeps the pane, hide/show widths, narrow single-pane, minimum-width toolbar wrap, clamped restore (15), multi-page navigation |
| `Infrastructure.Tests/PdfOutput/Phase33PdfOutputTests.cs` | 21 | print placement (actual size, A4, landscape, legal), DPI cap, banding, page ranges, DIB conversion; preference file defaults/round-trip/corruption/out-of-range; local-only path (28) |

Item 28 (no sync/database regression): no migration was added (schema 18 tests unchanged and passing),
Personal Sync code untouched, the preference path is asserted local-only. Item 26: all pre-existing Create
PDF tests pass unchanged, plus a new one with the preview present.

Deterministic partition: add **`Phase33PreviewWindowTests`** as the sixth Ui.Tests class run alone (real
headless `Window`s), alongside `OperationsCalendarViewTests`, `Phase15ERealWindowLayoutTests`,
`RealWindowStartupTimingTests`, `SpellCheckNavigationLifecycleTests`, `Phase21DMathEditorWindowTests`.
Counts: Domain 462, Application 1,369, Infrastructure 1,184, Documents 479, Ui mixed 1,688, run-alone
26 + 117 + 2 + 7 + 4 + 9. Clean `dotnet build -c Release`: 0 warnings, 0 errors.

**Mocked tests do not prove physical printing.** The print dialog and GDI spooling run only on Windows and
are validated by the manual checklist.

## Visual validation (macOS, Skia headless — not Windows validation)

`tools/visual-fixtures` now composes the real QuestPDF export and real PDFium rasterizer, and adds
`Preview-*` scenarios (Hidden, Modern, Compact, FitPage, Zoomed, LongNotesMultiPage, WideLayout,
NarrowPreviewFirst, SwitchingDates). Rendered in Light and Dark at 960×640, 1280×690, 1280×860 and
1920×1000 together with every existing `Associate-*` scenario (Overview, Products, Shows, Notes, …).
Geometry report: **no button past the right edge and no page-level horizontal overflow in any capture**;
the only horizontal overflow is inside the preview's own scroller at 150 % zoom (expected — the page is wider
than the pane and scrolls). The paper stays white in Dark mode; the desk and chrome follow the theme.
Windows display scaling (100/125/150 %) and Narrator must still be checked on Windows.

The harness now also uses an in-memory template preference store; before this fix a harness run that
changed the PDF style would write the developer profile's `associate-passdown-template.json`.

## Windows test checklist

See `docs/releases/rc12-phase33-localtest-windows-checklist.md`.

## Known limitations

- Windows printing, Windows rendering, display scaling and Narrator are **not yet validated on Windows**.
- Printing is raster at ≤ 600 DPI; very large print jobs spool more data than vector printing would.
- Copies/collate are delegated to the printer driver; a driver without multi-copy support disables the
  Copies box in the Windows dialog (Windows behavior).
- If the printer is offline, Windows usually accepts the job into its queue; the app reports "Sent to …"
  and the queue shows the problem (the app cannot see past the spooler).
- On the macOS/Linux dev hosts Print explains it is Windows-only.
- The preview shows one page at a time (page buttons/keys move between pages) rather than a continuous
  scroll of all pages — deliberate, to hold only one page bitmap.
- `Hide Preview` appears both on the action bar toggle and in the pane header (the pane header is the
  in-pane control the spec asks for).
