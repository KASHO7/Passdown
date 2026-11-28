# RC15 local Windows test candidate — post-closing pricing retention

**LOCAL-ONLY. Not a release.** It is not pushed, tagged, signed or published, and it has no update manifest.
RC.14 (published prerelease `v0.1.0-rc.14`) and every earlier file under `artifacts/` are unchanged: all 15,329 (39 under `artifacts/releases`)
were re-hashed after this build.

This candidate fixes the Windows report of 2026-11-27: after the park closed, Daily Admission and Fast Lane prices
disappeared from Products & Pricing and the Passdown PDF without a manual refresh. Prices verified earlier for the
selected date now stay, marked **"Last verified … — sales availability not confirmed."** Nothing else changes.

Record: [pre-rc15-post-closing-pricing-retention.md](../pre-rc15-post-closing-pricing-retention.md).

| | RC15-PricingRetention-LOCALTEST |
|---|---|
| Source commit | `42f6a7b` (production source identical to `1be060d`; the later commit is documentation only) |
| Version shown in the app | `0.1.0-rc.15-LOCALTEST.pricing-retention` |
| Package | `artifacts/local-test/RC15-PricingRetention-LOCALTEST/LOCAL-ONLY-RC15-PricingRetention-LOCALTEST-win-x64.zip` |
| ZIP size / files | 76,543,573 bytes (73.0 MB); 308 files |
| ZIP SHA-256 | `080ffc5765110f35a137bb08d276e2ec2ce9d19c7a412ab9287ec5835345d191` |
| EXE inside the ZIP | `LOCAL-ONLY-RC15-PricingRetention-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Deterministic suite 6,518 passed / 0 failed / 0 skipped; repository guards 31/31; clean Release build 0 warnings / 0 errors; script content validation (folder and ZIP, 308 files); `RealReleaseZip` passed (a bogus path fails); marker names `42f6a7b`; no database, WAL/SHM, draft, weather cache, preference file, key, signature, update-manifest, PDB or PDF file in the ZIP; all 15,329 earlier artifact files (including RC.14) re-hashed byte-identical |

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

## Simulated closing — verify without waiting for the park to close

Sections 1–3 need a real closing. This section reproduces the same transition deterministically with synthetic data
for the past date **Thursday, August 20, 2026**, which the app never retrieves online. It uses the separate harness
package `LOCAL-ONLY-RC15-PricingRetentionHarness-LOCALTEST-win-x64.zip` (41,731,586 bytes; SHA-256
`04547d8d82e7e6008fb3c778aecc64f51c5026c3521547f52c1a7b7a6c71d924`; source `780da37`; fix record §11). Use it with the app
package above (SHA-256 `080ffc57…d191`). The harness
runs the app's own Daily Admission and Fast Lane code and its own database code; only the official answers are
recorded synthetic ones and the clock is the harness's own (3:10 PM, then 10:15 PM on Aug 20). It makes no network
request and does not touch the Windows clock. Prices: Park Ticket **$49.00**, FAST LANE RESERVE **$49.00**, FAST LANE
ULTIMATE **$85.00** (synthetic).

Status of every item: **NOT EXECUTED.**

S.1 Sign in as **`CarowindsTest`**. Extract the app ZIP (this candidate) and the harness ZIP into two new folders.
Open the app once, wait until the window is ready, then close it completely.

S.2 Turn **Wi-Fi/network off** for the rest of this section (proves nothing comes from the internet; the app works
offline from its local data).

S.3 In the harness folder: Shift+right-click → **Open in Terminal**, run
`.\carowinds-pricing-retention-harness.exe seed`. Expected: Step 1 shows `Verified` for DatedAdmissionTickets and
DatedFastLane; Step 2 shows `Unavailable`; "Network requests: 0"; eight `[PASS]` lines including
"verified at Aug 20, 2026 3:10 PM ET" and "latest check 'Unavailable' at Aug 20, 2026 10:15 PM ET"; final line
**RESULT: PASS**. Screenshot the window. (It refuses, changing nothing, for another Windows user, while the app is
open, or if run a second time.)

S.4 Open the app → **Passdown** → select **August 20, 2026** → **Products & Pricing**: **Daily Admission** shows Park
Ticket $49.00 and **Fast Lane** shows FAST LANE RESERVE $49.00 and FAST LANE ULTIMATE $85.00, each with
**"Last verified Aug 20 at 3:10 PM ET — sales availability not confirmed."** No row reads as current or on sale.
Season Passes are **not** listed for this past date (existing rule: current catalog prices are never shown for past
dates); select **today** to see Season Passes listed as before.

S.5 Give **Park Ticket** a manual price of **$45** (Edit price). The row shows $45.00 as the price; the official $49.00
remains available through Reset to official (do not reset yet).

S.6 **Overview** → live PDF preview: DAILY ADMISSION Park Ticket **$45.00**, FAST LANE $49.00 and $85.00, and the footer
**"Some product pricing has not been recently verified."** Check Classic, Modern and Compact. Save PDF and Print
(Microsoft Print to PDF) show the same.

S.7 Optional (still offline): **Check Online** on August 20 — the prices and the "Last verified … 3:10 PM" lines stay.

S.8 **Close and reopen** the app (still offline). August 20 → Products & Pricing: the same three prices, the same
"Last verified Aug 20 at 3:10 PM ET" lines, Park Ticket still $45.00 effective. Note: because August 20 is past and now
has a saved draft, the app finalizes it at startup — **History** lists August 20, 2026, and its PDF shows the same
prices, $45.00 and the footer note. This synthetic History entry stays in the CarowindsTest account only.

S.9 Close the app; in the harness folder run `.\carowinds-pricing-retention-harness.exe verify` → eight `[PASS]` lines
and **RESULT: PASS**. Screenshot.

S.10 Light and Dark (Settings → Appearance): the "Last verified …" lines are readable in both. Turn the network back on
when finished. Never point Personal Sync at the real shared folder from this account.

**Evidence that confirms the fix:** S.3 and S.9 show the Aug 20 prices still stored as `Verified` at 3:10 PM after a
successful 10:15 PM answer that no longer lists the date (the same harness run against the unfixed source reports
`FAIL`: "last verified result is 'Unavailable'", "prices none"); S.4 shows the UI keeps them with the not-on-sale
wording; S.6/S.8 show the PDF and the restart keep them, with the override intact.

## Reporting

Send the PASS / FAIL / NOT EXECUTED list with notes and screenshots. Windows validation of this fix is **not complete**
until the owner confirms it.
