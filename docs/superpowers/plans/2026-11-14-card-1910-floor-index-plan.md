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
S4: split the timed-out combined integration row into the named methods/classes
already in that row; preserve assertions and the nine-minute execution cap.
No migrations, policy-input changes, timeout increases or assertion weakening.

## Performance budget

Pure witnesses share no database/process setup. Deterministic seeded data is local
to each method; counters measure algorithmic work, never elapsed time. Corpus
strings and JIT warmup precede the thread-local allocation measurement. Existing
integration fixtures retain their assembly/shared database lifetimes and isolation.
App ownership is server report settlement plus shared runner contracts; project
is Antiphon.Tests with its existing transitive ProjectReferences. No project graph changes. No new external calls or timeouts.

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
  Full base..HEAD audit at S3 selected the test-lane-category glob kind, covered
  by CP-4. The brief limits ordinary impact expansion to matching glob rows.
  Reverse-type suggestions also include 33 classes and a capped 405-consumer
  remainder for SessionRunnerContracts.cs; retain that census in ignored evidence,
  with no passing claim for unexecuted suggestions. The changed shared behavior is
  bounded by the local-record equivalence/allocation witnesses and the named
  prompt/working integrations. DTO shapes, serialization and HTTP contracts are
  unchanged; this task does not commission an assembly-wide contract replay.
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
PC-4 variants: case-insensitive command-name prefix, case-insensitive stdout
prefix, or missing whitespace trim; V-1 must reject each.
The unbound candidate is docs/superpowers/mutations/card-1910/candidate.md.
All PC variants remain pending for caller-commissioned method-scoped SourceLanding
Mutation; Code runs only ordinary test-first red/green, no deliberate mutants.

## S2 execution and S4 bounded rerun

Run 20261114-011456-a95a at f85e785c94fbc1f054c757406995c846b7bb979d
passed CP-2 (6), CP-4 (106), and CP-5 (32), with no failed/skipped TRX
results in those rows. CP-3, the combined SiblingToolReportBoundaryTests and
ChannelOutboundRecoveryTests row, reached its nine-minute deadline without TRX.
Its journal diagnostic completed 38 cases (36 passed, 2 failed) and had one
in-flight case; this is diagnostic, not a passing checkpoint or a TRX count.
The two failed method names were Running_converter_reconciles_after_dispatcher_death
and Recovery_preserves_settled_worker_and_publication_boundaries. Attribution is
unresolved until bounded reruns and, if red persists, exact base verification.
The full class union expands to 44 cases (26 sibling + 18 channel), not 35.

The historical CP-3 filter was
`/*/*/(SiblingToolReportBoundaryTests*)|(ChannelOutboundRecoveryTests*)/*`.
It is replaced in the current closed list by CP-6 through CP-9, preserving every
class, method and argument case. The failed methods run first so their complete
TRX can establish the assertion failures. No already-green proof, guard or
contract row is repeated. Keep the historical CHECKPOINT lines unchanged.

Measured S2 run wall was 1900.694s: charged execution 1330.186s, admission-only
570.507s. Build phase 368.086s (dotnet body 4m12s); CP-2 body 21.448s; CP-4
264.863s; CP-5 60.935s. Source/finalization remainder is retained in report JSON,
not attributed to fixture time. The over-30-minute run triggers caller-commissioned
Debug under CARD-1906. This delegate cannot sub-delegate; Debug is still pending.
S4 costs are provisional from the partial journal and fixed bounded selections:
CP-6 includes one cold build, CP-7 has six native publication cases, CP-8 the
26 sibling cases, CP-9 the remaining eleven channel cases. One measured attempt
per shard, under the unchanged nine-minute cap, supplies the collection sample.

## Final replay and completed Code evidence

S4 run 20261114-015417-51a6 at 88250fb99c186f088b8ff1cb5248a4821560f49d
passed CP-6/7/8/9: 1 + 6 + 26 + 11 = 44 results, zero failures/skips.
It passed strict checkpoint source validation. All ordinary Code selections total
188 passing TUnit results, with the earlier 144 at f85e785c94fbc1f054c757406995c846b7bb979d.
Production, tests and the derived index are byte-identical between those tips.
No base attribution is asserted for the timed-out batch's two failed journal
outcomes: both methods passed the bounded rerun without a production/test edit.

For a fresh Final replay at one reviewed tip, all active TUnit rows below use one
shared isolated build. Historical Code S2/S4 builds and actual tested SHAs remain
in their original receipts. This changes only replay build grouping and estimates;
filters, expanded cases, count floors and the nine-minute cap are unchanged.
CP-6/7/8/9 measured row execution was 138.722/170.144/63.558/415.073 seconds;
S4 wall was 1513.106 seconds, including 101.138 seconds of output cleanup.
Samples remain provisional, not p95. Do not repeat already-green Code selections.

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
| CP-2 | all | `tests/Antiphon.Tests -> bin-c1910-s2/` | floor-and-record | `/*/*/(TaskReportFloorIndexTests*)\|(LocalCommandRecordTests*)\|(TaskReportTurnSelectorTests*)/*` | V-1,V-2,V-3,V-4 | all 6 methods, 0 failed/skipped after repair | 6 | 5 |
| CP-4 | all | CP-2 | guards-and-derived | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(DerivedImpactIndexTests*)/*` | R-2 | all listed, 0 failed/skipped | 79 | 5 |
| CP-5 | all | CP-2 | prompt-working-contract | `/*/*/(TranscriptPromptSpanTests*)\|(TranscriptWorkingStateQueryTests*)\|(TaskReportHousekeepingTests*)/*` | R-3 | all listed, 0 failed/skipped | 32 | 3 |
| CP-6 | all | CP-2 | running-converter-rerun | `/*/*/ChannelOutboundRecoveryTests/Running_converter_reconciles_after_dispatcher_death` | R-1 | exact method, 0 failed/skipped | 1 | 3 |
| CP-7 | all | CP-2 | publication-boundaries-rerun | `/*/*/ChannelOutboundRecoveryTests/Recovery_preserves_settled_worker_and_publication_boundaries` | R-1 | all six argument cases, 0 failed/skipped | 6 | 3 |
| CP-8 | all | CP-2 | sibling-boundaries-rerun | `/*/*/SiblingToolReportBoundaryTests/*` | R-1 | all 26 methods, 0 failed/skipped | 26 | 2 |
| CP-9 | all | CP-2 | channel-remainder-rerun | `/*/*/ChannelOutboundRecoveryTests/(Resume_held_after_conversion_returns_to_ready*)\|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)\|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)\|(Broker_ack_before_process_death_remains_uncertain_without_replay*)\|(Process_death_preserves_ownership_at_each_boundary*)\|(Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not*)\|(Expired_publishing_lease_is_uncertain_until_explicit_retry*)` | R-1 | all 11 argument-expanded cases, 0 failed/skipped | 11 | 7 |
