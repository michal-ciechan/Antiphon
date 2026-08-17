# CARD-0021 — A task outlives its dead session: what is left, and the plan for it

- **Status**: Planned (this document is the plan; nothing here is implemented)
- **Card**: CARD-0021 (`177f87a0-d0de-49bc-bc05-81c7ca9d55ed`) — "A task outlives its dead session,
  and a stolen transcript hides it from the watchdog", written 2026-08-09, labels
  reliability/delegation
- **Date**: 2026-08-17
- **Concurrent work warning**: agent c930aeb8 is implementing CARD-0059/0060 in `AgentService`,
  `AgentControlService` and the client. **This plan touches none of those files.** The only shared
  surface is DI registration if the new sweep's ctor parameter needs `Program.cs` wiring — a
  trivial merge. See §6.

## 0. Establishment: what the card asked for vs what has shipped (verified against code, 2026-08-17)

The card names two gaps. One is half-closed, one is closed outright.

### Gap 1 — "no task-vs-session reconciliation": detection DONE, action UNTOUCHED

**Detection is done.** `AttentionService.BuildOpenTaskItemsAsync` condition 3
(`server/Application/Services/AttentionService.cs:241-269`, CARD-0035 slice 1) computes exactly the
card's predicate over every Dispatched/Working task: `AgentSessionId` null, session row missing,
`Status ∈ {Stopped, Failed}`, or `EndedAt` set — surfaced as `AttentionKind.DeadSession` at Error
severity with Retry/Cancel/Escalate actions and the session's own `FailureReason` as evidence. The
2026-08-09 zombies (a6e163fe, 4fe47cb2, a4b38a93) would all appear on `GET /api/attention` today.

**Action is untouched.** Verified exhaustively:

- `FailNeverStartedAsync` (`AgentTaskDispatcher.cs:327`) never consults session status. Its two
  branches are zero-transcript and uncorrelated-report; a Dispatched task whose session died AFTER
  writing entries passes the `started` check and is waved through forever. A **Working** task is
  outside its query entirely (`Status == Dispatched` only, line 335).
- `SessionReconciliationService` never touches `AgentTasks` (grep: zero references). Its three
  passes correct **session** rows only.
- No session-close path settles tasks. Only five files reference `AgentTasks.`:
  the four task services and the read-only `AttentionService`.
- The only automatic task-failure paths in the codebase are dispatch-time exceptions
  (`AgentTaskDispatcher.cs:197`) and the two `FailNeverStartedAsync` branches (`:391`).

**Partial coverage worth naming**: a dead session that wrote *nothing* IS caught for Dispatched
tasks — `FailNeverStartedAsync` doesn't care whether the session is alive, and its kill on an
already-dead session is a no-op. What still sits open forever: (a) any **Working** task with a dead
session, and (b) any **Dispatched** task whose session wrote at least one transcript entry and
then died before settlement. Neither gets the session's `FailureReason` attached, the parent never
hears, and the ephemeral agent is never cleaned. That is the whole remaining gap 1.

### Gap 2 — "a stolen transcript defeats the did-it-start test": DONE, closed upstream by CARD-0006

The card's ask was "require that the entries BELONG to this session". CARD-0006 enforced that at
**bind time**, before any entry is ingested: `TranscriptClaimRegistry` makes one transcript ⇄ one
session (the 2026-08-09 shape — three sessions ingesting one identical 30-record set — is now
structurally impossible on C1 alone), and C2/C2b/C3/C4 forbid binding a stranger's file at all.
Every transcript entry in the DB now arrives through a bind that carried positive evidence of
ownership, so `FailNeverStartedAsync`'s "any entry means it started" test is sound as written. A
second belonging check inside the watchdog would be a re-implementation of the tailer's rules with
nothing to catch. **No work remains on gap 2; this spec plans none.**

The **residual is the inverse failure**, and it is CARD-0064's, not this card's (see §1): a
*correctly refused* bind now produces a live, working session with zero transcript entries — which
`FailNeverStartedAsync` would fail at 10 minutes and kill mid-work. For delegates this window is
narrow (the brief itself is delivered through `RunnerSession.WriteAsync`, which feeds
`SessionInputLog`, so C4 has a needle within a single runner lifetime), but a runner restart in a
delegate's first minutes reopens it. One mitigating line lands in slice 1 (§4).

### The card's third bullet — "should a deleted agent row imply its tasks are dead?"

Resolved by proxy, no work planned: the 2026-08-09 deletions cascaded with dead sessions, and the
session predicate catches those. An agent row deleted while its session row survives leaves the
task settleable through the session (settlement is transcript-driven, not agent-driven). Declined.

### CARD-0064's residual (persist `SessionInputLog` across relaunch): belongs to CARD-0064, not here

The investigation (`docs/investigations/2026-08-17-az-care-transcript-bind-CARD-0064.md`)
establishes that CARD-0006's rules behaved correctly, that az-care's 66-hour unbound run was cured
only by the migration shim needing a runner restart plus a 20-second mtime window, and that the
sidecar protects across runner restarts but **not** across a session relaunch (fresh session,
`restartAdopt: false`, empty input log, shim deliberately unavailable). That risk is real and
unfixed — but it is a **runner-side binding-evidence** problem on standing/channel-bound agents,
in `TranscriptTailer`/`SessionInputLog`/`RunnerSession`, with its own fix already designed in the
investigation (persist the input log next to the sidecar + guarantee one ≥12-char submitted boot
prompt). CARD-0021's remaining work is **server-side task settlement** and depends on none of it.
**Decision: plan it under CARD-0064** (which is open, corrected, and holds the evidence), keep
this card's scope to the action half of gap 1. Mixing them would couple an independently landable
5-file server change to runner protocol work.

## 1. What this card still needs (the honest summary)

One thing: **a sweep that ACTS on the DeadSession condition the attention projection already
detects.** Fail the task, attach the session's own `FailureReason`, tell the parent, clean up the
ephemeral agent — the card's own prescription, which is also `FailNeverStartedAsync`'s existing
shape. Everything else in the card is done or declined above. This is a good outcome: the card
shrinks from "build reconciliation and fix transcript identity" to one sweep plus a lockstep
predicate.

**Fail, not retry, not surface-only.** Surfacing alone is what exists today (attention row), and
the card's own evidence is that unsurfaced-unacted rows sat for hours. Retry is the caller's
decision, not the sweep's: the completion note reaches the parent session (which can re-dispatch),
and the attention/board Retry actions exist for the human. A dead session with an unsettled task
is unambiguous — no report is coming — and `Failed` with the real reason is the truthful state.
The failed task then appears in attention as `RecentFailure` (context band) instead of
`DeadSession` (Error band), which is the correct demotion.

## 2. Evidence bar (the CARD-0056 constraint, applied)

CARD-0056 exists because a DB row saying `Failed` was once wrong about a healthy session — the
operator's own. Positive evidence, never inference from absence, never a kill on a healthy
session. Applied here:

1. **The runner must answer, and must not list the session as Running.** The sweep fetches the
   runner's session list once per pass (same `ListAsync` reconciliation uses). If the runner
   reports the session Running, **skip** — that is exactly the false-Failed shape, and
   reconciliation pass 3 will re-adopt it (flipping the row to Running removes it from our
   predicate). If the runner is unreachable, **skip the whole sweep** — an unanswerable runner is
   no evidence (`SessionReconciliationService.cs:94-97` doctrine), and the task has already waited
   minutes; another 5-second tick costs nothing. This also covers the re-adoption-cap-exhausted
   flap state: row Failed, session actually Running, human already escalated to — we do not fail
   the task under them.
2. **A grace window on first observation.** In-memory `ConcurrentDictionary<Guid, DateTime>`
   (task id → first seen dead), same pattern as reconciliation's `_reAdoptions`. Fail only after
   `DeadSessionFailGraceMinutes` (default 3) — several reconciliation sweeps' worth (15s interval)
   for a wrong Failed row to be re-adopted, and enough for the session-close transcript backfill
   (`SyncTranscriptAsync`'s turn-boundary flush) to give settlement its ordinary chance at a report
   that arrived just before death. Server restart clears the map; that only delays, never skips.
   A task that stops matching (re-adopted, settled, canceled) is evicted from the map.
3. **The sweep never kills anything.** Unlike `FailNeverStartedAsync`, there is no `KillAsync`
   here — the session is dead by evidence, and if that evidence is wrong the kill would be the
   CARD-0056 disaster. The one exception FailNeverStartedAsync makes (killing a never-started
   session) stays where it is.

`Stopped` counts as dead for the task: an operator stopped the session; no settlement is coming;
the reason says so ("session was stopped before the task settled"). `EndedAt` set with a live-ish
status counts too (lockstep with attention's predicate). `Role == Check` tasks are **excluded**:
their lifecycle is owned by `AgentTaskCheckService` (delivery failures already produce
`CheckOutcome.DeliveryFailed`), they are pinned to the standing interpreter, and a completion note
about a check task would be noise. Recorded as an open question in §7.

## 3. Slices

### Slice 1 — the shared predicate + the sweep (the whole feature)

**`server/Application/Services/AgentTaskLiveness.cs`** (new, small static class):

```csharp
public static class AgentTaskLiveness
{
    public readonly record struct SessionSnapshot(
        bool RowExists, SessionStatus Status, DateTime? EndedAt, string? FailureReason);

    // The ONE definition of "this open task's session is dead". AttentionService condition 3 and
    // the dispatcher's dead-session sweep both call this; a lockstep test pins it.
    public static bool IsDeadSession(Guid? agentSessionId, SessionSnapshot? session) =>
        agentSessionId is null
        || session is not { RowExists: true } s
        || s.Status is SessionStatus.Stopped or SessionStatus.Failed
        || s.EndedAt is not null;
}
```

**`server/Application/Services/AgentTaskDispatcher.cs`**:

- Ctor gains `ISessionRunnerClient? runnerClient = null` (same optional-with-null-disarms contract
  as `replies`/`checkQueue`, so no existing harness breaks; null ⇒ the sweep is not armed).
- New `internal async Task<int> FailDeadSessionTasksAsync(CancellationToken ct)`:
  - Guard: `_runnerClient` null or `DeadSessionFailGraceMinutes <= 0` ⇒ return 0.
  - Load open tasks: `Status ∈ {Dispatched, Working} && Role != Check`.
  - Load their session rows in one query; evaluate `AgentTaskLiveness.IsDeadSession`.
  - Non-matching tasks: evict from the first-seen map; continue.
  - Matching: fetch runner list ONCE per sweep (only when at least one match exists), inside
    try/catch — unreachable ⇒ log Debug, return 0. Session listed Running ⇒ evict + skip.
  - First-seen map + grace as §2.2. Within grace ⇒ skip.
  - Fail: reason =
    `"Session died before the task settled: <what> (<session.FailureReason or 'no failure reason recorded'>). No report is coming; read session <id> before re-running."`
    where `<what>` mirrors attention's four-way wording. Then the exact `FailNeverStartedAsync`
    tail minus the kill: `FailAsync`, `RemoveEphemeralAgentAsync`, completion note to
    `ParentSessionId` when `ReplyTo == Session` (swallow-and-log enqueue failure), publish
    `AgentTaskChanged`, log Warning, evict from map.
- `TickAsync`: register as a sixth clock, after the delivery watchdog:
  `sweepFailures += await RunSweepAsync("dead-session reconciler", FailDeadSessionTasksAsync, ct);`
- One-line mitigation for §0's inverse residual: `FailNeverStartedAsync`'s zero-transcript reason
  gains a clause when the session's *runner* status is Running-with-bind-failure incidents — **not
  planned as a behavior change**, only the reason text noting "if a TranscriptBindFailed incident
  is present, the delegate may have been working unbound" so the operator reading the failure has
  the CARD-0064 pointer. (Deliberately no logic change: behavior changes there belong with
  CARD-0064's fix.)

**`server/Application/Settings/DelegationSettings.cs`**: `DeadSessionFailGraceMinutes` (int,
default 3, `<= 0` disarms — the escape-hatch convention `FinalMessageGraceSeconds` already uses).

**`server/Application/Services/AttentionService.cs`**: condition 3's inline predicate becomes a
call to `AgentTaskLiveness.IsDeadSession` (pure refactor; wording strings stay local).

**DI**: pass the existing registered `ISessionRunnerClient` to the dispatcher in `Program.cs`
composition (verify at implementation time whether the dispatcher is constructed there or via DI
container resolution — if container-resolved, the optional parameter resolves automatically and no
`Program.cs` edit is needed).

**Tests — new `tests/Antiphon.Tests/Application/AgentTaskDeadSessionReconciliationTests.cs`**
(Integration, `[NotInParallel]` with NO group key — global sweep, per the shared-Postgres rule;
harness cloned from `AgentTaskDeliveryWatchdogTests` with a fake `ISessionRunnerClient` and fake
`TimeProvider`):

1. Dispatched task + session Failed + runner answers without it + grace elapsed ⇒ task Failed,
   reason contains the session's `FailureReason`, ephemeral agent removed, parent note enqueued,
   **no kill issued** (fake stopper's `Killed` empty).
2. **Working** task, same ⇒ Failed (the case FailNeverStartedAsync structurally cannot catch).
3. Session row deleted ⇒ Failed, reason names "session row is gone".
4. Session `EndedAt` set but Status Running ⇒ Failed.
5. Session Stopped ⇒ Failed, reason names operator stop.
6. Runner lists the session Running ⇒ untouched (the CARD-0056 false-Failed shape — the test that
   matters most).
7. Runner throws ⇒ untouched, sweep returns 0, no exception escapes.
8. Within grace ⇒ untouched; advance fake clock past grace, second call ⇒ Failed.
9. Healthy Running session ⇒ untouched, and evicted from the first-seen map after a transient
   dead observation (session re-adopted between sweeps ⇒ never fails).
10. `Role == Check` task with dead session ⇒ untouched.
11. Lockstep: a table of (session snapshot ⇒ verdict) asserted identical through
    `AgentTaskLiveness.IsDeadSession` and through an `AttentionService.GetAsync` DeadSession row's
    presence (guards the refactor and the predicate forever).

**Landable alone**: yes — additive sweep, disarmed wherever the runner client isn't wired.

### Slice 2 — close out the card (no code)

Update CARD-0021 via the board API: gap 2 closed by CARD-0006 (with the §0 reasoning), gap 1
detection closed by CARD-0035, action closed by slice 1; the deleted-agent bullet declined with
rationale; CARD-0064 residual explicitly re-homed to CARD-0064 with a pointer to this spec. Then
move the card per board convention once slice 1 ships.

## 4. What I could not determine

- **Dispatcher construction site**: whether `AgentTaskDispatcher` is container-resolved (optional
  ctor param needs nothing) or hand-built in `Program.cs`/a hosted service (one-line wiring).
  Implementation must check; either way the change is a line.
- **Whether reconciliation's pass-3 re-adopt and this sweep can interleave badly on the same
  tick**: the runner-answer gate + grace should make ordering irrelevant, but the implementer
  should confirm the dispatcher tick and reconciliation sweep run on independent timers (they
  appear to: 5s vs 15s hosted loops) and that neither holds a lock the other needs.
- **Check tasks' own dead-interpreter story** (§7): out of scope here, may deserve a line on
  CARD-0047's follow-up.
- **CARD-0059/0060's exact diff**: I established the named files don't overlap this plan, but
  could not see c930aeb8's uncommitted tree; if they add dispatcher ctor params too, the merge is
  mechanical but real.

## 5. Explicitly out of scope

- Persisting `SessionInputLog` / boot-prompt needle guarantees → CARD-0064 (§0).
- Any change to CARD-0006 binding rules, the migration shim, or its removal timing — the shim must
  **not** be removed until CARD-0064's fix ships (investigation's own warning).
- Any transcript-belonging re-check in the watchdog (gap 2 is closed; §0).
- Retry-on-dead-session automation; attention UI changes; killing anything.

## 6. Collision map (agent c930aeb8, CARD-0059/0060)

| This plan touches | c930aeb8 touches | Overlap |
|---|---|---|
| `AgentTaskDispatcher.cs`, `AgentTaskLiveness.cs` (new), `DelegationSettings.cs`, `AttentionService.cs`, new test file | `AgentService`, `AgentControlService`, client | **None** |
| Possibly `Program.cs` (one DI line) | possibly `Program.cs` | Trivial merge |

## 7. Open questions (answerable at implementation, none blocking)

- Should a dead-session failure raise an `AgentIncident` in addition to the task event? Leaning
  no: the task's `Failed` + attention `RecentFailure` + parent note already cover every surface,
  and incidents are agent-scoped while the agent row is being removed in the same breath.
- Should Check tasks eventually get their own dead-interpreter reconciliation? (Excluded here;
  `CallerIsListeningAsync` already guards the parent side.)
