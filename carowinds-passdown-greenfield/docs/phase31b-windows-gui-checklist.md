# Phase 31B — Windows GUI checklist (LOCAL-ONLY test build)

Short, GUI-only checks for Products, Important Details & Deadlines, Season Price History and ERT
conflict resolution. Everything else in Phase 31B is covered by the deterministic suite (5,000 tests).

## Build under test

| | |
|---|---|
| ZIP | `artifacts/local-test/phase31b-final/LOCAL-ONLY-phase31b-final-win-x64.zip` |
| SHA-256 | `43c9ce337a61456963549bc3256cae830e851b629782f812e7312969db541e6f` |
| Version | `0.1.0-rc.11` (development version; **not** a release, not published) |
| Source | uncommitted changes on top of `6fd1999` (Phase 31B finalization) |
| Frozen RC.10 | untouched — ZIP `26f447e3…5d28fae`, manifest and `.sig` byte-identical |

**Setup.** Use a disposable Windows test account (this build writes the normal app profile). Extract
the ZIP to a writable folder and run `Carowinds.Passdown.Desktop.exe`. Check that the folder holds
`LOCAL-ONLY-TEST-BUILD.txt` and that About this build shows `0.1.0-rc.11`. **Do not** use in-app
update from this build. Let the startup check finish (or press **Check Online**) before section A.

Write PASS / FAIL / N/A and a short note for each item. Take a screenshot of each FAIL.

## A. Products

1. Products shows **Current Deals** with an explicit status line ("Official deals checked …"). It is
   never blank without an explanation.
2. The **2027 Military Gold Pass** and the **Military & First Responder Ticket** are each listed
   once, with official prices. Neither appears as "Retired".
3. An offer whose only end date comes from a page banner shows the date line
   `Page banner (inferred end date, not stated in this offer's own terms): "…"`. An offer with its own
   date shows a plain `Purchase by <date> ("<official text>")` with no "inferred" wording.
4. Changing the selected date does **not** start an online check (status line and last-checked time
   stay the same).

## B. Important Details & Deadlines (a product row's expander)

1. On a season-pass tier row, expand **Important Details & Deadlines**. The header summary shows the
   next open deadline. Purchase ("Buy by") and redemption ("Use by") dates are labeled separately.
2. **Confirmed vs inferred.** A date taken from the campaign banner, or written without a year, reads
   e.g. `Buy by November 27, 2026 (inferred from the page's campaign end date; year not stated)`. A
   date stated in full in the card's own terms reads plain, e.g. `Use by January 3, 2027`.
3. When any inferred date is shown, the italic note below the lines appears: *Dates marked
   "inferred" are not stated in full as this item's own deadline …*. A row with only confirmed dates
   has no such note.
4. Each line shows its status word (Current / Previously verified / Expired / Unverified) in text,
   not by color alone. Hovering shows the reason.
5. *(Only if testing after an inferred date has passed, e.g. after Nov 27, and before a successful
   re-check.)* That line reads **Previously verified**, with a reason ending "not confirmed as ended
   by the official page". It must **not** read Expired. After a successful **Check Online** it either
   stays listed (Current) or reads "No longer listed on the official page".
6. Provenance line ("From the official … card … · Last verified …") and the source link button
   are present and the link opens the official Six Flags/Carowinds page.
7. The section is display-only: generating the Associate PDF does not print it.

## C. Season Price History (Associate ▸ Season Price History…)

1. The screen opens read-only. Columns FIRST SEEN / LAST CONFIRMED / PRICE / CHANGE are readable
   at 100 % and 150 % scaling, in light and dark appearance.
2. **2027 Military Gold Pass** is listed without ticking **Include retired products**. Its detail
   is "Season Pass" (or "Season Pass · Availability unconfirmed"), never "Retired". Its earlier prices
   are still shown.
3. Sale rows read "$… (sale price)". A price-type change at the same amount has the note
   "Price type changed: … → …".
4. *(Two-computer data only; see section E.)* A row imported from another computer carries the
   provenance text. A concurrent reading carries "Recorded while another computer's official checks
   still showed the earlier price …". The row after it measures its change from the period it
   actually followed.

## D. ERT conflict resolution (single account: layout only)

With one account you can only check layout. A real conflict needs section E.

1. Personal Sync settings open, and **Review Conflicts** / **Conflicts** appear when attention is
   needed. With no conflicts nothing is shown as needing attention.

## E. Requires two Windows accounts **and** real OneDrive syncing — NOT verified by simulation

The deterministic suite simulates these with in-process devices and a shared temp folder. That is
**not** evidence for Windows + OneDrive. Leave each item **UNVERIFIED** until it is run on two real
Windows accounts (or PCs) signed in to OneDrive with the same Personal Sync folder. Never mark one
PASS from a simulated test.

| # | Check | Expected |
|---|---|---|
| E1 | Same November ERT rides entered in a **different order** on each account, same start time; let OneDrive sync, run sync on both | No conflict on either. Each account keeps its own ride order |
| E2 | Different rides (e.g. Mountain Gliders vs Fury 325) | One conflict. **Conflict Detail** shows **Merge or edit rides** prefilled with the union, this device's order first |
| E3 | Same rides, different order **and** a different start time | A conflict about the start time only. Rides are not flagged as differing. "Keep them from: This Device / Other Device" is offered |
| E4 | Edit the merged list, choose a base, **Save Edited Rides** | Conflict closes. The edited list arrives on the other account after OneDrive syncs, with no new conflict |
| E5 | Restart the app mid-conflict (both accounts) | Conflict is still there and can be resolved after restart |
| E6 | Pause or disconnect OneDrive, then try **Save Edited Rides** | Nothing changes. Conflict stays open with a message |
| E7 | Season Price History after both accounts have run official checks on different days | Matching periods combine into one row (no repeated First Seen). Imported rows are labeled. Order of syncing does not change the table |
| E8 | OneDrive creates conflict copies (files with the PC name appended) in the sync folder | Read safely. Nothing is lost or duplicated |
| E9 | Live SQLite database location | `%LOCALAPPDATA%` profile only. No `.db`, `-wal` or `-shm` file ever appears in the OneDrive folder |

## Result

Record the tester, date, Windows build, both account names (E only) and every FAIL with a screenshot
in a follow-up note. This checklist does not approve a release candidate: RC.11 packaging, signing,
tagging and publishing remain separate steps.
