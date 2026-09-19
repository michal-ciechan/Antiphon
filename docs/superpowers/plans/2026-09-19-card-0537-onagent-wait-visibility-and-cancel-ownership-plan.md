# CARD-0537: A follow-up pinned to a busy agent is a visible hold, and a cancel retires only what it owns

Status: Plan, written under stated defaults (D-1 to D-8). Verification design is included (§Verification design), so the next stage is Code, sequenced after CARD-0535's Code lands (§Dependency on CARD-0535).

Authoring baseline: `0fb548da` (branch `feat/card-task-d383aee7`). Plan task: `d383aee7`. Card: CARD-0537 (Antiphon board). Source investigation: [`docs/investigations/2026-09-19-card-0537-follow-up-onagent-dispatch-silently-never-delivered.md`](../../investigations/2026-09-19-card-0537-follow-up-onagent-dispatch-silently-never-delivered.md) (commit `0fb548da`, landed to master). Sibling plan: [`2026-09-19-card-0535-dispatch-hold-visibility-plan.md`](2026-09-19-card-0535-dispatch-hold-visibility-plan.md) (commit `08c34ff1`, landed to master).

Owners to read before changing these areas: [orchestration](../../orchestration-loop.md) (holds, `Held` events, `-OnAgent`, `-Reply`), [ops HTTP](../../ops-http.md) (cancel and stop routes, `/api/attention`), [testing and build](../../testing-and-build.md) (TUnit filters, alternate output path), [project context](../../project-context.md) (layers, error shapes).

## Outcome and scope

The investigation's verdict stands: a follow-up (`-OnAgent`) pinned to a pool delegate whose current task settled Blocked reaches `TryReuseWarmAgentAsync`, gets `WaitForAgent`, and `DispatchOneAsync` rolls back and returns `false` with no event, no log line and no clock. The task holds a project cap slot forever. Three changes, two of them the fixes the card asks for and one the create-time guard it asks to be decided:

1. **`WaitForAgent` becomes a traced hold.** `DispatchOneAsync` reports it to the tick loop the same way CARD-0535 reports the lease hold; the loop writes one `Held` row per reason episode through `TraceHeldAsync`, naming the pinned agent and the task it is parked on, and records the hold for CARD-0535's `HeldAged` (300 s / 900 s) escalation and `DispatchHeld` attention row. Nothing new is invented for the clock.
2. **Create refuses a follow-up onto an agent parked on a Blocked task** with a 409 that names the Blocked task and the two recoveries (reply, or cancel). A follow-up behind a Dispatched/Working task still queues; that wait is supported and is now visible.
3. **`RemoveEphemeralAgentAsync` retires a pool agent only when the task being retired owned it**: never for a task that never ran on it, never while another open task still pins it. The sole-owner case keeps today's contract (row deleted).

Out of scope, per the brief: any change to how Blocked settles or releases the agent; the pool janitor; incident-parity retirement for task-driven paths (§Not done); `delegate.ps1 -Status` output (CARD-0535 D-9); `AgentControlService.StopAsync`; the follow-up's skip of the create-time open gate; the report-vocabulary question of whether `c40cf169`'s `blocked` verdict was honest.

## Ground truth

| Card premise or plausible shortcut | Confirmed behaviour at `0fb548da` | Design consequence |
|---|---|---|
| "The follow-up was never delivered; transcript discovery binding is suspected." | Investigation: the dispatch and reuse paths never read binding state; the silent branch is `ReuseOutcome.WaitForAgent` at `AgentTaskDispatcher.cs:4643-4644`, handled at `:3237-3241` by rollback and `return false`. | Fix the branch, not the binding. |
| No test covers the branch. | `AgentTaskPoolTests.a_pinned_follow_up_waits_while_its_agent_is_still_working` (`tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs:391-415`) drives exactly this branch (agent `Running`, `PoolIdleSince = null`, pinned Queued task) and asserts only that the task stays Queued. Nothing asserts silence or an event. | Extend that test with the one-row assertion (V-7) instead of duplicating it. |
| `WaitForAgent` has one producer. | Three: the pool pin (`:4643`), a standing always-on agent with no live session (`:4826`), and a standing agent busy on its live session (`:4887`). All three land in the same `DispatchOneAsync` arm. | One describer covers three text shapes (D-2). |
| CARD-0535 has landed the hold-tracing seam. | Plan landed (`08c34ff1`). Its Code task `b4ee43c2` was dispatched at 2026-09-19T01:45:35Z in a worktree and is still `Dispatched`; nothing of it is on `origin/master`; the running server reports `9404439a`. | Sequence this card's S1 after `b4ee43c2` lands; S2 and S3 do not depend on it (§Dependency on CARD-0535). |
| `TraceHeldAsync` dedupes per task. | It compares the new detail with the latest stored `Held` detail (`:713-726`, `lastHeld` from `:378-386`) and writes only on a change. | Hold text must be stable within an episode: no ages, no tick counts (D-2). A status change on the parked task is a genuine reason change and writes one row (D-3). |
| `RemoveEphemeralAgentAsync` deleting the pool row on a follow-up's cancel is a bug. | It is documented intent for the case the author had in mind: "a follow-up task pins a pool agent (so it is not ephemeral), but cancelling it must still retire that agent" (`AgentTaskService.cs:2373-2378`). The helper checks only `agent.IsPoolDelegate` (`:2380-2388`). It never asks whether the task ran on the agent or whether another open task still pins it. | Keep the sole-owner contract; add ownership checks inside the helper so every caller gets them (D-5). |
| The helper has one caller. | Five: `CancelAsync` `:1837` (unconditional), `BlockOnWallAsync` `:2193-2196` and `RequeueAsync` `:2326-2329` (only when `task.Ephemeral`), and the dispatcher's delivery watchdog `:1353` and `FailAndNotifyAsync` `:2355` (unconditional). | The guard lives in the helper, not in `CancelAsync`. |
| A follow-up is Ephemeral like any delegate. | `Ephemeral = request.AgentId is null` at create (`:1004`); a live follow-up carries `AgentId`, so it is never Ephemeral. The retry callers already skip the helper for it; cancel and the fail paths do not. | D-5. |
| Deleting the agent row only deletes the row. | `AgentIncidents.AgentId` cascades on delete (`AppDbContext.cs:217-221`). The boot-stall path already works around this by writing its incident after the fail with a null `AgentId` when the row is gone (`AgentTaskDispatcher.cs:1922-1931`). | The CARD-0537 incident rows survive under D-5 because the follow-ups never ran on the agent (R2). Full incident parity with the janitor is a follow-up (§Not done). |
| "Open" for ownership means Dispatched or Working. | The janitor's busy set is Queued, Dispatched, Working and Blocked (`:5110-5118`), and it excludes `Stopped` rows from every pass (`:5058-5059`). | R3 uses the janitor's set; R3 leaves the row's status untouched so the janitor's stale sweep or the next pinned dispatch handles it (D-5). |
| Create checks the agent before pinning a follow-up to it. | The live arm (`AgentTaskService.cs:349-386`) checks the agent row exists, the kind agrees and no env override is present. It reads neither the agent's status nor its open tasks. A live follow-up skips the create-time open gate (`:1055-1058`) yet counts as an occupant in `DelegationOpenGate` (`:20-25`). The caller sees only `FollowUpMessage: follow-up on the live agent`. | D-4 adds the one check that matters: an open Blocked task on that agent. |
| Reply works on any Blocked task. | `AgentTaskReplyService.AnswerAsync` requires `AgentSessionId` and enqueues onto it (`:300-330`); a dead session takes the answer nowhere. | The refusal names reply when the Blocked task's session is live and cancel when it is not (D-4). |
| `delegate.ps1` has a cancel verb. | It has `-Reply <taskId> "answer"` (`scripts/delegate.ps1:230-232`) and no cancel; cancel is `POST /api/agent-tasks/{id}/cancel`. | Text in D-2 and D-4 names the script for reply and the route for cancel. |
| A 409 with a code is a new shape. | `ConflictException(message, code)` (`server/Application/Exceptions/ConflictException.cs:12`) is how `concurrency_limit`, `model_disabled` and `provider_sign_in_required` refuse today; `delegate.ps1` prints the problem body and exits non-zero. | `follow_up_agent_blocked` needs no script change (D-4). |
| Test seams. | `AgentTaskPoolTests.CreateHarness(TimeProvider?)` (`:788`), `SeedWarmAgentAsync` (`:833`, returns agent and live session ids), `SeedQueuedTaskAsync(pinnedAgentId:)` (`:896`); `AgentTaskServiceIntegrationTests.SeedPoolAgentAsync` (`:1428`), `PinTaskAgentAsync` (`:1450`), `SeedTaskAsync(status:, sessionId:)` (`:1809`), `CreateService(db, stopper:)` (`:1852`) with `RecordingSessionStopper.Killed`. | Every guard seeds rows directly and ticks or calls the service; no wall-clock waits. |
| Existing tests pin the delete. | `AgentTaskDeadSessionReconciliationTests:698` asserts the pool row is gone after a sole-owner dead-session fail; `:374-398` asserts a standing specialist survives. | Both stay green under D-5: the failed task ran on the agent and nothing else pins it. |

## Dependency on CARD-0535

CARD-0535's plan (D-1) turns `DispatchOneAsync`'s `bool` into a three-valued outcome (`Dispatched`, `HeldOnLease`, `NotClaimed`), traces `HeldOnLease` in the tick loop through `TraceHeldAsync`, records every hold as a `(taskId, HoldKind)` for `EscalateHeldAgeAsync` (D-3), centralises hold strings in `DispatchHoldDetails` (D-5) and derives the `DispatchHeld` attention row from stored `Held`/`HeldAged`/`Dispatched` events (D-6). Its plan states that `WaitForAgent` lands in `NotClaimed` and "stays silent as today". This card changes that one sentence.

Integration contract (what S1 needs from the landed CARD-0535 code, by the names its plan uses; the Code stage verifies each against the actual landed source before editing):

1. The `DispatchOneAsync` outcome gains a fourth value, `HeldForAgent`, and the outcome carries the hold detail for held values (a `string? HoldDetail` beside the kind; if CARD-0535 landed a bare enum, widen it to a small readonly record struct in this card).
2. The tick loop handles `HeldForAgent` exactly as `HeldOnLease`: `TraceHeldAsync(task, detail, lastHeld, ct)`, the transition log line `Task {ShortId} held: {Detail}`, and a `HoldKind.PinnedAgent` entry in the per-tick held list. It is not excluded from escalation (only `RoutingPin` is).
3. `DispatchHoldDetails` gains the builders in D-2.
4. No change to `EscalateHeldAgeAsync`, `BuildDispatchHeldItemsAsync`, the settings, the enums or the client: the escalation and the attention row read stored events and are indifferent to which hold wrote them.

Sequencing: `b4ee43c2` and this card's S1 both edit `DispatchOneAsync` and the tick loop. Dispatch this card's Code after `b4ee43c2` lands. If Code is dispatched while `b4ee43c2` is still open, it does S2 and S3 first (independent files and tests), then waits for the land and rebases before S1. If CARD-0535 is abandoned, S1 implements the minimal seam itself under CARD-0535's names (the outcome type, the loop's trace, `DispatchHoldDetails`) so the escalation work can land on top of it later; V-5 and V-6 are then deferred to that card.

## Decisions

### D-1. `WaitForAgent` is reported to the tick loop and traced there

`DispatchOneAsync`'s `WaitForAgent` arm keeps its rollback and returns `HeldForAgent` with the detail from `DescribeAgentWaitAsync(claimed, ct)` (D-2). The loop traces it (contract item 2). The claim transaction is already rolled back and the repository lease is disposed when `DispatchOneAsync` returns, so the trace runs outside both, like every other hold.

The comment on the arm changes from "the pinned agent is mid-task" to say what is true: the pinned agent is not Idle, which for a pool delegate means its last task did not release it (Blocked keeps the session for the answer; `AgentTaskReplyService.cs:822-823`), and only a settle-and-release, a stop or a cancel changes that.

Rejected: converting the follow-up to Blocked or Failed after a timeout (the brief asks for the CARD-0535 mechanism; a Blocked follow-up would need its own answer path and would double the attention rows for one stuck agent); a caller-session note (AGENTS.md: visibility belongs on the attention feed, and the `DispatchHeld` row already reaches the orchestrator that polls it); having `TryReuseWarmAgentAsync` return the text (its callers and tests use the enum; a second query on the held path costs nothing on the normal path).

### D-2. The detail names the pinned agent and the task it is parked on, with no clock

`DescribeAgentWaitAsync` reads the pinned agent row and the newest open task on it other than the claimed one (`AgentId == pinned.Id && Id != claimed.Id && Status in {Dispatched, Working, Blocked}`, ordered by `DispatchedAt` descending, then `CreatedAt`). Builders live in `DispatchHoldDetails`:

- `PinnedAgentParkedOn(agentName, parkedShort, parkedStatus)` → `Held: pinned agent '{agentName}' is not idle; it is parked on task {parkedShort} ({parkedStatus}).` For `Blocked` the sentence continues: ` That task is waiting for an answer: reply to it (delegate.ps1 -Reply {parkedShort} "...") or cancel it (POST /api/agent-tasks/{parkedShort}/cancel); this follow-up dispatches when the agent is released to the pool.`
- `PinnedAgentNoOpenTask(agentName, agentStatus)` → `Held: pinned agent '{agentName}' is {agentStatus} with no open task and has not been released to the pool; stop it (POST /api/agents/{id}/stop) to relaunch, or cancel this task.` This is the shape of the sourced `verification_release_unresolved` arm and of the existing test at `:391`.
- `StandingAgentBusy(agentName, busyShort, busyStatus)` → `Held: standing agent '{agentName}' is busy with task {busyShort} ({busyStatus}) on its live session.`
- `StandingAgentNoSession(agentName)` → `Held: standing agent '{agentName}' (always-on) has no live session; waiting for supervision to restart it.`

Which shape applies is decided from the same facts `TryReuseWarmAgentAsync` and `PlaceOnStandingAgentAsync` used: `IsPoolDelegate`, `LiveSessionIdOfAsync`, and for a standing agent the busy query keyed on the live session (`:4880-4886`). No timestamps, no tick counters, no occupant lists.

Rejected: including the parked task's title (titles are edited by antiphon-diagnose after create, which would re-trace); quoting the agent id in the pool shape (the name is what the board and `agents` list show; the stop route needs the id, so that one shape carries it).

### D-3. A status change on the parked task is a reason change

The detail embeds `parkedStatus`, so a reply (Blocked → Working) writes one new `Held` row and the release writes the `Dispatched` row. Ticks with no change write nothing. CARD-0535's escalation measures from the first `Held` in the stint, so the retrace does not reset the age.

### D-4. Create refuses a follow-up onto an agent parked on a Blocked task

In the live arm of `AgentTaskService.CreateAsync` (after `followAgent` resolves and before the kind check), load open tasks pinned to `followAgent.Id` whose status is `Blocked` (the prior task included; ordered by `CompletedAt` descending, first one named). If one exists, throw `ConflictException(message, "follow_up_agent_blocked")`:

- session live (`AgentSessions` row Starting/Running for its `AgentSessionId`): `Task {priorShort} ran on agent '{name}', which is parked on Blocked task {blockedShort} waiting for an answer; a follow-up would queue behind it indefinitely. Reply to it (delegate.ps1 -Reply {blockedShort} "...") or cancel it (POST /api/agent-tasks/{blockedShort}/cancel), then re-send.`
- session not live: `Task {priorShort} ran on agent '{name}', which is parked on Blocked task {blockedShort} whose session is no longer live; a reply cannot reach it. Cancel it (POST /api/agent-tasks/{blockedShort}/cancel) and re-send; the follow-up then starts a fresh delegate with the prior task's inherited context.`

Nothing is written before the throw. A Dispatched or Working task on the agent does not refuse: that wait is the supported "settle → pool handshake" and S1 makes it visible. A Queued task on the agent (an earlier follow-up) does not refuse: queue order handles it.

Why refuse rather than warn: every other create-time condition that cannot resolve on its own refuses here (`concurrency_limit`, `model_disabled`, `provider_sign_in_required`, the kind mismatch two lines below), and a warned-and-queued follow-up would still occupy a project role slot with nothing that can free it but a human. The refusal costs the caller one command that they must run anyway.

Rejected: a warning in `AgentTaskCreatedDto.Warning` plus a `Warning` event (the slot is still held; the escalation would fire 300 s later on a task that was never going to run); making the follow-up act as the reply (changes `-OnAgent`'s meaning, and `-Reply` exists for exactly this); an override flag (no caller has a reason to queue behind a Blocked task; add one when a reason appears); refusing on any non-Idle agent (a follow-up behind a Working task is the normal case and the reason the wait exists).

### D-5. `RemoveEphemeralAgentAsync` retires only what the task owned

The helper keeps its name and its five callers; the rules move inside it, evaluated in order:

- **R1** (today): not a pool delegate → untouched.
- **R2**: `task.AgentSessionId is null` → untouched, Information log `Task {ShortId} never ran on pool delegate '{Name}'; leaving it as it is`. A Queued follow-up owns no process. This is the CARD-0537 incident shape: both follow-ups had `AgentSessionId = null`.
- **R3**: another task with `AgentId == agent.Id`, `Id != task.Id` and status in {Queued, Dispatched, Working, Blocked} → untouched, Information log naming that task and its status. The row's status is not changed: if the canceled task's session was the agent's current one, the next pinned dispatch relaunches the row through `SpawnFresh` → `ResolveAgentAsync` (the investigation's stop-path row), and once nothing pins it the janitor's stale sweep retires it. Stamping `Stopped` here would hide the row from the janitor forever (`:5058-5059`).
- **R4** (today): otherwise delete the row.

The helper's doc comment is rewritten to state the ownership rule and to cite CARD-0537 beside the original follow-up sentence, which stays true for the sole-owner case.

Rejected: guarding only in `CancelAsync` (the delivery watchdog and `FailAndNotifyAsync` call the helper unconditionally on the same shape); keying on `task.Ephemeral` (a pinned follow-up that ran on and now solely owns the agent must still retire it, which is the documented intent); incident parity with `FinishPoolRetire` (keeping incident-bearing rows as `Stopped`) — it changes every fail path's retirement and interacts with the boot-stall path's deliberate null-`AgentId` workaround, so it is a separate card (§Not done).

### D-6. Settle, release and the janitor are unchanged

Blocked still keeps the session and the agent `Running`; Succeeded still releases; the janitor still protects an agent with a Blocked task. This card makes the resulting wait visible and refuses to start a new one; it does not change what a Blocked task means.

### D-7. No client change; docs name the new hold and the new 409

The event type is `Held` (existing); CARD-0535's `HeldAged`/`DispatchHeld` are the escalation. `docs/orchestration-loop.md` §"Reuse first" gains three sentences: a follow-up behind a Working task waits visibly (`Held` naming the agent and task; `HeldAged` at 300/900 s; `DispatchHeld` on the attention feed); a follow-up onto an agent parked on a Blocked task is refused 409 `follow_up_agent_blocked` and the fix is `-Reply` or cancel; a cancel retires a pool agent only when the canceled task ran on it and nothing else open pins it. Gotcha #4 (`:1006-1008`) gets one CARD-0537 sentence beside CARD-0535's. `scripts/delegate.ps1`'s `-OnAgent` comment (`:109-113`) gains one line naming the 409. `docs/ops-http.md` names the code where `concurrency_limit` is listed, if it is; otherwise no change there.

### D-8. `delegate.ps1 -Status` stays as it is

CARD-0535 D-9 defers printing a Queued task's latest `Held` detail; this card inherits that deferral.

## Slices

### S1. `WaitForAgent` through the CARD-0535 hold seam (after `b4ee43c2` lands)

Files: `server/Application/Services/AgentTaskDispatcher.cs` (the `WaitForAgent` arm `:3237-3241` → `HeldForAgent` with detail; `DescribeAgentWaitAsync`; loop handling and `HoldKind.PinnedAgent`; the arm's comment), `server/Application/Services/DispatchHoldDetails.cs` (four builders from D-2).

Tests: `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs` (new `// ---- CARD-0537` section: V-1 to V-5; V-7 extends the existing test), CARD-0535's `DispatchHeldAttentionTests` (V-6, one case appended; a new `FollowUpAgentWaitAttentionTests` if that class has not landed).

### S2. Create-time refusal

Files: `server/Application/Services/AgentTaskService.cs` (live arm `:349-386`), `scripts/delegate.ps1` (comment only).

Tests: `tests/Antiphon.Tests/Application/AgentTaskServiceIntegrationTests.cs` (V-8 to V-11 beside the follow-up tests at `:1185`; V-12 re-runs the existing three).

### S3. Cancel ownership

Files: `server/Application/Services/AgentTaskService.cs` (`RemoveEphemeralAgentAsync` `:2373-2388` and its doc comment).

Tests: `AgentTaskServiceIntegrationTests.cs` (V-13 to V-17 beside the cancel tests at `:1002`), `AgentTaskDeadSessionReconciliationTests` re-run (V-18).

### S4. Docs

`docs/orchestration-loop.md` (§"Reuse first" `:430-445`, Gotcha #4 `:1006-1008`), `docs/ops-http.md` (conditional, D-7).

## Verification design

Ordinary verification (Code runs all of it; Review reads it; PCs are for Mutation). Guards are V-n; positive controls PC-n. Every dispatcher test uses `TestDbFixture.CreateIsolatedSchemaAsync()`, the `AgentTaskPoolTests` harness (`CreateHarness`, `RecordingSessionStopper`, seeds), `[Category("Integration")]`, and no wall-clock wait. Every service test uses `CreateService(db, stopper:)` from `AgentTaskServiceIntegrationTests`.

### Simulating the conditions

- **A pool agent parked on a Blocked task**: `SeedWarmAgentAsync(dir, Medium, idleMinutes: 0)` then set the row `Status = Running`, `PoolIdleSince = null` (as the test at `:391` does); seed task B with `AgentId = agent`, `AgentSessionId = the seeded live session`, `Status = Blocked`, `DispatchedAt` set, `CompletedAt` set; seed the follow-up F with `SeedQueuedTaskAsync(dir, Medium, pinnedAgentId: agent)`.
- **A reply**: set B `Status = Working` directly (that is all `AnswerAsync` changes that dispatch reads).
- **A release**: set B `Status = Succeeded` and the agent `Status = Idle`, `PoolIdleSince = now`, `PoolReservedForRootTaskId = B.RootTaskId` (what `ReleaseDelegateAsync` writes at `AgentTaskReplyService.cs:1767-1771`). The next tick reuses; the `Dispatched` event detail starts `Reused warm delegate`.
- **A standing agent**: seed as `SeedWarmAgentAsync` but `IsPoolDelegate = false`, `Status = Running`; the busy task T carries `AgentSessionId = its live session`. (`AgentNamePinTests` seeds standing agents; copy its shape if it differs.)
- **A long wait**: `CreateHarness(new FakeTimeProvider(T0))`; tick, `Advance`, tick. V-5 needs CARD-0535's `HeldAged`.
- **A dead session** for the create refusal: seed the Blocked task's `AgentSession` with `Status = Stopped`, or no session row.
- **An incident on the agent**: one `AgentIncident { AgentId = agent, Kind = TranscriptBoundByDiscovery, Severity = Info }`.

### Guards

| V | Slice | Test | Assertion |
|---|---|---|---|
| V-1 | S1 | `AgentTaskPoolTests.a_follow_up_behind_a_blocked_task_writes_one_held_event_naming_it` | Parked-on-Blocked setup. Three ticks → F `Queued`; exactly one `Held` row; detail contains the agent name, `Short(B)`, `Blocked` and `-Reply`; `TickResult.Dispatched == 0`; stopper killed nothing. |
| V-2 | S1 | `…the_hold_retraces_on_reply_and_releases_on_settle` | Continue V-1: reply → tick → two `Held` rows, newest contains `Working`; tick again → still two. Release → tick → F `Dispatched`; one `Dispatched` event starting `Reused warm delegate`; `Held` count stays two. |
| V-3 | S1 | `…a_pinned_agent_running_with_no_open_task_is_a_named_hold` | Agent `Running`, no task → one `Held` row equal to `DispatchHoldDetails.PinnedAgentNoOpenTask(name, Running)`; three ticks, still one. |
| V-4 | S1 | `…a_busy_standing_agent_writes_one_held_event` | Standing agent with T `Working` on its live session; F pinned → three ticks → one `Held` row equal to `StandingAgentBusy(name, Short(T), Working)`. Set T `Succeeded` → tick → F `Dispatched` with detail starting `Delivered into standing agent`. |
| V-5 | S1 | `…a_pinned_agent_wait_escalates_through_held_aged` (needs CARD-0535) | Parked-on-Blocked; ticks at T0 and +299 s → zero `HeldAged`; +300 s → one row starting `Warning:` whose detail contains the agent name; +900 s → second row starting `Error:`; +1000 s → still two. |
| V-6 | S1 | `DispatchHeldAttentionTests` case (needs CARD-0535) | Queued F with a `Held` row carrying `PinnedAgentParkedOn(...)` at now − 301 s → one `DispatchHeld` item, `Warning`, headline contains the agent name, `ConditionKey == dispatch-held:{F:N}`. |
| V-7 | S1 | existing `a_pinned_follow_up_waits_while_its_agent_is_still_working` | Add: exactly one `Held` row after the tick, equal to the no-open-task shape. Existing assertion unchanged. |
| V-8 | S2 | `AgentTaskServiceIntegrationTests.a_follow_up_onto_an_agent_parked_on_a_blocked_task_is_refused` | Pool agent A; prior `Succeeded` pinned to A; B `Blocked` pinned to A with a live session → `CreateAsync(FollowUpOnTask = Short(prior))` throws `ConflictException` with `Code == "follow_up_agent_blocked"`; message contains `Short(B)`, A's name and `-Reply`; `AgentTasks` count unchanged. |
| V-9 | S2 | `…names_cancel_when_the_blocked_tasks_session_is_dead` | As V-8 with B's session `Stopped` → refused; message contains `/cancel` and `inherited context`, not `-Reply`. |
| V-10 | S2 | `…a_follow_up_on_a_prior_task_that_is_itself_blocked_is_refused` | Prior `Blocked` pinned to A, live session → refused; message names `Short(prior)` as the Blocked task. |
| V-11 | S2 | `…a_follow_up_behind_a_working_task_still_queues` | B `Working` pinned to A → 200, `Status == Queued`, `AgentId == A`, `FollowUpMessage == "follow-up on the live agent"`. |
| V-12 | S2 | existing follow-up tests (`:1185-1300`) | Unchanged and green: pins the prior's agent; retired agent degrades with inherited context; never-ran prior degrades. |
| V-13 | S3 | `…cancelling_a_queued_follow_up_keeps_the_shared_pool_agent_and_its_incidents` | A `Running` with live session S; B `Blocked` (`AgentSessionId = S`) pinned; incident on A; F `Queued` pinned, `AgentSessionId = null`. `CancelAsync(F)` → F `Canceled`; A exists with `Status == Running`; the incident row exists; `stopper.Killed` empty. |
| V-14 | S3 | `…cancelling_a_queued_follow_up_on_a_warm_agent_leaves_it_for_the_janitor` | A `Idle` warm; F `Queued` pinned → cancel → A exists, `Idle`, `PoolIdleSince` unchanged. |
| V-15 | S3 | `…cancelling_a_running_task_keeps_its_agent_while_another_open_task_pins_it` | A with live session S; T `Working` (`AgentSessionId = S`) pinned; F `Queued` pinned → `CancelAsync(T)` → `stopper.Killed == [S]`; A exists, status unchanged; F still `Queued`. |
| V-16 | S3 | `…cancelling_the_sole_owner_still_retires_the_pool_agent` | A; T `Working` on S pinned; a `Succeeded` prior pinned (settled rows do not count) → cancel T → A row gone. |
| V-17 | S3 | `…retrying_an_ephemeral_task_keeps_an_agent_another_task_pins` | T `Failed`, `Ephemeral = true`, `AgentId = A`, `AgentSessionId = S`; F `Queued` pinned to A → `RetryAsync(T)` → T `Queued` with `AgentId == null`; A exists. (Seed as `retrying_a_failed_task_requeues_it_at_the_same_tier` does.) |
| V-18 | S3 | existing `AgentTaskDeadSessionReconciliationTests` (`:374`, `:698`), `cancelling_a_running_task_stops_its_delegate`, `cancelling_a_queued_task_settles_it` | Unchanged and green. |

Run commands (from the worktree; daemons hold `bin/`):

```
dotnet build --property:OutputPath=bin-c537/
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/AgentTaskPoolTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/AgentTaskServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/AgentTaskDeadSessionReconciliationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/DispatchHoldVisibilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/DispatchHeldAttentionTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/DelegationScopeHoldTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c537/ -- --treenode-filter "/*/*/AgentTaskDispatchBaseGuardTests/*"
```

Delete every `bin-c537/` directory afterwards. Do not run the full `Antiphon.Tests` assembly for this card (testing guide: class filters for a two-service change). Client tests are not affected (no client change).

### Positive controls (for the Mutation stage; method-scoped, red then green)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | The `WaitForAgent` arm returns the silent outcome (no detail, no trace) | V-1: zero `Held` rows; V-7 |
| PC-2 | `DescribeAgentWaitAsync` omits the parked task's short id | V-1: detail lacks `Short(B)` |
| PC-3 | The detail appends `UtcNow()` | V-1, V-3: three rows after three ticks |
| PC-4 | Create checks only `prior.Status` | V-8: not refused (B is another task) |
| PC-5 | Create refuses on `Working` too | V-11 |
| PC-6 | R2 removed | V-13, V-14: row deleted |
| PC-7 | R3 removed | V-15: row deleted |
| PC-8 | R3 counts settled statuses as open | V-16: row kept |
| PC-9 | `HoldKind.PinnedAgent` excluded from escalation (needs CARD-0535) | V-5: zero `HeldAged` |

## Risks

- **CARD-0535 lands with a different seam than its plan.** The contract above is written in its plan's names; Code reads the landed source first and adapts the four integration items. The invariants that the guards assert (one `Held` row per reason episode, escalation applies, attention row appears) do not depend on the names.
- **A follow-up created before S2 lands, already Queued behind a Blocked task.** S1 makes it visible on the next tick after deploy; nothing else changes for it. The residue named in the investigation (`c40cf169`, Blocked with a deleted agent and a dead session) should be canceled by the orchestrator regardless (§Not done).
- **R3 keeps a `Running` row whose session was just killed.** That is the investigation's stop-path shape and already handled: the pinned task's next tick relaunches through `SpawnFresh`, and the janitor's stale sweep retires it once nothing pins it.
- **Refusal text drifts from `delegate.ps1`'s verbs.** The text names `-Reply`, which exists, and the HTTP cancel route, which the script does not wrap; V-8 and V-9 pin both strings.

## Not done, noted

- Incident parity for task-driven retirement: keep a pool row with `AgentIncident` rows as `Stopped` instead of deleting it, as the janitor does (`FinishPoolRetire`). Touches every fail path and the boot-stall null-`AgentId` workaround (`AgentTaskDispatcher.cs:1922-1931`); a separate card.
- Housekeeping for the orchestrator: cancel `c40cf169` (`POST /api/agent-tasks/c40cf169/cancel`); its agent row is already gone and its session is Stopped, so a reply would go nowhere.
- A live follow-up counts against the project cap while skipping the create-time gate (`AgentTaskService.cs:1055-1058` versus `DelegationOpenGate.cs:20-25`). D-4 removes the case that made this hurt; whether Queued follow-ups should count at all is a separate question.
- `delegate.ps1 -Status` printing the latest `Held` detail (CARD-0535 D-9).
- `AgentControlService.StopAsync` on a pool delegate leaves the Blocked task pointing at a dead session with no signal; out of scope here.
