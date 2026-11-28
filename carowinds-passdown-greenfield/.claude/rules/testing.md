---
paths:
  - "tests/**"
  - "tools/perf-benchmarks/**"
  - "tools/visual-fixtures/**"
  - "CarowindsPassdown.slnx"
  - "Directory.Build.props"
---

# Tests and validation

Use the documented deterministic test partitions in `docs/release-manifest.md` and latest phase records; keep live tests opt-in. Focused tests during implementation; full required deterministic suite and applicable clean Release build at implementation-phase close. Add regression tests for changed behavior and architecture boundaries. Never delete, skip or weaken tests to make them pass; replace obsolete assertions with equivalent or stronger contracts. Preserve real-window isolation; investigate hangs, memory exhaustion and intermittent failures, recording pre-existing versus new issues. Prefer deterministic scheduling seams over timing sleeps. Never report unrun tests or Windows validation as passed. Use synthetic data and keep personal profiles untouched.
