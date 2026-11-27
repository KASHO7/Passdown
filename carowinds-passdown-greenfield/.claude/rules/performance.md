---
paths:
  - "src/**/*.cs"
  - "tools/perf-benchmarks/**"
  - "tests/**/*Performance*.cs"
  - "tests/**/*Timing*.cs"
---

# Performance and resources

Measure before changing. Use `tools/perf-benchmarks` and Phase 34B/C records; compare equivalent fixtures and environments. Preserve preview leading/trailing coalescing, engine warm-up, price history reader, official request coalescing and direct bitmap rendering. Avoid redundant requests, unbounded caches, needless allocations, unreleased native resources, redundant subscriptions/timers and unnecessary startup work. Do not introduce threading, WAL or caching just on a hypothesis; protect cancellation, thread affinity, state ownership and data integrity. Windows profiling is required before making Windows-specific performance claims. Document tradeoffs and deferred findings.
