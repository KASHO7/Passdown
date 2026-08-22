# UI Polish Skill

Use this skill for visual/layout refinement of the Avalonia desktop application.

## Goals

- Make the app feel like a polished Windows 11 desktop operations tool.
- Prefer compact desktop density over large web-style cards.
- Use available horizontal space effectively.
- Preserve vertical scrolling for smaller windows.
- Avoid horizontal scrolling at normal desktop widths.
- Keep business logic out of ViewModels/XAML.

## Layout

- Prefer Grid-based layouts over fixed coordinates.
- Use two-column or mixed layouts when enough width exists.
- Avoid giant single-column stacks on wide screens.
- Avoid excessive fixed Width/Height values.
- Avoid nested bordered cards unless they materially improve grouping.

## Visual hierarchy

- One clear page title.
- One obvious primary action.
- Secondary actions visually quieter.
- Operational values should be more prominent than status text.
- Status badges should be compact.
- Avoid oversized "Not checked" / empty-state headings.

## Forms

- Align labels and controls consistently.
- Use compact field spacing.
- Use inline validation rather than modal dialogs.
- Multiline fields should get enough horizontal space.
- Do not use tiny controls to save space.

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

## Accessibility

- All controls keyboard reachable.
- Visible focus indicators.
- Critical actions must have text labels/accessibility names.
- Status must not rely only on color.
- Arrow-only date controls require accessible names.

## Scaling

Design for:
- 100%
- 125%
- 150%

Avoid fixed-height text containers likely to clip when scaled.

## Theme

- Prefer Avalonia theme resources.
- Avoid scattering literal colors through XAML.
- Maintain readable contrast in dark/light themes where supported.

## Process

When polishing UI:

1. Inspect the existing view before editing.
2. Preserve bindings and behavior.
3. Make structural layout improvements first.
4. Run existing UI tests.
5. Launch the app.
6. Inspect actual rendering when possible.
7. Iterate based on visual evidence, not only passing tests.

Do not claim visual success based only on a clean build.
