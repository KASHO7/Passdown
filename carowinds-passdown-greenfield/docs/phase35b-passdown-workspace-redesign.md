# Phase 35B — Associate Passdown workspace and PDF preview redesign

Status: **implemented locally — awaiting Windows visual and functional review** (checklist:
`docs/releases/rc14-phase35b-localtest-windows-checklist.md`). Phase 35C (Products & Pricing) is **not started**.
The Manager workflow stays deferred (CLAUDE.md).

## 1. Repository baseline

| Item | Value |
|---|---|
| Branch / starting HEAD | `feature/passdown-redesign` at `beba3f8` (Phase 35A-6) |
| Phase 35A | Shell, sidebar, tokens and Interface animations — approved by the product owner after Windows testing (35B brief). Implementation record: `docs/phase35-unified-ui-modernization.md` |
| Release status | **RC.13 published and frozen** (`v0.1.0-rc.13`, tag → `d784c60`; frozen files under `artifacts/releases`). Next candidate identity: **rc.14** |
| Source version | `0.1.0-rc.13` (`Directory.Build.props`) — not changed by this phase |
| Database schema | 18 — no migration |
| Personal Sync | `SyncSchemaVersion` 1, draft payload schema 4, finalized snapshot schema 2 — unchanged |
| Deterministic tests before | **5,619 / 5,619**, 0 skipped, re-run on `beba3f8` (Domain 462, Application 1,378, Documents 484, Infrastructure 1,309, Ui mixed 1,804, OperationsCalendarView 26, Phase15ERealWindowLayout 134, RealWindowStartupTiming 2, SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, Phase33PreviewWindow 9) |
| Build before | Release, 0 warnings, 0 errors |
| Working tree | pre-existing unrelated changes left untouched: `.DS_Store` files, staged `.claude/skills/pdf-design/SKILL.md`, staged `tools/**/.DS_Store` |
| Phase 35A test ZIP | the record names `artifacts/local-test/RC14-Phase35A-LOCALTEST/LOCAL-ONLY-RC14-Phase35A-LOCALTEST-win-x64.zip`; only its `staging/` and `publish/` folders remain there now (the ZIP was moved out). That folder was not touched |

## 2. Approved reference

`carowinds_passdown_overview_phase35b_reference.html` — iCloud Drive › Carowinds Operations, 81,248 bytes, SHA-256
`35945565aeada7fe35b5b2f7dc7eb29ada19293077b436c686fc589ecd136ae5`. Used as the authority for visual direction only:
its prices, schedules, ride names, example checked states, "Admissions" label, illustrative PDF and simplified
Actions menu / preview header are **not** application data or behaviour.

What the reference specifies: one white toolbar (title · grouped date bar · quiet "Draft · saved" text · one primary
Create PDF · an Actions menu); a horizontal tab row (Overview / Products & Pricing / Shows / Notes, accent underline,
quiet meta text on the right); the editor as a white sheet (max ≈1,100 px) on a neutral workspace; four groups
separated by hairlines — Park Hours & Gates (three columns, two override checkboxes with conditional fields, a tinted
official-events note), Attendance & Early Ride Time (two count fields + ERT with an Edit Attractions link), Daily
Feature (type selector, formatting tools, multiline text, rendered preview line), Include in Passdown (two-column
checkbox grid); a visible divider with a grip; the document preview to the right.

## 3. Existing → new control mapping

| Before (Phase 22D–34) | Phase 35B |
|---|---|
| Title row: title · official freshness · auto-check note · Draft + save chip + Retry Save | Toolbar: title · date bar · Draft + save chip + Retry Save. Official freshness and the auto-check note moved to the tab row's right side (`WorkspaceMeta`) |
| Action bar: ‹ date › Today · Check Online · Create PDF · Classic · Print · Copy ▾ · Hide Preview · More ▾ | Date bar (‹ date › Today, one grouped control) · **Create PDF** · **Actions ▾**. Show Preview is on the bar only while the preview is hidden |
| Check Online (toolbar) | Actions → **Check Online (refresh official information)** — same command, name "Check this date online" |
| Print (toolbar) | Actions → **Print…** (shows Ctrl+P), **Ctrl+P** anywhere in the workspace, and the preview's own Print — one command |
| Save PDF (preview only) | Preview's Save PDF **and** Actions → **Save PDF…** — one command |
| Copy ▾ → three entries | Actions → **Copy for Teams: Operations Summary / Today's Pricing / Operations + Pricing** — same command and parameters |
| More ▾ → PDF Style ×3 · Season Price History | Actions → **PDF Style: Classic / Compact / Modern** (radio) · **Season Price History…**; the style in use is also shown as "Classic PDF style" beside the tabs and in Create PDF's tooltip |
| Hide / Show Preview (toolbar) | Actions → Hide/Show Preview · the preview's Hide Preview · toolbar Show Preview while hidden |
| Left rail (Overview / Products / Shows / Notes + count) | Section tabs (Overview / **Products & Pricing** / Shows / Notes + count) over the editor |
| Overview: one bordered hero panel + a separate Include panel | One white **sheet**, four hairline-separated groups, no nested cards |
| PARK HOURS headline + North Gate / Avenue rows | **Park Hours & Gates**: Main Park Hours (official headline, status dot, badge, waterpark, verification, stale note) · North Gate · Avenue Entrance (effective time, Override chip, override box, Reset, error) |
| HOURS OVERRIDES group (bottom of the panel) | Manual hours override / Private buyout checkboxes inside Park Hours & Gates; their Open / Close / Details fields appear below only while checked |
| Official events note (inside Include in Passdown) | Tinted source note at the bottom of Park Hours & Gates (`OfficialEventsBox`) |
| ATTENDANCE (Projected / Pre-solds) beside the hours | **Attendance & Early Ride Time**: Projected Attendance · Pre-solds · Early Ride Time (rides text, "Your setting", **Edit Attractions**) |
| EARLY RIDE row + Edit | Edit Attractions opens the same Monthly Default / Date Override editor, full width below the group |
| DAILY FEATURE (type · ∑ Math · text · preview) | **Daily Feature**: Feature type · Formatting ∑ Math · text (taller) · rendered math preview (unchanged control) |
| Include in Passdown panel (wrap of checkboxes) | **Include in Passdown** group: two-column checkbox grid, same eight options in the same order |
| Transparent 6 px splitter | Visible 6 px divider track with a centred grip; tinted on hover and on keyboard focus |

## 4. Implementation decisions

1. **One file of markup, no view-model rewrite.** Every change is in `AssociateView.axaml` (layout and local
   styles) plus one motion style in `PassdownStyles.axaml`. `AssociateViewModel`, `PassdownPdfPreviewViewModel`,
   services, documents and data are untouched; every binding the view had is still bound (Phase 22D's binding
   inventory test passes unchanged). The code-behind is unchanged.
2. **High-frequency actions.** Create PDF stays the one primary button. The brief's "abbreviated Actions menu"
   became one complete, flat menu (Print, Save PDF, three Copy options, preview, PDF style, Check Online, Season
   Price History) — every action is one open + one choice, the same count as the old Copy ▾ and More ▾ menus. Print
   is additionally on the visible preview toolbar and on **Ctrl+P**; Show Preview is on the bar whenever the preview is
   hidden, so a hidden preview is never hard to get back. The menu is flat because nested `MenuItem`s break headless
   construction (Phase 18F/21B rule).
3. **Status semantics.** The chip still reads the real states ("Unsaved changes", "Saving…", "Saved on this computer
   at 2:14 PM", "Save failed — changes not saved"), prefixed "Draft". Nothing says "synced" or "verified".
4. **Section tabs over the editor only.** In the narrow preview-first layout the editor and its tabs step aside
   together (as the rail did), so a tab can never be pressed while its section is hidden. Tabs are plain Buttons with
   a bound `active` class (not `TabControl`, which needs the dispatcher at load); `AssociateViewModel.SelectedSection`
   remains the single source of truth, so Dashboard/Calendar "Open Passdown" deep links still work.
5. **Official vs manual stays visible.** "Official", "Entered here" and "Your setting" tags were kept (subtly) on
   the groups; official hours are read-only text, never an input. The reference shows hours as inputs — intentionally
   not copied.
6. **Daily Feature tools.** The existing ∑ Math picker (symbols, fractions, roots, templates, LaTeX entry) is kept
   instead of the mockup's five static buttons; the rendered preview still appears only when math is present.
7. **Early Ride Time.** The rides are shown as the existing status text (rides list, "None", or "Not configured
   for …") rather than static pills, so nothing is shown that the resolver did not produce.
8. **Responsive without code.** The toolbar's left side wraps (title / date / status) while Create PDF and Actions
   keep their own column; the date text trims instead of overlapping, and the date's minimum width (which keeps ‹ ›
   still while stepping) applies only through a `ContainerQuery` when the toolbar is ≥ 1,000 px. Override and count
   boxes use capped star columns so they stay left-aligned. Narrow windows keep the Phase 33 one-pane behaviour.
9. **Motion.** No new infrastructure: one `BrushTransition` on the tab underline under the Phase 35A
   `Window.motion` scope. With Interface animations Off or Windows reduced motion the class is absent and no transition
   exists. Selection, data, saves and preview work never wait on it. No new debounce or timer.

## 5. Layout changes

- Toolbar 16 px padding, 17 px title, grouped date bar (30 px controls, tinted), quiet status, primary + Actions.
- Tab row 40 px, 20 px gaps, 2 px accent underline + SemiBold for the active tab, neutral underline on hover.
- Sections scroll on the app background with 16 px padding; Overview sheet: white, 1 px border, 6 px radius,
  28 × 22 px padding, max 1,100 px, centred.
- Group headings 15 px SemiBold; field labels 12 px SemiBold secondary; values 15–16 px SemiBold.
- Divider: `PassdownDividerBrush` track, `PassdownBorderStrongBrush` 2 × 36 px grip, `PassdownSelectedBackgroundBrush`
  on hover, `PassdownAccentBrush` on keyboard focus.
- All colours are existing theme tokens (Light and Dark); no literal colours.

## 6. Functionality intentionally preserved

Selected-date loading through the one local-only path (arrows, date popup, Today; never online) · autosave and the
save chip · Retry Save · manual hours, private buyout and gate overrides (LostFocus/Enter commit, validation,
warnings, Reset) · ERT monthly default and date override editor · Daily Feature types, spell check excluding math,
∑ Math insertion and the rendered preview · all eight Include in Passdown options, their defaults and the seasonal
Haunt rule · Products, Shows and Notes sections (markup unchanged apart from their container row) · Create PDF ·
Print / Save PDF of the latest edits · the three Copy for Teams options · PDF Style · Season Price History · Check
Online · the multi-page warning and the feedback stack · the Phase 33/34 preview (real QuestPDF + PDFium page,
fit width/page, zoom, pages, refresh, Updating chip, 34B scheduling, stale-generation and date guards) · divider
keyboard resizing and the remembered width · Hide/Show Preview and its saved preference · narrow single-pane layout.

## 7. Visual comparison (Avalonia, Skia headless, synthetic data — not Windows DPI evidence)

Renders: `tools/visual-fixtures`, `Workspace-*`, `Popup-Actions`, `LiveSwitch-Actions` and `Popup-DatePicker`
scenarios, Light and Dark, at 1920×1080, 1536×960, 1280×720 and 960×640 (82 captures + popups). States covered:
preview visible / hidden, expanded / collapsed sidebar, focused input, focused section tab, Products tab selected,
manual hours + buyout + gate override open, ERT editor open, long Daily Feature scrolled to the end, Actions open,
theme switched with Actions open. Geometry report: **no page-level horizontal overflow and no button past the right
edge in any capture.** One report line notes 20 px inside the Daily Feature TextBox's own template scroller with a very
long wrapped text at 1280×720 — the text wraps correctly on screen; it is not page overflow.

Reference vs implementation (reference rendered with headless Chrome at the same sizes):

| Area | Match | Intentional difference |
|---|---|---|
| Toolbar | Title, grouped date bar, quiet Draft status, Create PDF, Actions ▾ | Relative-day chip ("Today") kept inside the date; Show Preview appears while hidden |
| Tabs | Same four tabs, underline, right-side meta | Meta shows the real PDF style and official freshness, not "1-page preview illustration" |
| Sheet | White sheet, hairline groups, same order and headings | — |
| Park Hours & Gates | Three columns, two checkboxes, conditional fields, events note | Official hours are text (not inputs); gate override boxes sit under each effective time; "North Gate" label (no "(Turnstiles)") |
| Attendance & ERT | Two count fields + ERT column with Edit Attractions | Rides as text, not pills; source tags kept |
| Daily Feature | Type selector, Formatting, multiline text, preview | ∑ Math picker instead of five static buttons; preview only when math is present (as before) |
| Include in Passdown | Explanation + two-column checkbox grid | Real options and defaults (SCarowinds / Haunt appears in season) |
| Preview | Divider with grip, white page on a neutral desk | Full Phase 33 toolbar kept (Print, Save PDF, pages, zoom, Fit width/page, refresh, Hide Preview) |
| Shell | — | Native Windows title bar and sidebar without an "Admissions" heading (Phase 35A decisions) |

## 8. Accessibility

- Every button, checkbox, text box, combo box and menu item on the toolbar, tabs and Overview has an accessible name
  (test-enforced); visible labels are contained in names ("Edit Attractions for Early Ride Time").
- The active tab announces "Current section"; the active state is underline + weight, never colour alone.
- Keyboard order (test-enforced): Previous day → Operating date → Next day → Today → Create PDF → Passdown actions →
  Overview → Products → Shows → Notes → first Overview field. Enter/Space opens Actions; arrows move inside it.
- Focus is visible on inputs, tabs and the divider (accent tint on keyboard focus). Ctrl+P prints.
- Status, freshness, auto-check and print/copy results remain live regions where they were before.

## 9. Performance (macOS Arm64, headless Skia — development evidence, not Windows results)

Method: a scratch window-level harness (not committed; same approach as Phase 35A) that builds the real `MainWindow`
over the Ui.Tests fakes with the **real** QuestPDF export, PDFium rasterizer, thread-pool preview scheduler and the
Phase 34B quiet window — no injected delays — at 1536×960. "Before" = `beba3f8` exported with `git archive`; the same
harness code runs against both trees; runs alternate before/after. Each latency is measured to the new page image plus
one rendered frame.

| Measure (median) | Before `beba3f8` (round 1 / 2) | Phase 35B (round 1 / 2) |
|---|---|---|
| Checkbox (Include Fast Lane) → new page + frame, n=40 | 57.0 / 57.6 ms | 54.8 / 57.5 ms |
| Daily Feature text edit → new page + frame, n=40 | 58.4 / 57.8 ms | 56.2 / 57.3 ms |
| Section switch + frame, n=120 | 2.4 / 2.3 ms | 2.5 / 2.3 ms |
| Preview regenerated by a section switch | never | never |
| Date arrow → editor ready + frame, n=30 | 46.6 / 46.5 ms | 46.6 / 48.9 ms |
| Date arrow → that date's page, n=30 | 66.3 / 67.7 ms | 68.8 / 69.0 ms |
| Passdown full re-layout (width change) + frame, n=200 ×3 | 6.7 / 7.0 / 7.0 ms | 6.7 / 6.8 / 7.2 ms |
| Sidebar toggle on Passdown + frame, n=200 ×3 | 4.4 / 4.6 / 4.7 ms | 4.5 / 4.5 / 5.1 ms |
| Window + navigate + first page + frame, n=10 | 109.4 / 108.3 ms | 117.5 / 117.3 ms |

**Investigated: time to first preview.** The 10-window medians differed by ≈8 ms, so it was re-measured with a full GC
between fresh windows and before/after processes interleaved (4 × 12 windows each): before 106 / 210 / 207 / 149 ms,
after 218 / 215 / 136 / 226 ms — both builds are bimodal on this Mac (≈105–150 or ≈210 ms) and overlap completely. A
variant without the `ContainerQuery` was not faster. The steady-state re-layout of the new page is identical to the
old one (table), so the first-window spread is environmental, not a layout regression. Windows first-open
responsiveness is checklist item G13–G14.

**Memory.** One window, six cycles of 200 section/page navigations + 20 date switches each, live managed heap after a
full GC: before 68.8 → 84.5 → 86.6 → 88.8 → 91.5 → 93.0 → 95.3 MB; Phase 35B 72.6 → 77.5 → 92.2 → 95.0 → 96.4 → 98.5 →
101.3 MB. The per-cycle slope is the same (≈2.2 vs ≈2.3 MB; pre-existing, not introduced here); the new page's live
tree is ≈4–6 MB larger. No new leak. (A first multi-window probe showed ±40 MB swings — closed windows from earlier
rounds still being collected — and was replaced by this single-window measurement.)

No new timers, debounces or animations sit on the edit → preview path; the Updating chip is unchanged (Phase 34B-1).

## 10. Automated testing

Deterministic suite (Release, LiveSmoke excluded, six run-alone classes): **5,655 / 5,655, 0 failed, 0 skipped**
(+36): Domain 462, Application 1,378, Documents 484, Infrastructure 1,309, **Ui mixed 1,825** (+21),
OperationsCalendarView 26, **Phase15ERealWindowLayout 134** (one case replaced, one added), RealWindowStartupTiming 2,
SpellCheckNavigationLifecycle 7, Phase21DMathEditorWindow 4, **Phase33PreviewWindow 24** (+15, the 35B real-window
tests are a `partial` of this run-alone class, so the partition itself is unchanged). Clean Release build: 0 warnings,
0 errors.

New — `Phase35BPassdownWorkspaceTests` (mixed, 21): toolbar structure and single primary action; quiet draft status
that never claims sync; Show Preview only while hidden; the flat Actions menu binding all 11 earlier actions in order
(Ctrl+P shown); one Ctrl+P binding; headless Actions menu names, keyboard reachability and separators; tabs over the
editor only, four `sectionScroll` sections; active tab = underline + weight + "Current section"; workspace meta (PDF
style, freshness, auto-check live region); Overview = one sheet, four groups in order, no nested panels; Park Hours &
Gates bindings, tags and the events note; conditional override/buyout fields with LostFocus/Enter commit; attendance
regrouping, Edit Attractions and the full ERT editor; Daily Feature type, spell-check, math picker tab order and
preview; Include in Passdown — all eight options in order, section switches only; accessible names on every
interactive control in the toolbar/tabs/Overview; divider track, grip and focus/hover styles; tokens defined for both
themes; motion only under `Window.motion`; section switches never regenerate the preview or check online; date
arrows load locally only.

New — `Phase33PreviewWindowTests` partial `Phase35B_*` (real window, 15): tabs switch sections without rebuilding or
hiding the preview and announce the current section; each section keeps its scroll across a round trip; toolbar, tabs
and Overview fit without overlap at 1920×1080 Light, 1536×960 Dark, 1280×720 Light and Dark, 960×640 Dark; override
and buyout fields appear only while checked; Edit Attractions opens the real ERT editor; an Include checkbox changes
the draft and the next preview; Print and Save PDF from Actions after an unsaved edit contain that edit; print right
after the date arrow is for the new date; keyboard order across toolbar → tabs → Overview; divider/grip visibility and
Show Preview on the toolbar while hidden; tab underline transition exists only under the `motion` class.

Updated to the 35B layout without weakening intent (each keeps an equivalent or stricter check):
`Phase22DAssociateWorkspaceTests` (one accent action — Check Online asserted as an explicit Actions item; full action
order including the menu; save vs official status still separate, now toolbar vs tab meta; feedback stack end marker;
rail → four section tabs), `Phase21BAssociateNavigationTests` (toolbar names + Actions item names; PDF style label),
`Phase22FSettingsAccessibilityTests` (tab `ItemStatus`), `Phase31DPassdownPreferencesTests` (tab order of sections),
`Phase33PreviewMarkupTests` (tabs inside the editor pane; Actions Print + Ctrl+P share the preview's command),
`AssociateViewAccessibilityTests` (Check Online name via the Actions menu; "Edit Attractions for Early Ride Time"),
`AssociateViewModelCopyForTeamsTests` (the three Copy items in the Actions menu), `Phase15ERealWindowLayoutTests`
(toolbar names + Actions items reachable at four sizes; one popup-theme case for the merged Actions menu instead of
separate Copy/More; Park Hours then Attendance on one sheet — above the fold at 1920×1000 and 1280×860, one scroll
away at the 960×640 minimum, see §12).

Visual review harness: `tools/visual-fixtures` gained `Workspace-*`, `Popup-Actions` and `LiveSwitch-Actions`
scenarios (the Copy/More popups became Actions) and names the ScrollViewer in overflow reports.

## 11. Windows validation

**Windows test build** (local only, not a release): `artifacts/local-test/RC14-Phase35B-LOCALTEST/LOCAL-ONLY-RC14-Phase35B-LOCALTEST-win-x64.zip`,
75,999,023 bytes, SHA-256 `c0e925c9fbde305d12962c51a47d87dffa5f9db9385810ee9cb3b5a88d25a84f`, 308 files, built from `1677781`
with `Build-PortableRelease.ps1 -LocalTestLabel RC14-Phase35B-LOCALTEST -LocalTestVersion 0.1.0-rc.14-LOCALTEST.phase35b`,
shown as `0.1.0-rc.14-LOCALTEST.phase35b`. Script validation (folder and ZIP) and `RealReleaseZip` passed; all 55 earlier
release and local-test files re-hashed unchanged. Not pushed, tagged, signed or published; no update manifest.
Checklist (32 items, isolated `CarowindsTest` account, synthetic data): `docs/releases/rc14-phase35b-localtest-windows-checklist.md`.
**Windows validation: NOT EXECUTED.** Phase 35B is not Windows-validated until the product owner runs the checklist.

## 12. Known limitations

1. Headless renders use approximate fonts; 100 / 125 / 150 % scaling and Segoe UI metrics must be checked on Windows.
2. At the 960×640 minimum, the Attendance group can start below the fold (Park Hours & Gates with its events note
   comes first, as approved); it is one scroll away. With real fonts at 960×640 it starts at ≈490 px.
3. The ERT rides are shown as text, not pills (no new view-model surface added in this phase).
4. The Phase 33 preview toolbar is unchanged in content (wraps to two rows in a narrow preview pane).
5. Tab keyboard navigation is Tab/Shift+Tab (buttons), not arrow keys within a tab list.

## 13. Phase 35C dependencies

- Products & Pricing still uses its Phase 31C/31D panels inside the new tab and section scroller (unchanged markup).
  35C should adopt the `sectionScroll` container and decide whether Products also sits on an `editorSheet`.
- The `Button.sectionTab`, `Border.editorSheet`, `Border.overviewSection`, `Border.sourceBox`, `TextBlock.overview*`
  styles live in `AssociateView.axaml`; promote them to `PassdownStyles.axaml` if 35C–35F reuse them.
- The price popover planned for 35C should use `PassdownMotionOverlay` under `Window.motion`, like the tab underline.
