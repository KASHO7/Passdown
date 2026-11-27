# Archive

Superseded documents kept for historical context. Nothing here is an active requirement or plan.

| File | Provenance | Status |
|---|---|---|
| `original-greenfield-build-spec.md` | Byte-for-byte copy of the original *Carowinds Passdown — Greenfield Build Specification and Agent Handoff* as committed in `4364f95` ("Add Specs", as `AGENT_BUILD_SPEC (2).md`) and moved unchanged to `carowinds-passdown-greenfield/AGENT_BUILD_SPEC.md` (Git blob `5a16e09`). Archived 2026-11-26 when the owner-approved product specification replaced it. | **Superseded** by `../../AGENT_BUILD_SPEC.md` (§26 lists the superseded requirements). Its Manager Passdown requirements are preserved here as **deferred**; its Phases 1–7 plan and Playwright fallback are historical. |
| `claude-skills/ui-polish-legacy-staged-as-pdf-design.md` | Byte-for-byte copy of the index entry staged (never committed) at `.claude/skills/pdf-design/SKILL.md` (Git blob `83d7eaa`). Despite its path it is an extended version of the earlier *UI Polish* skill, with no frontmatter and no PDF content. | **Superseded** by `.claude/skills/ui-polish/SKILL.md`; preserved because it was staged user work. The real PDF Design skill is `.claude/skills/pdf-design/SKILL.md`. |

Verify a copy with `git hash-object <file>`; it must equal the blob listed above.

`HistoricalDocumentIntegrityTests` checks these files (and `docs/architecture/architecture-history.md`)
on every test run without Git: it computes the Git blob id itself (SHA-1 of `blob <byte length>\0`
followed by the file bytes). CRLF is first normalized to LF, matching Git's own `core.autocrlf`
checkout behavior, so a Windows checkout of these LF-only files hashes to the same id. Any other byte
change, including trailing whitespace, fails the test.
