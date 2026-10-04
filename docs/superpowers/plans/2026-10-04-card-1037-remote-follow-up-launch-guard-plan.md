# CARD-1037: refuse unlaunchable remote pool follow-ups at Create

Date: 2026-10-04. Plan task: `df74a022-2c1f-4b50-8f05-8f15bcef6c8a`.
Inspected source: `3f1c52e578bc4a5ddc4a566d8b053572cec114bd`.
Complexity: **easy**; verification design is folded into this Plan.
Code budget: **35-45 minutes**, with a 60-minute ceiling before reporting an
unexpected dependency. Next implementation stage: **Code**.

## Outcome and acceptance

Choose the card's explicit-refusal option. A follow-up naming an extant remote
pool delegate must receive HTTP 422 before a new task is inserted. The response
names the predecessor and runner, explains that remote pool continuations cannot
reuse that process, and directs the caller to a fresh Worktree task at a chosen
published commit. Do not relax the remote Worktree launch or pool ownership guards.

This is an admission fix, not implementation of remote same-session continuation.
Local follow-up admission, remote standing-agent placement, and retired-agent
Worktree continuation keep their current behavior. No schema, dispatcher,
session-input, runner, or settlement change is required.

## Ground truth

The card was read with `scripts/card.ps1 get CARD-1037 -Board Antiphon`
(card `7a95401a-b216-4ba4-848b-68dd10e71de7`, revision count 0).
The prior investigation was read from Git object
`dde45da524a0db70c659c3397626efd4b01b9892`, path
`docs/investigations/2026-10-04-card-1029-warm-reachability.md`; it is not present
in this task's starting tree. Its production observations are historical evidence,
not tests executed by this Plan.

| Card assumption | What inspected code/evidence actually does | Design consequence |
|---|---|---|
| A remote follow-up can be persisted as Shared. | `AgentTaskService.CreateAsync`, lines 417-547: an extant predecessor agent sets `liveFollowUp`, retains agent runner (falling back to predecessor runner), refuses explicit Worktree, and rewrites omitted/Shared workspace to Shared. Explicit ReadOnly remains ReadOnly. | Guard this existing-agent branch before rewriting the request. |
| Create already validates remote workspace. | Lines 1200-1224 validate the explicit request runner. Lines 1253-1263 restore the inherited runner only afterward when runner was omitted. | The explicit guard cannot catch the omitted-runner producer. Use the retained process binding, not request/default placement. |
| Every Shared remote row can never run. | This is too broad: `AgentTaskDispatcher.TryReuseWarmAgentAsync`, lines 6565-6568, sends non-pool standing agents to their own placement path. `TaskPlatformPlacementTests.Existing_process_is_never_relocated` admits that remote standing follow-up. | Restrict the new refusal to pool delegates; preserve the standing path. This test proves admission/placement metadata, not recipient delivery. |
| Real remote Worktree delegates can be reused as Shared. | Dispatcher creates pool agents in task worktrees. `TryReuseWarmAgentAsync`, lines 6573-6581, refuses Shared reuse of a pool delegate in a task-worktree directory. ReadOnly does not enter the ordinary Shared reuse branch. | Do not turn the new row into Worktree or remove the pool/path exclusion. Both would change ownership semantics. |
| Cold fallback can recover. | `PhoneHomeLaunchPolicy.RefuseUnsupportedStart`, lines 166-172, throws `phone_home_worktree_refused` for a remote delegated non-Worktree launch. | Refuse before persistence; retain this defense unchanged. |
| Fresh Create/Retry produces the problematic Shared state. | Explicit remote fresh Create requires Worktree; default placement excludes existing processes/non-Worktree shapes. Retry requeues the same workspace/runner. | Do not change fresh placement or retrofit old failed rows. |
| A predecessor without an agent needs the same refusal. | `RequireFrozenContinuationTipAsync` and the `followAgent is null` branch already produce a fresh Worktree from the proven prior tip. | Keep the guard exclusively in the extant-agent branch. |
| Five actual failures support the premise. | Investigation records five Shared rows with `ExistingProcess`, omitted requested runner, no session/dispatched timestamp, and failure: “Dispatch failed before a session existed: A runner-bound task must use a Worktree workspace.” | This is a confirmed defect; no additional production probe is necessary to plan the fix. |

Historical failed task IDs: `0cae04b9-3853-4f3f-b10d-304df67ff716`,
`0d9eaac8-7d00-430c-90ef-14d5a543c013`,
`bb0c5a65-9011-4d3e-b865-1ec19fb590b7`,
`c7359fcf-b9ce-476c-8a21-d91472f4a5bc`,
`ac06af26-d744-4862-9cc0-464cce9df564`.
The last followed Worktree task `b657a1e2-767d-4bcb-b25a-1cf2ed925be3` on
agent `95ea863d-e254-4f9b-8e07-d51965a9ea5e`. Historical agent pool/path behavior
was reconstructed from source and predecessor records; the investigation does
not claim a transcript-confirmed successful remote pool continuation.

## Decisions

- **D-1 — Reject remote pool follow-ups.** The card explicitly permits a clear
  up-front 4xx. Use `ValidationException` with field
  `nameof(CreateAgentTaskRequest.FollowUpOnTask)` and stable code
  `follow_up_remote_pool_unsupported`. Rejected: implementing inherited Worktree
  ownership, branch publication and same-session reuse in this small fix; those
  require a separate runtime design and delivery verification.
- **D-2 — Inspect the existing process binding.** Immediately after the existing
  explicit-Worktree conflict check in the live branch, reject when
  `followAgent.IsPoolDelegate` and
  `RunnerRequestIntent.CanonicalRunnerId(retainedRunnerId) is not null`.
  Reuse the existing retained-runner calculation (nonblank agent runner first,
  predecessor runner otherwise). Blank/null and case-insensitive `local`/`desktop`
  are local. Do not ask runner availability/configuration whether the binding is
  remote: a disabled or unknown remote host remains remote. Do not let an explicit
  local request override the actual remote process. Rejected: moving all explicit
  remote admission checks after selection, which would also reject supported
  standing-agent placement and retired continuations.
- **D-3 — Preserve existing validation precedence.** Launch-env, Blocked-agent,
  kind-mismatch and explicit-Worktree checks remain ahead of the new guard.
  Explicit Worktree still returns `workspace_existing_agent_conflict`; omitted,
  Shared and ReadOnly requests reach the new refusal. Reject all remote pool
  continuations, independent of provider or directory spelling, rather than
  selectively admitting undocumented retained pool states.
- **D-4 — Preserve the non-pool boundary.** Remote standing agents keep their
  existing path, and local pool follow-ups keep current admission. This plan
  does not claim to repair every local task-worktree continuation. The guard is
  not a general ban on Shared remote rows.
- **D-5 — Give actionable failure text without performing recovery.** Use the
  four-argument `ValidationException` constructor so `Message` and field error
  carry the explanation. Name predecessor short ID and canonical runner ID;
  suggest a fresh `-Worktree -StartRef <published-sha>` request **without
  `-OnAgent`**, after publishing the intended source. The placeholder is guidance,
  not a claim that the predecessor's original base is its latest tip. Do not
  resolve/push a branch, cancel a task, stop a session or retry automatically.
- **D-6 — No data repair or provider-policy expansion.** Leave the five failed
  historical rows, Retry, defaults, provider auth, named-agent kind restrictions
  and CARD-1029's verification obligations unchanged. The caller can commission
  an explicit fresh continuation when needed.

These decisions exercise the card's authorized refusal option; no unresolved
operator choice or speculative default is needed before Code.

## Implementation slices

**S1 — Admission guard, bounded regressions and user guidance (one commit).**

1. In `server/Application/Services/AgentTaskService.cs`, add the D-2 predicate
   and D-1/D-5 refusal at the D-3 location. Keep it inline; no new service,
   interface, setting, migration or dispatcher fallback is needed.
2. Add `tests/Antiphon.Tests/Application/RemotePoolFollowUpAdmissionTests.cs`,
   with exactly the three non-parameterized methods below. Use real Create and
   isolated PostgreSQL schemas with `DefaultRunnerKit`; keep any seed helper
   private to the new file. Add no fake row for the follow-up under test.
   In `tests/Antiphon.Tests/Application/TaskPlatformPlacementTests.cs`, wrap only
   the follow-up Create in `Existing_process_is_never_relocated` with an explicit
   no-exception assertion labeled `remote-standing-follow-up-admitted`, retaining
   its existing persisted-field assertions. This makes PC-3 an assertion red.
3. Update `docs/orchestration-loop.md`, “Reuse first”, to distinguish remote
   pool refusal from local/standing reuse and retired Worktree continuation;
   include the stable code and fresh published-commit recovery instruction.
4. Commit and push before the checkpoint group. Run CP-1 through CP-5 at that
   committed SHA. Report ordinary results, leaving deliberate PCs for Mutation.

## Verification design

### Inspection

- Read `AgentTaskService.CreateAsync`, `RequireFrozenContinuationTipAsync`,
  runner intent canonicalization, launch policy, dispatcher pool exclusions and
  `ValidationException`/`ExceptionMiddleware`: admission and exception mapping
  boundaries map to V-1/V-3, with unchanged launch defense in R-4.
- Read `DefaultRunnerKit` construction, service wiring, schema reads, standing
  seeding and fake runner directory in `DefaultRunnerCreateTests.cs`: reuse this
  controlled I/O fixture for V-1/V-2/V-3. It calls the real service and persistence;
  it does not prove dispatch or a provider conversation.
- Read complete bodies of `TaskPlatformPlacementTests.Existing_process_is_never_relocated`,
  `WorktreeDefaultContinuationTests.LiveFollowUpKeepsCwd`,
  `WorktreeDefaultContinuationTests.RetiredWorktreeCutsAtPriorTip`,
  `RunnerDefaultPlacementTests.Reroute_retry_followup_and_existing_process_keep_frozen_host`,
  and `PhoneHomeTaskRoutingTests.Shared_workspace_is_refused`: R-1 through R-5.
  Despite its name, the RunnerDefaultPlacement method exercises fresh remote
  Create, reroute and Retry; it does not exercise a follow-up.

Missing setup to add in the new file: create a predecessor through real
`AgentTaskService.CreateAsync` as a remote Worktree Worker, then seed the extant
pool agent/session and predecessor attachment that dispatch would have stored.
Use a valid in-repository directory and a `card-task-<eight hex>` suffix where
the case calls for a task directory. Local acceptance cases use an ordinary
directory. Any directory created for the fixture belongs in a unique ignored
scratch root, is removed by fixture disposal, and is never an actual task's
checkout. Seed successor-independent task/session identities and a completed
predecessor, with no Blocked occupant. The seed substitutes for prior launch;
it is explicitly not an end-to-end dispatch witness. Use fake healthy runner
responses, never production runner input or external provider credentials.

### Delivery inventory

No new or changed asynchronous delivery path. The producer is the synchronous
Create request; the destination is its HTTP caller, through existing exception
middleware. The changed outcome precedes task construction/insertion, Created
event and later dispatcher/queue handoffs. The observable receipt is 422 plus
stable code/field and unchanged stored task/event/session counts. Verify these
through fresh contexts, and verify no `AgentTask` is left Added in the service
context. No UserPrompt receipt is claimed or required for a refused request.
Existing session/queue delivery and recovery are unchanged; acceptance regressions
below prove only their stated admission/dispatch boundaries.

### Proves it works now

All three new methods are `[Test]`, `[Category("Integration")]`, one executed
TUnit result each; internal case loops are not additional executions.

| ID | Test | Cases and decisive assertions |
|---|---|---|
| V-1 | `RemotePoolFollowUpAdmissionTests.Remote_pool_follow_up_refuses_before_insert` | Base case: real remote Worktree predecessor, extant pool agent, omitted runner/workspace. Loop omitted/Shared/ReadOnly across Grok/ClaudeCode/Codex (9 cases). Add individual cases for blank agent runner inheriting prior remote, differing agent/prior remote (agent wins), unknown/disabled remote binding, and explicit `local` request against the remote process (4 cases). For each: `ValidationException.StatusCode == 422`, Code `follow_up_remote_pool_unsupported`, error field `FollowUpOnTask`, text contains predecessor short ID, actual runner, `-Worktree`, `-StartRef`, `-OnAgent`; fresh-context task/event/session counts unchanged and no Added follow-up entry. Assert the refusal with label `remote-pool-refused-before-insert`. |
| V-2 | `RemotePoolFollowUpAdmissionTests.Local_pool_follow_up_keeps_admission` | Agent/prior runner null, whitespace, `local`, mixed-case `DESKTOP`, and an explicitly local agent over a remote predecessor (5 internal cases); request runner omitted. Real Create succeeds with same AgentId/FollowUpOfTaskId, Shared workspace and same working directory. Do not add normalization changes to stored legacy alias behavior. Label `local-pool-follow-up-admitted`. |
| V-3 | `RemotePoolFollowUpAdmissionTests.Explicit_worktree_follow_up_keeps_existing_conflict` | Extant remote pool agent plus explicit Worktree still throws 422 `workspace_existing_agent_conflict`; task/event/session counts unchanged. Establishes the D-3 location, rather than changing an existing error contract. |

V-1's unsupported/unknown cases set the stored remote binding after predecessor
creation; they prove retained-binding classification, not admission of an unknown
runner. Keep these few boundary cases separate from the three-provider matrix.
The service exception's status/code/field plus inspected unchanged middleware
are the HTTP-contract witness; a full web host is unnecessary for this branch fix.

### Guards the regression

| ID | Existing exact method | Required existing witness |
|---|---|---|
| R-1 | `WorktreeDefaultContinuationTests.LiveFollowUpKeepsCwd` | Local pool follow-up keeps cwd and reuses the original session; no second worktree. |
| R-2 | `WorktreeDefaultContinuationTests.RetiredWorktreeCutsAtPriorTip` | An absent agent produces a new Worktree/branch at the frozen predecessor tip; old checkout unchanged. This is the existing local fixture, not a remote delivery test. |
| R-3 | `TaskPlatformPlacementTests.Existing_process_is_never_relocated` | Remote **non-pool** standing follow-up is still admitted at `remote-standing-follow-up-admitted`, on the same runner/agent with inherited platform and ExistingProcess source. |
| R-4 | `PhoneHomeTaskRoutingTests.Shared_workspace_is_refused` | Remote non-Worktree cold launch still throws `phone_home_worktree_refused`. |
| R-5 | `RunnerDefaultPlacementTests.Reroute_retry_followup_and_existing_process_keep_frozen_host` | Fresh remote Worktree Create, reroute and Retry retain the explicit runner and platform. |

### Guard inventory

The changed predicate has three independently breakable obligations, with one
control per behavior. Existing guard internals exercised by R-2/R-4/R-5 are
unchanged compatibility checks, not new mutation scope for this card.

| Guard | Invariant | Control |
|---|---|---|
| G-1 | D-1/D-2: remote pool follow-up refuses before persistence; request omission cannot bypass it. | PC-1 |
| G-2 | D-2/D-4: local pool binding remains outside the new refusal. | PC-2 |
| G-3 | D-4: non-pool remote standing binding remains outside the new refusal. | PC-3 |

Guards=3; mapped=3; missing=0; duplicate PC maps=0.

### Positive controls

Run each after confirmed implementation land in a separately commissioned
SourceLanding Mutation. Baseline, compiling mutation red, exact restore and green
must all use the named method only. Controls share `AgentTaskService.cs`; execute
sequentially. Zero tests, compilation or fixture errors do not count as red.

| PC | Compiling production mutation | Exact method filter | Expected red |
|---|---|---|---|
| PC-1 | Remove only the new refusal block, leaving existing workspace rewrite intact. | `/*/*/RemotePoolFollowUpAdmissionTests/Remote_pool_follow_up_refuses_before_insert` | Base omitted-runner case no longer throws at `remote-pool-refused-before-insert`; it admits a follow-up. |
| PC-2 | Replace only the new canonical remote-binding condition with `true`, retaining `IsPoolDelegate`. | `/*/*/RemotePoolFollowUpAdmissionTests/Local_pool_follow_up_keeps_admission` | A local pool Create throws the new refusal instead of satisfying `local-pool-follow-up-admitted`. Capture/assert the no-exception outcome under that label so the red is an intended assertion. |
| PC-3 | Remove only `followAgent.IsPoolDelegate` from the new condition. | `/*/*/TaskPlatformPlacementTests/Existing_process_is_never_relocated` | The no-exception assertion `remote-standing-follow-up-admitted` fails because Create throws `follow_up_remote_pool_unsupported`. |

Code executes ordinary V/R only. Review assesses ordinary evidence and this
pending PC design; it does not claim PC-clean. Mutation owns all three cycles,
retains external receipts/restoration, and makes no snapshot commits.

### Out of scope

Remote same-session pool reuse, branch/checkout ownership transfer, dispatch
recovery, whole provider matrices, CARD-1029's seeded warm-state coverage, and
repair/retry of previously persisted bad rows require their own scope. Bare
`-Agent`/routing-pin behavior, blocked/kind/env validation internals, and remote
standing cold-start availability are unchanged. No broad Unit, namespace or
whole-assembly run is justified by this one Create branch.

### Execution lane and evidence

Read `GET /api/runner-defaults` and `GET /api/session-runners` before dispatch.
The Plan read both on 2026-10-04 at about 17:33 UTC: defaults revision 2 selected
an available Linux remote runner; the desktop was also available and an alternate
remote entry was stale/draining. These are observations, not placement constants.
**Every CP below uses the portable .NET application lane**, PostgreSQL isolation,
and controlled runner clients; CP-2 also uses scratch Git under the existing
process limiter. No Windows-specific API or real provider process is required.
Omit `-Runner` and `-Platform`; `-Platform Any` is only needed to unpin a future
card OS constraint. Do not embed a host location in the dispatch.

Commit/push S1 first. Use the checkpoint tool `run --plan <this-plan>
--after S1 --expected-source-sha <committed-sha>` through the host build-slot gate;
await completion, repeating its `wait` if exit 75. The tool owns row leases.
Any bootstrap build/run also uses `scripts/build-slot.ps1` and alternate output.
Keep source frozen across the group and use the same build-source stamp for
reuse rows. Preserve all five unedited CHECKPOINT lines, executed names/counts
and strict clean-source receipts in the report; generated evidence stays ignored.
Validate receipts at the tested SHA and run the full-range evidence diff guard
for Code/Review. Delete task-owned alternate output directories before finishing.
An inherited failure is reproduced with only its exact filter at the base commit;
do not widen scope, timeouts or assertions to obtain green.

### Cost

Estimated, not measured in Plan: CP-1 includes one isolated build and database
setup (8 minutes); CP-2 2 minutes; CP-3/4/5 1 minute each. Ordinary floor is
**13 minutes**, plus 20-30 minutes authoring/docs and approximately 2 minutes
for receipt/static checks: **35-45 minutes Code**, budget ceiling 60.
One output build is reused by four narrow rows; four redundant builds are avoided.
Eight TUnit executions total (3 new + 5 existing), no repetition after green.

Mutation floor: three exact filters above, baseline/red/restored-green each,
estimated 4 minutes per isolated phase = **36 minutes** plus 6 minutes mutation
and restoration bookkeeping = **42 minutes**. No shared-file mutation batching.
The separate Review repeats only the ordinary closed list (13-minute floor).
Slot waits or measured build overruns are reported separately, never bypassed.

### Checkpoints

Lane for CP-1 through CP-5: portable .NET application lane described above.
All rows close the same committed S1 and reuse its single isolated build.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1037/` | remote-follow-up-admission | `/*/Antiphon.Tests.Application/RemotePoolFollowUpAdmissionTests/*` | V-1, V-2, V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-2 | S1 | CP-1 | existing-local-continuation | `/*/Antiphon.Tests.Application/WorktreeDefaultContinuationTests/(LiveFollowUpKeepsCwd*)\|(RetiredWorktreeCutsAtPriorTip*)` | R-1, R-2 | both named methods, 0 failed/skipped | 2 | 2 |
| CP-3 | S1 | CP-1 | existing-remote-standing | `/*/Antiphon.Tests.Application/TaskPlatformPlacementTests/Existing_process_is_never_relocated` | R-3 | named method, 0 failed/skipped | 1 | 1 |
| CP-4 | S1 | CP-1 | remote-launch-defense | `/*/Antiphon.Tests.Application/PhoneHomeTaskRoutingTests/Shared_workspace_is_refused` | R-4 | named method, 0 failed/skipped | 1 | 1 |
| CP-5 | S1 | CP-1 | remote-create-retry | `/*/Antiphon.Tests.Application/RunnerDefaultPlacementTests/Reroute_retry_followup_and_existing_process_keep_frozen_host` | R-5 | named method, 0 failed/skipped | 1 | 1 |
