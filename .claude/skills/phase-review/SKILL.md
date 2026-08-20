---
name: phase-review
description: Close out a Phase 1–7 implementation task from AGENT_BUILD_SPEC.md §17. Use before reporting a phase (or a task within one) complete — checks scope, tests, build, diff, and relevant acceptance criteria, then stops.
---

# Phase review

Follow CLAUDE.md workflow steps 8–10 (review the diff, report results, stop)
and "Work on only the requested phase/task."

## 1. Confirm scope

Re-read only the spec subsection for the phase actually being worked
(AGENT_BUILD_SPEC.md §17, Phase 1–7) plus any §18 acceptance-criteria bullets
it maps to. Do not re-read the whole spec.

| Phase | Spec section | Related §18 acceptance group |
|---|---|---|
| 1 — Discovery & solution setup | §17 Phase 1 | — (foundation) |
| 2 — Domain & cache redesign | §17 Phase 2 | Data and caching |
| 3 — Retrieval performance | §17 Phase 3 | Data and caching |
| 4 — Associate workflow | §17 Phase 4 | Associate passdown |
| 5 — Manager workflow | §17 Phase 5 | Manager passdown |
| 6 — Scheduling & resilience | §17 Phase 6 | Data and caching (scheduling parts) |
| 7 — Windows packaging | §17 Phase 7 | Windows delivery |

## 2. Run the build-and-test skill

Use the `build-and-test` skill for the relevant test tier(s). Before
declaring a *phase* (not just a task) complete, run the full suite excluding
live smoke tests.

## 3. Check acceptance criteria for this phase only

Walk the matching §18 bullets. For each: does the current implementation
satisfy it, or is it out of scope for this phase? Do not mark a later
phase's criteria as blocking this one.

## 4. Review the diff

- Confirm dependency direction held: Domain has no Avalonia/SQLite/HTTP/
  Playwright/QuestPDF/Windows references; Application depends only on
  Domain; Infrastructure/Documents/Desktop stay separated per CLAUDE.md
  "Architecture".
- Confirm no persistence, retrieval, business rules, or PDF generation
  landed in Avalonia code-behind.
- Confirm old-app code was not copied, patched, or reskinned (CLAUDE.md
  "Greenfield rule"; spec §4 "Do not copy" list).
- Confirm no fabricated/placeholder data was introduced as a production
  fallback (§20 working rules).
- Confirm `git status` was checked before the change and no unrelated user
  work was touched (CLAUDE.md "Git").

## 5. Report and stop

Summarize: what was implemented, which tests were run and their result,
which §18 criteria are now met, and any spec ambiguity that needs a
product-owner decision (§21, or CLAUDE.md "Ask only about genuine
product/UX/business decisions"). Do not automatically start the next phase
(CLAUDE.md "Workflow").
