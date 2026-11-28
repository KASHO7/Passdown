# UI Polish Skill

Use this skill for visual/layout refinement of the Avalonia desktop application.

## Goals

- Make the app feel like a polished Windows 11 desktop operations tool.
- Prefer compact desktop density over large web-style cards.
- Use available horizontal space effectively.
- Preserve vertical scrolling for smaller windows.
- Avoid horizontal scrolling at normal desktop widths.
- Keep business logic out of ViewModels/XAML.
- Optimize for fast scanning during real operations work.

## Layout

- Prefer Grid-based layouts over fixed coordinates.
- Use two-column or mixed layouts when enough width exists.
- Avoid giant single-column stacks on wide screens.
- Avoid excessive fixed Width/Height values.
- Avoid nested bordered cards unless they materially improve grouping.
- Prefer aligned label/value rows for simple operational data.
- Do not turn every metric into a separate card.
- Merge related operational information into a single coherent section.

### Row-first rule

If information can be expressed clearly as:

    Label        Value

prefer that over:

    ┌───────────────┐
    │ Label         │
    │ Value         │
    └───────────────┘

Cards should be reserved for content that genuinely requires visual separation.

## Visual hierarchy

- One clear page title.
- One obvious primary action.
- Secondary actions visually quieter.
- Operational values should be more prominent than status text.
- Status badges should be compact.
- Avoid oversized "Not checked" / empty-state headings.
- Use typography, alignment, and spacing before adding borders.
- Do not rely on card borders to create hierarchy.

## Associate Passdown

The Associate Passdown is a working operations screen, not a dashboard.

Prefer a structure such as:

    Park Hours       value
    North Gate       value
    Avenue           value

    Attendance       value
    Pre-solds        value

    Early Ride       rides
    Daily Feature    value

Avoid layouts where each of these becomes a separate card or narrow vertical
column.

### Early Ride

Early Ride displays rides, not an ERT start time.

Preferred:

    Early Ride       Thunderstriker, Sky Tower      [Edit]

Do not display or request a routine ERT start time.

Do not show multiple large configuration buttons inline.

Prefer one compact Edit action that opens/configures monthly/default and
date-specific behavior.

### Today-at-a-Glance rule

Do not create three or more narrow side-by-side columns for operational fields if
controls or text can collide.

Prefer aligned rows within one shared panel.

## Forms

- Align labels and controls consistently.
- Use compact field spacing.
- Use inline validation rather than modal dialogs.
- Multiline fields should get enough horizontal space.
- Do not use tiny controls to save space.
- Read-only information may be denser than editable forms.
- Avoid placing multiple large action buttons beside editable values.

## Products

- Prefer compact rows/list presentation.
- Clearly separate:
  - product name
  - official price
  - display override
  - qualifier/comparison price
  - include state
- Do not make every product its own giant card.
- Custom/internal items should look distinct from official products.
- Product catalog/reference screens may contain more information than the PDF.
- Do not remove useful historical/reference pricing merely to reduce visual density.

## Status

Status should be lightweight unless attention is required.

Prefer:

    Saved • Verified 2:14 PM • 1 source unavailable

over a large status card.

Warnings/errors may expand when action is required.

## Actions

- Primary action should be obvious.
- Secondary actions should use quieter styling.
- Avoid rows containing several equally-prominent buttons.
- Prefer a single Edit action for configuration areas rather than exposing every
  possible configuration path simultaneously.
- Button labels should remain readable at 960×640.

## Accessibility

- All controls keyboard reachable.
- Visible focus indicators.
- Critical actions must have text labels/accessibility names.
- Status must not rely only on color.
- Arrow-only date controls require accessible names.
- Compact layout must not reduce hit targets below comfortable desktop use.

## Scaling

Design for:
- 100%
- 125%
- 150%

Also test representative layouts at:
- 1280×860
- 960×640

Avoid fixed-height text containers likely to clip when scaled.

No controls should overlap at supported sizes.

## Theme

- Prefer Avalonia theme resources.
- Avoid scattering literal colors through XAML.
- Maintain readable contrast in dark/light themes where supported.
- Avoid decorative gradients, glass effects, oversized rounded cards, or
  marketing-site styling.

## Visual Review

A clean build is not visual validation.

When rendering is available:

1. Launch the actual app.
2. Navigate to the modified screen.
3. Inspect representative states.
4. Check for:
   - overlapping controls
   - clipped text
   - unnecessary empty space
   - weak alignment
   - excessive card framing
   - inconsistent spacing
   - awkward button placement
5. Revise.
6. Repeat.

Use screenshots supplied by the product owner as visual evidence.

If a screenshot reveals a problem that automated layout tests missed, the
screenshot takes priority.

## Reference-first rule

If the product owner supplies an approved layout/mockup:

- treat it as the visual specification
- match its information order
- match its grouping
- match its approximate density
- do not reinterpret it into a different layout without a concrete technical
  reason

## Process

When polishing UI:

1. Inspect the existing view before editing.
2. Preserve bindings and behavior.
3. Identify which containers genuinely need borders/cards.
4. Make structural layout improvements first.
5. Centralize repeated spacing/styles where practical.
6. Run existing UI tests.
7. Launch the app.
8. Inspect actual rendering when possible.
9. Compare against supplied screenshot/mockup.
10. Iterate based on visual evidence, not only passing tests.

Do not claim visual success based only on a clean build.
