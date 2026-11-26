# RC14 local Windows test candidate — Phase 35C (Products & Pricing and the price popover)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed or published; no update manifest. RC.13 (published
prerelease `v0.1.0-rc.13`, tag → `d784c60`) and every frozen file under `artifacts/releases` are unchanged
(all 117 earlier release and local-test files re-hashed byte-identical after this build).

| | RC14-Phase35C-LOCALTEST |
|---|---|
| Source commit | `051cc4e` (Phase 35C on top of Phase 35B `bad86da`) |
| Version shown in the app | `0.1.0-rc.14-LOCALTEST.phase35c` |
| Package | `artifacts/local-test/RC14-Phase35C-LOCALTEST/LOCAL-ONLY-RC14-Phase35C-LOCALTEST-win-x64.zip` |
| ZIP size | 76,066,099 bytes (72.5 MB); 308 files |
| ZIP SHA-256 | `69d4edb1a8f9ed9f87f270591fd929f007bd11d240b7943651f57c3e6c29dcad` |
| EXE inside the ZIP | `LOCAL-ONLY-RC14-Phase35C-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 5,697 passed / 0 skipped; clean Release build 0 warnings / 0 errors; script content validation on folder and ZIP (308 files); `RealReleaseZip` passed; marker file names the clean source commit; no database, draft, history or update-manifest file in the ZIP |

Source `<Version>` is still `0.1.0-rc.13` (not bumped). The test identity sorts above RC.13, so the updater never
offers a release over it. Database schema 18 (no migration). Personal Sync format unchanged. No new local files.

This build also contains Phase 35B (Passdown workspace), which has **not** been Windows-tested yet. If convenient, run
`rc14-phase35b-localtest-windows-checklist.md` with this build too; it supersedes the 35B ZIP.

## What changed (what to look for)

1. **Products & Pricing** is one white sheet like Overview: heading, a **Search products and offers** box and **Add
   custom item**; each category heading can be collapsed and says "N items · N included".
2. Each row: checkbox · product and its notes · **Official** price · **Display price** as plain text with a small
   pencil · **Details** / **History** on the right. No price boxes in the rows any more.
3. Clicking the display price (or Tab to it and press Enter/Space) opens a small **Edit display price** box next to it:
   the product name, the value, "Official price: …", **Cancel**, **Save**, and **Use official price** when you have
   changed the price before.
4. A changed price shows "Manual override" under it; the official price stays visible.
5. Custom items are rows in the same style; their price is edited with the same box.
6. Current Deals and Pass Perks are below the products (same content as before).

Mac results: `docs/phase35c-products-pricing-redesign.md`. They are **not** Windows results.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with synthetic data (setup in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation"). Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`. Do not run
`Enable Automatic Refresh.cmd`, do not point Personal Sync at your real shared folder, and do not install any update
from this build. Settings → About shows `0.1.0-rc.14-LOCALTEST.phase35c`.

## Manual checklist (all NOT YET TESTED)

Mark each **Pass / Fail / Not executed**, with a note or screenshot for anything odd. Record the PC model, Windows
build and display scaling.

### A. Products appearance

1. Passdown → **Products & Pricing**: a white sheet, category headings on a light tint, thin lines between rows, no
   boxes around single products. Compare with `carowinds_products_refined_prototype.html` / `carowinds_products_preview.png`
   (ignore their sample prices and offers).
2. Official and display prices line up in their columns; product names and notes are readable; "Compare at",
   "Reference price", "Renewal offer", "Valid ID required", deadlines and any stale warning still appear under names.
3. Memberships and unavailable options say "Not printed on the passdown." and have no pencil.
4. A product with no official price reads "Not priced" / "Not available" in grey, not like a price.

### B. Price popover — open, save, cancel

5. Click a display price: the box opens **right under that price**, the row does not move, the cursor is in the
   value with it selected. Only one box can be open.
6. Type `39` → **Save**: the box closes, the row shows **$39.00** and "Manual override", the official price is
   unchanged, the save status goes Unsaved → Saving… → Saved, and the preview shows $39.00.
7. Open it again, type `12`, press **Escape** (and separately: click **Cancel**, and click somewhere outside the box):
   nothing changes, nothing is saved, the preview does not change.
8. Open it, type `Special` → Save: the box stays open with "Enter a valid price, e.g. 24.99." and the typed text;
   nothing changes. Fix it to `41` and press **Enter**: it saves.
9. On a price you changed, open the box → **Use official price**: the official price is back, "Manual override" is
   gone, the preview updates.
10. A "From $…" product (e.g. Military/First Responder): the box notes it prints as "From"; saving `35` shows
    "From $35.00" and "Prints as From $35.00".
11. Open the box on one product, then click another product's price: the first box closes without saving and the
    second opens on the right product.

### C. Price popover — edges, scrolling and navigation

12. Scroll so a product is near the **bottom** of the window and open its price: the box opens **above** the price and
    is fully visible. Near the right edge it stays inside the window.
13. With a box open: scroll the list, resize the window, switch to Overview, change the date with ‹ ›, type in Search
    — each closes the box **without saving** (check the price is unchanged).
14. Open a box, then press Tab repeatedly: focus stays inside the box (value → Use official → Cancel → Save → back).
    After Save/Cancel focus returns to that product's price.

### D. Custom items

15. **Add custom item**: a row appears with the Custom chip, name and notes boxes, "No price" and Remove. Set a price
    through the box (blank is allowed and clears it). It prints on the PDF when ticked. Remove works.

### E. Categories, search and inclusion

16. Click a category heading: it collapses/expands; the "N items · N included" text changes when you tick/untick rows;
    ticking a row updates the preview.
17. Type in Search (e.g. "pass"): only matching rows show; clearing the search brings everything back; nothing is
    saved by searching.

### F. Details, history, deals and perks

18. **Details** opens the official details dialog for that product; **History** (season passes / add-ons) and
    **Season pass price history** open Price History.
19. **Current Deals**: current, upcoming, previously verified and expired offers, status line and View details are as
    in RC.13. **Pass Perks**: month picker, status, perks with dates/eligibility, Program terms — as in RC.13.

### G. Persistence, sync and PDF

20. Close and reopen the app: changed prices, inclusion and custom items for that date are still there; other dates
    are not affected.
21. **Create PDF**, Print and Save PDF contain the changed price; Copy for Teams pricing shows it too.
22. (Optional, test sync folder only) Personal Sync carries the override to the second test copy as before.

### H. Appearance, motion, scaling and keyboard

23. Light, Dark and "Use Windows setting": rows, headings, the box and its buttons are readable in each.
24. Interface animations **On**: the box fades in briefly; **Off** (or Windows Animation effects off): it appears
    instantly. In both, you can type and press Save immediately.
25. At 100 %, 125 % and 150 % scaling and window sizes ≈1920×1080, 1536×960, 1280×720 and the 960×640 minimum, with
    the sidebar expanded and collapsed and the preview shown and hidden: no overlapping columns, no clipped prices or
    pencils, no sideways scrolling, the box fully visible. Optionally watch Task Manager memory while opening and
    cancelling the box ~50 times (it should level off).
26. Narrator reads "Edit display price for <product>, currently <price>", the box name, the value field, and the
    error message when a value is invalid.

### I. Regression

27. Overview, Shows, Notes, Check Online, Calendar, History (open a stored PDF), Settings and the Phase 35B toolbar
    (Create PDF, Actions, Ctrl+P) behave as before.

Report each item as **Pass / Fail / Not executed**. Phase 35C is not Windows-validated until this is done.
