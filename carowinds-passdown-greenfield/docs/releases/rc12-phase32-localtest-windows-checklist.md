# RC12 local Windows test candidates (Phase 32 / 32A)

**LOCAL-ONLY. Not a release.** Not pushed, tagged, signed, or published; no update manifest. RC.11
(published prerelease `v0.1.0-rc.11`, tag → `573d17c`) and its frozen files are unchanged.

**Test the current candidate: `RC12-Phase32A-LOCALTEST`.** It supersedes `RC12-Phase32-LOCALTEST` (kept for
comparison only).

| | Current: RC12-Phase32A-LOCALTEST | Superseded: RC12-Phase32-LOCALTEST |
|---|---|---|
| Source commit | `9de3569` (Phase 32 + 32A typed Calendar markers) | `a409290` (Phase 32) |
| Version shown in the app | `0.1.0-rc.12-LOCALTEST.phase32a` | `0.1.0-rc.12-LOCALTEST.phase32` |
| Package | `artifacts/local-test/RC12-Phase32A-LOCALTEST/LOCAL-ONLY-RC12-Phase32A-LOCALTEST-win-x64.zip` | `artifacts/local-test/RC12-Phase32-LOCALTEST/LOCAL-ONLY-RC12-Phase32-LOCALTEST-win-x64.zip` |
| ZIP size | 76,786,217 bytes (73.2 MB); 288 files | 76,777,380 bytes (73.2 MB); 288 files |
| ZIP SHA-256 | `68dbe7feb05f7612f95028cbc386f81e1d78d25206949eb7a2f792499b167190` | `2972a346c8263f9ec2d53d45585bcb6a0ca58fa9b7a3cf4559c0c54cd9680987` |
| EXE inside the ZIP | `LOCAL-ONLY-RC12-Phase32A-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` | `LOCAL-ONLY-RC12-Phase32-LOCALTEST-win-x64\Carowinds.Passdown.Desktop.exe` |
| Mac verification | Full suite 5,252 passed; clean Release build 0/0; content validation on folder and ZIP; `RealReleaseZip` passed; stamped version read from DLLs; no manifest/signature; RC.10/RC.11 re-hashed unchanged | Same checks; suite 5,237 |

Both: assembly/file version `0.1.0.11` (source `<Version>` still `0.1.0-rc.11`, not bumped); database schema 18
(no migration). Both test identities sort above RC.11 and a future RC.12, so the updater never offers a
release over them. To switch candidates in the test account, extract the new ZIP to a new folder; the data
folder is shared by both within that account (same schema, no migration).

## Data isolation — use a separate Windows account

The app always keeps its database, official-data cache, settings, backups, logs, update staging and
Personal Sync configuration in the **signed-in Windows user's** `%LOCALAPPDATA%\Carowinds Passdown`. There is
no setting to change that, and running the portable EXE from a different folder does **not** isolate it.
Launching the test build under your normal account would use your real RC.11 data and your real sync folder.

So test in a separate local Windows account (one-time setup, no PowerShell):

1. **Settings → Accounts → Other users → Add account** → "I don't have this person's sign-in
   information" → "Add a user without a Microsoft account" → name it `CarowindsTest`.
2. Copy the ZIP to `C:\Users\Public\Downloads` (both accounts can see it).
3. **Start → your profile picture → CarowindsTest** to switch user (your normal session stays open).
4. In CarowindsTest: copy the ZIP to the Desktop, right-click → **Extract All**, open the extracted folder
   and double-click **`Carowinds.Passdown.Desktop.exe`**. If SmartScreen appears: **More info → Run anyway**
   (local builds are unsigned).
5. Test data lives only in `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`. When done, delete the
   account (Settings → Accounts → Other users → Remove) to discard everything.

Do **not** run `Enable Automatic Refresh.cmd` or set up Personal Sync with your real shared folder from this
build. Do not install any update from it.

## Manual checklist (all NOT YET TESTED)

Mark each Pass / Fail / Not executed. Two-computer or real shared-folder items stay **NOT EXECUTED** unless
actually performed.

### A. Startup and safety
- [ ] A1 The EXE launches from Explorer in the CarowindsTest account.
- [ ] A2 Settings → About this build shows `0.1.0-rc.12-LOCALTEST.phase32a (9de3569…)`; the extracted folder
      contains `LOCAL-ONLY-TEST-BUILD.txt`. Expected: "What's New in this version" says no notes are bundled
      (this build carries the RC.11 notes, which are never shown as another version's), and the packaged
      TESTER-GUIDE.txt / README still describe RC.11 — both are normal for a local test build.
- [ ] A3 In your normal account afterwards: RC.11 still opens, shows `0.1.0-rc.11`, and your Passdowns and
      notes are unchanged.
- [ ] A4 Test data is only under `C:\Users\CarowindsTest\AppData\Local\Carowinds Passdown`.
- [ ] A5 Settings → Application Updates → Check for Updates reports no newer version (RC.11 is older than
      the test identity) and installs nothing.

### B. Products → Pass Perks (after Check Online)
- [ ] B1 November 2026 (this month) lists **seven** rewards: Earn 2 FREE Bring-A-Friend Tickets; BOGO 50%
      Off Second Night at Carowinds Camp Wilderness; BOGO 50% Off Soft Pretzel; BOGO 50% Off Churro; Save 20%
      When You Spend $50+ On Retail; 20% Off Game Play; Buy Two, Get One FREE Hot Chocolate Mug.
- [ ] B2 Type tags read Visit-based reward / Passholder offer / Monthly perk; dates read e.g. "Visit by Nov 22 ·
      Redeem Dec 5 – Jan 3", "Book by Nov 30", "Valid all month (Nov 1 – Nov 30)", "Redeem Nov 27 – Jan 3".
- [ ] B3 Bring-A-Friend says "Requires 2 qualifying visits"; monthly perks say "Available to eligible
      passholders"; Camp Wilderness says "Promo code NOVPASS".
- [ ] B4 View details opens; earning, reward loading, dates and locations are separate; program-wide text is
      labelled "Program terms (apply to every perk)" / "Program FAQ", not as the perk's own condition.
- [ ] B5 "Official sources disagree" explains the Terms vs FAQ eligibility difference.
- [ ] B6 The three official links (park page, pass-perks-terms, perks-and-play) open in the browser.
- [ ] B7 "Program terms" link opens the program terms dialog.
- [ ] B8 December 2026 (once in the month list, or after Dec 1) says "Pass Perks for December 2026 have not
      been verified …" — never "no rewards".
- [ ] B9 No month before November 2026 appears with invented rewards.

### C. Calendar (Phase 32A typed markers)
Shapes: check-in-circle = qualifying visit, gift = redemption / use, clock = booking deadline.
- [ ] C1 November: check-circle on Nov 22 (last day for qualifying visits); gift on Nov 27 (Hot Chocolate Mug
      begins); clock on Nov 30 (last day to book Camp Wilderness); gift on Dec 5 (Bring-A-Friend redemption begins).
- [ ] C2 January 2027: Jan 3 shows one gift with a small "2" badge; clicking it lists both perks as
      "Redemption / use · Last day to redeem".
- [ ] C3 Markers sit at the same top-right spot in every marked cell; the day number and the lines below it
      are at the same position in marked and unmarked cells, including near month boundaries.
- [ ] C4 Hover a marker: tooltip reads "Pass perk qualifying visit date: …" / "Pass perk redemption date: …" /
      "Pass perk booking deadline: …". Narrator on a marked cell reads the same after the cell's details.
- [ ] C5 Clicking a marked date lists its perks, each starting with the type icon and label ("Qualifying visit ·
      …", "Redemption / use · …", "Booking deadline · …"), with View details.
- [ ] C6 The legend under the grid ("Pass Perks: Qualifying visit · Redemption / use · Booking deadline") shows
      while markers are on; it disappears when the setting is off.
- [ ] C7 Ordinary November days have no marker; the selected-date panel shows a collapsed "Valid all
      November (4)".
- [ ] C8 Settings → Calendar → Show Pass Perks on Calendar off hides markers at once; on restores them; the
      choice survives closing and reopening the app.
- [ ] C9 Hours, shows, notes and passdown status in day cells are unchanged.
- [ ] C10 At the narrowest window width you use, markers are not cut off (they shrink slightly only if a date
      ever has all three types — none do in November's official data).

### D. Refresh
- [ ] D1 Check Online updates the Pass Perks "checked …" time.
- [ ] D2 Changing Calendar months and the Products month picker does not show any checking/progress activity.
- [ ] D3 Disconnect the network, Check Online: "Unable to refresh; showing previously verified rewards (…)"
      and the seven rewards stay. Reconnect.
- [ ] D4 Each perk's details show "First listed … · Last verified …".

### E. Regression (disposable data only)
- [ ] E1 Associate Overview / Products / Shows / Notes navigation.
- [ ] E2 Shows selection and inclusion in the PDF.
- [ ] E3 Settings → Passdown Preferences defaults (Operational Notes on) apply to a new date.
- [ ] E4 Official product pricing, Current Deals and Season pass price history.
- [ ] E5 Notes and a saved/finalized Passdown appear in History.
- [ ] E6 Classic and Modern PDFs open.
- [ ] E7 ERT settings on a test date.
- [ ] E8 Personal Sync with a disposable empty folder only — NOT EXECUTED unless done; two-computer sync NOT
      EXECUTED.
- [ ] E9 Light and dark appearance.

### F. Windows appearance
- [ ] F1 100%, 125% and 150% display scaling: no truncated text, overlap, unreachable buttons or horizontal
      scrolling in the Pass Perks panel, details dialog, Calendar cells and selected-date panel.
- [ ] F2 Escape closes View details; focus returns to the link that opened it.
- [ ] F3 Keyboard only: Tab reaches the month picker, rows' View details, Program terms, the Calendar setting.
- [ ] F4 Narrator reads the Pass Perks rows, the month picker and "Pass Perk date: …" on marked days.

## After testing
Report Pass/Fail per item. Nothing here marks Windows testing as passed until you have run it. Publishing
RC.12 would need a real version bump (`<Version>`/`<FileVersion>`, release notes, tester guide, portable
README), a signed release build and the release checklist — none of that has been done.
