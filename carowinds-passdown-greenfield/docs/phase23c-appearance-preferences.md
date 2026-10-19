# Phase 23C — Appearance preferences & theme persistence

Unreleased development after `0.1.0-rc.7` (next candidate `0.1.0-rc.8`, not packaged here). Settings →
Appearance becomes a real choice: **Use Windows setting** (System), **Light** or **Dark**. The choice
applies immediately, survives restarts and stays on this computer. It selects one of the two existing
Phase 22A palettes; no palette, banner or navigation was redesigned.

## 1. Starting state

| | Verified |
|---|---|
| HEAD / branch | `00f8b3e` on `feature/passdown-redesign` |
| Version | `0.1.0-rc.7` / FileVersion `0.1.0.7` (`Directory.Build.props`, unchanged) |
| Deterministic tests | **4069/4069** (221 Domain + 1089 Application + 852 Infrastructure + 463 Documents + 1321 Ui mixed + 26 + 84 + 2 + 7 + 4 run-alone) |
| Release build | 0 warnings, 0 errors |
| Schemas | DB 16 (`Migration0016…` last), Draft 4, Snapshot 2, SyncSchemaVersion 1 |
| Unrelated staged files | `.DS_Store`, `.claude/.DS_Store`, `.claude/skills/.DS_Store`, `.claude/skills/pdf-design/SKILL.md` (plus unstaged `../.DS_Store`, `docs/.DS_Store`) — untouched, outside the commit |

No differences from the expected baseline.

## 2. Theme architecture (audit and what was reused)

Before 23C (RC.7):

- **Detection / initial theme / OS changes** — `App.axaml` sets `RequestedThemeVariant="Default"`.
  Avalonia 12.1.1 then reads `IPlatformSettings.GetColorValues().ThemeVariant` (Windows "app mode";
  the same API on macOS) and listens to `IPlatformSettings.ColorValuesChanged`. The app had no code of
  its own for this.
- **Palettes** — `Themes/PassdownTokens.axaml`: one `Light` and one `Dark` `ThemeDictionaries` entry;
  `Themes/PassdownStyles.axaml` holds shared class styles. Every theme token is a `DynamicResource`
  (enforced by an existing test), and the two brush converters re-resolve through `ThemeBrushLookup`
  with the element's `ActualThemeVariant`, so a variant change restyles live.
- **Windows, menus, popups** — inherit the application variant; Fluent's flyout/menu/tooltip/combo
  surfaces are overridden in the token dictionaries (Phase 22A/22F).
- **Screenshot fixtures** — `tools/visual-fixtures` set `Application.RequestedThemeVariant` directly.

23C keeps all of that and adds one owner for the choice:

| Layer | Type | Role |
|---|---|---|
| Application | `Preferences/AppearancePreference` (`System` = 0, `Light`, `Dark`), `EffectiveAppearance`, `AppearanceResolver`, `IAppearancePreferenceStore` | Pure rule: Light/Dark override, System follows the OS; `Sanitize` rejects undefined values |
| Infrastructure | `Preferences/FileAppearancePreferenceStore` | `appearance.json` beside the existing template preference |
| Desktop | `Services/AppearanceThemeController` | Loads once, applies, follows the OS only in System mode, saves |
| Desktop | `Services/AvaloniaAppearance.cs`: `ISystemAppearanceSource` / `AvaloniaSystemAppearanceSource`, `IThemeVariantTarget` / `ApplicationThemeVariantTarget` | Avalonia's own `IPlatformSettings` + `ColorValuesChanged` (no polling); sets `Application.RequestedThemeVariant` to `Light` or `Dark` |
| Desktop | `ViewModels/AppearanceSettingsViewModel` (exposed as `PersonalSyncSettingsViewModel.Appearance`) | The three radio options |

The controller always sets an explicit `Light` or `Dark` variant, including in System mode, where it
resolves the OS appearance itself from the same Avalonia API RC.7 relied on. That makes System mode
testable with a simulated OS and makes "return to System" immediate. No Windows-specific API is used.
No second resource dictionary, palette or settings system was created.

## 3. Selector design

```
APPEARANCE
Choose how Carowinds Operations looks. Saved on this computer only.
(●) Use Windows setting
    Windows is currently set to Dark.
( ) Light
( ) Dark
```

Three Fluent `RadioButton`s in one group (`GroupName="Appearance"`), inside the existing Appearance
panel. No Apply button, no hard-coded colors; Fluent draws the checked ring and dot from the Phase 22A
accent tokens. The hint line under "Use Windows setting" shows what Windows is set to, so its effect is
clear even while Light or Dark is forced. A save failure shows one line in the shared `statusError`
style (polite live region): "This choice couldn't be saved on this computer. It applies until the app
closes."

## 4. System mode

Shows the OS appearance and follows later OS changes (via `ColorValuesChanged`, marshalled to the UI
thread). Returning to System from a forced option shows the OS's current appearance at once.

## 5. Forced Light

Always the light palette. OS changes are ignored (the hint line still updates) until System is chosen.

## 6. Forced Dark

Always the dark palette, same rules as Light.

## 7. Preference persistence

`%LOCALAPPDATA%\Carowinds Passdown\appearance.json` (the existing per-user application-data folder;
the legacy folder and database names are unchanged; nothing is written under Program Files):

```json
{
  "Appearance": "Dark"
}
```

Stable enum names, never display labels. Written with the existing `AtomicFileWriter` (temp file,
flush to disk, rename). Picking an option saves only this file. Rapid changes are written one at a
time and each write stores the latest choice, so the file ends on the last option picked.

## 8. Local-only behavior

The file is outside the Personal Sync folder and no Personal Sync code references it (asserted by a
source test). Each computer keeps its own choice. No database table, no migration.

## 9. Invalid-value fallback

Missing file → System (the RC.7 behavior, so upgrades are unchanged). Malformed JSON, an empty file,
`null`, an unknown name (`"Blue"`, `"Use Windows setting"`), any number (integers are rejected, so `7`
cannot become an undefined option), or a missing property → System, with one warning in the app log that
names only the exception type (no path, no contents). The bad file is left as is until the user picks
an option. Startup never fails because of it.

## 10. Initialization ordering

`App.OnFrameworkInitializationCompleted` calls `ApplySavedAppearance` right after the startup log line —
before migrations, before `MainWindow` is resolved and before any refresh or sync starts. It reads one
small local file synchronously (no database, no network) and sets the variant, so a saved Light or Dark
is the first theme drawn; there is no switch after the window appears. Until then `App.axaml` still says
`Default` (follow the OS), which is also what the headless `--scheduled-refresh` host keeps: it shares
`BuildServiceProvider` but never initializes appearance.

## 11. Live theme switching

Setting `Application.RequestedThemeVariant` restyles every open window through the existing
`DynamicResource` tokens. Real-window tests switch Dark ⇄ Light on Dashboard, Calendar, Associate
Overview / Products / Notes & Shows, Operational Notes, History and Settings and find **no visible brush
left from the previous palette** (brushes compared by instance against the token dictionaries) and no
horizontal clipping. Theme selection saves no draft, starts no official request and does not sync (the
controller has no such dependency; asserted by reflection and with sync fakes).

## 12. Popups and overlays

Open while the theme changes, then checked in both directions: Associate date picker, Copy menu, More
menu, Math picker (real-window tests: each stays open and its content reports the new variant with no
old-palette brush), Season Price History overlay, Dashboard "+N more" showtimes, Calendar Shows
expansion. None needs to be closed and reopened. No popup template was duplicated or changed.

## 13. Accessibility

- One Tab stop for the group (`KeyboardNavigation.TabNavigation="Once"`); Tab / Shift+Tab lands on the
  **checked** option (the view keeps `TabOnceActiveElement` on it).
- Up/Down arrows move between options (`XYFocus.NavigationModes="Keyboard"`) and select, like a native
  Windows radio group (view code-behind checks the option when focus arrives by arrow key; presentation
  only). Space checks the focused option. Tab never changes the selection.
- Fluent's focus adorner is unchanged.
- Accessible names: group "Appearance" (help text "Choose how Carowinds Operations looks"), options "Use
  Windows setting", "Light", "Dark"; Fluent's RadioButton automation peer reports the checked state.
- Selection does not depend on color: the checked option draws an extra inner dot (asserted).
- Contrast: the checked indicator measured ≥ 3:1 against the panel in both themes (real-window test on
  the rendered ellipse fill). Phase 22A token contrast tests are unchanged and pass.

Narrator: NOT EXECUTED (checklist §16).

## 14. Screenshot results

Isolated harness (`tools/visual-fixtures`, output in the session scratchpad, not committed). Every theme
now goes through the real `AppearanceThemeController` + `ApplicationThemeVariantTarget` with an in-memory
store and a **simulated** OS appearance. Forced modes run against the opposite simulated OS (Light under
an OS Dark, Dark under an OS Light), so the captures prove the preference decides. The real profile
folder's file list, sizes and mtimes were identical before and after, and no `appearance.json` was
created there.

| Run | Captures | Horizontal overflow | Buttons past right edge | Shows overlaps |
|---|---|---|---|---|
| Forced Light / Dark × 960×640, 1280×690, 1280×860, 1920×1000 × all 51 scenarios | 408 | 0 | 0 | 0 |
| System under simulated OS Light / Dark × 1280×860 × all scenarios | 102 | 0 | 0 | 0 |
| System, `Appearance-Settings` and `LiveSwitch-SystemOs` × all four sizes | 16 | 0 | 0 | 0 |

New scenarios: `Appearance-Settings`, `Appearance-SaveFailed`, and `LiveSwitch-*` (Settings by checking
the opposite radio; Dashboard with "+N more" expanded; Calendar with Shows expanded; Products; Season
Price History open; date picker, Copy menu, More menu and Math picker open; System mode with a simulated
OS switch). Each LiveSwitch capture is taken after the switch.

Reviewed by eye (Settings, save failure, date picker and Copy menu after switching both ways, Price History
and Dashboard Shows after switching, System following a simulated OS switch, plus the regression screens):
no wrong-theme surface, no missing resource, no hard-coded light background, readable text, visible focus,
no clipping, no re-layout, banner unchanged. **No visual issues found; nothing needed correcting.** The
real-window tests (§11–12) back this with an exhaustive brush check. Headless Skia on macOS is not
Windows or DPI validation.

## 15. Deterministic tests

| Partition (`-c Release`, `--filter "Category!=LiveSmoke"`) | Before | After |
|---|---|---|
| Domain | 221 | 221 |
| Application | 1089 | **1100** (+11 `AppearanceResolverTests`) |
| Infrastructure | 852 | **874** (+22 `FileAppearancePreferenceStoreTests`) |
| Documents | 463 | 463 |
| Ui mixed | 1321 | **1371** (+50 `Phase23CAppearancePreferenceTests`) |
| `OperationsCalendarViewTests` (alone) | 26 | 26 |
| `Phase15ERealWindowLayoutTests` (alone) | 84 | **105** (+21 Phase 23C real-window tests) |
| `RealWindowStartupTimingTests` / `SpellCheckNavigationLifecycleTests` / `Phase21DMathEditorWindowTests` (alone) | 2 / 7 / 4 | 2 / 7 / 4 |
| **Total** | **4069** | **4173/4173** |

Release build (`dotnet build CarowindsPassdown.slnx -c Release`): 0 warnings, 0 errors. Run-alone set
unchanged (five classes); the new tests that set the application-wide theme live in the existing
run-alone `Phase15ERealWindowLayoutTests`, and the mixed-partition tests use a recording target.

Changed existing test: `Phase22FSettingsAccessibilityTests.AppearanceAndRefresh_AreInformationOnly…`
asserted that Appearance had no control and said "Follows Windows". Phase 23C deliberately replaces that,
so the test is now `Refresh_IsInformationOnly…` and still checks every Data Refresh assertion; the
Appearance expectations moved to `Phase23CAppearancePreferenceTests`. No other test was weakened.

Coverage: default System; load of each option; invalid stored values (11 file shapes + undefined enum);
save/reload/restart; independent profiles; only `appearance.json` written (neighbouring template, sync
config, device id and database files byte- and mtime-identical); no Personal Sync reference; no
migration; no sync/settings/official calls; OS changes while forced and while System; all transition
orders; rapid changes (file ends on the last choice); save failure (applies, honest message, cleared by
the next success); startup ordering (before migrations and `MainWindow`; scheduled host never
initializes); composition root; selector markup, names, keyboard wiring, no Apply button, no hard-coded
colors; real windows: Space and pointer selection, arrow keys, Tab/Shift+Tab lands on the checked option,
every primary screen in both directions with no old-palette brush and no clipping, four open popups and
three in-window overlays, simulated OS switch, first frame uses the saved theme, Fury banner identical,
checked indicator ≥ 3:1 and not color-only, selector fits all five size/theme combinations.

## 16. Windows validation checklist (all NOT EXECUTED)

1. Windows Light, preference System: app light; Settings hint "Windows is currently set to Light."
2. Windows Dark, preference System: app dark; hint "…Dark."
3. With System selected, switch Windows Settings › Personalization › Colors › app mode while the app is
   open: the app follows within a moment, no restart.
4. Windows Dark, choose Light: app light; switching Windows mode changes nothing.
5. Windows Light, choose Dark: app dark; switching Windows mode changes nothing.
6. Cycle System → Light → Dark → System: each applies instantly; System returns to the Windows mode.
7. Choose Dark, close, reopen: opens dark with no light flash; Settings shows Dark checked. Repeat for
   Light and System.
8. Open the Associate date picker (and the Copy menu) and switch Windows mode with System selected:
   the open popup restyles.
9. Season Price History in both themes: readable list, popup surface correct.
10. Dashboard and Calendar (Shows expanded) in both themes.
11. Associate controls (text boxes, combo boxes, date picker, checkboxes, Math picker) in both themes.
12–14. 100%, 125%, 150% scaling at 960 px and maximized: three options visible, no clipping, no
    horizontal scroll.
15. Keyboard only: Tab to Appearance lands on the checked option, Up/Down selects, Space selects, focus
    ring visible, Tab moves on.
16. Narrator: reads "Appearance", each option name, "radio button", checked/not checked, and position.
17. Per-user: another Windows account on the same PC keeps its own choice; a second PC with Personal Sync
    on keeps its own choice.
18. No administrator prompt at any point; `appearance.json` appears in
    `%LOCALAPPDATA%\Carowinds Passdown\` only.

## 17. Known limitations

- Windows behavior (title bar color, live OS switch notification, Narrator) is verified only by headless
  tests and simulation on macOS; not on Windows.
- Windows **high-contrast** themes are not a fourth option; Fluent's own high-contrast handling is
  unchanged.
- Headless screenshots are not DPI validation.
- If the preference file cannot be written, the choice lasts for the session only (shown on screen).

## 18. RC.8 readiness

Source-ready for inclusion in `0.1.0-rc.8` together with Phases 23A and 23B, pending Windows validation
(§16). Not packaged; RC.7 artifacts unchanged; version metadata not advanced.
