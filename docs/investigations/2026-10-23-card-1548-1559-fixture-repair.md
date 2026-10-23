# CARD-1548/1549/1550/1559 Code handoff

Original Code task / landing owner: 3fa840bf-f753-4f7f-8570-ddc66866711b.
Branch: feat/card-task-3fa840bf. Worktree: /work/worktrees/task-3fa840bf.
Base: 877b9ce108e6e2bb4ba81840a99c4dd788302ca4, the assigned current origin/master.
Plan/manifest: docs/superpowers/plans/2026-10-23-card-1548-1559-fixture-repair.md.
This report is frozen before final qualification. Final immutable receipt paths,
actual tested SHA and class timings are supplied in the stored task report
`.antiphon/task-3fa840bf.md` and caller completion report; pending here is not passed.

## Implementation

- CARD-1548: discard the one synthetic purge target's owned SessionRuntimeAction
  before its transcript row, under the existing store mutation lease. Preserve
  exact delete counts, sequence/epoch assertions and every other action.
  Warmup and inbound replay fixtures drain their own recipient's effects through
  the real SessionRuntimeActionCoordinator and assert debt is empty before pruning.
  The baseline also exposed real-delivery's unprocessed seed actions: drain those
  before asking the real queue to deliver; retain all submission/receipt assertions.
- CARD-1550: seed through the production runtime/store, then restart the observer
  provider without SessionStateStore. Remove the inherited registration explicitly
  and assert null before the measured passes. Disabled store remains distinct.
- CARD-1549: retain exact master budget [9,8,8]. Durable admission's active-task
  Id/Attempt authority SELECT and TurnEnd ApiCallId dedup SELECT are part of the
  admitted-action authority path. CARD-1403 consolidates the prior session,
  identity and MAX(sequence) probes into one command on master. RC predates that
  consolidation and needs exactly [11,10,10], supplied by a separate pick.
  Receipt SELECTs remain exactly [1,0,0]; full untruncated command rosters now
  accompany the live-committed-row result. No product code or cost range changed.
- CARD-1559: already fixed at base by
  b6615e643e570be7af25ec946153b52f03d3ee79. Its fixture deletes owned cleanup
  endpoints, targets and obligations before sessions/worktrees/revisions/cards.
  Preserve this fix and qualify the complete CardCorrection class.

No new test classes, timeouts, retries or weakened assertions. Existing fixtures
remain real production types. No allowlist changes are needed for new classes.

## Red evidence and attribution

Retained Windows diagnosis: branch origin/feat/card-task-392d9d33,
`docs/investigations/2026-10-23-rc-5a126f6fc-qualification-groups-15-20-debug.md`.
Candidate 5a126f6fc5ae4e16d803b628716b8f0485a37174, run 6bd38cbb9604482e96a4854957cead05.
It records both watermark FK arms, absent-store [1,0,0] vs [1,1,1], budget
[11,10,10] vs [9,8,8], both warmup prune counts 0 vs 1, inbound queue-pruned
0 vs 1, and twelve CardCorrection cleanup FK failures; all inherited there.
Use that evidence for the non-watermark baseline rows rather than another costly
build/run. CARD-1559 and the master budget are already repaired by prior work.

Fresh CP-1 at 58f6ebd517c06f0165d42d5572a603081f541dfc:
36 executed, 32 passed, 4 failed, 0 skipped, clean verified build source.
Fresh TRX inspection found ONLY AgentTaskLandReceiptWatermarkTests (36 nodes).
The bare OR between full paths was invalid on pinned TUnit and silently selected
the whole first class. Warmup/inbound did not execute; never count them as baseline
passes. Preserve the requested filter below as historical evidence, not a reusable
selection. Final CP-2..CP-5 use individual full-class filters, one shared build.
The four reds were the two stated FK arms, stated store-absent case and inherited
running-destination-real-delivery (no submission, undispatched durable seed actions).
The live-committed-row master budget passed unchanged. No new tests were substituted
for these existing behavior witnesses. Final rows must rerun all four reds.

An initial checkpoint admission used an incorrect expected SHA and was refused
before any build/test. It is not execution evidence. The corrected run is below.

CHECKPOINT CP-1 commit=58f6ebd517c06f0165d42d5572a603081f541dfc build=ok filter=/*/*/AgentTaskLandReceiptWatermarkTests/C1121_AnyCommittedChangeReopensFullScan*|/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnchangedTranscriptSkipsOnlyReceiptSelect*|/*/*/AgentTaskLandReceiptWatermarkTests/C1121_UnknownEvidenceUsesExistingScan*|/*/*/SessionStateWarmupTests/Retention_*|/*/*/ChannelInboundRecoveryTests/C768_BrokerReplayAfterPayloadPurgeKeepsNativeIdentity* executed=36 passed=32 failed=4 skipped=0 trx=/work/worktrees/task-3fa840bf/.antiphon/checkpoints/20261023-120037-09d5/rows/CP-1/run.trx slot=granted waited=60s dirty=0 source=58f6ebd517c06f0165d42d5572a603081f541dfc sourceState=clean buildSource=verified

CP-1 build driver: 376.47s; tool build phase including provenance: 415.20s.
Slot granted, build waited 0s; test waited 60s. Host wall 98.90s, startup 61.82s,
tests wall 31.89s, teardown 5.17s. Total run 9m53s including build and slot waits.
No unchanged-proof repetitions. The full class was selected accidentally, not a
justified expansion to another assembly or Unit lane. Final class filters fix it.

## RC pick order

Apply, in this exact order, onto origin/rc/2026-10-20 at
5a126f6fc5ae4e16d803b628716b8f0485a37174:

1. b6615e643e570be7af25ec946153b52f03d3ee79 (existing CARD-1559 cleanup fix).
2. f24010a46af8d7cd953ecf724257718959edc260 (common fixture repairs).
3. d87adc5768b61c6126d3edbb308eef987327edae (RC-only exact budget [11,10,10]).

Do not pick 58c2b8ec6191c0f503324ad60941440c8c001dfa onto this RC: it restores
master's consolidated-prelude budget [9,8,8]. Plan/report commits are not required
RC code picks. The assigned branch remains fast-forward-only; this deliberate
RC budget commit and subsequent master adjustment were each committed/pushed.

Scratch `/tmp/antiphon-3fa-rc` starts at the exact RC SHA. `cherry-pick --no-commit`
of all three succeeded without conflicts; index tree is
fb531f381f149c7279aed82058f15e6698ac293d. It intentionally has no new detached
commit. Compile uses `timeout 900s pwsh -NoProfile -File scripts/build-slot.ps1
-Label 3fa-rc-compile -- dotnet build tests/Antiphon.Tests
--property:OutputPath=bin-3fa-rc/ --property:UseAppHost=false --nologo`.
Results and measured build time are recorded below before final source freeze.
The RC fixture's only deliberate source-specific adaptation is the budget; all
other edits apply unchanged, including existing CARD-1559. This compile is R-4,
not a Windows execution receipt. Native Windows rows remain OWED to the separate
desktop qualification lane; this task does not use the desktop or repair its gate.

## Required ordinary verification and mutation

Final ordinary execution: V-1,V-2,V-3,V-4,R-1,R-2,R-3 via CP-2..CP-5; R-4 via
scratch compile. No Unit lane or whole-suite run: explicit commissioning override.
No implementation changes beyond these fixtures. No manual product activation.
Restart: none. Next: light ordinary Review once those required Linux rows pass.
Caller lands original Code task, then commissions SourceLanding Mutation.

No new PC IDs; Mutation owns missing-control discovery, including this zero-new-PC
repair. Existing CARD-1121 catalogue and all variants remain pending for Mutation:
PC-1 (every named flip plus positive), PC-2a..g, PC-3a/b, PC-4a/b, PC-5,
PC-6a/b/c (all five arms)/d, PC-7a/b/c, PC-8a/b/c, PC-9a/b, PC-10,
PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17 (all seven identity rows).
Exact variants/methods remain defined in
`docs/superpowers/plans/2026-10-08-card-1121-receipt-scan-watermark-test-design.md`.
No ordinary receipt discharges a deliberate red/restore/green control.

Additional builds outside CP rows: checkpoint-tool bootstrap (required to execute
the tool; host-slot gated, waited 0s, 16.78s driver / 18s lease) and required RC
compile (R-4). All other builds/tests are checkpoint-owned. No full-assembly run.
Before completion run receipt validation against the final pushed/tested SHA,
check-evidence-diff over full task base..HEAD, and merge-tree against freshly
fetched origin/master; retain all original outcomes and costs.

## First final attempt and master-only repair

Run 20261023-121620-cf2f at 7c46aed3925ac6d5d6aee1ddc16c887615b47731
completed CP-2 with 36 executed, 35 passed, 1 failed, 0 skipped. Both FK arms,
store-absent and exact master budget passed. The remaining inherited real-delivery
case was still held: master CARD-0680 correctly refuses Claude input without a
current, generation-bound dialog observation. Its default fake supplies none.
Use the existing ClaudeDialogFixture.EnableClaudeDialogObservationAsync on this
positive-delivery witness before enqueue; preserve every delivery assertion.
This is a master-only fixture API, absent from RC, so exclude this follow-up
commit from the three RC picks above. No product guard changes.

CHECKPOINT CP-2 commit=7c46aed3925ac6d5d6aee1ddc16c887615b47731 build=ok filter=/*/*/AgentTaskLandReceiptWatermarkTests/* executed=36 passed=35 failed=1 skipped=0 trx=/work/worktrees/task-3fa840bf/.antiphon/checkpoints/20261023-121620-cf2f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7c46aed3925ac6d5d6aee1ddc16c887615b47731 sourceState=clean buildSource=verified

CP-2 host wall 109.57s, startup 51.56s, tests wall 54.93s, teardown 3.08s.
Shared build driver 292.84s (0 errors); build slot granted, waited 45s.
CP-3 waited for a slot for 180s and did not launch tests. CP-4/CP-5 did not launch.
Explicitly stopped the executor before source edits to fix CP-2 and requalify all
four classes at the final tip. Stop exited 0; the concurrent wait reported exit 6
(executor died without phase=done / report handle verification refused), so there
is no complete successful receipt for this stopped run. Its completed CP-2 line
and fresh TRX remain historical red evidence; canceled rows are NOT passes.
The stop's initial shadow cleanup raced the observer (IOException); the waiting
observer then reported TOOL CLEANUP Removed / identity-dead. No test remained
running when source changed. Fresh isolated output is used for the next run.

Inspected the complete live-committed-row SQL from the fresh TRX, all three passes:
two notification SELECTs, one session Status/StartedAt SELECT, one queue-row
SELECT, one combined session/identity/MAX prelude, one active-task Id/Attempt
SELECT, one TurnEnd ApiCallId dedup SELECT, one first-pass receipt SELECT,
and one concurrency-token UPDATE. Totals are exactly 9/8/8; only the receipt
SELECT disappears. The two authority SELECTs are retained for the durable
admission path, not removed as a test repair. RC's three prelude probes replace
the one combined prelude: exactly 11/10/10. Full SQL is retained in the live-row
TRX StdOut; no range, sampled roster or tautological count replaced the assertion.

R-4 completed: all three RC picks applied without conflicts, Antiphon.Tests built
with 0 errors in 275.76s; BUILD SLOT granted, waited=0s, released after 276s.
Post-build worktree-to-index diff is empty and index tree remains
fb531f381f149c7279aed82058f15e6698ac293d. No RC execution/Windows pass is claimed.
