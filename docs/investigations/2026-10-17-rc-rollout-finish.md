# RC rollout finish, redeploy-old through retire-temp (task c0d49725)

Stopped at gate 6. `redeploy-old` refused `RecycleLandUnknown` before any container change. Smoke/Both, `drain-temp`, and `retire-temp` were not run. No retry: the rollout doc treats this token as a stop, and it does not allow a same-class tooling repair.

Requested image SHA for every phase: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.
Canonical checkout and `GET /api/version`: `ece7cb2e105cf60b5114034aec226272759ff7cd`. `/health` was Healthy. AppHost and the desktop runner were not restarted. The checkout was not switched or pulled.

The doc names gate 6 `redeploy-old`. There is no separate `upgrade-old` phase. That is the command that ran.

## Preflight (2026-10-17, before the phase)

| Check | Result |
|---|---|
| `GET /api/agent-tasks?landPending=true&projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | `items` empty |
| `server2` | `buildVersion=7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, `draining=true`, `redirectTo=server2-temp`, `retireWhenIdle=false`, `acceptingNewWork=false`, `sessions=0`, `queuedTasks=0`, `runnerSessions=0`, slots `occupied=0` |
| `server2-temp` | `buildVersion=09e48c5d6f1d404cfea8ec0d992270424fd2a184`, `acceptingNewWork=true`, `dispatchEligible=true`, `draining=false`, counters 0 |

## Phases

| Phase | UTC | Exit | Result |
|---|---|---|---|
| `redeploy-old` | start `2026-10-17T04:40:38.2861481Z`, end `2026-10-17T04:41:12.9485372Z` | 2 | `Rolling deploy stopped: HostCaseFailed deploy-parent exit=2`, `DIAGNOSIS=RecycleLandUnknown` |
| smoke `server2` and `verify-card0849-caches.ps1 -Case Both` | not started | | |
| `drain-temp` | not started | | |
| `retire-temp` | not started | | |

Command, from `C:\src\Antiphon`:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 09e48c5d6f1d404cfea8ec0d992270424fd2a184 -Phase redeploy-old
```

## Receipts

Evidence root: `C:\src\Antiphon\.antiphon\rolling-server2\c7275f99c4566f48`

- `deploy-parent\c590-result.json`: `accepted=false`, `diagnosis=RecycleLandUnknown`, `exit=2`
- `deploy-parent\census-deploy-parent-server2.json`: `refusal=RecycleLandUnknown`
- `census-redeploy-old-server2.json`: wrapper census completed and the phase reached `deploy-parent`
- `host-jq-redeploy-old.json`: host jq check ran before the host case

The wrapper open list for project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` had 5 items and did not include `55eaef83-6250-46d0-a422-a44661a9eb5f`. The host open list for the same path had 6 items. The host census row for that id, with no `landRequest` merged, was `status=Queued`, `runnerId=desktop`, `projectId=null`, `scopeSource=None`, both land timestamps null. The last host read was `GET /api/agent-tasks/55eaef83-6250-46d0-a422-a44661a9eb5f` (HTTP 200), then the detail/summary proof refused. A later read of that task showed `summary.status=Succeeded` and `landRequest=null`. Other open rows in the host receipt are desktop tasks, not `server2`.

## After the refusal

`server2` was still `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, drained, idle, redirect `server2-temp`, not accepting. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`. Standing `server2` `buildVersion` is not the requested SHA. `server2-temp` is not retired.

A later `redeploy-old` needs an explicit decision. This pass did not start one.

## Authorized retry (2026-10-17)

One new `redeploy-old` was authorized after `55eaef83` had settled. Preflight: land-pending `items` empty; no Queued or Dispatched task on `server2` or `server2-temp`. `be14f4d6` was Dispatched on desktop. `server2` was still drained and idle on `7507b3cf`; `server2-temp` was still accepting `09e48c5d`.

| Phase | UTC | Exit | Result |
|---|---|---|---|
| `redeploy-old` | start `2026-10-17T04:46:13.0431861Z`, end `2026-10-17T04:46:33.6622924Z` | 2 | `HostCaseFailed deploy-parent exit=2`, `DIAGNOSIS=RecycleLandUnknown` |

Evidence root: `C:\src\Antiphon\.antiphon\rolling-server2\c727f40d601ff14e`

The host open list had 6 items. The row with no `landRequest` merged was `c066e2f4-2472-44f3-9717-1e7a453cb1e0`, `status=Dispatched`, `runnerId=desktop`, `role` not stored on the census row, `projectId=null`, `scopeSource=None`. A later read of that task was `summary.status=Succeeded`, `role=Check`, `landRequest=null`. Same token as the first refusal, different task. No second retry. Smoke/Both, `drain-temp`, and `retire-temp` were not started.

After this refusal, `server2` was still drained and idle on `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce` with redirect `server2-temp`. `server2-temp` was still accepting `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.
