# Log sources and retrieval

This is the authoritative operator inventory for logs and transcript evidence. Run the helper from a checkout with `pwsh -NoProfile -File scripts/logs.ps1 -Source <name> -Tail 100`. It is read-only. For HTTP sources it reads `ANTIPHON_API`, the task token when supplied, and the operator-token file for Hangfire; it does not print those credentials or read provider credential stores. Logs and transcripts can contain user/task content, so keep output local and do not paste it into public issues.

`C:\src\Antiphon` below is the canonical desktop checkout. A linked worktree is not the owner of the local stack. A missing file is evidence: report it with its source name, do not create an empty substitute.

## Desktop and AppHost

| Source | Path and retrieval | Rotation / retention | Last desktop verification (2026-09-24) |
|---|---|---|---|
| `desktop-server` | Server Serilog files: `C:\src\Antiphon\server\logs\antiphon-*.log`; `scripts/logs.ps1 -Source desktop-server`. The configured relative `Serilog:LogPath=logs` resolves from the server content root. The helper resolves Git's primary worktree, never its caller's linked worktree, then selects the newest matching file. | Daily; 100 MB per file; time retention 5 days; 14-file disk backstop. | Retrieved from the newest server-content-root log. AppHost supervision does not by itself prove the server is accepting requests. |
| `apphost` | `C:\src\Antiphon\logs\apphost.log`; `scripts/logs.ps1 -Source apphost`. This is the parent Aspire/AppHost stdout/stderr, not the server Serilog file. The dashboard is `http://localhost:17205`; launch/restart ownership is recorded by `C:\src\Antiphon\logs\apphost.launch.lock` and `apphost.restart.lock`. | No application rotation policy; the restart/supervisor owns it. Never remove a lock to force a restart; use the runbook. | Retrieved; shows the AppHost listening on 17205. |
| `session-runner` | Supervised runner stdout/stderr: `C:\src\Antiphon\logs\session-runner.log`; the runner's own Serilog default is `%TEMP%\antiphon-logs\session-runner-*.log`. `scripts/logs.ps1 -Source session-runner` reads both when present. | Runner Serilog rolls daily, 50 MB/file, retains 14 files and 14 days. The supervisor file has no coded rotation. | Retrieved from the supervisor file. |
| `pty-host` | `C:\logs\antiphon\session-runner\pty-hosts\logs\*.log`; `scripts/logs.ps1 -Source pty-host`. Related runner state is under `C:\logs\antiphon\session-runner` (manifests, transcript sidecars and Herdr sidecars). | Runner cleanup removes pty-host logs and non-live transcript sidecars after 14 days. Optional raw PTY audits are `%TEMP%\antiphon-pty-audits`, disabled unless `ANTIPHON_PTY_AUDIT=1`, capped at 20 MB/session, two days and 50 directories by default. | Path was retrievable. |
| `fake-gateway` | Supervisor output: `C:\src\Antiphon\logs\fake-gateway.log`; delivery ledger: `C:\src\Antiphon\logs\fake-gateway\outbound.jsonl` unless `FakeGateway:DeliveryLog` overrides it. `scripts/logs.ps1 -Source fake-gateway` reads stdout/stderr. | No application rotation policy for either file. The JSONL is a local-dev/test assertion ledger, never production evidence. | Retrieved; current entries include inbound-unconsumed monitor failures. |

The desktop server was initially unavailable during this verification and later recovered enough to serve Hangfire. Do not restart it from a worktree; use [the AppHost runbook](apphost-runbook.md) from the canonical checkout.

## Desktop Postgres query statistics

`docker-compose.dev.yml` preloads `pg_stat_statements` for the desktop's
`antiphon-postgres` only. The server migration creates the extension in the
`antiphon` database. From the canonical desktop checkout, list the top 20
normalized queries by cumulative execution time:

```powershell
docker exec antiphon-postgres psql -U antiphon -d antiphon -c 'SELECT query, calls, round(total_exec_time::numeric, 2) AS total_exec_ms, round(mean_exec_time::numeric, 2) AS mean_exec_ms FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 20;'
```

The times are milliseconds. To start a fresh measurement window, reset the
counters for this Postgres instance (all databases and users); do this only
when you intend to discard the existing measurements:

```powershell
docker exec antiphon-postgres psql -U antiphon -d antiphon -c 'SELECT pg_stat_statements_reset();'
```

The Compose `antiphon` role is the database superuser and can reset the
statistics. The extension can exist in databases without the library
preloaded, including the separate E2E and test Postgres containers; querying
the view there requires preload. The [server2 self-contained stack](docker-stack.md)
also owns a separate Postgres service and is unchanged by the desktop Compose
setting.

## Hangfire jobs and failures

Hangfire is in-process and uses `Hangfire.InMemory`. The dashboard at `http://localhost:17202/hangfire` needs the operator token; open it with `scripts/hangfire-dashboard.ps1` (or check it with `scripts/logs.ps1 -Source hangfire`, which sends the token) and inspect **Failed** and **Recurring Jobs**. Job/history expiration is eight days (`Hangfire:HistoryRetentionDays`); a server restart loses it immediately. There is no durable Hangfire file log or supported API export. Correlate a job failure with `desktop-server` while it exists.

The 2026-09-24 desktop check retrieved the dashboard (HTTP 200). Proposed durability fix: use durable Hangfire storage or export failure summaries if history must survive server restarts.

## Agent transcripts

The server-normalized transcript is the delivery evidence. Obtain a live session id from `GET /api/agents`, then retrieve `GET /api/sessions/{id}/transcript?since=0`; `scripts/logs.ps1 -Source transcripts` does this for live agent sessions using the task token when supplied. Use `GET http://localhost:17204/sessions/{id}/transcript` only for runner-local diagnosis, not as the authoritative historic record.

Database retention is 7 days for normalized transcript rows (`Retention:TranscriptRetentionDays`). Runner sidecars in `C:\logs\antiphon\session-runner\transcripts\<sessionId>.json` are restart/adoption state, not the complete provider transcript, and non-live files are pruned after 14 days. Native provider stores are diagnostic-only and can contain unrelated conversation content: Claude is `${CLAUDE_CONFIG_DIR:-%USERPROFILE%\\.claude}\\projects\\<encoded-cwd>\\*.jsonl`; Codex is `${CODEX_HOME:-%USERPROFILE%\\.codex}\\sessions\\yyyy\\MM\\dd\\rollout-*.jsonl`; Grok is `${GROK_HOME:-%USERPROFILE%\\.grok}\\sessions\\<url-encoded-cwd>\\<session-id>\\updates.jsonl`. Read only the known session's file and never open `auth.json` or credential files.

On 2026-09-27, `scripts/logs.ps1 -Source transcripts -Tail 1` completed from the server2 runner and the server returned 19 live sessions. A separate metadata-only request confirmed `sessionId`, `entries`, and `lastSequence` on one transcript (4,420 entries). The helper throws if there are no live sessions; that means there is no live transcript to sample, not that the endpoint failed. Use a known session id with the server endpoint above for historical retrieval.

## server2 runner and deployment evidence

The runner container is `antiphon-runner-session-runner-1`. From a host with trusted SSH access to server2, retrieve its stdout/stderr plus its persisted runner and nested-Docker logs with `scripts/logs.ps1 -Source server2-runner` (equivalent: `ssh mc@server2 "docker logs --tail 100 antiphon-runner-session-runner-1; docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n 100 /state/runner-logs/session-runner-*.log /state/logs/dockerd.log'"`). The runner Serilog files are `/state/runner-logs/session-runner-YYYYMMDD.log`; nested Docker is `/state/logs/dockerd.log`. Session sidecars and runner-local transcripts are under `/state/session-runner`.

The server2 compose file sets `SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts`, so pty-host logs are **not** under `/state/pty-hosts`. From server2, retrieve the newest host log with `docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n 100 "$(ls -t /tmp/antiphon-pty-hosts/logs/*.log | head -n 1)"'`; for a known session use its `<sessionId:N>.log` in that directory. From inside the runner container, use `tail -n 100 /state/runner-logs/session-runner-*.log /state/logs/dockerd.log` and `tail -n 100 "$(ls -t /tmp/antiphon-pty-hosts/logs/*.log | head -n 1)"`. The pty-host directory is container temporary storage and does not survive container replacement; the runner Serilog and Docker files are on the state volume. Do not inspect `/run/secrets`, `/run/antiphon`, or provider state/auth files. Docker's configured logging driver/rotation is host policy and has not been established by this repository.

The 2026-09-24 host check reached the named container over SSH; its tail contained a phone-home reconnect after HTTP 502. That reconnect issue is now fixed by CARD-0655. On 2026-09-27, direct tails inside the runner succeeded for the current runner Serilog file, `dockerd.log`, and a pty-host log. The helper's SSH path could not be rechecked from inside the container because `ssh mc@server2` failed host-key verification; do not bypass host-key verification to make a log check pass.

Phone-home disconnects (CARD-0679 D-1/D-4, CARD-0716 D-4/D-6) leave one line on each side. On the desktop (`server` log), grep `Phone-home connection` for the accept line's `connection` id and `peer`, and the Warning `... epoch <n> ended: <reason> after <s>s; ...` with the same `connection` id, the pending, in-flight and waiter counts, and, for `transport_abort`, `transport` as `wsError/socketError`. Kestrel event 34 names that connection id. The 50%/90% pending high-water Warning still precedes an overflow. On the runner, grep `Phone-home connection ended:` for `epoch=`, `loop=`, `fault=`, `overflow=`, the pending/in-flight counts, `lifetimeMs=`, `close=` (`EndpointUnavailable:server_stopping` for a planned stop, `none` otherwise) and, when the handshake faulted, `wsError=` and `socketError=`. A registration that fails before the socket is up is `Phone-home registration failed: reason=` with `attempt=` and `backoffMs=` (`http_<status>`, `connect_<SocketError>`, `registration_timeout`, `connect_timeout`, `ws_<code>`, `overflow`, or `other:<Type>`). The Vite `/api` proxy appends the same shape to `logs/client-proxy.log` (truncated past 1 MB) and to the client console: `[proxy] ws client|target socket closed hadError=` names which side closed first. The next time a runner handshake faults while the desktop stays up, read in this order: the desktop accept line whose `connection` id equals Kestrel event 34, the runner's `socketError=` or `wsError=`, the first `[proxy] ws ... socket closed` line (`target` is Kestrel, `client` is the proxy's caller), a `[serve] mode changed` line if a client mode swap killed the preview, then Caddy's access log for `GET /api/session-runners/<id>/connect`.

At the 2026-09-24 host check there was **no retrievable durable server2 deployment-evidence source**: neither a deployment manifest nor `run.log` existed under `/home/mc/antiphon-server2` (only `secrets/` existed, which is deliberately excluded). `scripts/logs.ps1 -Source server2-deploy` inventories that root without touching `secrets/` and deliberately fails even if it lists files; it is a gap check, not a tail command. The 2026-09-27 container check could not inspect the host root because SSH host-key verification failed, so the gap still needs a host recheck. Proposed fix: have the server2 deploy owner write a non-secret manifest and `run.log` (deployment SHA, image digests, compose/config digest, timestamps, health/version probes, and rollback identity) to `/home/mc/antiphon-server2/evidence/`, with an explicit retention policy, then add its exact tail command here.

## Windmill runs

Windmill is the evidence source for scheduled nightly and tracker jobs, not a desktop log file. Open the job in Windmill and use its run-log view; the configured nightly script is `u/lndcobra/antiphon_nightly_tests` and writes durable local run evidence below `C:\Antiphon\nightly\logs\<yyyy-MM-dd-HHmm>-<runId>\` (`build.log`, suite logs and `summary.json`). `scripts/nightly-run.ps1` owns the layout and no local Scheduled Task is supported. This task did not use a Windmill credential or query an unauthenticated endpoint, so live Windmill retrieval remains an access-dependent gap rather than a claimed check.

## Verification checklist

Run each helper source from the machine that owns it after a stack change: the desktop sources need the canonical Windows host and `server2-runner`/`server2-deploy` need a host with trusted SSH access. `desktop-server`, `hangfire`, and `transcripts` must succeed before claiming a desktop incident is fully evidenced; `server2-deploy` is expected to fail until its named gap is fixed. The source-specific commands above are the canonical commands to put in incident notes.

The 2026-09-27 check ran all nine helper sources from **inside the Linux server2 runner**. `transcripts` succeeded. The five desktop-file sources (`desktop-server`, `apphost`, `session-runner`, `pty-host`, `fake-gateway`) could not resolve Windows files there; `hangfire` had no desktop operator-token path; and both SSH sources failed host-key verification. These are environment limits, not evidence that the desktop methods have stopped working. Recheck them on their owner hosts before claiming current end-to-end coverage.
