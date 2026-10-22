# Phase 29A Windows / Teams test kit

Materials for the manual Windows 11 + Microsoft Teams validation of the Phase 29 Morning Message
composer and formatted copy. Full record and results: `docs/phase29a-windows-teams-validation.md`.

| Path | What |
| --- | --- |
| `PHASE29A-TEST-CHECKLIST.txt` | The tester's step-by-step checklist (plain text, Notepad-friendly) |
| `synthetic-input/` | Fake message text to paste into the composer, and a synthetic DAP image (`08-synthetic-dap-image.png`, coloured blocks, no text) |
| `expected/` | What the composer produces for that input with synthetic official prices: plain text (CRLF), HTML fragment, CF_HTML payload (`*.cfhtml.bin`), preview notices; `verified` and `stale` variants. Byte-exact (`.gitattributes`: `-text`) |
| `scripts/Inspect-TeamsClipboard.ps1` | Reads the **real** Windows clipboard (format list, CF_UNICODETEXT, raw `HTML Format` bytes) and checks the CF_HTML header/offsets/markers, strict UTF-8, no BOM, allowed markup, safe links, same words in both formats, section order, Daily Feature last, OD Notes absent. Read-only. `-FromHtmlFile`/`-FromTextFile` re-check saved captures |
| `scripts/Hold-Clipboard.ps1` | Holds the clipboard open for N seconds — the controlled clipboard-failure test |
| `scripts/*.cmd` | Double-click launchers for the two scripts (`-ExecutionPolicy Bypass` for that process only; no admin) |
| `generator/` | Console project (outside `CarowindsPassdown.slnx`) that renders `synthetic-input/` through the real `MorningMessageComposer` and rewrites `expected/`, failing if any structural check fails |

The scripts are ASCII-only on purpose: Windows PowerShell 5.1 reads a `.ps1` without a BOM as the ANSI code
page.

## Rebuilding the expected outputs

```bash
dotnet run --project tools/phase29a-windows-kit/generator
```

Prints one PASS/FAIL line per check and `ALL CHECKS PASSED`. The prices in `expected/` are synthetic
(`SYNTHETIC …`, $11.11 / $22.22 / $33.33) and exist only in this kit; on Windows the tester's message
carries the real official values from Check Online, so compare structure, not amounts.

## Checking the inspector on a non-Windows host

```bash
pwsh -NoProfile -File tools/phase29a-windows-kit/scripts/Inspect-TeamsClipboard.ps1 -Kind MorningMessage \
  -FromHtmlFile tools/phase29a-windows-kit/expected/expected-morning-message-stale.cfhtml.bin \
  -FromTextFile tools/phase29a-windows-kit/expected/expected-morning-message-stale.txt \
  -OutputDirectory /tmp/phase29a-inspect
```

## Packaging the kit for the VM

The kit is copied next to the LOCAL-ONLY test ZIP under `artifacts/local-test/phase29a/` (gitignored); see
the validation document's "Test build" section for the exact build and hash.
