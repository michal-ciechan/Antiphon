# CARD-0492: A stale API-error stub must never settle a task whose session has moved on

Date: 2026-09-14. Stage: Plan (task `39f0a593`). Verification design is included below
(`## Verification design`), so the next stage is Code. Based on the Investigate report
[docs/investigations/2026-09-14-card-0492-task-fails-on-transient-api-error-while-session-recovers.md](../../investigations/2026-09-14-card-0492-task-fails-on-transient-api-error-while-session-recovers.md)
and checkout `dfa94af5` (origin/master at planning time).

## Outcome and scope

Three defects, one root: settlement judges a task from the **newest `TurnEnd`**, and while a
resumed turn is still open the newest `TurnEnd` is the API-error stub that killed the previous
turn. Everything downstream trusts that stale verdict.

1. **The reported one.** `HandleApiErrorTurnAsync` re-runs on every `AssistantText` of the resumed
   turn. Once the CARD-0072 ladder has resolved the stub's row `Superseded` (its own resume prompt
   is the later `UserPrompt`), the only two non-failing branches no longer apply and the task is
   written `Failed` with `Recovery ended (Superseded)`. Two Transient occurrences on 2026-09-14
   (`6bb32617`, `f14fb9d9`), one Unknown on 2026-09-12 (`d6fa8844`).
2. **The kill.** Release pools the live Shared session as a warm delegate on the DB-status-alive
   test alone, and the janitor retires it exactly `PoolIdleRetireMinutes` later on the idle clock
   alone. Both sessions were mid-turn, one in a chunked test sweep.
3. **A second, pre-existing strand that the fix for (1) would expose.** The ladder's resume prompt
   carries no task marker, so the resumed turn's own `end_turn` walks back to an unmarked prompt
   and is recorded as an uncorrelated report, never a settlement. Database evidence (§Ground truth):
   of the seven deferred-then-Succeeded tasks in history, **none** settled from its resumed turn;
   six settled only when the caller later typed a marked REFINEMENT prompt, one from a later
   marked brief. Fixing (1) alone converts "false Failed + kill 60 min later" into "Working until
   the role deadline". That is not a fix.

In scope: the guard in `HandleApiErrorTurnAsync` for every classification; the task marker on the
ladder's resume prompts; one housekeeping-aware "real prompt after the stub" predicate shared by
the ladder and settlement; a working-session gate on warm-pool retirement and warm reuse, plus a
warning at release; session liveness on the task detail, the failure reason and the parent note.

Out of scope, deliberately: any change to `IsWorkingAsync` itself; a new pool state beyond
Idle/Running/Stopped; a consumer for a report that arrives after a **genuinely** terminal row
(the fix keeps the row open instead); the idle auto-compact that produced no boundary at 13:09:14
(investigation uncertainty 3, separate card if real); the 13 Canceled deferred tasks
(uncertainty 4); summary-list liveness (D-9).

## Ground truth

Verified against `dfa94af5` and the local database on 2026-09-14.

| Claim (card, brief, or a tempting shortcut) | Evidence | Consequence |
|---|---|---|
| The fail arm has no "session moved on" branch. | `server/Application/Services/AgentTaskReplyService.cs:1074-1121`: unresolved → defer; Wall+capacity → hold; else `Failed` with `Recovery ended ({ResolvedReason})`. The `laterPrompt` guard exists only inside the `IsListGoverned && Wall` reroute branch (`:1043-1057`). | Move the guard in front of every branch. The Wall branch's inline copy becomes redundant and is deleted. |
| `Superseded` can only ever mean "a later prompt exists". | `ApiErrorRecoveryService.cs:255-263` (`FireOneAsync`) and `:390`/`:518` via `IsWallSupersededAsync` (`:596`) set it only after finding a later prompt or boundary. | After the guard, `Recovery ended (Superseded)` is unreachable by construction. No special case for the reason string; keep the fall-through as a defensive log. |
| Settlement runs off the **newest `TurnEnd`**, not the newest activity. | `ExtractMarkedTurnAsync` `:1910-1913`; walk-back to the last prompt before it `:1929`; stub verdict `:1980-1994`. Re-triggered by every `AssistantText` (`AgentSessionRuntime.cs:384` → `OnTurnEndAsync`). | The stale stub is re-handed on every narration of the resumed turn and by the 60 s sweep. The guard must be idempotent and cheap. |
| The ladder's resume prompt is a prompt the walk-back lands on. | `TranscriptPromptSpan.LoadAsync` (`server/Application/Services/TranscriptPromptSpan.cs:67-92`) keeps every `UserPrompt`/`QueuedUserPrompt` except the four housekeeping shapes (`IsHousekeepingPrompt` `:107`). `TransientPrompt` (`SupervisionSettings.cs:238`) is none of them and carries no marker. | The resumed turn's `end_turn` walks back to the resume prompt, fails the marker gate (`:1975`), and returns `UncorrelatedReport` → incident, no settle (`:143-155`). |
| Deferred tasks settle "normally when the resumed turn reports" (CARD-0072 plan, line 221). | DB: the 7 Succeeded tasks with an `ApiErrorDeferred` event all have `ReportEvidence = Marked`, never nudged. The prompt each settling turn walked back to: 6 × `[antiphon-task:…] REFINEMENT …` (`067aed13`, `0aa28b28`, `c46d9f5b`, `c7ff2da0`, `daf0a8c4`, `e73ea52a`), 1 × a later marked brief (`edabca77`). Session `e644b6cb` (task `0aa28b28`): resumed turn ended `end_turn` at seq 18 (22:36:01), task settled only after the caller's REFINEMENT at seq 19 (22:57:48). `DelegateReportUncorrelated` incidents on those sessions: `daf0a8c4` 1, `c46d9f5b` 1, `067aed13` 1, `edabca77` 5. | The CARD-0072 promise was never true. The resume must carry the marker (D-2) or the guard alone strands the task. |
| A marker-prefixed WhenIdle prompt is an established pattern. | `NudgeForClosingLineAsync` `:2640-2643` prefixes `TaskMarker(task.Id)` to its body; REFINEMENT does the same; `RepliedAtSequence` / `PreReplyBoundary` (`:1937-1942`) already handle a marked prompt that is not the brief. | D-2 is the settlement design's own correlation identity, not a new mechanism. |
| The ladder's own "later prompt" check is text-blind. | `FireOneAsync` `:255-258` counts any raw `UserPrompt` after the stub; `IsWallSupersededAsync` `:606-612` skips local-command and compaction-continuation records but only on the Wall path. The idle auto-compact typed `/compact` at 13:09:14, the same second as the resume. | A `<command-name>/compact</command-name>` record after a stub resolves a Transient row `Superseded` with no resume ever typed. With the new guard that becomes a strand, not a Failed. One shared predicate (D-3). |
| Release pools on "alive", never "working". | `ReleaseDelegateAsync` `:1601-1604` (`Starting`/`Running`) → `:1619-1631` `Idle` + `PoolIdleSince`. | Gate cannot be "do not pool": the pool state machine has no fourth state and CARD-0221 (session-runtime-invariants Gotcha #25) forbids "Running with no owner". Pool as today, warn, and make the janitor the gate (D-5). |
| The janitor kills on the idle clock alone. | `RetireIdleWarmAgentsAsync` `AgentTaskDispatcher.cs:4808`: retire set `:4817-4821`, cap surplus `:4828-4836`, kill loop `:4903-4913` → `KillPooledSessionAsync` `:5000`. No `IsWorkingAsync` call anywhere in the method. | Add the gate to both the TTL and the cap arms, with a silence bound so a stale mid-turn transcript cannot make a process immortal (D-5). |
| `IsWorkingAsync` is the fleet-wide working verdict. | `SessionMessageQueueService.IsWorkingAsync` / `IsWorkingBatchAsync` (transcript activity outranks the last turn end; interrupt marker, restart boundary and manual compact boundary count as ends). 20+ callers, including the delivery watchdog (`AgentTaskDispatcher.cs:1219`, `:1296`) and the agent cards (`AgentService.cs:152`). | Reuse it. Do not invent a second working rule. Its known trap (a relaunched row's stale mid-turn transcript) is handled by `WriteRestartBoundaryIfInterruptedAsync` (`AgentSessionService.cs:2711`). |
| Warm reuse can hand a brief to a mid-turn session. | `TryReuseWarmAgentAsync` `:4411-4437` filters on `Status == Idle && PoolIdleSince != null` only; the pinned path waits when `Status != Idle` (`:4403`). The brief is enqueued WhenIdle, so it types after the turn. | Harmless in delivery, wrong in accounting (the task is `Dispatched` against a turn it did not open). Skip working candidates in the unpinned shop (D-6). |
| The caller cannot see liveness. | `AgentTaskDetailDto` (`AgentTaskDtos.cs:263-318`) has no session status; `AgentSummaryDto.Working` (`AgentDtos.cs:42-44`) and `GET /api/sessions/{id}` are the other two endpoints; `delegate.ps1 -Status` prints `failed: <reason>` only (`scripts/delegate.ps1:557`); the drawer shows the reason alone (`client/src/features/delegations/TaskDetailBody.tsx:255-259`). The note header has no `session=` bit (`DelegationReportFormatter.BuildCompletionNote` `:553-596`). | One trailing DTO member, one header bit, one sentence in the reason (D-7, D-8). |
| The task row must be terminal for the report to be lost. | `SettleDeferredReportsAsync` scans `Dispatched`/`Working` only (`:2321-2323`); `OnTurnEndLockedAsync` returns when no open task (`:106-110`). | Keeping the row open (the guard) is what makes the resumed report consumable; D-2 makes it *settle*. |
| `Dispatched` vs `Working` after a skip. | `DeferApiErrorTurnAsync` promotes `Dispatched → Working` (`:1190`). Tonight's tasks never reached it (`6bb32617` was still `Working` from an earlier turn; a first-turn death would still be `Dispatched`). | The skip arm promotes too, and records the same once-per-stub `ApiErrorDeferred` event (idempotent on the `seq N` needle, `:1194-1210`), so the timeline shows the death and the resume. |

## Decisions

### D-1. The guard is the first verdict after adoption, for every classification

In `HandleApiErrorTurnAsync`, immediately after `EnsureAdoptedAsync` (adoption must still happen so
the ladder owns a stub settlement saw first), ask `TranscriptPromptSpan.HasTurnPromptAfterAsync(db,
sessionId, stub.Sequence)` (D-3). If true: the session has moved on. Promote `Dispatched → Working`,
write the `ApiErrorDeferred` event once per stub with detail
`turn killed by {classification} — session resumed at seq {N} (seq {stub})`, save, publish, log
at Debug on re-entry, and **return**. No incident (the sweep's adoption already raised one), no
parent note, no release, no kill.

Why every classification, including NeedsHuman and Unknown-exhausted: the defect is settling from a
stale boundary while a newer turn is live. A NeedsHuman stub followed by a real prompt means a
human or the caller already intervened; failing the row from the old stub would pool or kill the
session they are using. The newer turn settles the task (D-2), or the role deadline does.

Rejected: keeping the guard Wall-only and special-casing `ResolvedReason == Superseded`. The
reason string is a ladder fact, not a transcript fact; the transcript is the authority, and the
Wall branch's inline copy (`:1051-1057`) is deleted rather than duplicated a third time.

### D-2. The ladder's resume prompts carry the open task's marker

`FireOneAsync` looks up the session's open task (`AgentSessionId == sessionId && Status in
Dispatched/Working`, the same predicate as `OnTurnEndLockedAsync`) and enqueues
`{TaskMarker(task.Id)} {TransientPrompt|WallPrompt}`. A taskless session (an AlwaysOn agent) gets
the bare prompt, unchanged. The settings strings themselves do not change.

Effect: the resumed turn's `end_turn` walks back to a marked prompt, passes the marker gate, and
its final message settles the task with `ReportEvidence.Marked` (or is nudged, or is a question —
all the ordinary paths). A second stub on the resumed turn (`f14fb9d9` shape) walks back to the
marked resume, is classified as a stub of *that* turn, and defers on its own recovery row.

Rejected: (a) excluding supervision prompts from `TurnPrompts` by text — `IsHousekeepingPrompt`
is static, has no settings, and the codebase's rule is structural-not-text-matched; it would also
change the delivery watchdog's "started" answer. (b) Correlating the prompt to its
`Origin = Supervision` queue row by body equality — structural but a second correlation identity
next to the marker, and the queue row body is exactly what D-2 changes. (c) Retyping the brief —
re-runs the work.

### D-3. One "real prompt after sequence N" predicate, shared by ladder and settlement

Add `TranscriptPromptSpan.HasTurnPromptAfterAsync(db, sessionId, afterSequence, ct)` =
`LoadAsync(db, sessionId, dispatchedAt: null).TurnPrompts.Any(p => p.Sequence > afterSequence)`
— `UserPrompt` or `QueuedUserPrompt`, minus the four housekeeping shapes. Use it in D-1 and in
`FireOneAsync`'s Superseded check (`:255-263`). `IsWallSupersededAsync` is left alone (it also
counts boundaries and has its own tests).

Why: a `/compact` wrapper record or a compaction continuation prompt after a stub is not the
session moving on. Today it silently ends the ladder (`Superseded`, no resume typed); with D-1 it
would silently strand. The CARD-0412 V04 test already asserts this for the Wall path; D-3 makes
Transient/Unknown agree.

### D-4. Nothing new listens for a late report

Because D-1 keeps the row open and D-2 makes the resumed turn marked, the existing consumers
(`OnTurnEndAsync`, sweep arm 0 on a `[antiphon-report:]` token) carry the report. A row that is
terminal for a genuine reason stays terminal. Rejected: re-opening a `Failed` task when its session
later reports — reversible-terminal rows would break every "settled is settled" reader (land,
review evidence, cost roll-ups).

### D-5. The janitor never retires a mid-turn session; release warns but still pools

`RetireIdleWarmAgentsAsync`: before the kill loop, `IsWorkingBatchAsync` over the sessions of the
`retire` set (TTL **and** cap-surplus members). For a member that reads working **and** whose newest
transcript row is younger than `PoolIdleRetireMinutes`: remove it from `retire`, set
`PoolIdleSince = now` (the TTL restarts from the last moment it was seen working; the reservation
window moves with it, which is right — it is still that run's context), log one Warning
`Deferred retirement of warm delegate '{Name}': session {Id} is mid-turn (last transcript {At:O})`,
count it as acted so the save runs. A member that reads working but has been silent for a full TTL
is retired as today with a distinct log line naming the silence — a transcript that stopped an hour
ago is not evidence of work, and without this bound a tailer death makes a process immortal.

`ReleaseDelegateAsync` pool arm: compute `IsWorkingAsync` for the session; pool exactly as today
and log a Warning `Delegate '{Name}' pooled warm while session {Id} is mid-turn (task {ShortId}
{Status}) — the janitor will not retire it until the turn ends`. Rejected: leaving the agent
`Running` with no task (CARD-0221 zombie by construction; the stale sweep only sees ended sessions)
and a new `PoolState` (a fourth state every pool reader would have to learn for one transitional
case that D-1 makes rare).

### D-6. Warm reuse skips a mid-turn candidate

In `TryReuseWarmAgentAsync`'s unpinned shop, after the in-memory filters, drop candidates whose
session `IsWorkingBatchAsync` reads working. The pinned path already waits (`WaitForAgent`) when
the agent is not Idle; a working Idle pinned agent is the D-5 transitional case and is left to the
WhenIdle queue as today.

### D-7. Session liveness on the task detail, computed at read time

New record `AgentTaskSessionDto(Guid SessionId, SessionStatus Status, bool Working, DateTime
LastSeenAt, DateTime? EndedAt, DateTime? LastTranscriptAt)`; trailing optional member
`AgentTaskSessionDto? Session = null` on `AgentTaskDetailDto`, filled in `AgentTaskService.GetAsync`
from `AgentSessions` plus `IsWorkingAsync` when the session is `Running` (the same rule as
`AgentService.IsSessionWorkingAsync`, `AgentService.cs:149-151`). Null when the task has no session
or the row is gone. `delegate.ps1 -Status` prints one line:
`Session: <id> <Status>, working|idle; last transcript <ts>` or `Session: <id> ended <ts>`. The
drawer shows the same line under the Failed alert and, when Working, a "still working" chip.

### D-8. The failure note and the failure reason say what the session was doing at settlement

`BuildCompletionNote` gains an optional `sessionLiveness` string rendered as the header bit
`session=<live-working|live-idle|ended>`. `DeliverToParentAsync` fills it for `Failed` (and
`Blocked`) tasks from `IsSessionLiveAsync` + `IsWorkingAsync`. The API-error fail arm's reason
gains one sentence after "read session {id} before re-running this task": `At settlement that
session was {live and mid-turn|live and idle|already ended}; a Shared release pools it warm rather
than killing it.` The observation is taken before release, so "live" is true when the caller reads
it for a Shared task, and the sentence names the release consequence for a Worktree kill.

### D-9. Stated defaults

- No new settings. The silence bound in D-5 reuses `PoolIdleRetireMinutes`.
- No new incident kinds, event types, or DB columns; no migration.
- Summary-list DTOs do not gain liveness (a batch `IsWorkingBatchAsync` per list is a separate
  performance decision).
- `ApiErrorDeferred` is reused for the skip arm's timeline event (its needle idempotency is the
  point); the detail text distinguishes the two arms.
- The Wall reroute branch keeps its position and behaviour; only its inline guard is removed.

## Slices

### S1 — Settlement guard and shared predicate (fixes 1 and 3-ladder)

Files: `server/Application/Services/TranscriptPromptSpan.cs` (add `HasTurnPromptAfterAsync`);
`server/Application/Services/AgentTaskReplyService.cs` (`HandleApiErrorTurnAsync` guard first,
Wall branch inline guard removed, skip arm promotes and records the event — extract the
once-per-stub event write from `DeferApiErrorTurnAsync` into a private helper both arms call);
`server/Application/Services/ApiErrorRecoveryService.cs` (`FireOneAsync` uses the predicate).

Tests (`tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs` unless noted):
- `a_transient_stub_with_a_later_prompt_does_not_fail_the_task` — brief, stub (`server_error`),
  recovery row pre-resolved `Superseded`, later `UserPrompt` + `AssistantText`; `OnTurnEndAsync` →
  `Working`, no `Failed` event, no parent queue row, agent `Running`, stopper killed nothing.
- `a_transient_stub_with_a_later_queued_prompt_does_not_fail_the_task` — same with
  `QueuedUserPrompt`.
- `a_needs_human_stub_with_a_later_prompt_does_not_fail_the_task` — `authentication_failed`,
  NeedsHuman row; same assertions (D-1's "every classification").
- `the_skip_arm_records_the_death_once_on_the_timeline` — two `OnTurnEndAsync` calls → exactly one
  `ApiErrorDeferred` event whose detail contains `resumed at seq` and `(seq <stub>)`; `Dispatched`
  seed ends `Working`.
- `a_housekeeping_record_after_the_stub_is_not_a_resume` — `<command-name>/compact</command-name>`
  after the stub, row unresolved → the defer arm runs (existing behaviour), event detail says
  `resume scheduled`, not `resumed at`.
- `ApiErrorRecoveryServiceTests.A_local_command_record_after_the_stub_does_not_supersede_the_resume`
  — `/compact` wrapper after a Transient stub; the rung still fires one Supervision message and the
  row is not `Superseded`.
- Existing `A_later_user_prompt_resolves_superseded_and_does_not_fire` and
  `ComplexityWallRerouteTests.A_later_{UserPrompt,QueuedUserPrompt}_does_not_reroute_or_kill_an_in_flight_turn`
  stay green (their later prompts are real prompts).

### S2 — Marked resume prompt (fix 3)

Files: `server/Application/Services/ApiErrorRecoveryService.cs` (`FireOneAsync` open-task lookup,
marker prefix for both prompts).

Tests:
- `ApiErrorRecoveryServiceTests.The_resume_prompt_carries_the_open_tasks_marker` — session with an
  open `Working` task; the enqueued Supervision body starts with `[antiphon-task:<id>]` and ends
  with the configured `TransientPrompt`.
- `ApiErrorRecoveryServiceTests.A_taskless_session_gets_the_bare_resume_prompt`.
- `ApiErrorRecoveryServiceTests.The_wall_resume_prompt_carries_the_marker_too`.
- `AgentTaskReplyIntegrationTests.the_resumed_turns_marked_report_settles_the_task` — brief, stub,
  marked resume prompt, narration, `AssistantText` with `[antiphon-report:<id> done]`, `TurnEnd
  end_turn` → `Succeeded`, `Result` is the resumed turn's final message (stub text absent),
  `ReportEvidence.Marked`, one parent note.
- `AgentTaskReplyIntegrationTests.a_second_stub_on_the_resumed_turn_defers_on_its_own_row` —
  `f14fb9d9` shape: brief, stub 1 (row `Superseded`), marked resume, stub 2 → `Working`, an
  `ApiErrorDeferred` event naming stub 2's sequence, no `Failed`.
- `AgentTaskReplyIntegrationTests.an_unmarked_resumed_turn_is_still_uncorrelated` — the pre-D-2
  shape (bare resume prompt) documents the strand this slice removes: `Working` +
  `DelegateReportUncorrelated` incident. Keep it as the negative control for the marker gate.

### S3 — Pool gates (fix 2)

Files: `server/Application/Services/AgentTaskDispatcher.cs` (`RetireIdleWarmAgentsAsync`,
`TryReuseWarmAgentAsync`); `server/Application/Services/AgentTaskReplyService.cs`
(`ReleaseDelegateAsync` warning).

Tests (`tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs`; extend `SeedWarmAgentAsync` with
an optional transcript shape, or add `SeedMidTurnTranscriptAsync(sessionId)` = a `UserPrompt` newer
than any `TurnEnd`):
- `the_janitor_does_not_retire_a_warm_agent_whose_session_is_mid_turn` — idle 120 min, mid-turn
  transcript → not killed, row kept, `PoolIdleSince` within a minute of now.
- `the_janitor_retires_it_after_the_turn_ends_and_the_ttl_elapses_again` — append `TurnEnd`, set
  `PoolIdleSince` past the cutoff → killed.
- `the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl` — mid-turn transcript
  whose newest row's `CreatedAt` is older than `PoolIdleRetireMinutes` → killed (D-5 bound).
- `the_per_directory_cap_skips_a_mid_turn_agent` — five warm agents, the oldest mid-turn; the two
  retired are the oldest *idle* ones.
- `a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop` — one mid-turn and one idle warm
  agent in the directory → the idle one is taken; with only the mid-turn one → `SpawnFresh`.
- `AgentTaskReplyIntegrationTests.releasing_a_shared_task_whose_session_is_mid_turn_pools_it_and_warns`
  — genuine `Failed` (e.g. `a_needs_human_error…` shape with no later prompt) plus a mid-turn
  transcript after the stub is impossible by D-1, so seed the warning path through
  `FailUnreportedTurnAsync`'s grace shape or a direct `ReleaseDelegateAsync` call: agent `Idle`,
  `PoolIdleSince` set, logger captured a Warning containing `pooled warm while session`.
- Existing janitor tests (`the_janitor_retires_agents_idle_past_the_ttl`,
  `the_janitor_keeps_a_fresh_agent_warm`, `the_janitor_enforces_the_per_directory_cap_oldest_first`,
  `the_janitor_kills_a_recovered_worktree_delegate_after_the_ttl`,
  `AgentTaskSettlementRaceTests` FlushingSessionStopper) stay green: their seeded sessions have no
  transcript and read idle.

### S4 — Liveness surfaces (fix 3-caller)

Files: `server/Application/Dtos/AgentTaskDtos.cs` (`AgentTaskSessionDto`, detail member);
`server/Application/Services/AgentTaskService.cs` (`GetAsync`);
`server/Application/Services/DelegationReportFormatter.cs` (`sessionLiveness` bit);
`server/Application/Services/AgentTaskReplyService.cs` (`DeliverToParentAsync` fills it; API-error
fail arm sentence); `client/src/api/agentTasks.ts`, `client/src/features/delegations/TaskDetailBody.tsx`;
`scripts/delegate.ps1` (`-Status` line); `docs/antiphon-api.md` (detail DTO field).

Tests:
- `AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_a_live_working_session`
  — `Running` session with a mid-turn transcript → `Session.Status == Running`, `Working == true`,
  `LastTranscriptAt` set.
- `…_for_an_ended_session` — `Stopped` with `EndedAt` → `Working == false`, `EndedAt` set.
- `…_is_null_without_a_session`.
- `AgentTaskReplyIntegrationTests.a_failed_api_error_task_names_session_liveness` — NeedsHuman,
  no later prompt, `Running` session with a mid-turn transcript → `FailureReason` contains
  `live and mid-turn`; the parent queue row's header contains `session=live-working`.
- `DelegationReportFormatterTests` (or the nearest existing formatter test file):
  `the_header_carries_the_session_bit_only_when_supplied`.
- `client/src/features/delegations/TaskDetailBody.test.tsx`:
  `shows session liveness under a failed task` and `omits it when the detail has no session`.

### S5 — Documentation

Files: `docs/session-runtime-invariants.md` (new bullet next to Gotcha #25: a stale API-error stub
never settles a task once a real prompt follows it; the ladder's resume carries the task marker;
the janitor never retires a mid-turn session and a release that pools one warns);
`docs/orchestration-loop.md` (the warm-pool paragraph at `:386` and the delegate status output:
`session=` bit, `Session:` line, what `Recovery ended (…)` can still mean);
`docs/agent-card-lifecycle.md` only if it describes the defer arm (grep found nothing; skip
otherwise). Update the CARD-0072 plan's line 221 with a pointer to this plan (the promise was false
until S2).

Order: S1 → S2 → S3 → S4 → S5. S1 and S2 must land together (S1 alone strands). S3 and S4 are
independent of each other and of S1/S2; each slice is its own commit with its tests green,
committed before any long run.

## Verification design

### Code-stage execution clarification (2026-09-15, task db8d01f9)

The current stage contract and testing Fast lane supersede the historical full-namespace sweep
below. Ordinary verification builds once into `bin-c492/`, then runs Unit and these bounded
integration classes from that output. Fresh TRX files must contain every intended method/class:

| Coverage | Classes |
|---|---|
| Adoption, housekeeping, marked recovery | `ApiErrorRecoveryServiceTests` |
| Settlement, idempotence, release, parent note | `AgentTaskReplyIntegrationTests`, `AgentTaskSettlementRaceTests`, `ComplexityWallRerouteTests` |
| Pool retirement/reuse and dispatcher regressions | `AgentTaskPoolTests`, `AgentTaskCheckScheduleTests`, `GrokDelegateDispatchTests` |
| Read-time liveness | `AgentTaskDetailBlockedContextTests` |
| Header rendering | Unit lane including `DelegationReportFormatterTests` |
| Drawer liveness | Client suite including `TaskDetailBody.test.tsx` |

V-1 through V-23 also get exact-method ordinary runs; V-24 is the two named Vitest cases.
R-1 through R-14 below are deliberate mutations (PCs), not ordinary regression cases. All remain
pending for explicitly commissioned post-land SourceLanding Mutation after ordinary Review.
S1+S2 stay together on this original Code branch. Restart target: server; landing owner: db8d01f9.

Clarifications found during implementation: V-22's table is authoritative (`live-idle`); S4's
earlier `live-working` fixture is impossible after D-1. Cap protection must select replacement
idle members to satisfy V-16, not merely remove protected members from the original retire set.
The original hand-seeded V-10 would not detect the producer-prefix mutation R-5. Code closes
that gap by firing the real recovery service, confirming its queued prompt in the delegate's
transcript, then appending the resumed final report without another prompt. V-22 additionally
flushes the failure note through the real queue and checks its matching parent UserPrompt.

Build to an alternate output path (`--property:OutputPath=bin-c492/`, forward slash) while the
daemons hold `bin`. Run TUnit with `dotnet run --project tests/Antiphon.Tests -- --treenode-filter
"/*/*/<Class>/<Method>"`; the classes below are all in `Antiphon.Tests.Application`. Full-assembly
runs are chunked by namespace and run once at the end.

### Cases

| Id | Slice | Test (class.method) | Fixture | Assertions |
|---|---|---|---|---|
| V-1 | S1 | `AgentTaskReplyIntegrationTests.a_transient_stub_with_a_later_prompt_does_not_fail_the_task` | `SeedDispatchedTaskAsync` (Shared, pool agent), `SeedApiErrorStubTurnAsync(server_error, 500)`, `ApiErrorRecoveries` row `Classification=Transient, ResolvedReason=Superseded, ResolvedAt=now`, then `UserPrompt` "continue" + `AssistantText` | status `Working`; 0 `Failed` events; 0 parent queue rows; agent `Running`; `RecordingSessionStopper.Killed` empty |
| V-2 | S1 | `…_with_a_later_queued_prompt_…` | as V-1 with `QueuedUserPrompt` | as V-1 |
| V-3 | S1 | `…a_needs_human_stub_with_a_later_prompt_…` | `authentication_failed`, row `NeedsHuman` resolved | as V-1 |
| V-4 | S1 | `…the_skip_arm_records_the_death_once_on_the_timeline` | V-1 fixture, `Dispatched` seed, `OnTurnEndAsync` ×2 | exactly 1 `ApiErrorDeferred`; detail contains `resumed at seq` and `(seq N)`; status `Working` |
| V-5 | S1 | `…a_housekeeping_record_after_the_stub_is_not_a_resume` | stub, unresolved row, `UserPrompt` `<command-name>/compact</command-name>` | defer arm ran: 1 `ApiErrorDeferred` with `resume scheduled`; `Working` |
| V-6 | S1 | `ApiErrorRecoveryServiceTests.A_local_command_record_after_the_stub_does_not_supersede_the_resume` | `SeedTransientStubAsync`, sweep, insert `/compact` wrapper, advance 1 min, sweep | 1 Supervision message; row `ResolvedReason` null, `AttemptCount` 1 |
| V-7 | S2 | `ApiErrorRecoveryServiceTests.The_resume_prompt_carries_the_open_tasks_marker` | harness session + `AgentTasks` row `Working` on it | body `StartsWith(TaskMarker(id))`, `EndsWith(TransientPrompt)` |
| V-8 | S2 | `…A_taskless_session_gets_the_bare_resume_prompt` | no task | body `== TransientPrompt` |
| V-9 | S2 | `…The_wall_resume_prompt_carries_the_marker_too` | session-limit Wall stub with a reset, open task | body starts with the marker, ends with `WallPrompt` |
| V-10 | S2 | `AgentTaskReplyIntegrationTests.the_resumed_turns_marked_report_settles_the_task` | brief, stub, `UserPrompt` = marker + `TransientPrompt`, `AssistantText` narration, `AssistantText` report ending `[antiphon-report:id done]` (own `ApiCallId`), `TurnEnd end_turn` same `ApiCallId` | `Succeeded`; `Result` == report text; `Result` does not contain the stub text; `ReportEvidence.Marked`; 1 parent queue row; agent `Idle` (pooled) |
| V-11 | S2 | `…a_second_stub_on_the_resumed_turn_defers_on_its_own_row` | brief, stub 1 (row `Superseded`), marked resume, stub 2 | `Working`; `ApiErrorDeferred` detail names stub 2's seq; 0 `Failed`; `ApiErrorRecoveries` has a row for stub 2 |
| V-12 | S2 | `…an_unmarked_resumed_turn_is_still_uncorrelated` | as V-10 but bare resume prompt | `Working`; 1 `DelegateReportUncorrelated` incident (negative control for the marker gate) |
| V-13 | S3 | `AgentTaskPoolTests.the_janitor_does_not_retire_a_warm_agent_whose_session_is_mid_turn` | `SeedWarmAgentAsync(idleMinutes: 120)` + mid-turn transcript (`UserPrompt` seq 1, no `TurnEnd`, `CreatedAt` now) | not killed; row exists; `PoolIdleSince >= now - 1 min` |
| V-14 | S3 | `…the_janitor_retires_it_after_the_turn_ends_and_the_ttl_elapses_again` | V-13 then `TurnEnd end_turn`, `PoolIdleSince = now - 120 min` | killed; row removed |
| V-15 | S3 | `…the_janitor_retires_a_working_reading_session_silent_for_a_full_ttl` | mid-turn transcript whose rows have `CreatedAt = now - 2 × TTL` | killed |
| V-16 | S3 | `…the_per_directory_cap_skips_a_mid_turn_agent` | five warm, `idleMinutes` 0..4, the 4-minute one mid-turn | agents idle 3 and 2 killed; 4 kept |
| V-17 | S3 | `…a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop` | two warm same dir/tier, one mid-turn | `Reused` on the idle one; mid-turn only → `SpawnFresh` |
| V-18 | S3 | `AgentTaskReplyIntegrationTests.releasing_a_shared_task_whose_session_is_mid_turn_pools_it_and_warns` | a terminal settlement with a mid-turn transcript (see S3 note) | agent `Idle`, `PoolIdleSince` set; captured Warning contains `pooled warm while session` |
| V-19 | S4 | `AgentTaskDetailBlockedContextTests.GetAsync_reports_session_liveness_for_a_live_working_session` | `Running` session, mid-turn transcript | `Session.Status == Running`, `Working`, `LastTranscriptAt` not null |
| V-20 | S4 | `…_for_an_ended_session` | `Stopped`, `EndedAt` | `Working == false`, `EndedAt` set |
| V-21 | S4 | `…_is_null_without_a_session` | `AgentSessionId = null` | `Session == null` |
| V-22 | S4 | `AgentTaskReplyIntegrationTests.a_failed_api_error_task_names_session_liveness` | NeedsHuman, no later prompt, mid-turn transcript before the stub is impossible — use a `Running` session whose stub is the newest row (reads idle) | reason contains `live and idle`; parent header contains `session=live-idle` |
| V-23 | S4 | formatter: `the_header_carries_the_session_bit_only_when_supplied` | `BuildCompletionNote(..., sessionLiveness: "live-working")` vs null | header contains / does not contain `session=` |
| V-24 | S4 | Vitest `TaskDetailBody` `shows session liveness under a failed task` / `omits it when absent` | detail with `session: {status:'Running', working:true}` | text `still working` rendered; absent otherwise |

### Positive controls (red-then-green, method-scoped)

| Id | Mutation (restore after) | Must go red |
|---|---|---|
| R-1 | In `HandleApiErrorTurnAsync`, invert the guard (`if (!laterPrompt) return;`) | V-1, V-3 |
| R-2 | Guard checks `UserPrompt` only | V-2 |
| R-3 | Skip arm writes the event unconditionally (drop the needle check) | V-4 |
| R-4 | `HasTurnPromptAfterAsync` uses raw kinds (no housekeeping filter) | V-5, V-6 |
| R-5 | `FireOneAsync` omits the marker prefix | V-7, V-9, V-10 |
| R-6 | `FireOneAsync` prefixes the marker even with no open task | V-8 |
| R-7 | Janitor gate removed | V-13, V-16 |
| R-8 | Janitor gate ignores the silence bound (`always defer while working`) | V-15 |
| R-9 | Janitor gate does not bump `PoolIdleSince` | V-13 (`PoolIdleSince` assertion) |
| R-10 | Reuse shop filter removed | V-17 |
| R-11 | Release warning removed | V-18 |
| R-12 | `GetAsync` passes `Session: null` | V-19, V-20 |
| R-13 | `DeliverToParentAsync` never fills `sessionLiveness` | V-22 |
| R-14 | Fail-arm sentence omitted | V-22 (reason assertion) |

Each control: apply one mutation, run only its named tests with a precise `--treenode-filter`,
confirm the expected assertion fails (zero tests or a build error is not red), restore, run the
same tests green. Batch only mutations in different files.

### Regression sweeps

After S1+S2: `ApiErrorRecoveryServiceTests`, `AgentTaskReplyIntegrationTests`,
`ComplexityWallRerouteTests`, `AgentTaskSettlementRaceTests`. After S3: `AgentTaskPoolTests`,
`AgentTaskCheckScheduleTests`, `GrokDelegateDispatchTests`. After S4: `AgentTaskDetailBlockedContextTests`
plus `pwsh -File scripts/test-client.ps1`. Finally the `Antiphon.Tests.Application` namespace chunk
once, then the remaining chunks per `docs/testing-and-build.md`.

## Risks and what to watch

- **D-2 changes what a resumed session reads first.** The marker line is the same one every brief
  and nudge starts with; a delegate already knows it. Watch for a test in
  `ApiErrorRecoveryServiceTests` asserting body equality with the setting (none found at planning).
- **D-1 for NeedsHuman** trades an immediate, clearly-worded `Failed` for "the newer turn decides".
  If an operator prefers the old behaviour for 401s specifically, the guard can except
  `AgentTaskFailureCode.AuthenticationRequired`; the plan does not, per the brief.
- **D-5's bump extends the reservation window** (`PoolReservedForRootTaskId` is honoured while
  `PoolIdleSince` is within `PoolReservedForCallerMinutes`). Intended, noted.
- **Live proof.** After landing, the next Transient death on a working delegate should show:
  `ApiErrorDeferred` with `resumed at seq`, a marked `Your previous turn was killed…` prompt in the
  transcript, and a `Succeeded` row settled from that turn — with no `Retired warm delegate` line
  for a session that has a newer transcript row than its `PoolIdleSince`.
