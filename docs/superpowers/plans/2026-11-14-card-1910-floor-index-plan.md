# CARD-1910: index real prompts once and remove local-record copies

Base: `f22a2c1476da7c85a5b54cb04215b451ea73c0c9`. Code/landing owner: task 09095036.
The caller supplied the implementation and verification design directly; this records
that design as an executable closed list. CARD-1760 commits `6ac68b684edeadb9ed747c152527ef2f5ed5e01d`
and `f9181e7c91a72cfdf802cdfd7799ff70114a2350` supply the linear-oracle/probe pattern.
No separate CARD-1760 plan is present in master's tree.

S1: retain the scan behind an internal per-selection index seam; add differential
floor/lookup and local-record corpus tests, classification-count and allocation
witnesses. Commit and demonstrate the two performance failures before repair.
S2: materialize sequence-ordered real rows once; binary-search strict bounds,
including the rightmost equal-sequence row. Use the same index for floor owner, adjacent owner helpers, final caps
and next-real lookups. Replace only IsLocalCommandRecord's TrimStart with a span.
S3: regenerate derived impact bytes in their own commit and run remaining rows.
No migrations, policy-input changes, timeout increases or assertion weakening.

## Performance budget

Pure witnesses share no database/process setup. Deterministic seeded data is local
to each method; counters measure algorithmic work, never elapsed time. Corpus
strings and JIT warmup precede the thread-local allocation measurement. Existing
integration fixtures retain their assembly/shared database lifetimes and isolation.
App ownership is server report settlement plus shared runner contracts; project
is Antiphon.Tests with their existing transitive
ProjectReferences. No project graph changes. No new external calls or timeouts.

Budget is provisional (CARD-1404): no comparable p95 for this new selection.
Collect one red and one green witness sample, then one run of each remaining row;
retain TRX per-test times and checkpoint build/slot timings. CP-0/1 are bounded
driver costs (CARD-1861 precedent); cold server build estimated 4 minutes,
pure tests under 1 minute, named integrations under 4 minutes, guard replay under
5 minutes, portable prompt/working/allowlist contracts under 3 minutes with the shared build. Each row is limited
to 9 minutes (`--row-timeout 9m`), below the caller's ten-minute ceiling; slot
admission remains separately bounded by the broker default. Record /proc/loadavg;
timeout with load >30 is infrastructure evidence, not a product verdict.
Unknown fixture/external phases remain unknown, not zero. A complete run above
30 minutes requires caller Debug triage under CARD-1906; do not repeat it blindly.
No repetitions required after green; at most 3 normal + 2 loaded per unchanged selection.

## Verification design

- V-1: LocalCommandRecordTests.Record_check_matches_original_over_corpus: old/new
  equality for null/wrong kinds, Unicode whitespace, case, markers, near misses,
  empty and long records. Ordinal and TrimStart semantics remain exact.
- V-2: LocalCommandRecordTests.Record_check_allocates_zero_bytes: warmed synchronous
  thread allocation delta is exactly zero and observed matching count is correct.
- V-3: TaskReportFloorIndexTests.Floor_matches_original_scan_for_seeded_sessions:
  private old floor and lookup oracle; empty sessions, ties, first/last strict
  floors, interleaved real/local/phantom boundaries, stop reasons, dispatch/reply
  constraints, both owning kinds. Existing selector regression tests stay green.
- V-4: TaskReportFloorIndexTests.Floor_classifies_each_row_once_for_twenty_thousand_rows_and_two_thousand_boundaries:
  actual production classifier invocation count is exactly 20,000 and floor is
  null across 1,999 transparent candidates; old scans exceed the bound.
- R-1: TaskReportTurnSelectorTests, SiblingToolReportBoundaryTests and
  ChannelOutboundRecoveryTests are the complete grep-derived selector consumers,
  including integration report attribution and watchdog/turn behavior.
- R-2: standing policy/mechanical/platform/layering/census union plus
  DerivedImpactIndexTests. Check matching impacted-classes globs over full base..HEAD
  and append any newly selected bounded classes before executing them.
- R-3: TranscriptPromptSpanTests, TranscriptWorkingStateQueryTests and
  TaskReportHousekeepingTests cover local-command filtering and turn ownership.
  Eligibility audit before execution found FakeClaudeContractTests requires Windows
  ConPTY; the initial extra CP-5 was replaced by these portable integration/allowlist
  consumers. No Windows or headed canary is credited as passing. No OS-selected
  behavior changes, so neither native canary is a required changed-branch witness.
- R-4: standalone impact-driver witness before server builds; regenerate impact
  index in its own commit. Verify evidence-diff, OS inventory and conflict-free
  merge-tree over the full task. Changed behavior is portable; no OS-selected branch.
- Manual M-1: inspect production call wiring and strict tie bounds; validate fresh
  TRX roster/counts and source receipts at each actual committed SHA.
- Manual M-2 (post-activation, caller): server restart required. Repeat the five-minute
  sampled-thread-time trace and Get-Process deltas using commands in
  `docs/investigations/2026-11-13-desktop-cpu-trace.md` on origin/feat/card-task-b754552c
  at `c1963c4c0`. Compare NewestRealBetween + IsLocalCommandRecord from 0.52 cores
  toward <0.05 and mean server reduction ~0.4 cores under comparable session load.
  This is an expected saving, not a Code-stage live measurement.

PC-1: restore scan-based index queries; V-4 must fail with excess classifications.
PC-2: restore TrimStart string copies; V-2 must fail with nonzero bytes.
PC-3 variants: make before inclusive, make floor inclusive, choose first tie,
include housekeeping, omit queued prompts; V-3 must reject each.
PC-4 variants: case-insensitive marker or missing whitespace trim; V-1 must reject.
All PC variants remain pending for caller-commissioned method-scoped SourceLanding
Mutation; Code runs only ordinary test-first red/green, no deliberate mutants.

### Checkpoints

At S1 SHA 1de6dcbc1c879abd91712d70ee1d381faac2f578, run
20261114-005053-7b6f executed CP-2: 6 results, 4 passed, exactly two expected
performance failures: 19,991,999 classifications vs 20,000; 2,800,000 bytes vs zero.
Build and source were verified clean. The S2 output is fresh; no red output is reused.

Run through tools/Antiphon.Checkpoints with --serial --row-timeout 9m and
--expected-source-sha <committed HEAD>; CP-2 runs red at S1, then green at S2.
CP-0/1 precede CP-2; remaining rows follow regenerated S3. Command rows acquire their own executor build-slot lease; do not nest a wrapper.
The first run 20261114-004558-46dd completed CP-0, then was stopped while CP-1
waited for a redundant nested lease. CP-2 had not started. Preserve those outcomes;
resume CP-1/CP-2 with one executor-owned lease per driver.
Run 20261114-004745-27cc passed CP-1, but CP-2 built with a one-minute deadline
because the CLI interpreted bare 9 as seconds. No tests executed. Correct the
CLI unit to 9m (the existing nine-minute budget); retain the failed output and
use the fresh bin-c1910-s1/ output. CP-0/1 need no further repetition.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-0 | S1 | n/a | impact-driver-build | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo` | R-4 | exit 0, standalone driver only | n/a | 1 |
| CP-1 | S1 | n/a | impact-driver-witness | `pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1910-driver-witness` | R-4 | DRIVER WITNESS passed=3 failed=0 | n/a | 1 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1910-s2/` | floor-and-record | `/*/*/(TaskReportFloorIndexTests*)\|(LocalCommandRecordTests*)\|(TaskReportTurnSelectorTests*)/*` | V-1,V-2,V-3,V-4 | all 6 methods, 0 failed/skipped after repair | 6 | 5 |
| CP-3 | S2 | CP-2 | report-turn-integration | `/*/*/(SiblingToolReportBoundaryTests*)\|(ChannelOutboundRecoveryTests*)/*` | R-1 | all listed, 0 failed/skipped | 35 | 4 |
| CP-4 | S2 | CP-2 | guards-and-derived | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-2 | all listed, 0 failed/skipped | 79 | 5 |
| CP-5 | S2 | CP-2 | prompt-working-contract | `/*/*/(TranscriptPromptSpanTests*)\|(TranscriptWorkingStateQueryTests*)\|(TaskReportHousekeepingTests*)/*` | R-3 | all listed, 0 failed/skipped | 32 | 3 |
