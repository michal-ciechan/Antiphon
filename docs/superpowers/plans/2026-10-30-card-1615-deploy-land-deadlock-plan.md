# CARD-1615: isolate rollout control from canonical activation

Plan only. Base `ade54222543381e2e1b31ad79d8ae33a4035f70b` (rc source),
branch `feat/card-task-7734ac39`, inspected 2026-10-30. No product edits or rollout.

Choose **option (d), an owned Worktree controller**, with an early script admission
check against accidental Shared execution. Keep canonical pull/restart/provisioning
in a separate, bounded Shared task that never waits for recycle or pending lands.
Do not change land writer classification or weaken the recycle census.

## Root cause and ground truth

CARD-1615 records rollout `c727fa0180cd0d55` at rc `8edda999`: Shared Deploy
`ec24c45b` held request `6f070ca6`, owner `a798027e-698e-4291-9874-dc7f34a01eac`.
The host exhausted 60 polls, refused `RecycleLandInFlight`, and left old drained.
The caller reports cancellation released the land. This is recorded incident
evidence, not a reproduction performed by this Plan task.

| Card assumption | What the inspected code does | Consequence |
|---|---|---|
| Deploy inherently registers as a source writer. | There is no separate writer registration. `AgentTaskService.cs:2482-2518` stores explicit workspace; fresh tasks default to Worktree regardless of role. An existing-agent selection defaults Shared. `AgentTaskDispatcher.cs:557-578,702-735` projects active Shared claims for dispatch. | The offending task's Shared choice, rather than Deploy role, creates the repository claim. Preserve the existing workspace model. |
| A Shared Deploy holds only a conflicting target. | `AgentTaskLandService.cs:1015-1045` reads Dispatched/Working/**Blocked** nonspecialists, matches the land source checkout first, then any Shared task's Git common directory. It does not inspect target ref or task intent. `:590-596` holds before admission; `:740-746` repeats after verification reacquires the lease. | Same-repository Shared Deploy holds both rc and master lands. Changing only one check is insufficient. |
| Existing pure writer tests establish the production behavior. | `AgentTaskLandService.cs:1216-1225` uses a simpler path-key predicate and omits Blocked. | New witnesses must run `AgentTaskLandService.RunAsync` through the real protocol; do not establish acceptance through this helper. |
| Land only pushes from its source worktree. | The schema-3 protocol uses a detached land worktree. `AgentTaskLandingProtocol.cs:494-535,558-569` subsequently scans HEAD registrations: it fast-forwards the main checkout when it holds the target, or updates the local target ref if checked out nowhere. Unknown/elsewhere/dirty identities retain residue. | A master land while main holds rc normally leaves rc files alone. An rc land can change canonical files; a concurrent Deploy checkout switch invalidates a one-time comparison. |
| Recycle understands a self-held land. | `deploy-server2.ps1:795-825` and `c590-remote.sh:6538,6586` refuse bound pending lands, without a holder exception. Timestamp-bearing land-list rows can refuse before detail is read. Wrapper `:891-921` and host `:6414-6435` retry only this refusal, normally 30 minutes. | Cycle: Shared Deploy -> pending bound land -> Shared Deploy. Status Held is still pending work, never recycle permission. |
| The long rollout needs the canonical desktop checkout. | `deploy-server2.ps1:44-67` only requires canonical source for `provision-host-jq`. Ordinary phases consume scripts/manifest SHA; `c590-real.ps1:101-109` selects it, and remote `c590-remote.sh:135-150` materializes a separate host deployment checkout. | The ordinary controller can use a pinned task worktree. The remote deployment checkout is separate from the desktop canonical checkout and task mirrors. |
| Restart can run from the controller tree. | `restart-apphost.ps1:88-94` refuses linked worktrees; `:177-208` binds expected SHA to source HEAD. `docs/orchestration-loop.md:28-37` forbids pulling during a land. | Keep canonical activation separate. No `-AllowWorktree`, restart from the isolated tree, or concurrent canonical checkout move. |
| Timeout clears the dangerous owner. | `AgentTaskDispatcher.cs:3745-3770` describes a 240-minute role ceiling that fails the task but deliberately leaves its session alive. `AgentTaskReplyService.cs:849-858` refuses refinement after settlement. | Failed is not process exit or checkout release. Do not solve this cycle by waiting for failure, canceling blindly, or increasing the ceiling. CARD-1610 owns terminal/live-session recovery. |

Paths above are relative to `server/Application/Services/` unless explicitly named.
CARD-1610 was read: its concrete example is a Review, not this Deploy. The Deploy's
240-minute failure and inability to answer after Failed are caller-supplied facts;
its transcript, exact ceiling receipt and cancel/refine responses were not obtained.
No desktop files or ended native Windows run were inspected.

## Decisions

| ID | Decision and reason | Rejected alternative / risk |
|---|---|---|
| D-1 | Use existing Worktree mode for the long rollout. Pin its initial source to the reviewed rc SHA, execute scripts from that owned checkout, retain the same owner across explicit phases. | **(a) Target-aware Deploy exemption:** off-target publication can be safe, but role alone proves neither read-only behavior nor a stable canonical HEAD. A checkout switch after admission or during verification can make the later canonical fast-forward conflict with Deploy. Sound implementation needs coordinated checkout ownership, not just a ref comparison. |
| D-2 | No new source-writer type or backend schema. Keep Shared claims, source-alias exclusions, repository leases, post-verification recheck and fail-closed unknown identity unchanged. | **(b) Typed rollout class:** useful only with enforceable lifetime/checkout-mutation rules, lease acquisition, restart custody and recovery. A descriptive enum that lets lands pass is an unsafe role exemption with more API/DB work. |
| D-3 | Bound Held/Queued/Running/NeedsResolution lands remain in flight. Add no census exemption. | **(c) Ignore a land held only by this Deploy:** it still cannot run until Deploy ends, so continuous lands remain broken. The holder may settle/fail while a live process survives; the land can become runnable during recycle. Multi-holder and stale-detail cases make an exception unsafe. A fast named refusal is useful; admission supplies it before waiting. |
| D-4 | **(d) Worktree plus separate canonical activation** is the selected fix. A linked worktree isolates files while existing land admission already permits its independent owner. Keep remote deployment checkout preparation under existing single-rollout ownership. | A separate clone adds credentials/cache/identity handling; a ReadOnly label is false for Git movement, evidence and deployment effects. A Worktree controller that still invokes canonical pull/restart would retain the race: forbid that in its dispatch contract. |
| D-5 | Require delegated mutation phases to prove their own task is Worktree and the invoked script root is its registered checkout. Fail before any operator credential read, drain, SSH, Docker action, or 30-minute census wait. | A docs-only correction would allow the same explicit Shared invocation to recur. A new global prohibition on all Deploy/Shared tasks would break legitimate short canonical activation. |
| D-6 | Gate the five mutating rolling phases and `all`; existing restrictions on `all`, dry-run and resume remain. `check-census` and `check-host-jq` remain observational. `provision-host-jq` remains its existing canonical-only, separately commissioned short task. | Applying the Worktree rule to provisioning conflicts with its provenance contract; weakening that guard or allowing a worktree AppHost restart is out of scope. |
| D-7 | Keep interactive operator invocation compatible when neither task ID nor task token exists. If either exists, require a valid task ID, successful authenticated detail read, supported workspace/path evidence, and a live Dispatched/Working owner. Missing, mismatched, Blocked or terminal ownership refuses. | Silently treating an API/auth failure as interactive mode could conceal the original Shared claim. This is workflow admission using existing identity, not a new security sandbox against deliberate environment tampering. |
| D-8 | Finish any canonical preparation before commissioning the long controller. For later activation, first finish/join the controller and publish its receipt; wait for lands from the caller, then dispatch the short Shared activation. Within it mutate/build/verify and finish; never wait for pending lands while holding Shared. | A Shared activation that starts by waiting for queued lands recreates the cycle. New lands can queue during the short task and proceed after it finishes; a land already executing is protected by ordinary dispatch/repository admission. |
| D-9 | Current role ceilings, parking and session recovery are unchanged. Split operational dispatches at phase boundaries if duration risks the ceiling; preserve phase receipts and resume context. A Failed controller may still own live children: inspect/join them before replacement. | Raising timeouts, assuming Failed killed the session, or auto-releasing it belongs to CARD-1610 and would be unsafe incident recovery here. |
| D-10 | Plan review by a different company's model precedes separate TestDesign. The checkpoint roster below is a concrete proposed selection, not test execution evidence or authority to deploy. | Starting Code without the separate verification design and timing refinement would leave the production-path/race proof incomplete. |

## Implementation slices

**S1 — red tests first.** Add `tests/Antiphon.Tests/Scripts/DeployRolloutWorkspaceTests.cs`
and `tests/Antiphon.Tests/Application/DeployLandIsolationTests.cs`. Extend the needed
fixture transport in `scripts/fixtures/c727-fake-http.ps1` and `C1008WrapperFixture`
in `RollingVolumeRecycleScriptTests.cs`; explicitly clear inherited task identity
in legacy interactive fixtures. Feed owner facts through normal task-detail DTOs,
never a test-only workspace bypass. Reuse `LandingSafetyHarness`, `LandingGitFixture`,
`C1008WrapperFixture` and the existing production host census extraction harness.
Use private real Git worktrees for native identity. Commit/push the tests and their
red outcome before implementing the guard.

**S2 — controller admission.** Add `scripts/lib/rollout-controller.ps1`, called by
`scripts/deploy-server2.ps1` after pure argument checks but before operator-token
lookup or operational effects. GET `/api/agent-tasks/{ANTIPHON_TASK_ID}` with
existing task authentication. Validate `summary.id`, Worktree, live status,
registered `worktreePath`, native filesystem/Git root identity, clean tracked
source and `HEAD == -Sha`. Reject wrong/unregistered trees and unknown identities;
compare canonical identities, not path prefixes. The actual controller is native
Windows, not a mirror whose registered path names a different machine. Preserve
D-6 observational/provision exceptions and D-7 interactive compatibility.

Use fixed reasons `RolloutControllerWorkspaceRequired`,
`RolloutControllerIdentityUnknown`, `RolloutControllerSourceMismatch`; name the
validated task and a fresh Worktree controller after canonical preparation as
recovery. Write ignored `rollout-controller.json` under the invocation evidence
root: phase, task ID, workspace, source SHA, identity result, outcome and elapsed
milliseconds. Never retain token, full DTO, task prose or raw exception. Receipt
failure refuses. Self-read and Git probes have finite deadlines. No new API,
indefinite retry, modal prompt, land predicate or census exception.

**S3 — operating contract.** Update `docs/docker-stack.md` continuous dispatch and
phase/source notes, `docs/orchestration-loop.md` Shared activation guidance, and
`docs/apphost-runbook.md` source notes. Show fresh
`-Role Deploy -Worktree -StartRef <reviewed-rc-sha> -Platform Windows`; omit `-Runner`,
`-OnAgent` and `-Agent`. Keep its assigned branch and source frozen through phases;
commit narrative evidence after operational SHA checks finish. Pass a proven
`-ProjectId` if canonical origin URL cannot resolve the worktree's project. No
prose-only tests. Use a single rollout owner outside the runner being recycled.

Canonical-only steps: rc pull/checkout, AppHost build/restart, desktop runner
restart when needed, and `provision-host-jq`. The controller tree supports
`check-census`, `check-host-jq`, deploy-temp, drain-old, redeploy-old, smoke/cache
qualification, drain-temp and retire-temp. Those steps never pull/restart AppHost.
Preserve the existing canary, continuous-dispatch and final retire gates.

## Red witnesses, classification and roster

Separate TestDesign must finish positive-control details and measured costs. Scope:
scripts plus server/git integration; no messaging/client/pty behavior changes, no
live AppHost, production runner, SSH or Docker in tests. Existing test-project
references to server/infrastructure suffice; no project extraction is justified.

| ID | Production-path witness and observable assertion | Classification |
|---|---|---|
| V-1 | Shared own-task DTO; invoke real wrapper for each mutating phase and allowed resume/dry-run. Refuse before operator-token access, census sleep or any traced effect. Assert fixed reason and zero effects. Old wrapper reaches an effect/wait or lacks the new reason. | New red/green admission. |
| V-2 | Real pinned linked worktree, matching live DTO and SHA succeeds. Wrong SHA, tracked dirt, foreign tree and alias to canonical refuse. Equivalent native case/separator identity follows host rules. | Red/green negatives with positive native controls. |
| V-3 | Invalid/missing ID, token without ID, 401/404/timeout, absent workspace/path, mismatched ID, Blocked/Failed owner and receipt failure refuse. Read checks, canonical provision guard and true interactive use retain outcomes. Sentinels never enter output/receipts. | Red/green plus compatibility. |
| V-4 | Real `AgentTaskLandService.RunAsync` and Git protocol: Shared Deploy in Dispatched/Working/Blocked holds rc and master requests with holder ID, no publication or extra attempt. Independent Worktree Deploy allows both lands while still Working; its source hash/HEAD remain fixed. | Existing-behavior regression/remedy witness, green at base. |
| V-5 | Same-source owner/alias and unknown identity still hold. Shared writer appearing while verification releases the repository lease blocks the real resume path. | Existing safety regression; not a pure predicate test. |
| V-6 | Bind land owner to recycled runner. Prove Shared cycle, then separate Worktree scenario: complete real land and feed production task/list/detail projections into wrapper and host HTTP fixtures. Actual census refuses/waits while pending and clears only after publication/terminal proof, controller still Working. Another genuine writer keeps recycle refused. | Integrated remedy, remote effects stubbed. |
| R-1 | Six `C1364BoundLandWaitProofTests` methods preserve cleared/expired waits before/after removal, ordinary/resume/preview, wrapper and host. | Unchanged census regression. |

Planned `DeployRolloutWorkspaceTests` (seven non-parameterized methods):
`C1615_Shared_controller_refuses_before_side_effects`,
`C1615_Worktree_controller_runs_from_its_pinned_checkout`,
`C1615_Unknown_or_terminal_owner_refuses`, `C1615_Native_checkout_identity`,
`C1615_Read_checks_and_canonical_provision_keep_their_contract`,
`C1615_Interactive_mode_requires_absent_task_identity`,
`C1615_Admission_receipt_is_bounded_and_redacted`.

Planned `DeployLandIsolationTests` (five non-parameterized methods):
`C1615_Worktree_deploy_allows_rc_and_master_publication`,
`C1615_Shared_deploy_holds_before_admission`,
`C1615_Source_checkout_owner_and_unknown_identity_hold`,
`C1615_Shared_writer_appearing_during_verification_holds`,
`C1615_Bound_census_clears_only_after_real_land_completes`.
Internal matrix vectors do not multiply executed counts. Split expensive matrices
only with updated roster/counts/cost before Code dispatch.

Red-first: run exact new negative methods V-1/V-2/V-3 at the S1 tests-only commit
using one isolated build. These are explicitly accounted runs outside the green
table. Capture expected assertion failures, never fixture/build errors or zero
tests. V-4/V-5/V-6 and R-1 are expected green at base because the selected fix relies
on existing isolation; they are not evidence that the absent guard went red.

### Checkpoints

Run once after S1-S3 are committed. CP-1 builds; CP-2/3 reuse the same output and
slice group. All rows initially execute in the Linux lane. A separate native
Windows task executes CP-1 only with its own isolated build for operational
wrapper/path branches; no prior desktop run counts. TestDesign encodes that
selection through the tool, without inventing a Platform table column.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows |
|---|---|---|---|---|---|---|---:|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1615/` | portable-native-controller | `/*/Antiphon.Tests.Scripts/DeployRolloutWorkspaceTests/*` | V-1, V-2, V-3 | all 7 named methods, 0 failed, 0 skipped | 7 | 6 | 9 |
| CP-2 | S1-S3 | CP-1 | portable-land-isolation | `/*/Antiphon.Tests.Application/DeployLandIsolationTests/*` | V-4, V-5, V-6 | all 5 named methods, 0 failed, 0 skipped | 5 | 4 | 6 |
| CP-3 | S1-S3 | CP-1 | portable-census-regression | `/*/Antiphon.Tests.Scripts/C1364BoundLandWaitProofTests/*` | R-1 | all 6 existing methods, 0 failed, 0 skipped | 6 | 2 | 3 |

Exact class filters avoid suffix expansion. Check declared names again in TestDesign
and actual fresh TRX in Code. If combining later, each OR operand must be parenthesized
and end in `*`; escape table pipes as `\|`. Import validates syntax, not existence or execution.

## Performance budget

No product tests run for this Plan task. Checkpoint importer validation only, through the host build-slot gate.
Final proposed costs will distinguish build, admission, shared database setup, script/process work,
test body and cleanup. No measured p95 is available yet; provisional costs must be refined by
TestDesign under CARD-1615 and `docs/test-performance-standards.md`, with approximately 2x measured
p95 timeouts and Debug escalation for any selection or complete run over 30 minutes.

## Placement and publication

Live GET `/api/runner-defaults` (revision 2) and `/api/session-runners` succeeded on
2026-10-30. Portable implementation/tests use the default eligible runner, without
`-Runner` or `-Platform`. Native Windows filesystem/activation qualification uses
`-Platform Windows`, without pinning a host; refresh both routes before dispatch.
Actual rollout control uses the Windows operational lane with existing operator-token
and SSH access, outside the recycled runner; the Plan runs on Linux. No fleet address
belongs in a checkpoint or new helper.

This branch only publishes the plan. Product changes must first be reviewed on the
actual target line. The inspected source is rc; the caller chooses and commissions the
rc fix and a separate master port. Record exact dependencies/SHAs for each (including
CARD-1353 census behavior), review the resulting trees and run their affected checks.
Master publication is not activation. Product hosts deploy only a reviewed current rc
SHA; verify canonical HEAD, `/api/version`, runner `buildVersion` and staged smoke/Both.
No tags, target pushes, deploy, rc rebase or desktop-run credit are part of this task.
