# Phase 21B — Associate navigation, date picker and toolbar

Development phase after RC.5 (`0.1.0-rc.5`), following Phase 21A. A UI/workflow phase: no new
retrieval, no new persistence, no migration. Schema versions are unchanged: DB 16, Draft 4,
Snapshot 2, SyncSchemaVersion 1. The next packaged candidate is still `0.1.0-rc.6`;
`Directory.Build.props` stays at `0.1.0-rc.5` until that packaging phase.

## 1. Action inventory before this phase

Associate Passdown header, as it was at `50793de`:

| Action | Location | Function | Category | Date-sensitive | Changes data | Confirmation | Belongs in Settings |
|---|---|---|---|---|---|---|---|
| ◀ Previous day | Action bar | `SelectedDate − 1` | Navigation | Yes | Flushes pending edits of the old date | No | No |
| Date picker (`CalendarDatePicker`) | Action bar | Choose a date (text box + calendar) | Navigation | Yes | Same as above | No | No |
| ▶ Next day | Action bar | `SelectedDate + 1` | Navigation | Yes | Same as above | No | No |
| Check Online | Action bar (primary style) | Explicit official check for this date | Primary | Yes | Updates official cache (last-known-good kept on failure) | No | No |
| Export Diagnostics | Action bar | Sanitized support ZIP via save picker | Administrative | No | No | No | **Yes** |
| PDF Style (combo box) | Action bar | Classic / Compact / Modern template preference | Secondary | No | Device preference only | No | No |
| Create PDF | Action bar | Build PDF, save picker | Primary | Yes | No (flushes draft first) | No | No |
| Copy ▾ (3 items) | Action bar | Operations Summary / Today's Pricing / Operations + Pricing to clipboard | Primary | Yes | No | No | No |
| Save status chip | Title row | Saved / Saving… / Unsaved changes | Status | Yes | — | — | No |
| Overview / Products / Notes | Left rail | In-page sections | Navigation | No | No | No | No |
| Season Price History… | Products section | Opens the read-only overlay | Secondary | No | No | No | No |

There is no explicit Save button (debounced autosave + flush on every date change / PDF / Check
Online), and no Finalize, Reset-all, Clear or Delete action anywhere on Associate — finalization is
automatic (Phase 9). Per-field Reset buttons (gate overrides, product display overrides) are inline
next to the fields they reset and are unchanged.

## 2. New header

```
Associate Passdown                                                    [Saved locally at 9:14 AM]

[‹] [📅 Friday, October 16, 2026  (Today)   ⌄] [›] [Today]    [Check Online] [Create PDF · Classic] [Copy ▾] [More ▾]
```

- One row of 34px controls; a `WrapPanel` (the Phase 15E pattern) puts the action group on its own
  line when the window is too narrow (the 960×640 minimum, or high display scaling). No horizontal
  scrolling, no overlap — asserted at 1280×860, 960×640 and 1280×690 (≈ maximized at 150% on
  1920×1080) in `Phase15ERealWindowLayoutTests`.
- The date button has a fixed minimum width (360px) so stepping through dates doesn't shift the
  controls next to it.
- A small chip beside the date says **Today**, **Yesterday** or **Tomorrow** (Today in the accent chip
  colour); other dates show no chip. The word carries the meaning, never the colour alone.
- Save status remains the single chip in the title row; the Overview status strip ("Draft: … | …")
  is unchanged. Verification / refresh / copy / PDF / load-error / sync-conflict lines are unchanged.

## 3. Previous / next

`PreviousDayCommand` / `NextDayCommand` (unchanged) set `SelectedDate ± 1`, which goes through the one
existing path: `OnSelectedDateChanged` → `SwitchDateAsync` → flush the previous date's pending
autosave → local load → (Phase 19A) quiet automatic check only for today/future dates whose data is
missing or stale. Month, year and leap-day boundaries are plain `DateOnly.AddDays`. Future dates are
not restricted.

## 4. Today

- **Today** button in the header and **Today** link inside the calendar popup both run
  `GoToTodayCommand` → the same `SelectEasternToday` path Phase 21A added for Dashboard → Open
  Passdown (the log line now names which control triggered it).
- "Today" is `EasternTimeZone.ConvertFromUtc(IClock.UtcNow)` read **at click time** — never the host's
  local date, never a value cached at startup. Midnight rollover, DST transitions, month and year
  rollovers are covered by tests.
- When today is already selected nothing reloads and no check is requested; only the header's
  Today/Yesterday label is refreshed (it can lag after midnight until the next navigation, popup
  opening, or Today click).
- The Phase 21A 6:00 AM historical pricing cutoff is untouched — it is a pricing/finalization policy,
  not the meaning of "Today" here.

## 5. Calendar popup

The old `CalendarDatePicker` (typed text box + calendar, committed every arrow-key move) is replaced
by a plain `Button` with a `Flyout` containing Avalonia's built-in `Calendar` — no custom calendar
control, no new framework.

- **Opens on the selected date.** Every opening resets the calendar to month view, selects the current
  date and shows its month (`AssociateView.DateFlyout_OnOpening`) — reopening never shows an abandoned
  browse position.
- **Commit rules** (view-only code-behind; the view model is untouched): a click on a day cell, or
  Enter/Space in the month view, hands the date to `SelectedDateForPicker` (the same one-path adapter as
  before — a no-op when unchanged, so exactly one load per real change) and closes the popup. Arrow
  keys only move the highlight; nothing loads while browsing.
- **Dismissal:** Escape (handled explicitly; focus returns to the date button) and clicking outside
  (Flyout light-dismiss) close without changing the date. The popup also closes if the view is detached.
- **Month/year jumping:** the built-in heading button switches month → year → decade views; picking a
  month/year drills back down without selecting a day. Page Up/Down and the ⌃/⌄ buttons step months.
  A one-line hint ("Select the month name to jump") sits in the popup footer.
- **Visual states:** selected day = filled accent tile, bold white text; today = accent ring, bold
  accent text, no fill; selected + today = fill with a light inner ring; adjacent-month days muted
  (Fluent default); hover = light band tint. Fluent's 20px day text and grey grid background were
  reduced to 13px text on a transparent grid, 38px cells (294px wide popup).
- **Today marker vs host time zone:** the built-in Calendar marks "today" from the host's local
  date. `IsCalendarTodayHighlighted` shows that marker only when the host date equals the Eastern
  date, so the popup can never mark a different day than the Today button selects.
- **Placement:** `BottomEdgeAlignedLeft` under the date button; Avalonia's popup positioner keeps it
  on-screen. Asserted inside the window at 1280×860 and 960×640.
- **Spell check:** the popup and menus contain no text boxes; spell-check adorners are per-TextBox
  (Phase 20C) and unaffected.

## 6. Toolbar hierarchy

| Group | Actions |
|---|---|
| Date | ‹ Previous day · date / popup · › Next day · Today |
| Primary (always visible) | Check Online · Create PDF · Copy ▾ |
| More ▾ | PDF Style: Classic / Compact / Modern (radio items, current one checked) · Season Price History… |
| Settings → Diagnostics | Export Diagnostics… |

- **Create PDF** now shows the style in use on the button itself ("Create PDF · Classic"); the style is
  changed under More → PDF Style. That sets the same `SelectedTemplateOption`, so the saved template
  preference is loaded and saved exactly as before and is never changed implicitly.
- **Copy ▾** is unchanged: same three items, same command/parameters, same text output.
- **More** holds only clearly labelled, non-destructive items. Season Price History also remains in
  the Products section. The three PDF styles are flat radio items rather than a nested submenu: a
  `MenuItem` with child `MenuItem`s fills an `ItemCollection` while the XAML loads, which needs
  dispatcher access and broke headless construction of `AssociateView` in the mixed UI test partition
  (found by the full deterministic run; same reason this view avoids `TabControl`).
- No action was removed; Export Diagnostics moved (below).

## 7. Settings → Diagnostics

`DiagnosticsExportViewModel` (new, Desktop) holds the export command, moved verbatim from
`AssociateViewModel` — same `IDiagnosticExportService` (ZIP contents, privacy filtering, file name),
same `IDiagnosticExportFilePicker`, same messages ("Diagnostics saved.", "Diagnostics could not be
saved. Try again.", "Diagnostics could not be created. Try again."), same logging. It is exposed as
`PersonalSyncSettingsViewModel.Diagnostics` and shown as a **Diagnostics** section at the bottom of
Settings with the button and its result line. `AssociateViewModel` no longer takes the diagnostics
dependencies. No generation logic was duplicated; Infrastructure's `DiagnosticExportServiceTests`
(contents/privacy) are unchanged.

User-facing instructions updated: `packaging/windows/TESTER-GUIDE.txt`,
`packaging/windows/README-portable-release.md`, `docs/support-troubleshooting.md`.

## 8. Keyboard and accessibility

- Tab / Shift+Tab: ‹, date, ›, Today, Check Online, Create PDF, Copy, More — Fluent focus visuals.
- Enter/Space opens the popup and menus; inside the popup arrows move, Enter/Space selects, Escape
  closes. No global shortcuts were added.
- Accessible names: "Previous day", "Operating date" (help text: "Selected date: Friday, October 16,
  2026 (Today). Choose a different date."), "Next day", "Today", "Choose operating date" (calendar),
  "Go to today", "More passdown actions", "Classic/Compact/Modern PDF style", "Open Season
  Price History", "Export Diagnostics". Icon-only controls have tooltips (Previous day, Next day,
  Choose date, Jump to today, More passdown actions).
- Selection is never colour-only: date text + Today chip word in the header; fill vs ring vs weight in
  the popup.

## 9. Preserved behaviour

- Dashboard → Open Passdown → today's Eastern date (Phase 21A), including after arrow navigation.
- Operations Calendar → Open Passdown / View Date Pricing keep their explicit date; arrows continue from it.
- History routes keep their record; Associate navigation never changes History's selection.
- Draft safety: every date change flushes the previous date's pending edits first (Daily Feature,
  notes, attendance, pre-solds, ERT, gate overrides, products). No silent loss, no new dirty-state
  framework.
- Retrieval: date changes never call Check Online; superseded automatic checks are cancelled (rapid
  stepping leaves only the latest pending); re-selecting the same date does not reload.
- Pricing (Phase 21A), historical backfill (Phase 20C), spell check (Phase 20A/20C), PDFs, Copy output,
  frozen History and Personal Sync are untouched — no code in those areas changed.

## 10. Tests

- `Phase21BAssociateNavigationTests` (no real window): previous/next incl. month/year/leap rollovers,
  future dates unrestricted, Today from past/future, Today when already today (no load, no check),
  Eastern vs UTC, midnight rollover read at click time, DST/month/year Eastern wall-clock cases, header
  labels and long date format, host-vs-Eastern today marker, same-date no reload, rapid stepping
  cancels superseded checks, load failure doesn't block navigation, edits flushed on arrows/Today,
  Dashboard/Calendar/History routes, toolbar names, markup guards (Copy, PDF styles, no diagnostics, tooltips), PDF style selection and
  preference, Price History from More.
- `Phase15ERealWindowLayoutTests` (run-alone real-window class): action bar at three sizes incl. no
  overlap and no Export Diagnostics; popup opens on the selected month, 42-cell grid, selected and
  adjacent-month states, stays within the window; clicking a day selects it once and closes; month/year
  navigation never selects; reopening resets; Enter commits the highlighted day exactly once, Escape
  and browsing change nothing, Enter in the year view never commits, committing the already-selected
  day doesn't reload. (The last two need `Calendar.SelectedDate` set on the dispatcher thread, which
  only the run-alone classes guarantee — a test-host constraint, not an app defect.)
- `DiagnosticsExportViewModelTests` (renamed from `AssociateViewModelDiagnosticsExportTests`): the
  moved command's outcomes, Settings wiring and Settings markup.

No new run-alone class: the deterministic partition keeps the same four run-alone classes.

## 11. Windows validation checklist (not executed — requires the Windows VM)

- [ ] Calendar popup appearance in light and dark theme (selected fill, today ring, muted adjacent days)
- [ ] Popup placement under the date button; near the right/bottom edge of a small window
- [ ] ‹ / › step one day, including across a month end
- [ ] Today button and popup Today link from a past and a future date
- [ ] Header shows Today / Yesterday / Tomorrow correctly
- [ ] Month heading → year → decade jumping; picking a month does not select a day
- [ ] Clicking outside the popup closes it without changing the date
- [ ] Escape closes it without changing the date; focus returns to the date button
- [ ] Switching past → today → future: Products historical notice / pricing as in Phase 21A
- [ ] Type in Daily Feature / Notes, press › immediately, come back: text kept
- [ ] Dashboard → Open Passdown opens today after navigating elsewhere
- [ ] Operations Calendar → Open Passdown opens the chosen date
- [ ] Copy ▾: all three items, pasted text unchanged
- [ ] More → PDF Style: choose Compact, Create PDF uses it, restart app → still Compact
- [ ] More → Season Price History opens the overlay
- [ ] Settings → Diagnostics → Export Diagnostics saves a ZIP; contents unchanged (no notes/drafts/DB)
- [ ] 100%, 125%, 150% scaling: header readable, action group wraps cleanly, popup and menus not clipped
- [ ] Keyboard: Tab order, visible focus, Enter/Space/Escape/arrow keys in the popup
- [ ] Spell check: underlines in Notes/Daily Feature unaffected by opening the popup or menus;
      cleanup on section switch still works

## 12. Known limitations

- The Today/Yesterday header label is evaluated on date changes, popup opening and Today clicks; if
  the app sits on the same date across midnight it updates at the next of those.
- The calendar's built-in today marker is hidden (not moved) in the rare case the host's local date
  differs from the Eastern date; the Today button and header label remain correct.
- Rendering was checked with an isolated headless Skia harness on macOS (fake services, no real
  profile); actual Windows rendering, DPI scaling and screen-reader output are NOT EXECUTED.
