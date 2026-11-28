# Documentation index

Status labels: **Current** (describes the product or implementation now) · **Living record**
(appended over time; the latest sections are current, earlier sections are historical) ·
**Historical** (evidence of what was decided or done at the time; may contain superseded details) ·
**Superseded** · **Deferred** · **Retired**. Historical records are kept; none are deleted.

When documents disagree, `../AGENT_BUILD_SPEC.md` governs product requirements and
[architecture.md](architecture.md) governs the current implementation description, both subject to
verification against source and tests.

## Current

| Document | Scope |
|---|---|
| [../AGENT_BUILD_SPEC.md](../AGENT_BUILD_SPEC.md) | Authoritative product specification and acceptance criteria |
| [../CLAUDE.md](../CLAUDE.md), [../.claude/rules/](../.claude/rules/) | Development instructions and engineering rules |
| [architecture.md](architecture.md) | Current architecture overview, review baseline, data contracts |
| [architecture/application-lifecycle.md](architecture/application-lifecycle.md) | Composition, startup, shutdown |
| [architecture/domain-and-application.md](architecture/domain-and-application.md) | Use cases, workspace state, rules |
| [architecture/data-and-persistence.md](architecture/data-and-persistence.md) | SQLite, drafts, finalized history, local preferences |
| [architecture/retrieval-and-refresh.md](architecture/retrieval-and-refresh.md) | Official sources, freshness, coalescing |
| [architecture/personal-sync.md](architecture/personal-sync.md) | File exchange, conflicts, compatibility |
| [architecture/desktop-ui.md](architecture/desktop-ui.md) | Shell, workspaces, design system |
| [architecture/pdf-and-printing.md](architecture/pdf-and-printing.md) | Shared generation, preview, printing |
| [architecture/updates-and-release.md](architecture/updates-and-release.md) | Signing, packaging, installation |
| [architecture/testing-and-performance.md](architecture/testing-and-performance.md) | Test partitions, benchmarks, open concerns |
| [retrieval/pricing-semantics.md](retrieval/pricing-semantics.md) | Price vocabulary (authoritative vocabulary; verify against source) |
| [support-troubleshooting.md](support-troubleshooting.md) | Support guide (repository-only, not shipped) |
| [templates/phase-record-template.md](templates/phase-record-template.md) | Required structure for new phase records (maintenance status lines are test-enforced) |

## Living records

| Document | Notes |
|---|---|
| [release-strategy.md](release-strategy.md) | Release shape, RC numbering, packaging rules; read the latest sections |
| [release-manifest.md](release-manifest.md) | Per-build manifest and deterministic test partition history. Its title and opening table describe the historical Phase 15F `0.1.0-dev` build and are kept as evidence; current release identity is in the latest [release record](releases/rc14-release-readiness.md) |
| [personal-sync-architecture.md](personal-sync-architecture.md) | Phase 16A design with later implementation sections appended; the current summary is [architecture/personal-sync.md](architecture/personal-sync.md) |
| [releases/](releases/) | Release readiness, notes, installation guides and Windows checklists per RC; latest published: [rc15-release-preparation.md](releases/rc15-release-preparation.md) (RC.15, §21); previous: [rc14-release-readiness.md](releases/rc14-release-readiness.md) |

## Source investigation records (historical evidence of official-source behavior)

[phase1-discovery.md](phase1-discovery.md) ·
[retrieval/park-hours-source.md](retrieval/park-hours-source.md) ·
[retrieval/dated-admission-source.md](retrieval/dated-admission-source.md) ·
[retrieval/dated-fast-lane-source.md](retrieval/dated-fast-lane-source.md) ·
[retrieval/dated-haunted-source.md](retrieval/dated-haunted-source.md) ·
[retrieval/special-ticket-sources.md](retrieval/special-ticket-sources.md) ·
[retrieval/catalog-source-inventory.md](retrieval/catalog-source-inventory.md) ·
[retrieval/product-classification-and-lifecycle.md](retrieval/product-classification-and-lifecycle.md)

Live observations are dated; re-verify against the official site before relying on them. Playwright
is mentioned historically; it is not a production dependency.

## Phase records (historical)

Phase records (`phase*.md`) and Windows checklists record what each phase changed and validated.
They are evidence, not current requirements, and later phases supersede earlier details.
Notable groups:

- Foundation and early phases: `phase1-discovery.md`, `phase-7e-windows-validation-checklist.md`, `phase9`–`phase13`.
- Audit and release candidates: `phase18*`, `phase19*`–`phase22*`, `phase23*`.
- Product areas: [associate-passdown-templates.md](associate-passdown-templates.md) (Phase 17C, superseded in part by 17D–17F),
  [ert-configuration.md](ert-configuration.md) (Phase 17B), [scheduled-refresh-task-scheduler.md](scheduled-refresh-task-scheduler.md) (Phase 6C),
  `phase25`, `phase26`, `phase31*`, `phase32`, `phase33`.
- Performance and cleanup: `phase34a*`, `phase34b1*`, `phase34b2*`, `phase34c*`.
- UI modernization: `phase35*` through [phase35g-final-uiux-polish-readiness.md](phase35g-final-uiux-polish-readiness.md).
- Architecture and technical-debt audit (post-RC.14): [phase36a-current-architecture-technical-debt-audit.md](phase36a-current-architecture-technical-debt-audit.md);
  test infrastructure reliability: [phase36b-test-infrastructure-reliability.md](phase36b-test-infrastructure-reliability.md);
  Early Ride Time editor extraction: [phase36c-ert-editor-maintainability.md](phase36c-ert-editor-maintainability.md)
  ([Windows checklist](releases/rc15-phase36c-localtest-windows-checklist.md)).
- Weather integration (Phase 37; approved in `../AGENT_BUILD_SPEC.md` §6.6):
  [phase37a-weather-source-architecture-plan.md](phase37a-weather-source-architecture-plan.md) (NWS source research,
  architecture, milestone plan and decision register D1–D8);
  [phase37b-weather-retrieval-and-cache.md](phase37b-weather-retrieval-and-cache.md) (retrieval, parsing, cache and tests —
  implemented locally; reference points verified (D8));
  [phase37c-dashboard-weather-integration.md](phase37c-dashboard-weather-integration.md) (Dashboard Weather card from the
  approved D4 mock-up, weather activated for the running app — implemented locally, awaiting Windows validation;
  [Windows checklist](releases/rc15-phase37c-localtest-windows-checklist.md); approved mock-up images:
  [design/phase37c-weather-mockup/](design/phase37c-weather-mockup/README.md));
  [phase37f-compact-pdf-weather-outlook.md](phase37f-compact-pdf-weather-outlook.md) (compact, park-hours-aware Weather
  Outlook in the Classic, Modern and Compact Passdown PDFs, no alerts — implemented locally, awaiting Windows validation;
  [Windows checklist](releases/rc15-phase37f-localtest-windows-checklist.md));
  [phase37d-calendar-weather-settings-integration.md](phase37d-calendar-weather-settings-integration.md) (Calendar
  selected-date Weather group with hourly detail, and Settings → Passdown Defaults "Include Weather Outlook by default"
  sharing the one Weather Outlook preference — implemented locally, awaiting Windows validation;
  [Windows checklist](releases/rc15-phase37d-localtest-windows-checklist.md));
  [pre-rc15-post-closing-pricing-retention.md](pre-rc15-post-closing-pricing-retention.md) (verified dated prices kept
  after the official calendar stops listing the date — implemented locally; Windows harness PASS, screen and PDF checks
  not yet reported; [Windows checklist](releases/rc15-pricing-retention-localtest-windows-checklist.md));
  [phase37e-integrated-weather-pricing-release-readiness.md](phase37e-integrated-weather-pricing-release-readiness.md)
  (integrated validation and RC.15 release-readiness assessment, plus the owner-approved D-1 Calendar retained-price
  wording — the integrated `RC15-Phase37E2-LOCALTEST` build was owner-accepted on Windows (overall, §26); [Windows checklist](releases/rc15-phase37e-integrated-localtest-windows-checklist.md)).
- RC.15 release preparation: [rc15-release-preparation.md](releases/rc15-release-preparation.md) (version `0.1.0-rc.15`,
  staged unsigned package, signing dry run, updater plan — prepared; published 2026-11-28 as a prerelease, §21) and the GitHub notes draft
  [rc15-release-notes.md](releases/rc15-release-notes.md).
- Defect record: [rc9-price-history-legacy-identity-defect.md](rc9-price-history-legacy-identity-defect.md).

## Research (not product features)

[research/](research/) — FIMS ride-data audits (Phase 22G). The prototype tool in
`../tools/research/fims-ride-monitor/` is research-only; its local output is never committed.

## Retired

| Document | Notes |
|---|---|
| [morning-message-rollback.md](morning-message-rollback.md) | Decision record retiring Morning Message (current authority for the retirement) |
| `phase29*`, `phase30-optional-morning-message-sync.md`, `phase29-30-windows-validation-kit.md` | Retired Morning Message implementation records; do not reintroduce |

## Deferred

Manager Passdown: requirements preserved in the [original greenfield specification](archive/original-greenfield-build-spec.md)
and summarized as deferred in `AGENT_BUILD_SPEC.md` §23. Not authorized for implementation.

## Superseded and archived

| Document | Notes |
|---|---|
| [archive/original-greenfield-build-spec.md](archive/original-greenfield-build-spec.md) | Original build specification, superseded by `AGENT_BUILD_SPEC.md` (see [archive/README.md](archive/README.md)) |
| [architecture/architecture-history.md](architecture/architecture-history.md) | Previous cumulative architecture document, preserved byte-for-byte |
| [archive/claude-skills/](archive/claude-skills/) | Superseded Claude skill text |
