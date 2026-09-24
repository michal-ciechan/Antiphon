# CARD-0659: default runner for delegated Worktree tasks

Date: 2026-09-24. Plan task: `d2ce3ed6-c89b-434d-b6f2-3d909b8893e8`.
Inspected checkout: `799776677be420e0261cae554c66195d49d7d0fb`.
Next: **Code**, with test design folded into this plan as commissioned.

## Outcome and scope

Add `Delegation:DefaultRunnerId`, initially unset. A fresh, runner-compatible
delegated task created without `runnerId` selects that runner when it is
dispatch-eligible; otherwise it selects the desktop and records the reason.
`delegate.ps1 -Local` sends `runnerId: "local"`. Explicit remote placement retains
its existing admission/refusal behavior. Codex remains local.

This is a default for **AgentTask creation**, including a delegated Orchestrator
task, not a default for every process Antiphon starts. The caller inventory below
corrects the card's assumption about tracker, scheduled and channel card spawns.
Those paths do not create AgentTasks or meet this card's fresh Worktree-task
contract. Preserve them; do not silently convert card/session ownership into
delegated-task ownership. Their subsequent POSTs to `/api/agent-tasks` receive the
same server-side default as every other caller.

The authorized outcome includes enabling server2 after the dependency and rollout
gates below. Code may land with the setting unset before those gates pass. This
Plan changes no running configuration and claims no deployment or test execution.
CARD-0660 already tracks Codex on server2; do not file a duplicate.

## Ground truth

The card and CARD-0653/0657/0654 were read from the Antiphon board on 2026-09-24.
At observation they were InProgress, InProgress, Review and Backlog respectively
(CARD-0659 first). Board status is not publication or activation evidence.

| Card assumption | What the code does at the inspected SHA | Consequence |
|---|---|---|
| There is no default runner. | `AgentTaskService.CreateAsync`, around 1144, normalizes only `request.RunnerId`; `DelegationSettings` has no default-runner setting. | Select once in this service, not in the script or API endpoint. |
| A Worktree request is the eligible shape. | CARD-0644 resolves omitted workspace to Worktree. A standing-agent routing pin can later change omitted workspace to Shared, around 767. | Use the resolved workspace, effective agent kind, and original process-pin intent after the routing-pin overlay. |
| Grok and ClaudeCode can run remotely. | `PhoneHomeLaunchPolicy.IsAdmittedKind` admits both. `CreateAsync` also checks configured runner, delegated-task enablement, workspace, pins and follow-up. | Reuse these constraints; do not infer support from the parent kind or a role name. |
| `local` already works as a task opt-out. | `PhoneHomeRunnerDirectory.Resolve` understands `local`; task creation instead treats it as a remote ID and rejects it. | Consume the reserved sentinel before task admission. Persist null for desktop, never literal `local`. |
| Available means ready. | `PhoneHomeRunnerDirectory.Resolve` checks `SnapshotLive`, runner identity and `DispatchEligible`; `SnapshotLive` fences expired leases. Recovery must complete before the flag becomes true. | Reuse this existing eligibility boundary. Do not use a heartbeat, `KnownRunnerIds`, `LiveStoreId`, or successful health check as readiness. |
| SourceLanding is always forbidden remotely. | The create path now permits Mutation/Worktree SourceLanding and calls `SourceLandingAdmission.RequireSupportAsync(task.RunnerId)`; its older comment still describes Cut A. | Preserve Mutation-only validation and runner custody admission. Do not loosen either or reintroduce the blanket refusal. |
| Every spawn goes through task creation. | Only `AgentTaskEndpoints` invokes `AgentTaskService.CreateAsync`. `ScheduleService`, `AgentChannelService`, `CardService`, and `OrchestratorService` have separate card/session launch paths. | Document and test the boundary; there is no script-only solution and no implicit conversion of those APIs. |
| MaxConcurrentTasks is already local. | `AgentTaskDispatcher` counts all active non-specialist tasks, gates all queued tasks, and increments a single count. `CapacityRecoveryService.TryClaimCountedSlotAsync` also counts all hosts; pipeline status projects the same cap. | CARD-0653 owns the local/remote split and runner capacity hold. Verify all those paths before activation, not just the first count query. |
| All concurrency caps should become per-host. | `DelegationOpenGate` enforces **MaxOpenTasks** and per-role limits per project, counting Queued/Dispatched/Working. That is distinct from **MaxConcurrentTasks**, the execution cap. | Retain project/role gates across hosts. Remote placement is not permission to start a second same-stage task. |
| A later model decision cannot affect placement. | Dispatcher queued-chain rewalk/resume and service explicit/wall reroute can replace `AgentKind` after create. | Prevent a persisted remote task from later executing Codex; do not silently migrate the workspace or narrow a Required pin. |
| Remote completion is safe to multiply. | CARD-0657 describes progress classification before settlement sync. Its plan exists here; the fix is not proved by that plan or Review status. | Require published and activated settlement-sync evidence before setting server2 as default. |

### Caller inventory

| Entry/caller | Actual path | Effect of this plan / required coverage |
|---|---|---|
| `scripts/delegate.ps1` | POST `/api/agent-tasks` -> `AgentTaskService.CreateAsync` | Omit runner by default; `-Local` sends the sentinel; explicit `-Runner` remains explicit. Execute the real script against a loopback capture server. |
| External/standing orchestrator's task API call, pipeline stage dispatch, UI `createAgentTask` in `client/src/api/agentTasks.ts` | Same POST and service | No client-side default lookup. Test the HTTP endpoint with Worker and Orchestrator requests, including absent `workspace`. |
| Tracker import / automatic board tick | `ExternalTrackerSyncService` imports cards; `OrchestratorService.PollTickAsync` selects cards and enqueues `StartAgentSessionRequest` | Not a CreateAgentTaskRequest caller. Preserve card claim/RunAttempt semantics. An agent spawned this way can later create eligible delegates through the POST. |
| Scheduled card Spawn | `ScheduleService` -> `IScheduledCardActions` / `CardService.SpawnAsync` | Existing card/session launch remains unchanged. Scheduled prompts to standing agents also retain their existing host. A delegate POST made by the prompted agent gets the default. |
| Channel card delegation | `AgentChannelService.DelegateCardAsync` -> `CardService.SpawnAsync` | Existing card/session launch remains unchanged. Channel-facing agents invoking the task API get the default for eligible children. |
| `/api/orchestrator/tick` | `OrchestratorEndpoints` -> `OrchestratorService.PollTickAsync` | Not the delegated Orchestrator task-create API; retain existing launch behavior. |
| Internal helpers constructing an AgentTask directly | Existing Check/Distill/Diagnose/commit/merge helpers | Do not apply a fresh remote default behind pinned/specialist/shared callers. New automatic placement callers must deliberately use the create service, not reproduce its policy. |

Converting direct card starts to remotely mirrored AgentTasks would require a
separate lifecycle design: measure card ownership, RunAttempt recording, stop/
release behavior and workspace custody at those entry points first. It is not
required for the expressly scoped POST-create feature and must not be claimed as
an outcome of this card.

## Decisions

These are implementation choices within the commissioned scope, not unanswered
operator questions. The existing authority covers implementation and the gated
server2 activation; no repeated approval is required.

### D-1. Three input states, one persisted execution location

`runnerId` omitted/null/whitespace means automatic selection. Trim a nonblank
value. The reserved `local` token (case-insensitive) means explicit desktop;
every other value is an explicit remote ID, with existing ordinal ID matching.
`-Local` and a nonblank `-Runner` are mutually exclusive and fail before a POST.
`-Runner local` may use the same sentinel for compatibility; it does not force a
Worktree or trip remote-only guards. Prefer `-Local` in docs.

Snapshot the original input intent before normalizing the sentinel out of the
request. It must not prevent CARD-0644's omitted-workspace + agent-pin behavior:
today that logic tests `string.IsNullOrWhiteSpace(request.RunnerId)`.
Store `AgentTask.RunnerId = null` for desktop or the configured remote ID for
remote. No new entity column/migration is necessary: the Created event carries
the requested/automatic distinction. Existing rows and continuations are not
backfilled or moved. `-Local` suppresses a new default; it never relocates a pinned
existing process to the desktop.

Rejected: persisting `local` in RunnerId (existing null/non-null predicates would
misclassify it); treating omission as explicit desktop (defeats the feature);
putting the default in PowerShell (misses HTTP/UI/agent callers).

### D-2. Select after workspace, model and agent pins settle

Add nullable `DefaultRunnerId` to the existing typed `DelegationSettings`.
Null/blank and configured `local` disable automatic remote placement. Do not ship
`server2` in shared appsettings or hard-code it in policy. Other valid IDs are
resolved at create time; an unknown/disabled configured runner falls back with an
audit reason. Bound configured/input identifiers to 64 characters and reject
control characters; use the repository's ValidationException/options-validation
conventions. Reserve `local` against remote runner registration/configuration.

Introduce a small concrete `DefaultRunnerRoutingPolicy` in Application, taking
the resolved shape and existing `PhoneHomeLaunchPolicy` / `ISessionRunnerDirectory`
seams. It returns selected runner, source and stable reason. It must not depend
on Infrastructure's concrete directory or issue an HTTP call to this server.
It can be called from the existing service dependencies; no new interface or
optional DI fallback that silently disables production routing is needed.

Run selection at the existing remote-runner admission block, after routing pins,
complexity walking, inherited standing-agent kind and workspace resolution, before
remote provider authentication, SourceLanding support checks and task insertion.
Keep explicit runner validation intact. Automatic remote selection requires:

- resolved Worktree and effective kind Grok or ClaudeCode;
- no original or resolved Agent/AgentId pin, no original FollowUpOnTask/OnAgent
  (even a retired follow-up which degrades to fresh is excluded);
- no Shared/ReadOnly; ordinary fresh Worker or supported ClaudeCode Orchestrator;
- SourceLanding absent, or the existing valid Worker/Mutation/Worktree shape;
- configured default matches an enabled allowed runner with delegated tasks on;
- directory resolution confirms dispatch eligibility.

Do not choose a runner for an exhausted model chain pretending its first failed
candidate is selected. Keep that Blocked task local; if later resumed it remains
local. The policy never changes model, tier, forbidden aliases, quota behavior,
provider identity or pins. A pin choosing Codex stays local; a pin choosing Grok
or ClaudeCode without an agent can use the default. Required-pin conflicts still
refuse before runner selection. Explicit remote+Codex still refuses.

Rejected: resolving from raw request.AgentKind (misses role policy and pins);
applying before workspace defaulting; choosing a runner inside each endpoint;
silently overriding an agent pin to free desktop capacity.

### D-3. Readiness fallback is a bounded create-time decision

Use `ISessionRunnerDirectory.Resolve(defaultId)`: the real implementation already
checks lease/recovery dispatch eligibility synchronously without a round-trip.
Catch only its expected unavailable refusal (and an equivalent typed unavailable
conflict if the landed directory uses one). Do not catch cancellation or arbitrary
exceptions as permission to route locally. Missing directory => local fallback
with `runner_directory_unavailable`; unknown/disabled policy =>
`default_runner_not_enabled`; delegation disabled => `runner_tasks_disabled`;
known runner not dispatch-eligible => `runner_not_dispatch_eligible`.

This does not use `GetInventoryAsync`, provider login, free capacity, or a health
probe to decide placement. A healthy full runner is selected and queues under
CARD-0653. A model hold/auth refusal after selection preserves existing refusal/
Blocked behavior, never an implicit switch to the desktop. SourceLanding custody
admission also remains a refusal, not a second fallback rule.

For an **explicit** remote ID, preserve admission even if temporarily offline
(when its configuration and shape are valid); dispatcher holds remain explicit
operator intent. Never fallback an explicit ID, including a typo.

The snapshot-to-dispatch race cannot be eliminated by a create-time read. If a
runner disconnects after selection, the persisted task retains its runner and
the normal dispatcher hold. The card's “never Held for a runner that is down”
means one already known down when automatic placement is decided; it is not an
authorization to move queued/prepared work after the fact.

Rejected: live polling during create (adds latency and another truth source);
capacity overflow to local (belongs to CARD-0654); retrying selection every tick
(can strand mirrors/custody and make explicit placement indistinguishable).

### D-4. Persist the decision in the existing task timeline

For a configured automatic default, an explicit remote selection or explicit
local opt-out, append one bounded, deterministic Created-event segment, e.g.:

`runner source=default requested=unset default=server2 selected=local reason=runner_not_dispatch_eligible`

Successful remote selection uses `selected=server2 reason=eligible`; explicit
desktop uses `source=explicit-local reason=local_requested`; excluded shapes use
`reason=kind_not_supported|existing_process|workspace_not_worktree|routing_exhausted`.
Order exclusions deterministically. Add one existing Warning event and return the
same warning via AgentTaskCreatedDto when an otherwise eligible automatic request
falls back because its configured runner cannot dispatch. Exclusions are not
warnings. Do not emit new placement noise for wholly unchanged unset/defaultless
requests. Preserve all pin/model/workspace warnings and event notes.

Persist task and event in the existing admission transaction; refusal creates
neither. Never log launch env, credentials, secret config, or raw exception text.
Tests read the saved row/events after clearing EF tracking; stdout alone is not
the audit oracle. Existing task detail exposes events, so no UI or new enum is
required for this minimal feature.

Rejected: log-only evidence, deriving history from today's config, or storing
ambiguous “used fallback” without requested/default/selected host and reason.

### D-5. Keep placement stable through model rewalks

Once created, default selection is not recalculated. A local task stays local
even if a later compatible model appears or the default changes. Remote Grok <->
ClaudeCode transitions may keep their runner; Codex must never launch remotely.

Add a shared host/kind compatibility check at each point that publishes a newly
chosen kind: `AgentTaskService.RerouteAsync`, `RequeueOnWallAsync`,
`AgentTaskDispatcher.TryRewalkQueuedChainAsync`
and `ResumeRoutingBlockedAsync`. Explicit incompatible reroute refuses with
`runner_kind_unsupported` before changing the row. An automatic incompatible
choice produces an existing routing Blocked outcome with a stable
`runner_kind_unsupported` detail, preserving the runner, original kind and pin
list. Use the existing routing-exhausted prefix so the operator can reroute to a
compatible pair using the existing API. No silent skipping/reordering of Required
pin candidates and no desktop migration. An active usage-wall path must still
release its old process through the existing stop/settlement contract before
becoming Blocked. Suppress repeated identical automatic block events.

The dispatcher must also check current remote host/kind compatibility before
remote prep or a launch claim, defending persisted/legacy mismatches. A blocked
incompatible task is not automatically requeued just because the same incompatible
candidate is still the model walk's choice.

Rejected: change RunnerId to null during a reroute (workspace/custody migration is
not designed); run Codex through the remote Grok/Claude projection; rewrite a pin
to make a default-host choice appear compatible. CARD-0660 owns new remote kinds.

### D-6. Concurrency composes; it is not a second placement algorithm

CARD-0653 owns runner capacity reservations/holds, session release and exclusion
of remote execution from the desktop cap. Require its landed implementation to
cover initial dispatcher active counts, same-tick increments, queued gates,
retained CapacityWait redemption/count-and-claim and pipeline hold projection.
Those counts must use the same host classification. Literal `local` never enters
storage under D-1. Local work across projects still shares desktop capacity.

Remote occupancy includes the runner's standing/live sessions and launch
reservations as CARD-0653 defines; counting only AgentTasks is insufficient.
Runner-at-capacity holds before mirror prep and releases after task settlement.
This card does not raise limits, implement force-release, or duplicate capacity
reservations. Full server2 never becomes an automatic local overflow trigger.

MaxOpenTasks and project/role create gates remain cross-host and unchanged.
IgnoreConcurrencyLimit still concerns create admission, not runner capacity.
CARD-0654 can later replace host admission with min(configured budget, declared
capacity), add weights/overflow and host visibility. Keep the policy result
explicit enough for that future extension; no new scaling settings here.

### D-7. Publication and activation are separate gates

| Dependency | Required before dormant Code lands? | Required before server2 is enabled as default? |
|---|---|---|
| CARD-0653 capacity hold, local/remote cap split, settled-session release and conditional stop | No for S1-S3; S4 integrates after its publication. Coordinate shared dispatcher edits rather than dispatch competing Code tasks. | Yes. Verify normal slot release, capacity hold before prep and operator recovery API readiness; confirm server2 declared capacity 3 after its authorized redeploy. |
| CARD-0657 settlement sync/progress attribution | No; keep default unset. | Yes. Exact pushed remote SHA must be attributed and settle successfully before default traffic grows. |
| CARD-0649 Claude report markers | Existing code may be present; source presence is insufficient. | Yes for Claude. Confirm server2 image actually includes it, then capture an attributed Claude report. |
| CARD-0644 Worktree default | Present in inspected code; preserve it. | Yes, including pin/follow-up compatibility. |
| CARD-0654 per-host budgets/weighted placement | No. | No. This minimal default can ship with CARD-0653's declared-capacity ceiling and local cap. No claim that configurable 3+3 budgets or load-aware admission has shipped. |
| CARD-0660 remote Codex | No. | No. Codex remains local. |

S1-S3 can ship independently behind the unset setting; activation waits for S4
and the dependency evidence. Deployment uses the existing canonical checkout and
restart procedures, not this linked worktree. Record publication receipts and
server `/api/version` SHA; separately record runner build/image and status.

Canary sequence: with default still unset, use explicit `-Runner server2` for one
Grok and one Claude Worktree task; require prompt/report attribution, pushed SHA,
successful settlement sync and slot release. Enable `Delegation:DefaultRunnerId=server2`
in the deployment's existing configuration source and restart/reload as that source
requires. Create eligible omitted-runner tasks, a `-Local` task and Codex; inspect
their saved runner decisions and actual session ownership. Use an isolated runner
for the down-runner fallback and full-capacity tests, not a production outage.
Do not infer activated server code from runner health. Rollback unsets the default;
already created remote tasks retain their runner and drain through normal custody.

## Code slices

Run at most one round per Code dispatch if it approaches 90 minutes. Commit and
push the tests/scaffold before its red checkpoint, then the fix before its green
checkpoint. A test run owns frozen source. No daemon restart is part of these
rounds. Coordinate S2's dispatcher scope behind any CARD-0653 Code task.

| Slice / authoring budget | Files and implementation | Tests / stop condition |
|---|---|---|
| S1, 65-80 min | `server/Application/Settings/DelegationSettings.cs` (property + validation); `server/Application/Settings/PhoneHomeRunnerSettings.cs` (reserved ID); new `server/Application/Services/DefaultRunnerRoutingPolicy.cs`; `server/Application/Services/AgentTaskService.cs` (normalize intent, resolved-shape selection and audit); `server/Application/Dtos/AgentTaskDtos.cs` (contract comments). No schema/client changes. | New `tests/Antiphon.Tests/Application/DefaultRunnerCreateTests.cs` and `DefaultRunnerEligibilityTests.cs`. Red/green CP-1/2. Initial default-selection, fallback and explicit-local assertions must fail against the compiled scaffold. |
| S2, 65-85 min | `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, the new policy's common compatibility helper; preserve current model-pin and usage-wall process-release mechanics. Correct the stale remote-create Cut-A comment. | New `DefaultRunnerPinTests.cs`, `DefaultRunnerRerouteTests.cs` in the same test directory. CP-3/4. Cover model/agent pins, all rewalk paths, SourceLanding admission and no incompatible launch/prep. |
| S3, 55-75 min | `scripts/delegate.ps1` create-only Local switch, conflict/serialization behavior and help; `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/docker-stack.md` default/opt-out/rollout instructions; `docs/antiphon-api.md` request semantics and caller boundary. Keep operational PowerShell ASCII. Endpoint code should need no policy logic. | New `DelegateScriptLocalRunnerTests.cs`, `DefaultRunnerEndpointTests.cs`. Use real script process/captured body and actual minimal API route with controlled dependencies. CP-5/6. No synthetic caller label presented as a real scheduler integration test. |
| S4, 50-80 min, after CARD-0653 | New `DefaultRunnerCapacityCompositionTests.cs` and `DefaultRunnerCallerBoundaryTests.cs`; integrate only any missing host-count consistency in `AgentTaskDispatcher.cs`, `CapacityRecoveryService.cs`, `AgentTaskPipelineStatusService.cs` and DelegationSettings documentation. Prefer consuming the landed fix to duplicating it. If missing reservation/release machinery is found, return it to CARD-0653; default remains unset. | CP-7/8. Prove local/remote budgets compose and saved defaults survive dispatch; run Unit lane once. Cross-check existing card-spawn boundaries with focused tests using their real services, not source-string assertions. |

All new test files in the slice table are under
`tests/Antiphon.Tests/Application/`. Existing supporting fixtures:
`PhoneHomeTaskCreateTests`, `PhoneHomeTestHost`, `PhoneHomeRecoveryEligibilityTests`,
`DelegateScriptRunner`, `DelegateCreateStubApi`, `AgentTaskConcurrencyLimitTests`,
`AgentTaskPipelineStatusTests`, `CapacityRecoveryTestSupport`.

## Verification design

### Test boundaries and red evidence

Use isolated PostgreSQL schemas and controlled session/runner implementations;
no paid agent, native CLI login, live broker or production runner is required.
For readiness integration use `PhoneHomeTestHost` / its scripted peer and fake
clock: compare create decisions before recovery, after recovery, and after lease
expiry. A mocked boolean alone cannot prove dispatchEligible semantics. HTTP tests
use the actual route registration, controlled caller resolution and stopped
background dispatch; a host using real Program must retain the established
isolated-runner guard. Process-spawning script tests carry
`ParallelLimiter<ProcessSpawnLimit>`. Classify all new classes Integration; pure
policy tests may be Unit only when they perform no I/O.

Each round first adds its tests and only the declarations necessary to compile
against an inert/unchanged behavior. Commit that test-first state and run its red
row. Missing types/build errors, unknown PowerShell parameters and empty discovery
are not red behavior evidence. In S3 declare the Local parameter in the scaffold
but leave serialization unchanged, so the assertion on the posted body goes red.
Then implement, commit and run the green row. Preserve exact failing method,
assertion, commit and TRX for each red witness. Existing passing regressions may
share that red invocation; unexpected failures must be investigated separately.
Tests whose guarded behavior already landed in CARD-0653 are regressions, not
newly claimed red-first implementation evidence.

### Coverage and named test roster

Use these method names (parameterization/extra regression methods are allowed;
checkpoint Min is a conservative method-result floor, not assertion count).

| ID / class | Methods and required oracle |
|---|---|
| V-1 `DefaultRunnerCreateTests` (7 methods) | `Eligible_worktree_uses_default` (Grok and Claude, Worker and supported Orchestrator, explicit/omitted Worktree); `Unset_default_preserves_behavior` (null/blank/configured-local, no directory call/new warning); `Explicit_local_is_persisted_as_null`; `Explicit_remote_is_not_replaced_or_fallen_back`; `Excluded_shapes_keep_existing_placement` (Codex, Shared, ReadOnly, OnAgent live/retired, Agent/AgentId); `Fallback_is_durable_and_explained` (config disabled/unknown/no directory); `Provider_refusal_does_not_fallback` (selected host's auth probe and refusal, no task inserted). Assert saved RunnerId, kind/workspace, warning and Created audit, including no duplicate notes. |
| V-2 `DefaultRunnerEligibilityTests` (3 methods) | `Before_recovery_falls_back_after_recovery_selects_remote`; `Expired_lease_falls_back_even_with_a_retained_store_id`; `Selected_task_does_not_migrate_after_disconnect`. Exercise the real directory and persisted create result; disconnected-after-selection stays remote/held with zero local launch. |
| V-3 `DefaultRunnerPinTests` (4 methods) | `Kind_pin_is_resolved_before_host`; `Agent_pin_reuses_workspace_with_local_sentinel`; `Required_pin_conflict_is_unchanged`; `SourceLanding_uses_selected_host_custody`. Matrix includes ignored/prefer/required pins, Codex/Grok/Claude, multi-candidate final choice, exhausted routing local/Blocked, and valid Mutation custody vs invalid role/no custody. Existing source authorization/unique-open/custody guards still run. |
| V-4 `DefaultRunnerRerouteTests` (5 methods) | `Explicit_incompatible_reroute_is_refused_without_mutation`; `Queued_rewalk_blocks_incompatible_host_before_prep`; `Routing_resume_does_not_loop_on_incompatible_candidate`; `Usage_wall_blocks_and_releases_existing_process`; `Compatible_reroute_preserves_host_and_pin_order`. Assert durable Blocked/refusal, no Codex remote launch, unchanged runner/pin list, one event per transition and no recalculation from changed default config. |
| V-5 `DelegateScriptLocalRunnerTests` (5 methods) | `Omission_omits_runnerId`; `Local_posts_sentinel`; `Local_and_runner_conflict_never_posts`; `Local_preserves_workspace_and_existing_process_switches`; `Explicit_runner_and_local_alias_round_trip`. Execute real pwsh against loopback capture; assert POST count/body, diagnostic and exit status. Matrices cover `-Shared`, `-ReadOnly`, `-OnAgent`, `-Agent`, `-Runner server2`, `-Runner local`, and create-only parameter binding. |
| V-6 `DefaultRunnerEndpointTests` (3 methods) | `Direct_post_and_orchestrator_post_share_default`; `Explicit_local_and_omitted_runner_are_distinct_over_json`; `Task_detail_exposes_saved_routing_reason`. Exercise actual endpoint with authorized caller contexts, assert stored rows/events and HTTP detail after reloading; changing config later does not change history. |
| V-7 `DefaultRunnerCapacityCompositionTests` (5 methods) | `Remote_activity_does_not_consume_local_dispatch_cap` (and local-full does not suppress eligible remote); `Full_remote_runner_holds_before_mirror_without_local_overflow`; `Retained_capacity_wait_uses_its_execution_host`; `Project_and_role_admission_still_spans_hosts`; `Pipeline_hold_projection_matches_dispatcher`. Test same-tick claims, running plus reserved remote slots, local sentinel normalization, normal slot release allowing the next queued task, and inherited create-time role refusal. Consume CARD-0653's actual admission mechanism; no fake test-only capacity algorithm. |
| R-1 `DefaultRunnerCallerBoundaryTests` (new in S4, 3 methods) | `Scheduled_spawn_keeps_card_session_contract`; `Channel_spawn_keeps_card_session_contract`; `Tracker_tick_keeps_card_session_contract`. Adapt existing `ScheduleCardActionTests`, `AgentChannelServiceIntegrationTests`, `OrchestratorServiceIntegrationTests` harnesses with default configured. Assert real called service/launch/claim outcome and no AgentTask row generated by those paths. Do not invoke paid sessions. |
| R-2 existing affected regressions | S1: `PhoneHomeTaskCreateTests`, `PhoneHomeRecoveryEligibilityTests`; S2: `RoutingPinCandidateCreateTests`, `WorktreeDefaultAdmissionTests`, `SourceLandingAdmissionTests`; S3: `DelegateScriptRunnerSwitchTests`, `DelegateScriptWorkspaceDefaultTests`; S4: `AgentTaskConcurrencyLimitTests`, `AgentTaskPipelineStatusTests`. These are named narrowly instead of rerunning broad namespaces. |
| R-3 Unit lane | All existing Unit tests once on the final committed source, including configuration and script contract guards. No full assembly or Pty suite for this server-side policy change. |

Red witnesses: CP-1 must contain V-1 `Eligible_worktree_uses_default` and
`Explicit_local_is_persisted_as_null` behavior failures; CP-3 must contain V-4
`Explicit_incompatible_reroute_is_refused_without_mutation` or
`Queued_rewalk_blocks_incompatible_host_before_prep`; CP-5 must contain V-5
`Local_posts_sentinel` posted-body failure. CP-7 is a dependency regression gate:
it may pass immediately when CARD-0653 is complete; never undo its production fix
just to manufacture a Code red result. Add a test-first red checkpoint only for a
newly required composition fix, report that extra run and reason, then rerun CP-7.

### Later Mutation controls

After ordinary Code/Review/land, the verification companion commissions
SourceLanding Mutation against the published SHA. Proposed method-scoped PCs:

| PC | Mutation | Exact test whose outcome must turn red |
|---|---|---|
| PC-1 | Suppress assignment of the selected default runner | `/*/*/DefaultRunnerCreateTests/Eligible_worktree_uses_default` |
| PC-2 | Treat explicit local as omitted | `/*/*/DefaultRunnerCreateTests/Explicit_local_is_persisted_as_null` |
| PC-3 | Use connection/store presence instead of recovery eligibility | `/*/*/DefaultRunnerEligibilityTests/Before_recovery_falls_back_after_recovery_selects_remote` |
| PC-4 | Omit fallback decision event | `/*/*/DefaultRunnerCreateTests/Fallback_is_durable_and_explained` |
| PC-5 | Select before the model pin overlay | `/*/*/DefaultRunnerPinTests/Kind_pin_is_resolved_before_host` |
| PC-6 | Bypass remote-kind rewalk fence | `/*/*/DefaultRunnerRerouteTests/Queued_rewalk_blocks_incompatible_host_before_prep` |
| PC-7 | Serialize Local as absent | `/*/*/DelegateScriptLocalRunnerTests/Local_posts_sentinel` |
| PC-8 | Count remote work in the desktop active count | `/*/*/DefaultRunnerCapacityCompositionTests/Remote_activity_does_not_consume_local_dispatch_cap` |

Controls are not executed by Plan or ordinary Code. Each requires assertion red,
restoration and the same method green, with external SourceLanding evidence and
no snapshot commits. Broader capacity/stop controls remain CARD-0653's obligation.

### Cost and execution rules

Target about three minutes of focused verification per authoring round on a warm,
unsaturated checkout. Table estimates include builds and are planning estimates,
not measurements from this documentation task. The card records six-minute builds
under host saturation: report actual elapsed cost if that recurs; do not relax
assertions/timeouts, drop coverage or claim the three-minute target was met.
Ordinary floor is **12 minutes** from the EstimatedMinutes column, plus 235-320
minutes authoring split into the four <=90-minute rounds. Red test runs are
included; production canaries are a separate deployment obligation.

Use `scripts/run-checkpoint.ps1` per row, `tests/Antiphon.Tests`, forward-slash
`bin-c659/`, and a fresh `.antiphon/c659-<CP>-<attempt>` results root. Pass every
class operand in `-Expect` as one comma-separated value, and the table floor as
`-MinExecuted`. Record expanded counts/failed methods/TRX and verified commit.
Expected-red rows return exit 1 with the named assertion failure, not exit 2/3.
No source edit while running. Build once per row; CP-8 reuses CP-7 output because
both are at S4-final. A failed row is fixed/committed and rerun as that row; report
reruns. Any additional build/run needs a stated reason. Each ordinary checkpoint
uses trailing class wildcards (CARD-0403); later PCs deliberately use exact methods.

Report `CHECKPOINT CP-n commit=<sha> build=<ok|reused|failed> filter=<filter> executed=N passed=N failed=N skipped=N trx=<path>`.
Compare inherited failures at the base with the exact failing method, not a whole
assembly. Use bounded foreground Git children (e.g. subprocess timeout 30 seconds
for local commands, 120 seconds for push); never leave background jobs.
Before finish, remove only the `bin-c659` directories created by this dispatch,
after verifying each resolved path lies within this worktree. Do not run the shared
cleanup job or touch daemon bin/ directories. No product build/tests ran in Plan.

### Checkpoints

All builds below mean `tests/Antiphon.Tests -> bin-c659/`. `Sx-red` is its committed
compilable tests/scaffold; `Sx` is the committed implementation. Min excludes
parameter-matrix assertions and existing regression counts, so they can only
increase the actual roster. CP-7 covers the eight newly named composition/boundary
methods and the existing regression classes at the final source.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c659/` | placement-red | `/*/*/(DefaultRunnerCreateTests*)|(DefaultRunnerEligibilityTests*)/*` | V-1,V-2 red | All listed; named behavior assertions red, no build/fixture errors | 10 | 1.5 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c659/` | placement-green | `/*/*/(DefaultRunnerCreateTests*)|(DefaultRunnerEligibilityTests*)|(PhoneHomeTaskCreateTests*)|(PhoneHomeRecoveryEligibilityTests*)/*` | V-1,V-2,R-2 | All listed, 0 failed/skipped | 10 | 1.5 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c659/` | pins-red | `/*/*/(DefaultRunnerPinTests*)|(DefaultRunnerRerouteTests*)/*` | V-3,V-4 red | All listed; named compatibility assertion red, no build/fixture errors | 9 | 1.5 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c659/` | pins-green | `/*/*/(DefaultRunnerPinTests*)|(DefaultRunnerRerouteTests*)|(RoutingPinCandidateCreateTests*)|(WorktreeDefaultAdmissionTests*)|(SourceLandingAdmissionTests*)/*` | V-3,V-4,R-2 | All listed, 0 failed/skipped | 9 | 1.5 |
| CP-5 | S3-red | `tests/Antiphon.Tests -> bin-c659/` | wire-red | `/*/*/(DelegateScriptLocalRunnerTests*)|(DefaultRunnerEndpointTests*)/*` | V-5,V-6 red | All listed; posted-body assertion red, no build/fixture errors | 8 | 1.5 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c659/` | wire-green | `/*/*/(DelegateScriptLocalRunnerTests*)|(DefaultRunnerEndpointTests*)|(DelegateScriptRunnerSwitchTests*)|(DelegateScriptWorkspaceDefaultTests*)/*` | V-5,V-6,R-2 | All listed, 0 failed/skipped | 8 | 1.5 |
| CP-7 | S4-final, includes landed CARD-0653 | `tests/Antiphon.Tests -> bin-c659/` | capacity-final | `/*/*/(DefaultRunnerCapacityCompositionTests*)|(DefaultRunnerCallerBoundaryTests*)|(AgentTaskConcurrencyLimitTests*)|(AgentTaskPipelineStatusTests*)/*` | V-7,R-1,R-2 | All listed, 0 failed/skipped; failure keeps activation closed | 8 | 2 |
| CP-8 | S4-final, includes landed CARD-0653 | CP-7, no build | unit-final | `/*/*/*/*[Category=Unit]` | R-3 | >=1 executed, 0 failed; inspect expanded roster | 1 | 1 |
