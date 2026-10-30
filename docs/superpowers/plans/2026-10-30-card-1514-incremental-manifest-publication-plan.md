# Incremental executed-manifest publication

Task `bb0b355c`; base `17a39b9f05781743082c2ad225dbc4f60f2afa7e`.
CARD-1514 names the existing timing workstream; no new card identity is inferred.
Status: skeleton, investigation and verification design in progress. Product code is unchanged.

## Decisions

- D-1: Target cumulative publication after owner return. The measured opportunity is approximately 109.5 minutes of a 1378.1-minute run, not all 114.2 minutes of the original timestamp interval.
- D-2: Preserve per-test accounting, frozen release authority, ledger and receipt meaning, and recoverable completed-chunk evidence. Do not change test selection, concurrency, process ownership or unexplained historical exit tails.
- D-3: Verification design is included in this plan. Independent plan Review by a different company's model precedes implementation, as commissioned.

## Ground truth

| Assumption | Observed fact |
|---|---|
| Tests linger for about 20 seconds | The native investigation locates the dominant delay after owner return; the harness rewrites `tests.log` later. |
| Publication is constant per chunk | The executed manifest is cumulative; a 240-chunk prefix with 21,512 outcomes costs 24.147–31.239 seconds to publish, versus 0.020 seconds warm with an empty prefix. |
| All 114.2 minutes can be saved | Approximately 109.5 minutes is surrounding publication/bookkeeping. Five historical pre-log tails remain unexplained and outside scope. |

## Mechanism and cost

Read in full: `docs/investigations/2026-10-30-post-test-20s-exit-delay.md`.
Current-source call graph, consumer inventory, operation model and integration ordering are being checked before choosing the replacement.

## Slices

Pending: bounded writer/recovery design, consumer integration, regression and native replay slices with exact files and test names.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
This Plan executes no product tests. Checkpoint import will be validated through a slot-gated isolated tool bootstrap. Implementation checkpoints will carry explicit lane, phase/setup ownership, provisional duration provenance, bounded p95 collection and approximately 2x-p95 timeout derivation. Native Windows replay remains OWED; two historical publication observations are not a p95.

## Verification design

Required: deterministic production-path operation/byte-count RED witness independent of wall time, byte-equivalent compatibility artifacts at fixed inputs, kill/recovery/refusal cases, reader/liveness regressions, classification/census/OS inventory and native scale replay. Checkpoint table pending source roster inspection.
