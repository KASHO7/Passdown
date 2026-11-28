# Pre-RC.15 Post-Closing Pricing Retention Investigation and Correction

Status: implemented locally / awaiting Windows validation (local test candidate `RC15-PricingRetention-LOCALTEST`) / not owner-accepted / not published.

| | |
|---|---|
| Branch | `feature/passdown-redesign` (local; nothing pushed) |
| Starting commit | `700d881` (Phase 37D-6, documentation only) |
| Ending commits | `1be060d` (fix and regression tests), `42f6a7b` (specification, architecture, record, checklist; documentation only), then a documentation commit recording the Windows candidate |
| Reviewed source baseline | `1be060d` (tests, build and measurements ran on this production source) |
| Dirty-tree caveats | Clean at start; no unrelated changes present |
| Authorization | Owner brief "Carowinds Operations — Pre-RC.15 Pricing Reliability Fix: Preserve Verified Daily Pricing After Park Closing" (2026-11-27). Implementation gate (§13): local, bounded, no schema, Personal Sync or historical-pricing-format change, so implemented under that authorization |
| Release status | `v0.1.0-rc.14` (tag → `3d603f0`) published and frozen; source `<Version>` `0.1.0-rc.14` (not bumped); RC.15 not prepared; Phase 37E not started |
| Database schema / sync formats | Schema 18 (`Migration0018…`); sync envelope 1; draft payload 4; finalized snapshot 2; official price history file 1 — unchanged |

## Scope

Investigate and correct the Windows report of 2026-11-27. Not done (not authorized or not needed): pricing redesign,
new persistence, schema or sync change, new price-history contract, PDF template change, Weather/Calendar/PDF Weather
Outlook change, refresh-schedule change, RC.15, version bump, push, tag or publication.

## 1. User-reported symptom

On 2026-11-27, with the app left running and no Check Online, Daily Admission products disappeared or were reduced and
Fast Lane products became unavailable after the park closed; Season Passes stayed; the Passdown PDF preview followed.

## 2. Root cause

| | |
|---|---|
| Trigger | The official accesso calendar stops listing the current date once its sales window ends. The next automatic refresh (`OfficialDataRefreshCoordinator`, every 4 h, rolling window starting today; the same happens on Check Online or at the next app start) asks for today and gets a valid `OK` response without today in it |
| Classification (correct) | `DatedAdmissionSource.RetrieveAsync` / `DatedFastLaneSource.RetrieveAsync`: requested date ≤ latest listed date → `RetrievalStatus.Unavailable`, no payload. `Unavailable` is a successful completion (`RetrievalStatusExtensions.IsSuccessfulCompletion`) |
| Loss | `DatedSourceRegistration.ExecuteAsync` recorded every successful completion with `IDatedResultRepository.RecordSuccessAsync`; `SqliteDatedResultRepository`'s upsert set `success_payload_json = NULL` and replaced the verified success evidence |
| Lost or hidden? | **Lost**, not filtered. `dated_results.success_payload_json` is the only store of a date's dated prices: Season Price History never tracks dated products (`PriceHistoryTrackingPolicy`), and the draft holds only selections and overrides. `AssociatePassdownService.ComposeDatedProducts` then had nothing to compose |
| Why Season Passes stayed | Catalog sources (`CatalogSourceRegistration`) are not date-specific and were unaffected |
| Same bug, other trigger | Check Online on a past date: the adapters short-circuit to `Unavailable` ("That date has already passed.") without a request, and the same write erased that date's recorded prices. Finalization at 6:00 AM the next day would also have frozen the emptied products |

Not the cause (checked): time-based cache expiry (freshness only decides due-ness; it never deletes), selected-date
recalculation and product filtering (`HistoricalPricingPolicy` keeps exact-date rows), PDF preview (it renders the
composed workspace), a partial or error response (an `Error` already kept last-known-good), and automatic date
advancement (the Eastern date had not changed).

## 3. Official-source behavior (live, bounded)

One read-only request per keyword at 2026-11-27 11:12 PM ET, the standard anonymous storefront headers, window
`2026-11-27`…`2026-12-06`, no user data:

| Keyword | `SERVICE.status` | Dates listed |
|---|---|---|
| `SnapWizardDailyTickets` (Daily Admission) | `OK` | `2026-11-28`, `11-29`, `12-05`, `12-06` |
| `FastLaneOnly` (Fast Lane) | `OK` | `2026-11-28`, `11-29`, `12-05`, `12-06` |

So after closing the source returns a valid response that simply no longer offers today; later dates are unaffected.
It does not distinguish "sales window ended" from "date withdrawn". The exact moment the date drops out (closing time
or an earlier sales cut-off) was not established; no further live requests were made.

## 4. Reproduction evidence

Deterministic, no waiting for a real closing: `tests/Carowinds.Passdown.Infrastructure.Tests/Storage/PostClosingPricingRetentionSqliteTests.cs`
runs the real adapters, the real registration and real SQLite with a test clock moved from 3:10 PM to 10:15 PM ET, and
two new synthetic fixtures copied from the committed sanitized windows with today removed
(`fixtures/dated-admission/after-closing-window.json`, `fixtures/dated-fast-lane/after-closing-horizon-window.json`).
On the unfixed source (`700d881`) 5 of its 8 initial tests failed (Daily Admission, Fast Lane, repeated refreshes,
restart, past-date Check Online: the verified payload was NULL and the success status `Unavailable`); the 3 guard-rail
tests (failure keeps last-known-good, next date, never-verified date) passed. All pass after the fix.

## 5. Correction

| Piece | Change |
|---|---|
| `Application/Refresh/VerifiedDatedPriceRetention` (new) | Decides retention: category in the Phase 21A `StoredObservationsOnly` set (dated admission, Fast Lane, haunted/SCarowinds, dated Bring-A-Friend, BAF Duo, dated Military/First Responder), new answer `Unavailable`/`NotPublished` with no payload, stored success `Verified` with a payload |
| `DatedSourceRegistration` | For such an answer only, reads the stored row and, when retention applies, writes the latest attempt only |
| `IDatedResultRepository.RecordNoOfferAttemptRetainingVerifiedAsync` (new) / `SqliteDatedResultRepository` | Writes only the `last_attempt_*` columns (shared statement with `RecordFailedAttemptAsync`); rejects any status other than `Unavailable`/`NotPublished` |
| `Domain/Retrieval/LastKnownGood.RecordNoOfferAttemptRetainingVerified` (new) + `DatedResultRecord.ToLastKnownGood()` | The read projection replays a stored successful no-offer attempt the same way, so the verified value survives composition |
| `AssociateVerificationState.PreviouslyVerified` (new) | Derived in `AssociateVerificationInfo.From` when the success is `Verified` and the newer attempt is a successful no-offer answer; `LastVerifiedAt` is the original verification time |

Prices retained: same category, exact date and product payload, with the original evidence. Everything else is
unchanged: a new `Verified` payload replaces (price increase, decrease, product withdrawn while the date is on sale,
sales reopening); `Error` keeps the existing failed-attempt path (shown `Stale`); a never-verified date shows no price;
park hours, show schedules and catalog sources keep their previous semantics (park hours were deliberately excluded:
a later `Unavailable`/`Closed` there is a real schedule answer). Retention persists in SQLite, so it survives restarts.

### Pricing state before and after (today, verified 3:10 PM, refresh after closing)

| | Before | After |
|---|---|---|
| `dated_results` success | `Unavailable`, payload NULL, time = refresh | `Verified`, payload kept, time 3:10 PM |
| `dated_results` last attempt | `Unavailable` at refresh | `Unavailable` at refresh |
| Products & Pricing | rows gone; category banner "Not available for this date" | rows kept; note "Last verified Nov 27 at 3:10 PM ET — sales availability not confirmed." |
| PDF | products missing | products printed; existing footer "Some product pricing has not been recently verified." |
| Dashboard admission card | "Price not available" | price with "Last verified … — availability not confirmed" (stale styling) |

## 6. UI and PDF behavior

- **Products & Pricing:** existing per-row `VerificationNoteText` slot (`ProductRowViewModel`): "Last verified <date> at
  <time> ET — sales availability not confirmed." No new badge, control, layout or style; Light/Dark use the existing
  note style. The details popover still says "Official price last verified …". A category banner, shown only when a
  category has no rows, reads "No longer listed for this date" for this state (`CategoryStatusPresentation`).
- **Dashboard:** admission card freshness "Last verified … — availability not confirmed" with the existing stale flag;
  pricing card "Some prices are no longer listed — availability not confirmed" (the "could not be rechecked" wording is
  kept whenever any row is genuinely `Stale`); data-health source status maps the state to `Unavailable`, as before.
- **PDF:** no template or QuestPDF/PDFium change. Retained rows print through the one shared pipeline in Classic,
  Modern and Compact; `HasUnverifiedProductPricing` is true for any non-`Verified` included product, so the existing
  footer note marks the pricing as not recently verified; finalized snapshots carry the same flag. Manual overrides
  still print as the effective price.
- **Calendar:** not modified (brief §17). Its daily-ticket line shows a retained Park Ticket price as it already showed
  stale prices, without a freshness qualifier (limitation 3).

## 7. Manual overrides, Price History, Weather

Manual overrides stay draft-only and keep their effective-price role; the official cache is never written from the
draft (tested). Season Price History is unchanged: dated products are still never tracked, and the price-history
reader and snapshot cache are untouched. Weather, the NWS request cadence and the PDF Weather Outlook are untouched.

## 8. Tests

55 new deterministic tests (all pass; none weakened):

| Project | File | Tests |
|---|---|---|
| Domain | `Retrieval/PostClosingRetentionLastKnownGoodTests` | 7 |
| Application | `Associate/PostClosingPricingRetentionTests` | 22 — Daily Admission/Fast Lane retention, NotPublished, Season Passes unaffected, manual override and official/effective distinction, draft not mutated, next-morning historical rule, next operating date, price increase/decrease, product withdrawal, sales reopening, failed refresh (Stale), never verified, park hours out of scope, category set, decision matrix |
| Infrastructure | `Storage/PostClosingPricingRetentionSqliteTests` | 11 — the reproduction above, repeated automatic refreshes, restart, failure, next date, past-date Check Online, never verified, repository status guard |
| Documents | `Associate/PostClosingPricingRetentionDocumentTests` | 6 — model rows and effective prices, footer flag, Classic/Modern/Compact PDF text and one page, finalized snapshot |
| Ui (mixed) | `PostClosingPricingRetentionUiTests` | 9 — row note wording, override, other states unchanged, category banner, Dashboard admission and pricing wording |

The three existing in-test `IDatedResultRepository` fakes gained the new method. Print and Save PDF use the same
generated bytes as the PDF tests; actual Windows printing is in the checklist.

## 9. Performance

Measured on the Mac (Apple Silicon, .NET 10.0.11, Release) with the existing `tools/perf-benchmarks`, base `700d881`
(scratch worktree) vs fix `1be060d`; Mac numbers are not Windows evidence.

| Path | Base | Fix |
|---|---|---|
| `refresh-requests`: official POSTs for one 7-day rolling window (coalesced) | 9 (admission 1, Fast Lane 8) | 9 (admission 1, Fast Lane 8) |
| `db`: Associate date load (`LoadAssociateDateAsync`), median of 40, three runs interleaved | 0.38 / 0.43 / 0.39 ms | 0.43 / 0.44 / 0.35 ms |
| `db`: Calendar month load, median | 0.76 / 0.89 / 0.76 ms | 0.87 / 0.82 / 0.69 ms |
| `db`: Price History open (untouched code, noise reference) | 3.59 / 3.96 / 3.70 ms | 3.99 / 3.69 / 3.25 ms |

No measurable difference: run-to-run spread (shown by the untouched Price History path) exceeds any change. Request
volume is identical; the price-update path adds one indexed SQLite point read only when a dated pricing source returns
a payload-less no-offer answer, and no read otherwise. Preview coalescing, the price-history reader and snapshot cache,
accesso coalescing, direct PDFium rendering and the Lato font path are untouched.

## 10. Windows LOCALTEST package

| | RC15-PricingRetention-LOCALTEST |
|---|---|
| Source commit | `42f6a7b` (production source identical to `1be060d`) |
| Version shown | `0.1.0-rc.15-LOCALTEST.pricing-retention` (source `<Version>` still `0.1.0-rc.14`) |
| ZIP | `artifacts/local-test/RC15-PricingRetention-LOCALTEST/LOCAL-ONLY-RC15-PricingRetention-LOCALTEST-win-x64.zip` |
| Size / files | 76,543,573 bytes; 308 files |
| SHA-256 | `080ffc5765110f35a137bb08d276e2ec2ce9d19c7a412ab9287ec5835345d191` |
| Package validation | `Build-PortableRelease.ps1 -LocalTestLabel … -LocalTestVersion …` content validation passed for folder and ZIP; no database, WAL/SHM, draft, preference, weather cache, key, signature, manifest, PDB or PDF file |
| Real-ZIP test | `RealReleaseZip` passed against this ZIP (a bogus path fails) |
| Earlier artifacts | All 15,329 earlier files under `artifacts/` (39 under `artifacts/releases`) re-hashed byte-identical |
| Windows checklist | [rc15-pricing-retention-localtest-windows-checklist.md](releases/rc15-pricing-retention-localtest-windows-checklist.md) — deterministic reproduction: Check Online on today after closing; status **NOT EXECUTED** |

## 11. Deterministic Windows verification harness

Owner brief "Pre-RC.15 — Windows Pricing-Retention Verification" (2026-11-28): verify on Windows without waiting for
a closing. Chosen: a disposable console harness, `tools/pricing-retention-harness/` (not in `CarowindsPassdown.slnx`,
not shipped with the app; same pattern as `tools/updater-offline-stager`), rather than any production test mode.

- **Production code exercised:** `DatedAdmissionSource`, `DatedFastLaneSource`, `DatedSourceRegistration`,
  `SqliteDatedResultRepository`, `SqliteConnectionFactory` (and, only to create an empty build-host database for
  `--database`, the production migrations through `MigrationRunner`). No production source changed.
- **Synthetic inputs:** a stub `IRetrievalHttpClient` serves only embedded committed fixtures
  (`fixtures/dated-admission/published-window.json`, `fixtures/dated-admission/after-closing-window.json`,
  `fixtures/dated-fast-lane/published-today.json`, `fixtures/dated-fast-lane/horizon-window.json`,
  `fixtures/dated-fast-lane/no-dates-found.json`, `fixtures/dated-fast-lane/after-closing-horizon-window.json`); there
  is no HTTP client, so no request is possible. Its own `IClock` is set to 2026-08-20 3:10 PM ET, then 10:15 PM ET;
  the system clock is not changed.
- **Date:** Thursday 2026-08-20, a past date the app never retrieves online (automatic checks skip past dates; the
  rolling window starts today; Check Online short-circuits past dated-pricing dates without a request), so nothing
  live can overwrite the synthetic data during the check.
- **Isolation guards:** writes only to the signed-in user's database when that user is an approved disposable test
  account — exactly `CarowindsTest` or `CarowindsTestB`, case-insensitive (`tools/pricing-retention-harness/TestAccountPolicy.cs`;
  `CarowindsTestB` added 2026-11-28 at the owner's request, after the first package refused that account); refuses while
  the app runs, when the database is missing or not at the current schema, or when the date already has stored
  dated-admission/Fast Lane results. `verify` is read-only. No database is packaged (the build script refuses one).
- **Build-host evidence (Mac):** `seed` on a fresh schema-18 database → 8/8 PASS, 5 fixture answers, 0 network
  requests; a second `seed` refused; `verify` → 8/8 PASS; the default-path run as a non-approved user refused
  without creating a file. **Negative control:** the same harness built against the unfixed source `700d881` reports
  `RESULT: FAIL` (6 checks: success `Unavailable` at 10:15 PM, "prices none"). A throwaway scratch program (outside
  the repository) composed the production `AssociatePassdownService` workspace from the seeded database: three rows,
  `PreviouslyVerified`, row note "Last verified Aug 20 at 3:10 PM ET — sales availability not confirmed."; after a
  $45 override the PDF (Classic, Modern, Compact) printed Park Ticket $45.00, FAST LANE RESERVE $49.00, FAST LANE
  ULTIMATE $85.00 and the footer "Some product pricing has not been recently verified.".
- **Windows steps:** checklist section "Simulated closing" (S.1–S.10); not executed.

| | |
|---|---|
| Harness package (superseded) | `artifacts/local-test/RC15-PricingRetentionHarness-LOCALTEST/LOCAL-ONLY-RC15-PricingRetentionHarness-LOCALTEST-win-x64.zip` — built by `tools/pricing-retention-harness/build-harness.sh` from clean commit `780da37`; 41,731,586 bytes; 203 files (self-contained win-x64, no PDB, no database); refuses `CarowindsTestB`; SHA-256 `04547d8d82e7e6008fb3c778aecc64f51c5026c3521547f52c1a7b7a6c71d924`; six fixtures embedded; version stamp `0.1.0-rc.15-LOCALTEST.pricing-retention-harness+780da37…`. All 15,895 earlier artifact files re-hashed byte-identical (including the app ZIP `080ffc57…d191`). The app package is unchanged: production source has not changed since `1be060d`. |
| Harness package 2 (use this) | __H2ROW__ |

## Validation

| Check | Result |
|---|---|
| Deterministic suite (Release, `Category!=LiveSmoke`, six run-alone real-window classes separately, memory watchdog) | **6,518 passed / 0 failed / 0 skipped** (Domain 495, Application 1,521, Infrastructure 1,447, Documents 640, Ui mixed 2,142; OperationsCalendarViewTests 35, Phase15ERealWindowLayoutTests 162, RealWindowStartupTimingTests 2, SpellCheckNavigationLifecycleTests 7, Phase21DMathEditorWindowTests 4, Phase33PreviewWindowTests 63) — baseline 6,463 + 55 |
| Repository guards | 31/31 passed |
| Clean Release build (`dotnet clean` then `dotnet build CarowindsPassdown.slnx -c Release`) | 0 warnings, 0 errors |
| Live tests | Not run (opt-in); two bounded read-only official requests made manually for §3 |
| Windows | **Not run.** Checklist: [rc15-pricing-retention-localtest-windows-checklist.md](releases/rc15-pricing-retention-localtest-windows-checklist.md). Mac testing is not Windows validation |

## Documentation maintenance

- Product specification: **Updated** — `AGENT_BUILD_SPEC.md` §7.5 "Previously verified date-specific prices" and §22.4
  acceptance line, citing the owner brief of 2026-11-27.
- Architecture documentation: **Updated** — `docs/architecture.md` (overview paragraph),
  `architecture/retrieval-and-refresh.md`, `architecture/domain-and-application.md`, `architecture/desktop-ui.md`,
  `retrieval/pricing-semantics.md`, `retrieval/dated-admission-source.md`, `retrieval/dated-fast-lane-source.md`.
  Dependencies, DI registrations, projects, schema, sync, PDF templates and threading: reviewed — no change.

## Compatibility

Schema 18, Personal Sync envelope 1, draft payload 4, finalized snapshot 2 and the official price history file are
unchanged; no migration. Existing databases need nothing: rows already emptied before this fix cannot be restored
(nothing else recorded those prices), and rows written from now on are retained. `AssociateVerificationState` is not
persisted or synced (finalized snapshots store text and the footer flag only). The new interface member is internal to
the solution. Official request volume is unchanged.

## Known limitations

1. Prices already erased by the old behavior (including the 2026-11-27 occurrence) are not recoverable.
2. The source does not say why a date stopped being listed; a date withdrawn before closing (weather, sell-out) is
   also shown as previously verified with "sales availability not confirmed" — truthful, but not more specific.
3. The Calendar daily-ticket line shows a retained price without a freshness qualifier (Calendar code not modified
   under this brief); the selected date's Products & Pricing and the PDF footer carry the distinction.
4. A failed check after retention shows the existing `Stale` wording ("could not be rechecked") until the next
   successful check.
5. The exact time Accesso removes the date was not measured.
6. Windows behavior, printing and Narrator are unverified until the owner runs the checklist.
