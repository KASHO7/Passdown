# Phase 35D — Shows, Passdown Notes and shared UI polish

Status: **implemented locally — awaiting the product owner's Windows review.** Not pushed, tagged or published.
Phase 35E has not been started.

| | |
|---|---|
| Starting commit | `bfbdd18` (Phase 35C-2). Phases 35A, 35B and 35C were Windows-tested by the product owner and confirmed working before this phase started. |
| Implementation commits | `65c230a` 35D-1 navy title bar · `d7d6848` 35D-2 Shows, Notes, dropdown chevrons, tests, harness |
| Record commit | 35D-3 (this document, the Windows checklist and the plan update) |
| Database schema | 18 (unchanged; no migration) |
| Personal Sync format | unchanged |
| Draft serialization / autosave | unchanged (daily notes still one string per non-empty line; selections still the existing frozen copies) |
| PDF composition / engine / printing | unchanged (QuestPDF + PDFium preview, same Print / Save PDF path) |
| Local test version | `0.1.0-rc.14-LOCALTEST.phase35d` (source `<Version>` still `0.1.0-rc.13`) |

## 1. References used

From `~/Downloads/carowinds-phase35-approved-design-references/` (all present):

| Screen | Files |
|---|---|
| Shows | `carowinds_shows_refined_prototype.html`, `carowinds_shows_preview.png`, `carowinds_shows_expanded_preview.png`, `carowinds_shows_compact_preview.png` |
| Passdown Notes | `carowinds_notes_refined_prototype.html`, `carowinds_notes_preview.png`, `carowinds_notes_included_preview.png`, `carowinds_notes_compact_preview.png` |
| Title bar | `carowinds_settings_compact_preview.png` (navy top bar with icon, white title and Windows caption buttons) |
| Icon | `ChatGPT Image Aug 27, 2026, 02_00_21 PM.ico` — `src/Carowinds.Passdown.Desktop/Assets/logo.ico` is byte-identical (SHA-256 `e866bc82…61bdb4f`) and was not touched |

**Not available:** the product owner's Windows screenshot of the current light title bar / Actions button was described
in the brief but not attached. The fix is based on the description and the code, not on that image.

Prototype content (sample shows, times, notes, "SyncTest" note, fake save states, illustrative PDF) was **not** copied
into the product. All data on these pages still comes from the official schedule, the draft and Operational Notes.

## 2. Shows & Entertainment — layout mapping

| Prototype | Production | Note |
|---|---|---|
| Title "Shows & Entertainment" + date · "Official schedule checked …" | Same title with the existing **Official schedule** tag; second line = selected date · `ShowsFreshnessText` (incl. "latest check failed" when older data is kept) | Freshness comes from the real schedule; absent when nothing was checked |
| "4 of 4 shows selected" · Select all · Clear | `ShowsSelectionSummary` counted from the rows; the existing Select all / Clear commands and accessible names | Select all still never selects a show the schedule no longer lists |
| — | "Selected shows print on the Passdown." / "Shows is not included … Turn it on under Overview ▸ Include in Passdown." | Phase 31D rule kept: Overview decides whether Shows prints |
| — | Status note when there are no rows (not published, park closed, couldn't check, not checked) | The existing distinct source states; never "no shows" unless the schedule says so |
| Columns: Show and location · Scheduled performances · Details | Same three columns on a tinted header band; hairline rows | |
| Checkbox | Existing `IsSelected` checkbox, accessible name "Include <show> on the passdown, <times>" | |
| Venue line | Venue, or "Location not listed" when the feed gives none | Never a guessed location |
| "11:15 AM · 11:30 AM · 11:45 AM · +13 more" | First **three** times + "· +N more" for a schedule of more than five times; every time for five or fewer | Collapse threshold is the shared Dashboard rule (`ShowTimesDisclosure.CanCollapse`), the 3-time preview matches the reference |
| "View times" on every row | **View times / Less** only on rows that collapse | Intentional difference: on a short row everything is already visible, so the control would add nothing |
| Expanded "All 16 scheduled performances" tiles | Same heading and one tile per time (wrapping grid) | No time is ever dropped |
| Footer note + tip | One note: selected shows print when Shows is included; times and locations come from the official schedule; a scheduled time is not a confirmation that a show is running | |
| Row state | Existing state notes (times changed, no longer listed, not on current schedule, saved copy) under the row | Unchanged |

Expansion state is presentation-only. It lives on the row and is remembered by **official show id** for the date on
screen (so adding a persistent note or Check Online, which rebuild the rows, do not close an open list); it is dropped
when the date changes. Expanding never changes selection, never schedules an autosave, never loads or checks online and
never regenerates the preview (tested).

No manual show entry exists in the product, so "manually entered" has no Shows state to show.

## 3. Passdown Notes — layout mapping

| Prototype | Production | Note |
|---|---|---|
| "Passdown Notes" + subtitle; "4 daily notes · 1 persistent included" | Same; `NotesSummaryText` from the live text (non-empty lines) and the real number of included notes | |
| Daily notes editor, "Saved locally · One line per PDF bullet" | Same `OperationalNotesText` TextBox (spell check, placeholder, autosave, name "Operational notes"), 215 px minimum, 440 px maximum then scrolls inside, 22 px line height; "Saved automatically · One line per printed note" and the existing **Entered here** tag | Plain text only — no rich text, Markdown or bullet characters added |
| "Each non-empty line becomes one bullet …" · "4 lines" | Same sentence; live "N notes" count using the draft's exact `ParseLines` rule | The count can never disagree with the PDF |
| Persistent operational notes · Manage notes | Same heading; **Manage notes** opens Operational Notes (section switch only — date, draft and selections untouched) | New navigation, same as the Dashboard's "View Notes" |
| "They are never added to a Passdown automatically." | Same | |
| One card per note with a checkbox | Two groups: **Included in this Passdown (N)** (frozen copies, checked) and **Available for <date>** (unchecked); hairline rows, title, "Category · Priority priority · <date relevance>", body (included copies), date range, Important chip | Checkbox = the existing Add / Remove commands |
| Persistent-note detail text | The included copy's body, the "Included notes keep the version selected for this passdown." hint, and the existing source-changed line with **Replace with Current Version** | Unchanged Phase 11E behaviour |
| Independence tip | "Unchecking a note removes it from this date's Passdown only; it never resolves, edits or deletes the original note." | |

The checkbox two-way-binds a presentation flag and runs the existing command. If the command could not make the change
(for example the Phase 23A rule holds a note change back while an autosave is failing), the box returns to its real
state, so it never claims an inclusion that did not happen. Storage and lifecycle are unchanged: selecting stores a
frozen copy in this date's draft; deselecting removes that copy; the Operational Note itself is never written.

## 4. Dropdown chevron alignment — root cause and fix

**Root cause.** Both dropdown chevrons (Passdown **Actions** and the date button) drew `M 2,5 L 8,11 L 14,5` — a 12×6
shape — into a 12×12 `Path` with `Stretch="Uniform"`. Uniform stretch scales the geometry's bounds to fit and places
it at the **top-left** of the box, so the visible chevron occupied the top half of a vertically centred box and sat
roughly 3 px (at 100 %) above the label's centre — more at 125 %/150 %. The Products category disclosure chevrons had
the same defect (a 2:1 shape stretched into an 11×11 box; the right-pointing one was pinned left).

**Fix (shared, no one-off margins).** `PassdownDropdownChevronGeometry` and `PassdownDisclosureChevronRightGeometry`
are open strokes drawn with fixed coordinates inside a 12×12 box and centred on (6,6); the shared `Path.dropdownChevron`
style renders them with `Stretch="None"`, 1.5 px round strokes, centred vertically, not hit-testable, coloured with the
secondary text token (Light and Dark). `StackPanel.dropdownLabel` gives every menu trigger the same 7 px label-to-arrow
gap and centring. The Actions and date buttons use it; `Path.categoryChevron` uses the same geometries with
`Stretch="None"`. Commands, menu entries (Print, Save PDF, the three Copy for Teams options, Show/Hide Preview, the three
PDF styles, Check Online, Season Price History) and Ctrl+P are unchanged (tested). Fluent ComboBoxes (Daily Feature
type, Pass Perks month, Operational Notes filters, Price History) use Fluent's own centred `PathIcon` and were not
affected. Hover, pressed, focused and disabled states change only the button background/foreground, never the glyph's
box, so alignment holds in every state.

Real-window test: the chevron box is 12×12, its drawn bounds are centred on (6,6), its centre is within 0.75 px of the
label's centre, and it sits inside its button to the right of the label — Light and Dark.

## 5. Navy title bar — implementation and trade-offs

**Approach.** The window keeps the standard Windows title bar; only its colours change, through documented DWM window
attributes set on the main window's HWND (`Infrastructure/Windowing/WindowsCaptionStyler`, behind the Application
interface `IWindowCaptionStyler`):

1. `DWMWA_CAPTION_COLOR` (35) = `#1B3553` (new token `PassdownTitleBarColor`, a shade of the 35A brand navy taken from
   the approved reference's top bar);
2. `DWMWA_USE_IMMERSIVE_DARK_MODE` (20) = on, so minimize / maximize / close glyphs are light on the navy caption in
   both app themes;
3. `DWMWA_TEXT_COLOR` (36) = white (`PassdownTitleBarTextColor`; contrast 13.3 : 1).

`MainWindow` applies it when the window opens and again (posted) after a theme change, because Avalonia re-sets the
frame's dark-mode flag when the theme changes. App logs each distinct result once ("Title bar colour: Applied").

**What stays native:** dragging, double-click maximize/restore, minimize, maximize/restore, close, edge resizing, Snap
layouts, maximized bounds, per-monitor DPI, system menu (Alt+Space), keyboard behaviour. There is no extended client
area, no custom chrome, no hit-testing code and no second title bar inside the window. The icon (`logo.ico`, the
original) and the title "Carowinds Operations" are unchanged; no subtitle or role label.

**Trade-offs and limits (documented, not hidden):**

- The caption-colour attributes exist from **Windows 11 (build 22000)**. On Windows 10 the first call is rejected and
  nothing at all is changed — the system title bar stays.
- Under a **Windows high-contrast theme** the colours are never applied (the user's high-contrast caption wins).
- The caption keeps the same navy when the window is **inactive** (Windows does not dim a custom caption colour).
- Title-bar **height** and caption-button **hover/pressed** colours are Windows' own (about 31–32 px at 100 %, close
  button red on hover) — the approved mockup's 34 px bar is approximated, not pixel-matched. Matching it exactly would
  need custom chrome, which the brief forbids at the cost of native behaviour.
- Windows-only; on the macOS development host it reports "not supported on this platform" and changes nothing.
  **Flagged for Windows VM testing** (checklist section A).

## 6. Functional preservation

Unchanged and covered by the existing suites plus 35D tests: show selection rules, initial defaults (new shows never
pre-selected), Select all / Clear, date-specific schedules and selections, autosave and recovery, daily-note
serialization, persistent-note select / deselect / replace and the Phase 23A held-back rule, date-specific note
selection, Include in Passdown, the preview pipeline (no new debounce, no animation delay), Print / Save PDF / Copy
for Teams, Check Online, Accesso coalescing, price overrides, History immutability, schema 18, sync format.

Animations: none added. Expanding a schedule is an immediate layout change (no transition, no timer), so it behaves
the same with Interface animations On, Off or Windows reduced motion. The motion contract tests still pass.

## 7. Accessibility

- Every show checkbox: "Include <show> on the passdown, <all times>". The times column's name lists every time, so the
  shortened preview is never an unexplained fragment. View times / Less: "Show all 16 performance times for <show>" /
  "Show fewer performance times for <show>" — a Tab stop, Enter/Space toggle (tested).
- The selection count and the daily-note count are polite live regions.
- Persistent-note checkboxes: "Include <title> in this passdown", with help text saying what unchecking/checking does
  and that the original note is not changed.
- Daily editor keeps its name, help text, spell check and keyboard behaviour; text and the editor instance survive
  section switches (tested).
- Shape and text, never colour alone: tags and counts are text; included/available are separate labelled groups.

## 8. Visual comparison (actual Avalonia renders)

`tools/visual-fixtures` `P35D-*` scenarios (synthetic data; real Avalonia + Skia renders of the real views, not HTML):
Shows default / expanded / sidebar collapsed / preview hidden; Notes default / persistent note included / sidebar
collapsed / preview hidden / long notes with blank lines; Actions dropdown open — at 1920×1080, 1536×960, 1280×720 and
960×640 in Light and Dark: **80 captures, no horizontal overflow and no button past the window edge**. The renders are
development evidence, not Windows DPI evidence, and they show the client area only (no title bar). The title bar can only
be seen on Windows (checklist A1–A3).

Intentional differences from the references:

- Production keeps the 35A sidebar and 35B toolbar/tabs/preview (the references' shell is illustrative); the real PDF
  preview replaces the illustrative page.
- Shows: the **Official schedule** tag, the "prints when included" line and row state notes stay; View times appears only
  on schedules that collapse; the threshold for collapsing is the shared five-time rule.
- Notes: persistent notes are grouped (Included / Available) rather than shown as one card per note, because the product
  has frozen included copies and a separate available list; the per-note "Available for Nov 22" chip is the group heading.
- The Notes section tab keeps its existing count beside "Notes" (included notes + 1 when daily text exists).

## 9. Performance (macOS Arm64, headless Skia — development evidence, not Windows results)

Scratch benchmark (not committed) run with identical code against `bfbdd18` (before) and the 35D tree (after): one
Associate window at 1536×960, 12 shows (one with 16 times), 10 available persistent notes, fake rasterizer, each
sample including dispatcher pump + one rendered frame; median of 25 after 3 warm-ups.

| Measure | Before | After |
|---|---|---|
| MainWindow show + first frame (cold, single sample) | 1,531 ms | 1,258 ms |
| Open Shows from Overview | 4.2 ms (p90 4.7) | 3.5 ms (p90 3.8) |
| Open Notes from Overview | 3.6 ms (p90 4.2) | 3.4 ms (p90 3.9) |
| Section round trip (Shows→Notes→Overview→Products→Shows, 4 frames) | 13.4 ms (p90 17.8) | 15.8 ms (p90 19.2) |
| Show checkbox → preview refreshed | 1.7 ms (p90 2.5) | 2.7 ms (p90 3.3) |
| Daily note edit → preview refreshed | 3.6 ms (p90 6.1) | 2.8 ms (p90 3.0) |
| Expand + collapse a 16-time schedule (2 frames) | n/a | 6.0 ms (p90 8.8) |
| 500 navigation cycles, managed heap after full GC | 123.6 → 165.0 MB | 123.8 → 165.3 MB |
| 500 expand/collapse toggles, managed heap | n/a | 165.4 → 171.4 MB |

All changes are about a millisecond — within frame noise; no material regression. The checkbox → preview path is ~1 ms
slower because the selection count now updates with the checkbox (one extra text measure). Heap growth over the
navigation cycles is identical before and after (pre-existing, mostly retained render resources). The 6 MB over 500
expand/collapse toggles (16 tiles each, 8,000 tile creations) is small and not repeated per cycle; flagged for a Task
Manager check on Windows (checklist N2). Title-bar colouring is three DWM calls at window open and on theme change —
no per-frame cost. A Mac number says nothing about Windows performance.

## 10. Automated tests

- `Phase35DShowsNotesPolishTests` (17, mixed partition): real schedule rendering in operating-day order with every time;
  3-time preview and "+N more" vs short lists; expansion never changes selection, saves, loads or checks online; expansion
  survives a same-date rebuild and resets on another date; live selection count with Select all / Clear and autosave;
  subtitle freshness and the not-published / couldn't-check states; a selected show no longer listed keeps its note;
  daily-note count uses the draft rule and serialization is unchanged; persistent checkbox add / remove through the
  existing selection only, source note untouched; a blocked add returns the box to unchecked; notes never auto-included
  and selections date-specific; Manage notes switches section without touching date or draft; Shows / Notes markup
  contracts (one sheet, columns, live regions, plain-text editor, no animation); shared chevron geometry centred on
  (6,6) with Stretch None and no old stretched glyph left; Actions menu entries in order with Ctrl+P; native title-bar
  markup (no extended client area / custom decorations), original icon and name, navy tokens with ≥ 7 : 1 contrast.
- `Phase35DShowsNotesWindowTests` (11, partial of the run-alone `Phase33PreviewWindowTests`): Shows and Notes fit at the
  four review sizes, Light/Dark, preview shown and hidden, with non-overlapping time tiles; View times by mouse and
  Enter key, first line stays put, no selection / preview / load / save change over 30 toggles; show checkbox click
  reaches the live preview and the count; daily edit reaches the preview, survives section switches in the same editor,
  long text stays within 440 px; persistent checkbox click adds and removes; chevrons centred on their labels (Light and
  Dark); `MainWindow` requests the navy caption when shown and again after a theme change.
- `WindowsCaptionStylerTests` (10, Infrastructure): attribute order and COLORREF values, older Windows changes nothing
  else, high contrast never overridden, nothing off Windows or without a handle, missing DWM reported not thrown, hex
  parsing. `CompositionRootTests`: the styler is registered (+1).
- Superseded assertions replaced with their 35D equivalents (not deleted): `Phase22DAssociateWorkspaceTests` (show rows
  now name over venue then the times preview; required bindings `TimesPreviewText`, `MoreTimesText`, `AllTimes`,
  `TimesAccessibleName`, `ShowsSubtitleText`), `AssociateViewAccessibilityTests` (persistent notes' Add/Remove buttons
  are now inclusion checkboxes "Include {0} in this passdown" with remove/add help text).

**Deterministic total: 5,737 / 5,737**, 0 skipped (Domain 462, Application 1,378, Infrastructure 1,319, Documents 484,
Ui mixed 1,873, OperationsCalendarView 26, Phase15ERealWindowLayout 134, RealWindowStartupTiming 2,
SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 48). The 35C record reported 5,697;
measured here before the 35D tests were added, `Phase33PreviewWindowTests` already had 37 (not 36), i.e. 5,698 — so 35D
adds 39 (17 + 11 + 10 + 1). Clean
Release build: 0 warnings, 0 errors.

**Test-harness finding.** Under the headless test font, a realized `TextBox` whose text contains a blank line loops and
grows memory without bound (a bare Avalonia `TextBox` reproduces it; an early run of the new daily-notes window test
reached an 82 GB footprint before it was stopped). The real Skia renderer handles blank lines normally (the
`P35D-Notes-Long` capture includes them, ~250 MB peak), and the Windows build is not affected. Real-window tests therefore
never put a blank line into a realized TextBox; the blank-line rule is covered at view-model level.

## 11. Windows test package

| | |
|---|---|
| Source commit | `d7d6848` (clean tree) |
| ZIP | `artifacts/local-test/RC14-Phase35D-LOCALTEST/LOCAL-ONLY-RC14-Phase35D-LOCALTEST-win-x64.zip` |
| Size | 76,103,090 bytes (72.6 MB); 308 files |
| SHA-256 | `6b67cc89a87df5cad0324c68caa8f5117f3f6d2af7c14e2daf5f6ebd8a6b890b` |
| Shown as | `0.1.0-rc.14-LOCALTEST.phase35d` |
| Validation | script content validation (folder and ZIP) passed; `RealReleaseZip` passed; no database, draft, history, key or update manifest in the ZIP; all 890 earlier release and local-test files re-hashed byte-identical |

Checklist: `docs/releases/rc14-phase35d-localtest-windows-checklist.md`.

## 12. Known limitations

- Title bar: Windows 11 only; not applied under high contrast; native height and caption-button hover colours; same
  colour when inactive (§5).
- View times appears only on schedules longer than five times (by design).
- Expansion state is not saved between app sessions or across dates (by design: presentation only).
- The headless TextBox blank-line issue (§10) limits what real-window tests can type; it does not affect the app.
- Mac renders and timings are not Windows evidence; DPI 125 % / 150 % alignment is a Windows checklist item.

## 13. Windows validation status

**NOT EXECUTED.** Phase 35D is not Windows-validated until the product owner runs the checklist in the `CarowindsTest`
account.

## 14. Next milestone dependencies (35E — not started)

35E (Dashboard and Calendar) needs: Windows sign-off of 35D (especially the title bar, which affects every screen), and
the approved Calendar references (`carowinds_calendar_*`, present) plus the **approved Dashboard reference, which the
design bundle says is not present** — it must be supplied before Dashboard work starts. 35E reuses the shared tokens,
the dropdown chevron style and the sheet styles from 35B–35D.
