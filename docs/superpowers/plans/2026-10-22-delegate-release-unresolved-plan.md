# CARD-1499: false DelegateReleaseUnresolved when the runner says the session is gone

Card: CARD-1499 (Antiphon board). Evidence: `docs/investigations/2026-10-22-post-restart-rc-6e79f3e13-health.md`
section 7.3 (Debug 61836f59). Code task 7cea69ea, branch `feat/card-task-7cea69ea`.

## Findings (step 1, read-only)

- **Where the incident is raised.** `AgentIncidentKind.DelegateReleaseUnresolved` (76) is written by
  `AgentTaskReplyService.ReleaseDelegateAsync` (settlement; message "the release kill of session ...
  did not end it, so the pool row was kept Idle for the janitor") and by
  `PoolDelegateRelease.RecordUnresolvedOnceAsync` (the dispatcher's pool-release sweep and the
  janitor). All three share `PoolDelegateRelease.KillAndVerifyAsync`.
- **Call chain.** `KillAndVerifyAsync` calls `IDelegateSessionStopper.KillAsync` (=
  `AgentSessionService.KillAsync` -> `KillOnAsync`), logs and swallows any exception, then asks
  `IsSessionTerminalAsync`. That is resolved only when the session row is `Stopped`/`Failed` **and**
  `AgentSessionRuntime.IsLiveOrUnknown` is false for it (for a runner-less row without a runtime:
  `Stopped` only). Everything else is `SessionStillLive`, which keeps the row Idle for the janitor and
  records the Error.
- **What `KillOnAsync` does.** It sets the row `Stopping`, calls `_runtime.KillAsync` (the runner's kill
  answer; `true` when it reports `Exited` or an exit code) and then `_runtime.GetSessionAsync` to read
  the exit. Any exception from either call goes to `RecordUndeliveredKillAsync` (row stays `Stopping`,
  `FailureReason = "kill not delivered: ..."`, plus the CARD-0679 deferred-kill intent, kind 75
  "settlement/operator kill after transport loss: ...") and is rethrown.
- **Why "Session not found" is unresolved.** On a phone-home (server2) runner the kill is
  `PhoneHomeRunnerClient.KillAsync` -> `ReleaseSlot`. `SessionRunnerRuntime.ReleaseSlotUnderGateAsync`
  kills the process, **removes the record** from `_sessions` and answers `Exited` (for a record it no
  longer holds it answers `Exited`/`Released` too). The follow-up `GetSessionAsync` then hits
  `GetSession` -> `KeyNotFoundException("Session '<id>' was not found.")`, which
  `PhoneHomeCommandDispatcher` maps to a 404 `not_found` error frame and the server raises as
  `RunnerProblemException(404, ..., "not_found")`. `KillOnAsync` treats that 404 like transport loss,
  so a delivered and confirmed release reads as an undelivered kill: kind 75, then `SessionStillLive`
  -> kind 76 Error, then the slot reconcile drains the intent as kind 74 "deferred kill: missing" and
  stamps the row `Stopped` with "kill not delivered". The local HTTP runner keeps an exited record
  after `/kill`, which is why only server2 settlements show it.
- **Resolved vs unresolved today.** Resolved: the kill returns and the read returns `Exited` or an
  exit code, and no live/unknown runtime owner remains. Unresolved: any kill or read exception
  (including this 404), a read that reports neither `Exited` nor an exit code, or a runtime that
  still lists the session live/unknown.

## Change (D-1)

In `AgentSessionService.KillOnAsync` only: when the runner's kill answer already reported the exit
(`killed == true`) and the follow-up read fails with `RunnerProblemException` status 404 code
`not_found`, take the session as exited (no exit code) instead of recording an undelivered kill. The
existing path then marks the row `Stopped`, closes the attempt and returns normally;
`PoolDelegateRelease.IsSessionTerminalAsync` still applies the unchanged runtime liveness veto, so a
runner that still lists the session live keeps the Error.

Unchanged: what counts as live (`IsLiveOrUnknown`), every runner/restart path, `KillGenerationAsync`,
the deferred-kill ledger for real transport loss, a kill that throws (including a kill that itself
answers 404 on the local HTTP runner, which keeps its record and never produces this shape), a read
that throws anything else (timeouts included) and a kill answer that is not an exit.

## Slices

- S1: plan + red witnesses (`AgentTaskReplyIntegrationTests.C1499ReleaseNotFound.cs`).
- S2: the D-1 fix.
- S3: report; the final checkpoint run verifies this tip.

## Verification

- V-1: a release answered `Exited` (killed, exit code) then a 404 `not_found` read settles as a normal
  release: pool row deleted, session `Stopped` with no failure reason, no kind 76 and no deferred-kill
  intent. Red before S2.
- V-2: the same with the runner's already-released answer (`Exited`, no exit code). Red before S2.
- V-3 (guards, green before and after): a kill that throws, a read that times out, a kill answer that
  is not an exit, and a runner that still lists the session `Running` each keep the row Idle and record
  exactly one kind 76 at Error.
- R-1: the existing CARD-0691 kill/release tests stay green (`AgentSessionServiceIntegrationTests`
  whole class; the `AgentTaskReplyIntegrationTests` kill-and-release cases named in CP-2).

PC pending for Mutation: PC-1 remove the D-1 catch (V-1/V-2 go red); PC-2 drop the `killed` condition
(V-3 `release-not-exited` goes red); PC-3 widen the catch to any exception (V-3 `read-times-out` goes
red).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1499a/` | witnesses | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/C1499_*` | V-1, V-2, V-3 | 6 listed; at S1 2 failed (V-1, V-2) and 4 passed (red record); after S2 0 failed | 6 | 5 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1499b/` | release-classes | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/(C1499_*)\|(C691_*)\|(a_kill_*)\|(a_failed_verdict_*)` | V-1..V-3, R-1 | all listed, 0 failed | 10 | 8 | |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1499b/` | session-service | `/*/Antiphon.Tests.Application/AgentSessionServiceIntegrationTests/*` | R-1 | all listed, 0 failed | 20 | 8 | |

Lane: server2 (Linux, the runner default); Postgres-backed integration tests, no process spawn.
