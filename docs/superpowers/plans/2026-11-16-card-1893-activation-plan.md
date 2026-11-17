# CARD-1893 activation plan: durable server2 host-load telemetry end to end

Plan task `eb85f57b`, 2026-11-16; amended by plan task `a1d414b6`, 2026-11-17, after round-1 Review `ee9cdb64` (Grok, BLOCKER, `docs/superpowers/reviews/2026-11-16-card-1893-activation-plan-review.md` at `11fa40e13b766d8255634339f385cef0d3789361`). This is a plan only: no code, no setting change, no restart and no deployment was made.
Master re-read for this revision: `870a93d21f2b18c85a8b0905ab689b28e3099813` (`origin/master` at 2026-11-17, unchanged since the review). Every `file:line` below is at that SHA unless another ref is named. `rc/2026-11-24` is the same commit (`870a93d21`). The original revision read `61259342c25c9960c12ac7171488e52b61dc03be`.
Inputs: CARD-1893 and its history; `docs/investigations/2026-11-16-card-1893-telemetry.md` (`origin/feat/card-task-705f622b`, tip `ce399e772010ab89edc018fb133376badb613eb5`); `docs/investigations/2026-11-16-card-1893-binding-and-broker.md` (`740ec396b06da7d427095d03d03913dccb5b3aa6`); the collector plan `docs/superpowers/plans/2026-11-16-card-1893-collector-plan.md`. "Merge 253" (the collector hardening) is on master as `9cf3e491705247bc03814e6afed77a6534805dfd` (`fix: harden collector paths and expose bounded diagnostics`, a cherry-pick of `ccd0d185d`). The id `0f1f7bf48` named in the first revision is not an ancestor of master and is no longer used.

Roles: **ORCHESTRATOR** is the desktop orchestrator (desktop shell, `ANTIPHON_API` reads, desktop Postgres, the AppHost restart, the server2 rolling phases under operational autonomy). **OPERATOR-ONLY** is ssh to the server2 host as the operator account (`/home/mc/antiphon-server2`). **NEEDS-ROLLOUT** takes effect only through an rc and the server2 rolling phases.

## Revision (round 1 → round 2)

| # | Review finding | What changed | Why |
|---|---|---|---|
| R-1 | Blocker: Merge 253 is already on master and `rc/2026-11-24` (= `870a93d21`) carries it without `HistoryPath`; nothing stops that runner image rolling to server2 | New §7 **step 0, SERVER2 RUNNER HOLD**, with a mechanical release check (§7.0) and its cost; G-7 rewritten; G-15/G-16 added; D-8 rewritten; D-10 added; old step 2 ("land Merge 253") is now done and recorded as `9cf3e4917` | Without a configured path the rc-24 collector opens a container-layer spool and pins the server cursor to it (H-1). The hold must exist before anyone runs `deploy-temp`/`redeploy-old` of that rc |
| R-2 | Blocker: Q-6 deletes the `server2` cursor row while the old runner still serves history; the pump re-inserts it within ~5 s, and the new store id does not exist yet | Q-6 replaced by the **pump-paused reset window** (D-11, §7 step 5P/6P): pause ingestion with `HostLoadHistory:Enabled=false`, prove no commit, roll the S1 runner, delete only after the durable store is the one being served and before the pump resumes | The pump is the only cursor writer (`HostLoadIngestPump.cs:55`, `HostLoadIngestService.cs:60-64`) and stops when `Enabled` is false (`HostLoadIngestPump.cs:62`), so the window is deterministic instead of a race |
| R-3 | Advisory/dispatch: S1 and S2 share `docs/docker-stack.md` with CARD-1957 Code `c6c85b5b` and Docs `d336fcc3` | New §6.1 **dispatch ordering** and a shared-file table; S2's file list now also names `scripts/c590-real.ps1` and `scripts/verify-docker-stack.ps1` (a new host case needs both), and `scripts/c590-real.ps1` is a CARD-1957 file | A same-file Code task in flight defers (AGENTS.md cards rule) |
| R-4 | Keep: H-2, `--no-recreate`, idle-gated `upgrade-broker` outside `Phase=all`, binding values, one current row, red-capable witnesses, S1/S2 dispatch shape | Kept. The broker identity environment moves from S1 to S2 (D-9) so that S1 changes only the `session-runner` service and S2 owns every `build-slots` change together with `--no-recreate` | S1 alone releases the hold; it cannot trigger H-2 because it leaves the `build-slots` service config unchanged |
| R-5 | Advisory: step 2's literal ancestor command is red | Uses `9cf3e4917` | Operator would stop on the old id |
| R-6 | Advisory: `own_tree` chowns, does not chmod | S1 adds an explicit `chmod 0700` (`docker/stack/init-state.sh:77-107`) | Mode 0700 is otherwise not applied |
| R-7 | Advisory: `Host load ingest failed` is logged for every consume exception | Step 6 stops on the E-4 metadata witness; the log line is corroboration only (the warning carries no exception detail, `HostLoadIngestPump.cs:86`) | Avoids a false stop and a false pass |
| R-8 | Advisory: `upgrade-broker` must not treat `unknown` as idle; not in `Phase=all`; in-container probe | Written into D-6 step 1 and S2's test list | The census helper turns a failed GET into `unknown` (`scripts/c590-remote.sh:2771`) |
| R-9 | Advisory: CP-3 is unmeasured | Kept as a TestDesign obligation; rows split per slice (§6.3) | Unit-lane row deadline |
| R-10 | (found in re-read) every cursor row's gaps make every task-load answer incomplete | New G-13; E-4 projects `gaps_present`; §9 item 4 names it | `HostLoadHistoryService.cs:156-157` |

## 1. Outcome in one paragraph

Merge 253 is already on master and in `rc/2026-11-24`, so the first action is a hold, not a land: no server2 runner phase may roll an image that contains `9cf3e4917` while the compose that phase applies leaves `HistoryPath` unset (step 0). The hold is released by S1, a compose-only slice that gives the standing runner `/state/host-load` and its binding identity and turns history off on `server2-temp`. Before an S1 runner rolls, the desktop server gets exactly one primary `server2` binding with the same identity (step 5). If the hold was breached and a `server2` cursor row already exists, it is reset only inside a pump-paused window (D-11). S2 then moves the shared `build-slots` broker to a GET-capable image with its own identity, through a guarded `upgrade-broker` phase that waits for an idle broker; S2's `--no-recreate` keeps ordinary rollout phases from recreating the broker (H-2). Hazards: (H-1) the server's per-runner ingest cursor pins the first spool `RunnerStoreId` it sees, forever; (H-2) a changed `build-slots` service config would be recreated by `ensure_build_slots_broker` mid-rollout with no idle check; (H-3, new) one cursor row with recorded gaps makes every task-load answer incomplete.

## 2. Ground truth: what the card and brief assume versus what the code does

| # | Assumption | Code / evidence at `870a93d21` | Consequence |
|---|---|---|---|
| G-1 | Bindings may hot-reload | Every consumer takes `IOptions<HostLoadHistorySettings>` (`HostLoadIngestService.cs:19`, `HostLoadHistoryService.cs:21`, `HostLoadIngestPump.cs:12`, `HostLoadTaskWindowObserver.cs:15`); `ValidateOnStart` (`HostLoadRegistration.cs:12`). Runner `HostStatsSettings` is read once (`HostLoadCaptureService.cs:87`) | **No hot reload.** Server changes need an AppHost restart; runner and broker changes need the container recreated |
| G-2 | A config route shows the effective Bindings | No settings route for `HostLoadHistory` (`docs/ops-http.md`, `docs/antiphon-api.md`). `GET /api/hosts/{id}/load` reports `Binding(runner, now)?.PhysicalHostKey`; `Binding` is non-null only when exactly one row covers `now` (`HostLoadHistoryService.cs:175-179`) | Rows are read from configuration sources (E-1). After the change a non-null `physicalHostKey` witnesses exactly one current binding |
| G-3 | Production server location | Topology A: the desktop AppHost is production until CARD-1744. The server's user-secrets id is `antiphon-server` (`server/Antiphon.Server.csproj`) | Sources: `server/appsettings.json` (no `HostLoadHistory`), gitignored `server/appsettings.Development.json`, user-secrets, environment, command line. Arrays merge by index across sources |
| G-4 | Physical key, revision and epoch derive from code | Opaque deployment settings (`HostLoadHistorySettings.cs:24-33`). Validation: key 1..128, `SourceEpoch != Guid.Empty`, `Revision >= 1`, `To > From` (`:18-21`) | Values are **chosen** (§5) |
| G-5 | Ingest matches on runner and time | Matches runner, `[From,To)`, key, `Revision == sample.BindingRevision`, epoch; `IsPrimary` needs exactly one primary row (`HostLoadIngestService.cs:76-85`) | Collector and server values must be byte-identical. A sample written before the binding exists is unbound forever |
| G-6 | Unknown cursor state is harmless | A batch whose `RunnerStoreId` differs from the row throws `host_load_identity_conflict` (`HostLoadIngestService.cs:40`). The row is created by the first page, even one with no samples (`:60-64`). The pump logs and retries forever (`HostLoadIngestPump.cs:83-86`). Only the pump calls `IngestAsync` (`HostLoadIngestPump.cs:55`; no other caller in `server/`). No route resets the row | **H-1** |
| G-7 | Merge 253 is safe to roll without a path | On master the collector resolves an unset path to LocalApplicationData, then `$HOME/.local/share/antiphon/host-load` (`HostLoadStoragePath.cs:7-18`, `HostLoadCaptureService.cs:99-108`). In the container `HOME=/home/app` (`docker/session-runner-grok/dind-entrypoint.sh:228`), which is the container layer: the only durable mounts under it are `.nuget/packages` and `.npm/_cacache` (`docker-compose.server2-runner.yml:114-127`). The spool writes `cursor.json` with a new store id before any sample (`HostLoadSpool.cs:52-62`) and serves a page carrying that id even when empty (`:154-157`); the pump enqueues every non-null page (`HostLoadIngestPump.cs:43-45`) | **The rc-24 runner image, rolled with the rc-24 compose, pins `server2` to a store the next recreate deletes.** Step 0 holds it |
| G-8 | Upgrading the broker image is enough | `GET /build-slots/host-load` is `Unknown` unless the broker's `PhysicalHostKey` and `AuthorityId` are both set (`BuildSlotRoutes.cs:73-75`). The checked-in `build-slots` sets neither (`docker-compose.server2-runner.yml:169-190`). The broker branch registers no host-stats capture, so the broker opens no spool (`src/Antiphon.SessionRunner/Program.cs:45-57`) | The broker needs a new image and identity environment (S2) |
| G-9 | A compose edit to `build-slots` waits for the operator | `ensure_build_slots_broker` runs `compose_host --profile broker up -d --no-build build-slots` (`scripts/c590-remote.sh:1012-1030`), called from `case_deploy_parent` (`:9735`) and `case_deploy_temp_runner` (`:9986`) | **H-2.** S2 changes this |
| G-10 | The broker pin is known | `broker_sha12()` reads `BUILD_SLOTS_SHA12` from `$SERVER2_ROOT/secrets/stack.env` (`c590-remote.sh:979-983`); written only when absent (`:1017-1019`) | UNOBSERVED (E-6, OPERATOR-ONLY) |
| G-11 | A lost lease kills builds | A renew 404 stops the renewer (`scripts/lib/build-slot.ps1:116`); the driver keeps running | Over-subscription window, no failed build |
| G-12 | Temp and standing runners share a spool | Separate compose projects and `runner-state` volumes; temp is `server2-temp` and sets only `PhoneHome__RunnerId` and the broker URL (`docker-compose.server2-runner.temp.yml:4-7`) | Temp needs its own opt-out (D-4) |
| G-13 | Gaps on another runner do not matter | `TaskAsync` sets `durableLoss` if **any** cursor row has non-empty `GapsJson` (`HostLoadHistoryService.cs:156-157`), and then every task-load answer is `complete=false` | **H-3.** A stale gapped row (a breached `server2-temp` or desktop runner row) fails §9 item 4 for every task |
| G-14 | Runners before rc-24 ingest | At `rc/2026-11-23` the collector returns when `HistoryPath is null` (`HostLoadCaptureService.cs:60` on that ref); the history route answers 503 (`HostStatsRoutes.cs:88` on that ref) and the pump records `history_read_unavailable` (`HostLoadIngestPump.cs:74`) | A `server2` cursor row can exist only if an image containing `9cf3e4917` already ran on server2 (E-4 decides) |
| G-15 | A hand-set variable or `stack.env` line fixes a live runner | The phases run `docker compose` with `-f ${C1008_COMPOSE_DIR:-$CHECKOUT}/docker-compose.server2-runner.yml` (`c590-remote.sh:953-978`) from the checkout at the rollout SHA; `compose_host` passes no `--env-file`. `compose_temp` passes `--env-file "$SERVER2_TEMP_ENV"` (`:985-1010`), which feeds interpolation only, not container environment | The fix must be a literal in the compose files at the rolled SHA |
| G-16 | Holding the rc costs runner features | `git diff rc/2026-11-23 rc/2026-11-24` under `src/Antiphon.SessionRunner*`, `docker/` and `docker-compose.server2-runner*.yml` is exactly `9cf3e4917` (collector files only) | The hold defers no other runner change in rc-24. Its cost is schedule, §7.0 |
| G-17 | The desktop opens the server2 spool | An AppHost restart restarts the desktop server and desktop runner only; the server2 runner container is not touched. The desktop runner has no `HostStats:HistoryPath` (`src/Antiphon.SessionRunner/appsettings.json:5-10`; none in `Antiphon.AppHost/`) | **Verified: a desktop restart onto rc-24 does not open the server2 spool.** It does open a desktop-runner spool under that Windows account's `%LOCALAPPDATA%\antiphon\host-load`, which survives restarts, so its pin is stable (not H-1). Its row is visible in E-4 and counts for H-3 |

## 3. Evidence to gather first (read-only)

Never print a secret, a token, the opaque ingest cursor, a ledger secret or a whole configuration file. Project named non-secret fields only. Record the UTC time of each read.

| ID | Who | What | Exact read |
|---|---|---|---|
| E-0 | ORCHESTRATOR | **Hold status**: what server2 runs now, and whether a rollout of rc-24 has already begun | `GET /api/session-runners/server2/status` and `GET /api/session-runners/server2-temp/status` (`buildVersion`); compare each SHA12 with `git merge-base --is-ancestor 9cf3e4917 <sha>`. Any `true` means the hold was breached: go to E-4 and D-11 |
| E-1 | ORCHESTRATOR (desktop shell, main checkout) | Effective `HostLoadHistory` from every desktop server source | `pwsh -NoProfile -Command "$p='server/appsettings.json','server/appsettings.Development.json'; foreach($f in $p){ if(Test-Path $f){ $j=Get-Content -Raw $f \| ConvertFrom-Json; '{0}: {1}' -f $f, ($j.HostLoadHistory \| ConvertTo-Json -Depth 5 -Compress) } else { '{0}: absent' -f $f } }; $s=Join-Path $env:APPDATA 'Microsoft\UserSecrets\antiphon-server\secrets.json'; if(Test-Path $s){ $o=Get-Content -Raw $s \| ConvertFrom-Json; $o.PSObject.Properties \| ? Name -like 'HostLoadHistory*' \| % { '{0}={1}' -f $_.Name,$_.Value } } else { 'user-secrets: absent' }; foreach($t in 'Machine','User','Process'){ [Environment]::GetEnvironmentVariables($t).Keys \| ? { $_ -like 'HostLoadHistory__*' } \| % { '{0} env {1}' -f $t,$_ } }"`; and `Select-String -Path Antiphon.AppHost/Program.cs -Pattern 'HostLoadHistory'` (expected none). Classify absent, expired, overlapping or other |
| E-2 | ORCHESTRATOR | Server binding resolution | `GET /api/hosts/server2/load` (`physicalHostKey`); `GET /api/hosts/server2/load/history?from=<now-10m>&to=<now-5m>&resolution=raw&limit=60` (`gaps`) |
| E-3 | ORCHESTRATOR | Running server build | `GET /api/version` (SHA, `land-v2`) |
| E-4 | ORCHESTRATOR (desktop Postgres `localhost:17280`, connection string never printed) | **Every** ingest cursor row (H-1 and H-3 gate) | `SELECT "RunnerId","RunnerStoreId","CommittedAt","Complete",("Cursor" IS NOT NULL) AS cursor_present,("GapsJson" NOT IN ('[]','')) AS gaps_present FROM "HostLoadIngestCursors" ORDER BY 1;` Never select `"Cursor"` |
| E-5 | ORCHESTRATOR | Existing samples by binding | `SELECT "RunnerId","PhysicalHostKey","SourceEpoch","IsPrimary",count(*),max("SampledAt"),max("IngestedAt") FROM "HostLoadSamples" WHERE "RunnerId" IN ('server2','server2-temp') GROUP BY 1,2,3,4;` |
| E-6 | OPERATOR-ONLY | Broker pin, image, identity | `grep -E '^BUILD_SLOTS_SHA12=' /home/mc/antiphon-server2/secrets/stack.env`; `docker ps --filter label=com.docker.compose.project=antiphon-runner --filter label=com.docker.compose.service=build-slots --format '{{.Names}} {{.Image}} {{.CreatedAt}}'`; `docker image inspect --format '{{.Id}} {{index .Config.Labels "org.opencontainers.image.revision"}}' <image>`; `docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' <broker> \| grep -E '^SessionRunner__(HostStats\|BuildSlots)__'` |
| E-7 | ORCHESTRATOR (server2 task container on `antiphon-build-slots`) or OPERATOR-ONLY | Broker routes and occupancy | `curl -sS -o /dev/null -w '%{http_code} %header{allow}\n' http://build-slots:8080/build-slots/host-load`; the same for `/build-slots/metrics`; `curl -fsS http://build-slots:8080/build-slots` projected with node to `budget`, `occupied`, `waiters.length`, `enabled` |
| E-8 | ORCHESTRATOR (server2 task) | Standing collector | `curl -sS http://localhost:8080/host-stats/history?limit=1` status and `source.runnerStoreId`; the `collector` object; `curl -fsS http://localhost:8080/capabilities` (`version`) |
| E-9 | OPERATOR-ONLY | Image for the swap | `docker image ls antiphon-server2/session-testing --format '{{.Tag}} {{.ID}}'` |

Stop conditions after evidence:
- **E-0 shows a `9cf3e4917` image on `server2`, or E-4 has a `server2` row:** the hold was breached; activation follows D-11 (Q-6). Do not roll S1 without it.
- **E-4 has a `server2-temp` row or any row with `gaps_present`:** record it. It is harmless to ingestion after S1 (temp history is off) but fails §9 item 4 (H-3); clear it in the D-11 window (Q-6b).
- **E-1 shows Bindings in more than one source**, or values that differ from §5: resolve to one source first (Q-1).
- **E-7 already returns 200 for `host-load`:** skip the image part of the broker swap; the S2 identity environment is still needed.

## 4. Decisions

Defaults hold unless the operator answers otherwise (§8).

- **D-1 (server binding source).** Desktop user-secrets `antiphon-server` (`docs/agent-credentials.md`, `docs/bootstrap.md`). Rejected: tracked `server/appsettings.json` (binds every dev/E2E/test host to production identity); gitignored `appsettings.Development.json` (no CLI, less discoverable); AppHost `WithEnvironment` (code change for operator data).
- **D-2 (collector identity in compose).** Literal non-secret values in `docker-compose.server2-runner.yml`. Rejected: interpolation from `stack.env`; `compose_host` passes no `--env-file` (G-15), and an unset `${X:-}` binds `""`, which defeats the `is null` checks (`HostAdmissionSnapshotClient.cs:29`, `:63`, `HostLoadProjection.cs:36`).
- **D-3 (values).** `PhysicalHostKey=server2`. `SourceEpoch` is one new GUID generated once by the S1 Code task; the landed compose literal is the source of truth. `BindingRevision=1`, or `max+1` when E-1/E-5 show rows for the same key and epoch. `AuthorityId=server2-build-slots` (it is `HostAdmissionSnapshot.Id`, `HostLoadContracts.cs:20-21`, not a server binding field). One binding row, index 0, `Primary=true`, `From=T0` (the write instant), no `To`.
- **D-4 (temp runner).** `docker-compose.server2-runner.temp.yml` sets `SessionRunner__HostStats__HistoryEnabled: "false"`; no `server2-temp` binding. With it the collector returns before opening a spool (`HostLoadCaptureService.cs:87`), the route answers 404 (`HostStatsRoutes.cs:84`) and `HostLoadHistoryV1` is not advertised (`:131-133`), so the pump never creates a temp row. Rejected: a temp binding with `Primary=false` (still H-1 on every temp lifecycle).
- **D-5 (durable path).** `SessionRunner__HostStats__HistoryPath: /state/host-load` on the standing `session-runner`. `/state` is `antiphon-runner_runner-state`, preserved across redeploys. S1 adds `/runner-state/host-load` to `docker/stack/init-state.sh` and an explicit `chmod 0700` on it, because `own_tree` chowns only (`init-state.sh:77-107`).
- **D-6 (broker recreation, H-2).** S2 changes `ensure_build_slots_broker` to `up -d --no-build --no-recreate build-slots`, which still creates an absent broker. A new guarded phase `upgrade-broker` in `scripts/deploy-server2.ps1` (ValidateSet at `:15`), **not** in the `Phase=all` list (`:1547`), is the only path that recreates it:
  1. Idle gate: in-container `docker exec <broker> curl -fsS http://127.0.0.1:8080/build-slots` (the broker publishes no port) shows `occupied=0` and an empty `waiters`. A failed GET, unparsable body or `unknown` is **busy**, never idle. Poll every 5 s for up to 30 min; re-check immediately before recreating; otherwise exit `BrokerUpgradeBusy` with nothing changed.
  2. Tag the current image `antiphon-server2/broker-rollback:<old sha12>` (outside `retire_superseded_server2_images`, `c590-remote.sh:1082-1083`).
  3. Rewrite only the `BUILD_SLOTS_SHA12=` line to the rollout SHA12.
  4. `--force-recreate --no-build build-slots`; wait for health.
  5. Verify `GET /build-slots/host-load` is 200, `state=known`, key `server2`, `id=server2-build-slots`, plus a lease smoke (POST, renew, DELETE).
  6. On any failure restore the old pin line and recreate from the alias, at an idle instant.
  Rejected: hand-editing `stack.env` before `redeploy-old`; a manual `docker compose up`; a client fallback to the local count (`docs/ops-http.md` contract); an explicit `AuthorityEndpoint` (cannot add a missing route).
- **D-7 (broker image).** The same rc SHA12 the runner rolls; it contains `BuildSlotRoutes.cs:69-80`.
- **D-8 (order).** Hold first (step 0). The server binding is effective before the first S1 runner page is ingested. If a `server2` row exists, the reset happens in the pump-paused window (D-11). The broker swap comes after the primary witness so each fault's acceptance stays attributable; the broker must be known only before the final measurement.
- **D-9 (slice split, new).** S1 changes only the `session-runner` service, the temp override, `init-state.sh` and docs. All `build-slots` environment (key, revision, authority id) moves to S2 together with `--no-recreate`. Reason: S1 is then the only change that releases the hold, and it cannot trigger H-2, because Compose hashes each service's own config and S1 leaves `build-slots` (`docker-compose.server2-runner.yml:169-190`, no shared anchor with `session-runner`) byte-identical. A runner whose `AuthorityId` is set while the broker is still `Unknown` appends samples with `authorityUnknown` counted (`HostLoadCaptureService.cs` authority stage); nothing is dropped. Rejected: one combined slice (couples the hold release to the broker swap and to CARD-1957's `c590-real.ps1`).
- **D-10 (the hold, new).** Step 0. The hold is a rule on the orchestrator's own server2 rolling phases, mechanically checked (§7.0), and released only by a rolled SHA whose compose files carry D-4 and D-5. Rejected: a hand-set variable on the live container (lost at the next `compose up`) and a `stack.env` line (not applied, G-15). Fallback S0 only if Q-8 says so: `SessionRunner__HostStats__HistoryEnabled: "false"` on the base `session-runner`, which the temp inherits; it opens no spool anywhere.
- **D-11 (cursor reset window, new; replaces old Q-6).** Only when E-4 shows a `server2` row. Pause ingestion by writing `HostLoadHistory:Enabled=false` with the binding (step 5P) and restarting the AppHost: the pump and derived worker return at start (`HostLoadIngestPump.cs:62`, `HostLoadDerivedService.cs:127`), and nothing else writes the cursor table (G-6). Prove the pause, roll the S1 runner, then delete the one row only after the old runner's store is gone and the durable store is the one served, and before the pump resumes (step 6P). Keep sample and identity rows. Rejected: deleting while any runner serves the pinned store (re-inserted within one 5 s poll); deleting before the rollout without a pause (the new store id cannot be compared before the new spool opens).

## 5. Where each value comes from

| Setting | Runner (S1, `session-runner`) | Broker (S2, `build-slots`) | Server (D-1 user-secrets) | Source of truth |
|---|---|---|---|---|
| History path | `SessionRunner__HostStats__HistoryPath: /state/host-load` | n/a | n/a | D-5 |
| Temp history | temp override `SessionRunner__HostStats__HistoryEnabled: "false"` | n/a | no temp row | D-4 |
| Physical key | `SessionRunner__HostStats__PhysicalHostKey: server2` | same | `HostLoadHistory:Bindings:0:PhysicalHostKey=server2` | D-3 |
| Revision | `SessionRunner__HostStats__BindingRevision: "1"` | same | `…:0:Revision=1` | D-3, E-1/E-5 |
| Source epoch | `SessionRunner__HostStats__SourceEpoch: "<GUID from S1>"` | n/a | `…:0:SourceEpoch=<same GUID>` | landed S1 compose |
| Authority ID | `SessionRunner__HostStats__AuthorityId: server2-build-slots` | same | n/a | D-3 |
| Runner ID | existing `PhoneHome__RunnerId: server2` | n/a | `…:0:RunnerId=server2` | existing |
| Primary / From / To | n/a | n/a | `…:0:Primary=true`, `…:0:From=<T0 ISO-8601 Z>`, no `To` | D-3 |
| Pump pause (D-11 only) | n/a | n/a | `HostLoadHistory:Enabled=false`, removed at step 6P | D-11 |

Server write (ORCHESTRATOR, desktop): `dotnet user-secrets set "HostLoadHistory:Bindings:0:<Field>" "<value>" --id antiphon-server` per field; read back with the E-1 projection only.

## 6. Code slices (separate TestDesign, Code and Review; not done here)

**S1: runner durable path, identity, temp opt-out (releases the hold).** Files:
- `docker-compose.server2-runner.yml`: `session-runner` environment per §5 only. `build-slots` unchanged.
- `docker-compose.server2-runner.temp.yml`: `HistoryEnabled` false.
- `docker/stack/init-state.sh`: `/runner-state/host-load` plus `chmod 0700`.
- `docs/docker-stack.md`: server2 runner environment paragraph and the step-0 hold rule.
- `docs/ops-http.md` host-load section: binding values and the D-4 temp rule.

Tests: extend `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` (runner environment pins); new class `C1893ActivationComposeTests`: standing runner has the absolute path `/state/host-load`; key, revision and authority are literals (no `${`); epoch is a non-empty GUID; temp sets `HistoryEnabled` `"false"`; `build-slots` environment is byte-identical to `870a93d21` (the D-9 guard); `init-state.sh` creates and chmods the directory. `DockerStackDocumentationTests` reads `docs/docker-stack.md` and runs in the green row.

**S2: broker identity and guarded broker swap (H-2).** Files:
- `docker-compose.server2-runner.yml`: `build-slots` environment `PhysicalHostKey`, `BindingRevision`, `AuthorityId` per §5.
- `scripts/c590-remote.sh`: `ensure_build_slots_broker` with `--no-recreate`; new `case_upgrade_broker` and its dispatch line (case table at `:10704-10741`).
- `scripts/deploy-server2.ps1`: `upgrade-broker` ValidateSet value and dispatch, outside the `all` list.
- `scripts/c590-real.ps1`: `upgrade-broker` in `$script:C590LiveCases` (`:11-30`) and in the private-bundle list (`:793`).
- `scripts/verify-docker-stack.ps1`: the case's stub boundary.
- `scripts/test-deploy-server2.ps1` and `scripts/fixtures/c727-fake-verify.ps1` where they enumerate phases or cases.
- `docs/docker-stack.md` rollout phases; `docs/testing-and-build.md:1341` ("needs an operator to move the pin" becomes the phase).

Tests: `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` (ValidateSet parse; `ensure_build_slots_broker` stubs); new class `C1893BrokerUpgradeScriptTests`: refusal while occupied, while waiting and on a failed/`unknown` GET; re-check race; only the pin line changes; rollback alias; restore on failed verify; `ensure_build_slots_broker` never recreates; `upgrade-broker` absent from the `all` list; broker and runner identity literals equal.

**Existing tests affected.** Direct pins: `DockerStackContractTests`, `DockerStackDocumentationTests`, `RemoteScriptContractTests` (two classes share this name; the filter covers both). Derived readers from `tests/derived-impact-index.json` at `870a93d21` (fixtures copy these files; run at Merge through the CARD-1782 fixed subset, not per slice): for the compose files `C0824BranchDeletionRecycleAuditTests`, `C1008HostFixtureSetupInsertionTests`, `C1008PlatformContractTests`, `C1364BoundLandWaitProofTests`, `CheckpointImportTests`, `CodexRunnerImageContractTests`, `CustodyNamespaceBootstrapTests`, `DindRunnerContractTests`, `HostJqPrerequisiteScriptTests`, `HostStatsProbeParseTests`, `RetiredTempContainer{Host,Script}Tests`, `RollingProductionMountTests`, `RollingVolumeRecycle{CensusChurn,Script}Tests`, `Server2StagingServerContractTests`, `VerifyDindRunnerScriptTests`, `VolumeRetirement{ApplyGate,Content,Receipt,Wrapper}Tests`; S2 adds `ClaudeTokenRefreshOptInTests`, `HostJqFixtureClosureTests`, `SeedSmokeTargetingPackContractTests`, `Server2ProductionServerContractTests`, `VolumeRetirement{HostProof,Publication}Tests`. No migrations, DTOs, routes, operation labels or statement budgets. `scripts/impacted-classes.ps1 -Path …` returned empty `kinds` for these paths, so the impacted-classes table (CARD-1782) has no row; the derived list above is the authority.

### 6.1 Dispatch ordering and shared files (R-3)

| Shared file | Other owner in flight | S1 | S2 |
|---|---|---|---|
| `docs/docker-stack.md` | Docs `d336fcc3` (`feat/card-task-d336fcc3`, tip `12105bfbc`, +183/−7); CARD-1957 Code `c6c85b5b` (card advisory 1) | yes | yes |
| `scripts/c590-real.ps1` | CARD-1957 Code `c6c85b5b` (card advisory 5, `remote-output.txt` receipt) | no | yes |
| `tests/Antiphon.Tests/Scripts/VolumeRetirementHostProofTests.cs` | CARD-1957 (`c6c85b5b`, two test commits so far) | no | no (derived reader only) |
| `scripts/c1609-host-proof.ps1` | CARD-1957 (card advisories 2-4) | no | no |
| `tests/derived-impact-index.json` | every batch regeneration | no | no (Merge regenerates) |

Rules:
1. TestDesign for S1 and S2 may run now; it edits only plan files.
2. **S1 Code dispatches after `d336fcc3` and CARD-1957 (`c6c85b5b`) have landed**, or while neither is in flight; otherwise defer (same-file rule).
3. **S2 Code dispatches after S1 has landed** (both edit `docs/docker-stack.md` and the base compose file) **and after CARD-1957 has landed** (`scripts/c590-real.ps1`). S2 is never in flight beside S1.
4. Land order: `d336fcc3` → CARD-1957 → S1 → S2. If CARD-1957 stalls, S1 may go first only with `docs/docker-stack.md` edits rebased by the land; S2 still waits.

### 6.2 Lane

server2 Linux: no `-Runner` (runner-defaults places on server2), no `-Platform`.

### 6.3 Checkpoints (provisional; TestDesign binds exact methods and measures)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 red | tests/Antiphon.Tests -> bin-c1893s1-red/ | s1-red | `/*/*/C1893ActivationComposeTests/*` | V-1 | intended assertion failures | 6 | 14 |
| CP-2 | S1 green | tests/Antiphon.Tests -> bin-c1893s1-green/ | s1 | `/*/*/(C1893ActivationComposeTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | V-1,R-1 | all listed, 0 failed | 142 | 16 |
| CP-3 | S2 red | tests/Antiphon.Tests -> bin-c1893s2-red/ | s2-red | `/*/*/C1893BrokerUpgradeScriptTests/*` | V-2 | intended assertion failures | 8 | 14 |
| CP-4 | S2 green | tests/Antiphon.Tests -> bin-c1893s2-green/ | s2 | `/*/*/(C1893BrokerUpgradeScriptTests*)\|(DockerStackContractTests*)\|(DockerStackDocumentationTests*)/*` | V-2,R-1 | all listed, 0 failed | 144 | 16 |
| CP-5 | S2 green | CP-4 | remote-scripts | `/*/*/RemoteScriptContractTests/*` | R-1 | all listed, 0 failed | 230 | 30 |

Suffix check at `870a93d21`: classes beginning `DockerStack` are `DockerStackContractTests`, `DockerStackDocumentationTests`, `DockerStackDocuments`, `DockerStackSmokeCommandTests`; the operands `DockerStackContractTests*` and `DockerStackDocumentationTests*` match only themselves. `C1893*` today is only `C1893CollectorDiagnosticsTests`; `C1893ActivationComposeTests` and `C1893BrokerUpgradeScriptTests` do not collide. `RemoteScriptContractTests` matches its two same-named classes, not `RemoteScriptContractSupport`. Min counts: `DockerStackContractTests` 117 and `DockerStackDocumentationTests` 19 `[Test]` methods, plus the new classes. CP-5 is unmeasured on loaded server2: TestDesign measures it or splits it before use (a row over the derived 30-minute deadline exits 5 with no TRX).

## 7. Sequence

Each step ends with a stop condition. If it fails, stop.

### 7.0 Step 0: SERVER2 RUNNER HOLD (ORCHESTRATOR; in force now)

**Rule.** Do not run `deploy-server2.ps1 -Phase deploy-temp`, `-Phase redeploy-old` or `-Phase all` with `-Sha X` when X contains `9cf3e4917` and the compose files at X do not carry D-4 and D-5. The standing runner stays on its current image (E-0). `drain-old`, `drain-temp`, `retire-temp`, `check-*` and `provision-host-jq` are not held: they start no runner from a new image. A `retire-temp` of an existing temp from an earlier rollout is not held.

**Mechanical check** (desktop main checkout, before every held phase; `HOLD` means do not run):
```
git fetch origin
X=<40-hex rollout sha>
if git merge-base --is-ancestor 9cf3e491705247bc03814e6afed77a6534805dfd "$X"; then
  git show "$X:docker-compose.server2-runner.yml" | grep -Eq '^ +SessionRunner__HostStats__HistoryPath: /state/host-load$' \
  && git show "$X:docker-compose.server2-runner.temp.yml" | grep -Eq '^ +SessionRunner__HostStats__HistoryEnabled: "false"$' \
  && echo RELEASED || echo HOLD
else echo NOT-AFFECTED; fi
```
With the S0 fallback (Q-8) the release instead requires `SessionRunner__HostStats__HistoryEnabled: "false"` in the base file. For `rc/2026-11-24` (`870a93d21`) the check prints `HOLD`.

**What happens on the desktop.** Nothing is held there. An AppHost restart onto rc-24 restarts the desktop server and desktop runner only (G-17); the old server2 runner keeps answering history with 503, so the pump creates no `server2` row (G-14). The desktop runner opens its own durable `%LOCALAPPDATA%` spool and gets its own cursor row, which is stable across restarts and is checked by E-4 for H-3.

**Cost.** The runner-side content of rc-24 is only `9cf3e4917` (G-16), so server2 loses no runner fix. What is delayed: the rc-24 server2 rolling rollout as a whole, and every later rc's server2 rollout, until an rc carries S1 (by a reviewed rc port of S1 onto the current rc line, Q-9, or the next cut). With S1 in Code after the §6.1 order, the expected delay is one TestDesign + Code + Review + land + rc port cycle. Dispatch to server2 continues on the current runner image (keep-7 is unaffected). If an unrelated urgent runner fix needs a server2 rollout first, Q-8's S0 stop-gap releases the hold without S1.

**Stop:** never "continue past" the hold. If E-0 already shows a `9cf3e4917` image on `server2` or `server2-temp`, the hold was breached before it was written: keep the current runner (do not roll again with the same compose), go to D-11.

### 7.1 Steps

1. **Evidence** (ORCHESTRATOR and OPERATOR-ONLY, §3). Stop if E-1 finds multiple or conflicting sources (Q-1) or E-6 shows an unexpected image or customized broker environment. E-0/E-4 select the branch: **normal** (no `server2` row) or **P** (row present).
2. **Merge 253**: done. `9cf3e4917` is on master and in `rc/2026-11-24`. Check: `git merge-base --is-ancestor 9cf3e4917 origin/master` exits 0.
3. **S1**: TestDesign, Code (after §6.1 rule 2), Review, land. Stop until landed with Review clean and CP-1/CP-2 green. Record the epoch GUID from the landed compose.
4. **rc carrying S1** (NEEDS-ROLLOUT): reviewed port onto the current rc line or the next cut (Q-9). Precondition: the §7.0 check on that rc SHA prints `RELEASED`; rc qualification reports no new red in the selected rows.
5. **Server binding** (ORCHESTRATOR, Q-1): write the §5 user-secrets with `From=T0` = now (UTC). **Branch P only (5P):** also write `HostLoadHistory:Enabled=false`. Run `pwsh -NoProfile -File scripts/restart-apphost.ps1` (check both locks first). Stop if:
   - exit is not 0, or `/api/version` is not the expected SHA;
   - `GET /api/hosts/server2/load` is not `physicalHostKey=server2`;
   - E-2 for a new window still reports `binding-unknown` (after the write an empty window is `["missing"]`, `HostLoadHistoryService.cs:114`);
   - **5P:** E-4's `server2` `CommittedAt` changes between two reads 30 s apart taken after the restart (the pump is not paused).
   Rollback: `ValidateOnStart` failure (`HostLoadHistory binding is invalid.`) → `dotnet user-secrets remove` each key and restart.
6. **server2 rolling rollout** of the step-4 rc (NEEDS-ROLLOUT; ORCHESTRATOR): `deploy-temp → drain-old → redeploy-old → drain-temp → retire-temp`, each held phase preceded by the §7.0 check. Before S2 is rolled, `ensure_build_slots_broker` does not recreate the broker because S1 leaves `build-slots` unchanged (D-9).
   - **Normal branch**, stop after `redeploy-old` if: runner `/host-stats/history?limit=1` is not 200; `collector.appended` is not rising or `dropped` rises; E-4 does not show one `server2` row whose `RunnerStoreId` equals the runner's `source.runnerStoreId` with `CommittedAt` advancing across two reads 60 s apart. A `Host load ingest failed` log line is corroboration only (R-7).
   - **6P (branch P, D-11):** after `redeploy-old` is healthy: (a) runner `/host-stats/history?limit=1` is 200 and its `source.runnerStoreId` (R_new) differs from E-4's row; (b) E-4's row `CommittedAt` is still frozen (pump paused); (c) OPERATOR-ONLY or ORCHESTRATOR DB: `DELETE FROM "HostLoadIngestCursors" WHERE "RunnerId"='server2' AND "RunnerStoreId"='<E-4 value>';` must report 1 row (Q-6a); a `server2-temp` row, or any other row with `gaps_present`, may be deleted the same way only when that runner's history route answers 404 or the runner no longer exists (Q-6b); (d) E-4 shows no `server2` row across two reads 30 s apart; (e) remove `HostLoadHistory:Enabled` from user-secrets and restart the AppHost; (f) E-4 shows one `server2` row with `RunnerStoreId = R_new` and advancing `CommittedAt`. Stop at the first failing check; until (e) nothing is ingested, and the durable spool retains the samples for replay.
   Rollback: standard rolling phases back to the previous image. The `/state/host-load` store and its cursor row stay and are reused; never delete the store directory.
7. **Primary witness** (ORCHESTRATOR). E-5 for samples after the rollout: `PhysicalHostKey=server2`, `SourceEpoch=<E>`, `IsPrimary=true`. History for a fresh 5-minute window returns items with `ioWaitPercent`, memory and volumes. Stop otherwise (§5 drift: new revision with `To`/`From` at one instant, restart; earlier samples stay unbound).
8. **S2**: TestDesign, Code (after §6.1 rule 3), Review, land, rc carrying S2 (the §7.0 check still applies). Roll it with the normal phases: `--no-recreate` keeps the live broker untouched even though `build-slots` config changed. Stop if E-7 shows the broker container `CreatedAt` changed during the rollout.
9. **Broker swap** (Q-4/Q-5; ORCHESTRATOR may run the phase): `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Phase upgrade-broker -Sha <rc sha>`. Stop if it exits `BrokerUpgradeBusy` (retry later, nothing changed), or E-7 `host-load` is not 200 with `state=known`, `physicalHostKey=server2`, `id=server2-build-slots`, `revision>=1`, `observedAt` within 15 s and counts equal to a same-second `GET /build-slots`, or the lease smoke fails. Rollback: the phase restores the old pin line and recreates from the alias at an idle instant.
10. **Done measurement** (§9) under keep-7 load. Stop and file a Debug task on any miss.
11. **Close out.** Record the window on CARD-1893. File: the cursor store-rotation defect (H-1/G-6, so a future store change does not need D-11), the all-cursor `durableLoss` scope (H-3/G-13), and CARD-1744 topology B carrying `HostLoadHistory__Bindings__0__*` in `docker-compose.server2-server.yml`.

**Blast radius (step 9).** The broker leases every server2 build and test driver; its state is in memory. At an idle instant nothing is lost. If an acquire races the recreate, that driver's renew gets 404 and it continues unleased (G-11); waiters lose FIFO position. Recreate takes about 30 s; health is checked every 10 s. During it `build-slot.ps1` waits 60 s for an unreachable broker, then runs unleased at `-maxcpucount:4` (`scripts/build-slot.ps1:18-19`). Quiesce: do not pause dispatch; rely on the idle gate; no qualification or Full lane in flight; check `GET /build-slots` `leases[].holder` first.

**Blast radius (step 5P/6P).** Two AppHost restarts. Between them no host-load history is ingested from any runner and derived aggregation pauses; live host stats, dispatch and sessions are unaffected. The window should be minutes (one `redeploy-old`).

**Parking checklist:** not applicable; no session that waits for input is added or changed.

## 8. Operator decisions (yes/no, with recommendation)

- **Q-0:** Adopt the step-0 hold now: no `deploy-temp`/`redeploy-old`/`all` of `rc/2026-11-24` or any `9cf3e4917` descendant until the §7.0 check prints `RELEASED`? **Yes.**
- **Q-1:** Store the server2 binding in desktop user-secrets `antiphon-server` (D-1)? **Yes.**
- **Q-2:** Values `server2`, `server2-build-slots`, revision 1, one new epoch GUID set in S1 (D-3)? **Yes.**
- **Q-3:** Disable durable history on `server2-temp`, no temp binding (D-4)? **Yes.**
- **Q-4:** Commission S2 (guarded `upgrade-broker`, `--no-recreate`, broker identity) instead of a manual pin edit (D-6, D-9)? **Yes.**
- **Q-5:** Let the swap wait up to 30 min for a natural idle instant instead of pausing server2 dispatch? **Yes.**
- **Q-6a** (branch P only): authorize deleting the one `server2` `HostLoadIngestCursors` row **inside the D-11 pump-paused window**, after `redeploy-old` serves the durable store and before the pump resumes, keeping sample and identity rows? **Yes.** If no: the row stays, every `server2` batch conflicts, and activation waits for the store-rotation card.
- **Q-6b** (only if E-4 shows a `server2-temp` row or another row with `gaps_present`): delete it in the same window once its runner serves no history? **Yes** (H-3 otherwise fails §9 item 4 for every task).
- **Q-7:** Pin the broker to the runner's rc SHA12 (D-7)? **Yes.**
- **Q-8:** Ship the S0 stop-gap (`HistoryEnabled: "false"` on the base runner) to release the hold before S1? **No**, unless an unrelated runner fix must reach server2 first; rc-24 brings no other runner change (G-16), and S0 costs its own Code/Review/port cycle.
- **Q-9:** Release the hold by a reviewed port of S1 onto the current rc line rather than waiting for the next cut? **Yes**, once S1 is Review clean.

## 9. Measurement of done

One window `[t0, t0+10 min)`, starting at least 2 minutes after step 9, with server2 at keep-7 load (seven tasks in flight, at least one build-slot lease held for part of the window; recorded from `/api/agent-tasks/pipeline` and `GET /build-slots` at t0 and t0+10). All must hold:
1. `GET /api/hosts/server2/load/history?from=t0&to=t0+10m&resolution=raw&limit=720` is 200, `complete=true`, `gaps=[]`, **120** items at 5 s (`HostLoadAggregation.ExpectedCount`), each with `ioWaitPercent`, memory and volumes non-null and `authority.state=known`.
2. Runner `collector` deltas: `dropped` 0, `appended` ≥ 120, `cursorAdvances` > 0, `authorityUnknown` 0.
3. E-4 at t0 and t0+10: `server2` `CommittedAt` advanced and within 15 s of each read; `RunnerStoreId` unchanged; E-5 shows 120 `IsPrimary=true` rows in the window. Metadata only.
4. A task started inside the window: `GET /api/agent-tasks/{id}/load?attempt=1` reports start, during and end with `complete=true`. Precondition: E-4 shows no row with `gaps_present` (H-3); if one exists, item 4 is red by construction, which is a stop, not a pass.
5. Pre-activation intervals stay `binding-unknown` or unbound. No backfill.

## Performance budget

Owner rules: `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`. S1/S2 tests are file and process-fixture contract tests with no database; fixtures are shared per class. Each slice has one cold `Antiphon.Tests` build (the collector plan measured 607.7 s at load 14.85 on server2: budget a 12-minute build phase, record loadavg and phase timings). CP-1..CP-4 estimate 14-16 minutes including the build. `RemoteScriptContractTests` (CP-5, 230 tests) has no measured server2 p95: TestDesign measures one cold run and sets the row timeout near 2x p95, capped at 30 minutes, or splits it. Operational bounds: AppHost restart script bounds; existing per-phase deadlines; broker idle wait at most 30 min; D-11 window about one `redeploy-old`; done window 10 min plus 2 min settle.

## Verification design

Not folded into this dispatch. V-1 (S1: durable path, literal identity, temp opt-out, unchanged `build-slots`, init-state mode) and V-2 (S2: broker identity, idle gate incl. `unknown`=busy, re-check, pin-line-only edit, rollback alias and restore, `--no-recreate`, phase outside `all`), with R-1 (existing contract classes), are settled by TestDesign after plan review.

--- next stage ---
next: review
handoff: Plan-review round 2 of the CARD-1893 activation plan: only the diff from f3ab42a7c (revision table R-1..R-10: step-0 hold and its check, D-11 pump-paused cursor reset replacing Q-6, §6.1 dispatch order and shared files, D-9 S1/S2 split); then TestDesign for S1 and S2.
artifact: docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md
