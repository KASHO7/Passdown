# Phase 29A — Morning Message Windows & Microsoft Teams validation

> **RETIRED (2026-10-23).** The Morning Message Builder, its formatted copy and Morning Message sync were removed by a targeted rollback; see `docs/morning-message-rollback.md`. This document is kept unchanged as a historical record. Nothing it describes exists in the current application.

Validation record for the Phase 29 Morning Message composer and formatted Teams copy
(`docs/phase29-morning-message-teams-copy.md`).

**Status: PREPARED, NOT VALIDATED.** The isolated Windows test build, the testing kit and every Mac-side
check are complete. **No Windows clipboard, Teams desktop or Teams-in-Edge check has been executed.** No
Windows host was available to this session, which ran on the macOS build host. Each such row below stays
**NOT EXECUTED** until someone runs it on the Windows test VM and records the result. Mac results are
synthetic-data evidence only and do not stand in for any Windows or Teams row.

Result values: **PASS** · **PASS AFTER FIX** · **PARTIAL** · **FAIL** · **BLOCKED** · **NOT EXECUTED**.
Evidence columns say where a result came from: **Mac-auto** (deterministic suite or a scratch/kit harness
on the macOS build host), **Win-clip** (real Windows clipboard), **Teams-desk**, **Teams-Edge**, **UI**
(Windows UI, DPI, keyboard), **User** (manual check by the product owner).

Nothing was pushed, published, tagged or uploaded. RC.10 was not packaged or frozen. No production source
changed in this phase.

## 1. Baseline (verified)

| Item | Expected | Actual |
| --- | --- | --- |
| Branch | `feature/passdown-redesign` | `feature/passdown-redesign` |
| Commit | `fb6cc67` | `fb6cc671eaf3bbba7a68568983fb588d749b4ade` |
| Version (`Directory.Build.props`) | `0.1.0-rc.9` | `0.1.0-rc.9`, file version `0.1.0.9` |
| Schemas | DB 17, Draft 4, Snapshot 2, Sync 1 | Unchanged (Phase 29 changed no persistence, migration, snapshot or sync source; see §8) |
| Deterministic tests | 4838 | **4838/4838** (below) |
| Release build | 0 warnings, 0 errors | **0 warnings, 0 errors** (`dotnet build CarowindsPassdown.slnx -c Release`) |
| Working tree at start | — | Clean apart from pre-existing `.DS_Store` entries and a staged `.claude/skills/pdf-design/SKILL.md`; none of them was touched or committed by this phase |

Deterministic suite (Release, `Category!=LiveSmoke`, the five real-Window UI classes run alone):
303 Domain + 1294 Application + 1089 Infrastructure + 525 Documents + 1475 Ui (mixed) + 26
`OperationsCalendarViewTests` + 113 `Phase15ERealWindowLayoutTests` + 2 `RealWindowStartupTimingTests` + 7
`SpellCheckNavigationLifecycleTests` + 4 `Phase21DMathEditorWindowTests` = **4838 passed, 0 failed**. No test
was added: Mac-side validation found no defect and no gap in coverage (§6).

## 2. Disposable Windows test build

| | |
| --- | --- |
| File | **`LOCAL-ONLY-phase29a-morning-message-win-x64.zip`** |
| Location (build host, gitignored) | `artifacts/local-test/phase29a/` (also inside the kit folder `artifacts/local-test/phase29a/LOCAL-ONLY-phase29a-test-kit/`) |
| Bytes | **73,405,550** |
| SHA-256 | **`c6e9f1464aa3683ee2ef71be209712248b401f2b826353e8be06d80e81d53b1e`** |
| Source commit | `fb6cc67` (Phase 29), branch `feature/passdown-redesign`; no source changes on top |
| Application version | `0.1.0-rc.9+fb6cc671eaf3bbba7a68568983fb588d749b4ade` in all six packaged assemblies (Desktop, Application, Infrastructure, Domain, Documents, Updater). About this build reads **`0.1.0-rc.9 (fb6cc67)`**. The frozen RC.9 reads `(e20d846)` |
| Shape | win-x64, self-contained, ReadyToRun, not trimmed, multi-file; same publish flags as RC.9 (`-c Release -r win-x64 --self-contained true -p:PlaywrightPlatform=win-x64`), updater published from source and copied in, `.pdb` removed |
| Contents | One top-level folder `LOCAL-ONLY-phase29a-morning-message-win-x64\`; 288 files, 288 entries. **Same file list as frozen RC.9** plus one marker file, `LOCAL-ONLY-TEST-BUILD.txt` ("not a release; test Windows account only; do not install updates; delete afterwards") |
| Executables | `Carowinds.Passdown.Desktop.exe`, `Carowinds.Passdown.Updater.exe`: `PE32+ executable (GUI) x86-64, for MS Windows` |
| Morning Message present | Phase 29 types are in `Carowinds.Passdown.Desktop.dll`, `…Documents.dll`, `…Infrastructure.dll`, `…Application.dll` (built from `fb6cc67`) |

### Why `Build-PortableRelease.ps1` was not used

`Build-PortableRelease.ps1` names its staging folder and ZIP from `<Version>`. With the version still at
`0.1.0-rc.9`, running it would **delete the frozen `artifacts/release-staging/Carowinds-Operations-0.1.0-rc.9-win-x64`
folder and overwrite `artifacts/releases/Carowinds-Operations-0.1.0-rc.9-win-x64.zip`** (it calls `Remove-Item -Force`,
which also removes read-only files). The same steps were therefore run by hand into
`artifacts/local-test/phase29a/` (publish → updater files → staging with `tools\`, the two `.cmd` helpers,
`README.txt`, `TESTER-GUIDE.txt`, `THIRD-PARTY-NOTICES.txt`, `licenses\` → marker file → `zip -r -X -D -9`).
The ZIP's `-D` flag omits directory entries, as RC.9's ZIP does. See finding F2.

### Audit (build host)

| Check | Result |
| --- | --- |
| Repository package validator (`ReleasePackageValidator`, the C# mirror of `Test-ReleasePackage.ps1`, `CAROWINDS_RELEASE_ZIP=… --filter RealReleaseZip`) | **PASS** (a bogus path makes it fail, so it really ran) |
| `unzip -t`; one top-level folder; fresh extraction `diff -rq`-identical to staging | PASS |
| File list vs frozen RC.9 staging | Identical apart from `LOCAL-ONLY-TEST-BUILD.txt` |
| Forbidden names (`.pdb`, `.pem`, `.key`, `.db*`, logs, `.DS_Store`, `__MACOSX`, `.dylib`/`.so`, `.update.json(.sig)`, PDFs, `appearance.json`, `sync-config.json`, `morning-message*.json`) | None |
| Production signing private key: 16-byte DER windows and PEM lines, over all 288 files (key read locally, never printed) | **0 matches** |
| Tokens (`ghp_`, `github_pat_`, `BEGIN … PRIVATE KEY`) | None |
| Developer paths (build user name, checkout folder name), text and binary | None |
| User data | None. No database, preferences, sync configuration or Morning Message files ship. A fresh profile is created only on first launch, in the Windows account that runs it |
| Updater settings | Unchanged. `appsettings.json` and `Infrastructure/Updates` have no diff between `e20d846` (RC.9) and `fb6cc67`. Production updater configuration was not modified. No GitHub release was created |
| Frozen artifacts | RC.9 and RC.8 ZIP, single-file EXE, `.update.json` and `.sig`: SHA-256 recorded before the build and re-verified unchanged after it (§10) |

### Profile isolation

The application always stores its data under `%LOCALAPPDATA%\Carowinds Passdown` (and updates under
`%LOCALAPPDATA%\Carowinds Operations Updates`), resolved from `Environment.SpecialFolder.LocalApplicationData`
(`AppPaths`). **There is no supported data-root override**, and none was added. Extracting the ZIP to another
folder does **not** isolate it.

Method: run the test build only in the **separate, disposable Windows test account on the test VM**
(checklist step 0). That account has its own `%LOCALAPPDATA%`, so the build cannot reach the working database
or any Personal Sync folder. Before the first launch the tester renames any earlier test data in that account
(`Carowinds Passdown` → `Carowinds Passdown.before-29A`; same for the updates folder). That is reversible.
Personal Sync is not configured. Automatic refresh is not enabled. Offered updates are declined. If the
account does not exist yet, creating a local user needs an administrator on the VM once; nothing else in the
kit needs admin rights.

## 3. Synthetic morning-message data

All of it is fictional and lives in `tools/phase29a-windows-kit/synthetic-input/`:

| Section | Content |
| --- | --- |
| Greeting | `Gooooood Morning!` |
| Today's Operations | Park hours from Check Online on the test account (real official source); projected attendance **12,345**; pre-solds **2,345**; ERT from configuration |
| DAP / Placement | `[DAP / placement chart: attach the image in Teams after pasting]`: a placeholder; the image is never part of the text. A synthetic image (`08-synthetic-dap-image.png`, coloured blocks, no text) is used for Copy Image |
| Reminders | A heading-like line (`Safety First`), two normal lines (one with an em dash), a blank line, two `- ` bullets (`&`, `<before>`), a blank line, a line with `@Jordan Testperson`, `Café`, an `https://www.carowinds.com/` link and an inert `javascript:alert(1)` |
| Ticket prices | **Mac harness:** three `SYNTHETIC …` products (Daily Admission $11.11, Fast Lane $22.22, Promotions $33.33) that exist only in the harness; the stale variant marks Fast Lane stale (last verified Oct 19). **Windows:** the real official prices from Check Online. No invented price ever enters a real catalog. The stale case on Windows is produced by a Check Online with the network off (checklist step 8) |
| Price note | `SYNTHETIC price note (not official).` |
| Tentative Break Plan | Four fictional names, times and coverage notes, with column spacing |
| Daily Feature | Question of the Day: `SYNTHETIC: Which coaster would you ride first — and why?` |
| OD Notes | `SYNTHETIC OD note: Ride 4 test cycle at noon. Not for the main message.` |

The copy logic reads ticket prices from the application's existing official data path: the Associate workspace
products through `AssociatePassdownDocumentModelBuilder`, the same path the PDF uses
(`MorningMessageComposer.TicketPrices`). The kit generator feeds synthetic products into that same path.

## 4. Composer on Windows

| # | Check | Expected | Evidence | Result |
| --- | --- | --- | --- | --- |
| 4.1 | Associate → Copy ▾ | Operations Summary, Today's Pricing, Operations + Pricing first, then Morning Message…, OD Notes | Mac-auto (`View_HasOneLabeledCopyMenu_ThreeOriginalItemsPreserved_ThenMorningMessageAndOdNotes`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.2 | Open Morning Message… | Overlay over Associate, not a new navigation section | Mac-auto (`MainWindow_HostsTheComposerAsAnOverlay_…`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.3 | Default order | Greeting, Operations, DAP, Reminders, Ticket Prices, Break Plan, Daily Feature | Mac-auto (`DefaultOrder_IsThePreferredWorkflow_DailyFeatureLast`, kit generator) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.4 | Edit greeting / reminders / break plan / OD Notes | Preview follows | Mac-auto (ViewModel tests) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.5 | Select price categories | Only selected sections; PDF selection unchanged | Mac-auto (`TicketPrices_…SelectionDoesNotChangeThePdf`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.6 | Enable/disable sections | Disabled section leaves the message; row keeps its preview | Mac-auto | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.7 | Move Up / Move Down / Reset order | Message follows; Reset restores the default with all sections on | Mac-auto (`MoveUpDown_…AndPersistsOrder`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.8 | Daily Feature last by default | Yes | Mac-auto | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.9 | Close and reopen composer | Text and settings kept | Mac-auto | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.10 | Restart the app | Greeting, order, sections, copy mode, price sections kept (`morning-message.json`); today's text kept (`morning-message-content.json`) | Mac-auto (`Greeting_IsRemembered_AcrossRestarts…`, `MessageText_IsKeptPerDate…`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.11 | Composer edits do not change the draft | Draft, PDF, History untouched | Mac-auto (`ComposerEdits_AreMessageOnly_NeverChangeTheDraft`, `Compose_DoesNotModifyTheWorkspaceOrDraft`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 4.12 | Opening the composer does not retrieve | No online check, no save, no date change | Mac-auto (`Open_ReadsUnsavedOnScreenEdits_WithoutSavingOrCheckingOnline`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |

## 5. Windows clipboard

Implementation under test: `AvaloniaClipboardService` (Avalonia 12.1.1 `DataTransfer`). One item carries
`DataFormat.Text` (CF_UNICODETEXT) and a **bytes** platform format `"HTML Format"` with the UTF-8 payload
from `CfHtml.Build`. Copy Plain Text uses `SetTextAsync` only. `TeamsClipboard.CopyAsync` falls back to plain
text when the formatted copy throws.

| # | Check | Evidence | Result |
| --- | --- | --- | --- |
| 5.1 | Plain text: correct text, section order, CRLF line breaks, no generated markup, no OD Notes, Daily Feature last | Mac-auto (composer tests, kit generator: all checks PASS for both variants); kit inspector run on the expected files: PASS | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.2 | Formatted copy places **both** CF_UNICODETEXT and `HTML Format` | Mac-auto (`CopyFormatted_PutsHtmlAndTheSamePlainText`, fake clipboard). Real Windows clipboard format list: Win-clip | Win-clip **NOT EXECUTED** |
| 5.3 | CF_HTML: `Version:0.9`, four 10-digit offsets, byte offsets into UTF-8, fragment markers exactly at StartFragment/EndFragment, EndHTML = payload length, no BOM, no trailing NUL | Mac-auto (`CfHtmlTests`; kit generator: `CfHtml.Parse` round-trip; kit inspector on the generator's payload: every CF_HTML check PASS, and two tampered payloads (shifted offset, injected `<script>`) correctly FAIL) | PASS (Mac-auto) · Win-clip **NOT EXECUTED** (whether Avalonia's Win32 backend stores the bytes unchanged can only be seen on Windows; the inspector reads the raw HGLOBAL and reports any bytes after EndHTML) |
| 5.4 | Unicode (em dash, `é`) | Mac-auto (generator, inspector) | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.5 | Paragraphs, bold headings, italic lines, bullet lists, hyperlinks | Mac-auto: fragment uses only `p, br, strong, em, ul, li, a`; 15 `strong`, 2 `li`, 2 `em`, 1 link in the stale sample | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.6 | No active scripts or unsafe markup; `javascript:` inert; user `<before>` and `&` encoded; `@Name` plain | Mac-auto (`TeamsRenderingTests`, generator, inspector) | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.7 | Plain and HTML carry the same words in the same order | Mac-auto (165 = 165 words, stale sample) | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.8 | Controlled clipboard failure, both formats: no crash, no false "copied", "Couldn't copy to the clipboard. Try again." in the error color | Mac-auto (`ClipboardFailure_ShowsShortError_NeverThrows`). Windows: kit `4 Clipboard failure test.cmd` holds the real clipboard open | PASS (Mac-auto) · Win-clip **NOT EXECUTED** |
| 5.9 | Formatted fails, plain succeeds: plain text copied, status "…as plain text (formatted copy wasn't available)" | Mac-auto (`FormattedCopyFailure_FallsBackToPlainText_AndSaysSo`). This partial failure cannot be produced on purpose on a real Windows clipboard without changing the app | PASS (Mac-auto) · Win-clip **NOT EXECUTED** (no safe manual trigger) |
| 5.10 | Notepad paste after Copy Formatted gives plain text; Word/Outlook paste keeps bold and bullets | User | **NOT EXECUTED** |

A valid HTML clipboard payload does not show that Teams keeps the formatting. That is §6–§7.

## 6. Microsoft Teams desktop (Windows 11, new Teams)

Draft in a chat with yourself or a private test chat. **Never sent.** Screenshots T1–T5.

| Test | Expected | Result |
| --- | --- | --- |
| Greeting | Correct text | **NOT EXECUTED** |
| Section headings | Bold | **NOT EXECUTED** |
| Reminders | Paragraphs and bullets retained | **NOT EXECUTED** |
| Ticket prices | Correct amounts and order; italic stale line / price note | **NOT EXECUTED** |
| Break plan | Readable, one line per person (exact column alignment not expected: Teams uses a proportional font) | **NOT EXECUTED** |
| Daily Feature | Last | **NOT EXECUTED** |
| OD Notes | Absent; pasted separately into a second draft | **NOT EXECUTED** |
| Hyperlinks | `https://www.carowinds.com/` clickable; `javascript:` plain text | **NOT EXECUTED** |
| `@Name` | Plain text, not an active mention | **NOT EXECUTED** |
| Images | No image from the message text; Copy Image pastes the synthetic picture only when used | **NOT EXECUTED** |
| Copy Plain Text | Same words, no formatting, no tags | **NOT EXECUTED** |

If Teams drops formatting, record exactly which elements fail. The words and order stay the same, and Copy
Plain Text stays available.

## 7. Microsoft Teams in Microsoft Edge

Same table, same draft rules, plus a large message (~40 extra reminder lines). Screenshots E1–E5.

| Compare | Teams desktop | Teams in Edge | Difference |
| --- | --- | --- | --- |
| Bold headings | NOT EXECUTED | NOT EXECUTED | — |
| Lists | NOT EXECUTED | NOT EXECUTED | — |
| Paragraph spacing | NOT EXECUTED | NOT EXECUTED | — |
| Hyperlinks | NOT EXECUTED | NOT EXECUTED | — |
| Unicode | NOT EXECUTED | NOT EXECUTED | — |
| Break-plan alignment | NOT EXECUTED | NOT EXECUTED | — |
| Large message | NOT EXECUTED | NOT EXECUTED | — |

Success in one client is not assumed for the other.

## 8. Existing Copy options and other copies

| # | Check | Evidence | Result |
| --- | --- | --- | --- |
| 8.1 | Operations Summary, Today's Pricing, Operations + Pricing: plain text **byte-for-byte** as before Phase 29 | Mac-auto: (a) `AssociateTeamsTextFormatterTests` (17 exact-string assertions) unchanged since before Phase 29 and passing; (b) **differential check**: the pre-Phase-29 formatter from `780ceac` compiled next to the current one in a scratch harness. Over 2,160 cases (5 product sets incl. stale, excluded, override and price-less products; 3 ERT variants; 4 attendance/pre-sold combinations; 3 hours states incl. Carolina Harbor; Daily Feature on/off; all three actions) with 1,728 non-empty outputs (110 distinct), the old output, the new output and the new message model's plain rendering are **identical in every case** | PASS (Mac-auto) · Win-clip/Teams **NOT EXECUTED** |
| 8.2 | Their formatted equivalents (with "Use formatted copy by default"): HTML added, plain unchanged, same words | Mac-auto (`ExistingCopyActions_FormattedDefault_AddHtml_PlainTextUnchanged`, `ExistingCopyActions_PlainDefault_StayPlainTextOnly`, `ExistingCopyActions_PlainIsUnchanged_FormattedHasSameWords`) | PASS (Mac-auto) · Win-clip/Teams **NOT EXECUTED** |
| 8.3 | Morning Message order does not affect the standalone options | Mac-auto by construction (the original actions use `AssociateTeamsMessageBuilder` over the PDF model and never read `MorningMessagePreferences.Layout`; differential above) | PASS (Mac-auto) · Windows **NOT EXECUTED** (checklist 7.3) |
| 8.4 | OD Notes copied separately (composer buttons and Copy ▾ → OD Notes; "No OD Notes for this date…" when empty) | Mac-auto (`OdNotes_NotInMainMessage_CopiedSeparately_FromComposerAndMenu`, `MenuOdNotes_WhenNoneEntered_SaysSo_CopiesNothing`) | PASS (Mac-auto) · Win-clip/Teams **NOT EXECUTED** |
| 8.5 | Individual sections | No per-section Copy button exists (finding F3); tested by leaving only one section enabled | Mac-auto (disabling sections) PASS · Windows **NOT EXECUTED** |
| 8.6 | Local test image (Copy Image) | Mac-auto: missing file reported, image never in text. Real bitmap on the Windows clipboard and Teams paste: NOT EXECUTED | PARTIAL (Mac-auto only) · Windows **NOT EXECUTED** |

## 9. Real workflow (user)

Checklist steps 2–7 follow the normal order: open today's Associate Passdown → enter values → composer →
review → reminders → prices → break plan → Daily Feature → Copy Formatted → paste into a Teams test draft →
add real @mentions and DAP images by hand in Teams → copy OD Notes separately → second draft.

| Check | Result |
| --- | --- |
| Workflow completes end to end on Windows + Teams | **NOT EXECUTED** (User) |
| Quicker and less repetitive than assembling each section by hand | **NOT EXECUTED** (User judgement) |

No automatic Teams posting exists or was added.

## 10. UI, accessibility and display scaling

| # | Check | Evidence | Result |
| --- | --- | --- | --- |
| 10.1 | Light / Dark / System appearance | Mac-auto (`View_UsesOnlyThemeTokens_DefinedForLightAndDark`); Phase 29 headless Skia screenshots (`tools/visual-fixtures` `Associate-MorningMessage`, light and dark) | PASS (Mac-auto) · UI **NOT EXECUTED** |
| 10.2 | 100% / 125% / 150% scaling; small and maximized window; no clipping or overlap | No Windows DPI check possible on the build host | **NOT EXECUTED** |
| 10.3 | Keyboard-only, tab order, visible focus, Enter/Space, Escape closes | Mac-auto (`View_EscapeCloses_GreetingIsTheInitialFocusTarget`, named Move Up/Down buttons, every control named) | PASS (Mac-auto, markup) · UI **NOT EXECUTED** |
| 10.4 | Status announcements | Status `TextBlock`s carry `AutomationProperties.LiveSetting="Polite"`; Narrator not run | PARTIAL (markup only) · UI **NOT EXECUTED** |

## 11. Persistence and regression

| # | Check | Evidence | Result |
| --- | --- | --- | --- |
| 11.1 | `morning-message.json` preferences persist | Mac-auto (`Preferences_RoundTrip_…`, malformed/partial file tests) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 11.2 | `morning-message-content.json` per-date content | Mac-auto (`Content_RoundTripsPerDate_AndDatesAreIndependent`, `Content_SavingEmpty_RemovesTheDate`) | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 11.3 | Retention: most recent 60 dates | Mac-auto (`Content_KeepsOnlyTheMostRecentDates`); impractical to reproduce by hand | PASS (Mac-auto) · Windows **NOT EXECUTED** |
| 11.4 | Composer changes don't alter draft data; no duplicate notes | Mac-auto (`ComposerEdits_AreMessageOnly…`, `PassdownNotes_AreOfferedButNeverAddedUntilChosen`) | PASS (Mac-auto) |
| 11.5 | PDF output, Frozen History, official price history, Personal Sync unchanged | Phase 29 changed no PDF, persistence, migration, snapshot, History, price-history or sync source (`git diff 780ceac fb6cc67 -- src`: 23 files, all Morning Message/Teams/clipboard/DI/composer); the full suite passes, including the PDF, History, price-history and sync tests | PASS (Mac-auto) |
| 11.6 | Morning Message content stays local, not in Personal Sync | Mac-auto (`DefaultPaths_AreLocalAppData_NotTheSyncFolder`); not added to sync in this phase | PASS (Mac-auto) |

## 12. Findings

No defect in Phase 29 production code was found by the Mac-side validation. No production source changed, so
no rebuild under a new filename was needed.

| ID | Kind | Finding | Action |
| --- | --- | --- | --- |
| F1 | Product observation | Text the user types (Reminders, Break Plan, DAP, OD Notes) has **no bold or italic markup**. A heading-like line inside Reminders (`Safety First`) pastes as an ordinary line. Only the app's own section headings and labels are bold, and only the generated stale/price-note lines are italic. This matches the Phase 29 specification and is not a defect | Owner decision for a later phase (e.g. treat a line ending in `:` as a bold sub-heading). Not implemented |
| F2 | Process hazard | `Build-PortableRelease.ps1` derives its staging folder and ZIP names from `<Version>` and removes existing ones with `-Force`. Run while the version is still `0.1.0-rc.9`, it would destroy the frozen RC.9 staging folder and ZIP | Not run this phase (manual LOCAL-ONLY build instead). **Fixed** for the Phase 29 + 30 kit: frozen-release guard and `-LocalTestLabel` mode (`docs/phase29-30-windows-validation-kit.md` §2) |
| F3 | Observation | There is no per-section Copy button. "Individual sections" means disabling the others. The checklist tests it that way | None |
| F4 | Observation | The test build has the same version (`0.1.0-rc.9`) and production updater settings as RC.9 (unchanged by design). If a newer release is ever published, the test build would offer it | Checklist: decline updates; delete the test folder afterwards |

## 13. Windows testing kit

Committed: `tools/phase29a-windows-kit/` (`README.md` explains each file). Ready to copy to the VM:
`artifacts/local-test/phase29a/LOCAL-ONLY-phase29a-test-kit/` (gitignored), which holds the test ZIP,
`SHA256SUMS.txt`, `PHASE29A-TEST-CHECKLIST.txt`, `synthetic-input\`, `expected\` and `scripts\`. The same folder
as one file for transfer: `artifacts/local-test/phase29a/LOCAL-ONLY-phase29a-test-kit.zip` (72,773,941 bytes,
SHA-256 `2014f0414e394abb6d2ae9c397ce5b7df3d3afc1b6c1e339e0bdef3d2c2fd676`). The test ZIP's own hash is checked
separately on the VM (checklist 1.2).

- **Checklist** (`PHASE29A-TEST-CHECKLIST.txt`): safety and isolation first, install and hash check, About this
  build, entering the fake values, composer steps, clipboard checks, Teams desktop, Teams in Edge, the original
  Copy options, the stale-price check, appearance/DPI/keyboard, finishing up, a results table, troubleshooting
  and how to report failures. No source code; no administrator rights (except creating the test account,
  which already exists).
- **Clipboard inspector** (`scripts\1–3 *.cmd` → `Inspect-TeamsClipboard.ps1`): reads the real clipboard and
  writes a PASS/FAIL report plus the raw payloads to `Desktop\Phase29A-results\`. Read-only.
- **Failure test** (`scripts\4 Clipboard failure test.cmd` → `Hold-Clipboard.ps1`): holds the clipboard for
  20 s.
- **Expected outputs** (`expected\`): generated from `synthetic-input\` by `tools/phase29a-windows-kit/generator`
  through the real composer.
- **Screenshots to capture**: S1 About this build · S2 Copy menu · S3 composer · S4 Notepad paste · S5 Word paste
  · T1–T5 Teams desktop (top, bottom, plain, OD Notes, image) · E1–E5 the same in Edge · P1 stale price · U1–U5
  light/dark/125%/150%/small window.
- **Reporting**: zip `Desktop\Phase29A-results`, the screenshots and `RESULTS.txt`. Everything is fake data.

The PowerShell scripts were parse-checked with PowerShell 7.6 on the build host (installed as a local tool in
the session scratchpad, not system-wide). Their Windows-only clipboard calls have not run anywhere yet.

## 14. Frozen artifacts

SHA-256 of every RC.9 and RC.8 artifact in `artifacts/releases/` (ZIP, single-file EXE, `.update.json`,
`.sig`) was recorded before the test build and re-checked after it: **unchanged**. The RC.9 values still match
`docs/release-manifest.md` (ZIP `e1ad521f…1558`, EXE `5fa0769f…faa9`, manifest `936b196c…160f`, signature
`af82eaf5…93e2`). `artifacts/release-staging/Carowinds-Operations-0.1.0-rc.9-win-x64` was read for comparison
only.

## 15. RC.10 readiness

**Not ready to recommend.** The source is in good shape on the Mac side: 4838/4838 tests, a clean Release
build, the original copies byte-identical, valid CF_HTML. But the reason Phase 29 kept **Plain Text** as the
default copy mode — formatted pasting into Teams is unverified — still holds. Before packaging RC.10:

1. Run the kit on the Windows VM and fill in §5–§11 (at minimum: 5.2, 5.3, 5.8, the Teams desktop table, the
   Teams-in-Edge table, 8.1–8.2 in Teams, 10.2).
2. If Teams keeps bold/bullets/paragraphs in both clients, consider making Formatted the default. Otherwise
   keep Plain Text as the default and record which elements Teams drops.
3. Decide F1 (bold sub-headings in user text) and add the F2 guard before building RC.10.
4. RC.10 also carries the `780ceac` price-history fold fix, so its own release preparation (version bump,
   packaging, signing, manifest) is a separate phase.
