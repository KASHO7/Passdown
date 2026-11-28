---
name: phase-review
description: Close out an authorized phase, subphase or task for Carowinds Operations. Use before reporting completion — verifies scope, tests and build, the full diff, product specification maintenance and architecture documentation maintenance, then reports and stops.
---

# Phase review

Follow `CLAUDE.md` ("Required task workflow", "Architecture documentation gate", "Completion
report") and `.claude/rules/documentation.md`. Never start the next phase automatically.

## 1. Confirm scope and authority

- Identify the authorizing request or approved decision and the affected `AGENT_BUILD_SPEC.md`
  sections. Read only the relevant sections, current architecture guides and the latest related
  phase/release records.
- Do not treat the archived original greenfield specification
  (`docs/archive/original-greenfield-build-spec.md`) or its original phase plan as an active plan.
- Manager Passdown stays deferred: no Manager UI, PDF, view models or navigation. Morning Message
  stays retired. Preserve their compatibility infrastructure.

## 2. Verify implementation

- Implementation phases: run the `build-and-test` skill, including every deterministic partition
  and a clean Release build. Documentation-only work: run consistency checks (links, referenced
  paths, stated facts against source).
- Record actual counts and results; distinguish pre-existing from new failures.

## 3. Review the complete diff

```bash
git status --short && git diff --stat HEAD
```

- Dependency direction: Domain has no internal references; Application → Domain; Infrastructure and
  Documents → Application/Domain and not each other; Desktop → Application, Infrastructure,
  Documents; Updater → Infrastructure. No framework or I/O packages in Domain.
- No business logic, persistence, retrieval or PDF generation in Avalonia code-behind.
- No fabricated or prototype data; no silent date substitution; no online retrieval on date
  selection or navigation.
- Schemas, sync formats, templates, versions and release artifacts change only when authorized.
- Unrelated and staged user work untouched; no secrets, user databases, drafts or sync files.

## 4. Product specification maintenance (mandatory)

If an approved change affects behavior, business rules, workflows, defaults, scope, data ownership,
compatibility, privacy or acceptance criteria, update `AGENT_BUILD_SPEC.md` in the same change set
and cite the approved decision. Supersede obsolete requirements explicitly; keep active, proposed,
deferred and retired functionality distinct; never codify accidental behavior or edit requirements
to make tests pass. Report exactly one of:

- **Product specification:** Updated / Reviewed — no changes required / Discrepancy requires approval.

## 5. Architecture documentation maintenance (mandatory)

Review the diff for dependencies, DI, state ownership, data contracts, retrieval, persistence, sync,
PDF/printing, lifecycle, threading, configuration, UI architecture, security, packaging and
retired components. Update `docs/architecture.md` and the affected `docs/architecture/*.md` guides
in the same change set, verified against source. At phase close record the branch, reviewed source
baseline commit (not a self-referential hash), dirty-tree caveats, schema facts and validation.
Report exactly one of:

- **Architecture documentation:** Updated / Reviewed — no changes required / Discrepancy requires investigation.

A phase cannot be declared complete while a relevant known documentation discrepancy is unresolved.

Write the phase record from `docs/templates/phase-record-template.md`; it must contain the
`**Product specification:**` and `**Architecture documentation:**` status lines, which
`PhaseRecordImpactReviewTests` enforces for new records. Run the repository guards
(`FullyQualifiedName~.RepositoryGuards.`) for every task. If a guard fails, fix the documentation or
the source (whichever is wrong, after review); never weaken a guard, edit a historical record, or add
to its grandfathered list to make it pass. An approved dependency or schema change updates the guard
table and the architecture documents in the same change set.

## 6. Report and stop

Report scope, changed files, decisions, test/build results, both documentation statuses,
compatibility/performance effects, Windows validation status, blockers and Git/release state.
Keep implementation, automated validation, Windows review, owner approval and publication as
separate statuses. Then stop.
