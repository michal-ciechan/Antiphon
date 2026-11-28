---
name: antiphon-test-trace
description: Trace and explain Antiphon test runtime. Use when a test chunk or class is slow, when asked "where does test time go", to trace a nightly/rc chunk, or to investigate sub-2-minute chunks.
---

# Antiphon test trace

Follow the [test tracing runbook](../../../docs/test-tracing.md) from the repository root.

- Trace before proposing an optimisation. Use the original run's roster and wall time as the baseline. Label a class-only reproduction as a slice and verify its executed tests in TRX.
- Rank changes by expected wall-time saving and risk. Account for overlap, fixture ownership and intentional measurement windows. Treat an unobserved phase as unknown.
- Get the owner's decision before changing chunking, lanes, test semantics, or coverage. For an approved optimisation, change one thing and re-measure the same selection before moving on.
