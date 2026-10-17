# Phase 22B — Fury Banner & Application Navigation

**Status:** unreleased development after RC.6 (`0.1.0-rc.6`). Builds on Phase 22A (`d12ec88`).
Next packaged candidate: **`0.1.0-rc.7`** (not packaged here; `Directory.Build.props` still says
`0.1.0-rc.6`). RC.6 artifacts are untouched.

Scope: the app shell only — brand banner, section navigation and their responsive behavior
(selected direction: **Option A — Minimal Fury Banner**). No page was redesigned, no view model,
routing, retrieval, persistence, PDF or sync code changed, no schema changed
(DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1).

Files:

- `src/Carowinds.Passdown.Desktop/Assets/Branding/fury325-banner.jpg` — new banner asset.
- `src/Carowinds.Passdown.Desktop/Views/MainWindow.axaml` — banner replaces the navy band; the strip
  keeps its six buttons/bindings; window-local styles removed.
- `src/Carowinds.Passdown.Desktop/Themes/PassdownStyles.axaml` — new "App shell" section
  (`Border.brandBanner`, `TextBlock.brandWordmark`, `Border.appNavigation`, `Button.appSectionItem[.active]`).
- `src/Carowinds.Passdown.Desktop/Themes/PassdownTokens.axaml` — five theme-invariant shell tokens.
- Tests: `Phase22BShellNavigationTests` (new, mixed partition), `Phase15ERealWindowLayoutTests`
  (+11, run-alone), `Phase22ADesignFoundationTests` (token lists extended).

## 1. Starting shell audit (at `d12ec88`)

Measured in the isolated Skia harness (§13), identical on every section and size:

| Element | Before |
|---|---|
| Brand band | `Border` Padding `24,10`, navy `PassdownBrandDarkBrush`, **47 px** tall; 3-bar mark + two stacked rows "CAROWINDS" (15 Bold) / "OPERATIONS" (10.5 Medium, muted); a 10%-opacity `Polyline` "trackline" motif |
| Navigation container | `Border` on `PassdownSurfaceBrush`, 1 px bottom divider, no horizontal padding, **40 px** tall; `WrapPanel` of six `Button.appSectionItem` |
| Item style (window-local `<Window.Styles>`) | Padding `18,11`, 13.5 Medium, secondary text; active = 3 px accent-foreground underline + accent-foreground text + SemiBold |
| Total shell | **87 px** (content starts at y = 87) |
| Hover | neutral hover background; Fluent's own pointer-over border brush was not overridden; Fluent's pressed scale (0.98) applied |
| Focus | Fluent focus adorner (stroke mapped to `PassdownFocusBorderBrush` in 22A) |
| Selection binding | `Classes.active="{Binding Is…SectionSelected}"`, commands `Show…SectionCommand` on `MainViewModel`; Notes-editing CanExecute disables the others |
| Page hosting | six `Border IsVisible=…` hosts in one `Grid`; every section view constructed once and kept alive |
| Minimum window | 960×640 (`MinWidth`/`MinHeight`) |
| Responsive | `WrapPanel` wraps the strip to a second row only if labels do not fit (they fit at 960 with real fonts: right edge 773 px) |
| Icon | `Icon="/Assets/logo.ico"` (window) and `<ApplicationIcon>` (exe) — unchanged |
| Defects noticed | first nav label at x≈18 vs. wordmark mark at x=24 (misaligned); SemiBold-on-select changed label widths, so items to the right of the selection shifted by ~1–3 px when switching pages |

## 2. Navigation structure (kept)

Six destinations, same order, labels, commands, accessible names and routing:
Dashboard · Operations Calendar · Associate Passdown · Operational Notes · History · Settings.
No destination added or removed, no rename, no Manager entry. `MainViewModel` is unchanged, so every
state-preservation rule (Dashboard Open Passdown → today's Eastern date, Associate keeps its selected
date and unsaved edits, Calendar/History routing, Settings state) is untouched and still covered by
the existing tests.

## 3. Fury image location and provenance

| | |
|---|---|
| Source | The photograph the product owner attached in the Phase 22B request (session attachment `1.webp`, WebP, 1920×1080 landscape, SHA-256 `29918441e0a969dcb0a452e5ff5e784b7c5c9735aacd378f04def328f774fa97`). No copy existed in the repository, branding resources or earlier screenshots. |
| Subject check | Fury 325 at Carowinds: teal track with yellow-green spine, white tubular supports, the vertical-drop tower with a train at the top, camelback hill and the lift hill on the right, tree line and lawn. Matches the ride; not a substitute image. |
| Photographer / original publisher | **Unknown.** Not stated with the attachment; no credit in the file. |
| Processing | Crop only: full width × 320 px rows starting at source y=395 (centered on the camelback crest at y≈555), re-encoded as JPEG quality 80 with macOS `sips`. No retouching, color grading or generative change. Darkening/tint is done at runtime by the overlay, not baked in. |

## 4. Banner asset

`Assets/Branding/fury325-banner.jpg` — 1920×320 px (6:1), baseline JPEG, sRGB, **135,816 bytes**.
Metadata: only JFIF/pixel-dimension Exif, an Apple sRGB ICC profile and an empty Photoshop block —
no GPS, author, path or camera data. Packaged through the existing `<AvaloniaResource Include="Assets\**" />`,
so it is embedded in `Carowinds.Passdown.Desktop.dll` (no loose file, no network fetch, no absolute or
user path; works identically in `dotnet run` and published builds). One copy only (guarded by a test).
The executable icon and application identity are unchanged.

## 5. Banner styling

- `Border.brandBanner`: height `PassdownBannerHeight` (72), fallback fill `PassdownBrandDarkBrush`, clips.
- `Image` `Stretch="UniformToFill"` (aspect preserved, never stretched), centered crop, high-quality
  interpolation; decorative: `AutomationProperties.AccessibilityView="Raw"`, not hit-testable.
- `PassdownBannerOverlayBrush`: charcoal-navy `#0C1724` left-to-right `LinearGradientBrush` in
  **absolute** units (0→900 px, not % of width) so the scrim behind the wordmark is identical at every
  width: opacity 0.90 → 0.78 (x=306) → 0.50 (x=495) → 0.32 (x ≥ 900). Reads as slightly desaturated,
  restrained; Fury stays visible right of the wordmark.
- Wordmark: the existing 3-bar mark (slightly smaller, 18 px) + one line **CAROWINDS OPERATIONS**,
  `PassdownFontSizeWordmark` 16, Bold, letter-spacing 1.6, `PassdownHeaderForegroundBrush` (white),
  24 px left margin, vertically centered. The trackline `Polyline` and the stacked two-row title are
  gone (no duplicated title, no ornaments). No descriptor line (none was useful).

## 6. Header height

Banner **72 px** (the low end of the 72–84 range — chosen over 76 to limit the content-height cost;
76 was rendered and looked equivalent). Navigation strip **33 px** (item padding `14,8`, 14 px label,
2 px underline slot, 1 px divider). Fixed on every page; asserted by real-window tests.

## 7. Content-height comparison

Same harness, same fixtures, Dashboard (identical on all sections):

| Viewport | Old shell | New shell | Content height old → new | Δ |
|---|---|---|---|---|
| 1280×860 | 87 | 105 | 773 → 755 | −18 px (−2.3%) |
| 960×640 | 87 | 105 | 553 → 535 | −18 px (−3.3%) |
| 1280×690 | 87 | 105 | 603 → 585 | −18 px (−3.0%) |
| 1920×1000 | 87 | 105 | 913 → 895 | −18 px (−2.0%) |

The +18 px is the cost of a photograph legible at ~72 px (the old band was 47 px of flat color); the
nav strip shrank 7 px to offset part of it. Far below the 150–200 px the brief rules out.

## 8. Light/dark behavior

The banner is theme-invariant (same image, overlay and white text in both themes; tokens defined only
in the theme-invariant section). Below it the strip uses `PassdownSurfaceBrush` + `PassdownDividerBrush`,
so light = dark banner over a white strip and the neutral gray workspace; dark = banner over the
charcoal `#1F2227` strip — no bright seam (verified in screenshots).

## 9. Navigation styling changes

All in the shared styles (`Themes/PassdownStyles.axaml` "App shell"), tokens only, no literal colors;
`MainWindow.axaml` no longer has `<Window.Styles>`.

| State | Treatment |
|---|---|
| Rest | transparent, `PassdownTextSecondaryBrush`, 14 Normal, padding `PassdownNavigationItemPadding` (14,8), strip padding `PassdownNavigationStripPadding` (10,0) → first label aligns with the wordmark mark at x=24 |
| Hover | `PassdownHoverBackgroundBrush`, primary text, **no underline** (Fluent's pointer-over border suppressed) |
| Pressed | `PassdownPressedBackgroundBrush`; Fluent's 0.98 scale disabled (no jitter) |
| Active | 2 px `PassdownAccentForegroundBrush` underline + SemiBold + `PassdownTextPrimaryBrush` (accent no longer used as text) |
| Active + hover/pressed | neutral hover/pressed background, underline kept |
| Disabled (while an Operational Note is edited) | transparent, `PassdownTextDisabledBrush` (no Fluent gray fill) |
| Focus | Fluent keyboard adorner, unchanged (`PassdownFocusBorderBrush`) |

Layout stability: the item `ContentTemplate` overlays an invisible SemiBold copy of the label
(`Opacity=0`, `AccessibilityView=Raw`) under the visible one, so every item always reserves its
SemiBold width — selecting a section no longer moves any item (asserted across all six sections).
No icons exist in the navigation (text-only, as before); none were added. No accent fills or
selected-background fills are used in the shell (blue limited to the 2 px underline).

## 10. Active-page indication

Three independent cues: the underline (shape), SemiBold weight, and higher-contrast text — not color
alone. Exactly one item is active (each `Is…SectionSelected` flag binds exactly one item). Screen
readers: the active item exposes `AutomationProperties.ItemStatus = "Current page"`.

## 11. Keyboard / accessibility

- Tab order runs Dashboard → … → Settings; Enter activates the focused item; focus stays on it
  (real-window test with headless key input).
- Accessible names unchanged ("Dashboard section", …); the strip is a named Navigation landmark
  ("Application sections"); the photo is hidden from the automation tree; the wordmark is named
  "Carowinds Operations"; the 3-bar mark is hidden.
- Hit targets: 33 px tall; widths follow the label (narrowest, History, ≈ 75 px).
- Wordmark contrast (white over the composited banner, sampled from the actual asset pixels behind
  x 20–304, y 24–48 at each width's UniformToFill crop): **9.1–11.9:1** at 960, 1280, 1600, 1920 and
  2560 px wide; darkest region ≈ 18:1. Guaranteed floor even if the photo pixel were pure white:
  **9.0:1** at the end of the wordmark (asserted by `BannerOverlay_GuaranteesWordmarkContrast…`).
- Navigation text contrast is the 22A token pairs (secondary text on surface ≥ 7.2:1 light / ≥ 9.9:1 dark).

## 12. Theme switching

All shell brushes are `DynamicResource`. Verified two ways: the harness switched Light → Dark → Light
on one live window (the `*-Light2-*` frames are byte-identical to the first Light pass at all four sizes — no stale brushes), and
`MainWindow_ThemeSwitch_RestylesNavigation_WhileTheBannerStaysDark` asserts the strip background,
divider, active/inactive foregrounds and underline are the per-theme token instances after each switch
while the banner keeps `PassdownBrandDarkBrush`.

## 13. Screenshot results

Harness (session scratchpad, not committed): a console app compiled against the Desktop project and
the Ui.Tests fakes, `UseSkia().UseHeadless(UseHeadlessDrawing=false)` + `CaptureRenderedFrame()`, no
real services, no desktop lifetime (so no migration or DB access). The real profile DB
(`~/Library/Application Support/Carowinds Passdown/carowinds-passdown.db`) had identical mtime/size
before and after. 52 before + 52 after PNGs: six sections × Light/Dark (+ Light-after-Dark for the
Dashboard) × 1280×860, 960×640, 1280×690, 1920×1000, plus banner close-ups and a hover/keyboard-focus
state capture at 960×640 in both themes.

Fixtures (synthetic, no production data) — improved over 22A:

| Fixture | Status |
|---|---|
| Busy operating day: verified hours 10 AM–10 PM, attendance 38,500 / pre-solds 21,240, ERT 9:00 AM, 3 active operational notes (1 important), two daily notes, draft in progress | captured (Dashboard, Associate) |
| Selected shows (3 shows, 5 performances, venues) | captured (Dashboard, Associate) |
| Partial official-source failure (Fast Lane unavailable, Promotions stale, Haunted not applicable) | captured (Dashboard Data Status) |
| Mathematical Daily Feature (`\( t = \sqrt{\frac{2h}{g}} \)`) | captured (Associate preview; Dashboard shows source text as before) |
| Historical date / finalized record | History list (one finalized record); Calendar default month |
| Product prices | **PARTIAL** — Associate Products fixture has no product rows (building priced product fixtures was out of proportion to a shell phase) |

Reviewed: banner brightness, crop, wordmark contrast, header height, stretching, seams, alignment,
active state, clipping, whitespace. Defects found and fixed during review:

1. Percent-based overlay left the photo almost invisible (only the right ~15% showed) → absolute-pixel
   gradient easing after the wordmark.
2. Camelback crest clipped at 1280 px → crop moved 20 source rows up (center y≈555).
3. 76 px banner + 33 px strip = +22 px → banner set to 72 px (+18 px total).
4. First nav label misaligned with the wordmark → strip padding aligns both at x=24.
5. Items shifted when the SemiBold active item changed → width-reserving label template.

Note: this test assembly's default headless platform uses fake text metrics that are wider than real
fonts, so in unit tests the strip wraps to two rows at 960 px; the Skia harness (real Inter metrics)
fits one row with the last item ending at x=756. Tests therefore assert a per-row height budget and
stable, non-overlapping geometry instead of a row count.

## 14. Responsive behavior

- 960×640: all six destinations on one row (right edge 756 of 960), wordmark unclipped, content starts
  at y=105 with 535 px for the page, no horizontal scrollbar. Crop shows the camelback arch and track.
- 1280×690: shell 105 px (15% of height); Dashboard's first two cards and "Today at a Glance" visible.
- 1280×860 / 1920×1000: same geometry; at 1920 the photo is shown at native resolution.
- Wider than 1920 logical px: the 1920-px asset is upscaled slightly (soft but not stretched).
- If labels ever do not fit (e.g. very large text scaling), the existing `WrapPanel` wraps to a second
  row rather than clipping; no hamburger menu was introduced.

## 15. Windows validation checklist

All items **NOT EXECUTED** (no Windows host). Run on the RC.7 package.

| Check | Status |
|---|---|
| Banner loads from the packaged build (no missing image, no fallback navy) | NOT EXECUTED |
| Fury 325 recognizable | NOT EXECUTED |
| Crop correct at 960, 1280, maximized widths | NOT EXECUTED |
| Wordmark readable | NOT EXECUTED |
| Light theme | NOT EXECUTED |
| Dark theme | NOT EXECUTED |
| Live theme switching (Windows Settings → Colors with the app open) | NOT EXECUTED |
| All six destinations reachable | NOT EXECUTED |
| Active destination clear | NOT EXECUTED |
| Keyboard focus (Tab through the strip; Enter/Space activate) | NOT EXECUTED |
| Mouse hover distinct from active | NOT EXECUTED |
| 100% DPI | NOT EXECUTED |
| 125% DPI | NOT EXECUTED |
| 150% DPI (incl. 1280×690-equivalent maximized on 1920×1080) | NOT EXECUTED |
| Narrow window 960×640 | NOT EXECUTED |
| Dashboard Open Passdown opens today's Eastern date | NOT EXECUTED (PASS BY TEST) |
| Associate selected date preserved across section switches | NOT EXECUTED (PASS BY TEST) |
| Calendar and History navigation | NOT EXECUTED (PASS BY TEST) |
| No clipped content | NOT EXECUTED |
| No excessive header height | NOT EXECUTED |
| No broken popup layering (date picker, Copy/More menus, Price History overlay over the banner) | NOT EXECUTED |
| Narrator reads "Current page" on the active section; photo not announced | NOT EXECUTED |

## 16. Asset rights status

**BANNER IMAGE DISTRIBUTION RIGHTS PENDING.** The photograph was supplied by the product owner for
this design; its photographer, license and permission for redistribution inside a packaged
application are not documented. Embedding it is a technical step, not a rights clearance. Before any
broader distribution: confirm the source/license (or replace with a cleared photo of Fury 325 — the
crop/overlay design does not depend on this exact file), then add a credit/notice to
`packaging/windows/THIRD-PARTY-NOTICES.txt` if the license requires one. This is separate from build
success and from the still-pending **QuestPDF distribution-license decision**.

## 17. Known limitations

- No real Windows run, DPI scaling, Narrator or high-contrast check (§15).
- Headless unit tests cannot prove one-row fit (fake font metrics); one row is evidenced by the Skia
  harness only.
- The photo slice is thin at very wide windows (72 px of a 1920-px-wide image); recognizable, but the
  ride reads as arcs of track rather than the whole coaster — inherent to a compact panoramic banner.
- Product-price screenshot fixture not built (§13).
- Disabled-strip (Operational Note editing) appearance verified by style markup only, not screenshot.
- Windows high-contrast mode would show the photo under system colors; not designed for here.

## 18. Next phase guidance (22C+)

- Do not change the shell geometry from page work; the banner height is one token
  (`PassdownBannerHeight`) and shell height is asserted by real-window tests.
- Page headers start immediately under the strip at y=105; Dashboard redesign (22C) can assume that.
- Reuse the Associate rail's selected style for in-page navigation; keep accent use to thin indicators.
- If the image is replaced after the rights review, keep a ≥ 6:1 panoramic crop around ≤ 150 KB and
  re-run the contrast measurement (the floor test is independent of the photo).
