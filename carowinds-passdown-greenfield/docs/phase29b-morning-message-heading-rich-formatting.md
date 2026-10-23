# Phase 29B — Morning Message heading and rich formatting fix

Focused correction to the Phase 29 Morning Message composer (`docs/phase29-morning-message-teams-copy.md`).
Baseline `feature/passdown-redesign` at `1a04251`. Version stays `0.1.0-rc.9`. No release packaged, nothing
pushed. RC.8 and RC.9 artifacts untouched.

**Windows status: NOT VALIDATED.** Everything below marked Mac-auto ran on the macOS build host. Word, Teams
desktop and Teams in Edge rows stay **NOT EXECUTED** until run with `tools/phase29b-windows-kit/PHASE29B-CHECKLIST.txt`.

## 1. Heading

The Operations section was titled "Operations for Thursday, October 22:" whenever the open date was not
today (Eastern). It is now always **`Today's Operations:`** (`MorningMessageComposer.OperationsTitle`), in
the preview, the plain-text copy, the formatted copy and every regeneration. The section's name in the
section list was already "Today's Operations". `Compose`'s `easternToday` parameter is kept for existing
callers and no longer affects output. The PDF, History and the three original Copy options never used this
heading and are unchanged.

## 2. Why formatted copies showed no bold

Windows 11 findings (product owner): CF_HTML present, offsets valid, valid UTF-8, 9 `strong` and 3 `li`;
Word kept the bullets but showed no bold headings; Teams desktop showed no bold; plain and formatted pastes
looked virtually identical.

What was inspected on the build host:

| Item | Finding |
| --- | --- |
| Generated fragment | Structurally correct. Headings and labels were bare `<strong>`, italics bare `<em>`, no styling of any kind |
| Avalonia 12.1.1 `Avalonia.Win32` (decompiled: `DataTransferToOleDataObjectWrapper`, `OleDataObjectHelper`, `ClipboardFormatRegistry`) | Bytes formats are copied into the HGLOBAL unchanged (`WriteBytesToHGlobal`, `GHND` zero-filled) — consistent with the valid offsets seen on Windows. **`IEnumFORMATETC` lists formats in the order they were set on the `DataTransferItem`** (`CalcFormatIds` follows `Formats`, which is insertion order), and the app set `CF_UNICODETEXT` *before* `HTML Format` |
| Win32 clipboard convention | Formats are expected richest-first; a reader that takes the first format it understands takes the plain text |
| Word | Imports `<strong>` as its "Strong" character style, not direct bold; direct formatting (`<b>`, `font-weight`) is what Word, Outlook and browsers themselves put on the clipboard |

Two defects, both plausible causes of the observed result and both cheap and safe to remove:

1. **Format order** — the HTML was advertised second, after plain text.
2. **Unstyled semantic emphasis** — the only emphasis signal was `strong`/`em`, which Word maps to a
   character style and which editors that rebuild pasted content from explicit formatting may not treat as
   bold. Bullets (`ul`/`li`) are structural and survive either way, matching what was seen.

Which one actually stopped the bold in each client cannot be proven from macOS. Checklist rows C1–C2 and
D2–D3 (Paste Options state, Paste Special → HTML Format) are there to tell them apart on Windows.

## 3. Correction

- `AvaloniaClipboardService.CreateFormattedData`: `HTML Format` is set first, `CF_UNICODETEXT` second. Same
  bytes, same plain-text fallback, same failure handling.
- `TeamsHtmlRenderer`: bold is `<b style="font-weight:bold">…</b>`, italic `<i style="font-style:italic">…</i>`.
  These two fixed attribute values are the only `style` attributes ever written; no style sheets, classes,
  fonts or colors. Element set is now `p, br, b, i, ul, li, a`. Every user string is still HTML-encoded, so a
  user can never add markup or styling; `javascript:` and other unsafe links stay inert; `@Name` stays text;
  no Teams mention markup.
- CF_HTML envelope (`CfHtml`) unchanged.
- The plain text is byte-identical to before (kit `expected-*.txt` unchanged after regeneration; the
  17 exact-string `AssociateTeamsTextFormatterTests` and the composer's exact plain-text test unchanged).
  The formatted HTML of the three original Copy options changes the same way, since they share the renderer.

## 4. Preview

The preview was a plain `SelectableTextBlock`. It now binds `Inlines` to `MorningMessageViewModel.PreviewSpans`
through `TeamsPreviewInlinesConverter`:

- `TeamsPreviewRenderer` (Documents, pure) renders the message as styled spans in the plain-text layout
  (blank line between sections and blocks, `• ` bullets). Their text is exactly the plain copy with `\n` for
  CRLF; the run text comes from the plain renderer itself.
- "Use formatted copy by default" on: headings, `Label:` names and price categories bold; price note and
  stale-price line italic. Off: unstyled, as plain text pastes.
- Local text runs only — no HTML parsing, no web view, nothing remote. Colors are inherited from the theme
  tokens; checked in Light and Dark with `tools/visual-fixtures` (`Associate-MorningMessage`,
  new `Associate-MorningMessageFormatted`).
- Section selection, order, content and persistence untouched.

## 5. Not changed

Phase 30 sync (toggle, shared-folder format, reconciliation), DB 17 / Draft 4 / Snapshot 2 / Sync 1, OD Notes
separation, retention, PDF and History rendering, app version, updater configuration.

## 6. Tests (Mac-auto)

New/changed:

- Documents `MorningMessageComposerTests`: `OperationsTitle_IsAlwaysTodaysOperations_NeverDated` (replaces the
  dated-title test; 4 dates), `Phase29B_*` — exact formatted HTML of the full message, exactly one
  `Today's Operations:` in plain/HTML/regenerated output, plain/rich same words and every title bold,
  reminder bullets in plain/HTML/preview, user text escaped, OD Notes excluded from plain/HTML/preview, CF_HTML
  round trip with Unicode, preview spans (plain = plain copy; formatted adds only styling).
- Documents `TeamsRenderingTests`: element set `p, br, b, i, ul, li, a`; only the two fixed style values.
- Ui `MorningMessageViewModelTests`: `Phase29B_*` — preview spans follow the copy mode, never include OD
  Notes, non-today date still `Today's Operations:` in preview and both copies, converter runs/line breaks and
  theme-inherited color, view binds styled inlines, formatted clipboard data lists HTML before text.
- Windows kit: generator checks updated (element set, fixed styles, undated heading) and `expected/` regenerated
  (fragment and CF_HTML files changed; `.txt` files byte-identical). `Inspect-TeamsClipboard.ps1` gained
  "HTML Format listed before CF_UNICODETEXT", "Only fixed bold/italic style attributes", "Section headings are
  explicitly bold"; run under PowerShell 7 against the regenerated files: all PASS.

Deterministic suite (Release, `Category!=LiveSmoke`, five real-Window classes alone): 303 Domain + 1330 Application + 1136 Infrastructure + 538 Documents + 1511 Ui mixed + 26 + 113 + 2 + 7 + 4 = **4970 passed, 0 failed** (baseline 4951; +13 Documents, +6 Ui). Clean Release build (`dotnet build CarowindsPassdown.slnx -c Release --no-incremental`): **0 warnings, 0 errors**.

## 7. Windows verification

`tools/phase29b-windows-kit/PHASE29B-CHECKLIST.txt` (Word, Teams desktop, Teams in Edge, preview in Light/Dark,
clipboard inspector, original Copy options). All rows **NOT EXECUTED**.
