# Documentation and architecture maintenance

- Current facts belong in `docs/architecture.md` and its linked subsystem documents; historical evidence belongs in phase/release records and `docs/architecture/architecture-history.md`. Do not append contradictory phase summaries to current architecture. Keep links valid and clearly mark retired/deferred/proposed features.
- Before work, identify the affected architecture sections; investigate contradictions against actual source and tests. For stale documentation, reconcile through a read-only source review (documentation edits only) before related architectural changes.
- At task close review the complete diff for dependencies, interfaces, registrations, view ownership/navigation, persistence, schemas, sync, retrieval/cache, PDF/printing, startup/shutdown, threading, preferences/config, security/release and retirement. Update meaningful affected sections together with source; otherwise record no change required.
- At phase close record branch, reviewed source commit or archive identity, dirty-tree caveats, scope, verified paths/contracts, schema facts where relevant, validation performed and pending, and architecture impact. If the document itself changes the commit, identify the reviewed source baseline rather than inventing a self-referential commit hash.
- Run applicable existing architecture tests and consistency checks. Automated checks validate mechanically testable facts only; never treat them as proof of semantic accuracy. Report blocked checks and do not bypass established gates.
- Preserve historical evidence; add later sign-off separately. Never claim planned work is implemented or tests/Windows reviews ran when they did not.
## Mandatory Product Specification Maintenance

`AGENT_BUILD_SPEC.md` defines the approved product requirements, business rules, functionality and acceptance criteria.

Whenever an explicitly approved change affects product behavior, business rules, workflows, defaults, compatibility, privacy or acceptance criteria:

- Update the affected product specification in the same change set.
- Identify the approved decision authorizing the change.
- Correct or explicitly supersede obsolete requirements.
- Preserve relevant historical requirements.
- Distinguish active, proposed, deferred and retired functionality.
- Never treat accidental implementation behavior as an approved requirement.
- Never change requirements merely to make failing tests pass.

Before completing any phase or subphase, report both:

- Product specification: Updated / Reviewed — no changes required / Discrepancy requires approval.
- Architecture documentation: Updated / Reviewed — no changes required / Discrepancy requires investigation.

Internal refactoring and performance optimization require an architecture review but generally do not require product specification changes unless approved externally observable behavior changes.

A phase cannot be declared complete while relevant known documentation discrepancies remain unresolved.