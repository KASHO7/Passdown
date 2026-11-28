# Performance benchmarks (opt-in)

Phase 34B-2. Lightweight timing and allocation benchmarks for the areas Phases 34A–34B-2 optimized, so a later
change that brings back an unnecessary delay can be noticed. Results: `docs/phase34b2-general-performance-optimization.md`.

- **Opt-in only.** Not part of `CarowindsPassdown.slnx`; the normal build and the deterministic test count never
  depend on it, and nothing here asserts a time. Behaviour that can be checked without a clock (generation
  counts, request coalescing, cache invalidation, cancellation, disposal) is covered by the deterministic tests
  instead (`Phase34B1*`, `Phase34B2*`).
- **Synthetic data only.** A scratch SQLite database (real migrations and repositories) in a folder you pass,
  in-memory PDFs of an invented Passdown, headless Avalonia + Skia for real bitmaps, and the committed retrieval
  fixtures. It never reads or writes the real profile, makes no network request, collects no operational
  content and uploads nothing.
- Numbers describe the machine they were taken on. Do not compare a Mac number with a Windows number.

## Run

Always build Release:

```bash
dotnet build tools/perf-benchmarks -c Release
```

```bash
dotnet tools/perf-benchmarks/bin/Release/net10.0/PerfBenchmarks.dll <benchmark> [options]
```

| Benchmark | What it measures |
|---|---|
| `pdf-startup [runs]` | First-PDF cost in fresh processes (default 7 per mode): **cold**; **warmed** (the background warm-up finished first); **concurrent** (warm-up and first PDF start together — the worst case). Also the warm-up's cost on the thread that starts it. |
| `pdf-render [iterations]` | One preview page at fit-width 460 DIP @150 %: time, managed bytes allocated per render, gen-0/gen-2 collections, managed heap — RC.12 copy path vs the 34B-2 direct-to-bitmap path. |
| `preview [edits]` | Isolated edit → new page image through the real preview view model (leading edge + 200 ms quiet window, real QuestPDF/PDFium/Skia). No window, so the compositor's one frame is not included. |
| `print-bands [pages]` | Investigation only: the Windows print path's 600 DPI band loop (PDFium band render + BGR24 conversion), time and allocations per page. No GDI, no printer driver. |
| `db <scratchDir> [synced counts…]` | Associate date load, Calendar month, Price History open and product switch (before/after the 34B-2 reader), on a synthetic database with 3 years of dated results and the given numbers of synced price observations (e.g. `0 2000 20000`). "Caller" time is time the UI thread would be blocked (SQLite completes synchronously). The database is deleted afterwards. |
| `refresh-requests` | Official POSTs for one 7-day rolling window through the admission and Fast Lane sources, without and with coalescing (deterministic counts). |
| `weather [iterations]` | Phase 37C: weather cache file load, snapshot and Dashboard Weather card build (µs and bytes per call), and Dashboard realization at 1280×860 with and without the card (cached synthetic weather, headless Skia). No NWS request. |
| `pdf-weather [iterations] [outDir]` | Phase 37F: Associate Passdown generation per template without and with the PDF Weather Outlook (median/p90 ms, PDF bytes), the outlook build (selection + wording, µs and bytes per call); with `outDir`, the PDFs and PDFium PNG renders (2 px/pt, colour and grayscale) of the representative weather scenarios. Synthetic forecasts and the Documents test fixtures; no NWS request. |
| `calendar-weather [iterations]` | Phase 37D: the Calendar's selected-date Weather group build (µs and bytes per call: park hours, closed day, past date), view-model date steps with and without weather, and a realized Calendar (1280×860, headless Skia) — date and month step medians and managed heap after 120 steps, with and without weather. Synthetic data; no NWS request. |
| `all <scratchDir>` | Everything above with defaults. |

Use a scratch folder outside the repository and outside any cloud-sync folder (the tool refuses OneDrive,
Dropbox and iCloud paths), for example `/tmp/carowinds-perf` or `%TEMP%\carowinds-perf`.

## Mac and Windows

- **macOS / Linux**: as above.
- **Windows x64** (on the disposable `CarowindsTest` account, with the .NET 10 SDK installed): the same commands
  from a Developer PowerShell in the repository folder, with a scratch path such as `$env:TEMP\carowinds-perf`.
  Close other heavy applications, plug the laptop in, and run each benchmark twice; report the second run.
- Record: machine model, OS build, power mode, display scaling, and the full console output.
