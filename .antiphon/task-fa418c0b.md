# Review fa418c0b: CARD-1499 Final Review (Full) of Code task 7cea69ea

Outcome: CLEAN. Reviewed `a76d2974aa9989137c24da8d70f0dda2aa5dec75` (feat/card-task-7cea69ea), base `adc32643f`.
Owner amendment needed: CP-3 table row (section 6). This is not a finding: the land runs on this review's valid receipts.

## Receipts (server2, checkpoint tool, tree clean, dirty=0)

- Run `20261022-172819-62e2` (plan table as committed, `--rows CP-1,CP-2`), verdict GREEN:
  - `CHECKPOINT CP-1 ... executed=6 passed=6 failed=0 skipped=0 ... sourceState=clean buildSource=verified`
  - `CHECKPOINT CP-2 ... executed=13 passed=13 failed=0 skipped=0 ... sourceState=clean buildSource=verified`
  - `validate-checkpoint-receipt.ps1`: `CHECKPOINT SOURCE VALID source=a76d2974... rows=2`
- Run `20261022-174610-8b0d` (amended one-row plan `.antiphon/rv1499/cp3-amend.md`, CP-3 narrowed), GREEN:
  `executed=20 passed=20 failed=0 skipped=0`, `CHECKPOINT SOURCE VALID ... rows=1`.
- Unlisted (with reasons): one tool build (`bin-rv1499tool/`, build-slot, waited 0 s; deleted); one mutation
  build of `tests/Antiphon.Tests` in a scratch detached worktree (build-slot) plus one build-slot run of four
  `C1499_*` invocations (adequacy mutations, section 5); the worktree was removed afterwards.
- `git merge-tree --write-tree origin/master HEAD` (master `445df8a80`): clean.
- `check-evidence-diff.ps1 -BaseRef adc32643f -HeadRef HEAD`: `commits=4 entries=1 violations=0`.

## 1. Classification safety (answer shape x runner kind)

`AgentSessionRuntime.KillAsync` gives `killed = Status=="Exited" || ExitCode != null` from the runner's kill answer.
The only new behaviour is in `ReadAfterKillAsync`: `killed && RunnerProblemException{404,"not_found"}` on the read.

| Kill answer | Read after kill | Before | After |
|---|---|---|---|
| kill throws (404 on kill, 5xx, timeout, refused, auth, Conflict "still live") | not reached | undelivered (75), Stopping, 76 Error, row kept | same |
| Running/Stopping/Starting, no code (`killed=false`) | 404 / any exception | undelivered, kept | same (the catch needs `killed`) |
| `killed=false` | 200 Exited | Failed, then veto | same |
| Exited or exit code (`killed=true`) | 200 | Stopped/Failed, then veto | same |
| `killed=true` | 404 `not_found` | undelivered, 76 Error (the bug) | **Stopped, then veto** |
| `killed=true` | 404 other code, 5xx, timeout, transport, auth | undelivered, kept | same |

Runner kinds:
- Phone-home (server2): `ReleaseSlotUnderGateAsync` evicts a record only after `session.HasExited` (it throws
  "still live ... custody was retained" otherwise, and on a generation change). For a record it does not hold, it
  answers `Exited/Released` without killing. That is the CARD-0653 contract and is not changed here. Phone-home
  only starts at `app.Run()`, after `AdoptOrphanedHostsAsync` (`src/Antiphon.SessionRunner/Program.cs:188`).
  Herdr-unreachable OS-alive panes are adopted as tracked Pending records. So an absent record means the
  runner owns no process for that id.
- Desktop HTTP: it keeps an exited record after `/kill`, so the read is 200 and the path is unchanged. A 404
  arises only if the record is evicted between an Exited kill and the read. Eviction also requires a verified
  exit.

No shape deletes a pool row while the runner could still hold a live process. Residual (not new): the fix
now trusts the phone-home `Released` answer for an absent record. Before, that answer led to an Error anyway.
Its safety rests on the runner's adoption census being complete before it connects, which holds as above.

## 2. "Runner no longer lists the session live" veto

`PoolDelegateRelease.IsSessionTerminalAsync` (unchanged) checks `!runtime.IsLiveOrUnknown(id, runnerId)`. That
is `RemoteInventoryPending(runnerId)` (no answer from that runner since server start) OR membership in live
sessions plus `UnknownRemoteSessionIds()` (sessions on a disconnected runner). A List failure propagates
(CARD-1362 D-3). So "no connection yet" and "disconnected" cannot read as gone.

On phone-home, `PhoneHomeRunnerClient.ReleaseSlotAsync` itself calls `NoteSessionGone` on a non-running answer.
The veto therefore adds little for the released id. The deletion rests mainly on the runner's verified-exit
answer over a live connection (section 1). The veto is still proven by the `runner-lists-live` guard and the
`nolive` mutation.

## 3. Callers

- `ReadAfterKillAsync` has one call site (`KillOnAsync`).
- `KillOnAsync` has one caller (`AgentSessionService.KillAsync(sessionId, source, ct)`; the 2-arg overload
  forwards to it). That caller serves settlement (`AgentTaskReplyService.ReleaseDelegateAsync`), the dispatcher
  pool-release sweep and the janitor (`PoolDelegateRelease.KillAndVerifyAsync`), and operator/system stops.
- Every caller changes only in the `killed && 404 not_found` read shape.
- Not touched: the adapter kills at `AgentSessionService.cs:272/313/827`, the `_runtime.KillAsync` uses at
  `:1413/:1470`, `KillGenerationAsync`, `PoolDelegateRelease`, the runner and every restart path.
- Desktop is identical except for the eviction race above.

## 4. Events and incidents

On the new path there is no `RecordUndeliveredKillAsync`. So no kind-75 `RunnerSlotReleaseIntent` is written,
and no kind-74 `RunnerSlotForceReleased` "deferred kill: missing" drain follows. The row goes `Stopping` to
`Stopped`, the attempt goes to Canceled, `DisposeSessionAsync` runs and `SessionExited` is published once (the
same tail as a normal kill). Kind 76 is not raised. The V-1/V-2 assertions check 0 incidents of kinds 76 and 75.
A genuinely unresolved release still keeps the row Idle with one 76 Error (V-3 x4); the janitor code is
unchanged.

## 5. Red record and adequacy

- Code's run `20261022-162434-b3c6` at `6aa23a90f` (TRX read): both `normal_release(killed|already-released)`
  failed on `Agents.AnyAsync(a => a.Id == agentId) should be False`; the four guards passed. That is the stated
  reason.
- Mutations, env-gated in a scratch build of the tip, `C1499_*` (6) each. Each mutation turns exactly the named
  guard red ("agent should not be null"):

| Mutation | total | failed | Red guard |
|---|---:|---:|---|
| control | 6 | 0 | none |
| every 404 is exited (drops `killed` and the code) | 6 | 1 | `(release-not-exited)` |
| any exception is exited when killed | 6 | 1 | `(read-times-out)` |
| skip the live-list veto | 6 | 1 | `(runner-lists-live)` |

- "Remove the catch" is the pre-fix state (the red record above). Plan PC-1..PC-3 stay pending for the
  SourceLanding Mutation.

## 6. CP-3 owner amendment

`ReportValidator` requires `Skipped == 0` and has no skip allowance. The class row
`AgentSessionServiceIntegrationTests/*` always skips
`AgentSessionService_start_to_first_text_delta_with_real_worktree_and_raw_pty` on Linux (`SkipIfNotWindows`), so it
can never validate. Code's receipt `20261022-163852-0543` validates `CHECKPOINT SOURCE INVALID reason=row_failed`
as a whole.

The excluded test is a raw ConPTY start test, not on the kill path. Replace the CP-3 row in
`docs/superpowers/plans/2026-10-22-delegate-release-unresolved-plan.md` with this one, already proven valid at the
tip (run `20261022-174610-8b0d`):

```
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1499b/` | session-service | `/*/Antiphon.Tests.Application/AgentSessionServiceIntegrationTests/(Cancellation_*)\|(AgentSessionService_first_*)\|(AgentSessionService_start_reuses_*)\|(AgentSessionService_kill_*)\|(AgentSessionService_turn_*)\|(Claude_*)\|(Grok_*)\|(RunAttempt_*)\|(C691_*)\|(a_kill_*)` | R-1 | 20 listed, 0 failed (excludes the Windows-only raw ConPTY start test) | 20 | 8 | |
```

The amendment is a plan-only commit. Per D-2 it needs no Review rerun, and this review's CP-1/CP-2/CP-3 receipts
stand for the code at `a76d2974a`.

## 7. rc

On a scratch detached worktree at `origin/rc/2026-10-20` (`0714a9a89`), `1f0c20d68`, `6aa23a90f` and `382423d9e`
cherry-pick clean (the worktree was discarded). The rc result carries only the plan doc (droppable), the
`AgentSessionService.cs` fix and the test file.

The `ReadAfterKillAsync` body is identical on the rc. These are byte-identical rc vs tip:
- `PoolDelegateRelease.cs`
- `PhoneHomeRunnerClient.cs`
- `SessionRunnerRuntime.cs`
- `AgentTaskReplyService.cs`
- the `AgentSessionRuntime` `KillAsync`/`IsLiveOrUnknownAsync`/`ListLiveOrUnknownSessionsAsync` bodies

Every test helper the test file uses exists on the rc. Nothing was built on the rc.
