# Carowinds Operations — Product Specification

## 1. Purpose and authority

This document defines the approved product requirements, functionality, business rules, user experience, operational constraints, and acceptance criteria for Carowinds Operations.

Carowinds Operations was originally developed as Carowinds Passdown under the internal project namespace `Carowinds.Passdown`.

The application is now an established Windows desktop product. Development must preserve existing functionality, user data, compatibility, and approved operational behavior.

### Documentation hierarchy

- `AGENT_BUILD_SPEC.md` — authoritative product requirements and acceptance criteria.
- `CLAUDE.md` — mandatory development instructions.
- `.claude/rules/` — specialized engineering and development requirements.
- `docs/architecture.md` — current implemented architecture.
- `docs/architecture/` — detailed subsystem architecture.
- Phase records — historical implementation decisions and validation evidence.
- Release records — release identity, compatibility, and validation evidence.
- Production source and tests — evidence of actual implementation.

Explicitly approved later product decisions supersede conflicting historical requirements.

Implementation behavior does not automatically establish a new product requirement. Discrepancies between approved requirements, documentation, and implementation must be investigated.

Do not silently change product requirements to match accidental implementation behavior.

---

## 2. Product objective

Carowinds Operations is a professional, local-first desktop application supporting Carowinds Admissions operational preparation, communication, and historical reference.

The application enables employees and managers to:

- View official park operating information.
- Review operating schedules and events.
- Prepare date-specific Associate Passdowns.
- Review official products and pricing.
- Maintain operational notes.
- Generate, preview, print, and save professional operational documents.
- Access finalized historical Passdowns.
- Synchronize supported operational information between personal devices when explicitly enabled.
- Receive verified application updates.

The application must remain approachable for nontechnical employees while providing reliable operational information and efficient workflows.

The application must not require an external custom server, artificial intelligence service, OpenAI account, or administrator access for normal operation.

---

## 3. Intended users and operating environment

### 3.1 Users

Primary users:

- Carowinds Admissions managers.
- Admissions supervisors.
- Operational leadership.
- Associates responsible for preparing or reviewing daily operational information.

The interface must not assume technical knowledge.

Use clear operational terminology rather than implementation terminology.

### 3.2 Platform

- Target platform: Windows 11 x64.
- Native desktop application.
- Portable, self-contained distribution.
- No administrator permissions required for normal operation.
- No separately installed runtime required for normal portable distribution.
- No mandatory company account or external service account.
- No required AI integration.

### 3.3 Offline operation

The application must remain usable without an internet connection.

Offline capabilities include:

- Opening the application.
- Viewing previously cached official information.
- Creating and editing Passdown drafts.
- Managing local operational notes.
- Accessing finalized history.
- Generating PDFs from available information.
- Printing and saving documents.

Internet access is required for online functionality such as official data retrieval and application update discovery, but must not be required for ordinary local workflows.

---

## 4. Product scope

### 4.1 Active product areas

The application includes:

1. Dashboard.
2. Operations Calendar.
3. Associate Passdown.
4. Products and Pricing.
5. Operational Notes.
6. Finalized Passdown History.
7. Personal Sync.
8. Settings.
9. PDF Preview and Document Output.
10. Application Updates.

Some capabilities are integrated into other workspaces rather than exposed as independent navigation destinations.

### 4.2 Deferred functionality

The Manager Passdown workflow remains deferred.

Do not implement Manager Passdown functionality without explicit product-owner authorization.

Do not create placeholder Manager navigation, views, view models, forms, or PDF templates.

Preserve existing compatibility infrastructure where required.

### 4.3 Retired functionality

Morning Message is retired.

Do not restore its production interface, services, dependencies, or synchronization channels without explicit authorization.

Preserve compatibility safeguards required for existing installations and historical files.

### 4.4 Approved, not yet implemented

Weather information (§6.6) is approved by the product owner (2026-11-27, recorded in `docs/phase37a-weather-source-architecture-plan.md`). It is not active functionality until an implementation milestone delivers it and that milestone is validated and approved for release. Do not describe it as available before then. Status (2026-11-28): implemented, Windows-accepted as part of the integrated Phase 37E2 test build and released in RC.15 (`0.1.0-rc.15`, GitHub prerelease published 2026-11-28; `docs/releases/rc15-release-preparation.md` §21) (§6.6).

---

## 5. Core operational principles

The following requirements apply throughout the application:

- Never invent official information.
- Never silently substitute another operating date.
- Never present marketing prices as confirmed exact-date prices.
- Never overwrite verified cached information because a retrieval attempt failed.
- Never overwrite official cached values with manual draft edits.
- Never trigger online retrieval merely because the selected date changes.
- Never silently discard manual user entries.
- Never silently resolve conflicting synchronized edits.
- Never modify finalized historical Passdowns.
- Never sacrifice operational accuracy for visual appearance.
- Never present planned or unverified functionality as completed.

When information is unavailable or uncertain, communicate that uncertainty clearly and preserve available verified information.

---

## 6. Official data retrieval

### 6.1 Official sources

Use public, official Carowinds and Six Flags sources.

Supported operational data may include:

- Park operating hours.
- Operating dates and closures.
- Entertainment schedules.
- Special operating events.
- Daily admission products.
- Fast Lane products.
- Seasonal and haunted-attraction products.
- Season Passes.
- Memberships.
- Renewal offers.
- Promotions.
- Add-ons.
- Season Pass add-ons.
- Current Deals.
- Pass Perks.
- Other explicitly approved official operational information.

Do not use third-party ticket websites, search-engine snippets, unofficial estimates, or fabricated fallback values.

Approved exception: weather information comes from the U.S. National Weather Service (§6.6). That exception covers weather only. It does not widen the Carowinds/Six Flags source requirement for ticket pricing, operating schedules, shows, promotions, Pass Perks, or any other park-specific official information.

Official source URLs and structures may change. The application must tolerate individual source failures without losing unrelated operational functionality.

### 6.2 Retrieval strategy

Prefer official structured HTTP/API responses, followed by direct official HTML retrieval where appropriate.

Browser automation is not an active product requirement.

Do not reintroduce Playwright, Chromium, or other browser automation dependencies without an approved technical justification.

Do not use private credentials, bypass authentication, submit payment information, or interact with purchase flows beyond the public retrieval requirements.

### 6.3 Retrieval status

Maintain explicit distinctions between:

- Verified.
- Closed.
- NotPublished.
- Unavailable.
- Error.
- Stale.

Presentation wording may use approved user-friendly equivalents.

An unavailable, unpublished, stale, or failed result must not be presented as currently verified.

An official closure must not be inferred merely because information could not be retrieved.

### 6.4 Source evidence

Retain appropriate source evidence, including:

- Source identity.
- Official source URL.
- Requested operating date, where applicable.
- Confirmed operating date, where applicable.
- Verification timestamp.
- Result status.
- Relevant result details.
- Retrieval strategy.
- Sanitized technical diagnostics.

Present understandable freshness and verification information to users.

Detailed technical errors belong in diagnostic infrastructure rather than ordinary operational workflows.

### 6.5 Exact-date verification

Date-specific information must be verified for the requested operating date.

Reject results that silently fall back to today, a default date, or another operating date.

A starting-at marketing price is not an exact-date price.

Do not treat a successfully retrieved response as valid for the requested date without appropriate verification.

### 6.6 Weather information

Status: approved; released in RC.15 (2026-11-28, prerelease) (§4.4). Implemented locally: the Dashboard summary (Phase 37C, `docs/phase37c-dashboard-weather-integration.md`), the Passdown PDF Weather Outlook (Phase 37F, `docs/phase37f-compact-pdf-weather-outlook.md`), and the Calendar selected-date forecast and the Settings control for the Weather Outlook preference (Phase 37D, `docs/phase37d-calendar-weather-settings-integration.md`). The product owner accepted the integrated Phase 37E2 Windows test build (2026-11-28) and authorized RC.15 release preparation (`docs/releases/rc15-release-preparation.md`); the owner then authorized publication, and RC.15 was published as a GitHub prerelease on 2026-11-28 (record §21); the genuine RC.14 → RC.15 Windows update is pending owner confirmation. Authority: the product owner's approved specification amendment of 2026-11-27, recorded in `docs/phase37a-weather-source-architecture-plan.md`, which also holds the source research and architecture plan.

**Source.** Carowinds Operations may retrieve and display publicly available weather forecasts, observations, and official weather alerts from the U.S. National Weather Service (NWS/NOAA). NWS is an explicitly approved source for weather information and the primary provider for the initial implementation.

**Location.** Weather information is associated with Carowinds' fixed geographic location, with appropriate coverage of both the North Carolina and South Carolina portions of the property. Official alerts must reflect NWS geographic applicability for both portions. Never use the computer's location, GPS, or a location permission. Coverage uses two fixed reference points inside the property, one in North Carolina and one in South Carolina; their coordinates and NWS alert applicability must be verified before weather retrieval is enabled, and an unverified coordinate must never be treated as accepted (owner decision D8, 2026-11-27).

**Presentation and integrity.** Weather data must be:

- Attributed to its source.
- Timestamped.
- Cached responsibly, respecting the provider's caching guidance and avoiding unnecessary requests.
- Clearly labelled as an observation, a forecast, or an active official alert, and clearly marked when stale or unavailable.

A station observation is not a measurement taken at the park and must be identified by its station and time. A forecast value must never be presented as a current observation. A failed retrieval must not replace previously retrieved weather with fabricated or empty values. An expired alert must never be presented as active. Weather must not be shown for dates outside the provider's available forecast, and current weather must not be presented as a historical observation.

**Informational only.** Weather information must not automatically determine park closures, ride operations, lightning clearance, or other operational safety decisions. It does not replace official warning channels or established park procedures, and the application must not present itself as an emergency-warning or continuous-monitoring system.

**Initial scope.** A compact Dashboard summary (§8) and forecast details for the Calendar's selected date (§9). Date selection and navigation remain local operations (§7.1).

**Passdown PDF Weather Outlook.** Approved by the product owner on 2026-11-27 (brief "Phase 37F — Compact, Park-Hours-Aware PDF Weather Outlook", which also withdrew the PDF alert example of its single-day mock-up). Newly generated Associate Passdown PDFs (§15) may include a compact, informational forecast summary:

- It describes the selected Passdown date's effective park operating hours (official hours, or the manual or private-buyout hours that apply), in Eastern time, including hours that end after midnight. When the PDF is created after the park has opened, it covers the rest of the hours and says which period it covers.
- It may show: a small "Weather" label, one vector icon (never an emoji), the lowest and highest hourly forecast temperature over those hours, the dominant forecast condition, the highest hourly precipitation probability (labelled "up to", never as the chance of rain for the whole day), the approximate time precipitation becomes more likely only when the hourly forecast shows a clear rise ("possible after"), and a small NWS attribution with the forecast's issue time (or its retrieval time, labelled as such, when NWS gives none). A value the forecast does not give for every covered hour is left out; nothing is estimated or invented.
- It never shows NWS watches, warnings, advisories, alert badges, banners or expiry times, station observations, radar or graphs. Active alerts remain the Dashboard's responsibility.
- It is omitted, without reserving space, when the date has no effective park hours (closed or not checked; no other period is substituted), when no hourly forecast is cached, when the forecast was retrieved more than three hours earlier, when the hours have ended, or when the hourly forecast does not cover every remaining operating hour (beyond the NWS horizon or a gap). Weather never prevents a PDF from being created.
- It fits the existing header of each template (Classic, Modern, Compact) without adding a row, a page or header height; when even its most compact form does not fit, it is omitted. It reads in black and white.
- Each generation uses one immutable weather summary captured with the rest of the document for that document's own date; creating, previewing, printing or saving a PDF never requests weather, and a weather refresh can never change a document already being generated. A weather update alone does not redraw a preview already on screen: Print, Save PDF and Create PDF use the latest cached weather, so they can include newer weather than the preview showed a moment earlier, and the preview then shows the document just printed or saved. Output is never older than the preview and never another date's weather. The RC.15 release notes state this (owner decision D-2, Phase 37E, 2026-11-28, `docs/phase37e-integrated-weather-pricing-release-readiness.md`).
- Weather is not added to drafts, Personal Sync, finalized snapshots or the database. Finalized PDFs keep their stored bytes; scheduled finalization, which runs after the operating day, produces no forecast for past hours.
- **Include Weather Outlook** (owner requirement restated in the Phase 37F follow-up brief, 2026-11-27): a checkbox labelled "Weather Outlook" in the Overview's Include in Passdown choices, directly below Shows, decides whether the outlook is included (moved there from the Actions menu by the owner, 2026-11-27; there is no second control). It is on when nothing has been saved, is remembered on this computer only (a device preference, §16.6: never in drafts, Personal Sync, the database or finalized snapshots), and applies the same way to the live preview, Print, Save PDF and Create PDF; each document captures the setting together with its weather. Changing it refreshes the preview promptly. When it is off, no template prints any weather and each keeps its original layout with no reserved space. It never changes finalized History documents.
- **Settings control** (owner brief "Phase 37D — Calendar Weather Integration + PDF Weather Default Setting", 2026-11-27): Settings → Passdown Defaults shows the same preference as a checkbox labelled "Include Weather Outlook by default", described as "Include the Weather Outlook in Passdown PDFs when a valid forecast is available." It supersedes the earlier statement that the Passdown checkbox is the only control: the two checkboxes are one preference (one stored value, `passdown-weather-outlook.json`; no second preference, no separate default-versus-override). Changing either changes the other at once and refreshes the Passdown preview; the last saved choice applies after a restart; a first-time user starts with it on. A change that cannot be saved still applies until the app closes, and Settings says it was not saved.

Approved presentation decisions (owner decisions D4, D6 and D7, 2026-11-27):

- The Dashboard summary is a compact card at the top of the existing right-hand column, above Daily Ticket Pricing. Its visual design follows the owner-approved mock-up (D4, approved 2026-11-27; identified in `docs/phase37a-weather-source-architecture-plan.md`), which is a visual reference only: its sample values never enter the application.
- Weather-specific manual refresh is offered only in the expanded weather details. No additional global Check Online command is added, and Check Online keeps its official-data meaning. A manual refresh still respects each request's minimum interval and failure backoff, never runs alongside another weather refresh, and reports a failure as a failure.
- The Dashboard's rain chance is the highest hourly NWS precipitation probability for today's park operating hours (from now on, once they have started), or for the rest of the day when the park is closed, has no hours or has closed; the card states that period (approved mock-up, D4).
- Calendar month-grid day cells show no weather; weather appears only in the selected-date panel.
- **Calendar selected-date forecast** (Phase 37D, owner brief of 2026-11-27): a compact "Weather" group in the selected-date panel, after Park information, built only from the cached forecast (selecting dates or months never requests weather). For a date with confirmed park hours it describes those hours (the rest of them once they have started; hours past midnight belong to the operating date), using the Passdown PDF's selection and wording: the condition, the temperature range over the covered hours, the rain chance ("up to", or "possible after" a clear rise), the day's NWS high and low, and a collapsed hourly disclosure for those hours with Eastern times, a vector icon, temperature, condition, rain chance and wind when given. A closed date or a date without confirmed hours is labelled a general forecast for the day and never presented as park hours; when today's hours have ended the rest of the day is shown and labelled. Partial coverage (the forecast ends or has a gap inside the period, or only the 12-hour day forecast reaches the date) is stated; a missing value is left out or said in words, never a zero. Past dates show no weather; dates beyond the forecast say so; an outage or never-retrieved state shows words, not values; a forecast older than the freshness policy is marked "may be out of date". Official alerts appear only from a current alert check and only on dates their own effective and end times cover, as one line each with a pointer to the Dashboard for details; an unchecked or stale alert feed is never read as "no alerts".

**Refresh.** Weather is refreshed only while the application is open, following provider caching guidance, minimum request intervals, error handling, and backoff. Official alerts are checked with a target of every five minutes (owner decision D5).

**Exclusions.** Weather data stays outside Personal Sync, Passdown drafts and finalized History snapshots. PDF output carries only the Passdown Weather Outlook above. Other uses require a future explicitly authorized feature. Alternative providers, forecast-accuracy comparisons, historical weather, and operational automation are outside the approved scope.

**Validation.** Each implementation milestone requires its own validation and release approval (§22.13).

---

## 7. Cache, freshness, and refresh behavior

### 7.1 Local-first access

Changing the selected date must be a local operation.

The application must:

- Load available cached information.
- Load the relevant local Passdown draft.
- Combine applicable catalog information locally.
- Display missing or unverified information accurately.
- Avoid unnecessary network access.

Ordinary navigation, searching, filtering, changing tabs, opening details, and modifying presentation preferences must not trigger unintended online retrieval.

### 7.2 Freshness policies

Maintain separate freshness policies for different official information categories.

Preserve the original approved policy principles:

- Frequently changing dated information is refreshed more frequently.
- Short-term promotions are checked regularly.
- Stable catalog products are refreshed according to their individual due policies.
- Date-independent catalog information is not duplicated or retrieved for every selected date.

The original baseline cadences are:

| Information category | Baseline cadence |
|---|---|
| Park hours | Daily |
| Dated admission | Daily |
| Dated Fast Lane | Daily |
| Seasonal dated products | According to publication and operating season |
| Short-term promotions | Daily |
| Season Passes | Every seven days |
| General and Season Pass add-ons | Every fourteen days |

Later explicitly approved source-specific policies take precedence.

Current precise cadence values, eligibility rules, and scheduling implementation must be maintained in the relevant retrieval architecture and configuration documentation.

Do not silently change freshness policies.

### 7.3 Scheduled refresh

Preserve the established refresh scheduling behavior, including:

- Eastern operating dates.
- Due-only refreshes.
- Daily operational refresh scheduling.
- Rolling date-window retrieval.
- Missed-run handling.
- Scheduled refresh resilience.
- Per-user Windows scheduling where supported.
- Independent source failure handling.
- Last-known-good protection.

The original daily refresh target is 6:00 AM Eastern, with a default rolling window of today and the following six days.

Any later approved scheduling changes supersede these original defaults.

### 7.4 Manual refresh

Provide explicit online-check actions in approved operational locations.

Users must be able to distinguish between selecting a date locally and requesting online verification.

Manual refresh must preserve:

- Unsaved manual draft information.
- Product inclusion selections.
- Display-price overrides.
- Operational notes.
- Applicable workspace state.

Do not reintroduce removed refresh actions in unrelated screens solely because historical specifications mentioned them.

### 7.5 Last-known-good information

A failed retrieval must never destroy a previously verified value.

Retain last success and last attempt independently.

When older verified information is used:

- Communicate its stale status.
- Preserve its source evidence.
- Do not represent it as newly verified.
- Allow applicable user controls over its inclusion in operational documents.

**Previously verified date-specific prices** (owner brief "Pre-RC.15 Pricing Reliability Fix — Preserve Verified Daily Pricing After Park Closing", 2026-11-27). The official dated-ticket calendar stops listing a date once its sales window ends (after closing). A later successful check that only says the date is no longer offered (or not published), with no products, must not erase that date's officially verified dated prices (Daily Admission, Fast Lane, SCarowinds/haunted, Bring-A-Friend, Bring-A-Friend Duo, Military/First Responder):

- Keep the prices verified for that exact date and product, with their original verification time and source.
- Present them as previously verified, never as currently available ("Last verified … — sales availability not confirmed"); the PDF keeps its existing "not recently verified" note. This includes the Calendar's selected-date Daily ticket line (§9.2).
- A new verified official answer for the date still replaces them, including price increases, decreases and products withdrawn while the date is still on sale.
- Retention applies only to the same date, category and product evidence; a date never verified shows no price. Park hours, show schedules and catalog products are unaffected.
- Manual overrides remain draft-only and keep their effective-price role.

### 7.6 Retrieval efficiency

Official retrieval should be efficient and respectful of official services.

Preserve:

- Bounded concurrency.
- Request coalescing.
- Appropriate cancellation.
- Timeouts.
- Independent result handling.
- Due-source planning.
- Source-specific verification.
- Existing measured retrieval optimizations.

Do not introduce unnecessary repeated requests.

---

## 8. Dashboard

The Dashboard provides a concise operational overview for the selected or relevant operating date.

Its purpose is to help employees understand the current operating situation without opening individual editing workspaces.

Dashboard capabilities should follow the existing approved functionality, including applicable:

- Operating date.
- Park operating status.
- Operating hours.
- Relevant operating events.
- Operational summaries.
- Current information and freshness indicators.
- Relevant operational alerts or notes.
- Navigation to associated workflows.

The Dashboard must use real application information rather than static demonstration values.

Dashboard navigation must not trigger unintended online retrieval.

### Dashboard design status

The current Dashboard follows the approved Admissions Overview visual reference, which the product owner supplied after the earlier reference gap and accepted on Windows with the final Phase 35 candidate (`docs/phase35e-dashboard-calendar-redesign.md`, `docs/phase35g-final-uiux-polish-readiness.md`).

Any further Dashboard redesign must follow an explicitly approved visual reference. Do not invent or substitute a generic Dashboard redesign when the approved reference has not been supplied.

Preserve the existing Dashboard functionality until a replacement is authorized.

An approved compact weather summary (§6.6; Phase 37C, released in RC.15) may be added. It must keep the existing Dashboard content intact, must not push essential operations content out of view unnecessarily, and follows an approved visual reference as above.

---

## 9. Operations Calendar

The Operations Calendar provides a date-oriented view of official operating information.

### 9.1 Calendar behavior

Support:

- Month navigation.
- Operating-date selection.
- Today navigation.
- Operating and closed-day distinctions.
- Published operating hours.
- Applicable operating events.
- Relevant seasonal information.
- Applicable Pass Perks indicators.
- Selected-date details.
- Approved shortcuts to related operational workflows.

Operating dates follow Eastern time rather than the computer's arbitrary local timezone.

Calendar navigation and date selection must use local information without automatically performing online retrieval.

### 9.2 Calendar presentation

Preserve the approved Calendar design, including:

- Consistent calendar grid.
- Selected-date details panel.
- Clearly distinguishable event markers.
- Accessible date controls.
- Predictable navigation.
- Appropriate empty and unavailable states.

Do not communicate event or operational status by color alone.

The selected-date Daily ticket line shows a previously verified price (§7.5) with its original verification date, "$49.00 · last verified Nov 27", and an accessible name that adds "availability not confirmed"; other price states keep their existing wording (owner decision D-1, Phase 37E, 2026-11-28, `docs/phase37e-integrated-weather-pricing-release-readiness.md`; released in RC.15).

Approved weather forecast details for the selected date (§6.6; Phase 37D, released in RC.15) appear only in the selected-date panel; they must not crowd the day cells or change the existing open/closed, draft/finalized, or Pass Perks presentation.

### 9.3 Pass Perks

Display supported official Pass Perks information according to its publication, eligibility, applicable dates, and approved presentation behavior.

The Show Pass Perks preference controls its approved visibility across applicable workspaces.

Do not interpret an unsuccessful retrieval as evidence that a previously known perk has expired or disappeared.

Maintain appropriate historical information and provenance.

---

## 10. Associate Passdown

### 10.1 Purpose

The Associate Passdown is a pre-opening operational document intended for front-line Admissions employees.

It must be concise, professional, readable, accurate, and appropriate for ordinary office printing.

The Associate Passdown remains the primary operational document workflow.

### 10.2 Workspace structure

Preserve the approved Passdown workspace organization:

- Overview.
- Products & Pricing.
- Shows.
- Notes.
- Live PDF companion.

The workspace provides:

- Selected operating date.
- Draft and verification status.
- Create PDF functionality.
- Approved document and workspace actions.
- Date navigation.
- Editing controls.

Changing workspace tabs must preserve the current draft and must not trigger unintended online retrieval.

### 10.3 Overview information

Support the approved operational fields, including:

- Selected operating date.
- Official operating hours and operating status.
- Manual hours override.
- Private buyout information.
- Applicable custom hours.
- Projected attendance.
- Pre-solds.
- Early Ride Time.
- Participating ERT attractions.
- Daily Feature.
- Applicable document inclusion preferences.

Preserve existing approved validation and presentation behavior.

### 10.4 Gate opening calculations

Use the effective park opening time.

The effective opening time is determined from verified official hours or an applicable enabled manual override.

The following calculations are mandatory:

- North Gate opening: exactly 30 minutes before effective park opening.
- Avenue opening: exactly 45 minutes before effective park opening.

Recalculate immediately when effective opening time changes.

Handle date-boundary and midnight-crossing calculations correctly.

Do not fabricate gate times when the necessary opening time is unavailable.

### 10.5 Closures and buyouts

Distinguish between:

- Verified official operation.
- Verified official closure.
- Unpublished operating information.
- Unavailable operating information.
- Approved manual overrides.
- Private buyout operation.

A verified closure must remain distinguishable from an unknown operating schedule.

An applicable private-buyout override may establish custom operational hours without altering official cached data.

### 10.6 Early Ride Time

Preserve approved ERT functionality, including:

- Applicable ERT schedules.
- Monthly configuration.
- Date-specific overrides.
- Participating attractions.
- Relevant conflict handling.
- Document inclusion behavior.

Manual ERT configuration must not overwrite unrelated official information.

### 10.7 Daily Feature

Provide the approved Daily Feature editing functionality.

Preserve supported content types, formatting, mathematical presentation, and document behavior.

Daily Feature content is manually authored.

Do not introduce a dependency on an AI service.

### 10.8 Draft lifecycle

Each operating date has its own Associate Passdown draft.

Preserve:

- Automatic local saving.
- Visible save status.
- Save failure handling.
- Date-specific ownership.
- Manual fields.
- Product selections.
- Numeric price overrides.
- Show selections.
- Daily notes.
- Included persistent-note copies.
- Applicable document preferences.
- Conflict protection.

Refreshes must not erase manually entered information.

Changing dates must not mix information between separate drafts.

Pending changes must be handled safely before operations that depend on the latest saved or captured draft.

---

## 11. Products and Pricing

### 11.1 Product categories

Support the established official product categories, including applicable:

- Daily admission.
- Fast Lane.
- Season Passes.
- Memberships.
- Renewal offers.
- Promotions and bundles.
- Current Deals.
- General add-ons.
- Season Pass add-ons.
- Seasonal products.
- Haunted-attraction products.
- Other officially published supported products.

Use approved product grouping and display order.

### 11.2 Official product identity

Maintain stable product identities independently of display price.

Use reliable official identifiers where available.

Preserve appropriate product lifecycle information, including:

- Product identity.
- Display name.
- Category.
- Price and pricing qualifiers.
- Applicable date or season.
- Source evidence.
- Verification status.
- Visibility.
- Product history.
- Confirmed absence or retirement state.

A product name or price change must not unnecessarily destroy its established identity or history.

### 11.3 Product lifecycle

Official product reconciliation must:

- Add newly verified supported products.
- Update verified existing products.
- Preserve historical evidence.
- Distinguish confirmed absence from retrieval failure.
- Hide or archive products according to approved confirmation rules.
- Reactivate returning products appropriately.

An Error must never count as confirmed product absence.

### 11.4 Dynamic seasonal visibility

Seasonal product visibility must follow verified official information and approved seasonal applicability rules.

Do not permanently display irrelevant seasonal sections.

Do not infer seasonal product removal from failed retrieval.

Avoid empty product categories in operational documents.

### 11.5 Official and manual prices

Official prices are read-only from the Passdown editing perspective.

Users may apply approved numeric display-price overrides for their draft.

Requirements:

- Manual overrides must not change official cached values.
- Manual overrides must remain distinguishable from official prices.
- Users must be able to restore the official value.
- Overrides must be associated with the correct draft and product.
- Numeric validation must follow approved pricing rules.
- Do not introduce arbitrary text-price overrides without authorization.

Preserve applicable pricing qualifiers, reference prices, renewal prices, units, and unavailable states.

### 11.6 Price editor

Preserve the approved anchored price-editing interaction.

The editor must support:

- Viewing the applicable official price.
- Entering a valid manual display-price override.
- Saving.
- Cancelling.
- Restoring the official value.
- Appropriate keyboard and focus behavior.
- Safe dismissal when the editing context becomes invalid.

Opening or dismissing an editor must not unintentionally modify a price.

### 11.7 Product controls

Preserve approved controls for:

- Searching products.
- Expanding and collapsing product groups.
- Including and excluding eligible products.
- Editing supported prices.
- Restoring official prices.
- Adding supported custom internal products.
- Viewing product details and applicable official source information.

Not every displayed official product is necessarily eligible for inclusion in the Associate PDF.

Preserve existing approved export eligibility, including the distinction between Memberships and printable Season Pass products.

### 11.8 Price history

Provide access to historical official price observations.

Preserve:

- Product identity.
- Historical price values.
- Applicable price units.
- Observation dates.
- Relevant source information.
- Local and synchronized provenance.
- Supported filtering.
- Official price history compatibility.

Manual draft overrides must not become official price-history observations.

### 11.9 Memberships and offers

Preserve the approved distinction between:

- Season Passes.
- Memberships.
- New-purchase offers.
- Renewal offers.
- Recurring monthly prices.
- Initiation fees.
- Other supported price units.

Do not combine different offer types as if they were equivalent prices.

---

## 12. Shows and Entertainment

Display supported official entertainment schedules for the applicable operating date.

Requirements:

- Preserve exact-date verification.
- Distinguish unavailable or unpublished schedules.
- Present available show times clearly.
- Support approved show inclusion controls.
- Preserve existing show-selection behavior.
- Use approved disclosure controls for detailed show times.
- Preserve draft selections during refresh.

Do not fabricate show names, times, locations, or schedules.

Only include approved and selected show information in the Associate Passdown.

---

## 13. Operational Notes

### 13.1 Persistent notes

Provide an independent Operational Notes workspace.

Users must be able to perform approved note-management operations, including:

- Creating notes.
- Viewing note details.
- Editing applicable notes.
- Searching.
- Filtering.
- Managing note lifecycle states.

Preserve existing note validation, persistence, and synchronization behavior.

### 13.2 Passdown notes

Support date-specific Passdown notes and the inclusion of appropriate persistent Operational Notes.

Persistent notes and their included Passdown copies are separate concepts.

Editing a persistent note must not silently modify an already included frozen copy where the established behavior preserves that copy.

### 13.3 Note presentation

Use the approved searchable list-and-detail presentation.

Preserve:

- Clear note status.
- Accessible dialogs.
- Predictable keyboard navigation.
- Safe editing and dismissal.
- Appropriate empty states.

### 13.4 Privacy

Operational Notes may contain internal operational information.

Do not expose note contents unnecessarily in:

- Diagnostic logs.
- Public reports.
- Release packages.
- Test fixtures.
- Unrelated workspaces.

---

## 14. Finalized Passdown History

### 14.1 Purpose

Finalized History preserves completed operational documents as historical records.

It is distinct from editable Passdown drafts.

### 14.2 Finalization

When a Passdown is finalized, preserve the approved immutable historical information and final PDF.

Finalization must follow the established product workflow and validation requirements.

Do not reconstruct finalized records from current official information.

### 14.3 Immutability

Finalized records must not be modified by:

- Subsequent draft edits.
- Official price changes.
- Updated operating hours.
- Updated show schedules.
- Persistent-note changes.
- Template changes.
- Personal Sync reconciliation.

Do not introduce reopening or editing of finalized history without explicit authorization.

### 14.4 History workspace

Preserve the approved read-only History experience, including:

- Searching supported metadata.
- Filtering.
- Viewing historical record details.
- Opening the stored final PDF.
- Saving a copy of the stored final PDF.

History search must not introduce unintended searches through PDF content or unrelated private note content.

Saving a PDF copy must not modify the original finalized record.

---

## 15. PDF generation and document output

### 15.1 Document purpose

Associate PDFs provide concise operational information for daily use.

Documents must be:

- Professional.
- Readable.
- Operationally accurate.
- Suitable for ordinary office printers.
- Readable in black and white.
- Visually consistent with approved templates.

### 15.2 Page requirements

- US Letter page size.
- Preserve approved Associate document orientation.
- Typical Passdowns should fit within one readable page where practical.
- Support the established multi-page behavior when additional content requires it.
- Do not silently truncate critical operational content.
- Do not sacrifice readability merely to force a document onto one page.

### 15.3 Templates

Preserve the approved document templates.

Template selection must not alter the underlying official information or draft ownership.

Do not modify approved document content, template styling, pagination behavior, or output contracts without authorization.

### 15.4 Document content

Include applicable approved operational information, such as:

- Operating date.
- Park hours.
- Gate opening times.
- Operating status.
- Attendance.
- Pre-solds.
- ERT.
- Included products and prices.
- Selected entertainment information.
- Included operational notes.
- Daily Feature.
- Approved verification and supporting information.
- The compact Weather Outlook for the operating date's park hours, when a valid forecast exists (§6.6; Phase 37F, released in RC.15).

Empty or excluded categories must not introduce unnecessary document clutter.

### 15.5 Live PDF preview

The Passdown workspace provides a live preview of the actual generated PDF.

Preserve:

- Actual document rendering.
- Automatic updates following applicable edits.
- Approved preview scheduling.
- Page navigation.
- Zoom and fit behavior.
- Preview visibility and layout preferences.
- Correct operating-date association.
- Protection against stale preview results.

The live preview must not mutate persisted draft information.

A previously selected date's PDF must not appear as if it belongs to the current date.

### 15.6 Printing and saving

Preserve the approved document actions, including:

- Create PDF.
- Print.
- Save PDF.
- Finalize.
- Copy for Teams, where applicable.

Document output must reflect the latest applicable draft information.

Printing and saving must not silently use an obsolete preview.

Native printing must remain compatible with the target Windows environment.

Failures must preserve the underlying draft and provide understandable recovery guidance.

### 15.7 Finalized documents

Opening or saving a finalized PDF must use the stored final document rather than regenerating it from current information.

---

## 16. Personal Sync

### 16.1 Purpose

Personal Sync provides optional exchange of supported operational information between devices.

It must remain optional.

The application must remain fully usable without enabling Personal Sync.

### 16.2 User control

Users must explicitly configure and enable Personal Sync.

Preserve approved controls for:

- Sync-folder configuration.
- Enablement.
- Synchronization status.
- Supported manual synchronization actions.
- Conflict review.
- Conflict resolution.
- Applicable failure and recovery information.

### 16.3 Supported information

Preserve the established supported synchronization entities, including applicable:

- Associate Passdown drafts.
- Operational Notes.
- Monthly ERT configuration.
- ERT date overrides.
- Finalized Passdown history.
- Official price-history observations.

Additional synchronized entities require explicit approval and compatibility analysis.

Weather information (§6.6) is not synchronized.

### 16.4 Conflict handling

Conflicting edits must not be silently overwritten.

Preserve:

- Explicit conflict detection.
- Supported conflict-resolution choices.
- Revision and identity tracking.
- Deletion and retirement semantics.
- Finalized-record immutability.
- Existing compatibility guarantees.

### 16.5 Local database protection

The live SQLite database and its associated WAL or SHM files must never reside in a cloud-sync folder.

Personal Sync exchanges supported data through the established synchronization format, not by copying or sharing the live database.

### 16.6 Device-local settings

Device-local presentation and configuration preferences must remain local unless synchronization of a particular preference is explicitly approved.

### 16.7 Privacy and compatibility

Do not introduce mandatory cloud services, external accounts, or telemetry.

Do not change established Personal Sync formats without appropriate compatibility review and explicit authorization.

Do not reactivate the retired Morning Message synchronization channel.

---

## 17. Settings and preferences

Settings provides organized access to approved application preferences and configuration.

Preserve the approved Settings organization, including applicable:

- Appearance.
- Interface animations.
- Passdown Defaults.
- Data Refresh information.
- Windows Scheduling.
- Personal Sync.
- Application Updates.
- About.

### 17.1 Appearance

Support:

- System appearance.
- Light appearance.
- Dark appearance.

Appearance changes must preserve readable contrast and consistent visual presentation.

### 17.2 Interface animations

Provide the approved interface animation preference.

Respect operating-system reduced-motion preferences.

Animations must not delay operational commands or interfere with editing, saving, navigation, and document output.

### 17.3 Passdown Defaults

Preserve approved configurable defaults for future Passdown creation and applicable document behavior.

Passdown Defaults includes "Include Weather Outlook by default" (§6.6), the same device preference as the Passdown's Weather Outlook checkbox.

Changing a default must not silently overwrite existing date-specific draft decisions.

### 17.4 Data Refresh

Present approved data freshness and refresh information.

Do not reintroduce removed diagnostics or online-check controls into Settings without authorization.

### 17.5 Preference persistence

Preferences must survive application restarts according to their approved storage behavior.

Device-local preferences must not be automatically synchronized.

---

## 18. User experience and visual design

### 18.1 General direction

The application should feel like a polished, professional internal operations product.

Prioritize:

- Clear hierarchy.
- Efficient use of space.
- Consistent typography.
- Predictable alignment.
- Readable controls.
- Plain-language labels.
- Low visual clutter.
- Fast access to frequently used workflows.
- Clear operational status.

Avoid unnecessary decoration, excessive animation, inconsistent layouts, and technical implementation terminology.

### 18.2 Application shell

Preserve the approved application shell, including:

- Native Windows window behavior.
- Original application icon.
- Approved title-bar appearance.
- Collapsible sidebar navigation.
- Clear active navigation state.
- Consistent page headers.
- Existing workspace organization.

Do not introduce placeholder navigation for deferred features.

### 18.3 Date controls

Preserve approved compact date-navigation behavior.

Date controls must provide:

- Clear selected operating date.
- Previous and next date navigation.
- Calendar selection.
- Today functionality where approved.
- Keyboard accessibility.
- Accurate Eastern operating-date behavior.

Do not trigger network retrieval merely because the date changes.

### 18.4 Shared design system

Use the established shared design system.

Preserve consistent:

- Colors.
- Typography.
- Spacing.
- Control sizes.
- Focus behavior.
- Buttons.
- Status indicators.
- Dialogs.
- Filters.
- Dropdowns.
- Tooltips.
- Motion behavior.

Do not invent a separate visual language for each workspace.

### 18.5 Approved design references

Approved prototypes and visual references establish the desired presentation direction.

They are not authoritative sources of business logic, operational information, or production sample data.

Do not copy placeholder content into production.

Do not substitute an unapproved visual design when a required reference is unavailable.

### 18.6 Accessibility

Support:

- Keyboard navigation.
- Visible focus indicators.
- Predictable tab order.
- Accessible control names.
- Appropriate status announcements.
- Sufficient color contrast.
- Status distinctions beyond color.
- Readable text.
- Light and Dark appearance.
- Reduced-motion preferences.
- Common Windows display-scaling configurations.

Avoid unnecessary focus traps.

Dialogs and popovers must restore focus appropriately.

### 18.7 Responsive behavior

Preserve usable layouts across supported window sizes and Windows display scaling.

Validate applicable behavior at:

- 100% scaling.
- 125% scaling.
- 150% scaling.

Do not claim Windows-specific visual or accessibility validation based only on non-Windows screenshots or headless tests.

---

## 19. Local storage, privacy, and reliability

### 19.1 Local-first storage

Store operational information locally by default.

Use the established versioned SQLite persistence architecture and approved supporting local files.

Do not introduce a server database.

### 19.2 Data ownership

Maintain separation between:

- Official cached information.
- Editable Passdown drafts.
- Manual display-price overrides.
- Persistent Operational Notes.
- Finalized historical records.
- Official price history.
- Device-local preferences.
- Personal Sync exchange files.
- Application update files.

Do not allow one information category to unintentionally overwrite another.

### 19.3 Compatibility and migrations

Preserve:

- Existing user data.
- Historical database migrations.
- Supported upgrade paths.
- Backup requirements.
- Integrity verification.
- Safe handling of unsupported newer schemas.

Do not require users to delete existing data to install an ordinary application update.

### 19.4 Privacy

Do not collect unnecessary personal information.

Do not collect employee credentials, payment information, or company authentication secrets.

Do not expose private operational information through diagnostic exports or release packages.

Use appropriately sanitized or synthetic test data.

### 19.5 Failure recovery

The application must remain usable when:

- Internet access is unavailable.
- An official source changes.
- A source temporarily fails.
- Future information has not been published.
- A product is unavailable.
- PDF output fails.
- A file cannot be accessed.
- Personal Sync temporarily fails.
- Windows scheduling cannot be configured.
- Application update discovery fails.

Communicate what remains usable and what recovery action is available.

---

## 20. Application updates and distribution

### 20.1 Distribution

Provide the established portable Windows 11 x64 distribution.

The ordinary installation experience is:

1. Obtain an authorized application package.
2. Extract the complete package.
3. Launch the application.
4. Use the application without administrator permissions.

Preserve the approved application branding and executable identity.

### 20.2 Package requirements

Release packages must not include:

- Source code.
- Git metadata.
- Build intermediates.
- User databases.
- Personal Sync files.
- Private configuration.
- Signing keys.
- Credentials.
- Unrelated development artifacts.

Include appropriate user instructions and required dependency notices.

Browser runtime distribution is not required by the current product.

### 20.3 Application update experience

Preserve the established update system, including:

- Update discovery.
- Supported update channels.
- Update availability information.
- Release notes.
- Approved reminder behavior.
- Verified package download.
- Installation readiness checks.
- Safe application restart.
- Installation recovery.
- Rollback where supported.

### 20.4 Update integrity

Only trusted, verified application updates may be installed through the automatic update mechanism.

Preserve:

- Signed release manifests.
- Package verification.
- File-integrity verification.
- Version and channel compatibility.
- Protection against unintended downgrades.
- Safe extraction.
- Installation recovery.
- User-data protection.

Do not bypass verification for convenience.

### 20.5 Published release integrity

Published releases are immutable.

Never replace or modify a previously published release under the same version identity.

Clearly distinguish:

- Development builds.
- Local-test packages.
- Release candidates.
- Published releases.

Release publication requires explicit authorization.

---

## 21. Performance requirements

The application should remain responsive during ordinary operational use.

Prioritize:

- Fast application startup.
- Fast local date changes.
- Responsive navigation.
- Immediate ordinary editing feedback.
- Efficient product filtering.
- Responsive Calendar interaction.
- Efficient PDF preview updates.
- Reliable autosave.
- Bounded background activity.
- Efficient official retrieval.
- Stable resource consumption.

The original cached-date navigation target remains under 250 milliseconds on an appropriate Windows office computer.

Later approved performance targets and established benchmark baselines must be maintained in the relevant performance documentation.

Do not introduce artificial delays into ordinary operational interactions.

Background refresh, synchronization, preview generation, and update discovery must not unnecessarily interrupt active editing.

Preserve existing measured performance improvements unless an approved change requires a different approach.

Distinguish development-host measurements from actual Windows performance results.

---

## 22. Product acceptance criteria

### 22.1 Core application

- Application launches on supported Windows 11 x64 systems.
- Ordinary operation requires no administrator privileges.
- No mandatory external custom server, AI service, or account is required.
- Previously saved local information remains available offline.
- Existing user data survives supported upgrades.

### 22.2 Official information

- Official sources are used.
- Exact-date results are associated with the correct operating date.
- Unavailable or unpublished information is not fabricated.
- Failed retrieval preserves last-known-good information.
- Product absence is distinguished from retrieval failure.
- Date selection does not trigger online retrieval.
- Manual refresh preserves draft edits.
- Separate freshness policies remain effective.

### 22.3 Associate Passdown

- Date-specific drafts remain independent.
- Autosave preserves manual information.
- Official and manual hours remain distinguishable.
- Buyout behavior follows approved rules.
- North Gate and Avenue calculations are correct.
- Attendance and pre-solds are retained.
- ERT functionality remains correct.
- Daily Feature functionality remains available.
- Product inclusion and numeric overrides work.
- Show inclusion works.
- Notes and included persistent-note copies behave correctly.
- Changing dates does not mix draft information.

### 22.4 Products and pricing

- Supported official products are grouped correctly.
- Dynamic seasonal visibility works.
- Official prices remain unchanged by manual overrides.
- Price restoration works.
- Dated prices verified earlier for a date remain, marked previously verified, after the official calendar stops listing that date (§7.5).
- Price-history observations remain accurate.
- Membership and Season Pass offer distinctions are preserved.
- Unsupported products do not appear in operational documents.

### 22.5 Operations Calendar

- Calendar navigation works locally.
- Operating dates follow Eastern time.
- Operating status is represented accurately.
- Applicable events and Pass Perks appear correctly.
- Selected-date details remain consistent.
- Date navigation does not trigger unintended retrieval.

### 22.6 Operational Notes

- Notes can be managed through approved workflows.
- Search and filtering work.
- Persistent notes remain independent of frozen included copies.
- Note data survives ordinary application restarts.
- Private note contents are protected.

### 22.7 History

- Finalized records remain immutable.
- Historical metadata can be searched and filtered.
- Stored final PDFs can be opened.
- PDF copies can be saved without modifying the original.
- Current official data does not change historical records.
- No unauthorized reopen or edit functionality is introduced.

### 22.8 PDF output

- Preview represents the actual generated PDF.
- Latest applicable edits appear in document output.
- Stale results do not replace current-date output.
- Typical documents remain readable on US Letter paper.
- Black-and-white readability is maintained.
- Printing and saving work through approved workflows.
- Finalized PDFs remain unchanged.
- Existing approved templates are preserved.
- Where a Weather Outlook prints (§6.6), it adds no page, header height or overlap, shows no alerts, and is omitted when no valid forecast exists.

### 22.9 Personal Sync

- Synchronization remains optional.
- Supported records synchronize according to approved behavior.
- Conflicts are not silently overwritten.
- The live database never resides in the sync folder.
- Existing format compatibility is preserved.
- Device-local preferences remain local.
- Finalized history remains immutable.

### 22.10 Interface and accessibility

- Approved navigation remains consistent.
- Light and Dark themes remain usable.
- Keyboard navigation works.
- Focus is visible and predictable.
- Reduced-motion preferences are respected.
- Status is not communicated by color alone.
- Supported display scaling remains usable.
- Approved interface behavior is preserved.

### 22.11 Updates and release safety

- Update discovery does not prevent normal application use.
- Unverified update packages are rejected.
- Existing user data is protected during updates.
- Installation recovery remains available.
- Published release artifacts are not modified.
- Release packages contain no private user information.

### 22.12 Validation

Relevant automated tests, build checks, and required Windows validation must support claims of product acceptance.

Implementation completion, automated test success, Windows validation, product-owner approval, and release publication are separate statuses.

Do not represent one status as proof of another.

Deferred features are not active acceptance requirements.

### 22.13 Weather information (approved; released in RC.15)

These criteria apply once weather information (§6.6) is implemented:

- Every weather value shows its NWS source and its time.
- Observed, forecast, alert, stale, and unavailable information are distinguishable without relying on color alone.
- Alerts cover both the North Carolina and South Carolina portions of the park, and expired alerts are never shown as active.
- Date selection and navigation cause no weather retrieval; startup, Passdown editing, autosave, PDF preview, printing, and navigation never wait for weather.
- A provider outage or failed refresh keeps last-known-good weather with an honest status and leaves the rest of the application usable.
- No weather data appears in Personal Sync, Passdown drafts, or finalized History snapshots; PDF output carries only the compact Passdown Weather Outlook of §6.6, never an alert.
- No weather information drives an operational decision automatically.

---

## 23. Deferred Manager Passdown requirements

### 23.1 Status

**Deferred — not authorized for implementation.**

The Manager Passdown remains a potential future product capability.

The original detailed Manager requirements must be preserved in the archived greenfield specification.

### 23.2 Intended purpose

The original concept is an end-of-day Admissions communications and operational handoff document.

Potential future requirements include:

- Operating date and management information.
- Opening and closing managers.
- Staffing summaries.
- Operational success scores.
- Location status.
- Department notes.
- Priority issues.
- Operational actions.
- Carryover tasks.
- Training and recognition.
- Closing checklists.
- Management-only information.
- Separate Manager document generation.

These are historical deferred requirements, not authorization to implement them.

### 23.3 Unresolved decisions

Before the Manager workflow is authorized, confirm:

- Whether its audience is Admissions-only or broader park management.
- Required operational fields and privacy boundaries.
- Manager document orientation.
- Appropriate storage and synchronization behavior.
- Approved navigation and presentation.
- Current operational relevance of the historical requirements.

Do not automatically restore the original Phase 5 implementation plan.

---

## 24. Retired Morning Message functionality

Morning Message has been retired from the active product.

The following must not be reintroduced without explicit authorization:

- Morning Message composer.
- Morning Message production navigation.
- Morning Message content-generation workflows.
- Morning Message production dependencies.
- Morning Message synchronization.

Preserve required compatibility safeguards for historical local and synchronized files.

Retirement is not an instruction to delete historical user information.

---

## 25. Product specification maintenance

This specification must remain aligned with explicitly approved product decisions.

Whenever an approved change affects:

- Product functionality.
- Business rules.
- Operational workflows.
- User-facing behavior.
- Product defaults.
- Product scope.
- Data ownership.
- Compatibility requirements.
- Privacy requirements.
- Acceptance criteria.

Update the affected specification in the same change set.

Identify the decision or approved phase record authorizing the change.

### 25.1 Requirements integrity

- Do not document accidental implementation behavior as intended functionality.
- Do not change approved requirements merely to make failing tests pass.
- Do not silently remove historical requirements.
- Explicitly supersede obsolete requirements.
- Preserve relevant historical decisions.
- Distinguish active, proposed, deferred, and retired functionality.
- Do not introduce new product functionality solely through a documentation update.

### 25.2 Architecture separation

Technical implementation details belong in the architecture documentation.

Development instructions belong in `CLAUDE.md` and `.claude/rules/`.

Historical implementation evidence belongs in phase and release records.

This specification should remain focused on product behavior and acceptance requirements.

### 25.3 Phase completion

Every completed phase or subphase must report:

- Product specification updated.
- Product specification reviewed, with no changes required.
- Or product specification discrepancy requiring approval.

Architecture documentation must be reviewed separately.

Do not declare a phase complete while relevant known specification conflicts remain unresolved.

### 25.4 Version-independent requirements

Do not hard-code temporary development information into this specification, including:

- Current Git branch.
- Current commit.
- Current development phase number.
- Current release version.
- Current database schema number.
- Current synchronization format number.
- Current test count.
- Current dependency versions.

Maintain these values in the appropriate current-state architecture, configuration, source, or release documentation.

---

## 26. Historical specification and supersession

This document supersedes the original greenfield Agent Build Specification as the active product specification.

Preserve the original document at:

`docs/archive/original-greenfield-build-spec.md`

The archived specification provides historical context, original operational requirements, and deferred Manager Passdown details.

It must not be treated as an active implementation plan.

In particular, the following historical requirements are superseded:

- Building the current application from scratch.
- Mandatory Manager Passdown implementation.
- Mandatory Manager navigation.
- Playwright browser fallback as a baseline dependency.
- Browser runtime distribution.
- Optional-only historical snapshots.
- Local-only operation without optional Personal Sync.
- The original sequential Phases 1 through 7 implementation plan.
- Original interface recommendations superseded by approved later designs.

Preserve enduring operational requirements unless explicitly superseded by approved later decisions.

When a historical requirement conflicts with this specification or a later approved product decision, investigate and document the applicable authority.

---

## 27. Final product principles

Carowinds Operations must remain:

- Accurate.
- Reliable.
- Responsive.
- Professional.
- Accessible.
- Local-first.
- Privacy-conscious.
- Maintainable.
- Compatible with existing user data.
- Suitable for everyday Carowinds operational use.

Operational correctness and user-data integrity take precedence over convenience, speculative improvements, and unnecessary architectural change.

Preserve what works. Improve deliberately. Implement only approved changes. Document accurately. Validate honestly.