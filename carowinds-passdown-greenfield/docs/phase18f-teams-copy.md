# Phase 18F — Teams copy actions and empty Daily Feature

## Copy ▾ menu (Associate screen)

One compact **Copy ▾** button sits next to **Create PDF** in the Associate toolbar. Its menu has:

| Action | Copies |
| --- | --- |
| Operations Summary | park hours, effective gate times, projected attendance, pre-solds, Early Ride Time |
| Today's Pricing | only the products/sections selected for the PDF |
| Operations + Pricing | Operations Summary, a blank line, then Today's Pricing |

After a copy, a small muted line under the toolbar says what was copied (for example
"Today's pricing copied"). There is no dialog. A clipboard failure shows
"Couldn't copy to the clipboard. Try again." in the error color, logs the technical exception, and
never touches the draft. The message clears when the selected date changes.

Keyboard: Tab to **Copy ▾**, press Enter or Space to open the menu, arrow keys to choose, Enter to
copy. The button and every item have screen-reader names; nothing is hover-only.

## What Copy reads — and never does

Copy uses the currently open date's on-screen state, including edits not yet autosaved. It builds the
same `AssociatePassdownDocumentModel` that Create PDF renders
(`AssociatePassdownDocumentModelBuilder.Build`), from the loaded workspace with the current editable
fields applied in memory (`AssociateViewModel.WithDraftApplied`, the same projection autosave uses).

Copy never retrieves, checks online, refreshes, calls HTTP, saves, finalizes, or changes the selected
date. It does not read finalized History.

## Pricing follows the PDF exactly

```
Associate selected product state
          ↓
AssociatePassdownDocumentModelBuilder  (section toggles, per-product include, suppression,
          ↓                             section resolution/de-duplication, ordering,
   ┌──────┴──────┐                      effective price incl. override, "From $x")
   ↓             ↓
  PDF       AssociateTeamsTextFormatter (formatting only)
```

`AssociateTeamsTextFormatter` (Documents project) only formats `model.ProductCategories`. It has no
catalog, section, Conjuring or Add-On logic of its own, so:

- an unselected section (Daily Admission, Fast Lane, Promotions, Season Passes, SCarowinds, Add-Ons)
  is omitted entirely;
- a product deselected for the PDF is not copied (the loaded catalog is unchanged);
- SCarowinds products — including The Conjuring — are copied only when SCarowinds is selected;
- Add-Ons are copied only when Add-Ons is selected;
- included custom/internal items are copied under the PDF's "Custom / Internal Items" heading;
- the price is the PDF's own text: `$59.00`, or `From $37.00` for a starting-from price. No comparison
  ("was") price, struck-through value, or "Only"/"Online Price"/"Sale Price" wording.

Clipboard-only rules (formatting, not inclusion): a product with no price (the PDF prints "-") is left
out of the copy, and a section left with no priced products is left out with its heading. If nothing
priced remains, Today's Pricing copies nothing and says "No pricing selected"; Operations + Pricing
copies the operations summary alone and says "Operations summary copied (no pricing selected)".

## Formats

Plain text, CRLF line breaks, no Markdown tables, no columns — legible even if Teams strips
formatting. US formatting is fixed (`PresentationCulture`), independent of the Windows region.

Operations Summary:

```
Park Hours: 11:00 AM - 12:00 AM
Avenue: 10:15 AM
North Gate: 10:30 AM
Projected Attendance: 18,000
Pre-solds: 1,000
ERT: Thunder Striker and Skytower
```

- Gate times are effective values (override ?? calculated default) and are never labeled as overrides.
- Closed day: `Park Hours: Closed`, no gate lines. Hours not yet known: `Park Hours: Not checked yet`,
  no gate lines.
- Attendance/pre-solds lines are omitted when not entered.
- ERT: rides only, joined naturally (`A`, `A and B`, `A, B, and C`); `ERT: None` for a confirmed
  no-ERT day; `ERT: Not configured` when no ERT is set up for the month.

Today's Pricing:

```
Ticket Prices:

Daily Admission
Park Ticket: $59.00
Military/First Responder: From $37.00

Fast Lane
Fast Lane Reserve: $65.00
Fast Lane Ultimate: $109.00
```

Section headings are the PDF's section headings; product names are the PDF's display names.

## Not included (intentional)

- Daily Feature, Operational Notes, and reminders are not part of any of the three actions. A future
  "Full Teams Text" action may add them.
- No real Teams @mentions: "@Name" in plain clipboard text does not create a Teams identity mention.
  No Microsoft Graph, Teams API, OAuth, Azure app registration, directory lookup, or Power Automate.
- No DAP/placement images, break-plan assignments, or full Teams message automation.

## Empty Daily Feature

See `docs/associate-passdown-templates.md`, "Phase 18F — empty Daily Feature is omitted".

## Version / schema impact

DB 15, Draft 4, Snapshot 2, SyncSchemaVersion 1 — all unchanged. No migration. Copy output is derived
and never persisted or synced; product/section selections keep syncing as part of the draft.

## Release

This phase changes shipped code, so RC.1 is superseded for final validation. The next packaged
release candidate is `0.1.0-rc.2`. Release artifacts were not rebuilt in this phase.
