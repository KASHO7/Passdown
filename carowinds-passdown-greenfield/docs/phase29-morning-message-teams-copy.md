# Phase 29 — Morning Message Builder & Rich Teams Copy

> **RETIRED (2026-10-23).** The Morning Message Builder, its formatted copy and Morning Message sync were removed by a targeted rollback; see `docs/morning-message-rollback.md`. This document is kept unchanged as a historical record. Nothing it describes exists in the current application.

## Baseline

| Item | Value |
| --- | --- |
| Branch | `feature/passdown-redesign` |
| Starting commit | `780ceac` (RC.9 legacy Season Pass price-history fold fix) |
| Version in `Directory.Build.props` | `0.1.0-rc.9` (unchanged; RC.10 has **not** been packaged or frozen) |
| Database schema | 17 (unchanged) |
| Draft / Snapshot / Sync schema | 4 / 2 / 1 (unchanged) |
| Deterministic baseline before this phase | 4736 tests |

No release candidate was packaged in this phase, and RC.9 artifacts were not touched.

## Existing behavior (kept)

Associate → **Copy ▾** still has its three Phase 18F actions, first and unchanged:

| Action | Copies |
| --- | --- |
| Operations Summary | park hours, effective gate times, attendance, pre-solds, ERT |
| Today's Pricing | the PDF-selected official prices |
| Operations + Pricing | both, a blank line apart |

Their plain text is byte-identical to Phase 18F (the existing `AssociateTeamsTextFormatterTests` and
`AssociateViewModelCopyForTeamsTests` pass without change to their expectations). Daily Feature, Operational
Notes and reminders are still never part of these three actions, and the PDF, finalized PDFs and History are
untouched by this phase.

## What's new

Copy ▾ now ends with two more items:

- **Morning Message…** — opens the composer (an overlay like Season Price History, not a new navigation
  section).
- **OD Notes** — copies the open date's OD Notes on their own, in the default copy mode. If none were
  entered it says so and copies nothing.

### Composer

Left side: greeting, section order and visibility, and the message-only inputs (DAP / Placement, reminders,
ticket-price choices and note, tentative break plan, OD Notes). Right side: the message exactly as Copy Plain
Text will paste it, with the section order summarised above it. Each section row shows its warnings
(missing attendance, hours not checked, stale prices, formulas in the Daily Feature) or why it is left out.

Bottom: **Copy Formatted**, **Copy Plain Text**, and **Use formatted copy by default**. The default mode's
button is the accent button.

### Default order

1. Greeting
2. Today's Operations
3. DAP / Placement
4. Reminders
5. Ticket Prices
6. Tentative Break Plan
7. Daily Feature (last)

OD Notes are not a section of the main message and cannot be added to it.

### Section content

| Section | Source | Notes |
| --- | --- | --- |
| Greeting | saved preference | Defaults to "Good Morning!". Remembered, so it is not retyped daily. Multi-line allowed. No names are added and nobody is tagged. |
| Today's Operations | Associate draft (on-screen edits included) via the PDF's document model | Park hours, Avenue/North Gate, Carolina Harbor hours when the official source published them, projected attendance, pre-solds, ERT. Values never entered are left out and reported in the preview. Always titled "Today's Operations:" — the operating date is never appended (Phase 29B). |
| DAP / Placement | composer text | An optional "attach the image" reminder; a chosen local image can be copied separately (**Copy Image**). Images are never put into the text. |
| Reminders | composer text | Line breaks kept; a blank line starts a new paragraph; lines starting with `- `, `* ` or `• ` become bullets; or "Show every line as a bullet". **Pick a passdown note… → Add to Reminders** inserts one of the date's notes only when chosen. **Reuse last reminders** copies the most recent earlier date's reminders only when pressed. |
| Ticket Prices | official prices via the PDF's document model | The message has its own price-section choice (default Daily Admission, Fast Lane, Promotions; Season Passes, Add-Ons and SCarowinds opt-in; Memberships never). Per-product include/exclude and price overrides follow the Products tab exactly. Custom/internal items are excluded. The choice never changes the PDF. |
| Tentative Break Plan | composer text | Spacing and order kept (formatted copy uses non-breaking spaces so columns survive). |
| Daily Feature | Associate draft | Same text and type label as the passdown. Formulas are copied as typed (`\( … \)`) with a preview notice. |
| OD Notes | composer text | Separate message, "OD Notes:" heading. |

### Price freshness

A price whose latest official check is not **Verified** (stale, unavailable, failed, not published, not
checked yet) is still shown as the last-known-good official price, and:

- the preview lists each such product with its state and last verified time (Eastern), and
- the message adds an italic line: *Some prices could not be re-verified today; they are the last confirmed
  official prices.*

When no official prices are available for the selected sections, the section is left out and the preview
says why. Prices are never invented. The user's own price note is printed after the official prices, in
italics, and is never presented as an official price.

## Editing behavior

Everything typed in the composer is **message-only**: it never changes the Associate draft, the PDF, History,
official cached values or Personal Sync. Opening the composer and copying never checks online, never saves the
draft and never changes the selected date. Passdown values (hours, attendance, prices, Daily Feature) are
read-only in the composer — the header says to edit them on the Associate screen.

## Preferences and storage

| What | Where | Synced? |
| --- | --- | --- |
| Greeting, section order, enabled sections, default copy mode, price sections, reminders-as-bullets | `%LOCALAPPDATA%\Carowinds Passdown\morning-message.json` | No |
| Per-date message text (reminders, break plan, DAP text/image path/reminder, OD Notes, price note) | `%LOCALAPPDATA%\Carowinds Passdown\morning-message-content.json`, most recent 60 dates | No |

Both use the same atomic writer and flat-file convention as the appearance and PDF-template preferences.
Enums are stored by name. A missing, unreadable or malformed file falls back to defaults (or an empty message)
without touching anything else.

**Why not SQLite / Personal Sync:** this is device-level presentation preference and composer scratch text,
deliberately kept outside the draft, the PDF/History record and the sync schema. Keeping it in local files
needs no database migration and no sync schema change, and it never lands in the Personal Sync folder. If the
user later wants break plans or OD Notes on their other computers, that would be a deliberate sync-schema
addition, not part of this phase.

Saves are debounced (about half a second) and are flushed on copy, close and before reopening.

## Clipboard implementation

### One message model

`TeamsMessage` (Documents/Teams) → sections (key, title, blocks, preview notices) → blocks (paragraph or bullet
list) → lines → runs (text, bold, italic, optional link). Both outputs are rendered from it:

- `TeamsPlainTextRenderer` — CRLF, blank line between blocks and sections, `• ` bullets, no Markdown.
- `TeamsHtmlRenderer` — `p`, `br`, `b`, `i`, `ul`/`li`, `a href` only. Since Phase 29B bold and italic are
  `<b style="font-weight:bold">` / `<i style="font-style:italic">` (the only style attributes), not
  `strong`/`em` — see `docs/phase29b-morning-message-heading-rich-formatting.md`.

The three existing actions use the same model (`AssociateTeamsMessageBuilder`), so they gained a formatted mode
without a second string builder. Tests compare the word sequence of the plain text with the text content of
the HTML for every action and for the morning message.

### Formatted copy (Windows)

> **Phase 29C:** on Windows, formatted copy no longer goes through Avalonia. `WindowsNativeClipboard`
> (Infrastructure) writes "HTML Format" and CF_UNICODETEXT with `OpenClipboard`/`SetClipboardData`, because
> Avalonia's OLE data object refused standard OLE requests for the HTML (Word could not see it). See
> `docs/phase29c-windows-clipboard-interop.md`. The Avalonia description below now applies to macOS/Linux
> development builds only.

`AvaloniaClipboardService.SetTextAndHtmlAsync` uses Avalonia 12.1.1's `DataTransfer` API: one
`DataTransferItem` with

- `DataFormat.CreateBytesPlatformFormat("HTML Format")` = the CF_HTML payload (set first since Phase 29B, so
  it is advertised before the text), and
- `DataFormat.Text` (CF_UNICODETEXT) = the plain text.

A **bytes** format is required. Avalonia's Win32 backend (`OleDataObjectHelper.WriteStringToHGlobal`, checked
in the 12.1.1 source) writes a string platform format as UTF-16, but CF_HTML must be UTF-8 with byte offsets.

`CfHtml.Build` writes the documented header (`Version:0.9`, `StartHTML`, `EndHTML`, `StartFragment`,
`EndFragment`, ten zero-padded digits each), then
`<html><body><!--StartFragment-->…<!--EndFragment--></body></html>`. Offsets are byte offsets into the UTF-8
payload; there is no BOM. Since Phase 29C the native Windows writer adds one NUL byte after EndHTML in the
clipboard block (offsets unchanged). `CfHtml.Parse` validates a payload (used by the tests).

macOS/Linux (development only) get `public.html` / `text/html` as plain UTF-8 HTML.

References: [Avalonia clipboard](https://docs.avaloniaui.net/docs/services/clipboard),
[HTML Clipboard Format](https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format),
[Format a message in Teams](https://support.microsoft.com/en-us/teams/chat/format-a-message-in-microsoft-teams).

### Plain-text fallback

**Copy Plain Text** puts only CF_UNICODETEXT on the clipboard, exactly as Phase 18F did. If a formatted copy
throws, the same plain text is copied instead and the status line says "…as plain text (formatted copy wasn't
available)". If that also fails: "Couldn't copy to the clipboard. Try again." in the error color. Nothing
throws to the user and the draft is never touched.

### Default copy mode

The default is **Plain Text** on a fresh install, because formatted pasting has not yet been validated in
Teams on Windows. Turning on "Use formatted copy by default" makes Copy Formatted the accent button and makes
the menu's Operations Summary, Today's Pricing, Operations + Pricing and OD Notes also put HTML alongside their
unchanged plain text. Once the Windows checklist below passes, consider making Formatted the default.

### Security

- Every string — user text, product names, notes — is HTML-encoded (`& < > " '`). Control characters are
  dropped. Nothing typed can become markup.
- Only the seven elements above are produced. No scripts, styles, classes, fonts, colors, tables, images or
  external resources.
- Links: only absolute `http`/`https` with a host and no user name or password. `javascript:`, `data:`,
  `file:`, `mailto:`, `ftp:` and credentialed URLs render as inert text. Addresses typed in user text are
  linked only when they pass this check.
- `@Name` stays ordinary text. Pasting never creates a Teams mention or notifies anyone, and the composer
  says so.
- Copy Image reads only a local file the user chose; nothing is downloaded or attached automatically.

## Teams limitations

- Teams decides what it keeps from pasted HTML. Bold, bullets and paragraph breaks are expected to survive,
  but this is **not verified** yet. If Teams drops styling, the words and order are unchanged, and Copy Plain
  Text stays available.
- Copying the message never attaches the DAP image, GIFs or video. Attach media in Teams by hand.
- Mentions must be made in Teams after pasting.
- Non-breaking spaces keep break-plan columns roughly aligned, but Teams uses a proportional font, so columns
  may not line up exactly.

## Windows test checklist (NOT EXECUTED)

Use a test chat or an unsent draft only. Never send operational messages to staff while testing.

| # | Check | Teams desktop (Win 11) | Teams web (Edge) |
| --- | --- | --- | --- |
| 1 | Copy Formatted → paste: bold headings and labels | | |
| 2 | Bullets from reminders are a real list | | |
| 3 | Paragraph spacing between sections | | |
| 4 | Copy Plain Text → paste: same words, same order, no HTML | | |
| 5 | Special characters `& < > " '` appear literally | | |
| 6 | Unicode (é, —, emoji, CJK) intact | | |
| 7 | Large message (long reminders and break plan) pastes completely | | |
| 8 | Empty optional sections leave no orphan headings | | |
| 9 | Several copies in a row: each paste matches the latest preview | | |
| 10 | After Move Up / Move Down, the paste follows the new order | | |
| 11 | Message ends with the Daily Feature | | |
| 12 | OD Notes are absent from the main message | | |
| 13 | Copy ▾ → OD Notes pastes into a separate composer | | |
| 14 | Existing Operations Summary / Today's Pricing / Operations + Pricing still paste correctly in both modes | | |
| 15 | Copy Image pastes the chosen DAP image | | |
| 16 | Notepad paste after Copy Formatted gives plain text | | |
| 17 | Word/Outlook paste after Copy Formatted keeps bold and bullets (CF_HTML sanity check) | | |
| 18 | Clipboard viewer (e.g. NirSoft InsideClipboard) shows `HTML Format` with valid offsets and `CF_UNICODETEXT` | | |

### Synthetic workflow

1. Open a date with park hours, attendance and pre-solds entered, a Daily Feature, and official prices.
2. Morning Message…: greeting "Gooooood Morning!", two reminders, a price note, a three-line break plan, OD
   Notes "Synthetic OD note".
3. Copy Formatted → paste into the test chat → confirm the order and that it ends with the Daily Feature, with
   no OD Notes.
4. Attach a DAP image by hand (or Copy Image → paste).
5. Copy ▾ → OD Notes → paste into a new message in the test chat.
6. Confirm the three original Copy actions still work.

## Result reporting

| Area | Status |
| --- | --- |
| Automated tests (message content, order, HTML encoding, CF_HTML offsets, link safety, fallback, persistence, keyboard/automation markup, light/dark tokens) | **Verified** (deterministic suite) |
| Rendered composer, light and dark (headless Skia, `tools/visual-fixtures` scenario `Associate-MorningMessage`) | **Verified** as screenshots for review; not Windows DPI validation |
| Windows clipboard (real `HTML Format` on Windows) | **Not executed** |
| Teams desktop | **Not executed** |
| Teams web (Edge) | **Not executed** |

## Tests

Deterministic suite (Release, `Category!=LiveSmoke`, the five real-Window UI classes run alone): **4838 passed,
0 failed** (baseline 4736; +102).

| Project | Count | New in Phase 29 |
| --- | --- | --- |
| Domain | 303 | — |
| Application | 1294 | +8 `MorningMessageLayoutTests` |
| Infrastructure | 1089 | +13 `FileMorningMessageStoresTests` |
| Documents | 525 | +56 `TeamsRenderingTests`, `CfHtmlTests`, `MorningMessageComposerTests` |
| Ui (mixed) | 1475 | +25 `MorningMessageViewModelTests`; the Copy-menu test now asserts the original three items unchanged plus the two new ones |
| Ui run-alone classes | 26 + 113 + 2 + 7 + 4 | — |

Release build: 0 warnings, 0 errors.

## Remaining limitations

1. Formatted pasting into Teams is unverified (see above). Plain text is therefore the default.
2. The Associate draft's Shows are not part of the morning message.
3. Message text is per computer; it does not follow the user to their other computers through Personal Sync. *(Phase 30 adds this as an option, off by default: `docs/phase30-optional-morning-message-sync.md`.)*
4. Copy Image supports PNG, JPEG, BMP and GIF files (a GIF is copied as a still image).
5. ~~The rendered preview is plain text.~~ Since Phase 29B the preview is styled for the selected copy mode
   (bold headings and labels, italic notes when formatted copy is the default).

## Version / schema impact

DB 17, Draft 4, Snapshot 2, SyncSchemaVersion 1 — all unchanged. No migration. Two new local JSON files (above).
The version stays `0.1.0-rc.9` in source; the next packaged candidate (RC.10 or later) needs its own release
preparation.
