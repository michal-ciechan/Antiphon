# CARD-1615: isolate rollout control from canonical activation

Plan only. Round-2 base `f9bf72bd500b30d7dc7fae32971a2c8c83923c69` (rc source
plus round-1 plan and design review), branch `feat/card-task-28be9547`, inspected
2026-10-30. No product edits or rollout. Round-1 source base was
`ade54222543381e2e1b31ad79d8ae33a4035f70b`.

## Round-2 revision ledger

The three corrections below are complete in separate pushed slices, ready for
another design review:

1. Review D-1: classify task, standing-agent and interactive callers independently;
   preserve the standing orchestrator's authorized rollout path and test each class.
2. Review D-2: name rollout SHA X and canonical activation SHA, prohibit canonical
   restarts during an active controller, and refuse version skew at phase admission.
3. Review D-3: replace the conflicting queued-land wait, canonical rollout source,
   broad worktree refusal and stale Shared-default instructions by exact location.

CARD-1609 landing order, CARD-1610 phase-boundary behavior, receipt retention and
Windows operational prerequisites are specified below. Verification design is
folded into this revision under the requested Review-then-Code handoff. Import
results below distinguish historical round-1 evidence from this revision.

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
| A token always means an AgentTask owner. | `AgentSessionLaunchComposer.cs:43-50` emits token + agent ID without task ID for standing agents. Assigned-agent card launches use that composer (`CardService.cs:1683`, `OrchestratorService.cs:941`). The unassigned-card credential factories at `:1791` and `:976` emit a token alone. | Classify the actual environment; standing sessions are absent from `FindWriterAsync`'s AgentTasks query. Token-only card sessions need an explicit recovery path, not a false assertion that all card sessions have an agent ID. |
| A successful canonical restart preserves the rollout SHA. | `restart-apphost.ps1:177-208` defaults expected SHA to the canonical source HEAD; `AgentTaskLandingProtocol.CanonicalAdvanceAsync` can fast-forward that checkout during rc lands. `restart-session-runner.ps1` has no `ExpectedServerSha` parameter. | An X-to-Y canonical advance can turn an ordinary restart into an unintended activation of Y. Name X explicitly at preparation and prohibit AppHost/desktop-runner restarts during the controller lifetime, including between phases. |

Paths above are relative to `server/Application/Services/` unless explicitly named.
Round 1 read CARD-1610: its concrete example is a Review, not this Deploy. The Deploy's
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
| D-5 | Require TASK-class mutation phases to prove their own task is Worktree and the invoked script root is its registered checkout. D-7 separately admits standing and interactive callers. Fail before any operator credential read, drain, SSH, Docker action, or 30-minute census wait. | A docs-only correction would allow the same explicit Shared invocation to recur. A new global prohibition on all Deploy/Shared tasks would break legitimate short canonical activation. |
| D-6 | Gate the five mutating rolling phases and `all`; existing restrictions on `all`, dry-run and resume remain. `check-census` and `check-host-jq` remain observational. `provision-host-jq` remains its existing canonical-only, separately commissioned short task. | Applying the Worktree rule to provisioning conflicts with its provenance contract; weakening that guard or allowing a worktree AppHost restart is out of scope. |
| D-7 | Classify TASK first whenever task ID is present; STANDING AGENT only with token + valid agent ID and no task ID; INTERACTIVE only with all three absent. Admit standing agents without inventing a task record or requiring a Worktree DTO. Apply the class-specific rules below. | A token is not a task ID. Requiring a task record from a standing orchestrator breaks its authorized direct rollout path. Partial/invalid identity never falls back to interactive. This is workflow admission using launcher identity, not a sandbox against deliberate environment tampering. |
| D-8 | Preparation activates a named reviewed rc SHA X before the controller starts. Freeze **activation**, not land publication: prohibit canonical AppHost and desktop-runner restarts while the controller owns an unfinished rollout, including between phases. Each mutating invocation requires `/api/version.sha == -Sha == X` before effects. Remove the unqualified "later activation" step; after joining/completing X, a separately commissioned preparation may target a newly named reviewed Y. | Rc lands may advance canonical HEAD to Y while X runs; they remain allowed. Default-HEAD restart would silently activate Y, so health alone cannot admit the next phase. A Shared activation never waits for Held/Queued lands; a Running land is already protected by the dispatch repository lease. |
| D-9 | Current role ceilings, parking and session recovery are unchanged. Identity admission is once per phase invocation. A task failed by its ceiling mid-phase leaves the admitted command running to its existing bounded outcome; its Worktree cannot hold unrelated lands. The next invocation refuses its terminal owner. Preserve receipts and join actual children before a replacement owner. | Do not add owner polling inside a phase or mistake Failed for command exit. CARD-1610 owns terminal/live-session recovery. A phase-boundary successor retains X and the restart prohibition until the whole rollout is complete or explicitly handed off from a proven quiescent state. |
| D-10 | This dispatch requests revised-plan Review, then Code; verification design is therefore included here. Review evaluates the caller/SHA/doc corrections and executable test design; Code collects the bounded provisional timing samples. | No separate TestDesign is left as an unstated prerequisite, and no product test execution or deployment authority is claimed by plan approval. |
| D-11 | Order the overlapping work **CARD-1609 first, CARD-1615 second**. CARD-1609 plan round 2 is task `02c3c411` per this dispatch. Declare `scripts/deploy-server2.ps1` in Code scope and defer overlapping Code; implement this admission hook against the landed CARD-1609 retire-temp layout, recording its exact SHA. | Parallel edits or assuming today's line numbers survive that repair can lose the new retirement safety. This is the required scheduling order, not a claim that the other task has landed or agreed by message. |

### Caller admission (review D-1)

Presence means a nonempty environment value; whitespace-only supplied values are
malformed, not absent. Evaluate these cases before reading operator credentials:

| Caller class | Rule | Own test / refusal evidence |
|---|---|---|
| TASK: `ANTIPHON_TASK_ID` present, even when agent ID is also present | Require valid GUID, token, authenticated own-task detail with matching `summary.id`, Worktree, Dispatched/Working status and registered native checkout/branch. Shared refuses `RolloutControllerWorkspaceRequired`; unknown/missing/mismatched/Blocked/terminal owner refuses `RolloutControllerIdentityUnknown`. No fall-through on failure. | `C1615_Shared_controller_refuses_before_side_effects` and `C1615_Unknown_or_terminal_owner_refuses`, including ID + agent + token precedence and missing token. |
| STANDING AGENT: no task ID; token + valid `ANTIPHON_AGENT_ID` | Admit direct orchestrator execution, with no task-detail request and no synthetic task/Shared claim. Require clean pinned script source in a non-main linked controller checkout; no task registry/assigned-branch proof applies. Record `callerClass=StandingAgent` and agent ID. Retain ordinary authenticated API and operator-token gates. | `C1615_Standing_agent_without_task_id_is_admitted` uses the composer's exact environment shape, asserts admission receipt/class and zero task-detail requests, then reaches the stubbed phase boundary. Malformed agent ID refuses. |
| INTERACTIVE: no task ID, token or agent ID | Preserve interactive invocation without an owner GET; record `callerClass=Interactive`. Existing operator/phase guards remain. The operating recipe still uses a pinned controller checkout. | `C1615_Interactive_mode_requires_absent_task_identity` asserts explicit receipt classification and phase reachability; lone agent ID/token never becomes interactive. |

Token-only card sessions are a real fourth **incomplete identity shape**, not a
fourth admitted class. Refuse `RolloutControllerIdentityUnknown` before effects and
tell that caller to commission a fresh Worktree Deploy or use its assigned standing
agent's controller. Do not strip environment identity or fabricate an agent/task ID.
This preserves the standing orchestrator path while making the unassigned-card
migration explicit. No launch-composer or backend change is required.

### Activation and phase SHA contract (review D-2)

1. The caller names full reviewed rc SHA **X**, completes any prerequisite lands
   before commissioning Shared preparation, and records the currently active SHA
   **P** separately. The short canonical task advances to X using the existing
   supported pull/checkout procedure, verifies clean tracked source and exact
   `HEAD == X`, and calls `restart-apphost.ps1 -ExpectedServerSha X`. If the checkout
   is already Y or the rc tip changes before preparation, refuse/recommission with
   a newly reviewed named SHA; never reset/rebase a published rc to manufacture X.
   Require `/api/version.sha == X` and health after restart. Any needed desktop
   runner build/restart and `provision-host-jq -Sha X` happen here, before the
   controller, with HEAD X; verify runner `buildVersion` against X. There is no
   invented `-ExpectedServerSha` parameter on the runner restart script.
2. The long controller uses X for every phase, receipt and resume. Lands may
   advance canonical HEAD X -> Y without activating Y; canonical HEAD and the
   running API may intentionally differ in this window. Never pull/switch/build
   or restart the canonical stack from the controller. The restart prohibition
   applies to unrelated orchestrators and recovery restarts too, from the first
   mutation until joined completion including retire-temp, not just while a
   command is running. No new global lock/service is introduced: this is an
   operating contract plus a mechanical phase-entry skew check.
3. For all admitted caller classes, GET `/api/version` once per mutating invocation
   before operator-token lookup or effects. Require a full 40-hex `sha`, normalize
   case, and compare with X. A different SHA refuses loudly as
   `RolloutControllerServerShaMismatch`; missing/malformed/unreadable/401/timeout
   refuses `RolloutControllerServerVersionUnknown`. Print only reason and bounded
   expected/observed SHA; save both in `rollout-controller.json`. Never substitute
   the observed SHA, refresh X to current rc, wait for a match, or retry the phase.
   Identity and version probes each have a deadline within one admission budget.
   `check-census`, `check-host-jq` and canonical provisioning retain their existing
   contracts; the new version guard applies to the five mutating phases and `all`.
   `all` is still forbidden by the operating recipe; if invoked through its existing
   script API, its initial check precedes credentials and version is checked again
   before each subsequent subphase (no owner recheck within an admitted phase).
4. If a restart is needed before completion, stop starting phases and establish
   the owned command's bounded outcome/exit and a quiescent receipt. Do not restart
   automatically or claim completion. Caller commissions an explicit recovery or
   new rollout with a named SHA after reconciling both runner images and resume
   state; a task status change alone does not release the restart prohibition.
   Unexpected mid-phase API drift is not detected continuously by this change;
   the next phase refuses. Existing phase/census refusals remain authoritative.
5. After joined completion at X, record active server/runner SHAs and retained
   receipt paths. A later upgrade is a new preparation targeting explicit reviewed
   **Y**, with `-ExpectedServerSha Y`, followed by its own rollout Y. There is no
   automatic post-drain restart to "whatever HEAD is now".

## Implementation slices

**S1 — red tests first.** Add `tests/Antiphon.Tests/Scripts/DeployRolloutWorkspaceTests.cs`
and `tests/Antiphon.Tests/Application/DeployLandIsolationTests.cs`. Extend the needed
fixture transport in `scripts/fixtures/c727-fake-http.ps1` and `C1008WrapperFixture`
in `RollingVolumeRecycleScriptTests.cs`; explicitly clear inherited task identity
and agent identity in legacy interactive fixtures. Feed owner facts through normal task-detail DTOs,
never a test-only workspace bypass. Reuse `LandingSafetyHarness`, `LandingGitFixture`,
`C1008WrapperFixture` and the production wrapper/host census extraction in
`tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs`; adapt its helper visibility or
factor a test-only reusable harness if needed, without adding an admission bypass.
Use private real Git worktrees for native identity. Commit/push the tests and their
red outcome before implementing the guard.

**S2 — controller admission.** Add `scripts/lib/rollout-controller.ps1`, called by
`scripts/deploy-server2.ps1` after pure argument checks but before operator-token
lookup or operational effects. Classify D-7 first. Only TASK callers GET
`/api/agent-tasks/{ANTIPHON_TASK_ID}` with existing task authentication.
Validate `summary.id`, Worktree, live status,
registered `worktreePath`, native filesystem/Git root identity, clean tracked
source and `HEAD == -Sha`. Reject wrong/unregistered trees and unknown identities;
require a registered non-main linked worktree and its expected symbolic branch,
not just a DTO whose path happens to equal the main checkout. Compare canonical
identities, not path prefixes. New fixtures run the actual production script bytes
from their private registered worktree; they cannot bypass this proof. The actual controller is native
Windows, not a mirror whose registered path names a different machine. Standing
callers use the native clean pinned non-main source checks without a task DTO or
task branch comparison. Preserve D-6 observational/provision exceptions and
D-7 interactive compatibility; never query an empty task ID.

Add the D-8 version probe to the same helper and the existing offline HTTP seam,
before credential lookup. For the `all` loop, revalidate version at subsequent
`Invoke-Phase` entries. Fixture state must explicitly return `version.sha` matching
the invocation, plus mismatch/malformed/HTTP-failure vectors; no success default
that hides a missing production call. Preserve old observational fixtures.

Use fixed reasons `RolloutControllerWorkspaceRequired`,
`RolloutControllerIdentityUnknown`, `RolloutControllerSourceMismatch`, the two
D-8 version reasons, and `RolloutControllerReceiptUnavailable`; name the
validated task and a fresh Worktree controller after canonical preparation as
recovery. Write ignored `rollout-controller.json` under the invocation evidence
root: phase, caller class, nullable task/agent ID, nullable workspace, source SHA,
expected/observed server SHA, identity result, outcome and elapsed
milliseconds. Never retain token, full DTO, task prose or raw exception. Receipt
failure refuses with the fixed reason, without effects even when a file cannot
be saved. Self-read and Git probes have finite deadlines. No new API,
indefinite retry, modal prompt, land predicate or census exception.

**S3 — operating contract.** Update `docs/docker-stack.md` continuous dispatch and
phase/source notes, `docs/orchestration-loop.md` Shared activation guidance,
`docs/apphost-runbook.md` source notes, `AGENTS.md` and
`.claude/skills/antiphon-delegate/SKILL.md`. Show fresh
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

Replace these existing passages, rather than appending contradictory guidance
(line numbers refer to the round-2 source base):

| File / existing text | Required replacement |
|---|---|
| `docs/orchestration-loop.md:29-37`, "Before and after an AppHost pull or restart, verify that queued lands have finished" and "Wait for any land in progress before pulling" | "Before commissioning canonical preparation, the caller may wait for prerequisite lands. A Running land prevents Shared dispatch through the repository lease. Inside Shared preparation, never wait for Held or Queued lands, including those queued after dispatch: this task can itself hold them. Perform the named-SHA preparation, verify and settle; those lands then proceed. Never pull during a Running land. During rollout X, rc lands may advance canonical HEAD, but AppHost and desktop-runner restarts are prohibited until joined rollout completion or an explicitly commissioned quiescent recovery. Require API SHA X at each mutating phase; require HEAD and API equal the named activation SHA at preparation completion." Keep child-journal, lock and no-`AllowWorktree` rules. |
| `docs/docker-stack.md:473-474`, "From the canonical desktop checkout" for manual running-main cleanup | "From the pinned controller checkout on the Windows operational host, record main's status and exact candidates under that controller's printed rollout receipt path." Keep every existing running-main/ownership/publication/zero-work condition. The manual attested retirement path introduced by CARD-1609 also uses the controller tree; replace its canonical absolute evidence example with `<controller-root>/.antiphon/rolling-server2/manual-retire-<run>/`. This changes location, not cleanup authority. |
| `docs/docker-stack.md:513-514`, normal `retire-temp` "from the canonical desktop checkout" | "Run normal retire-temp from the pinned controller checkout with the same rollout SHA X and preserved resume context, after the phase identity/version admission checks." Preserve offline publication audit and strict volume proof. |
| `AGENTS.md:55`, "Restart and deploy scripts refuse ... when run from a linked Git worktree" | "`scripts/restart-apphost.ps1` and `scripts/deploy-local.ps1` refuse linked worktrees by default because they control the shared local stack. Their source is the canonical checkout. `scripts/deploy-server2.ps1` runs its rolling phases from the pinned controller worktree; only `provision-host-jq` requires canonical source. Desktop runner restarts remain canonical operations by policy; do not claim its script implements a linked-worktree guard. Never use `-AllowWorktree` for rollout preparation." |
| `.claude/skills/antiphon-delegate/SKILL.md:184` and following Shared-default bullets | "Fresh Workers and Orchestrators default to Worktree regardless of role or `-Dir`. An explicit existing-agent selection (`-Agent` / agentId) defaults Shared; explicit workspace and follow-up rules still apply. Use a fresh Worktree task for long rollout control; use an explicitly Shared, bounded task only for canonical preparation, with no pending-land wait inside it. Settlement commits the task's own footprint unless `-NoCommit`." Remove the stale implication that Shared is the normal fresh-task default. |

Also replace the opening docker-stack recipe's moving "current rc tip" with
"reviewed rc tip X selected and activated at preparation; keep that exact X for
this entire rollout". In its `cause=Http status=400` recovery at `:51-53`, require
prerequisite activation before gate 1, or quiescent recovery under D-8 if discovered
mid-rollout. Link the no-restart window and `-ExpectedServerSha X/Y` contract from
`docs/apphost-runbook.md`'s normal restart examples, so an unrelated recovery caller
sees it. Update the direct-orchestrator recipe to D-7's standing-agent environment
and controller checkout, not a made-up task ID. Manually review these operating
instructions; do not add tests that merely grep prose.

Keep controller receipts under `.antiphon/rolling-server2/<run>/`. Existing
`server/Infrastructure/Git/FileSystemEvidenceRetention.cs` copies and hash-verifies
evidence before managed task-worktree cleanup; the classifier includes
`**/.antiphon/**` (`docs/orchestration-loop.md:166-171`). The controller's report
must carry the printed original paths and subsequent retained paths when available,
since cleanup removes the original copy. A standing/interactive unmanaged controller
tree has no automatic task-cleanup retention promise: retain its receipts explicitly
before retiring that owned tree. No raw receipt directory is force-added to Git.

An existing Shared rollout cannot be converted by changing a label. At a proven
quiescent phase boundary, retain receipts, finish/join its owned command and settle
the controller; let pending lands finish, then commission the isolated controller
with the recorded resume context. A Failed-but-live session needs CARD-1610's
supported ownership/recovery assessment; no automatic cancellation or kill is
part of this repair. Merely installing this guard does not release an old owner.

## Verification design

Verification design is included for the explicit Review-then-Code handoff. Scope:
scripts plus server/git integration; no messaging/client/pty behavior changes, no
live AppHost, production runner, SSH or Docker in tests. Existing test-project
references to server/infrastructure suffice; no project extraction is justified.

| ID | Production-path witness and observable assertion | Classification |
|---|---|---|
| V-1 | Shared own-task DTO; invoke real wrapper for each mutating phase and allowed resume/dry-run. Refuse before operator-token access, census sleep or any traced effect. Assert fixed reason and zero effects. Old wrapper reaches an effect/wait or lacks the new reason. | New red/green admission. |
| V-2 | Real pinned linked worktree, matching live DTO/source/API SHA succeeds. Wrong source SHA, tracked dirt, foreign tree and alias to canonical refuse. Canonical HEAD moving X -> Y with API X still admits controller X; API Y refuses X before credentials/effects. Missing/malformed/API-failure version refuses, including next-phase drift and subsequent `all` subphases. Equivalent native case/separator identity follows host rules. | Red/green negatives with positive native controls. |
| V-3 | Each D-7 caller class has its own method: task ID takes precedence over agent ID; token + agent/no task is admitted with no task GET; all-absent interactive remains admitted. Assert new receipt class for both admitted paths (red at base). Invalid/partial identity including token-only card sessions, task 401/404/timeout, absent workspace/path, mismatched ID, Blocked/Failed owner and receipt failure refuse. Read checks and canonical provision retain outcomes. Sentinels never enter output/receipts. | Red/green plus compatibility. |
| V-4 | Real `AgentTaskLandService.RunAsync` and Git protocol: Shared Deploy in Dispatched/Working/Blocked holds rc and master requests with holder ID, no publication or extra attempt. Independent Worktree Deploy allows both lands while still Working; its source hash/HEAD remain fixed. | Existing-behavior regression/remedy witness, green at base. |
| V-5 | Same-source owner/alias and unknown identity still hold. Shared writer appearing while verification releases the repository lease blocks the real resume path. | Existing safety regression; not a pure predicate test. |
| V-6 | Bind land owner to recycled runner. Prove Shared cycle, then separate Worktree scenario: complete real land and feed production task/list/detail projections into wrapper and host HTTP fixtures. Actual census refuses/waits while pending and clears only after publication/terminal proof, controller still Working. Another genuine writer keeps recycle refused. | Integrated remedy, remote effects stubbed. |
| R-1 | Six `C1364BoundLandWaitProofTests` methods preserve cleared/expired waits before/after removal, ordinary/resume/preview, wrapper and host. | Unchanged census regression. |

Planned `DeployRolloutWorkspaceTests` (nine non-parameterized methods):
`C1615_Shared_controller_refuses_before_side_effects`,
`C1615_Worktree_controller_runs_from_its_pinned_checkout`,
`C1615_Unknown_or_terminal_owner_refuses`, `C1615_Native_checkout_identity`,
`C1615_Read_checks_and_canonical_provision_keep_their_contract`,
`C1615_Standing_agent_without_task_id_is_admitted`,
`C1615_Phase_requires_rollout_server_sha`,
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

Red-first: at the pushed S1 tests-only commit, build once into fresh
`bin-c1615-red/` and run seven exact-method rows, one result each, in the Linux
lane using a scratch checkpoint manifest. Each filter is
`/*/Antiphon.Tests.Scripts/DeployRolloutWorkspaceTests/<exact-method>`:

- `C1615_Shared_controller_refuses_before_side_effects` (V-1).
- `C1615_Native_checkout_identity` (V-2).
- `C1615_Phase_requires_rollout_server_sha` (V-2).
- `C1615_Unknown_or_terminal_owner_refuses` (V-3).
- `C1615_Standing_agent_without_task_id_is_admitted` (V-3).
- `C1615_Interactive_mode_requires_absent_task_identity` (V-3).
- `C1615_Admission_receipt_is_bounded_and_redacted` (V-3).

These are **seven expected assertion failures**, not seven green results or PCs.
The two admitted caller classes go red on their missing new classification
receipt, not on a requirement to reject them. Stage all assertion/effect traces
before reading a possibly absent receipt so a missing file is reported as the
intended assertion, not a fixture error. The Shared vector must demonstrate the
old wrapper reaching its stubbed effect without accessing a real credential.
Commit the tests first, retain red receipts against that SHA, then commit/push S2
and S3 before the green run. V-1..V-3 are the only red-first witnesses.
V-4/V-5/V-6 and R-1 characterize existing behavior and are expected green with
unchanged landing/census production code. They do not prove the absent guard red.

V-6's Shared leg must run real `AgentTaskLandService.RunAsync`, capture its actual
Held request and projected task/list/detail response, and pass those projections
to the extracted production wrapper and host census functions with their real
refusing/waiting mode. Reuse `C1361CensusProbes`' AST/host-function extraction and
stub only reads, bounded waits and effects. This characterizes the old cycle
without bypassing the new top-level admission; `check-census -Report` alone is
insufficient because it reports pending counts instead of refusing. Never seed Held as the
assertion's premise. In the separate Worktree leg assert remote ref publication,
terminal request proof, controller still Working, unchanged source HEAD, and only
then the census clearing. V-3 also changes the owner fixture to Failed after the
first admitted effect: assert one owner read, bounded phase completion, and refusal
on the next invocation. Use a barrier in V-5 at the real verification lease
release/resume boundary, not sleeps or the simplified writer predicate.

New tests are integration/script tests in the server/git-operations categories;
use the existing assembly limiter for each process-spawning method. Native
identity vectors use actual filesystem/Git roots on each lane, including native
case/separator aliases; host-inapplicable cases are named vector omissions, not
skipped methods. No live stack, SSH or Docker action is allowed in these fixtures.

Pending post-land method-scoped positive controls (not ordinary Code runs): remove
only the Shared refusal and run `C1615_Shared_controller_refuses_before_side_effects`;
route standing identity into the task branch and run
`C1615_Standing_agent_without_task_id_is_admitted`; suppress the SHA mismatch
refusal and run `C1615_Phase_requires_rollout_server_sha`. Each control uses
`/*/*/DeployRolloutWorkspaceTests/<exact-method>`, must fail its intended assertion,
then is restored and that same method runs green. They touch the same helper, so
run sequentially; retain individual evidence. No mutation of land admission or
census is commissioned by this plan.

### Checkpoints

Run once after S1-S3 are committed. CP-1 builds; CP-2/3 reuse the same output and
slice group. All rows initially execute in the Linux lane. A separate native
Windows task executes CP-1 only with its own isolated build for operational
wrapper/path branches; no prior desktop run counts. Code selects Windows with
`--rows CP-1`, without inventing a Platform table column. These are the closed
ordinary green selections; red-first selections and optional bounded calibration
above/below are the only extra authorized executions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1615/` | portable-native-controller | `/*/Antiphon.Tests.Scripts/DeployRolloutWorkspaceTests/*` | V-1, V-2, V-3 | all 9 named methods, 0 failed, 0 skipped | 9 | 6 | 9 | true |
| CP-2 | S1-S3 | CP-1 | portable-land-isolation | `/*/Antiphon.Tests.Application/DeployLandIsolationTests/*` | V-4, V-5, V-6 | all 5 named methods, 0 failed, 0 skipped | 5 | 4 | 6 | true |
| CP-3 | S1-S3 | CP-1 | portable-census-regression | `/*/Antiphon.Tests.Scripts/C1364BoundLandWaitProofTests/*` | R-1 | all 6 existing methods, 0 failed, 0 skipped | 6 | 2 | 3 | true |

Exact class filters avoid suffix expansion. Check declared names again when Code
adds them and verify actual fresh TRX. If combining later, each OR operand must be parenthesized
and end in `*`; escape table pipes as `\|`. Import validates syntax, not existence or execution.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and its owner
`docs/test-performance-standards.md`. This Plan executes no product tests; only
an isolated, slot-gated checkpoint-tool bootstrap/import validates the document.
The proposed acceptance total is **20 executed results**: 9 script, 5 integration,
6 census regression. Windows CP-1 adds 9 native results, reported separately.

| Lane / selection | Expected elapsed, including build | Phase allocation | Provisional execution deadline |
|---|---:|---|---:|
| Linux CP-1 | 6 min | build/restore 3; script/Git/HTTP fixtures 2.5; receipt/cleanup 0.5 | row 6 min; build floor exception below |
| Linux CP-2 | 4 min, reused build | shared DB readiness/template 1; clones/seed 0.5; Git/land/census 2; cleanup 0.5 | row 8 min |
| Linux CP-3 | 2 min, reused build | process startup/body 1.5; receipts/cleanup 0.5 | row 4 min |
| Linux ordinary batch | 12 min | one build, serial rows | charged execution 24 min; admission 5 min; hard wall 29 min |
| Native Windows CP-1 | 9 min | build/restore 5; native script/path fixtures 3.5; cleanup 0.5 | build/row 10 min override; charged 20 min; admission 5 min; hard wall 25 min |
| Linux S1 red batch | 7 min | one build 3; seven serial exact-method rows 3.5; cleanup 0.5 | each row 2 min; charged 18 min; admission 5 min; hard wall 23 min |

**Measured p95: unknown, sample n=0 for these proposed selections.** These are
planning estimates, not measured costs or a compliance claim. CARD-1615 carries the
bounded calibration obligation: Code first looks for comparable retained
receipts; Code collects per-method/phase samples from required red/green runs and,
where needed, at most five targeted calibration executions of the slow new methods.
Do not repeat the full table solely for timing. Record sample count/source/host/load,
then set per-test/row/build bounds to about 2x measured p95, rounded to practical
units. Until calibrated, new script methods have a provisional 90-second cap and
new land methods 120 seconds; a matrix that cannot fit must split. Any retained
exception needs measured evidence and its tracking card. Existing long class
attributes are not permission to copy generous bounds.

Code imports YAML and sets `build.slotWaitMinutes: 5`, serial rows,
`timeouts.rowMinutes: 4` and CP-specific `timeoutMinutes: 6`, `8`, `4` for Linux.
Use `run <manifest.yaml> --rows CP-1,CP-2,CP-3 --serial --total-timeout 24
--hard-wall-timeout 29 --expected-source-sha <committed-code-sha>` through the
checkpoint tool; the tool owns its build slots. For native Windows CP-1 use its
own fresh build and `--rows CP-1 --row-timeout 10 --total-timeout 20
--hard-wall-timeout 25`. Wait on the printed run ID until exit is not 75; an
observer ending is not executor completion. Use one run per committed slice group.
For the S1 scratch manifest use seven exact rows sharing its one build, each
`minExecuted: 1`, `timeoutMinutes: 2`, `serial: true`, and the red batch total/wall
bounds above. Keep generated manifests/receipts ignored and report exact source SHA.

**Known tool limitation / CARD-1615 provisional exception:**
`RunScheduler.BuildDeadlineMinutes` floors a scheduled build at 15 minutes even
when row deadlines are smaller. It has no independent build-timeout field.
The Linux/red manifests therefore retain that 15-minute build deadline bounded
by their charged/hard-wall totals; do not claim an unsupported six-minute build
pin. Windows uses the supported ten-minute `--row-timeout` override for both
build and CP-1 (a provisional ten-minute row exception to its eight-minute target).
Code records actual build/row p95 and sample provenance and reports remaining
tool constraints under CARD-1615; do not widen these limits or modify the scheduler
in this card. These provisional exceptions are not measured performance compliance.
The importer defaults (`max(15, 3 * EstimatedMinutes)`) do **not** satisfy R5; its
successful import is not approval of those default timeouts. Keep build time and
slot wait separate from test-body time. Slot timeout is not run, never bypass.

Share Postgres/template migrations once per test assembly invocation and clone
per test through the existing `TestDbFixture.CreateIsolatedSchemaAsync` contract
(the historical name now clones a database). Never migrate or start containers per
vector. Reuse immutable Git seed data, with independent owned worktrees for mutation
isolation. Process-spawning tests take the assembly-local `ParallelLimiter<ProcessSpawnLimit>`;
run checkpoint rows serially and do not co-schedule Pty/FakeClaude. No real web host
is required: use production services with isolated database and fake external effects.

Retain ignored TRX, checkpoint source/build receipts and phase JSONL under a unique
`.antiphon/checkpoints/card-1615/<run>/` root. Join by run/test/group, SHA, OS, host
and category; record Docker/readiness (not applicable here unless shared DB startup
requires it), template/migration, clone/seed, Git/filesystem, HTTP, process startup,
body, teardown and wall remainder. Unknown is null, never zero. Time each owner GET,
Git probe, census read and process cleanup separately. Provisional admission probes:
one owner GET and one version GET each capped at 5 seconds, each Git probe at
5 seconds, whole admission 15 seconds (individual deadlines share that remaining
budget); owned child cleanup has a separate bounded join. Code refines these
from measurements without widening existing recycle waits. Any selection/batch/run
expected or observed over 30 minutes triggers Debug with source/load/phase evidence;
no full-suite or rc qualification is added to this card.

## Plan validation and remaining evidence

The existing census roster was inspected: six declared methods, no argument expansion.
The two proposed classes do not yet exist and have no source-name/suffix collision;
the exact CP-3 class has no suffixed sibling. No OR expansion is used.

**Round-2 validation:** at `25530ecc98293f6c81e6721de2d0dffcc45120d2`, the
slot-gated isolated tool build finished in 5.13 seconds (5 warnings, 0 errors;
lease `16baf3a8-1af6-43ae-8cf5-20ca77adb25d`, waited 0 seconds, held 6 seconds).
Import exited 0 with 3 rows, one shared build, floors **9/5/6**, `serial: true`,
Linux estimates 6/4/2 and Windows estimates 9/6/3. Lease
`49f9a125-21fb-43dc-8acb-0c5b2bc22d60` waited 0 seconds and held 2 seconds.
Ignored output: `.antiphon/c1615-r2-plan-scratch/import.yaml`. Imported default
timeouts 18/15/15 require the explicit budget adjustments above before execution.
This is document validation, not execution of any of the 20 Linux or 9 Windows
results. `check-evidence-diff` at that tip reported 5 commits, 0 evidence entries,
0 violations. The final document was re-imported successfully (3 rows, exit 0)
to `.antiphon/c1615-r2-plan-scratch/import-final.yaml`; lease
`ae13a6f8-a7e5-457a-8081-6c1bad08b6ff` waited 0 seconds and held 4 seconds.

**Historical round-1 evidence, retained without crediting it to round 2:** at
`ff6b10c3264a60b959ce232b70be705bc37e638b`, the isolated checkpoint-tool bootstrap
completed in 6.90 seconds (5 compiler warnings, 0 errors; slot wait 0 seconds).
The importer accepted 3 rows with one build and floors 7/5/6, exit 0; the generated
YAML stays ignored at `.antiphon/c1615-plan-scratch/import.yaml`. The final Serial
annotation was re-imported successfully (3 rows, exit 0, slot wait 0 seconds), with
the final YAML at `.antiphon/c1615-plan-scratch/import-final.yaml`. These are document checks. Required product red,
green, timing and native Windows execution remain for Code; none is claimed.
No new input-waiting session is introduced. If an operational task waits for input,
parking remains disabled and there is no automatic release deadline; caller resolution
or separately authorized explicit stop/release must establish actual process exit.
The role ceiling and a terminal task status are not that proof.

Reproduce import after a slot-gated isolated tool build:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1615-r2-import-build -SlotWaitMinutes 5 -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj --property:OutputPath=bin-c1615-r2/ --property:UseAppHost=false -nodeReuse:false --verbosity minimal
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1615-r2-import -SlotWaitMinutes 5 -- dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1615-r2/ --property:UseAppHost=false -- import --plan docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md --out .antiphon/c1615-r2-plan-scratch/import.yaml
```

Delete only this task's owned `tools/Antiphon.Checkpoints/bin-c1615-r2/` and
`src/Antiphon.HostCleanup/bin-c1615-r2/` output directories afterward.

## Placement and publication

Live GET `/api/runner-defaults` (revision 2) and `/api/session-runners` succeeded on
2026-10-30. Portable implementation/tests use the default eligible runner, without
`-Runner` or `-Platform`. Native Windows filesystem qualification uses
`-Platform Windows`, without pinning a host; refresh both routes before dispatch.
The current catalogue has exactly one Windows runner, `desktop`, so this resolves
to the required operational host. Actual rollout control needs that desktop
account's owner-only operator-token file and SSH identity, outside the recycled
runner. If a second Windows runner appears, verify those prerequisites and pin
the qualified host with `-Runner` only when necessary; Windows alone is not the
credential/access proof. The Plan runs on Linux. No fleet address
belongs in a checkpoint or new helper.

This branch only publishes the plan. Product changes must first be reviewed on the
actual target line. The inspected source is rc; the caller chooses and commissions the
rc fix and a separate master port. Record exact dependencies/SHAs for each (including
CARD-1353 census behavior), review the resulting trees and run their affected checks.
Master publication is not activation. Product hosts deploy only a reviewed current rc
SHA; verify canonical HEAD, `/api/version`, runner `buildVersion` and staged smoke/Both.
No tags, target pushes, deploy, rc rebase or desktop-run credit are part of this task.

Round-2 publication preflight: fetched master
`98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`.
`git merge-tree --write-tree origin/master HEAD` returned exit 1; repeating it
against assigned base `f9bf72bd500b30d7dc7fae32971a2c8c83923c69` returned the same
four conflicts: `.antiphon/task-af2b78c8.md`,
`docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`,
`server/Application/Services/CapacityRecoveryService.cs`, and
`tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs`. These are inherited
rc/master divergence. The round-2 task delta is this one revised plan file, with clean
`git diff --check`; it is reviewable, but the whole rc-derived branch is not
declared directly landable on master. Caller-owned publication must preserve
that distinction; no merge, rebase or conflict edits were performed here.
