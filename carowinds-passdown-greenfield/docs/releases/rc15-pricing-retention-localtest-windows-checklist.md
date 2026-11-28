# RC15 local Windows test candidate — post-closing pricing retention

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: __ARTIFACT_COUNT__
were re-hashed after this build.

This candidate fixes the Windows report of 2026-11-27: after the park closed, Daily Admission and Fast Lane prices
disappeared from Products & Pricing and the Passdown PDF without a manual refresh. Prices verified earlier for the
selected date now stay, marked **"Last verified … — sales availability not confirmed."** Nothing else changes.

Record: [pre-rc15-post-closing-pricing-retention.md](../pre-rc15-post-closing-pricing-retention.md).

| | RC15-PricingRetention-LOCALTEST |
|---|---|
| Source commit | __SOURCE_COMMIT__ |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.pricing-retention` |
| Package | `artifacts/local-test/RC15-PricingRetention-LOCALTEST/LOCAL-ONLY-RC15-PricingRetention-LOCALTEST-win-x64.zip` |
| ZIP size / files | __ZIP_SIZE__ |
| ZIP SHA-256 | `__ZIP_SHA__` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-PricingRetention-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | __MAC_VERIFICATION__ |

The source `<Version>` is still `0.1.0-rc.14` (not bumped). Database schema 18, no migration. Personal Sync, the draft
format and finalized History are unchanged.

## Data isolation — use the separate Windows account

Test only in the disposable local account **`CarowindsTest`** with made-up Passdown data. Setup is in
`rc12-phase32-localtest-windows-checklist.md` → "Data isolation". Extract the ZIP to a **new** folder in that account
(right-click → Extract All), then double-click `Carowinds.Passdown.Desktop.exe`.

- **Never** point Personal Sync at the real shared sync folder. Use a disposable folder such as
  `C:\Users\CarowindsTest\Documents\SyncTest`.
- Do not run `Enable Automatic Refresh.cmd`, and do not install any update from this build.
- Settings → About shows `0.1.0-rc.15-LOCALTEST.pricing-retention`.
- Prices come from the real official Six Flags site. Use them for this test only.

## Manual checklist

Mark each item **PASS / FAIL / NOT EXECUTED**, with a note or screenshot (Windows+Shift+S) for anything odd. Record
once at the top of the report: PC model, Windows 11 build, local time of each step, and the park hours of the date
tested.

**Status of every item below: NOT EXECUTED.** No Windows run of this candidate has been reported.

### 1. During operating hours (an open day, e.g. a WinterFest Saturday)
1.1 Launch the app while the park is open (or before it opens). Wait for the startup check to finish (the status line
says the official data was checked). Open **Passdown → Products & Pricing** for **today**.

1.2 **Daily Admission** and **Fast Lane** show today's exact prices with no warning line under them. Note the prices
and the time. Open one row's details: "Official price last verified <date> at <time>".

1.3 **Season Passes** show as usual.

1.4 Optional: give one Daily Admission row a manual price (Edit price) and note it.

### 2. Deterministic closing-time reproduction (the reported problem)
2.1 After the park has **closed** (at least 30 minutes after closing time), with the app still open, click
**Check Online** for today. (Before this fix, this, or the next automatic refresh, cleared today's dated prices.)

2.2 Daily Admission and Fast Lane rows are **still listed with the same prices**. Each shows a line like
**"Last verified Nov 28 at 3:10 PM ET — sales availability not confirmed."** with the time from step 1.2 — not the
time of the Check Online.

2.3 No row says "Verified" as if current, and nothing says the tickets are on sale.

2.4 Season Passes are unchanged and have no new warning.

2.5 The manual price from 1.4 (if set) is still the effective price; Reset to official restores the retained official
price.

### 3. Automatic refresh (no clicking)
3.1 Leave the app open across closing without clicking anything until at least one automatic refresh has run after
closing (up to 4 hours; or close and reopen the app after closing — the startup check runs). Result is the same as
2.2–2.4.

### 4. Passdown PDF
4.1 Passdown → Overview → the live preview still lists the Daily Admission and Fast Lane prices (or the manual price).
The footer says **"Some product pricing has not been recently verified."**

4.2 Check Classic, Modern and Compact (Settings → Passdown template). Print (Microsoft Print to PDF), **Save PDF** and
**Create PDF** each contain the same prices and footer note.

4.3 Product inclusion checkboxes and the Overview selections are unchanged; the date is not marked as changed just by
the check in step 2.

### 5. Restart, next day and other dates
5.1 Close and reopen the app after closing: same as 2.2.

5.2 Select **tomorrow** (an operating date): its own prices show, with no "Last verified" line (normal verified
prices).

5.3 The next morning (after 6:00 AM), select **yesterday**: its Daily Admission and Fast Lane prices still show with
the "Last verified" line. Click Check Online on yesterday: they stay.

5.4 History: if yesterday was finalized automatically, open it in History: the finalized PDF lists those prices.

5.5 Select a date the app never checked (for example a past date before this install): no invented prices.

### 6. Dashboard
6.1 After closing, the Dashboard's Daily admission card shows the price with **"Last verified … — availability not
confirmed"** in the muted warning style. The pricing card's line reads "Some prices are no longer listed — availability
not confirmed".

### 7. Light / Dark and Narrator
7.1 Settings → Appearance → Light, then Dark: the "Last verified …" lines are readable in both.

7.2 Narrator on a retained row reads the product, price and the "Last verified … sales availability not confirmed"
line.

### 8. Unchanged areas
8.1 Weather (Dashboard card, Calendar Weather group, PDF Weather Outlook) behaves exactly as in the Phase 37D
candidate.

8.2 Calendar, Shows, Notes, History and Settings are unchanged.

## Reporting

Send the PASS / FAIL / NOT EXECUTED list with notes and screenshots. Windows validation of this fix is **not complete**
until the owner confirms it.
