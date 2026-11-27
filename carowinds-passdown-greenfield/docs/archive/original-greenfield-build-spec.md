# Carowinds Passdown — Greenfield Build Specification and Agent Handoff

## 1. Your role

Act as a senior Windows desktop, .NET, Avalonia, data-retrieval, UX, and document-generation engineer. Build a new **Carowinds Passdown** application from the ground up.

Do not build a website wrapper. The finished product must be a real Windows 11 desktop application that can be extracted and run by a nontechnical employee without administrator access, a server, an account, ChatGPT, or an OpenAI sign-in.

Read this entire specification before writing code. Create a clean new solution with deliberate boundaries, tests, and versioned local storage. Do not continue, patch, reskin, or incrementally refactor the previous application. The previous files may be inspected only to understand earlier experiments, official-source behavior, and failure modes. Independently validate every retrieval assumption before using it in the new application.

## 2. Product objective

Build a polished Windows application that creates two kinds of printable operational passdowns for Carowinds Admissions:

1. **Associate Passdown** — a pre-opening, front-line operational sheet.
2. **Manager Passdown** — a closing/end-of-day communications summary for managers.

Both outputs must be visually scannable, professional, energetic without looking childish, readable in black and white, and normally fit on one US Letter page.

The application must retrieve public operating hours and current public product prices from official Carowinds/Six Flags sources, cache them intelligently, support exact future dates where the official checkout supports them, and never invent data when retrieval fails.

## 3. Intended users and environment

- Primary users are Carowinds operations managers and associates who may not be technologically advanced.
- Target operating system: Windows 11.
- The app should run from an extracted ZIP or a per-user installer.
- It must not require administrator permissions.
- It must not require a company login, GPT, OpenAI, or access to an external custom server.
- Company computers may restrict downloads and installation. Prefer a self-contained, portable package.
- The computer may be offline temporarily. Previously verified cached data and saved drafts must remain available.
- Internet access is required only when refreshing official public information.
- Store application data in the current Windows user's local application-data directory.
- Store generated PDFs in a clearly named folder under the user's Documents folder.

## 4. Greenfield project and technology

Create a brand-new solution in a new top-level folder named `carowinds-passdown-greenfield`. Do not place new production code inside the previous `carowinds-data-retrieval-v2` source tree.

Use this baseline stack unless a short written architecture decision demonstrates a materially better Windows-compatible alternative:

- Current supported .NET release and C#.
- Avalonia desktop UI using MVVM.
- `HttpClient` for direct official public endpoints where dependable.
- Microsoft Playwright only as a contained fallback for pages that require browser execution.
- QuestPDF for deterministic PDF generation.
- SQLite or versioned atomic JSON for local-only persistence. SQLite is recommended for separate freshness policies, product history, and draft records.
- A standard .NET test framework for unit, integration, and document tests.

Suggested new solution layout:

```text
carowinds-passdown-greenfield/
  src/
    Carowinds.Passdown.Desktop/
    Carowinds.Passdown.Application/
    Carowinds.Passdown.Domain/
    Carowinds.Passdown.Infrastructure/
    Carowinds.Passdown.Documents/
  tests/
    Carowinds.Passdown.Domain.Tests/
    Carowinds.Passdown.Infrastructure.Tests/
    Carowinds.Passdown.Documents.Tests/
    Carowinds.Passdown.Ui.Tests/
  fixtures/
  docs/
  packaging/
```

The old project is **reference material only**. It may reveal useful official URLs, selectors, status vocabulary, sample outputs, and known problems, but none of its code is automatically trusted. In particular:

- Do not copy its UI or visual design.
- Do not copy its cache model.
- Do not copy its coordinator that opens all sources for every date.
- Do not copy fixed browser delays without proving they are necessary.
- Do not carry forward code-behind architecture into the new UI.
- Do not use old output JSON as proof that a live source still works.
- Do not overwrite or delete the old project; keep it available for before/after comparison.

Useful behavior must be rebuilt cleanly and covered by new tests: exact-date verification, source evidence, per-date drafts, hours overrides, buyout handling, gate calculations, local caching, black-and-white PDFs, and per-user scheduling.

## 5. Non-negotiable data principles

### 5.1 Official sources only

Use public, official Carowinds or Six Flags sources for hours and prices. Do not use search-result snippets, third-party ticket sites, cached search engines, or guessed values.

Current source adapters reference official URLs such as:

- Park hours: `https://www.sixflags.com/carowinds/park-hours`
- Dated tickets: the official `cf-ca.buy.sixflags.com` SnapWizard checkout
- Fast Lane: the official `cf-ca.buy.sixflags.com` FastLaneOnly checkout
- Haunted attractions: the official `cf-ca.buy.sixflags.com` HAPWizard checkout
- Season passes, daily tickets, special offers, Fast Lane, add-ons, and season-pass add-ons: their official pages under `sixflags.com/carowinds`

Official URLs and page structure may change. Keep each source isolated behind an adapter so one broken source does not break the entire application.

### 5.2 Never fabricate or silently substitute

Every source result must use an explicit status:

- `Verified` — exact date or current catalog value was confirmed.
- `Closed` — the official calendar explicitly marks the date closed.
- `NotPublished` — the requested date or schedule is not yet published.
- `Unavailable` — the date exists but that product/value is unavailable.
- `Error` — the source could not be completed.
- Add `Stale` at the presentation/domain level when only older last-known-good information exists.

Never replace `NotPublished`, `Unavailable`, or `Error` with placeholder hours such as 10:00 AM–10:00 PM. Never display a price from a different date as if it were exact-date pricing.

### 5.3 Evidence and traceability

For every retrieved source, retain:

- Source name.
- Source URL.
- Requested date, when applicable.
- Confirmed date, when applicable.
- Time checked in `America/New_York`.
- Result status.
- Human-readable detail.
- Technical error detail for logs.
- Retrieval strategy used, such as direct JSON/API, page DOM, or browser fallback.

Show a simple user-facing “last verified” indicator. Put detailed diagnostics in logs rather than exposing technical messages to ordinary users.

### 5.4 Exact-date verification

For date-specific admission, Fast Lane, and haunted-attraction products:

1. Open the official checkout calendar.
2. Find the requested date.
3. Select it.
4. Confirm the selected date from page state, heading, label, response, or another strong signal.
5. Only then read product names and prices.

Reject a response if the checkout falls back to today, a default date, or another date. “Starting at” marketing prices are not exact-date prices and must be labeled separately.

## 6. Retrieval and cache redesign

### 6.1 Freshness tiers

Do not refresh every source every time the user changes a date. Implement separate cache policies:

| Data group | Scope | Normal refresh cadence | Notes |
|---|---|---:|---|
| Park hours | Per date | Daily at 6:00 AM | Refresh rolling date window. |
| Dated admission tickets | Per date | Daily at 6:00 AM | Prices may change for day-of purchase. |
| Dated Fast Lane products | Per date | Daily at 6:00 AM | Includes Fast Lane Ultimate and reserve products if officially offered. |
| Dated haunted/SCarowinds products | Per date | Daily while published/active | Use lightweight availability checks outside season. |
| Short-term promotions and bundles | Catalog, with validity if known | Daily | These may expire after only a few days. |
| Season passes | Catalog | Every 7 days | Permit manual refresh. |
| General and season-pass add-ons | Catalog | Every 14 days | Permit manual refresh and lightweight change detection. |

Cadences should be configuration, not scattered hard-coded comparisons.

### 6.2 Separate dated data from catalog data

The cache must not duplicate stable add-ons for each date. Use logically separate stores or tables:

- `dated-results` keyed by date and source/category.
- `catalog-results` keyed by normalized product and category.
- `source-state` containing last attempt, last success, next due time, source fingerprint, and failure count.
- `associate-drafts` keyed by date.
- `manager-drafts` keyed by date or operating day/shift.
- `product-history` or archived catalog entries for traceability.

JSON is acceptable for the first version if writes are atomic and models are versioned. SQLite is also acceptable if it materially simplifies querying and migration. Do not introduce a server database.

### 6.3 Fast date changes

Changing the selected date must be an immediate local operation:

- Never open a browser merely because the date changed.
- Load cached date-specific values immediately.
- Merge reusable catalog data locally.
- Display “Not checked yet” if no cache entry exists.
- Provide a clear `Check this date online` action.
- A background refresh may update the view only when the user explicitly started it or when a due scheduled refresh is running.

Target: cached date changes should visually complete in under 250 ms on a normal Windows 11 office computer.

### 6.4 Efficient retrieval

Before relying on browser automation, inspect whether official pages or checkout experiences call stable public JSON/GraphQL endpoints. If a dependable official endpoint is available:

- Prefer it for speed and consistency.
- Preserve required public parameters, headers, locale, and park identifiers.
- Verify exact dates in returned data.
- Do not use private credentials or reverse-engineer authentication.
- Keep browser/DOM retrieval as a fallback.

If browser automation is required:

- Start one browser per refresh job, not per source.
- Reuse the browser context.
- Run independent due sources concurrently with a conservative concurrency limit.
- Block images, video, analytics, and fonts when they are not needed for page behavior.
- Replace fixed sleep delays with specific readiness conditions wherever possible.
- Use bounded retries with jitter for transient navigation/hydration failures.
- Keep independent timeouts per source.
- Cancel cleanly.
- Close pages and browser processes reliably.

Do not let a failure in one source cancel successful results from other sources.

### 6.5 Rolling cache window

At the daily 6:00 AM refresh, prioritize:

1. Today.
2. Tomorrow.
3. The following five days.

The default rolling window is seven days. The architecture should support a configurable longer window without redesign. Future dates outside the cache are checked only on demand.

Refresh results should be saved source-by-source as they complete so a late failure does not discard earlier successes.

### 6.6 Scheduling behavior

The app should refresh due data at 6:00 AM Eastern time when possible.

Support both cases:

- If the app is open, its internal scheduler checks whether the daily job is due.
- If permitted, create a per-user Windows Task Scheduler entry that runs without elevation.

If the computer is asleep or off at 6:00 AM, refresh the next time the app opens if that day's job has not successfully run. Do not run the same full job repeatedly.

Stable catalogs should not refresh merely because the daily job ran; only refresh them when their 7-day or 14-day due time has arrived.

### 6.7 Last-known-good behavior

- A failed source must not overwrite a previously verified value.
- Preserve the latest failed attempt separately from the last successful value.
- When using last-known-good data, clearly show its age and a non-alarming warning.
- For printing, allow the manager to include or exclude stale products.
- Hours must never be presented as currently official if the exact selected date was not verified.

## 7. Dynamic product catalog and seasonal behavior

### 7.1 No hard-coded seasonal visibility

Do not permanently show a SCarowinds or haunted-mazes section. Its visibility must be driven by successful official retrieval.

- Off season: no SCarowinds form section or PDF section.
- When official products become published: automatically create and show the section.
- After official products are removed or expire: automatically hide the section.
- If the website cannot be checked: retain the last-known-good state with a stale indicator; do not infer removal.

Apply the same principle to temporary bundles, America offers, new ticket types, reserve products, and other seasonal products.

### 7.2 Product identity

Do not identify products solely by display price or exact unnormalized heading. Create a stable normalized key using, in priority order:

1. Official product/package ID if available.
2. Official product URL or checkout package identifier.
3. Normalized source + category + product name.

Store at least:

- Stable key.
- Current display name.
- Category.
- Current price and display price.
- Comparison/gate price where explicitly shown.
- Qualifier such as `Starting From`, `Online Price`, or `Sale Price`.
- Date-specific flag.
- Applicable date if date-specific.
- First seen.
- Last seen.
- Last verified.
- Visibility state.
- Source/evidence.
- Consecutive confirmed-absence count.
- Archived date and reason, if archived.

### 7.3 Add, update, and remove rules

- A successfully retrieved new official product appears automatically.
- A price/name change updates the catalog but retains history if practical.
- Never treat an `Error` as product absence.
- If a successful retrieval does not contain a previously present product, perform a confirmation retry or a second successful absence check before archiving it.
- If the official source explicitly marks the product expired/unavailable, it may be hidden immediately.
- Archived products must not print by default but should remain recoverable for diagnostics/history.
- If a product returns later, reactivate its record.

### 7.4 Selected-date visibility

For a selected date, show:

- Exact-date products verified for that date.
- Current non-date-specific catalog products whose source is within freshness policy.
- Seasonal products only if applicable/published.
- Stale catalog items only with a visible warning and user control.

Do not present marketing-page `Starting From` values as the exact price for a selected date. Labels must make the distinction clear.

### 7.5 Manual controls

Managers need simple safeguards:

- Include/exclude checkbox for each product on the PDF.
- Edit displayed price for an internal correction without altering the official cached value.
- Add a custom internal product or event.
- Pin/show a normally hidden item for a specific draft.
- Restore official value.
- `Refresh this section` action for advanced troubleshooting.

Manual draft edits must never overwrite the official cache.

## 8. Associate Passdown requirements

### 8.1 Required fields

- Selected date.
- Official park opening and closing time/status.
- Manual hours override.
- Private buyout/closed toggle, custom hours, and buyout details.
- Projected attendance.
- Pre-solds.
- Early Ride Time details and participating attractions.
- Important operational notes as concise lines/bullets.
- Daily Fun type and text entered manually:
  - Question of the Day.
  - Fact of the Day.
  - Quote of the Day.
- Dynamic ticket, Fast Lane, pass, promotion, bundle, add-on, and seasonal product list.

Do not depend on an AI service for Daily Fun. It is a normal editable field saved in the draft.

### 8.2 Hours business logic

Use the effective opening time, which is either verified official time or an enabled manager override:

- North Gate opening = park opening minus exactly 30 minutes.
- Avenue opening = park opening minus exactly 45 minutes.

Recalculate immediately when the manager changes the override. If the park is officially closed, show `Closed` unless the private-buyout override is enabled.

### 8.3 Draft behavior

- Save a separate associate draft per date.
- Auto-save after meaningful changes with a short debounce.
- Also provide a visible Save status, such as `Saved locally at 2:14 PM`.
- Never erase manual entries during an official-data refresh.
- When product data refreshes, reconcile official products with per-draft include/exclude and display overrides.

## 9. Manager Passdown requirements

### 9.1 Purpose

Build the manager passdown as an end-of-day/closing **Admissions communications log** unless the product owner later specifies a different scope. It should summarize the day for the next manager, not reproduce an email form or print every blank department heading.

The supplied reference was a four-page Outlook export. Use it only as a content reference. Do not reproduce Outlook headers, recipient lists, personal names, example employee incidents, or email formatting.

### 9.2 Navigation

Provide a prominent mode switch or home screen with:

- `Associate Passdown`
- `Manager Passdown`

Switching modes should be obvious and should not mix the two drafts. The selected operating date may be shared.

### 9.3 Manager form sections

#### A. Overview

- Operating date.
- Opening manager.
- Closing manager.
- Optional shift/period.
- Operational success score from 1–5, with plain-language definitions.
- Optional short headline or theme of the day.
- Park secured toggle and secured time.
- Submitted/prepared by and automatically recorded local timestamp.

#### B. Staffing snapshot

Inputs:

- Scheduled headcount.
- Present/here.
- Late.
- Call-outs.
- No-call/no-shows.
- Optional reassigned/sent home count.

Calculate percentages automatically and define the denominator clearly. Validate impossible values and do not double-count late employees. If `Present` includes late employees, explain that in the UI.

#### C. Location status matrix

Default Admissions locations:

- Access Control.
- Toll Plaza.
- Sales.
- Guest Services.
- Guest Experience.
- Waterpark.
- South Gate.
- Lost and Found.

Allow configured additions/removals later. Each location has:

- `Successful`
- `Watch`
- `Action Needed`
- `Closed`

Only `Watch` and `Action Needed` open a required concise note field. Successful and Closed locations should not consume narrative space in the PDF.

The status must remain distinguishable in black and white through text, icons, borders, or patterns—not color alone.

#### D. Top issues/trends

Allow up to three priority items for the one-page summary. Each contains:

- Issue/trend.
- Operational impact.
- Action taken.
- Owner/next step.
- Optional priority.

Use short fields, not one large unstructured textbox.

#### E. Department notes

Provide `Add Department Note` with a dropdown and short note. Suggested departments:

- Admissions / Ticket Taking / Guest Services.
- Aquatics.
- Campground.
- Entertainment.
- Food and Beverage.
- Lost and Found.
- Marketing.
- Merchandise / Games.
- Park Services.
- Rides.
- Safety / Security.
- IT / Maintenance and work orders.

Only departments with notes appear in the PDF. Do not print blank prompts. Permit a custom department name.

#### F. Actions and recognition

- Tasks/actions completed today.
- Carryover tasks for the next shift.
- Owner and optional due date/priority.
- Training opportunities.
- Shadowing/promotion/follow-up items.
- Supervisor or associate recognition / `Caught Doing Good`.

Keep sensitive personnel or disciplinary detail out of the general layout. If the app accepts a sensitive follow-up note, label it `Managers Only`, store it locally, and do not copy it into the associate passdown.

#### G. Closing checklist

Compact checkboxes/times for:

- Reports run.
- Shift cards printed.
- Park secured.
- Work orders submitted or reviewed.
- Required handoff completed.
- Optional additional configurable checklist item.

### 9.4 Dynamic form behavior

- Use progressive disclosure; reveal detail fields only when needed.
- Empty optional sections should collapse.
- Adding a department note inserts a compact card/row.
- Removing it removes it from the PDF.
- A location marked Watch/Action Needed requires a note.
- Show a live one-page capacity indicator.
- Warn before printing if required fields are missing or content is likely to overflow.
- Auto-save locally by date.

## 10. User experience and visual design

### 10.1 Overall design direction

Design the new app as a credible internal operations product from its first wireframes. Do not imitate or reskin the previous interface.

Use:

- Strong information hierarchy.
- Generous but efficient spacing.
- Consistent typography and alignment.
- Clear section titles.
- Professional buttons with plain-language labels.
- A restrained Carowinds-inspired identity without copying protected web assets unnecessarily.
- A small custom coaster/track-inspired application mark already present in project assets or an improved original vector mark.

Avoid:

- Excessive gradients.
- Random decorative cards.
- Emoji as primary icons.
- Tiny text.
- Large empty panels.
- Dense text dumps.
- Overuse of uppercase.
- Ambiguous labels such as `Sync`, `Hydrate`, or `Execute` for nontechnical users.

### 10.2 Application shell

Recommended structure:

- Left navigation rail or a clean top-level mode selector.
- Page title and selected date.
- Data-status summary with last verified time.
- Main form using well-grouped sections.
- Sticky action area with Save, Refresh, and Create PDF.

The date picker must look and behave like a professional Windows control:

- Clear selected date.
- Previous/next day or week controls.
- Calendar flyout.
- Visible cached/not-checked state without visual clutter.
- Keyboard accessibility.
- No automatic web retrieval on selection.

### 10.3 Nontechnical language

Examples:

- `Check this date online`, not `Run probe`.
- `Saved on this computer`, not `Persisted`.
- `Official information could not be checked`, with an optional `View details` link.
- `Last verified Aug 20 at 6:08 AM`.
- `This price is from Aug 13 and may be out of date`.

### 10.4 Accessibility

- Support keyboard navigation and visible focus.
- Use sufficient contrast.
- Do not communicate status by color alone.
- Minimum comfortable desktop text size around 13–14 px equivalent.
- Label all controls for screen readers.
- Use predictable tab order.
- Do not trap focus in calendar or dialog controls.

## 11. PDF requirements

### 11.1 Shared requirements

- US Letter, printable on ordinary office printers.
- Associate PDF: portrait.
- Manager PDF: preferably landscape for the status/action matrix; use portrait only if testing proves it more readable.
- Designed first for black-and-white output.
- High contrast, clear borders, and strong headings.
- Do not rely on colored backgrounds to convey meaning.
- Use an energetic original coaster-line motif, small track marks, angled section rules, or another restrained visual device.
- Avoid heavy solid-ink coverage.
- Include date and passdown type prominently.
- Include preparation/verification timestamp in a small footer.
- Never print technical error stacks or source URLs in the main passdown.

### 11.2 Associate PDF layout

Recommended portrait structure:

1. Header: Carowinds Passdown, date, small original mark.
2. Hours banner: park hours, North Gate, Avenue opening, special-event/buyout indicator.
3. Metrics row: projected attendance, pre-solds, ERT.
4. Dynamic products grouped cleanly by category.
5. Important operational notes.
6. Daily Question/Fact/Quote.
7. Footer: source freshness and a brief operational message.

Do not reserve empty space for hidden categories. Automatically rebalance product columns. Use readable truncation warnings in the app rather than silently cutting critical content.

### 11.3 Manager PDF layout

Recommended one-page landscape structure:

1. Header: date, leaders, score, secured status/time.
2. Staffing metric strip.
3. Compact location status matrix.
4. Priority issues/actions table.
5. Populated department notes only.
6. Completed/carryover actions.
7. Closing checklist and recognition.
8. Footer: prepared by/time and `Managers Only`.

The PDF should summarize. It should not reproduce every form label or display blank sections.

### 11.4 Overflow policy

- Define realistic character/item limits in the form.
- Provide live remaining-capacity guidance.
- Use bullets and concise rows.
- Do not reduce type below a readable threshold just to force one page.
- For unusually busy days, keep page one as the complete executive summary and create an optional continuation page clearly labeled `Supporting Details`.
- Typical-day fixtures must remain one page.

### 11.5 PDF verification

Automated tests must confirm page count and page size. Render test PDFs to images and visually inspect:

- Typical associate day.
- Heavy-product associate day.
- Closed day.
- Private buyout day.
- Off-season day with no seasonal section.
- SCarowinds day with maze products.
- Typical manager day.
- Heavy manager day close to capacity.

## 12. Local files, privacy, and reliability

- Store everything locally by default.
- Do not automatically email, upload, or sync passdowns.
- Do not collect payment, sign-in, or employee account credentials.
- Avoid storing example names or sensitive content in test fixtures committed to source control.
- Sanitize logs so they do not contain draft notes or personnel content.
- Use atomic writes for cache and draft files.
- Keep rolling backups or recover from a partial/corrupt JSON file.
- Version stored data and provide migrations when models change.
- Include an `Open data folder` action for troubleshooting.
- Include an `Export diagnostic report` action that excludes manager notes and personal information.

Snapshot/history is not a required feature. Saved per-date drafts and generated PDFs are sufficient unless it is trivial to retain cache history safely.

## 13. Error handling

Each source fails independently. The app must remain usable when:

- The internet is unavailable.
- One official URL changes.
- The checkout calendar has not published a future date.
- A page loads but never hydrates.
- Browser runtime is missing.
- A product category has zero published products.
- Cache JSON is corrupted.
- The PDF cannot be opened automatically.
- Windows Task Scheduler cannot create a task.

User-facing messages must say what is usable and what action is available. Example:

> Ticket prices could not be checked. Your saved draft and previously verified prices are still available. Try again later.

Keep detailed source, selector, response, and exception information in diagnostic logs.

## 14. Testing requirements

### 14.1 Unit tests

Add tests for:

- North Gate and Avenue time calculations, including times crossing midnight.
- Effective official/override/buyout hours.
- Cache freshness/due calculations.
- Per-source last-known-good merging.
- Product normalization and identity.
- Product add/update/archive/reactivation rules.
- `Error` not counting as absence.
- Exact-date mismatch rejection.
- Seasonal section visibility.
- Manager staffing percentages and validation.
- Dynamic location/department inclusion.
- PDF content limiting and overflow decisions.
- Storage migration and corrupted-file recovery.

### 14.2 Retrieval contract tests

Keep HTML/JSON fixtures representing official responses so parsers can be tested without live internet. Include:

- Open date.
- Closed date.
- Not-published future date.
- Exact dated-ticket products.
- Fast Lane Ultimate and reserve product examples.
- Marketing `Starting From` price.
- SCarowinds/maze products present.
- Seasonal products absent.
- Product renamed or removed.
- Partial/broken page.

Fixtures must contain no private company information.

### 14.3 Live smoke tests

When internet access is available, run opt-in live checks against official sources. Live tests should:

- Never add products to a cart.
- Never submit payment or sign-in forms.
- Record a sanitized result report.
- Verify requested date equals confirmed date.
- Report source failures separately.

Do not make build success depend on official websites being online.

### 14.4 UI and performance tests

Verify:

- Cached date selection under 250 ms target.
- No browser process starts on date selection.
- Refresh progress remains responsive.
- Cancel stops outstanding retrieval.
- Manual fields survive refresh.
- App starts without admin rights.
- Keyboard navigation works.
- Layout works at common Windows scaling levels such as 100%, 125%, and 150%.

## 15. Packaging and deployment

Produce a portable Windows 11 x64 release ZIP containing everything needed to run the app. The intended flow is:

1. Download the ZIP.
2. Extract the entire ZIP.
3. Open the extracted folder.
4. Double-click `Carowinds Passdown.exe`.

Requirements:

- No administrator rights.
- No separately installed .NET runtime if practical; publish self-contained.
- Bundle or reliably bootstrap the browser runtime without elevation. If bundling Chromium makes the ZIP too large, document the exact fallback behavior and support installed Edge.
- Keep the release folder clean. Do not include source code, build intermediates, caches, test output, `.git`, or unrelated workspace files.
- Use the professional application name `Carowinds Passdown` in the title, executable, task-scheduler entry, and PDF metadata.
- Include a concise `README-FIRST.txt` for nontechnical testers.
- Explain that Windows SmartScreen may show a warning for an unsigned internal executable. Do not claim the warning can be eliminated without code signing.

Do not require a custom server. Do not require the user's home computer to remain online.

## 16. Recommended architecture

Keep boundaries clear:

```text
Avalonia UI
  ├── Associate passdown view model
  ├── Manager passdown view model
  ├── Date/cache status view model
  └── Refresh progress and diagnostics

Application services
  ├── Passdown draft service
  ├── Refresh orchestration service
  ├── Product catalog reconciliation service
  ├── Scheduling service
  ├── Associate PDF service
  └── Manager PDF service

Domain
  ├── Hours and effective-hours rules
  ├── Products and product lifecycle
  ├── Retrieval result/status/evidence
  ├── Cache policy and freshness
  ├── Associate draft
  └── Manager draft

Infrastructure
  ├── Official source adapters
  ├── Direct HTTP/API transport
  ├── Playwright browser fallback
  ├── Local versioned storage
  ├── Windows scheduling
  └── Diagnostics/logging
```

Do not place retrieval, storage, and PDF logic directly in Avalonia code-behind. MVVM is preferred, but a pragmatic incremental refactor is acceptable if behavior is covered by tests.

Suggested source interface:

```csharp
public interface IOfficialDataSource
{
    string SourceId { get; }
    DataScope Scope { get; }
    TimeSpan Freshness { get; }
    Task<SourceResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken);
}
```

Suggested refresh planner responsibilities:

- Determine which sources are due.
- Determine requested dates.
- Prioritize today and tomorrow.
- Reuse one browser session.
- Limit concurrency.
- Save each completed source result.
- Leave last-known-good values intact after failure.
- Report granular progress and cancellation.

## 17. Implementation phases

Work in the following order. Keep the app runnable after each phase.

### Phase 1 — Discovery, evidence, and clean solution setup

- Treat all previous source code as read-only reference material.
- Document current live official-source behavior independently.
- Record baseline timings from isolated retrieval experiments where useful.
- Create the new solution and project boundaries under `carowinds-passdown-greenfield`.
- Establish dependency direction, configuration, logging, testing, and storage migrations before building the production UI.
- Do not begin with a copy of the previous application.

### Phase 2 — Domain and cache redesign

- Add configurable source freshness policies.
- Separate dated and catalog data.
- Preserve last success and last attempt.
- Add versioned storage/migrations.
- Add product lifecycle reconciliation.
- Add tests before connecting UI.

### Phase 3 — Retrieval performance and consistency

- Investigate official public API/network responses.
- Add direct retrieval where stable.
- Optimize Playwright fallback.
- Save source-by-source.
- Add exact-date assertions and retries.
- Measure improvement.

### Phase 4 — Associate workflow and visual system

- Build the professional, responsive Avalonia workflow using MVVM.
- Build a new date experience from the specified behavior.
- Preserve manual fields on refresh.
- Add dynamic product categories and seasonal visibility.
- Improve stale/verified explanations.
- Update associate PDF and overflow behavior.

### Phase 5 — Manager workflow

- Add manager draft model and local storage.
- Build dynamic manager form.
- Add validation/calculations.
- Generate one-page manager PDF.
- Verify with typical and heavy fixtures.

### Phase 6 — Scheduling and resilience

- Implement due-only 6:00 AM refresh.
- Add missed-run-on-launch behavior.
- Improve diagnostics and recovery.
- Test offline and failure scenarios.

### Phase 7 — Windows packaging and handoff

- Publish self-contained Windows x64.
- Create clean portable ZIP.
- Verify on a clean Windows 11 VM without admin rights.
- Include testing instructions and sanitized diagnostic export.

## 18. Acceptance criteria

The work is not complete until all of the following are demonstrated:

### Data and caching

- Cached date changes do not start web retrieval and feel immediate.
- Today plus six future dates can be refreshed and cached.
- Exact-date products are never attributed to the wrong date.
- Stable add-ons are not downloaded for every selected date.
- Add-ons follow the 14-day policy unless manually refreshed or change detection requires a check.
- Season passes follow the 7-day policy.
- Daily tickets, hours, Fast Lane, and short promotions follow daily policy.
- A source error does not delete good cached data or products.
- New official products appear automatically.
- Removed products disappear only after confirmed absence.
- SCarowinds content is hidden off season and appears automatically when published.

### Associate passdown

- Hours, overrides, buyouts, gate calculations, attendance, pre-solds, ERT, notes, Daily Fun, and products work.
- Manual data persists per date.
- Typical and heavy-product associate PDFs are readable in black and white.
- Typical associate PDF is exactly one Letter portrait page.

### Manager passdown

- Separate Manager Passdown entry point exists.
- Staffing calculations work and invalid totals are explained.
- Location detail appears only for Watch/Action Needed.
- Blank department sections do not print.
- Top issues, actions, checklist, and recognition are easy to scan.
- Typical manager PDF is exactly one Letter page and is readable in black and white.
- Manager-only content never leaks into associate drafts or diagnostic exports.

### Windows delivery

- App launches from an extracted ZIP on Windows 11.
- No admin rights, custom server, login, GPT, or separate .NET installation are required.
- Generated PDFs open or can be found easily.
- Offline startup loads drafts and cache.
- Release ZIP contains no `.git`, source, or unrelated output.

## 19. Required deliverables from the implementing agent

Provide:

1. Updated source code.
2. Automated tests and test fixtures.
3. A short architecture/data-flow document.
4. A source/cadence table showing what refreshes daily, weekly, and biweekly.
5. A diagnostic report from live official-source testing, with no private data.
6. Rendered sample associate and manager PDFs.
7. Page-count and page-size verification results.
8. Performance results before and after optimization.
9. A clean Windows 11 x64 release ZIP.
10. Simple tester instructions for a nontechnical coworker.
11. A concise list of remaining limitations caused by official-site behavior.

## 20. Working rules for the implementing agent

- Do not claim retrieval works without testing exact dates.
- Do not use made-up sample values as production fallbacks.
- Do not remove cached products after a network or parser error.
- Do not trigger a full online refresh when a date changes.
- Do not scrape every catalog source daily when its policy is weekly/biweekly.
- Do not add a cloud service merely for convenience.
- Do not put private employee examples into source control or screenshots.
- Do not sacrifice readability to force extreme content onto one page.
- This is an authorized greenfield rebuild. Do not patch or reskin the previous production app as a shortcut.
- Keep the application usable and testable throughout the work.
- When official pages are ambiguous, surface uncertainty and retain evidence instead of guessing.

## 21. Product-owner decisions to confirm before finalizing manager workflow

Proceed with reasonable defaults, but flag these two decisions for confirmation before final release:

1. Is the Manager Passdown strictly an end-of-day Admissions closeout, or should it support a park-wide manager audience?
2. Should the manager PDF default to Letter landscape, as recommended for the location/action matrix, or remain portrait for consistency with the associate passdown?

The architecture should make either answer a presentation/configuration change rather than a retrieval rewrite.
