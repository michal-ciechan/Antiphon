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

Source analysis and decisions are complete. Detailed slices, red witnesses, checkpoint
manifest and timing budget follow in the next committed section.

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
