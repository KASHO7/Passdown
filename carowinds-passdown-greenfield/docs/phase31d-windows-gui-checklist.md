# Phase 31D/31E — Windows GUI checklist (Associate layout and Passdown default preferences)

Phase 31D is a focused refinement of the Associate screen and Settings:

- **Shows rail section (Phase 31E):** show selection moved off Overview into its own left-sidebar section.
  Sidebar order: Overview, Products, Shows, Notes. The Shows page holds the official-schedule label and
  status, the date's shows with times and locations, per-show checkboxes, and Select all / Clear
  (panel up to 960 px wide). Overview keeps **Include in Passdown** only. (Phase 31D's side-by-side
  Overview layout was replaced by this.)
- **Include in Passdown** now holds the **Shows** checkbox (after Operational Notes). The Shows page has no
  inclusion checkbox; one line there says whether Shows prints. Unchecking only stops printing; the
  selected shows are kept and print again when it is re-checked.
- **Settings ▸ Passdown Preferences:** seven checkboxes choosing which sections start checked for a date
  with no saved draft. Saved on this computer only (`%LOCALAPPDATA%\Carowinds Passdown\passdown-defaults.json`),
  never synced, Personal Sync not required.
- **Root cause fixed:** saved drafts always kept their selections. Operational Notes looked "deselected
  after a restart or update" because every date with no saved draft — including the date the app opens on
  — started from a hard-coded default with Operational Notes **off**. The built-in default is now **on**,
  and new dates use the configured Passdown Preferences.

No database migration (still schema 18), no draft schema change (still 4), no Personal Sync contract change.
Retrieval, show schedules, PDF templates and Copy for Teams are unchanged.

**Windows validation is recorded separately from this commit.** Until someone runs this checklist on
Windows, every item is **UNVERIFIED**. Headless renders on macOS (960×640, 1280×690, 1280×860, 1920×1000,
light and dark) are layout evidence only — **not** Windows DPI, ClearType or Narrator validation.

**Setup.** Use a disposable Windows test account (a test build writes the normal app profile). Do not use
in-app update from a local test build. Frozen RC.10 artifacts are not rebuilt or replaced.

Write PASS / FAIL / N/A and a short note for each item. Take a screenshot of each FAIL.

## A. Associate layout

1. The Passdown sidebar reads **Overview, Products, Shows, Notes**; the current section is highlighted and
   Narrator reads "Shows section".
2. Overview has no Shows list; Include in Passdown is the last panel.
3. **Shows** page: label "Official schedule", the check time, each show with times and location, a
   checkbox per show, **Select all** and **Clear**. Maximized, the panel is at most about 960 px wide and
   sits at the top-left; at about 960 px window width it fills the page. No horizontal scroll bar; nothing
   overlaps or is cut off. Long show names wrap.
4. Switching between Overview, Products, Shows and Notes never shows "Saving" or starts an online check.
5. Repeat 1–3 at 125 % and 150 % display scaling, in Light and Dark.

## B. Shows inclusion

1. Include in Passdown lists: Daily Admission, Fast Lane, Promotions, Season Passes, Add-Ons,
   (SCarowinds / Haunt when in season), Operational Notes, **Shows**. There is no checkbox in the Shows
   panel header.
2. Shows unchecked: the Shows page says "Shows is not included in the Passdown. Turn it on under
   Overview ▸ Include in Passdown." in orange text; you can still select shows, Select all and Clear.
3. Select two shows, check Shows, **Create PDF** in Classic and in Modern: both shows print with their times.
4. Uncheck Shows: the same two shows stay selected on the Shows page, and a new PDF has no Shows section.
   Re-check Shows: a new PDF prints the same two shows again.
5. A date saved by an earlier version with shows selected opens with the same shows selected.
6. The official seasonal-event information is still shown inside Include in Passdown.

## C. Settings ▸ Passdown Preferences

1. Settings shows **Passdown Preferences** between Appearance and Data Refresh, with the description
   "Choose which sections are selected automatically when starting a new Passdown."
2. Fresh profile: Daily Admission, Fast Lane, Promotions and **Operational Notes** are checked; Season
   Passes, Add-Ons and Shows are not.
3. Change several checkboxes, close and reopen the app: the choices are kept.
4. Go to a future date you have never opened: its Include in Passdown matches the preferences.
5. With Personal Sync **off**, preferences still save and apply.

## D. Saved selections (the reported problem)

1. On today's date, check Operational Notes (or leave it checked) and wait for "Saved". Close and reopen:
   still checked.
2. On a date with a saved draft, **uncheck** Operational Notes, wait for "Saved", then turn the Operational
   Notes preference **on** in Settings. Return to that date: still unchecked.
3. Change preferences while an unsaved date is open, then return to Passdown: that date shows the new
   preferences. A saved date does not change.
4. A draft saved by RC.10 (Operational Notes unchecked) still opens unchecked after updating; a draft
   saved with it checked opens checked.
5. Switching dates never starts an online check.

## E. Personal Sync (two computers, optional)

1. Computer A: save a draft with Shows on and Operational Notes off; sync. Computer B: the same choices
   appear for that date.
2. Different Passdown Preferences on A and B do not sync and do not change each other's saved drafts.

## F. Unchanged

1. **Copy ▾** still offers exactly: operations summary, today's pricing, both.
2. History, Calendar and Dashboard Shows look and behave as before.
3. Narrator reads "Include Shows in the Passdown" for the new checkbox and "Select … automatically for a
   new Passdown" for each preference. (Narrator validation is separate until actually tested.)
