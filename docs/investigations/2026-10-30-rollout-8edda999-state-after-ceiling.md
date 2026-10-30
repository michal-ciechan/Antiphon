# Rollout rc 8edda999: state after the Deploy ceiling failure

Investigate task a2f1dc5f, 2026-10-30, read-only. Subject: Deploy task 2e4f7375
("Resume rc 8edda999 rollout at redeploy-old"), marked **Failed** at
2026-10-30T00:01:37Z by the 240-minute Deploy role ceiling. Session
c205656a-2dcc-4029-a9a7-1cca0a938136 was not killed.

Observations were taken between 00:03Z and 00:06Z on 2026-10-30.

## Verdict

**(A) The old session is alive and is still driving the rollout.** It is inside
gate 8 (`drain-temp`), waiting for two Grok tasks on server2-temp to settle. Gates 6
and 7 passed. Nothing is stuck, and no lock or journal is held. Do not dispatch a
second Deploy while this one runs.

## 1. Session c205656a: alive and working

- Runner `GET :17204/sessions/c205656a-...`: `status=Running`, pid 181216, hostPid
  84236, `transcriptBound=true` (exact).
- Transcript `C:\Users\lndco\.claude\projects\C--src-Antiphon\c205656a-2dcc-4029-a9a7-1cca0a938136.jsonl`
  (2.2 MB). Last write was 00:03:52Z, when a new model request started after a foreground
  wait returned. The session was still active after that:
  - 00:03:56 to 00:04:12: it identified the two temp-bound tasks via
    `runner-slots.ps1 list -RunnerId server2-temp`. They are 6a33fe77 and f5557ddb.
  - 00:04:14: "drain-temp waits for them, so I'm waiting."
  - 00:04:16, 00:04:18 and 00:04:20: it issued three chained foreground waits, each at
    most 10 minutes (`for i in $(seq 1 29); do grep -q "EXIT=" <b7h8zh863.output> && break; sleep 20; done`).
    The third also prints the occupied count and the `openTaskId` lines.
- The ceiling notice never reached the session. The string `ceiling` occurs in its
  transcript only inside the loaded instruction files, at lines 15 and 22. The session
  does not know that its task row is Failed.
- The failure reason on the task row says: "Ran 4h00m against the 240-minute ceiling
  for role Deploy. Last transcript entry: ToolCall, 7m ago ... The session was NOT
  killed".

Process tree under pid 181216 (from `Win32_Process`, with secrets redacted):

| pid | started (UTC) | command |
|---|---|---|
| 89244 | 2026-10-29 23:54:02 | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8edda999623a473be2d1970727c172bfb5088d59 -Phase drain-temp -WaitIdleMinutes 240`: background shell `b7h8zh863`, alive, no children |
| 17548 | 23:54:15 | bash `until grep -q "EXIT=" b7h8zh863.output; do sleep 30; done`: background watcher `b7hfyir1y` |
| 44100 | 23:54:27 | the earlier foreground wait, still in its final `runner-drain.ps1 status` call or about to exit |
| 86796 | 20:01:35 | todoist MCP node, not relevant |

drain-temp output so far (`...\c205656a...\tasks\b7h8zh863.output`, 39 bytes):
`System.Threading.Tasks.VoidTaskResult`. That line comes from the drain POST, and no
`EXIT=` line has been written yet.

## 2. Phases completed after the canary reply (23:53:20Z)

| step | time (UTC) | result | evidence |
|---|---|---|---|
| Gate 6 `redeploy-old` (earlier, before the reply) | 22:30:31 to 23:48:03 | exit 0 | `C:\src\Antiphon\.antiphon\rolling-server2\c7275c94b6a235d4\c590-result.json` = `{"accepted":true,"diagnosis":"","exit":0}`, `remote-output.txt` = `transport-exit=0`, `verify-runner-caches.manifest.json` (runner server2, source 8edda999) |
| Gate 7 canary ad906f75 | done by 23:52:53 | Succeeded, commit f208c7d4ee86bb0a0f4b08acf50c57a8fa8bc54a | `delegate.ps1 -Status ad906f75`, quoted in the transcript at 23:53:33 |
| Gate 7 smoke (server2) | 23:53:29 | **pass** | `runner-drain.ps1 status -RunnerId server2`: `available/dispatchEligible/acceptingNewWork=true`, `draining=false`, `buildVersion=8edda999...`, `EXIT=0`. The container reports codex-cli 0.160.0, PowerShell 7.5.4 and dotnet 10.0.403, and the container check returned `EXIT=0` |
| Gate 7 `verify-card0849-caches -Case Both` | 23:53:36 to 23:53:58 | **pass, exit 0** | `C849_BOTH kind=cold schema=2 runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`. Evidence: `C:\src\Antiphon\.antiphon\c849-c8497b2be20fc8db4735` (two runner case sets, each with `c590-result.json` and `status.json`, written 23:53:45 and 23:53:56) |
| Gate 8 `drain-temp` | started 23:54:02 | **in progress** | `C:\src\Antiphon\.antiphon\rolling-server2\c727c5ac080d83a0\host-jq-drain-temp.json` (sshExit 0, jq-1.7.1 existing) and `census-drain-temp-server2-temp.json` (23:54:17). The temp drain was set at `drainedAt=2026-10-29T23:54:04.517Z`, `redirectTo=server2`, `retireWhenIdle=true` |
| Gate 9 `retire-temp` / Part C | not started | none | no receipt directory after c727c5ac080d83a0 |

The census-drain-temp receipt lists `tasks: []` even though two Antiphon tasks were bound
to server2-temp at that moment. The wait does not depend on the census. It polls the
temp runner counters and `retiredAt` (`scripts/deploy-server2.ps1:1051-1091`). I am
noting the empty list as an observation only.

## 3. Current fleet state (00:04Z to 00:06Z)

| runner | accepting | draining | sessions/runnerSessions | queued | buildVersion | container |
|---|---|---|---|---|---|---|
| server2 | true (dispatchEligible) | false | 2/2 | 0 | 8edda999 | `antiphon-runner-session-runner-1` started 2026-10-29T23:46:39Z, restarts 0 |
| server2-temp | false | true (redirect server2, retireWhenIdle) | 2/2 | 0 | 8edda999 | `antiphon-runner-temp-session-runner-1` started 2026-10-29T17:46:39Z, restarts 0 |

AppHost `GET /api/version` returns 8edda999623a473be2d1970727c172bfb5088d59 with `land-v2`.
The canonical checkout `C:\src\Antiphon` is detached at 8edda999.

Tasks still on server2-temp, the ones drain-temp waits for (`runner-slots.ps1 list -RunnerId server2-temp`: occupied 2/10):

| task | title | kind | dispatched (UTC) | session | last transcript |
|---|---|---|---|---|---|
| 6a33fe77 | CARD-1603 fix round 3: V-8 lock order must be logged and checked | Grok High, Custom | 2026-10-29 23:29:35 | 7fa8afaa (pid 23655) | 00:05:27, working |
| f5557ddb | CARD-1613 round 3: native repairs and the two C1483 land-graph defects | Grok High, Custom | 2026-10-29 23:35:58 | 087a05bb (pid 12591) | 23:57:44, working, quiet for 8 minutes at 00:05:48 |

Two other Antiphon tasks are on server2: 00b93ae2 (speed-ups S1 round 4) and 5c52f2f0
(CARD-1614 slice A round 2).

## 4. Lock and journal state

- Rollout lock `server2:/home/mc/antiphon-server2/locks/rollout.lock` (a flock, taken
  per operation by `scripts/deploy-server2.ps1:246` and `scripts/c590-remote.sh:5958`).
  A non-blocking shared `flock -n -s` probe returned **FREE**. The file mtime is 23:54
  from drain-temp's host-jq step, and no holder was found. `cache-maintenance.lock` was
  last touched at 23:53 by the C849 Both run.
- Recycle journals: no `*journal*` files under `/home/mc/antiphon-server2` newer than
  2026-10-29, so no unfinished `-ResumeRecycle` operation exists. The redeploy-old
  receipt shows exit 0.
- On the desktop, `.antiphon/rolling-server2` holds only per-run receipt directories
  and no lock file.
- The drain-temp wait loop holds no lock. It polls HTTP every 30 seconds
  (`deploy-server2.ps1:1053`), and its deadline is start + 240 minutes, about
  **2026-10-30T03:54Z**, after which it throws `TempContainerExitTimeout`.

A new Deploy task would not trip on a lock or journal. Running `drain-temp` a second
time would pass its guard, because the drain is already set with `redirectTo=server2`
and `retireWhenIdle` (`deploy-server2.ps1:1301-1308`). It would then poll concurrently
with the live one, and two `retire-temp` runs could race. Gate 8 in the runbook says
"do not start two drains". **Do not start a second Deploy while pid 89244 is alive.**

## 5. Recommendation: (A)

The old session will finish drain-temp and continue to retire-temp and Part C on its own:

- **When:** drain-temp returns once both temp Grok tasks settle, temp retires, its
  container exits and cleanup of the exited container is proven. The two tasks have
  been running 30 to 36 minutes against a 10-minute expectation. f5557ddb's transcript
  has been quiet since 23:57:44Z. There is no end time on record. The hard bound is
  the drain-temp deadline of about 03:54Z, which ends in `TempContainerExitTimeout`.
  The session re-issues 10-minute foreground waits, so it keeps polling until then.
  It does not hit another automatic kill: the ceiling already fired and left the
  session running.
- **How the orchestrator learns the outcome:** the task row is Failed and the
  ceiling notice never reached the session. When the session ends its turn, its
  report may not settle the Failed row. That behaviour is unverified; see the
  uncertainties below. Observe the outcome directly instead:
  1. Gate 8 done: `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp`
     shows `retiredAt` set and `available=false`, and the drain-temp output file
     `C:\Users\lndco\AppData\Local\Temp\claude\C--src-Antiphon\c205656a-2dcc-4029-a9a7-1cca0a938136\tasks\b7h8zh863.output`
     has an `EXIT=` line.
  2. Gate 9: a new receipt directory under `C:\src\Antiphon\.antiphon\rolling-server2\`
     after `c727c5ac080d83a0`, containing the retire-temp/C1008 recycle receipt.
  3. When the transcript tail at `C:\Users\lndco\.claude\projects\C--src-Antiphon\c205656a-2dcc-4029-a9a7-1cca0a938136.jsonl`
     shows a final report, or the runner session goes idle, run `delegate.ps1 -Reply 2e4f7375`
     to reopen the task and ask for the complete report. The 23:53:20 reply text says
     "This reply reopens task 2e4f7375", which shows that a Reply reopens this task.
- **Fallback (B), only if pid 89244 or session c205656a dies before `EXIT=`:** from the
  canonical desktop checkout `C:\src\Antiphon` (at 8edda999), re-run gate 8 and then gate 9:
  `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 8edda999623a473be2d1970727c172bfb5088d59 -Phase drain-temp -WaitIdleMinutes 240`,
  then `... -Phase retire-temp`. The drain is already set and the rollout lock is
  free, with no journal to resume, so no `-ResumeRecycle` is needed. If drain-temp
  fails with `TempContainerExitTimeout` at about 03:54Z, follow gate 8's refusal
  column: leave server2 accepting, leave temp as observed, and report.

## Remaining uncertainties

- It is unverified whether a `[antiphon-report:2e4f7375 ...]` emitted after the
  ceiling settle is recorded or discarded. I did not trace the report-ingest path for
  terminal rows.
- Whether f5557ddb's 8-minute transcript silence is a long build or a stall was not
  examined.
- The census-drain-temp receipt shows `tasks: []` while two Antiphon tasks were bound
  to temp, and the cause was not investigated.

## Not done, noted

- The ceiling settled a Deploy task that was still actively running a sanctioned
  `-WaitIdleMinutes 240` phase, and it did not notify the session. A possible fix is
  for the Deploy ceiling to account for the rollout's declared wait, or to deliver
  the notice. No card was filed.
