# CARD-0079 — Check-interpreter kill-restart loop: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0079 (`5067a50e-6adf-453d-8922-127ac1e241f2`) — the check interpreter has been in a
delivery-fail / kill / restart loop and every check silently fell back to the deterministic digest.
**Live confirm (this Plan pass):** CARD-0007's two check-ins today both carried
`(unverified digest — interpreter unavailable: no reading within 60s)`.
**Precedent:** CARD-0047 slice 4 (standing specialist + digest-as-floor), CARD-0047 4A
(`PlaceOnStandingAgentAsync` — never spawn a second session for AlwaysOn), CARD-0055 (always-on
kill on `NoTranscriptRecord`), CARD-0056 (unclaimed never implies kill), CARD-0071 (limit text is
not a report). Do not steal CARD-0074 (capture-time on the digest).

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The 22-hour kill-restart loop on the card is historical. Today's outage is a zombie
Dispatched interpretation occupying the specialist, and the session looks healthy.**

The interpreter is `antiphon-check-interpreter` (`be5d4502`), AlwaysOn, cwd
`C:\logs\antiphon\check-interpreter`, live session `345ce4ba` since 2026-08-17 17:56:21Z,
`consecutiveFailures = 0`, queue empty, `working = false`. Last incident is `Recovered` at
17:06 London / 18:06Z on the 17th. It is not cycling.

What is cycling is the *fallback*: 17 interpretation tasks Canceled on 2026-08-19 alone, every
one created then canceled exactly 60s later still Queued (`CancelIfStillQueuedAsync`). Cause:
task `1d407857` (check #1 on `49f11348`) has been **Dispatched since 2026-08-17 17:53:29Z**
on **dead** session `1cb2fadb`. `PlaceOnStandingAgentAsync` treats any Dispatched/Working row
on the agent as occupancy (`AgentTaskDispatcher.cs:1530-1536`) and returns `WaitForAgent`.
The 60s wait then degrades, and the caller sees a parenthetical.

The interpreter already answered `1d407857` on the **new** session at 17:56:46Z (`LOOKS STUCK
— Session hit its 35-minute time limit…`). Settlement never saw it: `OnTurnEndAsync` looks up
the open task by `AgentSessionId ==` the session that just ended the turn
(`AgentTaskReplyService.cs:71-75`), and `1d407857.AgentSessionId` is still `1cb2fadb`.

Two Code slices. Slice 1 unblocks; slice 2 makes the next outage unmissable.

**Operator unblock without waiting for Code:** fail task `1d407857`. Occupancy clears on the
next dispatcher tick; the following check will dispatch into `345ce4ba`. Do not kill the live
session.

---

## 1. What to establish (fresh, 2026-08-19)

### 1.1 Why delivery to this agent cannot be verified

Three distinct eras. The card collapsed them into one loop.

| When | What the incidents say | What was actually true |
|---|---|---|
| 08-16 20:02–21:07 | 9 `TranscriptBindFailed` (`TranscriptMissing`), 12 `DeliveryTransportFailed` (runner 500), `NoComposerEvidence` | CARD-0047's trust dialog. Brand-new cwd, modal, quiet-period called it READY, composer swallowed writes, always-on kill restarted into the same dialog. **Fixed** by `ClearStartupTrustPromptAsync`. `~/.claude.json` now has `hasTrustDialogAccepted: true` for `C:/logs/antiphon/check-interpreter`. Last bind failure **20:28Z on the 16th** — they stopped because the problem was fixed, not because a later mode masked them. |
| 08-17 10:14–15:29 | none | Interpreter **working**. 13 Succeeded readings in ~20–40s (e.g. `1459156d` 23s, `6f16bf23` 20s). Hook, CLAUDE.md, bind, settlement all fine. |
| 08-17 16:56 | — | Specialist itself hit Claude's session limit. `aa3e837c` stored `"You've hit your session limit — resets 6:10pm"` as `Result` (the CARD-0071 shape; that card now fails API-error turns). |
| 08-17 17:53–17:56 | `NoTranscriptRecord` on `1cb2fadb` at 17:54:20, kill, restart attempt 2 at 17:54:29, again at 17:55:56, new session `345ce4ba` at 17:56:21, `Recovered` 18:06:21 | Prompt **never** appears in `1cb2fadb`'s 55-entry transcript (last UserPrompt is `aa3e837c` at 16:56). Kill was correct. `StartInteractiveSessionAsync` **moved the Pending brief** onto `345ce4ba` (`AgentControlService.cs:319-331`) and did **not** move the task. Brief became seq 1 UserPrompt at 17:56:34; AssistantText at 17:56:46. Delivery on the fresh composer **worked**. Settlement missed. |
| 08-17 18:55 → 08-19 17:33 | no interpreter incidents | **No delivery is attempted.** `WaitForAgent` leaves every new Check task Queued. `CancelIfStillQueuedAsync` fires at T+60s. 20 Canceled interpretations in that window (1 on the 18th, 17 on the 19th, 2 late on the 17th). CARD-0007's `2978d549` / `3ca2b8f1` are this shape: Created only, failureReason `"The check that asked for it stopped waiting."` |

Candidates the card named, against current evidence:

- **Trust dialog:** ruled out. Directory trusted; current buffer is a normal haiku composer
  (`bypass permissions on`, cwd shown). CARD-0059 CLAUDE.md is present (managed marker). Deny-all
  PreToolUse hook is present and is not the 08-16 cause (CARD-0047 already settled that).
- **Prompt lands, no UserPrompt:** true of `1cb2fadb` at 17:54 (post-limit composer). False of
  `345ce4ba` — seq 1 is the full 4 082-char brief.
- **Transcript not bound:** true of 08-16; false since 21:11 on the 16th (`1cb2fadb` bound, 55
  rows) and false of `345ce4ba` (4 rows, first record 13s after start).

### 1.2 Whether the kill can ever help here

| Failure | Kill helps? |
|---|---|
| Trust dialog / untrusted cwd / hook eating startup | **No.** Same environment, same modal. 11 kills on 08-16 until CARD-0047. |
| Post-limit / wedged composer (`NoTranscriptRecord`, session already bound) | **Yes.** `345ce4ba` took the migrated brief and answered in 12s. |
| Occupancy by a Dispatched task on a dead session | **No.** Nothing is being typed. The live session is idle. |

CARD-0055's always-on kill is the right remedy for a wedged composer. It is missing the
follow-through that 4A already had to invent for **messages**: when a new session id is minted,
Pending queue rows move (`AgentControlService.cs:319-331`) and **in-flight tasks do not**.
`OnTurnEndAsync` then looks in the wrong session. The kill helped, then the occupancy lock
made the help invisible, then every later check paid the 60s tax.

A repeated identical failure should still escalate rather than loop. Parking at 3 attempts is
per-**message** (`SessionMessageQueueService`); the session-kill still fires on the 3rd, parked
rows stay `Pending` so they **migrate** onto the replacement, and automatic flush excludes them
— leaving a Dispatched task on a live empty composer. Slice 1's occupancy rule plus
FailDeadSession covering Check is the ceiling that actually unblocks. A separate kill-streak
cap is not required to close this card; do not add one.

`FailNeverStartedAsync` already **includes** Check (no role filter) but `continue`s when the
**named** session has any transcript. `1d407857` names `1cb2fadb` (55 rows, no
`DelegateReportUncorrelated`), so the 10-minute watchdog has been a no-op for two days.
`FailDeadSessionTasksAsync` **excludes** Check, pinned by
`AgentTaskDeadSessionReconciliationTests.a_check_task_is_never_settled_by_this_sweep`. The
comment is wrong on both counts it gives: Check tasks have `ReplyTo = None` so they emit no
caller note, and a delivery failure is not what produced `DeliveryFailed` here — delivery
succeeded on the other session.

### 1.3 Whether "interpreter unavailable" should be loud

Yes. 48 hours, 20 timed-out interpretations, zero incidents after `Recovered`, specialist
badged Running / idle / healthy. The fallback line is in the note body; orchestrators read the
note; the card-thread UI already distinguishes (`fromInterpreter` on `CardThreadCheckDto`,
"check reading" vs "check digest tail") and the orchestrator session does not.

---

## 2. What to decide

| Question | Decision | Why |
|---|---|---|
| Always-on vs per-check ephemeral | **Keep always-on** | 08-17 daytime is the existence proof (readings in 20s, warm). Ephemeral re-opens the 4A bug: `SpawnFresh` overwrites `PersistentSessionId` and fights the supervisor. Fail loud; do not throw away the specialist. |
| Withhold the digest vs send a marked fallback | **Send the digest, make the mark unmissable** | CARD-0047's non-negotiable: the specialist is garnish, the digest is the floor. Withholding is the silence that card existed to end. CARD-0074 owns capture-time / superseded-at-delivery; this card owns "this is not a status report". |

Rejected: mapping `0` of occupancy by failing every Check at 60s from the check service
(the task would still occupy). Rejected: answering the trust dialog again (already done).
Rejected: deleting the deny-all hook. Rejected: raising `CheckInterpreterWaitSeconds` — the
timeout is exact because dispatch never happens, not because haiku is slow.

---

## 3. Slice 1 — occupancy and settlement follow-through (Code)

The pair that 4A already wrote for messages, applied to tasks, plus the occupancy predicate
and the dead-session sweep the test currently forbids.

### 3.1 Re-point in-flight tasks when the session id changes

`AgentControlService.StartInteractiveSessionAsync` (`:319-331`), immediately after the
Pending-message `ExecuteUpdate`:

```
AgentTasks where AgentId == agent.Id
         && AgentSessionId == previousSessionId
         && Status in (Dispatched, Working)
  → AgentSessionId = session.Id
```

Log the count the same way the message move does. This is what would have settled `1d407857`
at 17:56:46. Card-spawn path (`SpawnAsync`) is out of scope — the specialist is cardless.

Do **not** also rewrite `FailNeverStartedAsync` to kill the new session. If a re-pointed Check
task then sits on a live specialist with zero new transcript (parked brief), failing the **task**
is right and killing the specialist is CARD-0056's disaster. If that arm is touched at all: for
`Role == Check` on an AlwaysOn non-pool agent, `FailAsync` and skip `KillAsync`.

### 3.2 Occupancy is the live session, not "any Dispatched row on this agent"

`PlaceOnStandingAgentAsync` (`:1530-1536`):

```
busy = AgentTasks any
    AgentId == standing.Id
    && Id != claimed.Id
    && AgentSessionId == liveSession          // was: missing
    && Status in (Dispatched, Working)
```

A Dispatched row whose session is dead, or whose session is a previous AlwaysOn generation,
must not block. Serialisation on the **live** composer stays: two briefs still must not land
between each other's turns
(`a_busy_standing_agent_makes_the_next_task_wait_and_take_it_after_the_first_settles`).

### 3.3 `FailDeadSessionTasksAsync` includes Check

Drop `&& t.Role != AgentTaskRole.Check` (`AgentTaskDispatcher.cs:514`). Keep the no-kill
contract and the runner evidence gates. `ReplyTo = None` already suppresses a completion note.
`RemoveEphemeralAgentAsync` only deletes `IsPoolDelegate` rows — the specialist is safe.

Rewrite `a_check_task_is_never_settled_by_this_sweep` to the opposite: past grace, a Check
task on a Failed session that the runner does not list Running becomes Failed, and no parent
note is enqueued.

Do not late-settle `1d407857` from `345ce4ba`'s transcript. The reading is two days stale and
about a finished task. Fail the zombie; the next check is the one anyone will read.

### 3.4 Tests

Existing homes, no new project:

- `AgentTaskStandingAgentDispatchTests` — new: a Dispatched task whose `AgentSessionId` is a
  **Stopped** previous session does not occupy; the next queued pin lands in the live session.
  Existing busy-wait test stays green.
- `AgentTaskDeadSessionReconciliationTests` — invert the Check exclusion test as in §3.3.
- New test next to the StartInteractiveSession message-move (there is **no** test for
  `:319-331` today; add both arms together): Pending messages move **and** a Dispatched task
  on the previous session is re-pointed. Drive `AgentControlService.StartAsync` with
  `Fresh: true` (or whatever the harness uses to mint a new id) against an AlwaysOn agent.
- `AgentTaskCheckInterpreterTests` — optional cheap extra: with a Dispatched Check zombie on a
  dead session, the next `RunCheckAsync` creates an interpretation that **dispatches** rather
  than sitting Queued until the fake clock hits 60s. Not required if the standing-agent test
  already proves occupancy.

```
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*AgentTaskStandingAgentDispatchTests/* --property:OutputPath=bin-card0079/
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*AgentTaskDeadSessionReconciliationTests/* --property:OutputPath=bin-card0079/
```

Forward slash on `OutputPath`. Delete `bin-card0079/` after. Both classes are `[NotInParallel]`
(standing-agent shares `"AgentQueue"`). Do not co-schedule with `Antiphon.Agents.Pty.Tests`.

---

## 4. Slice 2 — the fallback is an incident, not a parenthetical (Code)

`AgentTaskCheckService.InterpretAsync` (`:357-359`) already logs Information and returns
`Degraded("interpreter unavailable: no reading within Ns")`. That is the only record.

- New `AgentIncidentKind.CheckInterpreterUnavailable = 26` (append; do not renumber). Warning
  on first timeout / provision-failure / queue-failure; keep `interpreter busy` at Information
  (that is load, not a dead specialist). Dedup per specialist agent so a fleet of due checks
  in one minute is one incident, not one per check.
- Raise from `InterpretAsync` on the degraded paths that mean the specialist cannot be reached
  (`could not be provisioned`, `could not be queued`, `no reading within Ns`, `interpretation
  failed`, `empty`). Use `IAlertService` the same way `RecordUncorrelatedReportAsync` does.
- Note prefix: keep the `(unverified digest — …)` line for tests that assert it
  (`AgentTaskCheckInterpreterTests` 255/279/319/343) **and** put `INTERPRETER DOWN` on the
  header line `BuildNote` already builds (`:530-544`), so a skim of the first line is enough.
  Do not change `HeaderPrefix` (`[check `) — settlement and the client key on it.
- Client: optional, not required to close. Card-thread already labels digest vs reading.

Do not withhold. Do not stamp capture time (CARD-0074). Do not fail the **delegate** because
its check was unverified.

```
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*AgentTaskCheckInterpreterTests/* --property:OutputPath=bin-card0079/
```

---

## 5. Out of scope

- CARD-0074 capture-time / reconcile-at-delivery / "should a settled task generate a check".
- Re-opening CARD-0047's trust-dialog detector or the deny-all hook.
- Making the interpreter ephemeral.
- Raising `CheckInterpreterWaitSeconds`.
- Recovering `1d407857`'s stale `LOOKS STUCK` into its `Result`.
- A kill-streak cap in `HandleDeliveryFailureAsync` (parking + §3.2/§3.3 are the ceiling).
- The card-spawn session path.

---

## 6. Commits

Slice 1: `fix(delegation): CARD-0079 - stop a dead check-interpreter task occupying the specialist`

Slice 2: `fix(delegation): CARD-0079 - raise an incident when the check interpreter does not read`
