# CARD-1289 Code report

Implemented and pushed; the guarded defects are fixed, but Final verification is incomplete. Next: Code to finish qualification, then ordinary Review. No landing or deployment was performed.

Original Code task / landing owner: 5a9cd727-c32f-4f59-96ac-40b5a91c70b2.
Branch: feat/card-task-5a9cd727. Worktree: /work/worktrees/task-5a9cd727.
Base: b6fdedc13fc46baddc22206c5c34003a93d3cd5b.
Latest tested implementation: 3a2cb10fc293799a50e84876173aa99b9f0a9099.
The report publication is a subsequent Markdown-only commit, not a new tested-source claim.
Plan: docs/superpowers/plans/2026-10-15-card-1289-admission.md.
Mutation candidate: docs/superpowers/mutations/2026-10-15-card-1289-admission-candidate.md.
Restart: server after landing. Runner defaults and session-runners routes were read; execution used the assigned Linux checkout without a placement pin.

## Implementation

AgentTaskService compares status, archive, board/identifier/workflow binding, platform and verification policy instead of the all-write card token. Title, description, owner and token-only writes can now admit ordinary Code. The closed-card role list is unchanged; CARD-1288 remains separate.

Ordinary advisory worktree preparation runs before the card transaction. A read-only capacity preview preserves refusal before any Git inspection; the existing locked capacity check still governs insertion. SourceLanding retains custody-before-capacity ordering. Handoff preparation remains after existing-child recovery, preserving retry without Git inspection. Child insertion and handoff linkage still use one transaction. Card/action/host/concurrency lock order remains unchanged.

New tests cover eight concurrent-edit variants, two Git/lock variants and SQL order in observation, refusal and child writers. The existing token-only admission-fence refusal now uses an actual state change; the new token-only positive case specifies the intended replacement. No assertions or test timeouts were loosened. Card item (d), database-level immutability enforcement, was outside the Code brief.

## Outcomes and remaining work

| ID | Actual outcome |
|---|---|
| V-1 | 8/8 concurrent-edit cases pass at latest tested source. Four unrelated-edit cases failed against unchanged production before the fix. |
| V-2 | 2/2 real Git/row-lock cases pass with the actual open gate registered. Ordinary case failed on held card lock before the fix. |
| R-1 | CardLockOrder passes for observation writer, refusal writer and child admission. |
| R-2 | Full PipelineSpeedIntegrationTests observed 35/35 at dc4e7eb590d176e694ec3d25421fb3c3a3020f05, including every S0/S6a method; its run was stopped for repair and lacks a terminal run receipt. Final qualification pending. |
| R-3 | 160/160 across all seven full existing classes pass at latest source. Earlier 159/160 exposed V17 concurrency refusal priority; the repair restored it. All six V17 variants pass. |
| R-4 | Application Unit observed 2938 passed, 27 skipped at dc4e7eb590d176e694ec3d25421fb3c3a3020f05 in the stopped run. Latest CP-5/CP-6 hit the total cap without final TRX; CP-7 did not run. Incomplete. |

Deferred to Final qualification: R-2 and R-4; checkpoint rows CP-2 and CP-4 need valid current-source qualification, and CP-5, CP-6, CP-7 remain unfinished. Never credit these as Final passes. No remaining behavior failure was observed in the latest completed selections. No inherited-failure claim is made.

Existing-class roster: AgentTaskCardBindingTests 19; AgentTaskConcurrencyLimitTests 25; AgentTaskScopeOverlapCreateTests 6; AgentTaskWorktreeBaseCreateTests 23; AgentTaskWorktreeBaseResolverTests 67; DispatchConcurrencyAdmissionTests 8; PipelineHandoffPilotTests 12. Fresh TRX definitions and outcomes were inspected for every intended class and method, including all 19 distinct PipelineSpeedIntegrationTests methods and their 35 expanded cases.

The brief's no-Unit direction conflicts with the higher-priority Final requirement. Unit was retained and partitioned by the complete source namespace census after S0's documented single-row timeout. No full assembly run was performed. Total caps were shortened to respect the commissioning budget, never widened.

## Measurements

Single instrumented samples, not throughput benchmarks; startup/warmth and fixture overhead differ. Before: ordinary lock 722.0 ms, 7 Git calls totaling 34.2 ms, locked during Git; handoff lock 776.5 ms, 7 calls totaling 17.7 ms. Latest gate-enabled sample: ordinary lock 33.9 ms, create 222.1 ms, 7 Git calls totaling 21.4 ms, unlocked during every probe. Handoff lock 619.0 ms, create 939.3 ms, 7 calls totaling 21.4 ms, locked during Git. Its linked-child retry performed zero further Git calls. Handoff's remaining lock hold is disclosed: moving preparation before recovery would change idempotent recovery/refusal order and requires a separate two-phase design.

## History, costs and provenance

Two initial CP-1 builds failed on new assertion-overload mistakes, with zero tests; they are not red proofs. The first executable run had four intended admission failures plus two invalid Git probes (the resolver uses RunAsync directly). Correcting the probe produced the intended five red cases: four unrelated edits and ordinary Git under the card lock. No deliberate mutants were run.

The first fixed run passed CP-1, CP-2 and CP-4 but failed one CP-3 concurrency-priority case. It was explicitly stopped before editing source; wait returned exit 6 with phase=stopped/no terminal report. Its unedited per-row state lines and fresh TRX are preserved below as observations, not Final receipts. The latest run terminated normally at its configured total cap (exit 5), preserving clean verified CP-1/CP-3 row evidence. Generated JSON/TRX/logs remain ignored in their original checkpoint directories.

Only unlisted build: checkpoint-tool bootstrap via scripts/build-slot.ps1, needed because no tool binary existed. It succeeded with two warnings, slot=granted waited=0s, held=12s. All test/build drivers otherwise used the checkpoint tool and its build slots. There were no loaded repetitions or unchanged green repeats; reruns followed compile, instrumentation or implementation changes. All source slices were committed and pushed before their runs.

Evidence-history guard passed over base..3a2cb10fc293799a50e84876173aa99b9f0a9099 (6 commits, zero violations); it is rerun over the report publication. Fetch plus merge-tree at this code tip was conflict-free against origin/master f6310a452a911b45b0a0946feb9ac49dcc71bbfd (tree 3e90954dd5a42c986f6aee53e6a7f2604788d654). Final publication SHA and final guard result are in the caller report.

## Unedited checkpoint records

### 20261015-180739-3206

--- checkpoint report ---
run: 20261015-180739-3206   manifest: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-180739-3206/manifest.resolved.yaml
commit: b21794ee50df9897ce60c1b6b695d66315767e2e  branch: feat/card-task-5a9cd727  worktree: /work/worktrees/task-5a9cd727  host: Debian GNU/Linux 12 (bookworm) cores=24
source: b21794ee50df9897ce60c1b6b695d66315767e2e state=clean buildSource=unknown
CHECKPOINT CP-1 commit=b21794ee50df9897ce60c1b6b695d66315767e2e build=failed filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=b21794ee50df9897ce60c1b6b695d66315767e2e sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 3m21s  sequential-equivalent: 0m00s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1289-red/
evidence: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-180739-3206/report.md
verdict: RED exit=2


### 20261015-181202-964d

--- checkpoint report ---
run: 20261015-181202-964d   manifest: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-181202-964d/manifest.resolved.yaml
commit: 1caf47ad66e3c0bbbe7ed2c1633ced8e0de75465  branch: feat/card-task-5a9cd727  worktree: /work/worktrees/task-5a9cd727  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 1caf47ad66e3c0bbbe7ed2c1633ced8e0de75465 state=clean buildSource=unknown
CHECKPOINT CP-1 commit=1caf47ad66e3c0bbbe7ed2c1633ced8e0de75465 build=failed filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=1caf47ad66e3c0bbbe7ed2c1633ced8e0de75465 sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 4m59s  sequential-equivalent: 0m00s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1289-red/
evidence: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-181202-964d/report.md
verdict: RED exit=2


### 20261015-181744-4a0a

--- checkpoint report ---
run: 20261015-181744-4a0a   manifest: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-181744-4a0a/manifest.resolved.yaml
commit: d0912598a70468bef758ce300974894ce53809d5  branch: feat/card-task-5a9cd727  worktree: /work/worktrees/task-5a9cd727  host: Debian GNU/Linux 12 (bookworm) cores=24
source: d0912598a70468bef758ce300974894ce53809d5 state=clean buildSource=verified
CHECKPOINT CP-1 commit=d0912598a70468bef758ce300974894ce53809d5 build=ok filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=11 passed=5 failed=6 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-181744-4a0a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=d0912598a70468bef758ce300974894ce53809d5 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=360.9255495s startup=54.4105564s testsWall=13.4171135s teardown=5.5842078s hostWall=73.4118547s
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_GitLockDuration (CP-1) ShouldAssertException: git.Calls     should be greater than 0     but was 0  Additional Info:     real Git preview was reached -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_GitLockDuration (CP-1) ShouldAssertException: git.Calls     should be greater than 0     but was 0  Additional Info:     real Git preview was reached -> rows/CP-1/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 7m29s  sequential-equivalent: 1m14s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1289-red/
evidence: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-181744-4a0a/report.md
verdict: RED exit=1


### 20261015-182635-b991

--- checkpoint report ---
run: 20261015-182635-b991   manifest: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-182635-b991/manifest.resolved.yaml
commit: 9a446a829a2659657b90aa0654927035d6c5eb7d  branch: feat/card-task-5a9cd727  worktree: /work/worktrees/task-5a9cd727  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9a446a829a2659657b90aa0654927035d6c5eb7d state=clean buildSource=verified
CHECKPOINT CP-1 commit=9a446a829a2659657b90aa0654927035d6c5eb7d build=ok filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=11 passed=6 failed=5 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-182635-b991/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9a446a829a2659657b90aa0654927035d6c5eb7d sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=349.3617009s startup=39.3357066s testsWall=13.2889476s teardown=1.3312914s hostWall=53.9561774s
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_ConcurrentCardEdit (CP-1) ConflictException: The card changed during admission; request a fresh decision. -> rows/CP-1/failures.md
FAILED Antiphon.Tests.Application.PipelineSpeedIntegrationTests.C1289_GitLockDuration (CP-1) ShouldAssertException: git.CardLocked     should be False     but was True  Additional Info:     ordinary Git preview precedes the card lock; handoff keeps idempotent recovery first -> rows/CP-1/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 6m56s  sequential-equivalent: 0m55s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1289-red/
evidence: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-182635-b991/report.md
verdict: RED exit=1


### 20261015-183527-6dec

Stopped run: no terminal receipt.
CHECKPOINT CP-1 commit=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 build=ok filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-183527-6dec/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 build=reused filter=/*/*/PipelineSpeedIntegrationTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-183527-6dec/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 build=reused filter=/*/*/(AgentTaskCardBindingTests*)|(AgentTaskConcurrencyLimitTests*)|(AgentTaskScopeOverlapCreateTests*)|(DispatchConcurrencyAdmissionTests*)|(AgentTaskWorktreeBaseCreateTests*)|(AgentTaskWorktreeBaseResolverTests*)|(PipelineHandoffPilotTests*)/* executed=160 passed=159 failed=1 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-183527-6dec/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 build=reused filter=/*/Antiphon.Tests.Application/*/*[Category=Unit] executed=2938 passed=2938 failed=0 skipped=27 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-183527-6dec/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=dc4e7eb590d176e694ec3d25421fb3c3a3020f05 sourceState=clean buildSource=verified

### 20261015-184739-5977

--- checkpoint report ---
run: 20261015-184739-5977   manifest: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-184739-5977/manifest.resolved.yaml
commit: 3a2cb10fc293799a50e84876173aa99b9f0a9099  branch: feat/card-task-5a9cd727  worktree: /work/worktrees/task-5a9cd727  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3a2cb10fc293799a50e84876173aa99b9f0a9099 state=clean buildSource=unknown
CHECKPOINT CP-1 commit=3a2cb10fc293799a50e84876173aa99b9f0a9099 build=ok filter=/*/*/PipelineSpeedIntegrationTests/C1289_* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-184739-5977/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3a2cb10fc293799a50e84876173aa99b9f0a9099 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=266.6687095s startup=47.7542888s testsWall=13.7742997s teardown=4.8982387s hostWall=66.4268546s
CHECKPOINT CP-3 commit=3a2cb10fc293799a50e84876173aa99b9f0a9099 build=reused filter=/*/*/(AgentTaskCardBindingTests*)|(AgentTaskConcurrencyLimitTests*)|(AgentTaskScopeOverlapCreateTests*)|(DispatchConcurrencyAdmissionTests*)|(AgentTaskWorktreeBaseCreateTests*)|(AgentTaskWorktreeBaseResolverTests*)|(PipelineHandoffPilotTests*)/* executed=160 passed=160 failed=0 skipped=0 trx=/work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-184739-5977/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=3a2cb10fc293799a50e84876173aa99b9f0a9099 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=46.7372042s testsWall=127.3400414s teardown=2.4339829s hostWall=176.5112199s
SLOW CLASS Antiphon.Tests.Application.AgentTaskConcurrencyLimitTests 76s tests=25 (CP-3)
SLOW CLASS Antiphon.Tests.Application.PipelineHandoffPilotTests 74s tests=12 (CP-3)
SLOW CLASS Antiphon.Tests.Application.AgentTaskCardBindingTests 61s tests=19 (CP-3)
CHECKPOINT CP-5 commit=3a2cb10fc293799a50e84876173aa99b9f0a9099 build=n/a filter=/*/Antiphon.Tests.Checkpoints/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-7 commit=3a2cb10fc293799a50e84876173aa99b9f0a9099 build=n/a filter=/*/(Antiphon.Tests)|(Antiphon.Tests.AgentTui*)|(Antiphon.Tests.Agents*)|(Antiphon.Tests.ApiKeys*)|(Antiphon.Tests.Domain*)|(Antiphon.Tests.Infrastructure*)|(Antiphon.Tests.Migrations*)|(Antiphon.Tests.TestHelpers*)/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-6 commit=3a2cb10fc293799a50e84876173aa99b9f0a9099 build=n/a filter=/*/Antiphon.Tests.Scripts/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 10m01s  sequential-equivalent: 4m04s  builds: 1  max-concurrent-builds: 1  rows: 2 green 2 red 1 skipped
refused: still-present
outputs: refused/kept bin-c1289-repair/
evidence: /work/worktrees/task-5a9cd727/.antiphon/checkpoints/20261015-184739-5977/report.md
verdict: RED exit=5


## Pending Mutation inventory

# CARD-1289 unbound Mutation candidate

Original Code/landing owner: 5a9cd727-c32f-4f59-96ac-40b5a91c70b2.
Plan: docs/superpowers/plans/2026-10-15-card-1289-admission.md.
All controls remain pending for method-scoped SourceLanding Mutation after
ordinary Review and landing of the original Code task. No deliberate mutant was
run by Code. Bind the landed source and an external evidence root before use.

| PC | Method (PipelineSpeedIntegrationTests) | Pending variants |
|---|---|---|
| PC-1 | C1289_ConcurrentCardEdit | restore all-write token comparison: title, description, owner, token |
| PC-2 | C1289_ConcurrentCardEdit | omit state comparison; omit archive refusal; omit platform comparison; omit verification-policy comparison |
| PC-3 | C1289_GitLockDuration | move ordinary Git preview back under the card lock |
| PC-4 | C1289_GitLockDuration | inspect Git before returning an already linked handoff child |
| PC-5 | C1289_CardLockOrder | omit/reorder card lock in observation writer; refusal writer; child admission; swap host/concurrency locks |

Use exact method filters, e.g. /*/*/PipelineSpeedIntegrationTests/C1289_CardLockOrder.
Parameterized methods include their variants; do not substitute the whole class.
Mutation owns red/restore/green and missing-control discovery, including additional
board/identifier/workflow binding controls and other card-lock writers. Existing
CARD-1281 S0/S6a controls remain pending under their own inventories.

PC-6 pending: remove the early capacity preview; guard is
AgentTaskWorktreeBaseCreateTests.T0442_V17_existing_create_guards_precede_source_selection
(concurrency variant). Remove the locked recheck separately and use the existing
AgentTaskConcurrencyLimitTests racing-create control; Mutation must select its
exact method before commissioning. No Code mutant or claimed control green.


## Pushed implementation/test slices

b21794ee50df9897ce60c1b6b695d66315767e2e test(card-1289): expose unrelated card edit refusal and Git lock hold
1caf47ad66e3c0bbbe7ed2c1633ced8e0de75465 test(card-1289): name collection assertion diagnostics
d0912598a70468bef758ce300974894ce53809d5 test(card-1289): assert exact ordered lock sequence as text
9a446a829a2659657b90aa0654927035d6c5eb7d test(card-1289): observe resolver Git subprocess seam
dc4e7eb590d176e694ec3d25421fb3c3a3020f05 fix(card-1289): fence admission state and prepare ordinary Git before card lock
3a2cb10fc293799a50e84876173aa99b9f0a9099 fix(card-1289): preserve capacity refusal before Git preparation
