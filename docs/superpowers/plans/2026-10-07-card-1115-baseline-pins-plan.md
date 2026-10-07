# CARD-1115: Present-only baseline pins and lease-busy progress contracts

Date: 2026-10-07. Plan task: `45061b94-1298-48b9-ac17-41d98fb04cc3`.
Board: Antiphon. Branch: `feat/card-task-45061b94`.
Inspected current `origin/master`: **`ca1108d6576c0451f68b9be431a401cf59580ae4`**,
after `git fetch origin` and a fast-forward from the assigned
`b5e78700ae9a76430c13d75cc03399055dd82e59`. All source citations below refer to that
master tree, not the old S2 review SHA. CARD-1082 F3 and CARD-1125/1128 are present.

Outcome: **two production predicates need changing; progress evaluation needs tests,
not a policy change.** Verification design is folded into this plan. **next: code**.
The card's central premise is confirmed. Its suggested moved-remote positive, repeated
in the older F4 plan, contradicts an existing fail-closed guard and its test; D-2
resolves that expectation using current code. No policy decision remains for Build.

This plan supersedes only CARD-1115's part of F4, V-11/V-12 and PC-8 in
[the follow-up plan](2026-10-07-card-1082-followups-plan.md). In particular, do not
implement its unconditional `ProgressObserved` expectation for an Unavailable
baseline, or its instruction to gate every SHA comparison on Present. The
[original S2/S3 contract](2026-10-06-card-1082-settlement-sync-debt-plan.md) and
the landed CARD-1128 wiring probe remain intact.

## Ground truth

Paths in this table are repository-relative; line numbers were checked on the SHA above.

| Card/earlier-plan assumption | What current master does | Plan consequence |
|---|---|---|
| A lease-busy observation can carry a non-null SHA. | `server/Infrastructure/Git/TaskProgressGit.cs:123-142` reads the advertised full SHA, checks local object availability, and returns `Unavailable`, that SHA, fingerprint and `repository_lease_busy` if fetching cannot acquire the lease. `tests/Antiphon.Tests/Application/TaskProgressGitTests.cs:119-149` already pins the SHA and absence of fetch/update-ref. | Keep all observation fields and schema unchanged. |
| Dispatcher baseline pins lack a state gate. | `server/Application/Services/AgentTaskDispatcher.cs:6802-6808` tests only non-null SHA for both `repair-remote` and `primary-remote`. `:6817-6818` copies the observation into the serialized baseline without dropping the SHA. | Add Present to exactly these two predicates. |
| A contended pin is only unnecessary work. | `server/Infrastructure/Git/TaskProgressGit.cs:221-242` validates the pin name, attempts a lease, reads the existing ref and attempts `update-ref`; `pin_failed` is ignored by the dispatcher. Its absence on disk does **not** prove the pin was never attempted. | Tests assert the command trace as well as the resulting pin set. Do not redesign pin ownership. |
| Capturing under the dispatcher lease explains the busy result. | `AgentTaskDispatcher.cs:4674-4681` holds the repository lease through the claim; `:4981-4995` captures the baseline. The baseline observation does not pass that lease (`:6817`). `server/Infrastructure/Git/RepositoryMutationLease.cs:24-60` uses a non-reentrant file lock. | Let dispatch acquire its real lease. Holding an external lease before Tick would prevent dispatch and test the wrong boundary. |
| Repair-source currency and capture share the same observation behavior. | Currency uses `ObserveExactRefUnderLeaseAsync` with the held lease at `AgentTaskDispatcher.cs:6731-6735`, and refuses an already-ahead owner at `:6737-6748`. Capture later uses ordinary `ObserveExactRefAsync`. | The repair fixture must advance origin **after** currency validation, at the capture observation; pre-advancing it would be refused or fetched before capture. |
| The progress service also needs its pin gate fixed. | `server/Application/Services/TaskCompletionProgressService.cs:540-544` gates the current remote tip and its baseline remote pin on Present already. | No production change there. |
| An unmoved advertised primary tip is not new movement. | `TaskCompletionProgressService.cs:878-900` compares current tip with `source.Remote.Sha` even when the baseline is Unavailable. Equal tips skip the remote arm: no claim yields Primary / `NoAttributedProgress` / `no_movement`; a remote-only claim falls to the local claim check. The off-branch counterpart is `:705-714`. | Retain the equality comparison; test with baseline local A and advertised B where **A != B**, so deleting the comparison changes an observable result. |
| A moved remote after an Unavailable baseline qualifies as progress (old F4 V-12). | False for the local-task remote-only claim path. `TaskCompletionProgressService.cs:969-970` returns `Indeterminate / baseline_remote_unavailable` before qualification for either remote origin. Existing `tests/Antiphon.Tests/Application/TaskCompletionProgressPolicyTests.cs:344-353` expressly pins that rule. With Present baseline B, a new descendant C can qualify at `TaskCompletionProgressService.cs:984-1005`. | Pin the negative **and** the Present positive. Never remove this guard to satisfy old V-12. |
| A repair-source test alone covers the primary equality expression. | Repair evaluation calls `EvaluateClaimedOrUnclaimedAsync` (`TaskCompletionProgressService.cs:571-573`); its movement predicate already gates baseline remote movement on State (`:942-944`). Claim origin is selected by reachability at `:953-956`. | Exercise primary and repair separately; a repair-only no-movement test is not coverage of `:878`. |
| `RunnerCompletionProgressTests` necessarily reaches those comparisons. | Runner-eligible tasks take `EvaluateRemoteAsync` at `TaskCompletionProgressService.cs:90-92`, with the Pending/local-object rule at `:187-233`. The primary/repair source evaluator starts at `:103-108` for local tasks. Existing runner tests cover raw Unavailable versus Pending (`RunnerCompletionProgressTests.cs:202-243`), equal tip (`:252-278`) and absent objects (`:285-310`). | New comparison tests use `RunnerId = null`, persisted dispatcher baselines and the public evaluator. Rerun the existing runner contracts as regressions; do not conflate Pending with Present. |
| Non-Present SHA has a written baseline contract. | `server/Application/Dtos/TaskProgressDtos.cs:56-60` has no comment. Other Unavailable outcomes omit SHA (`TaskProgressGit.cs:91-121,146-152`). | Document a **possible** retained advertisement on lease-busy; do not promise a SHA for every Unavailable state or proof of local object absence/presence. |
| The older F4 wiring and F3 work must accompany this card. | `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs:36-58` already checks both injected sweeps; `PhoneHomeRollingRunnerTests.cs:215-252` has all three CARD-1125 removal cases. `server/Application/Services/SettlementSyncRecoveryService.cs:9-19,36-39` has the landed hourly Held recheck, explicitly without supersession. | No edits to wiring, removal, debt recovery, migration, settlement policy or their completed plans. |

The S1..S7 boundary review also checked `SettlementSyncDebtPolicy.Classify/BlockReason`,
`RemoteWorkspaceService.SyncOwnedCheckoutAsync` (advertisement versus ancestry),
`AgentTaskReplyService.cs:945-975,1040-1043,1152-1178,1998`, and the S7
`ReviewEvidenceResettlementTests.C1082_PendingContinuationRefusesStrictRebind` and
`SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch`.
These consume settlement sync evidence, not the two dispatcher pin predicates.
Pending remains unconfirmed; no accepted debt or review authority is changed.

## Decisions

### D-1. Add only the two Present predicates

In `CaptureProgressBaselineAsync`, use the same shape as the existing progress-service gate:
`remote is { State: ProgressRemoteState.Present, Sha: { } sha }`, then pin that SHA.
Apply it independently to owner/repair and primary. Keep local pins, observation calls,
warning text, snapshot serialization and ignored pin-result handling as they are.

This is a small fail-closed, cost-reducing production change: an advertisement without
local verification grants no remote baseline pin. It introduces no new Git command,
database query, retry, configuration or migration. For each suppressed call it removes
the pin-specific name check, lease attempt, show-ref and possible failed update-ref;
the test does not impose a brittle total process/SQL count on the whole dispatcher.

Rejected: tests/docs only (leaves the confirmed unnecessary mutation attempt); another
`cat-file -e` before pinning (duplicates the observation's proof and adds a child);
dropping the advertised SHA (breaks S2 and the conservative equality check); changing
`PinBaselineAsync` lease behavior (different ownership contract and much larger scope).

### D-2. Preserve existing attribution; correct the earlier suggested expectation

Let A be captured local tip, B the advertised origin tip captured while unavailable,
and C a later descendant of B, with A != B != C. At evaluation the lease is free,
the remote observation is Present and the local checkout/ref remains A.

| Task/source and input | Required existing result |
|---|---|
| Local task, primary baseline Unavailable(B), current remote B, no claim | Primary; NoAttributedProgress; `no_movement`; no verified SHA. |
| Same, valid task-scoped claim B | Primary; NoAttributedProgress; `claimed_commit_unreachable`; no verified SHA (B is not reachable from local A). |
| Local task, primary baseline Unavailable(B), current remote C, claim C | PrimaryRemote; Indeterminate; `baseline_remote_unavailable`; incomplete evidence and no automatic workspace mutation. |
| Same primary scenario with **Present(B)** captured at dispatch | PrimaryRemote; ProgressObserved; verified C. |
| Repair baseline Unavailable(B), owner local A, remote C, claim C | RepairSourceRemote; Indeterminate; `baseline_remote_unavailable`; incomplete evidence and no automatic workspace mutation. |
| Repair baseline Present(A), owner remote advances to C, claim C | RepairSourceRemote; ProgressObserved; verified C. |

The Present repair control can capture A without an artificial race. Both remote-only
positives require a claim and prove no owner/primary checkout was moved by evaluation.
Use the returned source assessment/origin/reason, not just task terminal status: an
Indeterminate local-task settlement has separate warning/settlement semantics.

Rejected: changing `TaskCompletionProgressService` to make moved Unavailable baselines
positive (weakens the existing R20 guard); imposing Present on the equality comparisons
(would discard retained negative evidence); using only A == B (the `remoteTip !=
source.LocalSha` conjunct would hide removal of the comparison being pinned).
The old off-branch comparison does not promise a new independent observable outcome:
an Unavailable remote qualification is discarded unless positive (`:708-714`). Do not
add a trace-only invocation-count test or a production probe solely to claim that line.

### D-3. Use real capture and Git, with one controlled repair race

Extend `RepairSourceDispatchTests` and reuse `RepairSourceWorld` and
`ControlledTaskProgressGit.Trace/BeforeCommand`. Keep fixture helpers private to this
test class unless reuse proves necessary; no production test hook is needed.

Primary setup: create `RepairSourceWorld.CreateAsync(ordinaryCodeTask: true)`. Before
dispatch, derive the future task full ref from `RemoteWorkspaceService.OwnedBranch`
and `world.Repair.Id`. Push the existing master A to that **remote-only** ref, then use
`CommitFromSecondCloneAsync` to push B. Assert B is not local with raw scratch Git and
that the local task ref does not yet exist. Dispatch creates its local ref at A.
For the Present argument, fetch B's objects into a task-owned scratch observation ref
before dispatch without moving the task branch/HEAD. Do not set a RunnerId.

Repair setup: start the normal world with owner local/origin A. Count only exact
`ls-remote` calls for `world.OwnerRef` in `BeforeCommand`. The first is currency
validation and sees A. Immediately before executing the second (baseline capture),
push B from the second clone using the raw ScratchGitRepo helper, which bypasses
the trace hook. Then allow the real command to run and the nested lease to refuse
fetch. Assert that the boundary fired once, B is not local and capture is Unavailable.
The Present control uses normal equal owner A and no race. Clear the hook in `finally`.
Never fail every cat-file invocation or fake the observation result.

Both setups reload the task row, deserialize its actual `ProgressBaselineJson`, and
check the exact state, SHA, reason and nonempty fingerprint. Assert local baseline
pins remain. For unavailable, assert **no command names the remote baseline pin**
(check-ref-format, show-ref and update-ref) and no resulting remote baseline ref.
For Present, assert the exact remote pin resolves to the observed SHA. Separate
primary/repair methods ensure neither predicate gets only inspection coverage.

For progress tests, use `new TaskCompletionProgressService(world.Git)` on the reloaded
task. This intentionally omits the independent file-progress rescue and still calls
the real public evaluator, exact-ref observer and ancestry commands. Check
`RemoteSync == null` to prove the local path. Release/capture completion is awaited
before evaluation; no sleeps or externally held lease span Tick.

### D-4. Document the data contract beside the record

Add this XML summary to `ProgressRemoteBaseline`, with normal XML escaping as needed:

> A repository_lease_busy observation may retain the exact origin-advertised Sha with
> State Unavailable. That SHA is not proof that the object is locally available.
> Require Present before pinning it or treating it as a locally verified baseline.
> The retained advertisement may participate in conservative equality/no-movement
> checks. Other Unavailable observations may omit Sha.

Behavior pins are V-1..V-5 and R-1 below; no test asserts the comment's wording.
No owner-doc sentence needs changing. Leave the previous multi-card follow-up plan
and the runtime/parking documentation with their current owners.

### D-5. Dynamic placement, collision sequencing and activation

Read `GET /api/runner-defaults` and `GET /api/session-runners` at 2026-10-07
18:35 UTC. Both returned 200. Defaults revision 2 retains an operator-selected
global preference; the catalogue has one available Windows entry and two available
Linux entries, with one Linux entry draining and one accepting work. This is a
point-in-time observation, not a fleet-location requirement. Re-read both at dispatch.
The checkpoint lane is Linux with real Git and isolated PostgreSQL; it has no OS-only
dependency. Omit `-Runner` and `-Platform`; do not pin a fleet hostname. `-Platform Any`
is needed only to explicitly clear an existing OS pin.

S1 shares **`server/Application/Services/AgentTaskDispatcher.cs`** with the brief's
in-flight CARD-1149/1150 S1 Code task `1ac80818`; defer that Code slice while the
same file is being changed, then start from their landed master and recheck the
two predicates/call sites. Do not overwrite their recovery changes.
`SessionReconciliationService.cs` is theirs and is not touched here.
Neither slice shares `scripts/c590-remote.sh` with CARD-1105. S2 shares this card's
test class with S1 and runs after S1; no parallel editing of that file.

Only S1 changes server execution and needs an AppHost restart **after land**.
S2's tests/XML comment need no additional restart. Activation is caller-owned from
the canonical checkout using the AppHost runbook, followed by `/health` and
`GET /api/version` matching the landed SHA. No runner rollout is required.

This plan neither adds nor changes an input-wait session. It adds no release timer:
parking stays default-off and currently supplies no automatic release deadline for
such a session (CARD-1083). CARD-0079 remains the only automatic stop of a Working
session, within its existing narrow conditions.

### D-6. Verification is designed here, not deferred to a separate stage

The brief requires the exact checkpoints, negative mutations, regressions and doc
pins; the following section supplies them for Build. No whole-Unit, namespace,
assembly, E2E, live-provider or full settlement-suite run is authorized by this plan.
Every new test has a compiling, method-scoped production mutation that makes its
outcome assertion red. Ordinary Code runs V/R; post-land SourceLanding Mutation
executes the temporary controls and restores its snapshot under the owner policy.

## Implementation slices

Commit and push each slice before its checkpoint group. No rebase, amend of a pushed
commit, or force-push on this task branch. A source change after a run invalidates
reuse of that run's clean build certificate.

| Slice | Budget | Files and concrete work | Tests / checkpoints | AppHost restart / collision |
|---|---|---|---|---|
| S1: pin predicates and capture proof | 60 min including CPs | `server/Application/Services/AgentTaskDispatcher.cs`: two Present predicates only. `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`: two parameterized pin tests and private fixture setup helpers described in D-3. | V-1 primary pin, V-2 repair pin; CP-1..CP-6. | Yes after land; dispatcher overlaps CARD-1149/1150. |
| S2: retained-advertisement semantics and record contract | 60 min including CPs | `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`: three parameterized public-evaluator tests reusing S1 capture helpers. `server/Application/Dtos/TaskProgressDtos.cs`: D-4 XML summary only. No production evaluator change. | V-3 unchanged primary, V-4 moved primary, V-5 moved repair; CP-7..CP-14. | No additional restart; test file shared with S1. No overlap with CARD-1105. |

## Verification design

### Behaviors and independent oracles

All five new methods live in `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`.
Retain its Integration/Slow classification and assembly-local ProcessSpawnLimit.
Each has exactly two `[Arguments]` cases and thus two native executed results.
Do not count internal assertions, fixture operations or a private helper as results.

| ID | Method and argument roster | Independent assertions |
|---|---|---|
| V-1 | `C1115_PrimaryBaselinePinsOnlyPresentObservations(bool present)`; false, true | D-3 actual primary capture. False: Unavailable/B/lease-busy/fingerprint retained, B absent locally, no pin command or remote pin. True: Present/B, pin resolves B. Local pin resolves A; stored baseline survives serialize/deserialize with all remote fields. |
| V-2 | `C1115_RepairBaselinePinsOnlyPresentObservations(bool present)`; false, true | D-3 owner race. False: Unavailable/B/lease-busy, no repair-remote pin command/ref, both local pins retained. True: Present/A and repair-remote pin A. Assert Dispatched and reloaded baseline in both cases, not merely the hook firing. |
| V-3 | `C1115_UnavailablePrimaryBaselinePreservesUnmovedRemoteSemantics(bool withClaim)`; false, true | Capture actual Unavailable(B), local A != B. Release dispatch; evaluate unchanged origin B with no claim or claim B. Require D-2's exact negative source/reason, current RemoteObserved B, no VerifiedSha, no automatic workspace mutation, unchanged local ref/HEAD and byte-identical persisted baseline. |
| V-4 | `C1115_MovedPrimaryRemoteRequiresPresentBaseline(bool present)`; false, true | Capture B as Unavailable or Present, then push descendant C from the second clone. Claim C. False: PrimaryRemote / Indeterminate / baseline_remote_unavailable / Complete=false. True: PrimaryRemote / ProgressObserved / VerifiedSha=C. Local A and persisted baseline remain unchanged. |
| V-5 | `C1115_MovedRepairRemoteRequiresPresentBaseline(bool present)`; false, true | Capture repair B as Unavailable via the controlled race, or A as Present normally; then push a novel descendant C and claim it. False: RepairSourceRemote / Indeterminate / baseline_remote_unavailable / Complete=false. True: RepairSourceRemote / ProgressObserved / VerifiedSha=C. Primary and owner checkout/ref tips and stored baseline are unchanged. |

V-1/V-2 deliberately assert attempted commands: on a real absent object, old code can
fail update-ref and still leave no pin. That failed mutation must turn the test red.
V-3 deliberately uses A != B, and a complete public-evaluator result; it does not
infer semantics from a source-string match. V-4/V-5 distinguish an unavailable
baseline from a later successful observation. The stored JSON never changes state
merely because the evaluator later fetched the object.

### Regression classes and exact selection

| ID | Class / methods rerun | Why |
|---|---|---|
| R-1 | `TaskProgressGitTests.C1082_LeaseBusyObservationReportsAdvertisedTip` | Original S2 advertisement and no unlocked fetch/update-ref contract. |
| R-2 | `RepairSourceDispatchTests.C499_V03_OccupiedSourceRoutesToAnIsolatedBranchAtTheOwnerSha` | Existing Present repair baseline and isolated routing. |
| R-3 | `RepairSourceDispatchTests.C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline` | Ordinary unreadable-remote baseline still dispatches with warning. |
| R-4 | `RepairSourceDispatchTests.C675_RepairSourceRefusesWhenOwnerRemoteIsAheadOrDiverged` (behind, diverged) | Fixture race must not bypass or weaken genuine currency refusal. |
| R-5 | `TaskCompletionProgressPolicyTests.C499_R20_UnavailableBaselineRemoteCannotCreditARemoteClaim` | Pre-existing unavailable-baseline guard, also with null advertised SHA. |
| R-6 | `RunnerCompletionProgressTests.C1082_PendingWithLocalObjectsAttributesClaimAgainstObservedTip` | Pending has its own local-object/ancestry authority; raw Unavailable is still refused. |
| R-7 | `RunnerCompletionProgressTests.C1082_PendingEqualTipIsNoPushedProgress` | Equal runner source tip retains its separate no-pushed-progress reason. |
| R-8 | `RunnerCompletionProgressTests.C1082_PendingWithoutLocalObjectsIsIndeterminate` | Advertisement alone cannot supply runner progress. |
| R-9 | `RunnerCompletionProgressTests.Local_and_repair_claim_rules_are_unchanged` | Existing real-Git local and remote-only claim positive/negative controls. |

These are bounded method selections within the named classes, not promises to rerun
each entire class. The changes do not reach a debt sweep, release path, receipt
delivery, landing writer or Program registration, so their previously reviewed suites
are not added to this dispatch's closed list. No new asynchronous outcome-delivery
chain is introduced: the new tests inspect the dispatcher-persisted baseline and
the synchronous evaluator result, not a caller-delivery claim.

### Guard inventory and negative controls

Every filter below is method-scoped with a trailing wildcard solely for TUnit's
native method/result naming. Verify its literal method and both argument results.
All controls share production files/methods and run **sequentially**, never batched.

| PC | Production mutation (line on inspected master) | Exact detecting filter | Expected assertion failure |
|---|---|---|---|
| PC-1 | After S1, restore only the old primary `Sha is not null` predicate at `AgentTaskDispatcher.cs:6807`. | `/*/*/RepairSourceDispatchTests/C1115_PrimaryBaselinePinsOnlyPresentObservations*` | False argument records commands naming baseline-primary-remote, including the doomed update-ref. |
| PC-2 | Restore only the old repair SHA predicate at `AgentTaskDispatcher.cs:6802`. | `/*/*/RepairSourceDispatchTests/C1115_RepairBaselinePinsOnlyPresentObservations*` | False argument records commands naming baseline-repair-remote. |
| PC-3 | Suppress the primary remote pin call at `AgentTaskDispatcher.cs:6808` while keeping compilation valid. | `/*/*/RepairSourceDispatchTests/C1115_PrimaryBaselinePinsOnlyPresentObservations*` | True argument's exact Present/B baseline pin is missing. |
| PC-4 | Suppress the repair remote pin call at `AgentTaskDispatcher.cs:6803`. | `/*/*/RepairSourceDispatchTests/C1115_RepairBaselinePinsOnlyPresentObservations*` | True argument's Present/A repair pin is missing. |
| PC-5 | Remove only `remoteTip != source.Remote.Sha` from `TaskCompletionProgressService.EvaluatePrimaryGraphAsync` at `:878`; keep the local-SHA conjunct. | `/*/*/RepairSourceDispatchTests/C1115_UnavailablePrimaryBaselinePreservesUnmovedRemoteSemantics*` | No-claim reason becomes unclaimed_or_unmatched_commit/remote origin; claim argument becomes Indeterminate/baseline_remote_unavailable instead of the local negative. |
| PC-6 | Remove/bypass the `source.Remote.State == Unavailable` early return in `QualifyClaimAsync` at `TaskCompletionProgressService.cs:969-970`. | `/*/*/RepairSourceDispatchTests/C1115_MovedPrimaryRemoteRequiresPresentBaseline*` | False argument wrongly credits C as ProgressObserved. |
| PC-7 | Same mutation as PC-6, restored and applied separately for this detecting method. | `/*/*/RepairSourceDispatchTests/C1115_MovedRepairRemoteRequiresPresentBaseline*` | False argument wrongly credits C as RepairSourceRemote progress. |
| PC-8 | In `TaskProgressGit.ObserveExactRefCoreAsync`'s local `LeaseBusy` factory at `:130-131`, replace advertised with null. | `/*/*/RepairSourceDispatchTests/C1115_PrimaryBaselinePinsOnlyPresentObservations*` | False argument's persisted baseline SHA is null instead of B. |

For every PC: baseline green, compiling defect, exact-method red, revert, rebuild,
same-method restored green. Min=2 per phase; expected red is the listed assertion,
not zero tests, skip, timeout, fixture/startup failure or build error. Keep separate
per-PC evidence and actual SHA. A control that cannot reach its named assertion
requires fixture repair, never a weakened expectation. Do not edit source while a
driver is running. SourceLanding uses its external evidence root and never commits
temporary mutations, snapshot changes or generated receipts.

### Docs sentences with pins

D-4's XML summary is the only product documentation change. Its contract is covered
by V-1/V-2 (captured state/SHA and pin authority), V-3 (conservative equality),
V-4/V-5 (remote attribution) and R-1 (origin advertisement). No source-text test is
needed. No card export under `docs/cards/` is edited.

### Checkpoints

Lane for every row: **Linux**, real Git; CP-1/2/4/5/6/7/8/9 use isolated PostgreSQL.
All rows run serially, and each test host also sets `TUNIT_MAX_PARALLEL_TESTS=1`.
This prevents argument cases using shared legacy DB fixtures from overlapping; a
row's Serial flag alone would not do that. Existing ProcessSpawnLimit remains.
Each row is one behavior and one exact method selector with a trailing wildcard.
No whole-Unit/class/namespace/assembly filter. CP-1 and CP-7 build separately for
their distinct committed After groups; all reuse stays within the same After group.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1115-s1/` | linux-primary-pin | `/*/*/RepairSourceDispatchTests/C1115_PrimaryBaselinePinsOnlyPresentObservations*` | V-1 | exact 2 results, 0 failed/skipped | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | CP-1 | linux-repair-pin | `/*/*/RepairSourceDispatchTests/C1115_RepairBaselinePinsOnlyPresentObservations*` | V-2 | exact 2 results, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | CP-1 | linux-advertisement | `/*/*/TaskProgressGitTests/C1082_LeaseBusyObservationReportsAdvertisedTip*` | R-1 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | CP-1 | linux-present-routing | `/*/*/RepairSourceDispatchTests/C499_V03_OccupiedSourceRoutesToAnIsolatedBranchAtTheOwnerSha*` | R-2 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S1 | CP-1 | linux-unreadable-capture | `/*/*/RepairSourceDispatchTests/C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline*` | R-3 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S1 | CP-1 | linux-owner-currency | `/*/*/RepairSourceDispatchTests/C675_RepairSourceRefusesWhenOwnerRemoteIsAheadOrDiverged*` | R-4 | exact 2 results, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1115-s2/` | linux-unmoved-primary | `/*/*/RepairSourceDispatchTests/C1115_UnavailablePrimaryBaselinePreservesUnmovedRemoteSemantics*` | V-3 | exact 2 results, 0 failed/skipped | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S2 | CP-7 | linux-moved-primary | `/*/*/RepairSourceDispatchTests/C1115_MovedPrimaryRemoteRequiresPresentBaseline*` | V-4 | exact 2 results, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S2 | CP-7 | linux-moved-repair | `/*/*/RepairSourceDispatchTests/C1115_MovedRepairRemoteRequiresPresentBaseline*` | V-5 | exact 2 results, 0 failed/skipped | 2 | 2 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S2 | CP-7 | linux-unavailable-policy | `/*/*/TaskCompletionProgressPolicyTests/C499_R20_UnavailableBaselineRemoteCannotCreditARemoteClaim*` | R-5 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S2 | CP-7 | linux-pending-local | `/*/*/RunnerCompletionProgressTests/C1082_PendingWithLocalObjectsAttributesClaimAgainstObservedTip*` | R-6 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S2 | CP-7 | linux-pending-equal | `/*/*/RunnerCompletionProgressTests/C1082_PendingEqualTipIsNoPushedProgress*` | R-7 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S2 | CP-7 | linux-pending-unfetched | `/*/*/RunnerCompletionProgressTests/C1082_PendingWithoutLocalObjectsIsIndeterminate*` | R-8 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S2 | CP-7 | linux-existing-claims | `/*/*/RunnerCompletionProgressTests/Local_and_repair_claim_rules_are_unchanged*` | R-9 | exact 1 result, 0 failed/skipped | 1 | 1 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

## Execution, cost and delivery

Ordinary Code floor: **28 minutes** (13 S1 + 15 S2), two isolated builds,
**20 native test results** across 14 rows. Authoring allowance: approximately
47 minutes for S1 and 45 for S2; each slice fits its 60-minute budget before slot
wait/diagnostic contingencies. Counts and minutes are independent. PCs are a
separate eight-cycle Mutation obligation, about 30-45 minutes including builds;
do not claim them green from ordinary V/R or merely from this static plan review.

Use the CARD-0723 checkpoint tool once per committed slice group. Bootstrap/build
the tool through `scripts/build-slot.ps1` into a producer-owned `bin-.../` output
if no certified tool is available; report that tooling bootstrap separately.
Run its `run --plan docs/superpowers/plans/2026-10-07-card-1115-baseline-pins-plan.md
--after S1 --expected-source-sha <committed-full-sha>` (then S2 against its commit).
The documented `dotnet run --project tools/Antiphon.Checkpoints -- run --plan ...`
entry point itself goes through the host build-slot wrapper. The checkpoint
executor obtains slots for the actual build/test drivers. Await every run; when
the tool yields exit 75, keep calling its `wait` until terminal. Slot timeout is
not permission to run unleased.

Code reports each CP with the unedited CHECKPOINT line, actual expanded roster,
source SHA, clean/build provenance, counts and reruns. Review executes/qualifies
the same required rows at the exact final source SHA: S1 receipts from an earlier
commit must not be relabeled as final-source evidence. If S1's test helpers change
during S2, rerun the affected S1 rows then and report why. Commit source before
long tests and keep it frozen throughout the group. Inherited failures are checked
at the recorded base with the exact failing method; do not loosen assertions,
timeouts or add retries. Clean up only this task's isolated `bin-c1115-*` outputs
in every built project after all owned runs have ended.

Code/Review run `scripts/check-evidence-diff.ps1` over their full assigned task
range. TRX, logs, JSON receipts and checkpoint payloads stay ignored. No migration,
settings change, production evaluator rewrite or unrelated dispatcher recovery fix
belongs in these slices. If the actual capture differs from the stated branch
path, stop expanding production scope: measure persisted State/Sha/LocalSha,
current ref/HEAD, source Origin/Assessment/Reason and pin-command trace on that
exact SHA, then return the discrepancy for investigation.

Plan validation is static: inspected master code/card, checked method names and
line citations, and reviewed the manifest shape. No build, test execution, PC
cycle or live activation is claimed by this planning task.
