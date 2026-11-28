---
name: ui-polish
description: Refine, modernize, review, and validate the Carowinds Operations Avalonia desktop interface while preserving approved designs, operational behavior, accessibility, performance, and data integrity.
---

# Carowinds Operations — UI Polish Skill

## 1. Purpose

Use this skill for:

- Visual and layout refinement.
- Desktop workspace modernization.
- UI consistency reviews.
- Responsive layout improvements.
- Shared control and style improvements.
- Accessibility improvements.
- Visual regression investigation.
- Light and Dark appearance refinement.
- Implementation of approved visual references.

The objective is to maintain a polished, professional, responsive Windows 11 operations application.

UI polish is not authorization to change business rules, application functionality, persistence behavior, document output, or architectural contracts.

Follow `CLAUDE.md`, applicable `.claude/rules/`, `AGENT_BUILD_SPEC.md`, and current architecture documentation.

---

## 2. Design philosophy

The application should feel like a purpose-built native desktop operations tool, not a website embedded in a desktop window.

Prioritize:

- Professional appearance.
- Operational efficiency.
- Compact but comfortable desktop density.
- Strong information hierarchy.
- Consistent visual language.
- Predictable interactions.
- Clear operational information.
- Responsive editing.
- Accessibility.
- Minimal visual distraction.

Avoid:

- Oversized web-style cards.
- Excessive rounded containers.
- Decorative gradients.
- Unnecessary shadows.
- Excessive whitespace.
- Repeated section headings.
- Excessive badges.
- Unnecessary explanatory text.
- Decorative animations.
- Emoji as primary interface icons.
- Inconsistent spacing.
- Arbitrary visual styling.
- Generic dashboard designs that disregard approved references.

Visual polish should improve usability, not merely add decoration.

---

## 3. Approved visual references

When an approved prototype, screenshot, HTML reference, or design is provided:

1. Inspect the reference carefully.
2. Inspect the corresponding existing application view.
3. Identify the intended hierarchy, spacing, density, alignment, controls, and interaction patterns.
4. Reproduce the approved visual direction using native Avalonia components and the existing design system.
5. Preserve real application bindings, data, commands, and behavior.
6. Compare the rendered implementation against the approved reference.

HTML prototypes are visual references only.

Do not:

- Embed the prototype as a web page.
- Copy prototype JavaScript into production.
- Copy placeholder operational data.
- Replace working application behavior with demonstration logic.
- Invent functionality implied by decorative prototype elements.
- Introduce a generic substitute when an approved reference is missing.

Where an approved reference is unavailable, preserve the existing view and report the missing design dependency.

Do not make significant unapproved visual departures.

---

## 4. Shared design system

Use the established application design system.

Prefer existing resources in:

- `PassdownTokens.axaml`
- `PassdownStyles.axaml`
- Existing icon resources.
- Existing shared controls.
- Existing typography resources.
- Existing appearance and motion infrastructure.

Before introducing a new resource, inspect existing equivalents.

Use semantic resources for:

- Background surfaces.
- Foreground text.
- Secondary text.
- Borders.
- Accent colors.
- Hover states.
- Pressed states.
- Selected states.
- Disabled states.
- Warning states.
- Error states.
- Success states.
- Focus indicators.
- Spacing.
- Typography.
- Corner radii.
- Control dimensions.
- Animation durations.

Do not scatter literal colors, arbitrary spacing values, or duplicated control templates throughout views.

Introduce new shared resources only when they represent a meaningful, reusable design requirement.

Do not create speculative design-system abstractions for one-off elements.

---

## 5. Application shell

Preserve the approved application shell.

Requirements:

- Native Windows window behavior.
- Original application icon.
- Approved native title-bar appearance.
- Collapsible sidebar navigation.
- Clear active navigation state.
- Consistent navigation icons.
- Accessible navigation labels.
- Predictable expanded and collapsed states.
- Appropriate navigation tooltips.
- Consistent workspace boundaries.

Do not reintroduce retired banners, redundant navigation systems, or decorative application chrome.

Do not replace native window chrome with a custom simulated title bar unless explicitly authorized.

Preserve:

- Minimize.
- Maximize.
- Restore.
- Close.
- Window resizing.
- Windows snapping.
- High-contrast compatibility.
- Native operating-system behavior.

Navigation animations must not delay workspace availability.

---

## 6. Layout and responsive behavior

Prefer flexible Grid-based layouts.

Use:

- Star-sized columns.
- Auto-sized content.
- Sensible minimum dimensions.
- Shared sizing where appropriate.
- Mixed-width layouts.
- Responsive content arrangements.
- Appropriate vertical scrolling.

Use available horizontal space effectively.

Prefer two-column or mixed layouts when sufficient width exists.

Avoid:

- Giant single-column stacks on wide screens.
- Fixed coordinates.
- Excessive fixed widths and heights.
- Nested scrolling regions without clear necessity.
- Horizontal scrolling at normal supported desktop widths.
- Large unused panels.
- Excessive nested borders.
- Unnecessary layout containers.
- Controls that become inaccessible when the window shrinks.

Keep frequently used actions accessible without requiring unnecessary scrolling.

When using split views or details panels:

- Preserve sensible minimum content widths.
- Avoid clipping primary controls.
- Ensure secondary panels do not consume excessive space.
- Preserve existing splitter and layout preferences where applicable.
- Maintain predictable behavior during window resizing.

Do not solve layout problems by arbitrarily increasing the application's minimum window size.

---

## 7. Desktop density and spacing

Maintain compact, comfortable desktop density.

Operational users should be able to view substantial information without excessive scrolling.

Use consistent spacing between:

- Labels and controls.
- Related fields.
- Form groups.
- Product rows.
- Section headings.
- Toolbars.
- Detail panels.
- Dialog actions.

Related information should appear visually connected.

Unrelated sections should have appropriate separation.

Do not:

- Place every field inside an individual card.
- Use oversized padding around compact operational information.
- Reduce text or control sizes below comfortable accessibility thresholds.
- Add blank space merely to imitate a modern web interface.

Prefer shared spacing tokens over arbitrary margins.

---

## 8. Visual hierarchy

Every workspace should have a clear purpose.

Prefer:

- One clear page title.
- One obvious primary action where applicable.
- Visually quieter secondary actions.
- Consistent section headings.
- Operational values emphasized above supporting metadata.
- Compact status indicators.
- Clear distinctions between editable and read-only information.

Avoid:

- Multiple competing primary buttons.
- Excessive bold text.
- Oversized status messages.
- Large empty-state headings.
- Repeated descriptions that add no useful information.
- Badges for ordinary labels.
- Excessive uppercase text.

Use color, weight, size, and spacing intentionally.

Do not rely on color alone to communicate meaning.

---

## 9. Page headers and toolbars

Maintain consistent page-header and toolbar patterns across workspaces.

Where appropriate, include:

- Page title.
- Relevant operating date.
- Applicable status.
- Primary action.
- Secondary actions.
- Search and filtering controls.

Avoid adding controls merely to fill available space.

Keep actions close to the information or workflow they affect.

Use approved action labels and ordering.

Do not duplicate the same action in several prominent locations without a clear usability reason.

Do not add online-refresh controls to unrelated workspaces merely because they existed in older designs.

---

## 10. Date navigation

Preserve the approved compact date-navigation experience.

Requirements:

- Clearly readable selected operating date.
- Consistent date formatting.
- Previous and next date actions.
- Calendar selection.
- Today action where approved.
- Accessible names for icon-only controls.
- Keyboard interaction.
- Appropriate tooltips.
- Consistent date-selector dimensions.

Use the established Eastern operating-date rules.

Do not introduce unnecessary date badges or duplicate date labels.

Do not independently recalculate operating dates using the computer's local timezone.

Changing the selected date must remain a local operation.

Date navigation must not initiate online retrieval.

Preserve date-switch safety for drafts, autosave, and PDF preview.

---

## 11. Forms and editing

Align labels and controls consistently.

Prefer:

- Compact field spacing.
- Clearly associated labels.
- Logical field grouping.
- Predictable tab order.
- Inline validation.
- Appropriate input widths.
- Clear editable and read-only states.

Multiline fields must receive sufficient space for realistic content.

Numeric inputs should clearly indicate their meaning and applicable units.

Do not use placeholder text as the only field label.

Do not introduce modal dialogs for ordinary validation errors.

Preserve:

- Existing bindings.
- Validation rules.
- Commands.
- Keyboard shortcuts.
- Save behavior.
- Unsaved-change protection.
- Focus behavior.
- Data ownership.

Changing the visual presentation of an editor must not change how its values are stored.

---

## 12. Buttons, icons, and interaction states

Use existing shared button styles and icon resources.

Distinguish:

- Primary actions.
- Secondary actions.
- Tertiary actions.
- Destructive actions.
- Disabled actions.
- Toggle states.

Icon-only controls must have appropriate accessible names and useful tooltips.

Critical or unfamiliar actions should use clear text labels.

Use consistent:

- Icon sizes.
- Stroke weights.
- Alignment.
- Button heights.
- Hover states.
- Pressed states.
- Focus states.
- Disabled appearance.

Do not introduce emoji-based icons or inconsistent external icon sets.

Do not make controls appear interactive when they perform no action.

---

## 13. Associate Passdown workspace

Preserve the approved workspace structure:

- Overview.
- Products & Pricing.
- Shows.
- Notes.
- Live PDF companion.

The workspace must maintain one authoritative operating date and draft state.

Visual refinements must preserve:

- Date navigation.
- Draft status.
- Autosave.
- Create PDF.
- Document actions.
- Manual overrides.
- Product selections.
- Show selections.
- Daily Feature.
- ERT.
- Operational notes.
- Document inclusion preferences.
- PDF preview behavior.

Changing tabs must not:

- Reset draft information.
- Trigger unnecessary retrieval.
- Lose editing state.
- Introduce duplicate save workflows.
- Generate unnecessary document previews.

Do not redesign an individual section in isolation if the change breaks the consistency of the complete workspace.

---

## 14. Products and pricing

Prefer compact, readable rows and grouped lists.

Clearly distinguish:

- Product name.
- Official price.
- Manual display-price override.
- Pricing qualifier.
- Comparison or reference price.
- Pricing unit.
- Verification state.
- Inclusion state.
- Custom or internal product status.

Do not place every product inside a large individual card.

Preserve:

- Product grouping.
- Search.
- Collapsible groups.
- Stable product identity.
- Official source details.
- Price history.
- Membership and Season Pass distinctions.
- Document inclusion rules.
- Seasonal visibility.

Official prices must remain visually and functionally distinct from editable overrides.

Custom internal products must remain distinguishable from official products.

Do not visually imply that a manual override is an official price.

### Anchored price editor

Preserve the approved anchored price-editing interaction.

Requirements:

- Clear association with the selected product.
- Readable official price.
- Validated numeric override input.
- Save action.
- Cancel action.
- Restore official value action.
- Predictable keyboard interaction.
- Appropriate focus restoration.
- Safe dismissal when the editing context becomes invalid.

Do not save merely because the popup closes.

Do not allow stale popups to remain associated with recycled, hidden, or unrelated product rows.

---

## 15. Shows and entertainment

Present shows in a compact, readable operational layout.

Preserve:

- Official show names.
- Verified operating dates.
- Existing inclusion controls.
- Show-time disclosure behavior.
- Appropriate schedule status.
- Clear distinction between available and unavailable information.

Avoid excessive containers around each show.

Do not fabricate schedules, times, or locations to improve visual completeness.

---

## 16. Operational Notes

Preserve the approved independent Operational Notes workspace.

Prefer:

- Searchable lists.
- Compact filters.
- Clear note summaries.
- Predictable list selection.
- Appropriate details presentation.
- Consistent create and edit dialogs.

Long note content should remain readable and scrollable.

Preserve note lifecycle and synchronization behavior.

Persistent Operational Notes and included Passdown copies must remain visually and functionally distinct.

Do not introduce editing interactions that silently modify previously included frozen copies.

---

## 17. Dashboard

The Dashboard should provide an efficient operational overview.

Prioritize:

- Important operational values.
- Relevant operating information.
- Clear status.
- Current operational context.
- Useful navigation.
- Minimal unnecessary decoration.

Avoid filling space with speculative statistics or meaningless metrics.

Use actual application data.

Do not introduce placeholder charts, fabricated values, or unsupported operational indicators.

Follow the approved Dashboard reference.

If an approved replacement reference is missing, preserve the existing Dashboard and report the missing dependency.

---

## 18. Operations Calendar

Preserve the approved Calendar workspace.

Requirements:

- Consistent calendar grid.
- Clear month navigation.
- Visible selected-date state.
- Clear distinction between today and selected date.
- Readable date cells.
- Compact operating information.
- Consistent event markers.
- Appropriate selected-date details panel.
- Predictable shortcut actions.

Use text, shape, icons, or other visual distinctions alongside color where necessary.

Preserve the established Show Pass Perks preference.

Do not overload date cells with excessive text.

Do not introduce horizontal scrolling into the normal Calendar layout.

Calendar navigation must remain local and must not initiate online retrieval.

---

## 19. Finalized History

Preserve the read-only nature of finalized history.

Use:

- Compact searchable record lists.
- Appropriate filters.
- Clear historical metadata.
- Readable record details.
- Consistent document actions.

Preserve approved actions such as:

- Open Final PDF.
- Save PDF Copy.

Do not introduce:

- Reopen actions.
- Editable historical fields.
- Regenerate-from-current-data actions.
- Unapproved historical deletion controls.

Historical information must remain distinguishable from editable Passdown drafts.

---

## 20. Settings

Preserve the approved Settings organization and terminology.

Use consistent:

- Section headings.
- Settings rows.
- Radio groups.
- Switches.
- Supporting descriptions.
- Status indicators.
- Action buttons.

Use switches for genuine binary preferences.

Use radio groups or appropriate selection controls for mutually exclusive options.

Avoid placing unrelated preferences inside the same group.

Maintain clear distinctions between:

- Device-local preferences.
- Passdown defaults.
- Data refresh information.
- Scheduling configuration.
- Personal Sync.
- Application updates.
- Application information.

Do not add unnecessary technical diagnostics or duplicate operational actions to Settings.

---

## 21. PDF companion

The live PDF preview must display the actual generated document.

Preserve:

- Existing document-generation pipeline.
- Preview scheduling.
- Page navigation.
- Zoom.
- Fit behavior.
- Visibility controls.
- Preview layout preferences.
- Correct operating-date association.
- Stale-result protection.

The preview should occupy a useful amount of space without unnecessarily restricting the editor.

Avoid excessive preview borders, duplicated toolbars, and unnecessary surrounding decoration.

Do not:

- Replace the live PDF with a simulated mockup.
- Alter PDF content solely to improve its appearance inside the application.
- Introduce another PDF-generation implementation.
- Regenerate documents for unrelated presentation-only changes.
- Reintroduce unnecessary preview delays.
- Display a stale PDF as current.

UI polish must not change approved printed document styling unless the requested task explicitly includes document design. For authorized document design work, use the `pdf-design` skill.

---

## 22. Dialogs, popovers, and overlays

Use dialogs only when the interaction warrants a separate focused surface.

Prefer inline or anchored editing for small contextual changes.

Requirements:

- Clear title where appropriate.
- Clear purpose.
- Predictable primary and secondary actions.
- Appropriate validation.
- Keyboard accessibility.
- Escape behavior where applicable.
- Safe dismissal.
- Focus containment when modal.
- Focus restoration after closing.
- Appropriate sizing.
- Light and Dark appearance support.

Do not allow background interactions that invalidate an active editor.

Do not silently discard meaningful unsaved changes.

Avoid nested modal dialogs.

Do not use overlays merely for decorative effects.

---

## 23. Empty, loading, error, and status states

Every relevant workspace should provide understandable states for:

- No available records.
- No matching search results.
- Information not checked.
- Unpublished information.
- Unavailable information.
- Stale information.
- Loading.
- Saving.
- Recoverable failure.

Use concise, nontechnical language.

Distinguish between:

- An empty result.
- Missing information.
- An ongoing operation.
- A failed operation.

Do not make an unavailable value look like a verified zero.

Do not use oversized empty-state illustrations or headings in compact operational areas.

Avoid unnecessary loading indicators for operations that complete almost immediately.

Preserve existing measured delayed-indicator behavior where applicable.

Status announcements should be useful without excessive repetition.

---

## 24. Appearance and theming

Support:

- System appearance.
- Light appearance.
- Dark appearance.

Use established Avalonia theme resources and semantic application tokens.

Verify readability of:

- Primary text.
- Secondary text.
- Disabled controls.
- Placeholders.
- Borders.
- Input backgrounds.
- Selected states.
- Hover states.
- Popovers.
- Dialogs.
- Tooltips.
- Calendar markers.
- Warning and error indicators.
- Document-preview surroundings.

Do not assume that a hard-coded Light appearance color remains readable in Dark appearance.

Avoid introducing independent theme controllers or preference stores.

Preserve the existing appearance infrastructure.

Respect Windows high-contrast behavior.

---

## 25. Motion and animation

Use the established interface-motion infrastructure.

Animations should be:

- Short.
- Purposeful.
- Subtle.
- Consistent.
- Interruptible where appropriate.

Appropriate uses include approved:

- Sidebar transitions.
- Disclosure transitions.
- Workspace transitions.
- Selection feedback.
- Contextual surface appearance.

Avoid:

- Decorative looping animations.
- Continuous motion.
- Large-scale movement.
- Excessive fades.
- Unnecessary layout animations.
- Animations that delay user actions.

Respect:

- The interface animations preference.
- Operating-system reduced-motion preferences.
- Existing motion tokens.

Disabling animations must preserve complete functionality.

Never delay commands, autosave, navigation, input handling, document generation, or state updates merely to wait for an animation.

---

## 26. Accessibility

All interactive controls must be keyboard reachable where appropriate.

Maintain:

- Visible focus indicators.
- Predictable tab order.
- Accessible control names.
- Meaningful labels.
- Appropriate selection behavior.
- Keyboard activation.
- Logical focus restoration.
- Screen-reader-compatible status information.
- Readable contrast.

Critical information must not depend solely on:

- Color.
- Icon shape.
- Tooltip text.
- Animation.
- Mouse hover.

Icon-only date-navigation controls require accessible names.

Test keyboard interactions for:

- Sidebar navigation.
- Workspace tabs.
- Search and filtering.
- Date selection.
- Product rows.
- Price editing.
- Dialogs.
- Popovers.
- Settings controls.
- Document-preview actions.

Do not introduce focus traps outside legitimate modal interactions.

Do not remove focus indicators for aesthetic reasons.

---

## 27. Display scaling and window sizes

Design and validate for Windows display scaling at:

- 100%.
- 125%.
- 150%.

Also inspect behavior at the established minimum supported window size and representative larger desktop sizes.

Verify:

- Text does not clip.
- Controls remain reachable.
- Icons remain appropriately aligned.
- Toolbars remain usable.
- Popovers remain within available screen bounds.
- Dialog actions remain accessible.
- Navigation remains readable.
- Calendar cells remain functional.
- PDF preview remains usable.
- Scroll behavior remains predictable.

Avoid fixed-height containers that clip text when scaling changes.

Do not assume headless rendering proves real Windows DPI behavior.

Flag multi-monitor and native scaling behavior for Windows validation where applicable.

---

## 28. Architecture and behavior preservation

UI polish must preserve established architectural boundaries.

ViewModels may own:

- Presentation state.
- Selection.
- Commands.
- UI coordination.
- Validation presentation.
- Interaction state.
- Workspace coordination.

Domain and Application remain responsible for business rules and application use cases.

Code-behind may handle legitimate presentation concerns, including:

- Focus.
- Layout.
- Keyboard interaction.
- Native control integration.
- Presentation lifecycle events.

Do not move persistence, retrieval, business logic, or PDF generation into view code-behind.

Do not introduce duplicate state ownership merely to support a visual change.

Do not introduce new dependencies without a demonstrated need.

Prefer existing shared components over repeated implementations.

---

## 29. Performance safeguards

Preserve existing measured performance improvements.

UI changes must not introduce:

- Unnecessary HTTP requests.
- Additional database reads for presentation-only changes.
- Repeated expensive workspace composition.
- Unnecessary PDF generation.
- Duplicate event subscriptions.
- Unbounded visual caches.
- Excessive layout recalculation.
- Expensive work on pointer movement.
- Unnecessary timers.
- Resource leaks.
- Artificial input delays.

Use appropriate virtualization where justified by realistic collection sizes and supported control behavior.

Do not introduce speculative performance abstractions without evidence.

Use existing performance harnesses where changes affect responsiveness.

Distinguish measured performance from assumptions.

---

## 30. Visual validation process

For every UI polish task:

1. Read the applicable product and architecture requirements.
2. Inspect the existing view, bindings, styles, and behavior.
3. Inspect approved visual references.
4. Establish a baseline screenshot or equivalent visual evidence where possible.
5. Identify specific layout, hierarchy, accessibility, or consistency issues.
6. Plan focused improvements.
7. Reuse existing design resources and shared components.
8. Implement structural layout improvements.
9. Refine spacing, typography, styling, and interaction states.
10. Preserve existing functional behavior.
11. Run relevant UI and regression tests.
12. Run the applicable build.
13. Render or launch the actual application where available.
14. Inspect actual rendering.
15. Compare against the approved reference.
16. Review Light and Dark appearance.
17. Review keyboard navigation and focus.
18. Review supported window sizes and scaling.
19. Investigate and correct observed regressions.
20. Review the implementation diff.
21. Update affected documentation.
22. Report results and stop.

Do not claim visual success based solely on a clean build or passing automated tests.

---

## 31. Visual fixtures

Reuse the existing visual-fixture infrastructure.

Use realistic synthetic scenarios covering relevant:

- Typical operating dates.
- Closed dates.
- Unpublished dates.
- Private buyouts.
- Heavy product lists.
- Seasonal products.
- Empty lists.
- Long notes.
- Validation errors.
- Stale information.
- Expanded and collapsed navigation.
- Light and Dark appearance.
- Reduced motion.

Do not use fabricated information as production data.

Do not include private employee information, user drafts, or operational secrets in committed fixtures.

Inspect the actual rendered output rather than relying exclusively on markup.

Preserve existing visual-fixture conventions (`tools/visual-fixtures`, see its `README.md`). Render one scenario per process; a single-process full run has grown to many gigabytes. Headless Skia renders are development evidence, not Windows DPI or accessibility validation.

---

## 32. Windows validation

The target platform is Windows 11 x64.

Development-host screenshots and Avalonia headless rendering are useful but do not replace real Windows validation.

Explicitly identify Windows-specific checks involving:

- Native window chrome.
- Windows snapping.
- Display scaling.
- Multiple monitors.
- Font rendering.
- Keyboard navigation.
- Narrator.
- High contrast.
- Native printing.
- Popup placement.
- Actual perceived responsiveness.

When Windows validation cannot be performed:

- Report the limitation.
- Provide a focused validation checklist.
- Preserve the distinction between implementation and validation.
- Do not claim Windows approval.

Use the established local-test packaging process when a Windows validation package is required.

---

## 33. Testing safeguards

Follow the repository's current documented test strategy.

Preserve:

- Existing UI regression tests.
- Architecture dependency tests.
- Functional tests.
- Visual-fixture coverage.
- Headless test isolation.
- Existing resource and memory guards.

Do not remove or weaken tests merely because the visual implementation changed.

Update obsolete visual assertions only when the approved design justifies the change.

Investigate headless hangs, excessive memory usage, and intermittent failures.

Do not treat a headless-platform problem as proof of a production Windows defect without supporting evidence.

Do not treat a passing headless test as proof of visual correctness.

---

## 34. Documentation maintenance

When UI work changes architectural or product contracts, update the applicable documentation in the same change set.

Review:

- `AGENT_BUILD_SPEC.md`
- `docs/architecture.md`
- Relevant subsystem architecture documents.
- Applicable design references.
- Current phase records.

Document meaningful changes to:

- Workspace ownership.
- Navigation.
- Shared controls.
- Design resources.
- Appearance.
- Motion.
- Accessibility.
- Layout preferences.
- User-facing behavior.

Do not turn architecture documentation into a collection of incidental spacing adjustments.

Preserve historical design decisions separately from current-state documentation.

Record whether product specification and architecture updates were required.

---

## 35. Completion criteria

A UI polish task is complete only when applicable requirements have been satisfied:

- Approved visual direction preserved.
- Existing functionality preserved.
- Real bindings and commands preserved.
- Data ownership unchanged.
- No unintended online retrieval.
- No unintended persistence changes.
- No unintended PDF changes.
- Shared design resources used appropriately.
- Layout remains usable.
- Light and Dark appearance remain readable.
- Keyboard accessibility preserved.
- Focus behavior verified.
- Motion preferences respected.
- Relevant tests passed.
- Build passed.
- Actual rendering inspected where possible.
- Windows-specific limitations documented.
- Required documentation reviewed.
- Complete implementation diff reviewed.

Do not declare an entire phase complete when only a visual subtask has been implemented.

---

## 36. Completion report

Report:

1. Requested visual scope.
2. Baseline issues identified.
3. Approved references used.
4. Views and shared resources changed.
5. Visual and interaction improvements.
6. Functional behavior preserved.
7. Accessibility checks.
8. Appearance and scaling checks.
9. Performance considerations.
10. Test and build results.
11. Visual evidence inspected.
12. Windows validation status.
13. Product specification review result.
14. Architecture documentation review result.
15. Remaining limitations and blockers.

Clearly distinguish implemented, visually inspected, Windows-validated, and product-owner-approved work.

Never fabricate screenshots, validation results, or performance measurements.

Stop after completing the authorized UI polish task.

---

## Final design standard

The application should feel cohesive, efficient, professional, accessible, and intentionally designed for everyday Carowinds operations.

Prefer clarity over decoration.

Prefer consistency over novelty.

Prefer compact usability over unnecessary whitespace.

Prefer shared design resources over isolated styling.

Prefer measured improvements over speculative optimization.

Preserve operational behavior, user data, and approved product decisions.

Visual quality is demonstrated through actual rendering and interaction evidence, not through markup complexity or passing builds alone.
