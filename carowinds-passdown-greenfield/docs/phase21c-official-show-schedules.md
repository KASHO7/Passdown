# Phase 21C — Official show schedules and passdown integration

Development phase after RC.5 (`0.1.0-rc.5`), following Phases 21A and 21B. Adds date-specific
official Carowinds entertainment schedules to the Dashboard, Operations Calendar, Associate Passdown
and all three PDF templates. **Show information is never part of any Teams Copy output.**

Schema versions are unchanged: **DB 16, Draft 4, Snapshot 2, SyncSchemaVersion 1** — no migration.
The next packaged candidate is still `0.1.0-rc.6`; `Directory.Build.props` stays at `0.1.0-rc.5`.

## 1. Official source inventory

Investigated live from the dev host on 2026-10-16 (Eastern). Only official Six Flags/Carowinds
resources were used.

| Resource | What it carries | Dated performances? | Used |
|---|---|---|---|
| `https://www.sixflags.com/carowinds/shows` (Next.js page) | Show cards from the site's search index: name, description, image, official `fimsId` (`SHOW-030-00122`), `durationInMinutes`, category. Page text: "Check the app for showtimes!" | No | No (marketing only) |
| `https://www.sixflags.com/carowinds/shows/{slug}` (e.g. `wake-dead-show`) | CMS `showTimesSchedule`: a date range (`2026-09-11`–`2026-11-01`) with no times for Carowinds shows; venue name | No (range only) | No |
| **`https://d3ln8b3jj5b2pq.cloudfront.net/operating-hours/park/30?date=yyyyMMdd`** (also served byte-identically by `https://fims.prod.six-mt.com/…`) | The official FIMS operating-hours feed the park-hours page itself loads (see `docs/retrieval/park-hours-source.md`). Each `dates[]` entry has a **`shows[]`** array: `showId`, `extLocationID` (= the show page's `fimsId`), `showName`, `items[].assignmentLocation` (venue) and `items[].times` (`"07:00 PM, 08:00 PM"`). | **Yes** | **Yes** |

The feed is the same public, unauthenticated JSON this app already reads for park hours — no login,
CAPTCHA, cookies or browser automation.

## 2. Date-specific evidence

- `?date=20261017` (Saturday, SCarowinds) returned 11 shows, each with its own times — e.g. Wake the
  Dead `07:00 PM, 08:00 PM, 09:30 PM, 10:30 PM` at Carowinds Theatre; Camp Snoopy Meet and Greet with
  16 times.
- `?date=20261016` (Friday, park 6 PM–midnight): the five daytime shows are **listed with blank
  times**; only the six evening shows carry times. A listed show is therefore not proof it performs.
- `?date=20261205` (Winterfest): the Halloween shows are still listed, **all with blank times**.
- `?date=20271015` (far future): no park hours, shows listed without times.
- `?date=20260704`: a different, seasonal lineup (Fireworks 4th of July 9:30 PM, Splash Parade, …).
- Closed dates (`isParkClosed: true`) carry no shows.
- Daily requests for past dates in the current year still answer (e.g. 2026-08-15); the monthly form
  (`yyyyMM`) returns only today-forward dates, as already documented for park hours.

## 3. Source limitations

- No end time or duration per performance (the marketing card's `durationInMinutes` is not
  date-specific and is not used); the app never guesses whether a started show is still running.
- No cancellation, "changed", or last-updated field. The app never claims a cancellation; it only
  reports what the latest successful official answer lists.
- No explicit time zone: times are America/New_York wall-clock, the same assumption as park hours.
- Blank `assignmentLocation` happens (e.g. Costume Contest); venue is then omitted, never invented.
- Past-date answers exist, but whether they reflect what actually ran was not established — they are
  not used (see §8).

## 4. Retrieval architecture

- `ShowScheduleSource` (Infrastructure, `SourceCategory.ShowSchedule`, source id
  `show-schedule.official`) — one daily-form GET per date, `RetrievalStrategy.DirectApi`.
- Registered with the existing refresh orchestrator through `CurrentAndFutureDatedSourceRegistration`,
  so it participates in **startup refresh, the four-hour comprehensive check, Check Online, Deep
  Refresh, Associate selected-date checks, and the scheduled (headless) refresh** with no second
  polling engine, using the same gate, single-flight, freshness (daily 6 AM cadence) and cancellation.
- Priority: the rolling window is dispatched by date rank (today first, then the next six days), and
  within a date the show source is registered last, after park hours and pricing. Check Online covers
  only the selected date. Deep Refresh covers the normal 7-day window for shows (not the park-hours
  coverage horizon). No multi-month crawl.
- `CoalescingRetrievalHttpClient` wraps the HTTP client given to both `ParkHoursSource` and
  `ShowScheduleSource`: identical GETs in flight are joined and a successful response is reused for
  2 minutes, so a date's hours and shows normally cost **one** request. Failures are never cached.
- HTTP timeout and cancellation are the existing `RetrievalHttpClient` behavior; no retries are added.
- Changing a selected date never triggers retrieval by itself (unchanged Phase 19A rules: the
  Associate quiet date check only runs for current/future stale dates; the Calendar never goes online).

## 5. Show and performance identity

- Show identity (`ShowIdentity.Resolve`): official `extLocationID` (uppercased) → numeric `showId`
  (`fims-show:2261`) → normalized `name:{name}|{venue}` (case/whitespace-insensitive). Display order
  is never identity; the same name at two venues stays two shows.
- Duplicate entries for one identity in a response are merged (times unioned, venues combined).
- A performance is a (show, operating date, start time) triple, stored as the show's distinct start
  times in operating-day order. Times before 4:00 AM belong to the end of the operating day (a 12:15 AM
  SCarowinds performance sorts after 11:30 PM and starts on the next calendar date).
- Times are formatted in U.S. style without seconds ("1:00 PM"). The compact form writes AM/PM once
  per run ("1:00, 3:00 PM").

## 6. Schedule statuses

| Official answer | Stored status | Shown as |
|---|---|---|
| ≥1 show with times | Verified + shows | the shows |
| Open day with published hours and an empty `shows` array | Verified, no shows | "No shows scheduled …" |
| Shows listed, none with times; or no hours and no times | NotPublished | "… show schedule is not published." |
| `isParkClosed` | Closed | "No shows — the park is closed …" |
| HTTP/JSON/shape error, date mismatch, any unparseable time | Error (never replaces stored data) | "Couldn't check …", or the kept schedule marked "latest check failed" |
| Past date | not requested | stored record, else "Show schedule not available for this past date." |

Only a confirmed empty schedule ever says "No shows scheduled".

## 7. Freshness policy

`SourceCategory.ShowSchedule` uses the same daily (6:00 AM Eastern) freshness policy as every other
dated category, refreshed by the four-hour comprehensive check. A failed attempt updates only the
latest-attempt evidence: the last verified schedule stays, shown with "Official schedule checked
{time} · latest check failed" — never as newly verified. A successful answer that no longer lists a
performance replaces the schedule (that is the source's own current answer); incomplete/invalid
responses are errors and never count as removals.

## 8. Historical-date policy

- Past dates are **never requested**: `CurrentAndFutureDatedSourceRegistration` skips them before any
  network request or write (a Check Online on a past date therefore cannot overwrite that date's
  recorded schedule), and `ShowScheduleSource` itself refuses past dates.
- A past date shows only the schedule recorded for that exact date while it was current/future.
- Today's lineup is never substituted for a historical date.
- Shows were **not** added to the Phase 20C historical park-hours backfill.

## 9. Storage schema

No migration. Each date's schedule is one `dated_results` row (category `ShowSchedule`), the same
last-known-good table every dated source uses, holding the compact `ShowSchedulePayload` JSON
(show id, name, venue, start times; plus a count of shows listed without times). No raw HTTP bodies,
HTML, descriptions, images or screenshots are stored.

## 10. Dashboard presentation

"Today's Shows" panel after "Today at a Glance": one compact row per show with upcoming performances
(name · remaining times · venue), ordered by next performance, "+N earlier" when some have started,
and a single "Started earlier today: …" line for shows whose performances have all started. The panel
re-evaluates upcoming/started once a minute from the snapshot already shown (no data read, no network)
and reloads with every local data change (e.g. a finished refresh), via the existing Phase 19A
invalidation. Empty states as in §6.

## 11. Calendar presentation

The selected-date detail panel gains "Shows — {Month d}" with each show's name, times and venue,
built from the same snapshot as the rest of the panel, so changing dates can never carry another
date's shows over. Day cells are unchanged (no show lists in cells). Opening the date in Associate
preserves the date (existing behavior); nothing is selected automatically.

## 12. Associate selection behavior

- Overview section, "Shows" panel: a section checkbox ("Include the Shows section in the Passdown",
  off by default), Select all / Clear (quiet secondary buttons), the freshness line, and one checkbox
  per show (name — times — venue). All of a selected show's verified times print.
- Selections are stored in the date's draft as `AssociateDraftPayload.Shows`
  (`AssociateShowsSelection`: section switch + selected shows with a copy of name/venue/times as last
  verified). They autosave, survive date switching/restart/reload, and belong to that date only.
- Reconciliation with the current schedule (`AssociateShowsSection.Compose`):

| Situation | Row | Prints |
|---|---|---|
| Selected, on the schedule, times unchanged | normal | yes |
| Selected, on the schedule, times changed | "Official times changed since this show was selected." | yes, current verified times |
| Selected, a later successful check no longer lists it | "No longer on the official schedule for this date — won't print." | no |
| Selected, schedule now closed / not published | "Not on the current official schedule — won't print." | no |
| Selected, no official schedule on this computer (offline, never checked, sync recipient) | "Official schedule unavailable — using the times saved when selected." | yes, saved times |
| Newly published show | listed, **unselected** | no |

- Show source failures never block saving, editing other sections, PDFs, Copy, navigation or
  offline use.

## 13. PDF inclusion semantics

Classic: "SHOWS" heading + one aligned row per show (name and muted venue left, times right).
Compact: one extra summary-strip line — `SHOWS  Show A (1:00, 3:00 PM)  •  Show B (5:00 PM)`.
Modern: standard accent heading, name with venue beneath, times right-aligned; no numbering.
All three print the same selected shows and the same verified times from one document model
(`AssociatePassdownDocumentModel.Shows`). With the section off, nothing selected, or nothing printable,
there is no heading and no reserved space (byte-for-byte the same text as before). In tight density
Classic/Modern write the compact time list. PDF generation never makes a network request.

## 14. Finalized History behavior

The finalized snapshot gains an optional `Shows` list (`FinalizedShowSnapshot`: name, venue, times
text — exactly as printed). The frozen PDF bytes already contain the section. Later schedule changes,
removals or venue changes cannot alter either. History shows a "Shows" panel only when the snapshot
has one. The property is omitted when no Shows section printed, so pre-21C rows and passdowns without
shows serialize and hash exactly as before; Snapshot schema stays 2 and older snapshots load unchanged.

## 15. Personal Sync compatibility

- The draft's new `Shows` property is additive and omitted when unused (the Phase 18D precedent), so
  drafts that never use Shows keep byte-identical JSON and content hashes — upgrading causes no
  re-exports or spurious conflicts. SyncSchemaVersion stays 1; Draft schema stays 4.
- A recipient device without that date's show schedule prints the saved copy of each selected show
  (§12) rather than nothing or a guess; no show-source cache is ever synced, and the live SQLite
  database is still never in the sync folder.
- Draft conflict details now include a "Shows" row (section on/off and chosen show names).
- **Mixed versions:** an RC.5/21A/21B build ignores the `Shows` property and drops it if it re-saves
  that date's draft (then syncs that version). **Upgrade every PC that shares a sync folder before
  using Shows.**
- Snapshots: an older build reads a 21C snapshot (it ignores `Shows`); the frozen PDF still contains it.

## 16. Teams Copy exclusion

No Shows option in the Copy menu; `AssociateTeamsTextFormatter` never reads
`AssociatePassdownDocumentModel.Shows`. Tests compare every copy kind (Operations Summary, Today's
Pricing, Operations + Pricing) with and without selected shows and require identical output with no
show names, times, venues or headings.

## 17. Test coverage

| Area | Tests |
|---|---|
| Domain | `ShowTimesTests`, `ShowIdentityTests` (ordering, late night, DST date, formatting, identity precedence/normalization) |
| Source fixtures (`fixtures/show-schedules/`) | `ShowScheduleSourceTests`: valid schedule, multiple times/shows, listed-without-times, not published, far future, explicitly empty, closed, duplicate ids, missing venue/ids, late night, unparseable time, date mismatch, malformed JSON, HTTP 404, timeout, offline, caller cancellation, historical date never requested, seasonal schedule, changed/removed performances |
| Request sharing | `CoalescingRetrievalHttpClientTests` |
| SQLite end to end + storage | `Phase21CShowScheduleSqliteTests` (persist/read, last-known-good on failure and on invalid response, removal by successful refresh, past date never requested/overwritten, historical not available, storage measurement) |
| Refresh integration | `Phase21CShowRefreshTests` (Check Online, error isolation from park hours, LKG, past-date skip, bounded today-first window, Deep Refresh, date-check staleness, freshness policy) |
| Application | `Phase21CShowsTests` (status composition, upcoming, reconciliation table above, selection building, draft/snapshot compatibility and hashes, date-specific persistence across restart), `Phase21CDashboardCalendarShowsTests` |
| PDF/History/Teams | `Phase21CShowsPdfTests` (all templates, selected only, no empty section in four scenarios, consistency, changed time, stale, source unavailable, long names and 16-time show, venue present/missing, normal/busy page counts, Teams Copy exclusion, frozen snapshot/PDF) |
| UI | `Phase21CShowsUiTests` (empty-state wording, upcoming/past ordering, freshness, Dashboard live update, Associate selection/autosave/date isolation/select all/removed shows/offline), `Phase21CShowsMarkupTests` |
| Updated guards | `CompositionRootTests` (13 registrations incl. ShowSchedule), `Phase21APricingIntegrityTests` (ShowSchedule is not a pricing category) |
| Live (opt-in) | `ShowScheduleSourceLiveSmokeTests` (`Category=LiveSmoke`) |

## 18. Storage measurement

Measured by `Phase21CShowScheduleSqliteTests.StorageMeasurement_…` on real SQLite, using the busiest
observed day (2026-10-17: 11 shows, 46 performances; payload 1,601 bytes):

| | |
|---|---|
| DB before | 155,648 bytes (empty schema) |
| DB after 365 such days | 925,696 bytes |
| Growth | **770,048 bytes per year** (≈2.1 KB/day including evidence columns) |
| Per performance | ≈46 bytes |
| Raw responses stored | none (asserted: no `venues`/`operatings` in stored payloads) |

In practice only open dates within the 7-day window and checked dates are stored, so real growth is
lower.

## 19. Windows validation checklist

**NOT EXECUTED** — no Windows host was available. To run on Windows 11 x64 with a backed-up profile:

Source and refresh
- [ ] Today's official schedule appears after startup refresh (compare with the Six Flags app).
- [ ] Different selected dates (tomorrow, next Saturday) show their own shows after Check Online.
- [ ] Check Online on a past date leaves that date's recorded shows unchanged.
- [ ] Deep Refresh and the four-hour automatic refresh update shows; Settings → Diagnostics log shows no repeated failures.
- [ ] Offline (network off): app opens, shows "latest check failed"/"Couldn't check", everything else works.
- [ ] A not-yet-published date says "not published", never "No shows scheduled".

Dashboard
- [ ] Today's Shows rows are compact; upcoming ordering correct; finished shows move to "Started earlier today" within a minute.
- [ ] Freshness line shows the check time; empty states read correctly.

Operations Calendar
- [ ] Selected-date shows update on every date change; no carry-over; past dates without records say "not available".

Associate
- [ ] Shows panel: section checkbox, per-show checkboxes, Select all / Clear; keyboard (Tab/Space) works; screen reader reads names.
- [ ] Selections autosave, survive date switching and app restart, and do not appear on other dates.
- [ ] After a schedule change, removed/changed shows are explained; new shows unselected.
- [ ] Compact layout at 960×640 — no horizontal scroll.

PDF
- [ ] Classic, Compact, Modern show the same selected shows and times; no Shows heading when off/none selected.
- [ ] Long show names and the Meet and Greet (16 times) wrap cleanly.
- [ ] Normal day + shows stays one page in all templates; busy day: Compact one page.

History and Sync
- [ ] Finalize a passdown with shows; reopen it in History after the schedule changes — frozen shows/PDF unchanged.
- [ ] Old snapshots and old drafts load.
- [ ] Two PCs on 21C: selections sync; a PC without the schedule prints the saved copy.

Teams Copy
- [ ] All three Copy options contain no show names, times, venues or headings, even with shows selected.

Visual
- [ ] Light/dark themes; 100%, 125%, 150% scaling; narrow windows; popups/overlays unaffected.

## 20. Known limitations

- The feed has no cancellation/duration/updated flags (§3); a "changed" state is only inferred from
  the supervisor's own saved selection.
- Modern on the representative busy-day fixture has no spare room: adding any Shows section moves it
  to a second page (Classic also needs two pages for the heaviest four-show case). Compact — the
  density benchmark — stays one page; nothing is dropped.
- Past-date show schedules are not retrieved; dates never checked while current have no history.
- Mixed-version Personal Sync can drop a Shows selection (§15) — upgrade all PCs together.
- **Rollback:** RC.5 reads `dated_results` categories with `Enum.Parse`; a profile used by this build
  contains `ShowSchedule` rows, the same exposure Phase 21A introduced with
  `DatedMilitaryFirstResponder`. Do not run RC.5 against a profile used by RC.6 — back up
  `%LOCALAPPDATA%\Carowinds Passdown` before upgrading.
- Windows validation not executed.
