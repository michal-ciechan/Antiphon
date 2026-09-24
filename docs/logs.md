# Log sources and retrieval

This is the authoritative operator inventory for logs and transcript evidence. Run the helper from a checkout with `pwsh -NoProfile -File scripts/logs.ps1 -Source <name> -Tail 100`. It reads only; it never reads environment variables, secret files, or provider credential stores. Logs can contain user/task content, so keep output local and do not paste it into public issues.

`C:\src\Antiphon` below is the canonical desktop checkout. A linked worktree is not the owner of the local stack. A missing file is evidence: report it with its source name, do not create an empty substitute.

## Desktop and AppHost

| Source | Path and retrieval | Rotation / retention | Current verification (2026-09-24) |
|---|---|---|---|
| `desktop-server` | Server Serilog files: `C:\src\Antiphon\server\logs\antiphon-*.log`; `scripts/logs.ps1 -Source desktop-server`. The configured relative `Serilog:LogPath=logs` resolves from the server content root. The helper resolves Git's primary worktree, never its caller's linked worktree, then selects the newest matching file. | Daily; 100 MB per file; time retention 5 days; 14-file disk backstop. | Retrieved from the newest server-content-root log. AppHost supervision does not by itself prove the server is accepting requests. |
| `apphost` | `C:\src\Antiphon\logs\apphost.log`; `scripts/logs.ps1 -Source apphost`. This is the parent Aspire/AppHost stdout/stderr, not the server Serilog file. The dashboard is `http://localhost:17205`; launch/restart ownership is recorded by `C:\src\Antiphon\logs\apphost.launch.lock` and `apphost.restart.lock`. | No application rotation policy; the restart/supervisor owns it. Never remove a lock to force a restart; use the runbook. | Retrieved; shows the AppHost listening on 17205. |
| `session-runner` | Supervised runner stdout/stderr: `C:\src\Antiphon\logs\session-runner.log`; the runner's own Serilog default is `%TEMP%\antiphon-logs\session-runner-*.log`. `scripts/logs.ps1 -Source session-runner` reads both when present. | Runner Serilog rolls daily, 50 MB/file, retains 14 files and 14 days. The supervisor file has no coded rotation. | Retrieved from the supervisor file. |
| `pty-host` | `C:\logs\antiphon\session-runner\pty-hosts\logs\*.log`; `scripts/logs.ps1 -Source pty-host`. Related runner state is under `C:\logs\antiphon\session-runner` (manifests, transcript sidecars and Herdr sidecars). | Runner cleanup removes pty-host logs and non-live transcript sidecars after 14 days. Optional raw PTY audits are `%TEMP%\antiphon-pty-audits`, disabled unless `ANTIPHON_PTY_AUDIT=1`, capped at 20 MB/session, two days and 50 directories by default. | Path was retrievable. |
| `fake-gateway` | Supervisor output: `C:\src\Antiphon\logs\fake-gateway.log`; delivery ledger: `C:\src\Antiphon\logs\fake-gateway\outbound.jsonl` unless `FakeGateway:DeliveryLog` overrides it. `scripts/logs.ps1 -Source fake-gateway` reads stdout/stderr. | No application rotation policy for either file. The JSONL is a local-dev/test assertion ledger, never production evidence. | Retrieved; current entries include inbound-unconsumed monitor failures. |

The desktop server was initially unavailable during this verification and later recovered enough to serve Hangfire. Do not restart it from a worktree; use [the AppHost runbook](apphost-runbook.md) from the canonical checkout.

## Hangfire jobs and failures

Hangfire is in-process and uses `Hangfire.InMemory`. Open the local-only dashboard at `http://localhost:17202/hangfire` (or run `scripts/logs.ps1 -Source hangfire`) and inspect **Failed** and **Recurring Jobs**. Job/history expiration is eight days (`Hangfire:HistoryRetentionDays`); a server restart loses it immediately. There is no durable Hangfire file log or supported API export. Correlate a job failure with `desktop-server` while it exists.

Current verification retrieved the dashboard (HTTP 200). Proposed durability fix: use durable Hangfire storage or export failure summaries if history must survive server restarts.

## Agent transcripts

The server-normalized transcript is the delivery evidence. Obtain a live session id from `GET /api/agents`, then retrieve `GET /api/sessions/{id}/transcript?since=0`; `scripts/logs.ps1 -Source transcripts` does this for live agent sessions using the task token when supplied. Use `GET http://localhost:17204/sessions/{id}/transcript` only for runner-local diagnosis, not as the authoritative historic record.

Database retention is 30 days for normalized transcript rows (`Retention:TranscriptRetentionDays`). Runner sidecars in `C:\logs\antiphon\session-runner\transcripts\<sessionId>.json` are restart/adoption state, not the complete provider transcript, and non-live files are pruned after 14 days. Native provider stores are diagnostic-only and can contain unrelated conversation content: Claude is `${CLAUDE_CONFIG_DIR:-%USERPROFILE%\\.claude}\\projects\\<encoded-cwd>\\*.jsonl`; Codex is `${CODEX_HOME:-%USERPROFILE%\\.codex}\\sessions\\yyyy\\MM\\dd\\rollout-*.jsonl`; Grok is `${GROK_HOME:-%USERPROFILE%\\.grok}\\sessions\\<url-encoded-cwd>\\<session-id>\\updates.jsonl`. Read only the known session's file and never open `auth.json` or credential files.

Current verification reached the server API, but the first helper attempt exposed an array-enumeration defect in the helper and was corrected. Re-run `scripts/logs.ps1 -Source transcripts` to record the current live-session result; a no-live-session response is not a transcript failure.

## server2 runner and deployment evidence

The runner container is `antiphon-runner-session-runner-1`. Retrieve its stdout/stderr plus its persisted runner and nested-Docker logs with `scripts/logs.ps1 -Source server2-runner` (equivalent: `ssh mc@server2 "docker logs --tail 100 antiphon-runner-session-runner-1; docker exec antiphon-runner-session-runner-1 sh -lc 'tail -n 100 /state/runner-logs/session-runner-*.log /state/logs/dockerd.log'"`). The runner Serilog files are `/state/runner-logs/session-runner-YYYYMMDD.log`; nested Docker is `/state/logs/dockerd.log`; pty-host state is `/state/pty-hosts` and session sidecars/transcripts are under `/state/session-runner`. Do not inspect `/run/secrets`, `/run/antiphon`, or provider state/auth files. Docker's configured logging driver/rotation is host policy and has not been established by this repository.

Verified: SSH and the named container were reachable and running; the retrieved tail contained a phone-home reconnect after HTTP 502. That reconnect issue is now fixed by CARD-0655.

There is currently **no retrievable durable server2 deployment-evidence source**: neither a deployment manifest nor `run.log` exists under `/home/mc/antiphon-server2` (only `secrets/` exists, which is deliberately excluded). `scripts/logs.ps1 -Source server2-deploy` inventories that root without touching `secrets/` and deliberately fails to make this gap visible. Proposed fix: have the server2 deploy owner write a non-secret manifest and `run.log` (deployment SHA, image digests, compose/config digest, timestamps, health/version probes, and rollback identity) to `/home/mc/antiphon-server2/evidence/`, with an explicit retention policy, then add its exact tail command here.

## Windmill runs

Windmill is the evidence source for scheduled nightly and tracker jobs, not a desktop log file. Open the job in Windmill and use its run-log view; the configured nightly script is `u/lndcobra/antiphon_nightly_tests` and writes durable local run evidence below `C:\Antiphon\nightly\logs\<yyyy-MM-dd-HHmm>-<runId>\` (`build.log`, suite logs and `summary.json`). `scripts/nightly-run.ps1` owns the layout and no local Scheduled Task is supported. This task did not use a Windmill credential or query an unauthenticated endpoint, so live Windmill retrieval remains an access-dependent gap rather than a claimed check.

## Verification checklist

Run each helper source once after a stack change. `desktop-server`, `hangfire`, and `transcripts` must succeed before claiming a desktop incident is fully evidenced; `server2-deploy` is expected to fail until its named gap is fixed. The source-specific helper commands above are also the canonical commands to put in incident notes.
