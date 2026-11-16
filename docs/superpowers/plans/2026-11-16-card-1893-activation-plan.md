# CARD-1893 activation plan: durable server2 host-load telemetry end to end

Plan task `eb85f57b`, 2026-11-16. This is a plan only: no code, no setting change, no restart and no deployment was made.
Master read: `61259342c25c9960c12ac7171488e52b61dc03be` (`origin/master` at the start of this task, equal to the task base).
Inputs read in full: CARD-1893 and its 11 history revisions; `docs/investigations/2026-11-16-card-1893-telemetry.md` (`origin/feat/card-task-705f622b`, tip `ce399e772010ab89edc018fb133376badb613eb5`); `docs/investigations/2026-11-16-card-1893-binding-and-broker.md` (commit `740ec396b06da7d427095d03d03913dccb5b3aa6`); the collector plan `docs/superpowers/plans/2026-11-16-card-1893-collector-plan.md` at `0f1f7bf4892a1dfa8005f910917b0885ccddb571`. That commit is **not** an ancestor of master yet; this plan calls it "Merge 253".

Roles in this plan: **ORCHESTRATOR** means the desktop orchestrator, which has a desktop shell, the `ANTIPHON_API` task-token reads and the AppHost restart. **OPERATOR-ONLY** means ssh to the server2 host as the operator account (`/home/mc/antiphon-server2`). **NEEDS-ROLLOUT** means the step takes effect only through an rc cut and the server2 rolling phases.

## 1. Outcome in one paragraph

Three faults have to be fixed in order. Merge 253 makes the spool open. A compose change (S1) gives the collector a durable path and its binding identity. A desktop server configuration entry gives the server exactly one primary binding with the same identity, and it must be in effect before the first keyed sample is ingested. A guarded broker swap (S2) moves the shared `build-slots` service to a GET-capable image that has its own identity. The reading for this plan found two hazards the diagnoses did not name, and both change the design. (H-1) The server's per-runner ingest cursor pins the first spool `RunnerStoreId` it sees, and there is no rotation. Any spool that is ingested before the durable `/state/host-load` path is in effect, or any pre-existing cursor row, locks out ingestion permanently with `host_load_identity_conflict`. (H-2) Adding environment entries to `build-slots` changes its compose config hash. The existing `ensure_build_slots_broker` would then **recreate the shared broker in the middle of a rollout, with no idle guard**. So S2 has to land with S1.

## 2. Ground truth: what the card and brief assume versus what the code does

| # | Assumption | Code / evidence at `61259342c` | Consequence |
|---|---|---|---|
| G-1 | Bindings may hot-reload | Every consumer takes `IOptions<HostLoadHistorySettings>` (`HostLoadIngestService.cs:19`, `HostLoadHistoryService.cs:21`, `HostLoadIngestPump.cs:12`, `HostLoadTaskWindowObserver.cs:15`). `IOptions` is a singleton computed once, and `ValidateOnStart` runs at startup (`HostLoadRegistration.cs:12`). The runner's `HostStatsSettings` is also `IOptions`, read once (`HostStatsRoutes.cs:42-67`, `HostLoadCaptureService.cs:26`) | **No hot reload.** A server change needs a server restart. A runner or broker change needs the container recreated |
| G-2 | A config route can show the effective Bindings | There is no settings route for `HostLoadHistory` (`docs/ops-http.md`, `docs/antiphon-api.md`). `GET /api/hosts/{id}/load` returns `physicalHostKey = Binding(runner, now)?.PhysicalHostKey` (`HostLoadHistoryService.cs:27-28`) | Rows can be read only from the configuration **sources** (E-1). After the change, a non-null `physicalHostKey` is the API witness that exactly one current binding exists |
| G-3 | Production server location | Topology A: the desktop AppHost is production until CARD-1744 (`docs/docker-stack.md:1907`). AppHost pins `ASPNETCORE_ENVIRONMENT=Development` (`Antiphon.AppHost/Program.cs:72`). The server has `UserSecretsId=antiphon-server` (`server/Antiphon.Server.csproj:8`) | Effective sources are `server/appsettings.json` (tracked, no `HostLoadHistory` section), the gitignored `server/appsettings.Development.json`, user-secrets `antiphon-server`, process environment and the command line. Arrays merge **by index across sources** |
| G-4 | The physical key, revision and epoch can be derived from code | They are opaque deployment settings (`HostLoadHistorySettings.cs:24-33`; CARD-1514 plan:156-160 says "explicit opaque deployment setting … on failover start a new source epoch"). Validation: key 1..128 chars, `SourceEpoch != Guid.Empty`, `Revision >= 1`, `To > From` (`:18`) | The values are **chosen**, not derived. §5 names who chooses each value and where the single source of truth lives |
| G-5 | Ingest matches on runner and time | Ingest matches all of: runner, sample time in `[From,To)`, `PhysicalHostKey`, `Revision == sample.BindingRevision` and `SourceEpoch`. `IsPrimary` additionally needs exactly one primary row for that key and epoch at that time (`HostLoadIngestService.cs:76-84`). Samples copy the three fields from runner settings (`HostLoadProjection.cs:44-45`) | Collector and server values must be byte-identical. A sample written before the runner has the values is unbound forever |
| G-6 | Unknown cursor state is harmless | `cursor.RunnerStoreId != page.Source.RunnerStoreId` throws `host_load_identity_conflict` on every batch (`HostLoadIngestService.cs:40`). The pump logs a warning and retries forever (`HostLoadIngestPump.cs:78-86`). The spool store ID lives in `<HistoryPath>/cursor.json` (`HostLoadSpool.cs:46-50`). No route resets the cursor | **H-1.** The first ingested spool must be the durable one. A pre-existing cursor row (unobserved) blocks activation. A temp runner gets a new store at every rollout because `antiphon-runner-temp_runner-state` is removed at retirement (`docs/docker-stack.md` CARD-1008 table) |
| G-7 | Merge 253 makes storage durable | Merge 253 resolves an absent path to absolute LocalApplicationData with `DoNotVerify`, then to `~/.local/share` (`HostLoadStoragePath.cs` at `0f1f7bf48`). In the container that is `/home/app/.local/share/antiphon/host-load`, which sits in the container layer and is lost on every recreate | Rolling out Merge 253 **without** a `HistoryPath` creates an ephemeral store. The server pins it, and the next recreate locks ingestion out (H-1). The `HistoryPath` must ship in the **same** rc |
| G-8 | Upgrading the broker image is enough | Even the current `GET /build-slots/host-load` returns `HostAdmissionSnapshot.Unknown` unless the broker's own `SessionRunner:HostStats:PhysicalHostKey` **and** `AuthorityId` are set (`BuildSlotRoutes.cs:71-78`). The checked-in `build-slots` service sets neither (`docker-compose.server2-runner.yml:169-184`). The client needs a matching key, a matching `AuthorityId` when configured, `Revision >= 1`, `state=known` and an age of 15 s or less (`HostAdmissionSnapshotClient.cs:62-70`) | The broker needs both a new image **and** identity environment |
| G-9 | A compose edit to `build-slots` waits for the operator | `ensure_build_slots_broker` runs `compose_host --profile broker up -d --no-build build-slots` in both `case_deploy_parent` and `case_deploy_temp_runner` (`scripts/c590-remote.sh:1012-1030`, `:9735`, `:9986`). Compose recreates a service whose config hash changed | **H-2.** S1's broker environment would recreate the live broker during `deploy-temp` with no idle check, dropping in-memory leases (`BuildSlotBroker.cs:37`). S2 must change this |
| G-10 | The broker pin is unknown | `broker_sha12()` prefers `BUILD_SLOTS_SHA12` from `$SERVER2_ROOT/secrets/stack.env` (`c590-remote.sh:979-983`, `:906`, `SERVER2_ROOT=/home/mc/antiphon-server2` at `:21`). The pin is written only when absent (`:1017-1019`). `retire_superseded_server2_images` keeps the pinned and live broker images (`:1078-1096`) | The value is UNOBSERVED. Reading it is OPERATOR-ONLY (E-6). It must be moved deliberately |
| G-11 | A lost lease kills builds | When renew gets 404, the renewer stops (`scripts/lib/build-slot.ps1:116`). The driver keeps running and nothing is killed | Recreating the broker with a held lease costs an over-subscription window. No build fails |
| G-12 | Temp and standing runners share a spool | They are separate compose projects with separate `runner-state` volumes. Temp is `server2-temp` (`docker-compose.server2-runner.temp.yml:5`) and inherits base environment | A temp runner inheriting the key and epoch is harmless only if it has no server binding row and no durable history (D-4) |

## 3. Evidence to gather first (read-only)

Never print a secret, a token, the opaque ingest cursor, a ledger secret or a whole configuration file. Every read below projects named non-secret fields only. Record the UTC time of each read.

| ID | Who | What | Exact read |
|---|---|---|---|
| E-1 | ORCHESTRATOR (desktop shell, main checkout) | Effective `HostLoadHistory` from every desktop server source | `pwsh -NoProfile -Command "$p='server/appsettings.json','server/appsettings.Development.json'; foreach($f in $p){ if(Test-Path $f){ $j=Get-Content -Raw $f \| ConvertFrom-Json; '{0}: {1}' -f $f, ($j.HostLoadHistory \| ConvertTo-Json -Depth 5 -Compress) } else { '{0}: absent' -f $f } }; $s=Join-Path $env:APPDATA 'Microsoft\UserSecrets\antiphon-server\secrets.json'; if(Test-Path $s){ $o=Get-Content -Raw $s \| ConvertFrom-Json; $o.PSObject.Properties \| ? Name -like 'HostLoadHistory*' \| % { '{0}={1}' -f $_.Name,$_.Value } } else { 'user-secrets: absent' }; foreach($t in 'Machine','User','Process'){ [Environment]::GetEnvironmentVariables($t).Keys \| ? { $_ -like 'HostLoadHistory__*' } \| % { '{0} env {1}' -f $t,$_ } }"` and also `Select-String -Path Antiphon.AppHost/Program.cs -Pattern 'HostLoadHistory'` (expected: none). Only `HostLoadHistory*` keys are emitted; the secrets file is never echoed. Classify the result as absent, expired, overlapping or other |
| E-2 | ORCHESTRATOR | Server binding resolution (API) | `GET /api/hosts/server2/load`: expect `physicalHostKey` null today. `GET /api/hosts/server2/load/history?from=<now-10m>&to=<now-5m>&resolution=raw&limit=60`: `gaps` |
| E-3 | ORCHESTRATOR | Running server and runner builds | `GET /api/version` (SHA, `land-v2`); `GET /api/session-runners/server2/status` (`buildVersion`, `runnerStoreId`, `processBootId`) |
| E-4 | ORCHESTRATOR (desktop Postgres `localhost:17280`, connection string from config, never printed) | Ingest cursor rows: **the H-1 gate** | `SELECT "RunnerId","RunnerStoreId","CommittedAt","Complete",("Cursor" IS NOT NULL) AS cursor_present FROM "HostLoadIngestCursors" WHERE "RunnerId" IN ('server2','server2-temp');` Never select `"Cursor"` itself |
| E-5 | ORCHESTRATOR | Existing samples by binding | `SELECT "RunnerId","PhysicalHostKey","SourceEpoch","IsPrimary",count(*),max("SampledAt"),max("IngestedAt") FROM "HostLoadSamples" WHERE "RunnerId" IN ('server2','server2-temp') GROUP BY 1,2,3,4;` |
| E-6 | OPERATOR-ONLY (ssh) | Broker pin, image and identity | `grep -E '^BUILD_SLOTS_SHA12=' /home/mc/antiphon-server2/secrets/stack.env` (one line only, never `cat`); `docker ps --filter label=com.docker.compose.project=antiphon-runner --filter label=com.docker.compose.service=build-slots --format '{{.Names}} {{.Image}} {{.CreatedAt}}'`; `docker image inspect --format '{{.Id}} {{index .Config.Labels "org.opencontainers.image.revision"}}' <that image>` (label may be absent: then report the image ID only); `docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' <broker> \| grep -E '^SessionRunner__(HostStats\|BuildSlots)__'` |
| E-7 | ORCHESTRATOR (from any server2 task container on the `antiphon-build-slots` network) or OPERATOR-ONLY | Broker route table and occupancy | `curl -sS -o /dev/null -w '%{http_code} %header{allow}\n' http://build-slots:8080/build-slots/host-load`; the same for `/build-slots/metrics`; `curl -fsS http://build-slots:8080/build-slots` projected to `budget`, `occupied`, `waiters` length and `enabled` (node one-liner; server2 has no jq) |
| E-8 | ORCHESTRATOR (server2 task) | Standing collector state | `curl -sS http://localhost:8080/host-stats/history?limit=1` status code, plus the `collector` object once Merge 253 runs; `curl -fsS http://localhost:8080/capabilities` (`version`) |
| E-9 | OPERATOR-ONLY | Image availability for the swap | `docker image ls antiphon-server2/session-testing --format '{{.Tag}} {{.ID}}'`: confirm the rc SHA12 image exists after the rollout |

Stop conditions after evidence:
- **E-4 has a `server2` row:** activation cannot ingest from a new spool. Go to Q-6 and do not continue past step 3.
- **E-1 shows Bindings in more than one source**, or any row whose key, epoch or revision differs from §5: resolve to a single source first (Q-1).
- **E-6 shows the broker already GET-capable** (E-7 returns 200): skip the image part of S2. The identity environment is still needed.

## 4. Decisions

Defaults hold unless the operator answers otherwise (§8).

- **D-1 (server binding source).** Use user-secrets `antiphon-server` on the desktop, the existing per-machine production override store (`docs/agent-credentials.md:333`, `docs/bootstrap.md:205`). Rejected: tracked `server/appsettings.json`, which would bind every dev, E2E and test host to production server2 identities and could break host-load tests that assume empty Bindings. Rejected: the gitignored `appsettings.Development.json`, which is less discoverable and has no CLI. Rejected: AppHost `WithEnvironment`, which is a code change for operator data.
- **D-2 (collector identity lives in compose).** S1 puts **literal** non-secret values in `docker-compose.server2-runner.yml`. Rejected: interpolation from `stack.env`. `compose_host` passes no `--env-file`, an unset `${X:-}` binds `""` rather than null, and a string key of `""` would defeat the `is null` checks (`HostAdmissionSnapshotClient.cs:29`, `:63`, `HostLoadProjection.cs:36`).
- **D-3 (values).** `PhysicalHostKey=server2`. `SourceEpoch` is one new GUID generated once by the S1 Code task with `[guid]::NewGuid()`. The landed compose literal is the single source of truth, and the server entry copies it. `BindingRevision=1`, or `max+1` when E-1/E-5 show existing rows for the same key and epoch. `AuthorityId=server2-build-slots`. Binding `Primary=true`. `From` is the UTC instant the server entry is written (step 4). `To` is unset.
- **D-4 (temp runner).** `docker-compose.server2-runner.temp.yml` sets `SessionRunner__HostStats__HistoryEnabled: "false"`, and the server gets **no** `server2-temp` binding. Reason: G-6. Temp gets a new store each rollout, so durable temp history would lock out its own cursor from the second rollout on and spam ingest warnings. Temp samples would be non-primary anyway. Rejected: a temp binding with `Primary=false`, which still hits H-1.
- **D-5 (durable path).** `SessionRunner__HostStats__HistoryPath: /state/host-load` on the standing runner. `/state` is the `antiphon-runner_runner-state` volume, owned by uid 1654 (`docker/stack/init-state.sh:16-21,107`) and preserved across redeploys (CARD-1008 table). The spool creates the directory itself. S1 also adds `/runner-state/host-load` to the init list with `chmod 0700` so the store is private.
- **D-6 (broker recreation, H-2).** S2 changes `ensure_build_slots_broker` to `up -d --no-build --no-recreate build-slots`: it creates or starts the broker, never recreates a live one. A new guarded phase `upgrade-broker` in `scripts/deploy-server2.ps1` (ValidateSet) and `scripts/c590-remote.sh` is the only path that recreates it. The phase:
  1. Refuses unless `GET /build-slots` shows `occupied=0` and an empty `waiters` list. It polls every 5 s for up to 30 min, re-checks immediately before recreating, and otherwise exits `BrokerUpgradeBusy`.
  2. Tags a rollback alias `antiphon-server2/broker-rollback:<old sha12>`, which `retire_superseded_server2_images` does not match.
  3. Rewrites only the `BUILD_SLOTS_SHA12=` line to the rollout SHA12.
  4. Runs `--force-recreate --no-build build-slots` and waits for health.
  5. Verifies `GET /build-slots/host-load` returns 200 with `state=known`, the matching key and the matching `AuthorityId`.
  6. On any verification failure, restores the old pin line and recreates from the alias.
  Rejected: the operator hand-editing `stack.env` before `redeploy-old` (an unguarded recreate during live temp builds). Rejected: a manual `docker compose up` (needs about 12 required interpolation variables by hand). Rejected: a client change to fall back to the local count, which the contract forbids (`docs/ops-http.md:1018`) and which would be wrong for a shared broker. Rejected: an explicit `AuthorityEndpoint`, which cannot add a missing route.
- **D-7 (broker image).** Pin the broker to the same rc SHA12 the runner rolls out. That image exists locally after the rollout (E-9) and contains `BuildSlotRoutes.cs:71-80` (added at `b0604d33a`). There is no separate broker release line.
- **D-8 (order).** Server binding comes before the runner rollout. Runner activation goes in the same rc as Merge 253. The broker swap comes after the primary witness, so each fault's acceptance stays attributable. The broker only has to be known before the final measurement window.

## 5. Where each value comes from

| Setting | Runner (S1 compose, `session-runner`) | Broker (S1 compose, `build-slots`) | Server (D-1 user-secrets) | Source of truth |
|---|---|---|---|---|
| History path | `SessionRunner__HostStats__HistoryPath: /state/host-load` | n/a | n/a | D-5 |
| Physical key | `SessionRunner__HostStats__PhysicalHostKey: server2` | same | `HostLoadHistory:Bindings:0:PhysicalHostKey=server2` | D-3, compose literal |
| Revision | `SessionRunner__HostStats__BindingRevision: "1"` | same | `…:0:Revision=1` | D-3, E-1/E-5 |
| Source epoch | `SessionRunner__HostStats__SourceEpoch: "<GUID from S1>"` | n/a | `…:0:SourceEpoch=<same GUID>` | Generated once by S1, copied from the landed compose |
| Authority ID | `SessionRunner__HostStats__AuthorityId: server2-build-slots` | same | n/a | D-3 |
| Runner ID | existing `PhoneHome__RunnerId: server2` (`HostStatsRoutes.cs:33`) | n/a | `…:0:RunnerId=server2` | existing |
| Primary / From / To | n/a | n/a | `…:0:Primary=true`, `…:0:From=<T0 ISO-8601 Z>`, no `To` | D-3 |

The server write (ORCHESTRATOR, desktop, after Q-1): `dotnet user-secrets set "HostLoadHistory:Bindings:0:<Field>" "<value>" --id antiphon-server` once per field. Read the values back with the E-1 projection only.

## 6. Code slices (separate Code task, then TestDesign and Review; not done here)

**S1: compose identity, durable path, temp opt-out.** Files:
- `docker-compose.server2-runner.yml`: the `session-runner` and `build-slots` environment per §5.
- `docker-compose.server2-runner.temp.yml`: `HistoryEnabled` false.
- `docker/stack/init-state.sh`: `/runner-state/host-load` with mode 0700.
- `docs/docker-stack.md`: server2 runner environment paragraph.
- `docs/ops-http.md` §host-load: the server2 binding values and the D-4 temp rule.

Tests:
- extend `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`, which pins the `build-slots` and runner environment at `:278-378`;
- add a new class `C1893ActivationComposeTests` that asserts the key, revision and authority ID are equal across both services, the epoch is a non-empty GUID, the temp opt-out is present and there is no empty-string interpolation.

**S2: guarded broker swap (H-2).** Files:
- `scripts/c590-remote.sh`: `ensure_build_slots_broker` with `--no-recreate`, plus the new `case_upgrade_broker`.
- `scripts/deploy-server2.ps1`: the `upgrade-broker` ValidateSet value and dispatch.
- `scripts/test-deploy-server2.ps1`, if it enumerates phases.
- `docs/docker-stack.md` rollout phases.
- `docs/testing-and-build.md:1310`: replace "needs an operator to move the pin" with the phase.

Tests:
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` (ValidateSet parse at `:5039`; `ensure_build_slots_broker` stubs at `:3036`, `:3973-4184`, `:7514`, `:7630`);
- a new class `C1893BrokerUpgradeScriptTests` that covers: refusal while occupied or waiting; a re-check race; that only the one pin line changes; the rollback alias; restore on a failed verify; and that `ensure_build_slots_broker` never recreates.

**Existing tests affected:** `DockerStackContractTests` and `RemoteScriptContractTests` (two classes share this name, and the filter covers both). There are no migrations, DTOs, routes, operation labels or statement budgets. `scripts/impacted-classes.ps1 -Path docker-compose.server2-runner.yml,docker-compose.server2-runner.temp.yml,scripts/c590-remote.sh,scripts/deploy-server2.ps1 -Json` returned `kinds=[]` at `61259342c`. The CARD-1782 fixed subset still applies at Merge.

### Checkpoints (provisional; TestDesign binds exact methods and measures)

Lane: server2 Linux (no `-Runner`, no `-Platform`; `runner-defaults` global runner is `server2`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-c1893a-red/ | activation-red | `/*/*/(C1893ActivationComposeTests*)\|(C1893BrokerUpgradeScriptTests*)/*` | V-1,V-2 | intended assertion failures | 6 | 14 |
| CP-2 | green | tests/Antiphon.Tests -> bin-c1893a-green/ | activation | `/*/*/(C1893ActivationComposeTests*)\|(C1893BrokerUpgradeScriptTests*)\|(DockerStackContractTests*)/*` | V-1,V-2,R-1 | all listed, 0 failed | 123 | 16 |
| CP-3 | green | CP-2 | remote-scripts | `/*/*/(RemoteScriptContractTests*)/*` | R-1 | all listed, 0 failed | 230 | 30 |

Suffix check: no other class begins with `DockerStackContractTests`, `RemoteScriptContractTests`, `C1893Activation` or `C1893Broker` at `61259342c`. `RemoteScriptContractTests` is unmeasured on loaded server2. TestDesign must measure it or split it by method group before using CP-3 (see the unit-lane deadline memory: a row over the 30-minute derived deadline exits 5 with no TRX).

## 7. Sequence

Each step ends with a stop condition. If it fails, stop and do not continue.

1. **Evidence** (ORCHESTRATOR and OPERATOR-ONLY, §3). Stop if E-4 has a `server2` cursor row (go to Q-6), if E-1 finds multiple or conflicting sources (Q-1), or if E-6 shows an unexpected image or customized broker environment.
2. **Land Merge 253** (ORCHESTRATOR). Stop until `git merge-base --is-ancestor 0f1f7bf48 origin/master` (or its rebased equivalent by subject) holds.
3. **S1 + S2:** TestDesign, then Code, then Review, then land (ORCHESTRATOR dispatch). Stop until both are landed on master with Review clean and CP-1..CP-3 green. Record the epoch GUID from the landed compose.
4. **rc cut** containing Merge 253, S1 and S2 (NEEDS-ROLLOUT; `docs/release-gates.md`). Preconditions: all three are on the rc line, `docker-compose.server2-runner.yml` on rc carries §5, and rc qualification reports no new red in the selected rows. Stop on any rc gate failure.
5. **Server binding** (ORCHESTRATOR, desktop, Q-1 yes): write the §5 user-secrets with `From=T0` set to now (UTC). Then run `pwsh -NoProfile -File scripts/restart-apphost.ps1` onto the rc tip (the orchestrator's own restart; check both locks first). Stop if:
   - exit is not 0;
   - `/api/version` does not equal the rc SHA;
   - `GET /api/hosts/server2/load` does not report `physicalHostKey=server2`;
   - E-2 history for a new window still reports `binding-unknown`.

   If startup fails `ValidateOnStart` (`HostLoadHistory binding is invalid.`): **rollback**, i.e. `dotnet user-secrets remove` each key and restart.
6. **server2 rolling rollout** on the rc SHA (NEEDS-ROLLOUT; ORCHESTRATOR under operational autonomy): `deploy-temp → drain-old → redeploy-old → drain-temp → retire-temp`. With S2, the broker is not recreated (`--no-recreate`). Stop after `redeploy-old` if:
   - runner `/host-stats/history?limit=1` is not 200;
   - `collector.appended` is not rising while `dropped` stays flat;
   - E-4 does not show one `server2` row whose `RunnerStoreId` equals the spool store (`/host-stats/history` `source.runnerStoreId`), with `CommittedAt` advancing across two reads 60 s apart;
   - the ingest warning `Host load ingest failed` appears in the desktop server log.

   Rollback: the runner rolls back with the standard rolling phases to the previous rc. Note that the `/state/host-load` store and the server cursor row stay and must be reused on the next attempt. Never delete the store directory.
7. **Primary witness** (ORCHESTRATOR). E-5 for samples after the rollout must show `PhysicalHostKey=server2`, `SourceEpoch=<E>` and `IsPrimary=true`. History for a fresh 5-minute window must return items with `ioWaitPercent`, memory and volumes. Stop otherwise: a key, epoch or revision mismatch means §5 drifted. Fix the server entry (a new revision with `To`/`From` at one instant) and restart. Pre-fix samples stay unbound.
8. **Broker swap** (Q-4/Q-5 yes; ORCHESTRATOR may run the phase, OPERATOR-ONLY if run by hand): `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Phase upgrade-broker -Sha <rc sha>`. It waits for an idle instant (at most 30 min). Stop if:
   - it exits `BrokerUpgradeBusy` (retry later, nothing changed);
   - E-7 `host-load` is not `200` with `state=known`, `physicalHostKey=server2`, `id=server2-build-slots`, `revision>=1` and `observedAt` within 15 s, with counts equal to a same-second `GET /build-slots`;
   - `POST`/`renew`/`DELETE` semantics fail in the phase's own lease smoke.

   Rollback: the phase restores the old pin line and recreates from the alias, again at an idle instant.
9. **Done measurement** (§9) under normal keep-7 load. Stop and file a Debug task on any criterion miss.
10. **Close out.** Record the measured window on CARD-1893. Then file two follow-ups: a card for the cursor store-rotation defect (H-1/G-6), and a card so CARD-1744 topology B carries `HostLoadHistory__Bindings__0__*` in `docker-compose.server2-server.yml` with the same values.

**Blast radius (step 8).** The broker leases every build and test driver on server2, and its state is in memory. At an idle instant nothing is lost. If an acquire races the recreate, that one driver's renew gets 404, the renewer stops, and the build continues unleased until it ends (G-11). That is a short over-budget window; no build fails and nothing is killed. Waiters lose their FIFO position. Recreating takes about 30 s or less, and health is checked every 10 s (`docker-compose.server2-runner.yml:185-190`). During it, `build-slot.ps1` treats an unreachable broker as a 60 s wait and then runs unleased at `-maxcpucount:4` (`scripts/build-slot.ps1:19`). Quiesce: do not pause dispatch (keep-7 standing direction). Rely on the idle guard. Scheduled long rows (qualifications, Full lanes) must not be in flight. Check `GET /build-slots` `leases[].holder` before running the phase.

**Parking checklist:** not applicable. This plan adds or changes no session that waits for input.

## 8. Operator decisions (yes/no, with recommendation)

- **Q-1:** Store the server2 binding in desktop user-secrets `antiphon-server` (D-1)? **Yes.**
- **Q-2:** Use physical key `server2`, authority ID `server2-build-slots`, revision 1 and one new epoch GUID set in S1 (D-3)? **Yes.**
- **Q-3:** Disable durable history on `server2-temp`, with no temp binding (D-4)? **Yes.**
- **Q-4:** Commission S2 (guarded `upgrade-broker` phase plus `--no-recreate`) instead of a manual pin edit (D-6)? **Yes.** Without it, S1's broker environment recreates the live broker mid-rollout.
- **Q-5:** Accept a broker swap that waits up to 30 min for a natural idle instant, rather than pausing server2 dispatch? **Yes.**
- **Q-6** (only if E-4 shows an existing `server2` cursor row): authorize the operator to delete that one `HostLoadIngestCursors` row before step 6, keeping the sample and identity rows? **Yes** if `RunnerStoreId` differs from the new store. Otherwise no. The alternative is waiting for the rotation follow-up card.
- **Q-7:** Pin the broker to the runner's rc SHA12 (D-7)? **Yes.**

## 9. Measurement of done

Take one window of `[t0, t0+10 min)`, starting at least 2 minutes after step 8, with server2 at its keep-7 load (seven tasks in flight and at least one build-slot lease held for part of the window, recorded from `/api/agent-tasks/pipeline` and `GET /build-slots` at t0 and t0+10). All of the following must hold:
1. `GET /api/hosts/server2/load/history?from=t0&to=t0+10m&resolution=raw&limit=720` returns 200, `complete=true`, `gaps=[]`, and **120** items at a 5 s interval, each with `ioWaitPercent`, memory and volumes non-null and `authority.state=known`.
2. Runner `collector` deltas over the window: `dropped` 0, `appended` 120 or more, `cursorAdvances` greater than 0, `authorityUnknown` 0.
3. E-4 at t0 and t0+10: `CommittedAt` advanced and is within 15 s of each read time. `RunnerStoreId` is unchanged. E-5 count for the window is 120 rows with `IsPrimary=true`. Metadata only; the cursor value is never read.
4. A task started inside the window: `GET /api/agent-tasks/{id}/load?attempt=1` reports start, during and end, `complete=true`.
5. Pre-activation intervals stay `binding-unknown` or unbound. No backfill.

## Performance budget

Owner rules are in `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`. The S1/S2 tests are file and process-fixture contract tests with no database, so the fixture is shared per class. `RemoteScriptContractTests` (230 tests) has no measured server2 p95. TestDesign must measure one cold run and set each row timeout at about 2x the measured p95, capped at 30 min per row, or split CP-3. Build estimates follow the collector plan's cold `Antiphon.Tests` build on loaded server2 (607.7 s at load 14.85): budget a 12-minute build phase and record loadavg and phase timings. The operational steps have their own bounds: the AppHost restart uses its script's bounds; each rollout phase uses the existing phase deadlines; the broker idle wait is at most 30 min; and the done window is 10 min plus 2 min of settle.

## Verification design

Not folded into this dispatch. V-1 (compose identity equality, temp opt-out, durable path) and V-2 (broker guard, rollback, no-recreate), with R-1 (existing contract classes), are settled by TestDesign after plan review.

--- next stage ---
next: review
handoff: Plan-review (different company) of the CARD-1893 activation plan: check H-1 cursor-store lock-out and H-2 unguarded broker recreate, D-1..D-8, the §7 order, and the evidence-first gates; then TestDesign for S1/S2.
artifact: docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md
