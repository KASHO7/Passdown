# Carowinds Operations — Development Instructions

Internal project: `Carowinds.Passdown`. Target: Windows 11 x64. Determine the actual development host, SDK, branch, version and phase from the repository; do not assume historical values.

## Authority and scope

- `AGENT_BUILD_SPEC.md` is the authoritative product specification, subject to explicitly approved later decisions. `docs/architecture.md` describes the current implementation; phase/release records preserve historical evidence. Verify claims against source and tests. Report contradictions instead of silently changing behavior.
- The product owner does not write or debug code. Handle implementation, dependencies, builds, tests, migrations, documentation and packaging. Ask only for genuine unresolved product, UX, business or release decisions. Provide prepared packages/checklists when Windows review is required.
- This is an established local-first application. Preserve existing functionality and compatibility; do not copy, patch or reskin the retired predecessor. Prefer incremental, reversible changes; no speculative abstractions, dependencies or rewrites.
- Manager workflow remains deferred; no Phase 5 or placeholder Manager UI/PDF/view models/navigation. Preserve required compatibility infrastructure. Morning Message is retired; do not revive it or its sync channel without explicit approval.

## Non-negotiable contracts

- Preserve Domain/Application/Infrastructure/Documents/Desktop/Updater dependency boundaries; no cycles, business logic or I/O in Avalonia code-behind. Keep Windows integration isolated.
- Only official Carowinds/Six Flags operational data; verify exact dates, never invent data or silently substitute dates. Selecting a date or navigating must not trigger online retrieval. Failed retrieval cannot overwrite last-known-good data.
- Manual draft edits cannot modify official cache. Preserve correct-date autosave, conflict protection and required flushes; finalized history and stored PDFs are immutable. Never place live SQLite or WAL/SHM files in cloud-sync folders. Preserve sync compatibility and historical migrations.
- Preserve one shared document-generation pipeline, latest-edit output and stale-date protection. Maintain accessibility, approved visuals and reduced-motion behavior. Do not add fake prototype data.
- Keep credentials, signing keys and user data out of logs, fixtures and release packages. Never bypass update verification. Published artifacts are immutable.
- Inspect Git status; protect unrelated work. No destructive Git operations, history rewriting, merging, pushing, tagging, signing or publishing without explicit authorization. Do not assume remote is current.

## Required task workflow

1. Inspect Git status, relevant source, specification sections, architecture and phase records. Read only what is relevant; identify affected contracts and applicable `.claude/rules/` files (read them explicitly if path loading has not occurred).
2. Plan briefly; establish a baseline or reproduction appropriate to the task.
3. Implement only authorized work; reuse established services, skills and tooling.
4. Run focused tests and applicable `dotnet build` checks; fix failures caused by the work. Before implementation-phase completion, run the required deterministic partitions and build checks. Documentation-only work requires consistency checks, not gratuitous production edits.
5. Review the entire diff. Complete the architecture/documentation gate below. Report evidence and stop; never automatically start the next phase.

## Architecture documentation gate (every task)

Review the diff for changes to dependencies, DI, state ownership, data contracts, retrieval, persistence, sync, PDF, lifecycle, threading, configuration, UI architecture, security, packaging and retired components. Update affected current-state documentation in the same change set, verify claims against source, and retire obsolete descriptions. If no architectural change, record **reviewed — no change required**; do not make meaningless edits. At phase close verify relevant paths, versions, contracts and architectural tests, record the reviewed source baseline and outstanding limitations. No phase completion without this review. Never change source merely to match stale docs. See `.claude/rules/documentation.md` for the detailed gate.

## Completion report

Report scope, changed files, decisions, tests/build results, architecture review (updated/no change/blocked), compatibility and performance effects where relevant, Windows validation status, blockers, and Git/release state. Distinguish implementation, automated validation, Windows review, owner approval and publication. Never claim unrun checks or declare broader phases complete. Relevant required failing tests block implementation completion; document pre-existing failures separately. Live website tests remain opt-in. Non-Windows testing is not Windows validation.

## Detailed rules

Global documentation and release/security rules are always active. Other rules are path-scoped; explicitly consult relevant rules for cross-cutting work, shell scripts or planning before editing. Existing `.claude/skills/ui-polish/SKILL.md` and `.claude/skills/pdf-design/SKILL.md` remain in place; use as applicable. Do not load all historical phase records on every task.
