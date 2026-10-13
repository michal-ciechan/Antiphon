# CARD-1032 S3 round-2 Code repair evidence

F1 and F2 repaired. Required ordinary Final verification passed **97/97**, zero
failures/skips. Both required held-lease regressions first failed against
unchanged production from the reviewed base. CompletedCard cleanup stays inert.
Next: **Review**. Post-land Mutation was **not run**.

Code task / landing owner: `f6e00270-2149-45b2-b065-5c0ef10d1f17`.
Prior Code owner: `c533d6c6-3a11-4b45-b851-f27c39ed0b92`.
Branch: `feat/card-task-f6e00270`.
Runner worktree: `/work/worktrees/task-f6e00270`.
Assigned desktop checkout (unreachable here): `C:\Antiphon\worktrees\card-task-f6e00270`.
Task base: `91974075f4a04a10ea98d9e06e4e599a10b29c3b`.
Actual final tested SHA: `859171fc1da1792d46d823054ada13e4603994ac`.
The subsequent report-only commit is identified by the final progress marker.
This file changes no tested production/test implementation.

## Artifacts and source

- Plan: `docs/superpowers/plans/2026-10-11-card-1032-runner-mirror-cleanup-plan.md`.
- Authoritative checkpoint/test design: `docs/superpowers/plans/2026-10-12-card-1032-test-design.md`.
- Unbound Mutation candidates: `docs/superpowers/mutations/2026-10-13-card-1032-s3-removal-repair-candidates.md`.
- Prior evidence: `docs/superpowers/reviews/2026-10-13-card-1032-s3-code-evidence.md`.
- Required brief-sized report copy: `/work/worktrees/task-f6e00270/.antiphon/task-f6e00270.md`.

Committed/pushed meaningful slices, in fast-forward order:

| SHA | Outcome |
|---|---|
| `65bc23ce4228e24e2021ba1e0dc06832f6d1a83a` | Tests/plan first; production unchanged from base; CP-25 red 0/2 |
| `9ea8721b0a863e072cbfe8c0932ed876f7d569c5` | Optional removal/verification gate repair; verification pending |
| `859171fc1da1792d46d823054ada13e4603994ac` | Decisive Git-start and exceptional-release witnesses; final 97/97 green |

No rebase, amend, reset, force-push, landing or deployment was performed.
Fetched `origin/master=e19186dc8e156216a2875c8c87237b94b7a857f8`;
`git merge-tree --write-tree origin/master HEAD` at the tested SHA exited 0,
tree `5d8d0cb581876e8ba7d496ce4a0787563d459d52`.
The final report-only HEAD is also checked by merge-tree and the full task
base..HEAD evidence guard before settlement; results are in the final message.
The tested-range evidence guard exited 0: three commits, zero entries/violations.

## Repair and removal-path audit

`AcquireRemovalAsync` takes the shared runner admission without waiting and
returns `workspace_in_use` for competing admission or retained ownership.
It holds the lease through Git removal and pruning. It also honors unavailable
census, unknown/torn journals and existing retirement fences. A successful
acquisition uses await-using; validation refusal releases in catch. Exceptions,
cancellation, ordinary refusal responses and success therefore release the
admission. There is no await inside a C# lock.

Documented lock order: dispatcher MutationLock, when present, then workspace
admission, then journal/binding registration store and session/custody/pane
operations. No admitted callback reacquires the dispatcher mutation lock.
The private pruning helper uses the outer lease and never reacquires it.
Repository-wide prune checks owners and retirement intents across the worktree
root; the existing runner-wide lane may conservatively refuse unrelated active
admission. An unconfigured gate follows the original method bodies unchanged.

Legacy remove requests have no immutable CompletedCard command/binding/proof
identity. The existing journal contract records typed cleanup intent only in
TryRetireAsync before its callback. This repair honors that journal and does
not fabricate cleanup authority for a legacy Path/Force request.

| Audited path | Shared gate taken / conclusion |
|---|---|
| RunnerWorkspaceService.RemoveAsync, present path: status/proof, owning repository, git worktree remove --force, prune | AcquireRemovalAsync before Directory.Exists, through every exit and both mutations |
| RemoveAsync, absent path -> PruneRepositoriesAsync, primary/secondary repositories | Same outer removal lease; competing use refuses before any Git |
| RunnerVerificationWorkspace.RemoveVerificationAsync, present path: expected-output Directory.Delete/File.Delete, Git remove/prune, branch and metadata removal | Same optional removal lease before all reads/mutations; additional bypass fixed |
| RemoveVerificationAsync, absent path: Git prune, branch and metadata removal | Same outer lease; retained use refuses before Git |
| MirrorAsync -> bound/unbound create and replay | Existing AcquireUseAsync retained; no S2 body changes |
| CreateVerificationAsync -> creation/replay | AcquireUseAsync added before metadata/Git, required to serialize with the newly guarded verification removal |
| RunnerWorkspaceUseGate.TryRetireAsync callback | Shared admission; journal.Record precedes callback. No physical remover or production caller added |
| PhoneHomeCommandDispatcher WorkspaceRemove / VerificationWorkspaceRemove | Existing MutationLock precedes the service's optional workspace admission; no reverse acquisition |
| CodexCliVersionProbe.TryDeleteScratch | No workspace gate; deletes its own separately named temp probe root, not a mirror/worktree |
| GrokRulesFileStore.Expire | No workspace gate; explicit stopped-session artifact expiry under launch lock; only rules files and artifact leaf directory |
| HerdrPaneSidecar.Retire / label-retirement callbacks; AgentPinWorkspacePublisher.RemoveEmptyLeaf | Metadata/artifact leaf operations; no mirror/worktree removal |
| Binding/removal journal temporary-file cleanup | External store-owned atomic-write temporary files, not a worktree deletion |
| Git clean | No callers found in src/Antiphon.SessionRunner |

The audit used Directory.Delete, git worktree remove/prune, git clean,
RemoveAsync and retirement-callback searches across the runner source.
No remaining mirror/worktree removal bypass was found in that scope.
Verification file changes are the narrow additional dependency needed to close
the second deletion bypass and its competing creation path.

F2's comment now correctly describes existing-binding replay in both orders;
the original CP-5 names/count remain unchanged. New repair tests use actual
scratch Git creation/removal, deterministic admission barriers and Git-entry
observations. Creation-first removal refuses; removal-first creation starts no
Git until removal/prune release, then creates the real tree at the expected HEAD.
Post-exception/cancellation removal succeeds, and a leaked lease fails immediately.
Retained-owner tests also cover pruning an absent sibling under the same root.

## Final scope and outcomes

The repair brief explicitly says **NO Unit lane**, overriding its generic Final
profile paragraph. Final scope here is the complete specified S2 regression
group, CP-5 three times, and CP-25..27. CP-26/27 cover the complete newly affected
removal class and workspace service class. No full assembly was selected; no
unbounded full-assembly cost was incurred. No assigned ID is deferred to Final.

| ID | Actual outcome in this repair |
|---|---|
| V-1 | Passed all 33 assigned S2 creation/capture/store/join/compatibility cases |
| V-4 | Passed six CP-5 methods three times and all new removal/use witnesses |
| R-1 | Passed full 33-result RunnerWorkspaceServiceTests and three Workspace dispatcher cases, plus repair compatibility witnesses |
| R-2 | Passed S2 explicit legacy-unbound and dispatcher compatibility rows; unconfigured present/absent removal and verification legacy class green |
| V-2 | Out of this S3 repair; wire/guarded transport acceptance remains assigned to other slices; no new pass claimed |
| V-3 | Out of this repair; guarded source/identity/content inspection belongs to S4/S5 |
| V-5 | Out of this repair; new cleanup execution/removal-replay belongs to S5; existing legacy scratch removal is not claimed as that acceptance |
| V-6 | Out of this repair; server mirror coordinator/receipt recovery belongs to S7/S8 |
| R-3 | Out of this repair; destructive parent witnesses and native activation qualification remain pending in their assigned slices |

| CP | Final counts passed/executed |
|---|---|
| CP-3 | 5/5 |
| CP-4 | 4/4 |
| CP-5 | 18/18 = 6/6 for each of three repetitions |
| CP-17, CP-18, CP-19, CP-20 | 1/1 each |
| CP-21 | 6/6 |
| CP-22 | 10/10 |
| CP-23 | 1/1 |
| CP-24 | 3/3 |
| CP-25 | 2/2 |
| CP-26 | 11/11 |
| CP-27 | 33/33 |
| Total | 97/97; failed=0; skipped=0 |

Fresh TRX class/method rosters were inspected for every row, including all ten
registration variants, both verification-removal arguments, all 33 workspace
service methods, and each of CP-5's six methods exactly three times. Internal
loops do not inflate counts. All final receipts show dirty=0, sourceState=clean
and buildSource=verified at the exact actual tested SHA.

Receipt validation exited 0 for all three final runs:

- `.antiphon/checkpoints/20261013-012156-2cd5/report.json`: three rows, repair.
- `.antiphon/checkpoints/20261013-012422-ef93/report.json`: CP-5, expected-repeat=3.
- `.antiphon/checkpoints/20261013-012601-88d3/report.json`: ten S2 rows.

## Negative proof and run history

The first CP-25 run tested `65bc23ce4228e24e2021ba1e0dc06832f6d1a83a`, a
test-only descendant of `91974075f`. `git diff base..test-only -- src` is empty:
the production body is exactly the reported defective base, not a deliberate
mutant. It executed two tests, zero passed, two failed, zero skipped. Both fresh
TRX results failed on the intended missing-refusal assertion:

```text
FAILED Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyRemoveHeldLease (CP-25) ShouldAssertException: Task `f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None)`     should throw Antiphon.SessionRunner.PhoneHomeAdmissionException     but did not -> rows/CP-25/failures.md
FAILED Antiphon.SessionRunner.Tests.LegacyWorkspaceRemovalGateTests.C1032_LegacyPruneHeldLease (CP-25) ShouldAssertException: Task `f.Workspace().RemoveAsync(new(f.Checkout, true), CancellationToken.None)`     should throw Antiphon.SessionRunner.PhoneHomeAdmissionException     but did not -> rows/CP-25/failures.md
```

The first fixed run, `20261013-011756-0ae4` at `9ea8721b0`, was deliberately
stopped before editing source to strengthen two witnesses. CP-25 had passed
2/2; CP-26 has no completed TRX and CP-27 did not run. Stop exited 0; the next
wait exited 6 (`executor died without phase=done`), because this was a stopped
executor. No acceptance is claimed for that incomplete run. Its owned outputs
were cleaned. The stronger tests were committed/pushed before rerunning all
repair rows at `859171fc1`; that completed run passed 46/46.

Final repair, repeated CP-5 and S2 wall times were respectively 1m56s, 1m07s
and 8m33s. The initial negative proof took 6m42s, including 270s slot wait.
CP-5 used exactly the required three normal repetitions, no loaded repetitions
and no repetitions after green. There was no flake rerun, widened timeout,
weakened assertion or inherited failure attributed to this repair.

## Builds, platform, manual work and rerun commands

All selected project builds/tests used the checkpoint tool and host slots.
Final runs used four actual isolated project builds: repair, repeated fence,
S2 runner and S2 server. S2 reuse is declared in the same After/project/SHA
group, with every original exact filter/count retained. Configured but unused
manifest builds were not run. All driver slots were granted; slot/wait fields
and build provenance follow below. OutputPath values use trailing forward
slashes and UseAppHost=false. No build ID is named tests.

The only unlisted build was the required checkpoint-tool bootstrap, at test-only
SHA `65bc23ce4228e24e2021ba1e0dc06832f6d1a83a`:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1032-repair-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1032-repair-tool/ --property:UseAppHost=false -nodeReuse:false
BUILD SLOT granted lease=ae969014-46a7-4f91-b149-997d44e24c87 waited=0s maxcpucount=6
BUILD SLOT released lease=ae969014-46a7-4f91-b149-997d44e24c87 held=29s
```

Bootstrap passed, zero errors, one existing TaskOwnerGuard nullable warning.
It released its lease before invoking the built DLL. Read-only tool wait,
validate, status/cleanup and evidence/merge commands are not build/test drivers.

Rerun after bootstrap by invoking the tool DLL with `run --plan
docs/superpowers/plans/2026-10-12-card-1032-test-design.md --expected-source-sha
<committed-HEAD>`, with these selections in separate awaited groups:
`--rows CP-25,CP-26,CP-27`; `--rows CP-5 --repeat 3`; and
`--rows CP-3,CP-4,CP-17,CP-18,CP-19,CP-20,CP-21,CP-22,CP-23,CP-24`.
Use `wait --run <id>` until the result is not 75. No unleased retry is allowed.

GET /api/runner-defaults and /api/session-runners succeeded; eligible Linux and
Windows lanes were observed, without embedding a host/platform pin. Card and
prior Final Review were read. Manual inspection confirmed unchanged server and
runner Program, the complete server tree/residue composition and Execute
configuration, and no production caller for the new typed retirement callback.
No real mirror/worktree/process was removed; destructive tests used only their
owned scratch roots. No live messaging, service restart or deployment occurred.
Restart: **none**. Parent native qualification/activation remains outside this
inert S3 repair.

Checkpoint-owned green outputs were automatically cleaned; negative and stopped
run outputs were cleaned with the tool. The separately owned bootstrap output
is removed before settlement. TRX/receipts/logs remain ignored in their original
checkpoint directories. Only this individual Markdown summary is committed;
no generated payload is relocated or force-added.

## Mutation handoff

All original **292 PC IDs**, including every named variant and the prior
`PC-224/cwd-changed` vector, plus **PC-551..560** and their named variants remain
**pending**. New IDs include separate exception/cancellation, present/absent
verification removal, same-path/absent-sibling retained ownership, both race
orders, and retiring/torn/incomplete-census holds. They are unbound candidates;
Mutation owns deliberate mutants, exact method-scoped red/restore/green and
missing-control discovery. No PC is discharged by ordinary green or by the
pre-fix negative proof. Caller obtains Review, lands this Code owner, then
commissions SourceLanding Mutation on the published source.

The appended inventory explicitly reports every PC ID and its source-defined
variants pending. No control was run in this Code task.

## Unedited checkpoint lines and build provenance


### Pre-fix negative proof

CHECKPOINT CP-25 commit=65bc23ce4228e24e2021ba1e0dc06832f6d1a83a build=ok filter=/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)|(C1032_LegacyPruneHeldLease*) executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-010848-b616/rows/CP-25/run.trx slot=granted waited=135s dirty=0 source=65bc23ce4228e24e2021ba1e0dc06832f6d1a83a sourceState=clean buildSource=verified

Build bin-c1032-removal-repair: state=ok, slot=granted, waited=135s, seconds=120.6666241.

### Final repair

CHECKPOINT CP-25 commit=859171fc1da1792d46d823054ada13e4603994ac build=ok filter=/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)|(C1032_LegacyPruneHeldLease*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012156-2cd5/rows/CP-25/run.trx slot=granted waited=30s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-26 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/LegacyWorkspaceRemovalGateTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012156-2cd5/rows/CP-26/run.trx slot=granted waited=15s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-27 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceServiceTests/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012156-2cd5/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

Build bin-c1032-removal-repair: state=ok, slot=granted, waited=30s, seconds=23.9573462.

### Final repeated CP-5

CHECKPOINT CP-5 commit=859171fc1da1792d46d823054ada13e4603994ac build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_Fence* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012422-ef93/rows/CP-5/run.trx slot=granted waited=0s repeat=3 repetitions=3/3 hostInvocations=1 dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

Build bin-c1032-fence: state=ok, slot=granted, waited=0s, seconds=55.2028847.

### Final S2

CHECKPOINT CP-3 commit=859171fc1da1792d46d823054ada13e4603994ac build=ok filter=/*/*/RunnerWorkspaceBindingTests/C1032_Binding* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-4 commit=859171fc1da1792d46d823054ada13e4603994ac build=ok filter=/*/*/(CardWorktreeMirrorBindingTests*)|(RemoteWorkspacePreparerTests*)/C1032_Capture* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-17 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-17/run.trx slot=granted waited=15s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-18 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-19 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatch* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-20 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacement* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-20/run.trx slot=granted waited=30s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-21 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/PhoneHomeMirrorJoinTests/C1032_Join* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-21/run.trx slot=granted waited=60s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-22 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentity* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-22/run.trx slot=granted waited=15s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-23 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-23/run.trx slot=granted waited=15s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

CHECKPOINT CP-24 commit=859171fc1da1792d46d823054ada13e4603994ac build=reused filter=/*/*/PhoneHomeCommandDispatcherTests/Workspace_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-012601-88d3/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=859171fc1da1792d46d823054ada13e4603994ac sourceState=clean buildSource=verified

Build bin-c1032-binding: state=ok, slot=granted, waited=0s, seconds=24.5961569.
Build bin-c1032-capture: state=ok, slot=granted, waited=0s, seconds=114.8793836.

### Stopped run, no completed-run acceptance

CHECKPOINT CP-25 commit=9ea8721b0a863e072cbfe8c0932ed876f7d569c5 build=ok filter=/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)|(C1032_LegacyPruneHeldLease*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f6e00270/.antiphon/checkpoints/20261013-011756-0ae4/rows/CP-25/run.trx slot=granted waited=15s dirty=0 source=9ea8721b0a863e072cbfe8c0932ed876f7d569c5 sourceState=clean buildSource=verified

Build bin-c1032-removal-repair: slot=granted waited=90s; CP-26 has no completed TRX; CP-27 not run. Stop=0, subsequent wait=6.

## Pending PC inventory

| PC / variant | Outcome | Source-defined fault / variants |
|---|---|---|
| PC-52 | pending | eligible command through dispatcher -> break G-52: route it to legacy RemoveAsync. |
| PC-53 | pending | different full TaskId with equal short id and valid path -> break G-53: omit TaskId comparison. |
| PC-54 | pending | same task and binding coordinates, foreign attempt -> break G-54: omit attempt comparison. |
| PC-55 | pending | new store at same path, otherwise matching journal -> break G-55: omit RunnerStoreId comparison. |
| PC-56 | pending | real foreign repository with the same commit -> break G-56: omit repository identity comparison. |
| PC-57 | pending | valid second task tree, sibling-prefix and outside-root variants -> break G-57: omit exact recorded-path comparison. |
| PC-58 | pending | different symbolic branch at equal SHA; detached variant -> break G-58: omit symbolic-ref comparison. |
| PC-59 | pending | unique committed HEAD changed after request -> break G-59: omit expected HEAD comparison. |
| PC-60 | pending | bare SHA with no proof; exact contained and bound-rebase positive variants -> break G-60: accept missing preservation proof. |
| PC-61 | pending | staged and unstaged tracked edits; separate untracked variant -> break G-61: accept nonempty tracked status. |
| PC-62 | pending | bin/.env fixture overlaps disposable rule -> break G-62: remove protected-name precedence. |
| PC-63 | pending | swap enumerated directory for outside link after final inspection -> break G-63: enumerate through the substituted link. |
| PC-64 | pending | terminal session row with observed live owned child -> break G-64: use terminal status as sufficient no-owner proof. |
| PC-65 | pending | intent-first and start-first with different session IDs at same workspace -> break G-65: skip workspace fence in StartAsync. |
| PC-66 | pending | restart at durable intent, then attempt admission through adoption -> break G-66: skip restored workspace fence on pty adoption. |
| PC-67 | pending | pause before first physical rename and read journal independently -> break G-67: move intent persistence after first mutation. |
| PC-68 | pending | same ID with changed valid digest versus byte-identical replay -> break G-68: return saved success for changed digest. |
| PC-69 | pending | late tracked file after successful first read -> break G-69: omit final tracked-source validation. |
| PC-70 | pending | raw force=true with otherwise valid request and dirty source -> break G-70: add compiling raw-JSON force arm routing to forced legacy removal. |
| PC-71 | pending | local Complete and mirror Pending, fresh coordinator -> break G-71: filter out targets with completed local endpoint. |
| PC-72 | pending | offline mirror with eligible local tree -> break G-72: require online mirror before local execution. |
| PC-73 | pending | real H ancestor of P, and changed-H refusal -> break G-73: require H==P despite exact H containment proof. |
| PC-74 | pending | valid foreign-operation receipt, other fields equal -> break G-74: omit receipt operation-ID comparison. |
| PC-75 | pending | valid receipt with only endpoint changed -> break G-75: omit endpoint-ID comparison. |
| PC-76 | pending | valid receipt from replacement store -> break G-76: omit store-ID comparison. |
| PC-77 | pending | valid receipt with only attempt changed -> break G-77: omit attempt comparison. |
| PC-78 | pending | ack, empty payload, null path, timeout and component-unknown variants -> break G-78: mark Complete on successful RPC return. |
| PC-79 | pending | missing capability or UnsupportedOperation response -> break G-79: retry using WorkspaceRemove. |
| PC-80 | pending | reopen commits after local result before mirror intent -> break G-80: reuse local generation admission for mirror. |
| PC-81 | pending | two stores, first Complete, second offline -> break G-81: replace normalized bindings with current scalar path. |
| PC-82 | pending | real removal, dropped reply, server restart and reconnect -> break G-82: allocate new operation on retry. |
| PC-108 | pending | genuine verification creation with forged ordinary fields -> break G-108: accept verification creation as ordinary binding. |
| PC-109 | pending | unavailable census and torn owned-child record -> break G-109: treat unknown ownership as empty. |
| PC-200 | pending | concurrent inserts with same PreparationId -> break G-200: drop preparation unique index in mapping and generated migration. |
| PC-201 | pending | same task/attempt/store/binding under different preparation IDs -> break G-201: drop tuple unique index in mapping and generated migration. |
| PC-202 | pending | upgrade predecessor DB with active, Done, canceled and scalar-only mirror rows -> break G-202: add synthetic binding backfill for RemoteWorktreePath. |
| PC-203 | pending | save intent, change task coordinates, reopen DbContext -> break G-203: omit immutable-request property from EF persistence mapping. |
| PC-204 | pending | save typed receipt with component facts then fresh context -> break G-204: omit receipt payload column write. |
| PC-205 | pending | round-trip inspect/remove plus old operation 15 literal -> break G-205: reuse legacy enum value for guarded remove. |
| PC-206 | pending | unknown/zero schema against otherwise valid JSON -> break G-206: accept unknown version as current. |
| PC-207 | pending | equivalent object property order and artifact order normalization -> break G-207: digest uncanonicalized property insertion order. |
| PC-208 | pending | duplicate operationId with two values -> break G-208: accept last duplicate field. |
| PC-209 | pending | different full IDs share task-deadbeef leaf and equal source -> break G-209: lookup binding by short name alone. |
| PC-210 | pending | cut after preparation journal, after worktree add, before completion, after completion -> break G-210: write creation intent only after worktree add. |
| PC-211 | pending | legacy equal-SHA tree exists before incomplete preparation -> break G-211: mark pre-existing tree as completed bound creation. |
| PC-212 | pending | new store object over same state after reply loss -> break G-212: generate new BindingId for exact PreparationId replay. |
| PC-213 | pending | same preparation with changed attempt/branch/baseline -> break G-213: overwrite existing preparation record on mismatch. |
| PC-214 | pending | payload asks state path inside mirror and outside allowed state root -> break G-214: use payload-supplied state root. |
| PC-215 | pending | torn, unreadable or missing record after interrupted write -> break G-215: recover failed record read as a completed binding. |
| PC-216 | pending | old request without binding extension on configured ordinary service -> break G-216: require binding fields for legacy mirror requests. |
| PC-217 | pending | attempt/store A then B; reload with current scalar pointing only at B -> break G-217: upsert binding by TaskId only. |
| PC-218 | pending | hold reply, reassign runner, then return valid binding -> break G-218: return before persisting historical binding when assignment differs. |
| PC-219 | pending | current runner B already has a path when A replies -> break G-219: update scalar path without runner compare. |
| PC-220 | pending | same runner, increment attempt before reply -> break G-220: drop attempt predicate on scalar update. |
| PC-221 | pending | cancel originating attempt before reply -> break G-221: skip binding persistence for terminal task. |
| PC-222 | pending | server dies after request send then preparation rearmed -> break G-222: allocate fresh PreparationId on recovery. |
| PC-223 | pending | different session IDs and normalized aliases of one workspace -> break G-223: key gate by raw path or session ID. |
| PC-224 | pending | real AttachHerdrAsync after intent, then inverse order -> break G-224: omit workspace admission in explicit attach. |
| PC-225 | pending | hold manifest census at restart with retained live child -> break G-225: mark census ready before manifests inspected. |
| PC-226 | pending | live sidecar at fenced path -> break G-226: omit ownership conflict on startup Herdr adoption. |
| PC-227 | pending | unreachable sidecar then reconnect via SweepVanishedSessionsAsync -> RetryPendingHerdrAsync -> break G-227: omit workspace guard on pending retry arm. |
| PC-228 | pending | MirrorAsync competing with retiring gate, both race orders -> break G-228: omit workspace admission from mirror creation. |
| PC-229 | pending | missing/torn/unreadable retirement journal after restart -> break G-229: restore malformed fence as open. |
| PC-230 | pending | hold workspace, observe request at gate before session gate acquired -> break G-230: acquire session lock before workspace lock. |
| PC-231 | pending | live surviving child contradicts persisted retiring intent -> break G-231: call KillPidBestEffort on the cleanup conflict arm. |
| PC-232 | pending | same contradictory manifest/sidecar, fresh runtime observation -> break G-232: discard retained owner before recording conflict. |
| PC-233 | pending | known bound clean mirror inspection -> break G-233: invoke removal/registration cleanup from inspect. |
| PC-234 | pending | unbound valid legacy tree with full caller-supplied identity -> break G-234: create binding while inspecting unknown path. |
| PC-235 | pending | unknown ignored synthetic file -> break G-235: default unknown classification to Disposable. |
| PC-236 | pending | known checkpoint evidence overlaps custom disposable rule -> break G-236: classify Disposable before Evidence. |
| PC-237 | pending | request policy removes .env protection -> break G-237: replace protected minima with request policy. |
| PC-238 | pending | bin/.antiphon/unknown under broad bin rule -> break G-238: apply ancestor disposable glob to .antiphon subtree. |
| PC-239 | pending | H behind P and advanced H2; clean fixture -> break G-239: return request or creation baseline as observed HEAD. |
| PC-240 | pending | foreign real common directory with equal commit -> break G-240: omit common-directory comparison. |
| PC-241 | pending | foreign valid worktree admin directory -> break G-241: omit admin-directory comparison. |
| PC-242 | pending | same task/attempt/path but replaced BindingId -> break G-242: omit BindingId comparison. |
| PC-243 | pending | canonical, reusable slot, shared or borrowed tree with crafted ordinary fields -> break G-243: accept unsupported workspace kind. |
| PC-244 | pending | valid proof for different destination -> break G-244: omit destination-fingerprint comparison. |
| PC-245 | pending | H exists only on unique pushed task branch -> break G-245: use task ref instead of configured destination ref. |
| PC-246 | pending | confirmed unrelated landing with equal resulting tree bytes -> break G-246: accept mapping for other task/source operation. |
| PC-247 | pending | remote target rewound after proof or read fails -> break G-247: trust stale observed target without fresh remote check. |
| PC-248 | pending | nonignored untracked source only -> break G-248: omit untracked inventory from initial status. |
| PC-249 | pending | nested .git repository under otherwise disposable tree -> break G-249: ignore nested repository detection. |
| PC-250 | pending | real git worktree lock on otherwise eligible mirror -> break G-250: ignore locked registration. |
| PC-251 | pending | known evidence with non-CardDone disposition -> break G-251: allow evidence disposal for ordinary/unknown disposition. |
| PC-252 | pending | existing link/dangling link to outside owned sentinel -> break G-252: ignore links in ignored-content inventory. |
| PC-253 | pending | pooled process at mirror with terminal task row -> break G-253: exclude pooled owners from use census. |
| PC-254 | pending | new owner appears after initial ownership read -> break G-254: omit final owner validation. |
| PC-255 | pending | protected ignored file appears after initial inventory -> break G-255: reuse initial ignored inventory at deletion. |
| PC-256 | pending | swap registration/admin identity after initial inspection -> break G-256: reuse initial registration at deletion. |
| PC-257 | pending | remote containment disappears before mutation -> break G-257: omit final preservation validation. |
| PC-258 | pending | real owned clean redundant mirror, CardDone evidence -> break G-258: return success receipt without executing no-follow delete. |
| PC-259 | pending | owned mirror and unrelated stale registration -> break G-259: replace targeted unregister with repository-wide prune. |
| PC-260 | pending | snapshot local and origin refs before successful mirror removal -> break G-260: delete owned task ref after remove. |
| PC-261 | pending | observer paused before rename reads external record -> break G-261: persist set-aside path only after rename. |
| PC-262 | pending | crash after rename, unregister, partial delete, receipt write; each component pair -> break G-262: treat directoryGone alone as completed removal. |
| PC-263 | pending | restart with partial receipt and spent allowance -> break G-263: reset allowance while resuming journal. |
| PC-264 | pending | original absent, no journal or foreign journal -> break G-264: return Complete on Directory.Exists=false. |
| PC-265 | pending | matching set-aside journal plus newly created original path -> break G-265: delete original pathname while resuming set-aside. |
| PC-266 | pending | recorded sibling name occupied by foreign content -> break G-266: delete pre-existing sibling to make room. |
| PC-267 | pending | default runtime/dispatcher with Execute=true equivalent fixture settings -> break G-267: auto-construct guarded remover in dispatcher. |
| PC-268 | pending | capability query and registration on unconfigured then configured runtime -> break G-268: advertise guarded capability unconditionally. |
| PC-269 | pending | one missing binding payload sent via real guarded frame -> break G-269: default malformed guarded body into a valid command. |
| PC-270 | pending | two distinct transport RequestIds carrying same removal operation -> break G-270: return a newly allocated receipt on second transport request. |
| PC-271 | pending | Queued/Working/Blocked with remaining facts valid -> break G-271: omit terminal-task predicate. |
| PC-272 | pending | just below/at configured settling duration using fake clock -> break G-272: omit settling-floor predicate. |
| PC-273 | pending | change attempt or binding after inspection before admission -> break G-273: admit stale captured attempt/binding. |
| PC-274 | pending | open repair/continuation using mirror with terminal owner -> break G-274: ignore active workspace consumer. |
| PC-275 | pending | pending land/repair/recovery request -> break G-275: omit outstanding-recovery predicate. |
| PC-276 | pending | missing/digest-changed canonical Result or path inside deletion root -> break G-276: accept existing report path without verifying external bytes. |
| PC-277 | pending | deliverable only below local/mirror/set-aside root; missing external variant -> break G-277: accept File.Exists without external artifact proof. |
| PC-278 | pending | change Result/deliverable pointers after initial artifact verification -> break G-278: reuse old artifact digest at intent. |
| PC-279 | pending | pause outbound send and observe from independent DB connection -> break G-279: send before intent transaction commits. |
| PC-280 | pending | intent commits, then reopen, then reconnect replay -> break G-280: revoke already-admitted exact mirror on reopen. |
| PC-281 | pending | hold runner inspection/send and execute independent card writer -> break G-281: hold admission row lock across network await. |
| PC-282 | pending | matching IDs with different request digest -> break G-282: omit receipt digest comparison. |
| PC-283 | pending | receipt-save fault before state update -> break G-283: mark Complete in separate earlier save. |
| PC-284 | pending | observer reloads DB when invalidation received -> break G-284: emit invalidation before saving component facts. |
| PC-285 | pending | hold runner mutation turn with owned earlier work, then release; ready variant -> break G-285: drop guarded work queued while mutation turn is busy. |
| PC-286 | pending | pre-send request-limit/write failure and queued disconnect -> break G-286: clear intent/preparation debt when transport did not send. |
| PC-287 | pending | real completed receipt then server save fails and restarts -> break G-287: erase operation identity after save failure. |
| PC-288 | pending | reconnect same runner name with different persistent store -> break G-288: retarget old intent to new store. |
| PC-289 | pending | due-1 tick then due tick after fresh coordinator -> break G-289: ignore persisted NextAttemptAt on retry. |
| PC-290 | pending | real Program fixture with Execute=true and populated eligible endpoints -> break G-290: register coordinator/executor into production services. |
| PC-291 | pending | invoke existing sweep on populated eligible fixture with Execute=true -> break G-291: add mirror cleanup call to WorktreeResidueSweepService. |
| PC-292 | pending | enumerate default endpoint data sources and probe candidate route -> break G-292: map public completed-card mirror cleanup endpoint. |
| PC-293 | pending | synthetic credential URL/process-env/secret-content canaries in failed inputs -> break G-293: log raw guarded payload in refusal detail. |
| PC-300 | pending | required.CardId: remove only CardId from valid bound JSON -> break G-300: remove the CardId required-value validation. |
| PC-301 | pending | required.DoneRevisionId: remove only DoneRevisionId from valid bound JSON -> break G-301: remove the DoneRevisionId required-value validation. |
| PC-302 | pending | required.TargetId: remove only TargetId from valid bound JSON -> break G-302: remove the TargetId required-value validation. |
| PC-303 | pending | required.EndpointId: remove only EndpointId from valid bound JSON -> break G-303: remove the EndpointId required-value validation. |
| PC-304 | pending | required.TaskId: remove only TaskId from valid bound JSON -> break G-304: remove the TaskId required-value validation. |
| PC-305 | pending | required.Attempt: remove only Attempt from valid bound JSON -> break G-305: remove the Attempt required-value validation. |
| PC-306 | pending | required.BindingId: remove only BindingId from valid bound JSON -> break G-306: remove the BindingId required-value validation. |
| PC-307 | pending | required.PreparationId: remove only PreparationId from valid bound JSON -> break G-307: remove the PreparationId required-value validation. |
| PC-308 | pending | required.RunnerId: remove only RunnerId from valid bound JSON -> break G-308: remove the RunnerId required-value validation. |
| PC-309 | pending | required.RunnerStoreId: remove only RunnerStoreId from valid bound JSON -> break G-309: remove the RunnerStoreId required-value validation. |
| PC-310 | pending | required.RepositoryIdentity: remove only RepositoryIdentity from valid bound JSON -> break G-310: remove the RepositoryIdentity required-value validation. |
| PC-311 | pending | required.CommonDirectory: remove only CommonDirectory from valid bound JSON -> break G-311: remove the CommonDirectory required-value validation. |
| PC-312 | pending | required.GitDirectory: remove only GitDirectory from valid bound JSON -> break G-312: remove the GitDirectory required-value validation. |
| PC-313 | pending | required.WorktreePath: remove only WorktreePath from valid bound JSON -> break G-313: remove the WorktreePath required-value validation. |
| PC-314 | pending | required.SourceFullRef: remove only SourceFullRef from valid bound JSON -> break G-314: remove the SourceFullRef required-value validation. |
| PC-315 | pending | required.CreationBaselineSha: remove only CreationBaselineSha from valid bound JSON -> break G-315: remove the CreationBaselineSha required-value validation. |
| PC-316 | pending | required.OperationId: remove only OperationId from valid bound JSON -> break G-316: remove the OperationId required-value validation. |
| PC-317 | pending | required.ExpectedHeadSha: remove only ExpectedHeadSha from valid bound JSON -> break G-317: remove the ExpectedHeadSha required-value validation. |
| PC-318 | pending | required.DestinationFingerprint: remove only DestinationFingerprint from valid bound JSON -> break G-318: remove the DestinationFingerprint required-value validation. |
| PC-319 | pending | required.DestinationRef: remove only DestinationRef from valid bound JSON -> break G-319: remove the DestinationRef required-value validation. |
| PC-320 | pending | required.ObservedTargetSha: remove only ObservedTargetSha from valid bound JSON -> break G-320: remove the ObservedTargetSha required-value validation. |
| PC-321 | pending | required.ProofKind: remove only ProofKind from valid bound JSON -> break G-321: remove the ProofKind required-value validation. |
| PC-322 | pending | required.ProofIdentity: remove only ProofIdentity from valid bound JSON -> break G-322: remove the ProofIdentity required-value validation. |
| PC-323 | pending | required.PreservedSourceSha: remove only PreservedSourceSha from valid bound JSON -> break G-323: remove the PreservedSourceSha required-value validation. |
| PC-324 | pending | required.ReportDigest: remove only ReportDigest from valid bound JSON -> break G-324: remove the ReportDigest required-value validation. |
| PC-325 | pending | required.ArtifactProofDigest: remove only ArtifactProofDigest from valid bound JSON -> break G-325: remove the ArtifactProofDigest required-value validation. |
| PC-326 | pending | required.DisposalPolicyDigest: remove only DisposalPolicyDigest from valid bound JSON -> break G-326: remove the DisposalPolicyDigest required-value validation. |
| PC-327 | pending | required.Disposition: remove only Disposition from valid bound JSON -> break G-327: remove the Disposition required-value validation. |
| PC-340 | pending | digest.SchemaVersion: replace only SchemaVersion with a second valid value -> break G-340: exclude SchemaVersion from canonical digest input. |
| PC-341 | pending | digest.CardId: replace only CardId with a second valid value -> break G-341: exclude CardId from canonical digest input. |
| PC-342 | pending | digest.DoneRevisionId: replace only DoneRevisionId with a second valid value -> break G-342: exclude DoneRevisionId from canonical digest input. |
| PC-343 | pending | digest.TargetId: replace only TargetId with a second valid value -> break G-343: exclude TargetId from canonical digest input. |
| PC-344 | pending | digest.EndpointId: replace only EndpointId with a second valid value -> break G-344: exclude EndpointId from canonical digest input. |
| PC-345 | pending | digest.TaskId: replace only TaskId with a second valid value -> break G-345: exclude TaskId from canonical digest input. |
| PC-346 | pending | digest.Attempt: replace only Attempt with a second valid value -> break G-346: exclude Attempt from canonical digest input. |
| PC-347 | pending | digest.BindingId: replace only BindingId with a second valid value -> break G-347: exclude BindingId from canonical digest input. |
| PC-348 | pending | digest.PreparationId: replace only PreparationId with a second valid value -> break G-348: exclude PreparationId from canonical digest input. |
| PC-349 | pending | digest.RunnerId: replace only RunnerId with a second valid value -> break G-349: exclude RunnerId from canonical digest input. |
| PC-350 | pending | digest.RunnerStoreId: replace only RunnerStoreId with a second valid value -> break G-350: exclude RunnerStoreId from canonical digest input. |
| PC-351 | pending | digest.RepositoryIdentity: replace only RepositoryIdentity with a second valid value -> break G-351: exclude RepositoryIdentity from canonical digest input. |
| PC-352 | pending | digest.CommonDirectory: replace only CommonDirectory with a second valid value -> break G-352: exclude CommonDirectory from canonical digest input. |
| PC-353 | pending | digest.GitDirectory: replace only GitDirectory with a second valid value -> break G-353: exclude GitDirectory from canonical digest input. |
| PC-354 | pending | digest.WorktreePath: replace only WorktreePath with a second valid value -> break G-354: exclude WorktreePath from canonical digest input. |
| PC-355 | pending | digest.SourceFullRef: replace only SourceFullRef with a second valid value -> break G-355: exclude SourceFullRef from canonical digest input. |
| PC-356 | pending | digest.CreationBaselineSha: replace only CreationBaselineSha with a second valid value -> break G-356: exclude CreationBaselineSha from canonical digest input. |
| PC-357 | pending | digest.OperationId: replace only OperationId with a second valid value -> break G-357: exclude OperationId from canonical digest input. |
| PC-358 | pending | digest.ExpectedHeadSha: replace only ExpectedHeadSha with a second valid value -> break G-358: exclude ExpectedHeadSha from canonical digest input. |
| PC-359 | pending | digest.DestinationFingerprint: replace only DestinationFingerprint with a second valid value -> break G-359: exclude DestinationFingerprint from canonical digest input. |
| PC-360 | pending | digest.DestinationRef: replace only DestinationRef with a second valid value -> break G-360: exclude DestinationRef from canonical digest input. |
| PC-361 | pending | digest.ObservedTargetSha: replace only ObservedTargetSha with a second valid value -> break G-361: exclude ObservedTargetSha from canonical digest input. |
| PC-362 | pending | digest.ProofKind: replace only ProofKind with a second valid value -> break G-362: exclude ProofKind from canonical digest input. |
| PC-363 | pending | digest.ProofIdentity: replace only ProofIdentity with a second valid value -> break G-363: exclude ProofIdentity from canonical digest input. |
| PC-364 | pending | digest.PreservedSourceSha: replace only PreservedSourceSha with a second valid value -> break G-364: exclude PreservedSourceSha from canonical digest input. |
| PC-365 | pending | digest.ReportDigest: replace only ReportDigest with a second valid value -> break G-365: exclude ReportDigest from canonical digest input. |
| PC-366 | pending | digest.ArtifactProofDigest: replace only ArtifactProofDigest with a second valid value -> break G-366: exclude ArtifactProofDigest from canonical digest input. |
| PC-367 | pending | digest.DisposalPolicyDigest: replace only DisposalPolicyDigest with a second valid value -> break G-367: exclude DisposalPolicyDigest from canonical digest input. |
| PC-368 | pending | digest.Disposition: replace only Disposition with a second valid value -> break G-368: exclude Disposition from canonical digest input. |
| PC-369 | pending | digest.RebasedSourceSha: replace only RebasedSourceSha with a second valid value -> break G-369: exclude RebasedSourceSha from canonical digest input. |
| PC-370 | pending | digest.ArtifactPointers: replace only ArtifactPointers with a second valid value -> break G-370: exclude ArtifactPointers from canonical digest input. |
| PC-371 | pending | digest.PolicyPatterns: replace only PolicyPatterns with a second valid value -> break G-371: exclude PolicyPatterns from canonical digest input. |
| PC-380 | pending | receipt.SchemaVersion: replace only SchemaVersion on genuine recipient receipt; missing/null/unknown arm -> break G-380: omit receipt SchemaVersion comparison. |
| PC-381 | pending | receipt.CardId: replace only CardId on genuine recipient receipt; missing/null/unknown arm -> break G-381: omit receipt CardId comparison. |
| PC-382 | pending | receipt.DoneRevisionId: replace only DoneRevisionId on genuine recipient receipt; missing/null/unknown arm -> break G-382: omit receipt DoneRevisionId comparison. |
| PC-383 | pending | receipt.TargetId: replace only TargetId on genuine recipient receipt; missing/null/unknown arm -> break G-383: omit receipt TargetId comparison. |
| PC-384 | pending | receipt.TaskId: replace only TaskId on genuine recipient receipt; missing/null/unknown arm -> break G-384: omit receipt TaskId comparison. |
| PC-385 | pending | receipt.BindingId: replace only BindingId on genuine recipient receipt; missing/null/unknown arm -> break G-385: omit receipt BindingId comparison. |
| PC-386 | pending | receipt.PreparationId: replace only PreparationId on genuine recipient receipt; missing/null/unknown arm -> break G-386: omit receipt PreparationId comparison. |
| PC-387 | pending | receipt.RunnerId: replace only RunnerId on genuine recipient receipt; missing/null/unknown arm -> break G-387: omit receipt RunnerId comparison. |
| PC-388 | pending | receipt.RepositoryIdentity: replace only RepositoryIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-388: omit receipt RepositoryIdentity comparison. |
| PC-389 | pending | receipt.CommonDirectory: replace only CommonDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-389: omit receipt CommonDirectory comparison. |
| PC-390 | pending | receipt.GitDirectory: replace only GitDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-390: omit receipt GitDirectory comparison. |
| PC-391 | pending | receipt.WorktreePath: replace only WorktreePath on genuine recipient receipt; missing/null/unknown arm -> break G-391: omit receipt WorktreePath comparison. |
| PC-392 | pending | receipt.SourceFullRef: replace only SourceFullRef on genuine recipient receipt; missing/null/unknown arm -> break G-392: omit receipt SourceFullRef comparison. |
| PC-393 | pending | receipt.CreationBaselineSha: replace only CreationBaselineSha on genuine recipient receipt; missing/null/unknown arm -> break G-393: omit receipt CreationBaselineSha comparison. |
| PC-394 | pending | receipt.ExpectedHeadSha: replace only ExpectedHeadSha on genuine recipient receipt; missing/null/unknown arm -> break G-394: omit receipt ExpectedHeadSha comparison. |
| PC-395 | pending | receipt.PreservationProofDigest: replace only PreservationProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-395: omit receipt PreservationProofDigest comparison. |
| PC-396 | pending | receipt.ArtifactProofDigest: replace only ArtifactProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-396: omit receipt ArtifactProofDigest comparison. |
| PC-397 | pending | receipt.DisposalPolicyDigest: replace only DisposalPolicyDigest on genuine recipient receipt; missing/null/unknown arm -> break G-397: omit receipt DisposalPolicyDigest comparison. |
| PC-398 | pending | receipt.Disposition: replace only Disposition on genuine recipient receipt; missing/null/unknown arm -> break G-398: omit receipt Disposition comparison. |
| PC-399 | pending | receipt.SetAsideIdentity: replace only SetAsideIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-399: omit receipt SetAsideIdentity comparison. |
| PC-400 | pending | forward child commit; expected published tip -> break G-400: omit push invocation. |
| PC-401 | pending | diverged tip -> break G-401: classify diverged as descends. |
| PC-402 | pending | equal tip with untracked source -> break G-402: hardcode Dirty=false. |
| PC-403 | pending | foreign path and branch -> break G-403: remove target validation before Git. |
| PC-404 | pending | detached worktree -> break G-404: accept detached symbolic head. |
| PC-405 | pending | MERGE_HEAD exists -> break G-405: omit sequencer check. |
| PC-406 | pending | non-FF remote rejection -> break G-406: report push as successful regardless of exit. |
| PC-407 | pending | ancestor tip behind remote -> break G-407: map behind to equal. |
| PC-408 | pending | unpublished commit without Force, then legacy Force positive -> break G-408: omit legacy unpublished-work guard. |
| PC-409 | pending | equal published SHA -> break G-409: refuse exact published tip. |
| PC-410 | pending | mirror behind published SHA -> break G-410: require equality in legacy removal. |
| PC-411 | pending | secondary clone -> break G-411: run remove in primary checkout. |
| PC-412 | pending | unadmitted repository -> break G-412: skip clone-source admission. |
| PC-413 | pending | synthetic credential URL -> break G-413: echo unredacted invalid request URL in exception. |
| PC-414 | pending | secondary path has other origin -> break G-414: skip origin identity check. |
| PC-415 | pending | credentialed existing origin -> break G-415: include raw origin in mismatch text. |
| PC-416 | pending | failing dry-run probe -> break G-416: ignore failed push probe. |
| PC-417 | pending | successful dry-run -> break G-417: remove --dry-run from push probe. |
| PC-418 | pending | normal mirror -> break G-418: create detached mirror. |
| PC-419 | pending | known but wrong fetched SHA -> break G-419: omit fetched-tip equality. |
| PC-420 | pending | absent and populated destination variants -> break G-420: refuse absent repository rather than provisioning. |
| PC-421 | pending | child-start exception and caller cancellation -> break G-421: let start IOException escape without typed mapping. |
| PC-422 | pending | invalid name with valid branch/SHA -> break G-422: skip mirror-name validation. |
| PC-423 | pending | untracked unique source -> break G-423: omit dirty-status guard. |
| PC-424 | pending | root/outside path -> break G-424: skip managed-root validation. |
| PC-425 | pending | /etc removal -> break G-425: allow foreign Cwd into workspace operation. |
| PC-426 | pending | foreign repository request -> break G-426: skip repository admission. |
| PC-427 | pending | foreign publish path -> break G-427: skip allowed-Cwd routing guard. |
| PC-428 | pending | SSH/HTTPS origin normalization -> break G-428: drop repository identity from successful push result. |
| PC-429 | pending | remote lookup fails -> break G-429: continue push on failed origin read. |
| PC-430 | pending | baseline ancestor of observed SHA -> break G-430: return Synchronized without merge. |
| PC-431 | pending | diverged desktop -> break G-431: permit merge after failed ancestry check. |
| PC-432 | pending | dirty tracked desktop -> break G-432: omit dirty check. |
| PC-433 | pending | remote push exit128 -> break G-433: mark push successful regardless of exit. |
| PC-434 | pending | successful prep push -> break G-434: omit configured push options/task tag. |
| PC-435 | pending | known full task id -> break G-435: use nine characters in mirror name. |
| PC-500 | pending | valid complete payload with one stale digest -> break G-500: omit recomputed digest check before journal admission. |
| PC-501 | pending | commit unique change after initial read -> break G-501: omit final HEAD equality. |
| PC-502 | pending | switch branch at same HEAD after initial read -> break G-502: omit final symbolic-ref equality. |
| PC-503 | pending | late nonignored untracked file -> break G-503: omit final untracked status. |
| PC-504 | pending | durable intent changed after initial read -> break G-504: skip final journal identity read. |
| PC-505 | pending | nested registered worktree without a nested repository sentinel masking the check -> break G-505: omit nested-registration check. |
| PC-506 | pending | matching report bytes/digest only inside mirror or set-aside -> break G-506: omit report-root exclusion. |
| PC-507 | pending | external file exists with truncated Result -> break G-507: accept external report based on existence only. |
| PC-508 | pending | same-looking desktop path exists but runner artifact does not -> break G-508: use desktop File.Exists for remote artifact proof. |
| PC-509 | pending | genuine receipt registration gone, directory remains -> break G-509: treat registration success as complete. |
| PC-510 | pending | genuine receipt directory gone, registration remains -> break G-510: treat directory success as complete. |
| PC-511 | pending | task/path/proof/policy changes after persisted intent -> break G-511: rebuild replay command from current task. |
| PC-512 | pending | move wall clock backward with unchanged monotonic deadline budget -> break G-512: compute timeout from wall-clock deadline. |
| PC-513 | pending | default options from production configuration -> break G-513: set default WorktreeResidueSettings.Execute=true. |
| PC-514 | pending | replace admin directory after initial read preserving other facts -> break G-514: omit final admin equality. |
| PC-515 | pending | common identity changes after initial read -> break G-515: omit final common equality. |
| PC-516 | pending | late worktree lock and separate late nested registration -> break G-516: omit final structural inventory read. |
| PC-517 | pending | pause at reply writer and independently reopen runner journal -> break G-517: send success before durable receipt commit. |
| PC-520 | pending | capture.TaskId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-520: omit returned TaskId validation. |
| PC-521 | pending | capture.Attempt: one mismatch/missing member in otherwise valid runner creation receipt -> break G-521: omit returned Attempt validation. |
| PC-522 | pending | capture.PreparationId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-522: omit returned PreparationId validation. |
| PC-523 | pending | capture.RunnerId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-523: omit returned RunnerId validation. |
| PC-524 | pending | capture.RunnerStoreId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-524: omit returned RunnerStoreId validation. |
| PC-525 | pending | capture.BindingId: empty ID, or a changed ID on exact repeated preparation receipt; a first nonempty runner-issued ID is accepted -> break G-525: omit returned BindingId validation. |
| PC-526 | pending | capture.RepositoryIdentity: one mismatch/missing member in otherwise valid runner creation receipt -> break G-526: omit returned RepositoryIdentity validation. |
| PC-527 | pending | capture.WorktreePath: empty path, or changed path on repeated known binding; no desktop filesystem probe -> break G-527: omit returned WorktreePath validation. |
| PC-528 | pending | capture.SourceFullRef: one mismatch/missing member in otherwise valid runner creation receipt -> break G-528: omit returned SourceFullRef validation. |
| PC-529 | pending | capture.CreationBaselineSha: one mismatch/missing member in otherwise valid runner creation receipt -> break G-529: omit returned CreationBaselineSha validation. |
| PC-530 | pending | A writes creation intent and is cancelled; B with the same short id creates the mirror; A retries -> break G-530: adopt a directory when only presence and HEAD match. |
| PC-531 | pending | exact replay after ordinary worktree removal -> break G-531: return the completed binding when that generation's directory is absent. |
| PC-532 | pending | request names another store or runner -> break G-532: copy the request store into a completed binding. |
| PC-533 | pending | store replaced between preparation and retry -> break G-533: reuse the old-store preparation. |
| PC-534 | pending | idle registered recipient -> break G-534: skip the owner-stamp write and still return success. |
| PC-535 | pending | second mirror while the mutation lock is held -> break G-535: skip MutationLock.WaitAsync. |
| PC-536 | pending | server drops the waiter after enqueue -> break G-536: mint a new PreparationId on retry. |
| PC-537 | pending | colliding short id through the live queue -> break G-537: adopt when the owner stamp does not match. |
| PC-538 | pending | exact replay after removal through the live queue -> break G-538: return success when the generation directory is absent. |
| PC-539 | pending | registration store file replaced before the request -> break G-539: bind the requested store and skip the mismatch throw. |
| PC-540 | pending | missing registration file, request names the nonempty custody guid -> break G-540: substitute RunnerStoreId and accept the bound mirror. |
| PC-541 | pending | empty registration file, request names the custody guid -> break G-541: substitute RunnerStoreId and accept the bound mirror. |
| PC-542 | pending | whitespace registration file, request names the custody guid -> break G-542: substitute RunnerStoreId and accept the bound mirror. |
| PC-543 | pending | garbled registration file, request names the custody guid -> break G-543: substitute RunnerStoreId and accept the bound mirror. |
| PC-544 | pending | wrong-length registration guid, request names the custody guid -> break G-544: substitute RunnerStoreId and accept the bound mirror. |
| PC-545 | pending | directory at the registration path, request names the custody guid -> break G-545: substitute RunnerStoreId and accept the bound mirror. |
| PC-546 | pending | mode-denied registration file, request names the custody guid -> break G-546: substitute RunnerStoreId and accept the bound mirror. |
| PC-547 | pending | readable registration guid differs and the request names custody -> break G-547: compare the request with RunnerStoreId instead of the file. |
| PC-548 | pending | readable registration guid matches the request and differs from custody -> break G-548: bind RunnerStoreId and refuse the registration guid. |
| PC-549 | pending | unbound request and missing registration file -> break G-549: require a parsed registration guid before the legacy mirror. |
| PC-550 | pending | omitted binding and missing registration file -> break G-550: fabricate a binding row from the unbound mirror path. |
| PC-551 | pending | Omit present legacy removal admission; expected PhoneHomeAdmissionException is missing |
| PC-552 | pending | Move admission after missing-path prune; expected PhoneHomeAdmissionException is missing |
| PC-553 | pending | Keep admission held after disposal; actual removal refuses workspace_in_use |
| PC-554 | pending | Release removal admission before Git removal/prune; creation must wait through removal and prune |
| PC-555 | pending | Require a configured gate on legacy remove; legacy real removal must succeed |
| PC-556 | pending | Leak removal admission on exception/cancellation; next admission/removal must succeed; bind exception and cancellation variants separately |
| PC-557 | pending | Ignore retained owners; expected PhoneHomeAdmissionException is missing; same-path and absent-sibling prune variants |
| PC-558 | pending | Omit verification removal admission; expected PhoneHomeAdmissionException is missing; bind present and absent variants separately |
| PC-559 | pending | Omit verification creation admission; GitStarts increases while lease held |
| PC-560 | pending | Omit journal/census hold checks; expected PhoneHomeAdmissionException is missing; retiring, torn and incomplete-census variants |
| PC-224/cwd-changed | pending | Fresh pane cwd comparison before attached sidecar effects; prior S3 implementation vector |
