# 2026-10-17 rc rollout finish — redeploy-old stop

Deploy task `77abf8f0` on `feat/card-task-77abf8f0`. Target `-Sha` `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Canonical checkout and `GET /api/version` stayed `69978ec1a246cd7a860f88a364eafc53a416aff7`. AppHost and the desktop runner were not restarted. No scripts were edited.

Preflight at start: server2 drained and idle (`acceptingNewWork=false`, `redirectTo=server2-temp`, sessions/runnerSessions/queuedTasks all 0, build `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, container `fc2983ea4991` running since `2026-10-11T13:42:57Z`). server2-temp accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` land-pending was empty before each start. A Running land on `187ee0ab-d84b-4ffe-9f87-30cdc92b01aa` (CARD-1320 Code) cleared before the first start.

Smoke, `drain-temp`, and `retire-temp` were not run.

## Attempt 1 — census retry class

- Start `2026-10-17T12:12:55Z`, wrapper pid 1568, exit 2 about `2026-10-17T12:13:34Z`.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727e81ce041ca91`.
- Diagnosis verbatim: `RecycleTaskCensusUnknown cause=Unstable`.
- Host case: `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.
- Pass 1 of the deploy-parent census gained desktop check `c8dac380-0774-4ed1-9e59-7b1fbbd7d728` (`check #1 on task 77abf8f0`, Queued, scopeSource None). A later read showed it Succeeded with `landRequest` null.
- No container stop. Standing container start time unchanged.

## Attempt 2 — second census retry

- Start `2026-10-17T12:18:24Z`, wrapper pid 33776, exit 2 about `2026-10-17T12:20:17Z`. About 60s after attempt 1.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c727ce33eabc1a49`.
- Diagnosis verbatim: `RecycleTaskCensusUnknown cause=Unstable`.
- Pass 1 gained desktop Diagnose `fcd95bb3-764d-421c-911d-b809775f3cf6` (`labels for CARD-1287`, Queued). A later read showed it Succeeded with `landRequest` null.
- No container stop. Standing container start time unchanged.

## Attempt 3 — stop, containers removed

- Start `2026-10-17T12:23:48Z`, wrapper pid 36816, exit 2 at `2026-10-17T15:19:44Z`.
- Receipt `C:\src\Antiphon\.antiphon\rolling-server2\c72724f3f2dfd7b8`.
- Local redeploy-old census passed. deploy-parent then audited and removed containers before its own census refused.
- Audit helper container `abc517209651` (`boring_herschel`) ran the old image `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835` from `2026-10-17T13:51:05Z`. Audit summary line: `repositories=800 partial=800`. No `RecycleGitAuditUnknown` line. Helper tmpfs stayed about 20M of 2.0G. The helper is gone.
- `deploy-parent/recycle.json` operation `c100808382374392e4ce1ac7dde48ca3b195f`: `phase=containersRemoved`, `outcome=refused`, `ownedRemoved=true`, `dryRun=false`.
- Stop and remove receipts (2 each): `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832` and `2c96347842e49022771fccc34f74bb04549ceeb603a5944d860b12f438953cfe`.
- Volume outcomes all `pending`: `antiphon-runner_work`, `antiphon-runner_runner-tmp`, `antiphon-runner_dind-data`.
- Diagnosis verbatim: `RecycleLandInFlight 4ce63b7a-db63-4189-90a5-96a55abb03b3`.
- Stderr verbatim: `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`.
- That task was a Succeeded Plan on server2-temp titled `Plan release branch model amendment`, `landRequestedAt` `2026-10-17T15:17:43Z`, `landRequest.state=Running` when read just after the stop. This token is not in the settled-census retry class. Both allowed retries were already used.

## State at stop

- `antiphon-runner-session-runner-1` is absent. server2 `available=false`, `dispatchEligible=false`, `acceptingNewWork=false`, `draining=true`, `redirectTo=server2-temp`, `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`, `processBootId=9a6d75cc-09cb-4034-818e-440ceb9e2d2f`, `disconnectReason=transport_abort`, `runnerSessions` null, sessions 0, queuedTasks 0.
- server2-temp still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184` (`antiphon-runner-temp-session-runner-1` up). It was serving work (5 sessions, 1 queued) at the stop read.
- Images `antiphon-server2/session-testing:09e48c5d6f1d` and `:7507b3cfe0e2` are still present. The old image id matches the recycle journal.
- No second deploy was started after the stop. Smoke, drain-temp, and retire-temp were not started.
