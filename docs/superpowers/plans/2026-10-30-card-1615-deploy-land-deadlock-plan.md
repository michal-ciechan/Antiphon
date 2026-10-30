# CARD-1615: isolate rollout control from canonical activation

Plan only. Round-4 assigned base `dbae0d65de7142ff61c8ec05cdd8b965d8b2f2f7`,
branch `feat/card-task-c1db9627`, inspected 2026-10-30. No product edits or rollout.
Round-3 base was `30b76fbf22dc90161acbcf5622e5d4f6ba4fc57c`.
Round-2 base was `f9bf72bd500b30d7dc7fae32971a2c8c83923c69`; round-1 source
base was `ade54222543381e2e1b31ad79d8ae33a4035f70b`.

## Round-4 revision ledger

The four final-review repairs are specified below, with production-path witnesses.
The confirmed token-first route, phase-scoped tasks, documentation pins, dependency
ordering and six-row checkpoint roster are retained. Import validation is recorded
below; product execution remains for Code. Historical evidence stays historical.

1. Review D-1: make watchdog recovery marker-aware: canonical HEAD X permits
   automatic same-X recovery of a down stack; a mismatch must alert, not retry silently.
2. Review D-2: separate durable rollout-active state from controller heartbeat.
   Protection spans every handover; a 60-minute handover deadline raises a loud
   stale condition and never removes protection. Refuse a new rollout over an
   unfinished one; define explicit audited close.
3. Review D-3: add explicit reason-bearing, audited quarantine for unknown/corrupt
   markers, including bounded sharing-violation retries and documented recovery.
4. Review D-4: correct the landing witness to publication plus canonical-advance
   residue `target_checked_out_elsewhere`, not a pre-publication refusal.

Also take A-1 (all session/orchestrator environment absent for interactive), A-2
(failed same-X restart deliberately stops heartbeat renewal during its retained
15-minute lock) and A-3 (current source line-number label).

### Confirmed round-3 design retained

All five earlier review decisions remain specified with production-path witnesses
and closed checkpoints. Historical evidence is labelled; Code execution remains pending.

1. Review D-1 — complete: classify from server-resolved token ownership; environment
   IDs are cross-checks, never authentication. Add the bounded read-only route.
2. Review D-2 — complete: a canonical active-rollout marker mechanically guards
   AppHost restart; qualify all conflicting activation/autonomy instructions.
3. Review D-3 — complete: phase-scoped tasks, at most 30-minute drain slices, durable
   journal and explicit successor handover; CARD-1610 is not a dependency.
4. Review D-4 — complete: detached standing/interactive controller and canonical
   activation HEAD; reject any controller checkout of a landing target.
5. Review D-5 — complete: update existing documentation pins deliberately and add
   their complete affected class roster to the closed checkpoints.

CARD-1609 must land first on the intended target lines. Verification design is
folded into this final Plan round; the completed handoff is Code after CARD-1609,
without another Plan or separate TestDesign. Preserve historical evidence without
crediting it to this revision. No build-definition files are commissioned.

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
| A token always means an AgentTask owner. | `AgentSessionLaunchComposer.cs:43-50` emits token + agent ID without task ID for standing agents. Assigned-agent card launches use that composer (`CardService.cs:1683`, `OrchestratorService.cs:941`). The unassigned-card credential factories at `:1791` and `:976` emit a token alone. | Classify the server-resolved token principal; environment values are cross-checks only. Standing sessions are absent from `FindWriterAsync`'s AgentTasks query. Token-only card sessions need an explicit recovery path, not a false assertion that all card sessions have an agent ID. |
| A successful canonical restart preserves the rollout SHA. | `restart-apphost.ps1:177-208` defaults expected SHA to the canonical source HEAD; `AgentTaskLandingProtocol.CanonicalAdvanceAsync` can fast-forward that checkout during rc lands. `restart-session-runner.ps1` has no `ExpectedServerSha` parameter. | An X-to-Y canonical advance can turn an ordinary restart into an unintended activation of Y. Name X at preparation and retain durable rollout protection through every handover; planned same-X recovery is quiescent, watchdog down-stack recovery is the narrow automatic exception. Stale state alerts and never silently unlocks. |
| Environment IDs authenticate caller class. | `AgentTaskService.AuthenticateAsync:186-234` resolves task hashes first, then session hashes, then capabilities (whose usage stamp writes). `AgentTaskEndpoints:184-193,537-548` makes detail GET public and ignores bad polling tokens. | The new route projects the token's principal without a caller-selected ID or persistence writes. |
| Existing locks span the whole rollout. | `apphost-common.ps1:16-19,28` expires restart/launch locks at 15 minutes; `deploy-server2.ps1:234-275` holds the remote rollout lock around a POST only. | Add durable Active state independent of heartbeat, synchronized restart admission and audited recovery; neither stale nor unknown silently unlocks. |
| One phase task can wait for the entire drain. | `deploy-server2.ps1:12,1246,1295` defaults to 480 minutes, while TaskDeadlinePolicy limits local tools to 90 and tasks to 240. Invoke-HostCase has no local child deadline. | Cap drain slices at 30, bound whole phase/local cleanup, and require joined journal handover. |
| A clean pinned branch cannot block land. | `AgentTaskLandingProtocol:470-473,557-564` records `target_checked_out_elsewhere` during canonical advance **after confirmed publication**, producing LandedWithResidue. It does not refuse or hold the land. | Detached standing/interactive source and detached main activation; registered task feat branch is the branch-contract exception. |
| Watchdog always supplies the expected rollout SHA. | `scripts/apphost-common.ps1:600-613` starts restart without ExpectedServerSha; `watchdog-apphost.ps1:329-332` does not stamp exit 3 and retries at its next scheduled fire. `:99-123` already observes outage independently. | Pass X only when canonical HEAD is X; otherwise alert without invoking restart. Preserve existing cadence and outage signals. |
| A controller heartbeat covers phase handovers. | Each foreground phase exits; `deploy-server2.ps1:1238-1245` preserves an existing old-runner drain on re-entry. No durable cross-task Active guard exists at this base. | Persist Active until Completed/audited Closed, use a 60-minute handover deadline, and reject another rollout even after heartbeat expiry. |
| Unknown rollout state may permanently prevent AppHost recovery. | The new marker guard is planned, not existing code; current restart already separates refusal from effects. | Add an explicit audited reason-bearing unknown-marker override without lifting ordinary rollout protection. |
| Version response exposes a sha field. | `server/Application/Dtos/VersionDtos.cs:16-19` and `VersionEndpoints.cs:19` expose the SHA as JSON `version`; `Resolve-AppHostLoadedIdentity` reads that member. | Round-3 correction: validate `version`, never invent `sha` or accept informationalVersion as proof. |

Paths above are relative to `server/Application/Services/` unless explicitly named.
Round 1 read CARD-1610: its concrete example is a Review, not this Deploy. The Deploy's
240-minute failure and inability to answer after Failed are caller-supplied facts;
its transcript, exact ceiling receipt and cancel/refine responses were not obtained.
No desktop files or ended native Windows run were inspected.

## Decisions

These are the caller's stated defaults, settled here from inspected code. D-7..D-13
retain the earlier review decisions. D-14..D-16 settle round-4 watchdog, durable
state and quarantine; D-12 carries the corrected publication/residue rationale.
Where code cannot prove recovery, choose fail-closed with an explicit audited
recovery path. No open product decision remains for another Plan round.

| ID | Decision and reason | Rejected alternative / risk |
|---|---|---|
| D-1 | Use existing Worktree mode for the long rollout. Pin its initial source to the reviewed rc SHA, execute scripts from that owned checkout, use a fresh owner per explicit phase or bounded drain slice, joined through the durable rollout journal. | **(a) Target-aware Deploy exemption:** off-target publication can be safe, but role alone proves neither read-only behavior nor a stable canonical HEAD. A checkout switch after admission or during verification can make the later canonical fast-forward conflict with Deploy. Sound implementation needs coordinated checkout ownership, not just a ref comparison. |
| D-2 | No new source-writer type or backend schema. Keep Shared claims, source-alias exclusions, repository leases, post-verification recheck and fail-closed unknown identity unchanged. | **(b) Typed rollout class:** useful only with enforceable lifetime/checkout-mutation rules, lease acquisition, restart custody and recovery. A descriptive enum that lets lands pass is an unsafe role exemption with more API/DB work. |
| D-3 | Bound Held/Queued/Running/NeedsResolution lands remain in flight. Add no census exemption. | **(c) Ignore a land held only by this Deploy:** it still cannot run until Deploy ends, so continuous lands remain broken. The holder may settle/fail while a live process survives; the land can become runnable during recycle. Multi-holder and stale-detail cases make an exception unsafe. A fast named refusal is useful; admission supplies it before waiting. |
| D-4 | **(d) Worktree plus separate canonical activation** is the selected fix. A linked worktree isolates files while existing land admission already permits its independent owner. Keep remote deployment checkout preparation under existing single-rollout ownership. | A separate clone adds credentials/cache/identity handling; a ReadOnly label is false for Git movement, evidence and deployment effects. A Worktree controller that still invokes canonical pull/restart would retain the race: forbid that in its dispatch contract. |
| D-5 | Require TASK-class mutation phases to prove their own task is Worktree and the invoked script root is its registered checkout. D-7 separately admits standing and interactive callers. Fail before any operator credential read, drain, SSH, Docker action, or 30-minute census wait. | A docs-only correction would allow the same explicit Shared invocation to recur. A new global prohibition on all Deploy/Shared tasks would break legitimate short canonical activation. |
| D-6 | Gate the five mutating rolling phases and `all`; TASK `all` is additionally refused by D-9; existing dry-run/resume restrictions remain. `check-census` and `check-host-jq` remain observational. `provision-host-jq` remains its existing canonical-only, separately commissioned short task. | Applying the Worktree rule to provisioning conflicts with its provenance contract; weakening that guard or allowing a worktree AppHost restart is out of scope. |
| D-7 | Classify from a bounded server-side token-resolution read, task first. Environment IDs are optional consistency checks and never select the principal. Admit authenticated standing sessions without inventing tasks; preserve explicit interactive mode with no identity supplied. | The public task-detail GET never binds its requested ID to the token. Environment-shape admission permits a Shared task to drop its task ID and pose as standing. Unknown tokens and unsupported principals fail closed; operator credentials remain independently required. |
| D-8 | Preparation activates a named reviewed rc SHA X before the controller starts. Keep land publication open; durable Active rollout state mechanically refuses ordinary AppHost restart unless the explicit expected SHA is X. Later rc activation is deferred; planned same-X recovery requires a quiescent journal, while the down-stack watchdog may recover at proven canonical X. Unknown state has only the explicit audited D-16 override. Desktop-runner restart remains a separately enforced operating restriction. Each mutating invocation requires `(GET /api/version).version == -Sha == X` before effects. Remove the unqualified "later activation" step; after joining/completing X, a separately commissioned preparation may target a newly named reviewed Y. | Rc lands may advance the rc ref to Y while detached canonical HEAD and API stay X; they remain allowed. Default-HEAD restart would silently activate Y, so health alone cannot admit the next phase. A Shared activation never waits for Held/Queued lands; a Running land is already protected by the dispatch repository lease. |
| D-9 | Phase-scoped controllers: one phase per task, each drain poll slice at most 30 minutes. Preserve X and a durable handover journal; the orchestrator dispatches a fresh Worktree successor only after a receipt proves command exit and joined owned children. The 240-minute task ceiling and 90-minute tool deadline are unchanged. | One task for the whole rollout routinely exceeds its ceiling (drain-old defaults to 480 minutes). No ceiling increase, automatic cancellation, or dependency on unlanded CARD-1610. Its future hold/recovery support may simplify exceptional live-session assessment, but normal handover uses this journal now. |
| D-10 | This is the caller's final Plan round with verification design folded in. Next is Code after CARD-1609 lands; no further Plan/TestDesign pass. Code collects bounded timing evidence, then ordinary Review evaluates the implemented production paths and complete receipts. | No separate TestDesign is left as an unstated prerequisite, and no product test execution or deployment authority is claimed by plan approval. |
| D-11 | Order the overlapping work **CARD-1609 first, CARD-1615 second**. Before Code dispatch, confirm CARD-1609 is actually landed on rc and master and record both exact SHAs; no landing status is inferred from its plan. Declare `scripts/deploy-server2.ps1` in Code scope and defer overlapping Code; implement this admission hook against the landed CARD-1609 retire-temp layout, recording its exact SHA. | Parallel edits or assuming today's line numbers survive that repair can lose the new retirement safety. This is the required scheduling order, not a claim that the other task has landed or agreed by message. |
| D-12 | Standing/interactive controller source and the canonical activation checkout are detached at X. A managed task keeps its registered private feat branch, as required by git-common; never put a land target branch in any controller worktree. Refuse symbolic standing/interactive HEAD and task branches outside the registered feat forms. | A controller on rc permits publication but leaves `target_checked_out_elsewhere` residue: the canonical checkout and its local rc ref do not advance, retaining the CARD-0358 stale-AppHost-source hazard. Detaching a managed task would violate its assigned-branch contract, so the existing private feat form is the narrow exception. A throwaway clone adds custody/caching overhead and is not the default. |
| D-13 | Maintain the existing doc-contract pins and include their affected classes in CP-4. Preserve unrelated authority, retention and branch-contract phrases. | Dropping pinned text without updating/checking its owner tests creates a predictable red build; a new duplicate prose-only suite is unnecessary. |
| D-14 | Make the watchdog marker-aware: recover a down stack with explicit X only when canonical HEAD equals X; alert on mismatch/unknown, preserve two-minute cadence, probe spacing, cooldown and flap cap. A failed restart stops controller renewal. | Waiting for marker expiry can keep AppHost down for a whole phase; implicit HEAD restart can activate Y. Recovery is same-X and still honors existing locks. |
| D-15 | Separate durable Active state from the five-minute heartbeat. Protect all phase gaps; allow a joined successor for 60 minutes, then alert/refuse until audited reconciliation/close. Refuse a new rollout over any unfinished state. | A liveness-only marker silently loses protection during normal dispatch gaps. Automatic abandonment or deadline extension guesses at remote outcome and permits interleaved rollouts. |
| D-16 | Explicit reason-bearing quarantine/one-invocation restart override for Unknown, with durable audit and retained bytes/disposition. Ordinary rollout admission stays blocked until audited close; valid Active cannot use this flag. | Permanent unknown-state refusal prevents recovery. Silent deletion, automatic override or a general unlock would erase evidence and permit version drift. |

### Caller admission (earlier review D-1)

Add `GET /api/agent-tasks/caller-identity` as a literal route before the existing
`/{id}` mapping. This endpoint has no caller-selected task/session ID. It reads
`X-Antiphon-Task-Token` only; require exactly one nonempty header value when
supplied, refusing duplicates/comma-joined ambiguity. Never accept a token in
query/body or echo it.
Return `Cache-Control: no-store` on success and error, at most 16 KiB with bounded
path fields, and a small DTO, never a full task/session entity:

| Server-resolved class | Response and admission |
|---|---|
| Task | `callerClass, taskId, sessionId, agentId, workspace, status, worktreePath, branch` from that token's task. Require Worktree, Dispatched/Working and its registered native non-main linked checkout; Shared refuses `RolloutControllerWorkspaceRequired`. Missing facts, Blocked/terminal task or mismatched supplied environment IDs refuse `RolloutControllerIdentityUnknown`. A missing task-ID environment variable changes nothing. |
| StandingAgent | `callerClass, sessionId, agentId` from a live session with a non-null `StandingAgentId`. No task-detail GET or synthetic task claim. Require clean pinned non-main controller source and existing operator authorization. Provided agent ID must match; any provided task ID refuses. |
| Interactive | No token header returns only `callerClass=Interactive`. The wrapper admits this class only when `ANTIPHON_TASK_TOKEN`, `ANTIPHON_TASK_ID`, `ANTIPHON_AGENT_ID`, `ANTIPHON_SESSION_ID` and `ANTIPHON_ORCHESTRATOR` are all absent (including no empty supplied values). Whitespace or partial identity is malformed. Existing operator authorization remains mandatory. |
| Unsupported / unknown | Unknown, empty supplied or stale credentials return generic 403 with no principal facts. A token-only unassigned card session (no StandingAgentId), a capability principal or ended session returns 403 `RolloutCallerUnsupported`; never infer standing from session existence or interactive from failed authentication. |

Implement the read-only projection in `AgentTaskService.cs` and its DTO in
`server/Application/Dtos/RolloutCallerIdentityDto.cs`; map it in
`server/Api/Endpoints/AgentTaskEndpoints.cs`. Factor the common token
resolver used by `AuthenticateAsync` so both remain task-first. The projection
uses no-tracking reads and must not stamp capability `LastUsedAt` or save changes:
the existing capability arm at lines 220-234 does write. Preserve current
AuthenticateAsync behavior for its existing callers; reject capabilities in this
read before its usage-stamp arm. Missing-header Interactive is intentional and
discloses no data; it is not an authenticated task claim. Reuse the existing
ForbiddenException response convention; do not use ResolvePollingCallerAsync,
which deliberately catches bad authentication. Use one bounded DB attempt under request cancellation; this read is not added to
the resilience retry registry. Bound cancellation and output;
never log headers, token hashes, full DTOs or raw exceptions.

The wrapper calls this route once per mutating invocation, before the operator
credential read. No public `GET /api/agent-tasks/{id}` can satisfy this proof.
A task token with `ANTIPHON_TASK_ID` dropped still resolves Task and Shared still
refuses; a forged task or agent ID cannot redirect the lookup. Test task-first
resolution even when the same fixture hash appears on a session row. A standing
token admits the composer's normal shape with no task-detail GET. A card session
without a standing owner must commission a fresh Worktree Deploy. Interactive
compatibility is not a sandbox against a same-account operator removing every
credential; neither filesystem markers nor operator-token custody promise that.

### Activation and phase SHA contract (review D-2)

1. The caller names full reviewed rc SHA **X**, completes any prerequisite lands
   before commissioning Shared preparation, and records the currently active SHA
   **P** separately. The short canonical task fetches the reviewed rc object and detaches
   canonical HEAD at X under its existing repository/Shared ownership, verifies
   clean tracked source and exact `HEAD == X` with no symbolic ref, and calls `restart-apphost.ps1 -ExpectedServerSha X`. If the reviewed rc selection changed before preparation, refuse/recommission
   with a newly reviewed named SHA. Fetch then detach at the selected immutable
   commit; do not pull on detached HEAD, reset/rebase rc, or move its published ref.
   Require `(GET /api/version).version == X` and health after restart. Any needed desktop
   runner build/restart and `provision-host-jq -Sha X` happen here, before the
   controller, with HEAD X; verify runner `buildVersion` against X. There is no
   invented `-ExpectedServerSha` parameter on the runner restart script.
2. The long controller uses X for every phase, receipt and resume. Lands may
   advance the rc ref X -> Y while canonical detached HEAD and API remain X.
   Do not reattach canonical HEAD to the moving target until rollout completion. Never pull/switch/build
   or restart the canonical stack from the controller. The deferred-activation policy applies to unrelated orchestrators too,
   through joined retire-temp completion. Planned same-X recovery requires a
   proven quiescent boundary. The watchdog exception below permits recovery of a
   down stack at the same X during a phase, subject to existing restart guards.
   The active-rollout marker below enforces the AppHost SHA restriction across
   phases. Rollout-active is durable across controller exits; heartbeat expiry never
   lifts the SHA restriction. A late handover alerts and requires recovery.
3. For all admitted caller classes, GET `/api/version` once per mutating invocation
   before operator-token lookup or effects. Read the DTO's `version` member (the SHA), require full 40-hex, normalize
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
4. If a planned restart is needed before completion, stop starting phases and establish
   the owned command's bounded outcome/exit and a quiescent receipt. Do not restart
   automatically from the controller or claim completion. Watchdog crash recovery
   is the narrow automatic same-X exception below. Caller commissions an explicit recovery or
   new rollout with a named SHA after reconciling both runner images and resume
   state; task status alone does not prove quiescence or release durable Active state.
   Unexpected mid-phase API drift is not detected continuously by this change;
   the next phase refuses. Existing phase/census refusals remain authoritative.
5. After joined completion at X, record active server/runner SHAs and retained
   receipt paths. A later upgrade is a new preparation targeting explicit reviewed
   **Y**, with `-ExpectedServerSha Y`, followed by its own rollout Y. There is no
   automatic post-drain restart to "whatever HEAD is now".

### Active-rollout marker and restart guard (review D-2)

Add `scripts/lib/active-rollout.ps1`, shared by the controller helper and
`scripts/restart-apphost.ps1`. Resolve the canonical main checkout from Git's
common-directory/main-worktree registration, not a fixed path or environment
override. Keep `logs/active-rollout.json` there, separate from the controller
checkout and retained phase receipts. The helper and restart script stay ASCII and
Windows PowerShell 5.1 compatible; avoid PowerShell-7-only JSON/filesystem APIs. Restrict it to a regular file under the
verified canonical logs directory; refuse links, oversized (>16 KiB), malformed,
unreadable or unsupported-schema content with `ActiveRolloutMarkerUnknown`.
Do not turn unknown content into absence or an expired-state permission. The explicit
quarantine/one-invocation recovery below is the sole unknown-marker override.

Schema 1: `rolloutId, sha, controllerId, callerClass, phase, sequence,
createdAtUtc, heartbeatAtUtc, expiresAtUtc, rolloutState, phaseState,
handoverStartedAtUtc, handoverDeadlineUtc, journalPath, journalGeneration`.
IDs/SHA and bounded local paths are validated; journalPath must equal the fixed
canonical logs/rollouts/<validated-rolloutId>/journal.json location. No tokens or task prose.
**rolloutState=Active is durable**, from first deploy-temp admission until joined
final retire-temp Completed or explicit audited Closed. ReadyForNext, SlicePending,
RecoveryRequired, process exit and heartbeat expiry all remain Active. Completed
and Closed are distinct retained terminal states; Closed is not rollout success.

Heartbeat every 30 seconds, liveness expiry five minutes after the last heartbeat,
UTC with one-minute future-skew tolerance; validate createdAt <= heartbeatAt and
expiry exactly heartbeatAt + five minutes. This reports controller health only.
At a joined ReadyForNext/SlicePending boundary set handoverStartedAtUtc once and
handoverDeadlineUtc exactly **60 minutes later**, persisted before predecessor exit.
Repeated reads/reports cannot extend it. Before that deadline a successor may
claim the exact joined generation. At/after it, emit `ActiveRolloutHandoverExpired`
to the same durable alert/Event Log sinks as watchdog refusal, retain Active and
refuse normal successor admission. No background timer is promised: every watchdog
fire (including healthy-stack fires) and every admission evaluates the deadline;
with the watchdog running, observation is on its two-minute cadence. A stale
in-phase heartbeat likewise alerts `ActiveRolloutControllerStale`; it never
proves process exit or releases SHA protection. Defaults are explicit operating
bounds, not measured test timeouts.

Create/renew atomically (same-directory temporary file plus replace) under a short
exclusive `logs/active-rollout.gate` file handle. Never use the marker as a long
blocking lock. A controller first validates identity/source, acquires the gate,
refuses an active AppHost launch/restart lock or **any unfinished rollout**, probes
the API version while holding the gate, then persists Active before any phase
effect. Write the initial marker first and journal second, both atomically; no
effects until both agree. A crash between files is Unknown/RecoveryRequired,
never absence. New rollout admission (deploy-temp without RolloutId) refuses
`RolloutAlreadyActive` naming the bounded prior rollout ID and journal, regardless
of heartbeat/deadline. A matching RolloutId admits only the specified successor
transition, not an arbitrary phase rerun. If the marker is absent, inspect bounded
canonical journal headers under the same gate (five-second total deadline,
64 KiB per journal): any nonterminal journal refuses; unreadable/partial inventory
is Unknown. An empty verified inventory alone permits first rollout admission.
Completed/Closed journals require their matching terminal receipts. Preserve the
previous terminal marker in its own journal directory before a new Active write.
Release the gate before credentials/phase effects.
Restart takes the same gate **before any kill/build/start**, reads the marker and
publishes its existing restart lock while still holding the gate. That ordering
closes version-check/create and restart-check/create races: controller sees the restart lock, or restart sees the
marker. Keep existing launch/restart lock semantics and cleanup. Probe and gate
acquisition have bounded deadlines; unknown gate ownership refuses.

Restart decision table (all existing worktree/source/lock guards still apply):

| Marker | Explicit `-ExpectedServerSha` | Result |
|---|---|---|
| Durable Active X (fresh, handover or stale) | Missing, malformed or different Y | Exit 3 `ActiveRolloutRestartRefused`, expected X/controller/phase printed; zero kill/build/start. Defaulting to canonical HEAD never supplies explicit consent. |
| Durable Active X (fresh, handover or stale) | X | Marker permits restart; existing source HEAD must still equal X. Planned recovery requires quiescence; watchdog recovery of a down stack follows the narrow exception below. |
| Heartbeat stale or handover expired, still Active X | Any | Emit the loud stale alert and apply the two Active-X rows above; time alone never permits Y or an implicit HEAD restart. |
| Verified absent with no incomplete journal, or proven Completed/audited Closed | Any ordinarily valid request | Existing restart behavior; retain terminal history. |
| Malformed, unreadable, unsupported-schema or future-skewed | Ordinary request | Exit 3 `ActiveRolloutMarkerUnknown`; recovery requires the explicit audited quarantine flag below. Unsafe canonical/gate path still refuses. |

A foreground controller owns its heartbeat runspace and joins/disposes it in
finally; never leave a background writer. Renewals serialize through the same
short gate and require rollout ID/controller ID/sequence equality, a still-live
heartbeat and no active restart/launch lock or unresolved recovery intent (even
if a quarantined/unreadable marker has since become readable). Check the renewal
failure signal before each new phase effect. Expired owners cannot silently renew;
re-admission needs the quiescent successor/recovery proof. Any journal/marker
generation mismatch after a partial write refuses; do not repair it by guessing. Renewal failure
stops new effects and records uncertainty; it does not authorize killing remote
work. Preserve command exit/census receipts and the bounded cancellation/join
policy. A successor renews only after the journal handover described below.
If a same-X restart fails with exit 4/5, its retained restart lock ordinarily
lasts 15 minutes. Heartbeat renewal must stop on that lock even before the
five-minute heartbeat expiry. This is intentional fail-closed phase custody:
record RecoveryRequired, join owned local children, and require reconciliation
before succession. Do not clear the restart lock or keep renewing to hide failure.
The existing two-fire dead-launch proof may reclaim a proven-dead lock earlier;
CARD-1615 neither bypasses that proof nor treats a retained lock as successful recovery.
At joined final retire-temp completion, write the completion journal/proof then
atomically mark rolloutState=Completed only if both files still belong to this
rollout/generation. A partial terminal transition remains protected until repaired;
never infer completion from task status. Retain historical receipts.
No automatic marker deletion, global DB lock, new service, or fleet location is
required. The narrow audited quarantine below does not unlock rollout effects. Direct dev-aspire/other restart paths are not covered by this
specific guard; operating policy continues to require restart-apphost as the front door.

### Explicit unknown-marker recovery (round-4 review D-3)

Add `restart-apphost.ps1 -QuarantineRolloutMarker -RolloutOverrideReason <text>
-ExpectedServerSha <full-sha>`. This is explicit recovery authority for a malformed,
unreadable, unsupported-schema or future-skewed marker only. The reason must be
1-512 printable characters; require the explicit expected SHA to equal canonical
HEAD and preserve all other restart guards. A valid Active marker, even stale,
**refuses this override**: use same-X restart or the audited close procedure.
The watchdog never supplies the flag. No interactive prompt or automatic override.

Retry transient marker sharing violations at most **3 reads total**, with bounded
100/300 ms waits inside a **2-second** read budget. Parsing/schema/ACL failures
are not retried. After that, ordinary callers fail loudly; an explicit override
revalidates Unknown under the same gate. Before effects, append an immutable
recovery intent under `logs/rollouts/recovery/<recoveryId>/` containing actor IDs
when available, UTC, reason, canonical HEAD/explicit SHA, marker path, classified
error, prior identity/hash when readable (otherwise null), and disposition.
Do not log raw malformed contents or filesystem exceptions. Print ERROR
`ActiveRolloutMarkerQuarantineOverride` and the receipt path; record the override
and final restart exit/version proof in the restart receipt, including failures.
Audit-write failure refuses before kill/build/start.

For a safe regular marker, atomically rename to
`logs/active-rollout.quarantine-<utc>-<recoveryId>.json` without deleting bytes.
If sharing/ACL denial prevents reading or renaming, record `PreservedUnreadable`
and leave the original in place: the same explicit request may proceed with its
one-invocation override after the durable audit succeeds. This avoids making a
locked corrupt file an indefinite ban on AppHost recovery. Never follow a symlink
or move a file outside canonical logs; an unsafe marker is left in place and its
safe parent-local audit records that disposition. Unknown canonical/gate ownership
or inability to write the audit still refuses; the runbook requires filesystem
ownership/access repair and a fresh explicit invocation, never silent bypass.

Quarantine does **not** mean rollout completion or absence. Every admission checks
for an unresolved recovery intent under the gate; it keeps ordinary restarts and
new rollout effects Unknown even if the marker was renamed. Only this invocation
may bypass that marker uncertainty, at the explicitly recorded HEAD; it cannot
bypass live restart/launch locks, source checks or release a running controller.
If a readable journal identifies X, print any requested recovery-SHA mismatch as
an explicit override risk in the receipt; never change that journal's SHA. All
existing controllers must stop effects on the changed/missing marker and require
reconciliation. A later ordinary watchdog fire alerts while the recovery is
unresolved; a further manual recovery needs a new reason-bearing receipt.

Document the full procedure in `docs/apphost-runbook.md`: inspect the bounded
refusal and owner evidence, use the flag from canonical source with the named
SHA/reason, retain quarantined bytes or unreadable-file disposition, verify
`/api/version` and reconcile remote rollout state. Extend `close-rollout.ps1`
with an exclusive `-RecoveryReceiptId <id>` parameter set for this case: require
that exact open receipt, hash-linked local/remote reconciliation and proof no
controller remains live, then audit Closed for identified unfinished journals
and resolve the recovery intent. Unknown rollout IDs are recorded as unknown,
never invented. No receipt resolution on restart success alone. This is the
explicit close/override path; a fresh rollout still needs its normal preparation.

CP-6 `Malformed_or_unreadable_marker_refuses` must witness ordinary refusal;
corrupt-file quarantine plus exact reason/audit and reached restart sentinel;
unreadable/unrenamable file plus PreservedUnreadable audit and one-invocation
recovery; bounded sharing retries; future skew; missing reason/wrong SHA and
valid-Active override refusals; audit failure yields zero effects. A second
ordinary restart/new rollout must still refuse until explicit reconciliation
closes the recovery intent. Native Windows exercises real sharing denial and
PowerShell 5.1; clocks/transports are inert. No new checkpoint methods are added.

### Watchdog same-X recovery (round-4 review D-1)

Inspected `watchdog-apphost.ps1:318-332` calls
`Invoke-AppHostRestartCaptured`; `scripts/apphost-common.ps1:600-613` currently
passes no expected SHA and watchdog exit 3 is merely logged without a restart
stamp. Add an optional validated `ExpectedServerSha` argument to that capture
helper and pass it as a literal child argument only when supplied. Preserve
existing no-marker behavior and the `-RestartScript` test seam.

The watchdog reads the shared rollout helper and canonical Git HEAD, using a
five-second total local observation budget. When its existing health classifier
finds a down stack and the durable active rollout names X, **HEAD == X permits
automatic recovery with `-ExpectedServerSha X`**, including during an active phase
or a handover. It never fetches, switches source, defaults to Y, or waits for the
controller to heartbeat. Same-X recovery cannot activate a different source SHA.
All existing worktree/clean-source, maintenance, launch/restart lock, Docker,
three-down-round, cooldown and flap-cap guards still apply. Restart re-reads the
marker and HEAD under admission; watchdog observation is not a lock or authority
to bypass a concurrent change. The controller stops effects on renewal failure
and reconciles its in-flight outcome before any successor, even if restart succeeds.

HEAD != X or unknown HEAD/marker must produce ERROR
`ActiveRolloutRecoveryRefused` with bounded reason, rollout/journal, expected X and
observed SHA (null when unknown), **zero restart-child calls**, and a durable
`logs/active-rollout.alert.json`. Emit a Windows Event Log alert through the
existing `Write-AppHostWindowsEvent` sink, with fixed kind
`rollout-recovery-refused`; no AppHost API dependency. Persist a condition/event ID
before sending and deduplicate Event Log attempts per condition; refresh local
last-observed time and log ERROR on each refused fire. Sink failure is loud and
does not suppress the other sink. Existing outage alerts and optional webhook
continue independently at 180 seconds; no new notification service or secret is
required. On a changed/resolved condition retain the audit and record resolution,
never erase the rollout's unfinished state. ProbeOnly logs the proposed action
and sends no alert or restart. Observe this condition after health observation
and before recovery guards so a retained lock/cooldown does not hide it. Intentional
maintenance still suppresses notifications/recovery as it does today; it does not
clear rollout state or extend its handover deadline. Healthy-stack fires evaluate
stale rollout deadlines independently, without calling restart.

Preserve cadence: scheduled fire every **2 minutes**; three down probes **15 seconds**
apart; **15-minute** cooldown after an actual attempt; **3 attempts / 60 minutes**.
A refusal consumes no restart stamp/flap budget. Re-evaluate HEAD and durable state
on the next scheduled fire (no tight retry loop); it must remain loudly refused
until corrected. Do not change the installer schedule. Qualify
`docs/bootstrap.md:579` and its CARD-1527 refinement, plus the watchdog/runbook
recovery text, with this policy.

Within CP-6's `Live_marker_requires_explicit_matching_sha`, drive the **real
watchdog** with its `-RestartScript` stub: HEAD X captures explicit X and one
attempt; HEAD Y/unknown captures zero attempts plus alert/log; a race changing
HEAD after observation is refused by real restart admission. Controlled-clock
fires at +2/+4 minutes prove repeated mismatch observation without restart
stamps, and recovery after correction respects the 15-minute/3-per-hour guards.
Test 15-second probe spacing through a controlled wait seam, never real sleeps;
verify installer cadence remains two minutes. In
`Heartbeat_and_completion_preserve_owner`, failed same-X restart exit 4/5 retains
the lock, stops renewal and forbids effects/succession until reconciliation.
No extra methods/rows: all six CP-6 methods remain non-parameterized; native
Windows vectors cover the actual Event Log call boundary with inert transports.

Add `tests/Antiphon.Tests/Scripts/ActiveRolloutMarkerTests.cs`, six methods:
`Live_marker_requires_explicit_matching_sha`,
`Durable_state_spans_handover_and_refuses_new_rollout`,
`Malformed_or_unreadable_marker_refuses`,
`Marker_and_restart_admission_are_serialized`,
`Heartbeat_and_completion_preserve_owner`,
`Successor_handover_requires_joined_receipt`.
Use actual helper/script admission, temporary canonical/controller Git roots,
a controlled clock and existing process-effect stubs. Assert no restart effects
on refusal; matching-X and completed/audited-closed controls reach the effect sentinel. Race tests
use barriers, never sleeps. Native Windows runs cover replacement/file sharing. Heartbeat/completion vectors
also exercise phase deadline, owned child exit/output drain, failed cleanup and
unknown remote outcome: only joined proven outcomes permit succession. No timeout
is credited as a successful remote completion.

### Checkout pinning (review D-4)

The code at `AgentTaskLandingProtocol.cs:557-563` inspects every Git worktree
registration. A non-main controller with `refs/heads/rc/...` checked out causes
`target_checked_out_elsewhere` **after publication**, regardless of token class or
Shared status in this canonical-advance check. This is LandedWithResidue, not a
pre-publication hold/refusal; a separate Shared claim can still hold admission.
The canonical checkout's own branch/local target ref stays behind the published
remote ref. A subsequent restart builds stale canonical files (CARD-0358), so a
successful land alone cannot prove the source was advanced or activated.
Standing and interactive controllers therefore use an owned linked worktree
created detached at X, and admission requires `git symbolic-ref -q HEAD` to report
detached with a valid exact HEAD, not merely a clean tree. Reject all symbolic
branches for these two caller classes as `RolloutControllerSourceMismatch`.
Do not turn an unreadable HEAD into detached success.

Managed phase tasks already sit on their own registered feat branch. Keep that
branch, cross-check it against the server-resolved identity, and reject rc/master
or any non-registered branch. This is the assigned-branch exception above, not
permission for a task to detach itself. No branch change is made by this Plan. The later canonical Shared preparation brief
must explicitly commission the main-checkout detach operation; it has no assigned
feat checkout to switch. The managed phase task's assigned branch remains untouched.

Canonical preparation likewise leaves its **main** checkout detached at X before
activation. Lands can then update target refs without moving source files beneath
the running/preparing stack. Verify no foreign worktree holds the selected rc
target before commissioning the rollout; never detach another task's tree.
After completion, another short canonical preparation fetches/detaches reviewed Y.
Update the normal rollout recipe's pull wording accordingly, preserving the
general ban `never pull while a land runs` outside this detached procedure.

Add vectors to existing `C1615_Native_checkout_identity`: a clean standing or
interactive controller on rc refuses before credentials, detached X admits,
unknown HEAD refuses, and a registered task feat branch admits without any
checkout mutation. Extend the real land witness to prove rc publication with
detached canonical/controller HEADs still at X; the target ref becomes Y.
The adversarial Worktree-controller-on-target fixture bypasses only the new
controller admission by setting up Git directly, then drives the real land
service/protocol: assert **remote publication Y**, terminal LandedWithResidue and
`CanonicalAdvanceReason=target_checked_out_elsewhere`, with canonical source/local
rc ref unadvanced. Assert the reason from the persisted operation, never a mocked
refusal. The positive detached fixture must publish Y and update local rc to Y
without this residue, while canonical/controller detached HEADs remain X.
`ScanTarget` returns `(null, null)` for an otherwise known detached main with no
foreign target holder, so canonical advance updates the target ref without moving
source files. This preserves named-X activation and avoids the stale local-ref
residue; later activation still deliberately selects reviewed Y. These are vectors within existing
methods, so the nine/five rosters do not expand.

### Phase tasks, bounded waits and successor recipe (review D-3)

Keep the current Deploy 240-minute ceiling. Commission **one phase per task**,
not `all`; TASK-class `-Phase all` refuses `RolloutPhaseScopeRequired` before
effects. Standing/interactive `all` retains its existing API with SHA recheck at
every subphase, but the operating recipe does not use it. Apply the same 30-minute
drain slice cap to all callers; reject explicit larger `-WaitIdleMinutes` as
`RolloutWaitBudgetExceeded`, and change its rolling default from 480 to 30.
Maintain overall drain allowances in the journal (old 480 minutes, temp 240);
successor dispatch never resets them. An exhausted overall allowance requires
recovery/decision under the existing runbook, not endless automatic new tasks.

The current `Invoke-HostCase` uses a synchronous child without its own deadline
(`deploy-server2.ps1:1108-1134`). S2b must replace that call with owned child
start/wait/output-drain/cancellation and joined cleanup, while preserving manifest
arguments and host proofs. Phase wall budget: 75 minutes including all census
waits, HTTP calls and host children, with a separate bounded two-minute local
cleanup/join. The local tool remains below its 90-minute deadline. Each task starts this phase
promptly and settles after it; do not batch phases merely because ceiling time remains. Every nested
wait consumes the remaining phase allowance; no reset at each host case. Drain
polling specifically stops at 30 minutes, with per-read deadlines consuming the
same remaining slice. These operating bounds are not test performance measurements.

A process deadline may stop/join owned **local** command children but cannot prove
remote SSH work stopped. If remote exit/outcome is not known from the host's
operation receipt, journal `RecoveryRequired`, never `ReadyForNext`. No remote
kill, destructive rollback, or fresh controller with permission to repeat effects
is inferred from a local timeout. This is the simpler fail-closed choice where
existing code cannot establish quiescence.

Add `-RolloutId <guid>` for continuity; first deploy-temp creates/prints it, later
invocations require it. Resolve its journal to canonical
`logs/rollouts/<rolloutId>/journal.json`; no arbitrary journal-path option.
It survives task-worktree cleanup. Under the marker gate, persist schema,
rollout ID/X, current and next phase, sequence, predecessor/current controller
IDs, caller class, task IDs, start/end UTC, overall drain budget consumed,
`ResumeRecycle` operation ID, saved-donor context, scoped project ID, durable
rolloutState plus handover start/deadline, source
receipt paths and hashes, original/retained evidence paths, exit code, joined
local-child proof, host outcome proof and `ReadyForNext/SlicePending/
RecoveryRequired/Completed/Closed`. Bound/sanitize fields and write atomically.
Never store tokens or arbitrary task prose. Full phase receipts remain in the
evidence roots; copy/hash the minimal handover proof into the durable journal
directory before settlement can remove the predecessor tree.

Successor procedure:

1. The foreground phase joins all owned children, stops/joins its heartbeat
   runspace, then records the outcome and handover deadline under the gate.
   Normal drain exhaustion emits `RolloutSlicePending`
   (exit 3, explicitly pending, never success), same-phase next action and spent
   allowance. It does not clear drain routing or redo drain POSTs on resume.
   Only that identified wait-boundary condition is pending; unrelated errors
   remain refused/recovery. Other phase completion advances next phase.
2. The orchestrator reads the full task report and journal, checks hashes, X,
   generation, command exit and local/remote proof as applicable. Failed task
   status alone is neither quiescence nor a reason to reject an otherwise fully
   joined command receipt; a live/unknown command always blocks succession.
3. Dispatch a fresh `-Role Deploy -Worktree -StartRef X -Platform Windows`
   with `-ProjectId <proven-project>` where needed, no existing-agent selection.
   Its brief names rollout ID, exact next/same phase, journal generation/hash,
   `-WaitIdleMinutes 30` (or remaining smaller budget), preserved
   `-ResumeRecycle`/`-SavedDonor` only where supported, and receipt roots.
   No CARD-1610 hold or predecessor Reply is required. Keep dispatching ordinary
   work through the existing continuous-dispatch rules.
4. Fresh token identity/source admission runs again. Under the gate, reject restart
   locks, probe API X, then compare
   the expected predecessor/generation and joined proof before assigning the new
   controller ID and incrementing sequence; a concurrent successor loses and
   refuses `RolloutHandoverConflict`. No foreign unfinished rollout can be stolen.
   The handover deadline must not have elapsed; a six-minute-old heartbeat does
   not invalidate a joined handover still inside its 60-minute allowance. API X,
   expected generation and retained proof remain mandatory. At/after 60 minutes
   normal resume refuses; use the explicit audited close/reconciliation below.
   No expiry interval permits activation of Y.
5. Final joined retire-temp writes Completed to journal/marker. Retain history;
   later activation Y is a separate short Shared preparation.

The handover witness uses two authenticated Worktree task fixtures with different
IDs and real local owned children. First drain reaches its bounded pending
outcome, records no repeated destructive effect and a joined receipt; successor
at X resumes the same phase, consumes remaining overall allowance, then advances.
Negative vectors: predecessor exits then clock advances **6 minutes** (implicit
restart still refuses, matching-X recovery and proven successor still work),
**60 minutes** (loud stale, implicit/Y restart and normal successor refuse), new
rollout over stale unfinished state (refuses with prior ID/journal), missing marker
with incomplete journal (refuses), and live predecessor, absent/changed receipt, wrong X/generation,
duplicate successor, exhausted allowance, unsupported resume, and remote outcome
unknown all refuse before effects. A terminal predecessor with proven joined
receipt succeeds, proving no dependency on CARD-1610. Extend the six-method
ActiveRolloutMarkerTests roster's handover method; use clock advancement and
barriers, never real 30-minute sleeps.

### Audited close of an unfinished rollout (round-4 review D-2)

Add `scripts/close-rollout.ps1 -RolloutId <guid> -ExpectedSequence <n>
-Reason <text> -EvidenceFile <local-reconciliation-receipt>`, a separately
commissioned short canonical recovery action, using the shared gate/helper.
Reason is required (1-512 printable characters); record actor/task/session IDs
when available, UTC, X, generation, previous state and receipt hash. Validate the
fixed canonical journal identity; a caller path supplies evidence, never selects
which state file to unlock. Require joined predecessor/local-child proof and
explicit reconciled remote outcome, runner images/drain routing and remaining
resume context. Unknown/live owners or mismatched evidence refuse. It performs
no kill, restart, Docker action, land, or new deployment. This is a local rollout
coordination close, not deletion of host deployment markers or donor material.

Write an immutable close receipt and journal Closed first, then marker Closed
under the gate; audit-write failure or partial transition retains protection.
Print a loud `RolloutExplicitlyClosed` including prior rollout/journal, reason
and receipt path. Never relabel it Completed or silently delete state. This
explicit path is also the recovery for a handover older than 60 minutes; it
requires caller reconciliation, not endless automatic redispatch. A subsequent
rollout needs a newly commissioned named-SHA preparation and must satisfy all
existing host/resume prerequisites; Closed grants no permission to repeat remote
effects. A healthy, timely joined successor is the ordinary path and needs no
close. CP-6's durable-state method asserts close rejects missing proof/audit
failure and permits a new rollout only after an exact audited Closed transition.

### Implementation scope and compatibility

S1 adds the four classes named below, extends only their reusable test fixtures
and stages tests first. S2 adds the identity DTO/route/service projection and
controller helper. S2b adds durable state, restart/watchdog hooks, audited recovery/close and the
bounded phase journal. S3 changes the nine instruction/API documents listed below and the two
existing doc-pin test methods. No migrations, project files, props/targets,
global.json, launch composer or landing/census policy change is commissioned.
AGENTS/skills are executable instructions for land classification: this is a build
land, not a docs-only skip, and requires ordinary Code/Review gates.

Use existing restart effect seams in `scripts/test-apphost-server-version.ps1`
as the model for CP-6's inert fixture; copy required helper dependencies into its
temporary source tree. The six CP-6 methods also cover existing no-marker
worktree/SHA/launch-lock refusal paths and `-NoBuild`/`-StopOnly`:
any unfinished mismatched marker must refuse before shutdown even for StopOnly;
WhatIf remains observational. Native Windows vectors run the helper/restart
admission under PowerShell 7 and Windows PowerShell 5.1. They are internal vectors
within the same six test methods, not extra checkpoint commands. Do not run any
script against the live canonical checkout.

## Implementation slices

**S1 — red tests first.** Add `tests/Antiphon.Tests/Scripts/DeployRolloutWorkspaceTests.cs`,
`tests/Antiphon.Tests/Application/DeployLandIsolationTests.cs`,
`tests/Antiphon.Tests/Application/RolloutCallerIdentityTests.cs` and
`tests/Antiphon.Tests/Scripts/ActiveRolloutMarkerTests.cs`. Extend the needed
fixture transport in `scripts/fixtures/c727-fake-http.ps1` and `C1008WrapperFixture`
in `RollingVolumeRecycleScriptTests.cs`; explicitly clear inherited task identity
agent, session and orchestrator identity in legacy interactive fixtures. Feed owner facts through the new token-resolved caller DTO,
never a test-only workspace bypass. Reuse `LandingSafetyHarness`, `LandingGitFixture`,
`C1008WrapperFixture` and the production wrapper/host census extraction in
`tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs`; adapt its helper visibility or
factor a test-only reusable harness if needed, without adding an admission bypass.
Use private real Git worktrees for native identity. Commit/push the tests and their
red outcome before implementing the guard.

**S2 — controller admission.** Add `scripts/lib/rollout-controller.ps1`, called by
`scripts/deploy-server2.ps1` after pure argument checks but before operator-token
lookup or operational effects. Classify D-7 with the new caller-identity GET for every mutating invocation.
Validate server-resolved task identity, Worktree, live status,
registered `worktreePath`, native filesystem/Git root identity, clean tracked
source and `HEAD == -Sha`. Reject wrong/unregistered trees and unknown identities;
require a registered non-main linked worktree and its expected private feat branch,
not just a DTO whose path happens to equal the main checkout. Compare canonical
identities, not path prefixes. New fixtures run the actual production script bytes
from their private registered worktree; they cannot bypass this proof. The actual controller is native
Windows, not a mirror whose registered path names a different machine. Standing/interactive
callers use the native clean pinned non-main source checks without a task DTO and
must have detached HEAD. Refuse any symbolic ref for those classes; a TASK must
match its registered feat/card-<n>-<stage>-<8hex> or feat/card-task-<8hex> branch.
Do not silently detach any caller or change its branch during admission. Preserve D-6 observational/provision exceptions and
D-7 interactive compatibility; never query an empty task ID.

Add the D-8 version probe to the same helper and the existing offline HTTP seam,
before credential lookup. For the `all` loop, revalidate version at subsequent
`Invoke-Phase` entries. Fixture state must explicitly return `version` matching
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
be saved. Identity read and Git probes have finite deadlines. The bounded identity API is
the only new HTTP surface; no indefinite retry, modal prompt, land predicate or census exception.

**S2b — marker and phase custody.** Implement the active marker/restart guard,
phase budget, bounded host-child wrapper, journal, quarantine and close in
`scripts/lib/active-rollout.ps1`, `scripts/lib/rollout-controller.ps1`,
`scripts/restart-apphost.ps1`, new `scripts/close-rollout.ps1` and
`scripts/deploy-server2.ps1`. Add marker-aware recovery to
`scripts/watchdog-apphost.ps1` and the optional literal expected-SHA forwarding in
`scripts/apphost-common.ps1`. Keep daemon/shared script edits ASCII/5.1 compatible. Do not edit landing/census classification or weaken
CARD-1609 retirement proofs. Commit/push after the S1 red evidence.

**S3 — operating contract.** Update `docs/docker-stack.md` continuous dispatch and
phase/source notes, `docs/orchestration-loop.md` Shared activation guidance,
`docs/apphost-runbook.md` source/recovery notes, `docs/bootstrap.md` watchdog
policy, `AGENTS.md` and
`.claude/skills/antiphon-delegate/SKILL.md` and
`.claude/skills/antiphon-orchestrator/SKILL.md`. Add the caller-identity route's
read-only/no-cache/auth contract to `docs/ops-http.md` and `docs/antiphon-api.md`.
Show fresh
`-Role Deploy -Worktree -StartRef <reviewed-rc-sha> -Platform Windows`; omit `-Runner`,
`-OnAgent` and `-Agent`. Keep its assigned branch and source frozen for its one phase;
report retained evidence and the journal's next-phase recipe before settling.
The task owns the local foreground process until exit; do not settle while a
command or heartbeat runspace remains in flight. Pass a proven
`-ProjectId` if canonical origin URL cannot resolve the worktree's project. Maintain the existing documentation contracts below. Use one active controller generation outside the runner being recycled, with
successive phase tasks owning successive journal generations.

Canonical-only steps: reviewed rc fetch and explicit detached checkout, AppHost build/restart, desktop runner
restart when needed, and `provision-host-jq`. The controller tree supports
`check-census`, `check-host-jq`, deploy-temp, drain-old, redeploy-old, smoke/cache
qualification, drain-temp and retire-temp. Those steps never pull/restart AppHost.
Preserve the existing canary, continuous-dispatch and final retire gates.

Replace these existing passages, rather than appending contradictory guidance
(line numbers refer to the inspected round-4 assigned base; refresh after CARD-1609):

| File / existing text | Required replacement |
|---|---|
| `docs/orchestration-loop.md:29-37`, "Before and after an AppHost pull or restart, verify that queued lands have finished" and "Wait for any land in progress before pulling" | "Before commissioning canonical preparation, the caller may wait for prerequisite lands. A Running land prevents Shared dispatch through the repository lease. Inside Shared preparation, never wait for Held or Queued lands, including those queued after dispatch: this task can itself hold them. Perform the named-SHA preparation, verify and settle; those lands then proceed. Outside this detached preparation, never pull while a land runs; the rolling phases need no land freeze. During rollout X, rc lands may advance the rc ref while canonical detached HEAD stays X, but later-SHA AppHost and desktop-runner activation is deferred until joined rollout completion; planned same-X recovery needs a quiescent journal and explicit X. The down-stack watchdog may recover at proven canonical X. Durable Active state refuses other ordinary restarts across handovers; expiry alerts and never unlocks. Unknown-marker recovery requires the explicit reason-bearing audited quarantine. Require API SHA X at each mutating phase; require HEAD and API equal the named activation SHA at preparation completion." Keep child-journal, lock and no-`AllowWorktree` rules. |
| `docs/docker-stack.md:473-474`, "From the canonical desktop checkout" for manual running-main cleanup | "From the pinned controller checkout on the Windows operational host, record main's status and exact candidates under that controller's printed rollout receipt path." Keep every existing running-main/ownership/publication/zero-work condition. The manual attested retirement path introduced by CARD-1609 also uses the controller tree; replace its canonical absolute evidence example with `<controller-root>/.antiphon/rolling-server2/manual-retire-<run>/`. This changes location, not cleanup authority. |
| `docs/docker-stack.md:513-514`, normal `retire-temp` "from the canonical desktop checkout" | "Run normal retire-temp from the pinned controller checkout with the same rollout SHA X and preserved resume context, after the phase identity/version admission checks." Preserve offline publication audit and strict volume proof. |
| `AGENTS.md:55`, "Restart and deploy scripts refuse ... when run from a linked Git worktree" | "`scripts/restart-apphost.ps1` and `scripts/deploy-local.ps1` refuse linked worktrees by default because they control the shared local stack. Their source is the canonical checkout. `scripts/deploy-server2.ps1` runs its rolling phases from the pinned controller worktree; only `provision-host-jq` requires canonical source. Desktop runner restarts remain canonical operations by policy; do not claim its script implements a linked-worktree guard. Never use `-AllowWorktree` for rollout preparation." |
| `.claude/skills/antiphon-delegate/SKILL.md:184` and following Shared-default bullets | "Fresh Workers and Orchestrators default to Worktree regardless of role or `-Dir`. An explicit existing-agent selection (`-Agent` / agentId) defaults Shared; explicit workspace and follow-up rules still apply. Use a fresh Worktree task for long rollout control; use an explicitly Shared, bounded task only for canonical preparation, with no pending-land wait inside it. Settlement commits the task's own footprint unless `-NoCommit`." Remove the stale implication that Shared is the normal fresh-task default. |

| `docs/orchestration-loop.md:15-21`, autonomy grant | Keep the grant's existing authority sentence, immediately qualify it: "During an active rollout, defer activation of later rc tips. Durable Active rollout state guards AppHost restart through phase handovers. Planned same-SHA recovery needs quiescence; the down-stack watchdog may recover at canonical X. Stale state alerts and remains protected; unknown state requires explicit audited quarantine." Keep human-only exclusions. |
| `docs/orchestration-loop.md:372-390`, post-land activation and terminal-card checks | Continue checking/reporting activation on every terminal move; while rollout X is active, record later landed Y as activation deferred and never claim its feature active. Commission Y preparation only after joined rollout completion. Same-X quiescent recovery uses explicit X; never default to moving HEAD. |
| `docs/orchestration-loop.md:622`, standing rule 7 | Replace the unconditional changed-rc-tip restart trigger with "restart for a changed reviewed rc tip only when no active rollout defers activation; during rollout follow durable Active state, planned quiescent recovery and the narrow watchdog same-X exception." |
| `.claude/skills/antiphon-orchestrator/SKILL.md:174-179`, landing step 6 | Add the same deferral before any pull/restart instruction, preserve post-rebase receipt identity, and close cards with desired/active SHA plus deferred status. Read marker before canonical movement. |
| `docs/apphost-runbook.md:196-220`, post-land recipe | Select reviewed rc X, never origin/master as deployment target; check marker before pull/detach/restart. Document durable mismatch exit 3, explicit same-X/watchdog allowance, stale alerts, the 60-minute handover deadline, audited close and the reason-bearing quarantine procedure, including unreadable-file recovery and unresolved audit custody. |
| `docs/bootstrap.md:579,637-651`, watchdog and dead-launch refinement | Preserve cadence, existing outage sinks, maintenance and dead-launch proof. Add HEAD X/expected X recovery, mismatch alerts, durable handover protection and a link to audited quarantine/close; no installer changes. |
| `AGENTS.md:53/56`, autonomy and current-rc statement | Qualify authority and current-tip activation by the rollout marker. Hosts stay on named rollout X while later rc lands publish; deferred activation is explicit. Keep the existing runner-restart boundary sentence unchanged. |

Also replace the opening docker-stack recipe's moving "current rc tip" with
"reviewed rc tip X selected and activated at preparation; keep that exact X for
this entire rollout". In its `cause=Http status=400` recovery at `:51-53`, require
prerequisite activation before gate 1, or quiescent recovery under D-8 if discovered
mid-rollout. Link the no-restart window and `-ExpectedServerSha X/Y` contract from
`docs/apphost-runbook.md`'s normal restart examples, so an unrelated recovery caller
sees it. Update the direct-orchestrator recipe to D-7's standing-agent environment
and controller checkout, not a made-up task ID. Manually review these operating
instructions and deliberately maintain existing documentation-contract tests; do not
invent a duplicate prose-only suite.

Live `GET /api/orchestrator-instructions` on 2026-10-30 returned version
`fed0edf3`. The generated text has no independent restart/activation instruction;
it does carry continuous dispatch with `-WaitIdleMinutes 240`. The phase-slice
recipe qualifies that as an overall drain allowance, never one task's wait.
`OrchestratorInstructionsRenderer.cs:239-244` emits these setting-provided standing
lines; there is no source literal to replace. Keep the live allowance and enforce
the smaller 30-minute per-task slice in the controller. Do not edit the generated
file, renderer or live operator settings. Refresh/read the effective text
before operational dispatch. `server/Bundles/orchestrator.md` delegates restart
policy to its owner anchor already; preserve that pointer.

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
with the recorded resume context. A Failed-but-live predecessor with no joined receipt requires an explicit
quiescence assessment using existing process/host receipts; fail closed until
that proof exists. CARD-1610 may simplify this later, but is not a dependency.
No automatic cancellation or kill is part of this repair. Merely installing this guard does not release an old owner.

### Existing documentation contracts (review D-5)

Search the replacement phrases and their readers before editing. Inspected
`StandingPipelinePolicyDocumentationTests.cs:134-157` pins the authority sentence,
queued-land wait and pull ban; `DockerStackDocumentationTests.cs:45-47` pins
`need no land freeze` and `never pull while a land runs` case-sensitively.
Keep those latter two phrases verbatim in their correct scope. Replace the
obsolete queued-land pin with `Inside Shared preparation, never wait for Held or
Queued lands`, and assert the durable-state guard/deferred-activation clause and narrow watchdog exception.
Keep the authority sentence and append its qualification; pin that qualification
in the same existing method, preserving its removal-must-fail assertion pattern.
Update DockerStackDocumentationTests' existing lands-proceed method to also pin
the phase-scoped and named-X operating contract. No new doc-test methods.

Do not remove `no worktree is half-reset`, `git pull --rebase` (general canonical
policy, outside detached rollout preparation), the child-journal sentence,
human-only exclusions, the exact runner-restart boundary sentence, `-NoCommit`,
`Delegation:CommitOnSettle`, the release-line spelling `rc/YYYY-MM-DD`, or
git-common/retention anchors. Preserve current non-rollout semantics when replacing
the five original passages. The delegate skill's Shared-default correction keeps
its commit-on-settle paragraph and routing/branch pointers.

CP-4 runs these eight existing classes across namespaces, with counts from source
attributes at the assigned base; parameterized methods expand as shown:

| Class / source under tests/Antiphon.Tests | Declared methods | Executed results |
|---|---:|---:|
| Application/StandingPipelinePolicyDocumentationTests.cs | 9 | 12 |
| Infrastructure/DockerStackDocumentationTests.cs | 16 | 16 |
| Application/InstructionBundleTests.cs | 58 | 78 |
| Application/RunnerBranchContractDocumentationTests.cs | 8 | 8 |
| Application/OrchestratorInstructionsGuidanceTests.cs | 5 | 6 |
| Application/ReleaseBranchModelDocumentationTests.cs | 2 | 2 |
| Application/CommitOnSettleDocumentationTests.cs | 2 | 2 |
| AgentContextContractTests in Application/AreaMapTests.cs | 2 | 2 |
| Total | 102 | 126 |

The instruction/routing/branch/settlement/release pins cover the touched short
copies even where their exact pinned text is retained. AgentContextContractTests
also keeps AGENTS under 24,576 raw UTF-8 bytes and its owner links valid. Other
readers found by phrase search pin unrelated sections (image credentials, jq,
mechanical PCs, scopes and release mechanics); preserve those sections. Code
reruns the phrase search on the landed CARD-1609 layout and updates the roster
before its green run only if that source adds a touched contract. The eight suffix
patterns have no class-name collisions in this checkout. Import is syntax proof,
not discovery or green evidence.

### Token-resolution API witnesses

Add `tests/Antiphon.Tests/Application/RolloutCallerIdentityTests.cs`, five
non-parameterized methods using a shared database template and per-test clones:
`Task_token_wins_over_session_and_missing_environment_ids`,
`Task_projection_contains_only_its_own_admission_facts`,
`Standing_and_unassigned_session_tokens_are_distinguished`,
`Unknown_partial_and_ended_credentials_fail_closed`, and
`Identity_read_never_writes_or_exposes_secrets`.
Exercise the real service resolver and the real endpoint handler/header policy,
including Interactive, duplicate headers, missing-header versus empty-header,
capability no-write, cancellation and fixed error output. HTTP tests can use an
in-process minimal route host once per run, without production Program/runner.
CP-5 runs these five methods plus all seven existing
`AgentTaskCallerResolutionTests` methods (12 results), preserving the ordinary
public polling/manual/capability behavior while the shared resolver is factored. S1 stages compileable black-box route tests first; absent
route/response is an intended assertion failure, never a fixture/build error.

## Verification design

Verification design is included for the explicit Code handoff after CARD-1609. Scope:
scripts plus server API/git integration; no messaging/client/pty behavior changes, no
live AppHost, production runner, SSH or Docker in tests. Existing test-project
references to server/infrastructure suffice; no project extraction is justified.

| ID | Production-path witness and observable assertion | Classification |
|---|---|---|
| V-1 | Shared own-task DTO; invoke real wrapper for each mutating phase and allowed resume/dry-run. Refuse before operator-token access, census sleep or any traced effect. Assert fixed reason and zero effects. Old wrapper reaches an effect/wait or lacks the new reason. | New red/green admission. |
| V-2 | Real pinned linked worktree, matching live DTO/source/API SHA succeeds. Wrong source SHA, tracked dirt, foreign tree and alias to canonical refuse. Rc ref moving X -> Y while canonical detached HEAD stays X with API X still admits controller X; API Y refuses X before credentials/effects. Missing/malformed/API-failure version refuses, including next-phase drift and subsequent `all` subphases. Equivalent native case/separator identity follows host rules. | Red/green negatives with positive native controls. |
| V-3 | Each D-7 caller class has its own method: task token takes precedence even with task ID dropped or forged agent ID; a genuine standing token is admitted with no task-detail GET; all-five-absent interactive remains admitted; a lone session ID or orchestrator marker refuses. Assert new receipt class for both admitted paths (red at base). Invalid/partial identity including token-only card sessions, identity 403/404/timeout, absent workspace/path, mismatched ID, Blocked/Failed owner and receipt failure refuse. Read checks and canonical provision retain outcomes. Sentinels never enter output/receipts. | Red/green plus compatibility. |
| V-4 | Real `AgentTaskLandService.RunAsync` and Git protocol (including the checkout-pinning publication-plus-residue and detached positive vectors): Shared Deploy in Dispatched/Working/Blocked holds rc and master requests with holder ID, no publication or extra attempt. Independent Worktree Deploy allows both lands while still Working; its source hash/HEAD remain fixed. | Existing-behavior regression/remedy witness, green at base. |
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
`bin-c1615-red/` and run eleven exact-method rows, one result each, in the Linux
lane using a scratch checkpoint manifest. The first seven filters are
`/*/Antiphon.Tests.Scripts/DeployRolloutWorkspaceTests/<exact-method>`:

- `C1615_Shared_controller_refuses_before_side_effects` (V-1, including task-ID removal with unchanged task token).
- `C1615_Native_checkout_identity` (V-2).
- `C1615_Phase_requires_rollout_server_sha` (V-2).
- `C1615_Unknown_or_terminal_owner_refuses` (V-3).
- `C1615_Standing_agent_without_task_id_is_admitted` (V-3).
- `C1615_Interactive_mode_requires_absent_task_identity` (V-3).
- `C1615_Admission_receipt_is_bounded_and_redacted` (V-3).

Add four more exact-method rows (one result each) to the S1 scratch manifest:

- `/*/Antiphon.Tests.Application/RolloutCallerIdentityTests/Task_token_wins_over_session_and_missing_environment_ids`.
- `/*/Antiphon.Tests.Application/RolloutCallerIdentityTests/Identity_read_never_writes_or_exposes_secrets`.
- `/*/Antiphon.Tests.Scripts/ActiveRolloutMarkerTests/Live_marker_requires_explicit_matching_sha`.
- `/*/Antiphon.Tests.Scripts/ActiveRolloutMarkerTests/Successor_handover_requires_joined_receipt`.

These are **eleven expected assertion failures**, not green results or PCs.
The two admitted caller classes go red on their missing new classification
receipt, not on a requirement to reject them. Stage all assertion/effect traces
before reading a possibly absent receipt so a missing file is reported as the
intended assertion, not a fixture error. The Shared vector must demonstrate the
old wrapper reaching its stubbed effect without accessing a real credential.
Commit the tests first, retain red receipts against that SHA, then commit/push S2
S2b and S3 before the green run. V-1..V-3 plus the four API/marker/handover methods above are the red-first witnesses.
V-4/V-5/V-6 and R-1 characterize existing behavior and are expected green with
unchanged landing/census production code. They do not prove the absent guard red.

The handover red method begins with an invocation using only existing phase
arguments, lets the old wrapper reach its stubbed effect/exit, and asserts the
missing joined journal as an assertion failure before any new-parameter resume.
Do not count an old-script parameter-binding error as red. API red methods map the
existing endpoint collection in the shared minimal test host and assert the
missing caller-identity response before parsing; no reference to the not-yet-added
DTO/helper may prevent S1 compiling. The four new classes are staged in S1,
including marker/API tests; S2/S2b add production code only after their red receipts.

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
first admitted effect: assert one caller-identity read, bounded phase completion, and refusal
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

Also pending after land: bypass task-first token resolution and run
`/*/*/RolloutCallerIdentityTests/Task_token_wins_over_session_and_missing_environment_ids`;
bypass only the durable-marker mismatch guard and run
`/*/*/ActiveRolloutMarkerTests/Live_marker_requires_explicit_matching_sha`;
bypass only the joined-predecessor requirement and run
`/*/*/ActiveRolloutMarkerTests/Successor_handover_requires_joined_receipt`.
These make six pending controls in total. Restore each mutation and run that same
method green; no product repair or PC execution is part of Plan. A-1's task-token
precedence and later-all-phase version check have ordinary witnesses; additional
all-phase mutation is not commissioned because the operating recipe forbids all.

### Checkpoints

Run once after S1, S2, S2b and S3 are committed (slice-group label S1-S3 includes
S2b). CP-1 builds; CP-2 through CP-6 reuse that output. All six rows execute in
the Linux lane. A separate native Windows task executes CP-1 and CP-6 with one
fresh isolated build, using `--rows CP-1,CP-6`. No ended Windows rc run supplies
this evidence. Each row reports its own fresh executed roster, failures/skips and
source/build receipt. These are the closed ordinary green selections; the eleven
S1 red rows and bounded timing calibration are the only additional executions.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial |
|---|---|---|---|---|---|---|---:|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1615/` | linux-controller / windows-native | `/*/Antiphon.Tests.Scripts/DeployRolloutWorkspaceTests/*` | V-1, V-2, V-3 | 9 named methods, 0 failed, 0 skipped | 9 | 6 | 9 | true |
| CP-2 | S1-S3 | CP-1 | linux-land-isolation | `/*/Antiphon.Tests.Application/DeployLandIsolationTests/*` | V-4, V-5, V-6 | 5 named methods, 0 failed, 0 skipped | 5 | 4 | 6 | true |
| CP-3 | S1-S3 | CP-1 | linux-census-regression | `/*/Antiphon.Tests.Scripts/C1364BoundLandWaitProofTests/*` | R-1 | 6 existing methods, 0 failed, 0 skipped | 6 | 2 | 3 | true |
| CP-4 | S1-S3 | CP-1 | linux-doc-contracts | `/*/*/(StandingPipelinePolicyDocumentationTests*)\|(DockerStackDocumentationTests*)\|(InstructionBundleTests*)\|(RunnerBranchContractDocumentationTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(ReleaseBranchModelDocumentationTests*)\|(CommitOnSettleDocumentationTests*)\|(AgentContextContractTests*)/*` | D-5 and all touched doc pins | 126 results in eight classes, 0 failed, 0 skipped | 126 | 1 | 2 | true |
| CP-5 | S1-S3 | CP-1 | linux-caller-identity | `/*/Antiphon.Tests.Application/(RolloutCallerIdentityTests*)\|(AgentTaskCallerResolutionTests*)/*` | D-1 real resolution and existing caller behavior | 5 new + 7 existing results, 0 failed, 0 skipped | 12 | 3 | 4 | true |
| CP-6 | S1-S3 | CP-1 | linux-marker-handover / windows-native | `/*/Antiphon.Tests.Scripts/ActiveRolloutMarkerTests/*` | round-4 D-1..D-3 watchdog, durable state, override, custody, successor | 6 named methods, 0 failed, 0 skipped | 6 | 3 | 4 | true |

Class/method OR operands are individually parenthesized and every operand ends
in `*`; table pipes are escaped. Check the intended suffix roster again after
Code adds the new classes. The existing eight CP-4 classes and
AgentTaskCallerResolutionTests have no suffixed sibling; the four planned new
class names are unused. Single exact filters elsewhere remain valid. Import
validates syntax, never existence or execution.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and its owner
`docs/test-performance-standards.md`. This Plan executes no product tests; only
an isolated, slot-gated checkpoint-tool bootstrap/import validates the document.
The proposed Linux acceptance total is **164 executed results**:
9 controller + 5 land + 6 census + 126 documentation + 12 caller identity +
6 marker/handover. Native Windows CP-1/CP-6 add **15 results** separately.
The confirmed original 9+5+6 roster is unchanged. Round-4 watchdog/cadence,
durable-state and override vectors live within the existing six CP-6 methods;
their added process/IO cost must be measured before retaining these provisional
3-minute Linux/4-minute Windows estimates. No real 2/5/15/60-minute wait is allowed
in tests; controlled clocks/waits and independent effect stubs cover those bounds.

| Lane / selection | Expected elapsed, including build | Phase allocation | Provisional execution deadline |
|---|---:|---|---:|
| Linux CP-1 | 6 min | build/restore 3; script/Git/HTTP fixtures 2.5; receipt/cleanup 0.5 | row 6 min; build floor exception below |
| Linux CP-2 | 4 min, reused build | shared DB readiness/template 1; clones/seed 0.5; Git/land/census 2; cleanup 0.5 | row 8 min |
| Linux CP-3 | 2 min, reused build | process startup/body 1.5; receipts/cleanup 0.5 | row 4 min |
| Linux CP-4 | 1 min, reused build | discovery/doc reads 0.5; receipts/cleanup 0.5 | row 2 min |
| Linux CP-5 | 3 min, reused build | shared DB/route host 1; clone/resolution/header vectors 1.5; cleanup 0.5 | row 6 min |
| Linux CP-6 | 3 min, reused build | Git/script startup 1; marker/race/journal bodies 1.5; cleanup 0.5 | row 6 min |
| Linux ordinary batch | 19 min | one build, six serial rows | charged 24 min; admission 5 min; hard wall 29 min |
| Native Windows CP-1 + CP-6 | 13 min | one build 5; controller 3.5; marker/path/journal 3.5; cleanup 1 | 10 min build/row override; charged 24 min; admission 5 min; hard wall 29 min |
| Linux S1 red batch | 10 min | one build 3; eleven serial exact-method rows 6; cleanup 1 | each row 2 min; charged 22 min; admission 5 min; hard wall 27 min |

**Measured p95: unknown, sample n=0 for these proposed selections.** These are
planning estimates, not measured costs or a compliance claim. CARD-1615 carries the
bounded calibration obligation: Code first looks for comparable retained
receipts; Code collects per-method/phase samples from required red/green runs and,
where needed, at most five targeted calibration executions of the slow new methods.
Do not repeat the full table solely for timing. Record sample count/source/host/load,
then set per-test/row/build bounds to about 2x measured p95, rounded to practical
units. Until calibrated, new script/API methods have a provisional 90-second cap and
new land methods 120 seconds; doc-only methods use a provisional 10-second cap; a matrix that cannot fit must split. Any retained
exception needs measured evidence and its tracking card. Existing long class
attributes are not permission to copy generous bounds.

Code imports YAML and sets `build.slotWaitMinutes: 5`, serial rows,
`timeouts.rowMinutes: 4` and CP-specific `timeoutMinutes: 6`, `8`, `4`, `2`, `6`, `6` for CP-1..CP-6 on Linux.
Use `run <manifest.yaml> --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6 --serial --total-timeout 24
--hard-wall-timeout 29 --expected-source-sha <committed-code-sha>` through the
checkpoint tool; the tool owns its build slots. For native Windows CP-1/CP-6 use their
own shared fresh build and `--rows CP-1,CP-6 --row-timeout 10 --total-timeout 24
--hard-wall-timeout 29`. Wait on the printed run ID until exit is not 75; an
observer ending is not executor completion. Use one run per committed slice group.
For the S1 scratch manifest use eleven exact rows sharing its one build, each
`minExecuted: 1`, `timeoutMinutes: 2`, `serial: true`, and the red batch total/wall
bounds above. Keep generated manifests/receipts ignored and report exact source SHA.

**Known tool limitation / CARD-1615 provisional exception:**
`RunScheduler.BuildDeadlineMinutes` floors a scheduled build at 15 minutes even
when row deadlines are smaller. It has no independent build-timeout field.
The Linux/red manifests therefore retain that 15-minute build deadline bounded
by their charged/hard-wall totals; do not claim an unsupported six-minute build
pin. Windows uses the supported ten-minute `--row-timeout` override for both
build and both native rows (a provisional ten-minute row exception to each
eight-minute target).
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
run checkpoint rows serially and do not co-schedule Pty/FakeClaude. Use production services with isolated database and fake external effects.
Only CP-5's minimal in-process route host is shared once per invocation; it never
boots production Program or contacts a live runner. Each row is a separate test
assembly invocation; reuse the external shared Postgres/template across them.

Retain ignored TRX, checkpoint source/build receipts and phase JSONL under a unique
`.antiphon/checkpoints/card-1615/<run>/` root. Join by run/test/group, SHA, OS, host
and category; record Docker/readiness (not applicable here unless shared DB startup
requires it), template/migration, clone/seed, Git/filesystem, HTTP, process startup,
body, teardown and wall remainder. Unknown is null, never zero. Time each caller-identity GET,
Git probe, census read and process cleanup separately. Provisional admission probes:
one caller-identity GET and one version GET each capped at 5 seconds, each Git probe at
5 seconds, whole admission 15 seconds (individual deadlines share that remaining
budget); owned child cleanup has a separate bounded join. Code refines these
from measurements without widening existing recycle waits. Any selection/batch/run
expected or observed over 30 minutes triggers Debug with source/load/phase evidence;
no full-suite or rc qualification is added to this card.

## Plan validation and remaining evidence

The existing census roster was inspected: six declared methods, no argument expansion.
The four proposed classes do not yet exist and have no source-name/suffix collision;
the exact CP-3 class has no suffixed sibling. CP-4/CP-5 OR expansion has been
checked against declared source classes; their proposed floors are 126/12.

**Round-4 validation at `4487a919d965474d67114407df31a21b79753369`:**
slot-gated isolated checkpoint-tool build succeeded in **6.45 seconds**, five
warnings, zero errors (lease `e7b33b2a-b4ab-40c5-9d1e-b86dbbf463bb`, wait 0 s,
held 7 s). Import exited **0**, **6 rows**, one shared build, floors
**9/5/6/126/12/6**, every row serial; whole-minute Linux estimates **6/4/2/1/3/3**,
Windows **9/6/3/2/4/4** (lease `a64ff302-925b-4f09-8c23-573631965210`, wait 0 s,
held 2 s). Generated YAML remains ignored at
`.antiphon/c1615-r4-plan-scratch/import.yaml`. Its default timeouts are superseded
by the explicit Performance budget before Code execution. **Zero product tests**
ran; this is document validation only. The 164 Linux / 15 Windows result totals
are proposed acceptance counts, not executed tests.

Source class-prefix audit found one exact match and no suffixed siblings for each
of the eight CP-4 classes, AgentTaskCallerResolutionTests and the CP-3 census class;
all four new class names remain unused. The added bootstrap watchdog text touches
only the existing StandingPipelinePolicyDocumentationTests owner pointer, already
in CP-4; KeepSix/Grok bootstrap readers pin unrelated sections and remain unchanged.
Evidence diff at that tip: six commits, zero entries, zero violations. Live GETs
`/api/runner-defaults` and `/api/session-runners` succeeded on 2026-10-30 at
04:41 UTC (revision 2; one Windows and two Linux entries); effective instructions
remain `fed0edf3`, revision 600. No host address is stored in the design.

**Round-3 validation at `ef75a59413125a3ba92424ade043104abc7e1700`:**
the slot-gated isolated checkpoint-tool build completed in 6.44 seconds, five
warnings, zero errors (lease `cf1f0e45-f743-475f-a0ba-bf743778b66b`, wait 0 s,
held 7 s). Import exited 0: **6 rows**, one build, floors **9/5/6/123/12/6**,
all serial, Linux whole-minute estimates **6/4/2/1/3/3** and Windows
**9/6/3/2/4/4**. Lease `4d9a0617-d23b-4770-b2a2-e00823918f2f`, wait 0 s,
held 2 s. Generated YAML stays ignored at
`.antiphon/c1615-r3-plan-scratch/import.yaml`. Default imported timeouts
18/15/15/15/15/15 are explicitly superseded by the Performance budget before
execution. This validates the document; **zero product tests** ran.
Evidence history check at that tip: six commits, zero entries, zero violations.
The final source roster audit corrected CP-4 from 123 to **126**: the complete
InstructionBundleTests file has **58** test methods and **25** Arguments rows on
five methods, giving 78 executed results. The earlier bounded file read had
under-counted it; that draft import remains historical syntax evidence only.
Final Linux total is 164; Windows remains 15.

Corrected import at `f6577ac815ec42c3bc56a99db4535fccc943113d` exited 0
with six rows and floors **9/5/6/126/12/6**, one build and all rows serial
(lease `49576291-78a7-4529-8be3-a7e19e5bb936`, wait 0 s, held 2 s).
Artifact: `.antiphon/c1615-r3-plan-scratch/import-corrected.yaml`.
The prior reconciliation import at `1cd067444` also accepted six rows (lease
`ddc97aac-d7ec-48cf-a1bd-70c7e2babff4`, wait 0 s, held 2 s); it still carried
the superseded CP-4 floor. Neither import ran product tests.

Round-3 route reads succeeded on 2026-10-30: runner defaults revision 2;
catalogue one Windows and two Linux runner entries; effective orchestrator
instructions version `fed0edf3`. No fleet address was added. Source audit also
corrected the inherited plan's nonexistent `/api/version.sha` member to the
actual JSON `version` member. This is a wire-contract correction, not a new API
version field or deployment.

**Historical round-2 validation:** at `25530ecc98293f6c81e6721de2d0dffcc45120d2`, the
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

**Historical round-1 evidence, retained without crediting it to later rounds:** at
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

Reproduce import from the assigned checkout; keep both projects' output inside
this task's ignored scratch root (the absolute path is resolved locally):

```sh
task_scratch="$(pwd)/.antiphon/c1615-r4-plan-scratch"
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1615-r4-import-build -SlotWaitMinutes 5 -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj "--property:OutputPath=$task_scratch/bin-tools/" --property:UseAppHost=false -nodeReuse:false --verbosity minimal
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1615-r4-import -SlotWaitMinutes 5 -- dotnet run --no-build --project tools/Antiphon.Checkpoints "--property:OutputPath=$task_scratch/bin-tools/" --property:UseAppHost=false -- import --plan docs/superpowers/plans/2026-10-30-card-1615-deploy-land-deadlock-plan.md --out .antiphon/c1615-r4-plan-scratch/import.yaml
```

Remove only the owned scratch `bin-tools/` after validation; keep the ignored
manifest/evidence. No output directory outside this task's scratch is removed.

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

Round-3 publication preflight at `ef75a59413125a3ba92424ade043104abc7e1700`:
fetched `origin/master=98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`;
`git merge-tree --write-tree origin/master HEAD` returned exit 1 with the same
four paths listed above. The same command against the round-3 assigned base
`30b76fbf22dc90161acbcf5622e5d4f6ba4fc57c` reproduced all four. No merge,
rebase, target push or conflict edit was performed; only this plan changes.
Do not land the whole rc-derived branch directly onto master. Code starts after
the caller confirms CARD-1609 on both target lines and records its SHAs.

Round-4 publication preflight at `4487a919d965474d67114407df31a21b79753369`:
fetched `origin/master=98fc3ceb6440a17b68cafe3ecc21c76d190b18ca`;
`git merge-tree --write-tree origin/master HEAD` exited 1 with the same four
paths listed above. Repeating against assigned base
`dbae0d65de7142ff61c8ec05cdd8b965d8b2f2f7` reproduced all four conflicts.
They are inherited rc/master divergence, not plan changes. The only tracked
delta is this plan; no merge, rebase, target push, deployment or product edit
was performed. The caller should use the rc plan and separately commission the
master port; this whole rc-derived branch is not directly landable on master.
