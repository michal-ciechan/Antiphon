# CARD-1987 checkpoint extract (Code task 53932621)

Measure slice only: M1 `scripts/pg-qps.ps1`, M2 the 60 s sampler and `GET /api/diagnostics/db-rate`,
M3 call-site and transaction counters, and the prompt-span outcome counters. S1 and S2 are another
task. The parent plan's CP-3..CP-6 are not rows of this extract.

Parent plan: `docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md`.
Review: `docs/superpowers/reviews/2026-11-18-card-1987-plan-review.md` (clean, advisory only).

No migration. No positive-control row. Mutation execution is waived (operator 2026-11-12) and is not discharged.

## Verification design

- W-1..W-5. `scripts/test-pg-qps.ps1` on one throwaway `postgres:16` pinned by digest, removed by exact name.
- W-6..W-8. `DbRateSamplerTests` synthetic snapshots: rollups, reset and dealloc gaps, the 1,440 ring, the 2,000 label cap, redaction.
- W-9. `DbCallSiteMetricsTests`: sampled command name, transaction completions, async demangle, the 256 cap, unknown.
- Prompt span. `PromptSpanOutcomeTests` (allowlist and oversize, no database) and `PromptSpanOutcomeReadTests` (cold, hit, bypass, tail, full read after a failed proof).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | M1 | n/a | c1987-pgqps | `pwsh -NoProfile -File scripts/test-pg-qps.ps1` | W-1..W-5 | WITNESS passed=5 failed=0, exit 0 | n/a | 15 | true | n/a |
| CP-2 | M3 | tests/Antiphon.Tests -> bin-c1987-m/ | c1987-m | `/*/*/(DbRateSamplerTests*)\|(DbCallSiteMetricsTests*)\|(PromptSpanOutcomeTests*)/*` | W-6..W-9, allowlist, oversize | 0 failed, executed 13 | 13 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | M3 | CP-2 | c1987-span | `/*/*/PromptSpanOutcomeReadTests/*` | prompt-span read outcomes | 0 failed, executed 1 | 1 | 12 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
