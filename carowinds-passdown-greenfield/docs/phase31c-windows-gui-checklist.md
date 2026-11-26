# Phase 31C — Windows GUI checklist (compact Products, LOCAL-ONLY test build)

Phase 31C is a presentation-only change to Associate ▸ Products: compact Current Deals rows, a small
**View details** link on product and deal rows, and an in-app details dialog. Retrieval, availability,
pricing, deadlines, price history, ERT and Personal Sync are unchanged (database still schema 18).
Everything below is GUI-only; the logic is covered by the deterministic suite.

**Windows validation is recorded separately from this commit.** Until someone runs this checklist on
Windows, every item is **UNVERIFIED**. Headless renders on macOS are evidence for layout review only —
they are **not** Windows DPI, ClearType or Narrator validation.

## Build under test

| | |
|---|---|
| ZIP | `artifacts/local-test/phase31c-compact-products/LOCAL-ONLY-phase31c-compact-products-win-x64.zip` |
| SHA-256 | recorded in the Phase 31C commit report (the ZIP is built from the committed source) |
| Version | `0.1.0-rc.11` (development version; **not** a release, not published) |
| Frozen RC.10 | untouched — ZIP `26f447e3…5d28fae`, manifest and `.sig` byte-identical |

**Setup.** Use a disposable Windows test account (this build writes the normal app profile). Extract
the ZIP to a writable folder and run `Carowinds.Passdown.Desktop.exe`. Check that the folder holds
`LOCAL-ONLY-TEST-BUILD.txt` and that About this build shows `0.1.0-rc.11`. **Do not** use in-app update
from this build. Let the startup check finish (or press **Check Online**) before section A.

Write PASS / FAIL / N/A and a short note for each item. Take a screenshot of each FAIL.

## A. Current Deals (Associate ▸ Products, top panel)

1. Each deal is one compact row: **offer name**, price on the right, one short grey description, and
   one line with a confirmed deadline (if any) and a small **View details** link. No large
   "Terms and details" boxes, no campaign banners, no long URLs inside the rows.
2. A multi-option offer shows "From $…" only when the official options state prices. An offer without
   a dollar price shows its official benefit (for example "3rd Night FREE"), never "$0".
3. An offer whose only end date is a page banner shows **no** deadline in its row (the banner date is
   in View details, labeled inferred). An offer with its own date shows "Buy by <date>".
4. **Upcoming offers** has its own heading; previously verified and expired offers stay inside their
   collapsed "… (n)" sections, and their rows say "Previously verified" / "Expired" in text.
5. An offer whose official sources disagree shows one short orange line "Official sources disagree —
   see details." (text, not color alone).

## B. Product rows

1. Season Passes, Memberships, Military/First Responder, promotional and add-on rows each show one
   small **View details** link (beside **Price history** where that exists). Daily Admission and Fast
   Lane rows do not.
2. A row with a confirmed purchase deadline in its official terms shows it in one short bold line
   ("Buy by …"). Inferred dates never appear in the row.
3. Official prices, Display Price boxes, "Include on PDF" checkboxes and "Use official price" are
   unchanged from RC.10 behaviour.

## C. View details dialog

1. Opens centered over the window, dimming the rest. Title, category, official price (with reference /
   renewal / initiation notes) and status are at the top; **Close** is top-right.
2. Terms are grouped under short headings (Eligibility, Included benefits and perks, Purchase terms and
   deadlines, Validity and visit windows, Exclusions and restrictions, Fees, Official sources
   disagree, Earlier official terms). Each Important Details line shows its status word and deadline.
3. Inferred dates keep their wording ("inferred from the page's campaign end date; year not stated")
   and the italic inferred-dates note appears below the terms.
4. A product with no recorded terms says "No additional official terms have been recorded for this
   item…" rather than showing an empty dialog.
5. The footer shows the last successful verification and one **Official page:** link per official
   source. The link opens the official Six Flags / Carowinds page in the default browser and the
   dialog stays open.
6. Long terms scroll inside the dialog; nothing is cut off and there is no horizontal scrollbar.
7. Viewing details changes nothing: no autosave indicator change, no online check, same prices after
   closing.

## D. Keyboard and Narrator

1. Tab reaches every **View details** link; Enter or Space opens the dialog.
2. Focus lands on **Close**. Tab cycles inside the dialog (Close → official link(s) → Close) and never
   reaches the page behind it.
3. Esc closes the dialog and focus returns to the same **View details** link.
4. Narrator reads each link as "View details for <product or offer>", the Close button as "Close
   details for <name>", the title as a heading, and each section heading as a heading.
5. Narrator reads each Important Details line with its status word (Current / Previously verified /
   Expired / Unverified).

## E. Appearance and scaling

Check each at **100 %, 125 % and 150 %** Windows display scaling, in **light and dark** appearance:

1. Products rows: names prominent, prices right-aligned and easy to find, no overlap between the link
   line and the next row, long descriptions wrap (at most two lines, then "…").
2. Dialog: fits the window at 1280×720 and at a small window (about 960×640); text wraps; links and
   the Close button are not clipped.
3. Compare against RC.10's Products screen: the page should be at least as compact (fewer lines per
   product) and the Daily Admission / Fast Lane groups should look unchanged.

## Result

Record the tester, date, Windows build, scaling/appearance combinations tested and every FAIL with a
screenshot in a follow-up note. This checklist does not approve a release candidate: RC.11 packaging,
signing, tagging and publishing remain separate steps.
