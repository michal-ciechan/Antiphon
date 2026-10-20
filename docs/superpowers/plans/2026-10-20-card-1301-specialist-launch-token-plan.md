# CARD-1301: specify and test fresh-launch task credentials

Plan task: `8bd0836c-05b8-4b29-bce2-a424709ddb07`.
Branch: `feat/card-task-8bd0836c`.
Assigned base: `bf9d96db2449bc35dab3c7d1c168c0ce9e78359c`.
Latest fetched master: `3ab9e9d0d531d15a610e5497a72f1d85988610d8`.
Card: Antiphon CARD-1301, `56648cc9-6ad2-42be-96e7-bc8e3e798ae1`.

The two coverage gaps still exist on fetched master. The proposed outcome is
tests and a documented policy: keep the existing Worker credential for a fresh
specialist process, and prove that failure to save its hash prevents launch.
No production repair is expected under the defaults below. This is a Plan
artifact; TestDesign is a separate stage after the caller settles the decisions.

## Ground truth

Source references below are relative to the assigned base. The subsequent
master delta changes checkpoint fixtures and unrelated CARD-1233 documents;
the production and token-test sources inspected here are unchanged.

| Card assumption | What current code does | Planning consequence |
|---|---|---|
| Specialist/Check runs start without a token hash. | `SpecialistTaskRunner.CreateRunTaskAsync` (`server/Application/Services/SpecialistTaskRunner.cs:308`) creates Worker/Shared tasks without `TokenHash` or a raw-cache entry. `SpecialistRequestService.ClaimAsync`'s task initializer (`server/Application/Services/SpecialistRequestService.cs:225`) also omits them, but its routed Check attempt already requires a live, qualified session. | Exercise the legacy/general pinned fresh-spawn shape; do not claim a routed Check attempt can bypass its live-session qualification. |
| A non-AlwaysOn standing agent with no live session can fresh-spawn. | `PlaceOnStandingAgentAsync` (`server/Application/Services/AgentTaskDispatcher.cs:8158`) returns `SpawnFresh` for that shape and `WaitForAgent` for AlwaysOn. Existing `AgentTaskStandingAgentDispatchTests.a_standing_agent_that_nothing_supervises_still_gets_a_fresh_session` checks placement, not credentials. | Seed the actual pinned Worker/Shared shape with null hash, an absent raw-cache entry and a stopped prior session. |
| CARD-1285 now gives this process a token. | `FinishDispatchedLaunchSpecAsync` (`AgentTaskDispatcher.cs:6224`) has no role/null-hash exclusion. Its repair block mints and saves a hash before returning the environment. `BuildEnv` (`:7075`) consumes any cached token. | The behavior is present; the missing piece is policy and regression coverage, not another token-generation mechanism. |
| The token is a Worker credential. | `AgentTaskService.AuthenticateAsync` (`server/Application/Services/AgentTaskService.cs:187`) resolves the hash to that task and its session. `Caller.MayDelegate` (`:180`) is false for Worker. `CreateAsync` rejects a caller without that permission (`:618`). | Assert the exact task/session identity and `MayDelegate == false`; do not describe this as a new capability or a session-scoped delegating token. |
| No test proves a failed hash save blocks launch. | `PhoneHomeTaskDispatchProjectionTests.TaskToken.cs` covers six missing/cached/stale/relaunch cases, all seeded with a non-null original hash. It observes committed state at enqueue, but injects no save failure. Both fresh dispatch (`AgentTaskDispatcher.cs:6029`) and boot-wedge relaunch (`:6476`) await the same helper before calling the launch sink. | Add one parameterized save-failure test covering both callers, using the real persistence boundary. |
| A late failure can strand a committed claim/Starting session. | Initial dispatch commits the claim/session at `AgentTaskDispatcher.cs:6000`, then composes the spec. Its generic tick catch (`:1043`) fails/notifies the task with the misleading “before a session existed” text; it does not terminalize that session there. Relaunch catches/logs composition failure (`:6487`) and can continue to brief queuing. | Item 3 remains a separate lifecycle question. A zero-launch assertion must not imply claim cleanup, successful recovery or an unchanged final hash. |
| The report may concern only the old RC line. | CARD-1285 commit `b602eac16a150d2acfe05d472aff500524168195` is an ancestor of both inspected master and fetched `origin/rc/2026-10-15` at `61117dd1663d2f47ce4708c18b609b8936491986`. The RC has the same repair block and identical six-case token-test file. CARD-1301's title explicitly names `[rc-2026-10-15]`. | Report RC applicability to the caller; do not choose a release target or commission an RC change from this task. |

## Decisions

These are proposed defaults for caller disposition, not newly approved product
policy. The completed plan therefore returns `next: decide`.

- **D-1 — Retain a Worker token for every fresh specialist launch**, including an
  initially null hash. A fresh process has an attributable task identity, and the
  existing authenticator preserves Worker authority. Reject suppressing tokens
  by specialist role: it would introduce a second fresh-launch identity contract
  without a stated requirement. Reject substituting a standing-session token:
  session principals can delegate and would widen authority.
- **D-2 — Pin the current save-before-launch behavior with tests.** Keep minting
  at the common final launch boundary and fail closed when its save throws.
  Reject moving minting to specialist creation: it recreates the restart/cache
  lifetime problem CARD-1285 fixed. Reject separate implementations in the two
  callers: their shared ordering already provides the required behavior.
- **D-3 — Defer claim/session cleanup from item 3 to separately scoped work.**
  This plan records the gap but changes no recovery, release or retry policy.
  Reject automatically clearing the claim in a generic exception handler:
  committed generation, ownership and any uncertain external effects require
  their own analysis. If the caller includes cleanup, return to investigation:
  measure persisted task/session/agent pointers, launch-attempt evidence,
  workspace consumers and the next recovery tick after a late save failure.
- **D-4 — The caller selects publication scope.** Master is the implementation
  baseline, but the named RC also contains the behavior. Report that fact rather
  than assuming either an RC backport or a master-only close is authorized.

No session-wait or release behavior changes under these defaults. In particular,
this work supplies no automatic release deadline for an input-waiting session;
parking is disabled by default (CARD-1083).

## Slices

### S1 — Cover tokenless specialist fresh spawn

Add `tests/Antiphon.Tests/Application/TaskLaunchTokenBoundaryTests.cs`, with the
method `C1301_tokenless_specialist_fresh_spawn_has_worker_token`, parameterized
over `Check`, `Distill` and `Diagnose`. Use an isolated PostgreSQL schema, a local
recording `IAgentTaskLaunchSink`, inert runner dependencies and the standing
dispatch graph as the model. Put fixture support in
`tests/Antiphon.Tests/Application/TaskLaunchTokenBoundaryTests.Harness.cs` if
needed; avoid changing unrelated shared fixtures.

Seed a Worker/Shared task pinned to a non-AlwaysOn standing agent with a stopped
session, null `TokenHash` and no entry in `AgentTaskService.RawTokens`. Use the
actual dispatch path, not a private launch-helper invocation. At enqueue, an
independent context must see the new session binding and a committed hash
matching the environment token. Authenticate through a fresh scope and assert
the same task, the fresh session, and no delegation permission. Check token
absence from argv without printing the token or its hash. Remove only this
test's raw-cache entry in `finally`.

This one method is behavior B-1's narrow green selection. Its one red witness is
the compiling defect “skip token recovery when `task.TokenHash` is null,” which
must fail at the authenticating-token/durable-hash assertion for all three roles.
It must not fail earlier from fixture admission, build or discovery.

### S2 — Cover token-hash save failure in both launch callers

In the same new class/harness add
`C1301_token_hash_save_failure_blocks_launch`, parameterized over initial
dispatch and `RelaunchWedgedAsync`. Register a test-local EF save interceptor in
every dispatcher context. Target the seeded task's changed `TokenHash` only;
allow fixture writes and the committed claim/new-session write to succeed.
Throw once at the late hash save, before persistence. Do not use an unconditional
“first save fails” interceptor or a fragile absolute save-call count.

Prove that the intended fault fired exactly once, the independent database read
at that boundary still sees the prior hash and committed Starting generation,
and no launch-sink call for that new generation occurs. Initial dispatch may
report failure through its outer catch; relaunch catches internally, so a
`Should.Throw` assertion is not the common oracle. Observe state through a fresh
context. Later failure/brief saves can persist tracked changes; do not demand
that the final hash remain unchanged or claim item 3 is repaired.

This method is behavior B-2's narrow green selection. Its one red witness is
the compiling defect “catch and ignore the replacement-hash save exception,
then return the token-bearing spec.” Both caller variants must fail the
zero-launch assertion. A defect that skips the save and therefore never reaches
the injected fault is not this witness.

### S3 — Record policy and qualify the two methods

Add a short task-credential paragraph to `docs/agent-credentials.md`: null-hash
specialist fresh spawn receives a durable Worker token; live-session reuse does
not rotate credentials; hash persistence precedes launch. Do not alter
authentication, role policy, producers or runner transport.

Expected production source delta: none. If either test exposes a different
product failure on the selected base, preserve its failing commit/evidence and
return the exact finding before expanding this plan. Do not manufacture a
production change simply to obtain a red-first history for existing behavior.

Commit and push each meaningful slice. Code performs ordinary green
qualification; Review remains independent. The two intentional red witnesses
belong to post-land SourceLanding Mutation under the repository's current order.
For each witness use only its exact method, observe the expected assertion,
restore, rebuild and run that same method green once. Keep both obligations
pending until that execution is commissioned and complete.

## TestDesign handoff

The brief did not fold TestDesign into Plan. After decisions, TestDesign adds
the executable `## Verification design` and `### Checkpoints` manifest to this
artifact. Bound ordinary scope to the following two selections, reusing one
isolated build after S1-S3; no whole-Unit, class-wide or assembly run.

| Planned checkpoint | Lane | Exact filter | Executed-case floor | Obligation |
|---|---|---|---:|---|
| CP-1 | Portable server integration / isolated PostgreSQL | `/*/*/TaskLaunchTokenBoundaryTests/C1301_tokenless_specialist_fresh_spawn_has_worker_token` | 3 | B-1; one ordinary green, one method-scoped red/restored-green witness after land |
| CP-2 | Portable server integration / isolated PostgreSQL | `/*/*/TaskLaunchTokenBoundaryTests/C1301_token_hash_save_failure_blocks_launch` | 2 | B-2; both callers, one ordinary green, one method-scoped red/restored-green witness after land |

No real provider CLI, production runner or browser is needed. Read
`GET /api/runner-defaults` and `GET /api/session-runners` when commissioning.
This plan read both on 2026-10-20: defaults revision 2, with the configured
default resolving to an eligible Linux runner with capacity. Placement remains live configuration: omit
`-Runner` and `-Platform`; no host is pinned by this plan.

TestDesign should budget approximately six minutes for the shared isolated build
and two ordinary selections, plus authoring and actual build-slot wait. Budget
the two post-land red/restore/green cycles separately (approximately 20–24
minutes, dominated by isolated builds). These are estimates, not measured
results. Use the checkpoint tool/build-slot gate, committed source identity,
fresh TRX counts and receipt validation; keep generated evidence ignored.

## Plan-stage validation

The card, current master, named RC, producers, authenticator, both launch
callers and existing relevant test methods were inspected. This task runs no
build, product test or positive control; source inspection establishes the
coverage gap, not executed acceptance. Plan publication is to the assigned
branch only. The caller first disposes D-1 through D-4, then dispatches
TestDesign with this artifact.
