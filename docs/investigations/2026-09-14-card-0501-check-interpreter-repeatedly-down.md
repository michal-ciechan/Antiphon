# CARD-0501: check-interpreter repeatedly down (INTERPRETER DOWN digests)

Investigated 2026-09-14 (task b45ac852). All timestamps below are UTC unless marked `local`
(server and runner Serilog files stamp `+01:00`; subtract one hour to get UTC).

## Verdict

**Confirmed.** The check interpreter has produced **zero** readings since 2026-09-12 17:00:49
UTC. Its standing session `cea73d57-3072-4cc2-8cb0-ab859e7d3415` is wedged in a delivery loop
that no automatic path can leave, and the dispatcher's 10-minute watchdog kills and the
supervisor's resumes have recycled that session **78 generations** since 2026-09-12 (43 restarts
on 2026-09-14 alone). Every check task since then ends as INTERPRETER DOWN: mostly the 60 s wait
timeout, occasionally the generation mismatch when a check fires during a kill/resume window.

This is not a background rate that was always there. On 2026-09-11 the interpreter produced
78 successful readings and 1 restart; on 2026-09-12 it produced 97 readings up to 17:00:49 and
none afterwards.

## The four-part mechanism

### 1. Ignition (2026-09-12 16:55 to 17:06): auto-compaction mid-check, watchdog kill

`TranscriptEntries` for the session:

| seq  | kind           | time     | text                                                   |
|------|----------------|----------|--------------------------------------------------------|
| 1053 | UserPrompt     | 16:55:52 | `[antiphon-task:a42e10c4] role=Check tier=Low ...`     |
| 1054 | CompactBoundary| 16:56:33 | `Context compacted (auto)`                             |
| 1055 | UserPrompt     | 16:56:33 | `This session is being continued from a previous ...`  |
| 1057 | AssistantText  | 16:56:52 | `On track, ... [antiphon-report:a42e10c4 done]`        |
| 1059 | UserPrompt     | 17:04:21 | `[System note from Antiphon: your context was just compacted ...` |
| 1061 | AssistantText  | 17:04:27 | `NO_REPLY`                                             |
| 1062 | TurnEnd        | 17:04:27 | (last transcript record the session has ever produced) |

Claude auto-compacted the Haiku conversation while check `a42e10c4` was in flight. The reading
was produced, but its turn's prompt is the synthetic continuation prompt (seq 1055), which
carries no task marker. The server logged (`antiphon-20260912.log`, 18:05:41 and 18:06:09 local):

> Session "cea73d57-…" ended a turn WITH a report but the prompt carried no marker for task
> a42e10c4 — not settling it.

At 18:06:10 local the delivery watchdog (`AgentTaskDispatcher.cs:1273-1303`, the
"Delegate reported but the result could not be attributed" arm) failed `a42e10c4` and, because
the session was idle, called `KillAsync` on the standing session. `AgentSupervisorService`
scheduled restart attempt 1 five seconds later and resumed the same session id at 17:06:48
(`AgentControlService.cs:498`, `previous.StartedAt = SessionGeneration.Next(...)`).

The `AgentTasks` row for `a42e10c4`: status Failed at 17:06:10, reason "Delegate reported but
the result could not be attributed: 10 minutes after dispatch ...".

### 2. First delivery after the resume failed with no screen output (17:11:47)

The next check brief (`419b8b34`, queue row `95f57091`, sequence 535, 3937 chars) was typed at
17:11:47. Server log 18:12:19 local:

> Delivery verification failed for session "cea73d57-…": the body (3912 chars) never became a
> UserPrompt record past sequence 1062 within 30s after 3 Enter(s); **screen output never
> advanced** in that window

That attempt had a captured generation, so the failure path killed the session
(`killed=true`) and the supervisor resumed it again at 17:13:36. The queue row was reverted to
Pending with `DeliveryAttempts = 1` kept, by design ("the attempt metadata deliberately survives
the revert — it is the retry brake", `SessionMessageQueueService.cs:3549-3565`).

Why the resumed TUI produced no output for that first paste is the one part of the chain I could
not reconstruct from stored evidence (see Uncertainties). Everything after it is deterministic.

### 3. The permanent wedge: head-fragment false positive against replayed history

The queue delivers in sequence order, so row `95f57091` is the head of the queue from then on.
It carries `DeliveryAttempts = 1`, which routes every flush through the CARD-0340 S3 /
CARD-0342 "previously typed body still standing in the composer gets Enter only" rule
(`SessionMessageQueueService.cs:1728-1740`):

```csharp
if (run.Any(m => m.DeliveryAttempts > 0))
{
    ...
    if (ComposerDeliveryEvidence.HeadFragmentIsVisible(retrySnap.RenderedScreen, body))
        return await EnterOnlyConfirmLockedAsync(db, sessionId, run, body, ct, ceilings);
}
```

`HeadFragmentIsVisible` (`src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs:155-168,212-224`)
takes the first 40 whitespace-stripped characters of the body and returns true if **any single
10-character window** of that head appears anywhere on the 30-row rendered screen.

Every check brief starts `[antiphon-task:<id>] role=Check tier=Low workspace=Shared`. A resumed
Claude session replays the compacted conversation on screen, and the replay ends with the old
`a42e10c4` brief, whose closing line is the bare marker `[antiphon-task:a42e10c4]`, followed by
`[antiphon-report:a42e10c4 done]`. Reproduced against the live runner snapshot
(`GET :17204/sessions/cea73d57-…/snapshot`, captured 2026-09-14 18:22, 30 rows,
`lastSequence` 61):

```
head: "[antiphon-task:419b8b34]role=Checktier=L"
"hecktier=L" -
"ole=Checkt" -
"b34]role=C" -
"419b8b34]r" -
"task:419b8" -
"phon-task:" VISIBLE
"[antiphon-" VISIBLE
```

The a42e10c4 brief's own head line is not on screen (it scrolled off), but the marker line is,
and one window is enough. So the queue concludes the 419b8b34 body is sitting in the composer,
presses Enter only, and never re-types it. The composer is in fact empty: the rendered screen
shows `> ` with nothing in it, and the raw output since the 18:11:50 resume (14.6 KB) contains
the runner's composer probe `zzcea73d57` being typed and deleted (`Ctrl+Y to paste deleted
text`) and then only 35 bare SI (`0x0f`) bytes, one per Enter. No `[Pasted text` placeholder
and no brief text ever appears.

Each Enter-only cycle then ends in `NoTranscriptRecord` and calls
`HandleDeliveryFailureAsync(..., capturedGeneration: null)` (`SessionMessageQueueService.cs:2148`),
which explicitly declines the always-on recovery kill:

> Delivery to session "cea73d57-…" failed verification but the attempt retained no generation;
> declining the recovery kill.

And because `EnterOnlyConfirmLockedAsync` never increments `DeliveryAttempts` (0 references in
lines 2059-2150), the row stays at 1 of `MaxDeliveryAttempts = 3` forever and never parks. The
cycle repeats on the 60 s stranded sweep (`StrandedAgeSeconds = 60`); observed cadence is one
cycle every ~51 s. Live state of the head row while polling on 2026-09-14 18:31-18:32:

```
seq 535  95f57091  Status=Pending  DeliveryAttempts=1  LastDeliveryStartedAt=09-12 17:11:47
         LastDeliveryBaselineSequence=1062  DeliveryVerdict=3 (NoTranscriptRecord)
         DeliveryVerdictAt re-stamped every cycle (18:31:38)
```

Behind it: 64 more Pending check briefs (rows 536+), all `DeliveryAttempts = 0`, never typed,
because the head blocks them. Log-message census for the session on 2026-09-14 up to 18:30:

| message                                                              | count |
|----------------------------------------------------------------------|-------|
| Enter-only recovery for 1 message(s) ... the body head is visible    | 1219  |
| No transcript record yet ... pressing Enter again (attempt N of 3)   | 2428  |
| Delivery verification failed ... screen output DID advance           | 1204  |
| ... retained no generation; declining the recovery kill              | 1204  |
| Delivery to session ... failed verification ("NoTranscriptRecord")   | 1204  |

### 4. The outer loop: watchdog kill, supervised resume, new generation, same trap

Ten minutes after each check task is placed into the session, the delivery watchdog fails it
with the reason seen on every failed row since 09-12 17:22 (e.g. task `12ae8322`):

> Boot prompt was never delivered: 10 minutes after dispatch the session wrote no turn prompt
> of either kind for this task (the brief is still queued Pending and was never attempted).

That arm does kill (`AgentTaskDispatcher.cs:1297-1303`), the supervisor resumes the session
("supervised restart attempt N (resume)"), the resume replays the same compacted history with
the same `[antiphon-task:a42e10c4]` line, the head row still has `DeliveryAttempts = 1`, and
step 3 starts again. Per-day counts from the server logs:

| log file (local date) | supervised restarts | Enter-only cycles | watchdog kills | typed attempts | late-confirmed OK |
|---|---|---|---|---|---|
| 2026-09-11 | 1 | 0 | 1 | 0 | 14 |
| 2026-09-12 | 14 | 10 | 4 | 1 | 79 |
| 2026-09-13 (3 files) | 23 | 419 | 20 | 0 | 0 |
| 2026-09-14 (to 18:30) | 43 | 1222 | 43 | 0 | 0 |

`AgentSupervisionStates` for the agent at 18:34: `RestartBackoffFailures = 3`,
`LastObservedRestartStartedAt = 18:23:25`, `NextRestartAt = 18:34:43`, with kind-2
`RestartScheduled` incidents at 18:11:33, 18:22:53, 18:34:03 (10 s, 20 s, 40 s backoff). A
kind-3 `Recovered: running healthily for 10 min` incident at 14:18:23 shows the supervisor
considers the session healthy whenever it stays up ten minutes, which it always does between
watchdog kills, so backoff keeps resetting and the loop never escalates.

## What "execution generation/profile" means

`SpecialistTaskRunner.CreateRunTaskAsync` (`SpecialistTaskRunner.cs:320-334`) snapshots the
specialist's live session at task creation: `SpecialistSessionId`, `SpecialistSessionStartedAt`
and `SpecialistProfileRevisionId`. `AgentTaskDispatcher.PlaceOnStandingAgentAsync`
(`AgentTaskDispatcher.cs:4557-4563`) refuses placement when the live session's `StartedAt` or
`TuiProfileRevisionId` differs from the snapshot:

```csharp
if (claimed.SpecialistSessionId is { } expectedSession
    && (session != expectedSession || live?.StartedAt != claimed.SpecialistSessionStartedAt
        || live?.TuiProfileRevisionId != claimed.SpecialistProfileRevisionId))
    throw new SpecialistIdentityMismatchException(... "has a different execution generation/profile.");
```

A standing agent's supervised restart is a **resume of the same session row**: the guid is
unchanged and only `StartedAt` advances (`AgentControlService.cs:498`,
`SessionGeneration.Next(prior, now)` in `src/Antiphon.SessionRunner.Contracts/SessionGeneration.cs:24`).
That is why the same guid recurs in every mismatch message. The profile revision
(`19c2faee`) never changed; every mismatch is a `StartedAt` change.

All 7 mismatch failures since 2026-09-12 line up with resumes to the second (task created
seconds before a resume completed, placed just after):

| task created | selected generation | live generation at placement |
|---|---|---|
| 09-12 17:13:32 | 17:06:48.233 | 17:13:36.163 |
| 09-12 17:31:41 | 17:24:52.241 | 17:31:51.010 |
| 09-12 18:06:37 | 17:44:07.777 | 18:06:54.743 |
| 09-12 18:50:15 | 18:06:54.743 | 18:51:10.363 |
| 09-12 20:18:19 | 18:51:10.363 | 20:18:20.871 |
| 09-12 23:09:43 | 20:18:20.871 | 23:10:33.640 |
| 09-14 13:00:19 | 12:49:19.513 | 13:01:00.596 |

The mismatch is therefore a **symptom** of the restart loop, not a cause. The dominant
INTERPRETER DOWN shape is the other one: `SpecialistTaskRunner.RunAsync` waits
`CheckInterpreterWaitSeconds = 60` (`DelegationSettings.cs:636`) for the run task to settle,
and it never does because the brief is stuck behind the wedged head row, so the run is canceled
("The caller that asked for it stopped waiting.", 92 rows since 09-13) and
`CheckInterpreterUnavailable` (incident kind 26) is raised with "no reading within 60s".

## Outcome census for the interpreter agent (`AgentTasks`, agent `be5d4502`)

| day (UTC) | Succeeded | Failed | Canceled |
|---|---|---|---|
| 09-10 | 62 | 0 | 0 |
| 09-11 | 78 | 1 | 0 |
| 09-12 | 97 (last at 17:00:49) | 10 (6 mismatch) | 89 |
| 09-13 | 0 | 23 | 65 |
| 09-14 (to 18:30) | 0 | 40 (1 mismatch) | 27 |

Per-generation breakdown (78 distinct `SpecialistSessionStartedAt` values since 09-12 00:50):
every generation from 17:06:48 onward has `ok = 0`; the 13:35:31 generation before it had 48
of 51 succeed.

Note on "steady background rate": before the wedge, almost every delivery to this session was
already being late-confirmed after three Enters (14 on 09-11, 79 on 09-12: "our ingestion was
just behind"). That was slow but succeeded, and is a separate observation from this card.

## Ruling out the alternatives

- **CARD-0415 usage wall**: not involved. The session never submits anything, so no model call
  is made; `ModelAvailabilityHolds` is irrelevant to a composer that is never fed.
- **Claude Code binary change**: `claude` 2.1.266, file dated 2026-09-09; unchanged across the
  boundary.
- **Server deploy**: the server restarted at 16:42 UTC on 09-12 (24 min before ignition) and
  again 21:02 UTC and twice on 09-13; sessions survive server restarts (runner is a separate
  daemon) and the loop continued across all of them. The running server SHA (`8ccdb1c9`,
  `GET /api/version`) contains every line cited above (verified with `git show 8ccdb1c9:<file>`).
- **Transcript binding**: the runner is tailing the correct JSONL
  (`session-runner-20260914.log` 19:11:50 local). The JSONL itself has no record after
  2026-09-12 17:04:27; the TUI truly has not been fed a prompt.
- **Wrong session or agent**: `Agents.PersistentSessionId` for `antiphon-check-interpreter` is
  `cea73d57-…`; it is the only Running session for the agent.

## Uncertainties

1. Why the single real typing attempt at 2026-09-12 17:11:47 saw "screen output never
   advanced" 5 minutes after a resume. The runner Serilog for that minute holds nothing but the
   transcript-tail line, and the per-session `.ansi.log` is a 9 MB replay-heavy file I did not
   dissect. The current generation's raw output proves the resumed TUI does echo typed text
   (the probe), so a fresh full re-type today would most likely succeed. Candidate explanations:
   a resumed-after-compaction TUI not yet draining stdin (the CARD-0103 dead zone, measured
   48-200 s), or a pty/ConPTY hiccup on that one generation.
2. Whether a resume without a preceding compaction would also show the old marker line within
   the 30 rendered rows. It should (the replay always ends with the last brief and reply), but
   the trap additionally needs a head row with `DeliveryAttempts > 0`, which only a failed real
   attempt produces, so the compaction is not strictly required for the wedge, only for the
   ignition observed here.

## Not done, noted

- Fix idea, one line: cancel/park the wedged head row (or make the Enter-only path charge an
  attempt, capture a generation, and require a window that includes the task id rather than
  the shared `[antiphon-task:` prefix) so a replayed history can never satisfy
  `HeadFragmentIsVisible`. Not designed or implemented here.
- I did not clear the queue or restart anything; the loop is still running as of
  2026-09-14 18:34 UTC.
