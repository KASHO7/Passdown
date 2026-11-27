# Carowinds Operations

Local-first Windows 11 x64 desktop application for Carowinds Admissions: Dashboard, Operations
Calendar, Associate Passdown, Products and Pricing, Operational Notes, Finalized History, Personal
Sync, PDF preview/printing and verified updates. Internal project namespace: `Carowinds.Passdown`.
Internal repository — not a public product.

## Repository layout

The Git root is the parent folder; this application directory holds everything below.

| Path | Contents |
|---|---|
| `CarowindsPassdown.slnx`, `Directory.Build.props` | Solution; single source of version and target framework |
| `src/` | Domain, Application, Infrastructure, Documents, Desktop (Avalonia), Updater |
| `tests/` | One xUnit project per production layer (UI tests are Avalonia headless) |
| `fixtures/` | Synthetic official-source fixtures for offline contract tests |
| `tools/` | Update signing, updater test harness/offline stager, perf benchmarks, visual fixtures, research |
| `packaging/windows/` | Portable release scripts, tester guide, third-party licenses |
| `docs/` | Architecture, phase and release records — see [docs/README.md](docs/README.md) |
| `artifacts/` | Generated release output (ignored by Git; published artifacts are immutable) |

## Authority

1. [AGENT_BUILD_SPEC.md](AGENT_BUILD_SPEC.md) — product requirements and acceptance criteria.
2. [CLAUDE.md](CLAUDE.md) and [.claude/rules/](.claude/rules/) — development instructions.
3. [docs/architecture.md](docs/architecture.md) — current implementation, verified against source.
4. Phase and release records — historical evidence.

## Build and test

Requires the .NET SDK for the target framework in `Directory.Build.props`.

```bash
dotnet build CarowindsPassdown.slnx -c Release
```

Deterministic test partitions (including the six real-window UI classes run alone) are listed in
[.claude/skills/build-and-test/SKILL.md](.claude/skills/build-and-test/SKILL.md). Live-website tests
(`Category=LiveSmoke`) are opt-in only. Repository guards in
`tests/Carowinds.Passdown.Infrastructure.Tests/RepositoryGuards` check the project dependency graph,
documented schema versions, documentation links/paths, archived-document integrity and phase-record
maintenance statuses. Release packaging follows
[docs/release-strategy.md](docs/release-strategy.md); never rebuild a published version.

## Claude Code

Open Claude Code sessions in this directory so `CLAUDE.md`, `.claude/rules/` and the project skills
load directly. Skills: `build-and-test`, `phase-review`, `retrieval-source`, `ui-polish`,
`pdf-design` (all in `.claude/skills/`).

## Repository hygiene

macOS metadata and generated test/IDE output are ignored. Some `.DS_Store` files were committed or
staged before those rules existed; ignoring them does not untrack them. Removing them from the index
(`git rm --cached`) is a separate step that needs the owner's approval.
