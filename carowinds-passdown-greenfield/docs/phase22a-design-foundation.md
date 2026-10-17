# Phase 22A — Design Foundation & Dark Mode

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`, shipped code `ef13a6d`, docs `04a71c3`).
Next packaged candidate: **`0.1.0-rc.7`** (not packaged here; `Directory.Build.props` still says
`0.1.0-rc.6`). RC.6 artifacts are untouched.

Scope: a shared visual foundation (tokens + class styles) and a deliberately designed dark theme.
No screen layout was rearranged, no operational logic changed, no schema changed
(DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1).

Files:

- `src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml` — every color, type size, spacing step
  and radius (merged into `App.axaml` resources).
- `src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml` — the shared class styles (included in
  `App.axaml` after `FluentTheme`).
- `Converters/ThemeBrushLookup.cs` + the two brush converters — token lookups instead of hex literals.
- Views keep only their own local deltas in `<UserControl.Styles>`.

## 1. Initial visual audit

Measured before any change (screenshots of every section, light and dark, three window sizes):

| Finding | Detail |
|---|---|
| Duplicated styles | `pageTitle`, `sectionTitle`, `muted`, `panel`, `chip`, `primaryAction`, `secondaryAction`… were copied into up to **7** views (`TextBlock.sectionTitle` ×7, `Button.secondaryAction` ×7, `Border.panel` ×6). Small drift between copies (panel padding 18 vs 14, radius 8 vs 7, sectionTitle margin 8 vs 10, `fieldError` 12.5 vs 11.5). |
| Excessive blue | Every section title, sub-section title, category band label, secondary button (41 uses: blue 1.5px outline + blue text), hero-panel frame, toolbar hover and glyph used `PassdownAccentBrush`. Panels used a blue-tinted surface (`#F5F8FC`) and bands `#E4EDF7`. |
| Dark mode | Window background was Fluent's **pure black** `#000000`; calendar cells were transparent on black. Dark primary button was white text on `#6FA8E0` — **2.4:1, fails AA**. |
| Interaction bug | `Button.primaryAction` had no hover/pressed style, so on hover Fluent replaced its blue fill with the default gray button fill. |
| Metadata color | 25 uses of `TextControlPlaceholderForeground` (Fluent's placeholder brush) for real metadata text. |
| Hard-coded colors | Overlay scrim `#99000000`; `ParkHoursBadgeBrushConverter` (`#9E9E9E`, `#546E7A`, `#1E5AA8`, `#B26A00`); converters resolved once per value and did not follow a live theme switch. |
| Radii/chips | Chips radius 10/9 (pill-shaped); panels 7–8; buttons 5. |
| Typography | Dashboard headline 26 vs Associate 22; section titles 13.5 Bold +0.4 letter-spacing in accent blue; column headers 10 px Bold. |
| Kept as-is | Brand band, app-section strip structure, Calendar day-cell geometry, date popup calendar behavior, math preview/picker behavior, all bindings. |

## 2. Design principles

Operational information over decoration; hierarchy through weight and size, not colored containers;
neutral surfaces dominate; accent only for interaction, selection and focus; one look per control
across screens; dark mode designed (charcoal levels, not inverted); desktop density; accessibility
(text + color, never color alone).

## 3. Shared token names

Backgrounds: `PassdownAppBackgroundBrush`, `PassdownSurfaceBrush`, `PassdownSurfaceSecondaryBrush`,
`PassdownSurfaceElevatedBrush`, `PassdownPopupSurfaceBrush`, `PassdownOverlayScrimBrush`.
Text: `PassdownTextPrimaryBrush`, `PassdownTextSecondaryBrush`, `PassdownTextMutedBrush`,
`PassdownTextDisabledBrush`, `PassdownTextOnAccentBrush`.
Borders: `PassdownBorderBrush`, `PassdownDividerBrush`, `PassdownBorderStrongBrush`, `PassdownFocusBorderBrush`.
Interaction: `PassdownAccentBrush` (fills), `PassdownAccentHoverBrush`, `PassdownAccentPressedBrush`,
`PassdownAccentForegroundBrush` (accent as text/glyph/thin indicator), `PassdownSelectedBackgroundBrush`,
`PassdownSelectedForegroundBrush`, `PassdownHoverBackgroundBrush`, `PassdownPressedBackgroundBrush`.
Chips: `PassdownChipBrush`, `PassdownChipAccentBrush`, `PassdownChipAccentForegroundBrush`.
Statuses: `PassdownSuccess|Warning|Error|Info|Stale` `Brush` + `BackgroundBrush`,
`PassdownNotPublishedBrush`, `PassdownUnavailableBrush`, `PassdownNeutralStatusBackgroundBrush`.
Brand (theme-invariant, shell phase): `PassdownBrandDarkBrush`, `PassdownHeaderForegroundBrush`,
`PassdownHeaderMutedForegroundBrush`.

Retired keys (no remaining references; guarded by a test): `PassdownBandBrush`,
`PassdownSurfaceTintBrush`, `PassdownSubtleSurfaceBrush`, `PassdownComparePriceBrush`,
`PassdownBrandDarkColor2/Brush2`, and `TextControlPlaceholderForeground` as a metadata color.

`NotPublished` and `Unavailable` deliberately share the neutral value today (both are "nothing
official to show", not alarms); they remain separate semantic slots so a later phase can diverge.

## 4. Light palette

| Role | Value | Role | Value |
|---|---|---|---|
| App background | `#F3F4F6` | Text primary | `#1B1F24` |
| Surface | `#FFFFFF` | Text secondary | `#4A525C` |
| Surface secondary | `#F6F7F9` | Text muted | `#5F6873` |
| Popup | `#FFFFFF` | Text disabled | `#8B939C` |
| Border / divider / strong | `#D6DAE0` / `#E4E7EB` / `#868E98` | Focus | `#1B1F24` |
| Accent / hover / pressed | `#1E5AA8` / `#174B8E` / `#123C73` | Accent foreground | `#1E5AA8` |
| Selected bg / fg | `#E6EEF8` / `#164A8A` | Hover / pressed bg | `#EEF0F3` / `#E3E6EA` |
| Success | `#1E6B3C` on `#E5F2E9` | Warning | `#8A5A00` on `#FBF1DC` |
| Error | `#B3261E` on `#FBE9E7` | Info | `#2B5F94` on `#E8F0F8` |
| Stale | `#7A5A10` on `#F6F0E1` | NotPublished / Unavailable | `#5F6873` |

## 5. Dark palette

| Role | Value | Role | Value |
|---|---|---|---|
| App background | `#141619` | Text primary | `#E8EAED` |
| Surface | `#1F2227` | Text secondary | `#BAC0C8` |
| Surface secondary | `#252930` | Text muted | `#9EA6B0` |
| Elevated / popup | `#2A2E35` / `#2B3038` | Text disabled | `#6E757E` |
| Border / divider / strong | `#353A42` / `#2D3138` / `#737B86` | Focus | `#E8EAED` |
| Accent / hover / pressed | `#2F6DB8` / `#2A64AA` / `#24578F` | Accent foreground | `#8DB9EB` |
| Selected bg / fg | `#233650` / `#B5D2F4` | Hover / pressed bg | `#2A2E35` / `#31363E` |
| Success | `#7CC59A` on `#1D3326` | Warning | `#E3B45C` on `#3A3020` |
| Error | `#F1958D` on `#3F2427` | Info | `#8DB9EB` on `#1F2F44` |
| Stale | `#D2AE68` on `#33301F` | NotPublished / Unavailable | `#9EA6B0` |

The dark accent *fill* is deeper than before so white button text passes; accent used as text or a
thin indicator switches to the lighter `PassdownAccentForegroundBrush`.

## 6. Typography hierarchy

Application font family unchanged (Fluent default / Inter); STIX Two Math remains math-only.

| Style | Size / weight | Color |
|---|---|---|
| `pageTitle` | 20 SemiBold | primary |
| `hoursHeadline` (primary value) | 22 SemiBold (Dashboard was 26 Bold) | primary |
| `statValue` | 20 SemiBold | inherited |
| `sectionTitle` | 14 SemiBold, no letter-spacing (was 13.5 Bold +0.4, blue) | primary |
| `subsectionTitle` | 12 SemiBold | secondary |
| body | 14 (Fluent default) | primary |
| `muted` (metadata/supporting) | 12.5 | secondary |
| `fieldLabel` | 12.5 SemiBold | primary |
| `fieldHint` / `fieldWarning` / chip text | 11.5 | secondary / warning |
| `columnHeader`, `statLabel` | 11 SemiBold (+0.2) | secondary |

No visible text was changed (existing uppercase labels such as "DATA STATUS" keep their literal text).

## 7. Spacing scale

`PassdownSpace1/2/3/4/6` = 4 / 8 / 12 / 16 / 24. Thickness tokens: page padding `24,20,24,24`, page
header `24,16`, panel `16` (unifies 18 and 14), inset `12`, button `14,7`, compact button `12,6`, chip
`8,2`, row `0,7`. Literal paddings inside view bodies were not mechanically replaced; the shared
styles consume the tokens.

## 8. Radius scale

`PassdownRadiusSmall` 3, `PassdownRadiusControl` 4 (buttons, inputs, chips, insets, bands),
`PassdownRadiusCard` 6 (panels, conflict rows, warning banner), `PassdownRadiusPopup` 8 (= Fluent
`OverlayCornerRadius`). Chips are no longer pill-shaped.

## 9. Button variants

| Class | Rest | Hover / pressed |
|---|---|---|
| `primaryAction` | accent fill, white text, SemiBold | accent hover / pressed fill (fixes the gray-on-hover bug) |
| `secondaryAction` | surface, 1px strong neutral border, primary text | hover / pressed neutral backgrounds |
| `warningAction` | warning outline + text | warning background |
| `textAction` (inline) / `linkAction` | transparent, accent-foreground text | neutral hover / pressed background |
| Associate `toolbarButton`/`dateButton`/`dateNavIcon` | surface + strong neutral border | neutral hover; **open flyout** = selected background + accent border |
| `mathPicker` | surface + neutral border, accent-foreground label | neutral hover; open = selected |

Disabled states remain Fluent's (gray fill + disabled text): recognizable as controls, distinct
from read-only text. Behavior, commands and toolbar order are unchanged. No destructive variant
exists in the app, so none was added.

## 10. Input styling

Color-only Fluent resource overrides: `TextControlBackground*`, `TextControlBorderBrush` (strong
neutral, ≥3:1 boundary), `TextControlBorderBrushPointerOver`, `TextControlPlaceholderForeground`
(readable placeholder), `ComboBoxBackground*`, `ComboBoxBorderBrush*`. Focused text boxes keep
Fluent's accent bottom rule. Templates, IME, selection, clipboard and spell-check adorners are
untouched. CheckBox/RadioButton/ToggleSwitch keep Fluent visuals driven by `SystemAccentColor`.

## 11. Popup styling

`FlyoutPresenterBackground`, `FlyoutBorderThemeBrush`, `MenuFlyoutPresenterBackground/BorderBrush`,
`MenuFlyoutItemBackgroundPointerOver/Pressed`, `ComboBoxDropDownBackground/BorderBrush`,
`ToolTipBackground/Foreground/BorderBrush` all map to the popup surface + border tokens, covering
the Associate date picker, Copy and More menus, math symbol picker and tooltips. Radius stays
Fluent's 8. The Season Price History overlay uses `PassdownPopupSurfaceBrush` on
`PassdownOverlayScrimBrush`. Content and dismissal behavior are unchanged.

## 12. Status styling

- `TextBlock.statusSuccess|statusWarning|statusError|statusInfo|statusStale|statusNotPublished|statusUnavailable`
  — opt-in foreground reinforcement.
- `Border.statusBadge` + `.success|.warning|.error|.info|.stale` — compact tag with tinted background.
- Calendar "stale" label now uses `PassdownStaleBrush` (muted amber); NotPublished/Unavailable remain
  neutral text labels; Error remains the accessible red. Every status still states itself in words.
- Park-hours dot: Open → success, Closed → unavailable (neutral), override/buyout → warning,
  not checked → strong neutral. Save chip: Saved → emphasized chip, otherwise neutral chip.
- Saving / Unsaved / Failed / Conflict / Verified visuals reuse these tokens; no state transitions changed.

## 13. Focus styling

Fluent's keyboard focus adorner is kept everywhere (native `:focus-visible` behavior); its primary
stroke `SystemControlFocusVisualPrimaryBrush` is mapped to `PassdownFocusBorderBrush` (near-black /
off-white, ≥13:1 on surfaces). Selected-and-today in the date popup uses the focus color for its ring.

## 14. Accessibility / contrast findings

Token-pair ratios (WCAG relative luminance), asserted by `Phase22ADesignFoundationTests`:

| Pair | Light | Dark |
|---|---|---|
| Primary text on surface | 16.6 | 13.2 |
| Secondary text on app background | 7.2 | 9.9 |
| Muted text on app background / popup | 5.1 / 5.7 | 7.4 / 5.4 |
| White on accent / hover / pressed | 6.8 / 8.6 / 10.9 | 5.3 / 6.0 / 7.4 |
| Accent foreground (links, selected tab) on surface | 6.8 | 7.8 |
| Selected fg on selected bg | 7.5 | 7.9 |
| Error / warning / stale / success on surface | 6.5 / 5.9 / 6.4 / 6.5 | 7.2 / 8.3 / 7.6 / 7.8 |
| Strong border (inputs, secondary buttons) on surface | 3.3 | 3.7 |
| Focus stroke on surface | 16.6 | 13.2 |

Limitations: these are token measurements, not a WCAG conformance claim. Not measured: Fluent's own
disabled-control text (exempt but low), the brand-band muted subtitle, the 0.4-opacity
out-of-month calendar cells, Fluent's CheckBox unchecked border, the spell-check underline
(`#E81123`, unchanged — ~4.6:1 light / ~4.3:1 dark), text over the scrim. Decorative panel borders and
dividers are intentionally below 3:1 (they are not the only cue for any control).

## 15. Screenshot results

Isolated harness (session scratchpad, not committed): a console app compiled against the Desktop
project and the Ui.Tests fakes, rendered with `UseSkia().UseHeadless(UseHeadlessDrawing=false)` and
`CaptureRenderedFrame()`, switching `RequestedThemeVariant` live on one window. No real services
were resolved; the real profile DB (`~/Library/Application Support/Carowinds Passdown`) mtime/size
were identical before and after. 66 before + 66 after PNGs: Dashboard, Calendar, Associate
(Overview/Products/Notes), Season Price History, Operational Notes, History, Settings × light/dark ×
1280×860, 960×640, 1280×690, 1280×2200.

| Fixture | Status |
|---|---|
| Typical day (fake defaults), selected Calendar date, Math Daily Feature, Price History records, empty Notes, one History record | captured |
| Busy day, missing/unpublished data, partial source failure, historical Calendar date, selected Shows | **PARTIAL** — fake defaults show "Not checked"/"No published data" states only; no busy-day/shows/product-row fixture was built |

Observed after: neutral gray page with white cards (light) / charcoal levels (dark, no pure black);
section titles no longer blue; only primary actions, the selected tab/rail item, selected dates and
links carry accent; dark primary buttons readable; secondary buttons neutral; Calendar cells are
surfaces with a visible selected state; inputs have visible boundaries in both themes.

## 16. Theme-switching behavior

`App.axaml` keeps `RequestedThemeVariant="Default"` (follows Windows). All theme-dependent tokens are
referenced with `DynamicResource` (enforced by a test), and the two brush converters receive the
element's `ActualThemeVariant` through a `MultiBinding`, so a live OS switch restyles every screen,
including the park-hours dot and save chip, without reopening views. Verified in the headless
harness (same window, Light → Dark); not verified on Windows.

## 17. Known limitations

- Brand band (navy) and the app-section strip structure are unchanged — Phase 22B.
- Fluent `ListBox` background in the Price History product list is Fluent's light gray.
- Some view bodies still contain literal paddings/font sizes; only shared styles consume tokens.
- Visual-regression testing is screenshot review, not pixel assertions.
- No real Windows DPI (100/125/150%) validation — only simulated viewport sizes on macOS.
- Busy-day / Shows / product-row fixtures were not rendered (see §15).

## 18. Guidance for Phases 22B–22F

- Use classes first (`panel`, `inset`, `sectionTitle`, `muted`, `primaryAction`, `secondaryAction`,
  `statusBadge.*`, `selectableRows`), tokens second, literals never.
- One primary action per screen; everything else `secondaryAction` or link-style.
- Accent only for interaction, selection, focus, links; use `PassdownAccentForegroundBrush` for accent
  text/indicators, `PassdownAccentBrush` only for fills.
- Prefer one panel level + `inset`; to reduce cards, drop `Border.panel` and let the section heading
  sit on `PassdownAppBackgroundBrush`.
- Add a new token only for a genuinely new role; add it to both theme dictionaries and to
  `Phase22ADesignFoundationTests.ColorTokens`.
- Override Fluent by resource key (color-only) before considering template overrides; target
  `ContentPresenter#PART_ContentPresenter` for state colors.
- Keep view-local styles to deltas; the markup tests reject re-duplicating shared selectors.

## 19. Windows visual-validation checklist

All items **NOT EXECUTED** (no Windows host in this session). Run on the RC.7 package.

| Check | Status |
|---|---|
| Light theme — every section readable, cards distinguishable from page | NOT EXECUTED |
| Dark theme — no pure black, readable metadata, visible borders | NOT EXECUTED |
| Live theme switching (Windows Settings → Colors while app open) incl. park-hours dot & save chip | NOT EXECUTED |
| Dashboard styling (hero panels, Check Online / Run Deep Refresh) | NOT EXECUTED |
| Associate controls (toolbar, date nav, Create PDF, Copy, More, rail) | NOT EXECUTED |
| Calendar popup (today ring, selected fill, hover) | NOT EXECUTED |
| Shows section (checkboxes, Select all / Clear, disabled state) | NOT EXECUTED |
| Math picker and preview | NOT EXECUTED |
| Notes and History (filter tabs, list selection/hover) | NOT EXECUTED |
| Season Price History overlay (scrim, surface, row dividers) | NOT EXECUTED |
| Settings controls (checkbox, Choose Folder, Sync Now disabled) | NOT EXECUTED |
| Focus indicators visible in both themes (Tab through every section) | NOT EXECUTED |
| Keyboard navigation unchanged | NOT EXECUTED |
| 100%, 125%, 150% DPI | NOT EXECUTED |
| Narrow window (960×640) layouts, no clipping / horizontal scroll | NOT EXECUTED |
| Statuses and error messages readable (Error, Stale, NotPublished, Unavailable) | NOT EXECUTED |
| Windows high-contrast mode (informational) | NOT EXECUTED |
