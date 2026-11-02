# Investigation: retiring the stale server2-temp runner registration

Task d86bdf31 (Investigate), base `4bd1473f0b59620db8832eb781a57a8d1da6e78c`, 2026-11-02.
Read-only: GET routes on `https://antiphon.desktop.codeperf.net` and repository reads only.
No host, runner, container, setting or secret was changed.

Operator request (verbatim): "server2-temp: Linux, stale, not available. It looks like an old
registration you could clean up. can we clean this up"

## Verdict

**Confirmed. The row is the rolling rollout's retired placeholder, kept by design. It is not
an orphan. Nothing on server2 needs cleaning.** The last rollout (rc `8edda999`) drained and
retired `server2-temp` on 2026-10-30 at 00:45:02Z. Its containers, its four private volumes
and its network were removed and verified at 01:16Z. Removing the configuration entry would
break the next rollout at gate 1 (`deploy-temp`) and gate 2 (the temp canary).

What the operator actually sees is caused by two server gaps:

1. **A permanent Error attention row** (`RunnerUnavailable`, "Runner server2-temp
   unavailable"). The alarm exclusion for draining and retired runners was designed but never
   wired up: `server/Program.cs:354` still registers `NeverExcluded`.
2. **The catalogue shows the retired runner as "stale"**, not "retired".
   `SessionRunnerCatalogue.Row` sets `unavailableReason` to `stale` or `unavailable`, and its
   DTO has no `retired` field.

## (1) Where the registration lives

| Place | Key / table | What it holds for server2-temp | Evidence |
|---|---|---|---|
| Desktop server user-secrets (`antiphon-server`) | `PhoneHomeRunner:Runners:server2-temp` (`SharedSecret`, `CallbackOrigin`, homes, `MaxCapacity`, …) | The **only** source of the catalogue row. Every map key, enabled or not, becomes a directory slot. | `server/Application/Settings/PhoneHomeRunnerSettings.cs:145` (map), `:199-214` (`Configured` includes disabled entries); `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerDirectory.cs:58-61` (slots built from `Configured`), `:102-110` (`KnownRunnerIds`); `server/Application/Services/SessionRunnerCatalogue.cs:27` (one row per known id). Documented custody: `docs/testing-and-build.md:974-981`, `docs/agent-credentials.md:326-328`. Not readable from here, and no value was read. |
| Database | `SessionRunnerStates` row, keyed by `RunnerId` | The durable drain and retire record: `draining=true`, `drainedAt=2026-10-29T23:54:04Z`, `drainReason="CARD-0727 rolling upgrade complete"`, `redirectTo=server2`, `retireWhenIdle=true`, `retiredAt=2026-10-30T00:45:02Z`, `retireReason=idle` (from `GET /api/session-runners/server2-temp/status`). | `server/Domain/Entities/SessionRunnerState.cs:7-20`; `AppDbContext.cs:132,457`; loaded at startup by `server/Application/Services/RunnerStateLoader.cs:38-49`, which logs and skips a row whose id is not configured. |
| Server memory only | Directory slot: `RegisteredPlatform` and `PlatformObservedAt` | `platform=linux`, `platformObservedAt=2026-10-29T17:51:53Z`. This is set at registration (`PhoneHomeRunnerDirectory.cs:343-344`) and is not persisted, so the server process has stayed up since the temp runner last registered. | Live GET. |
| Database (history) | `AgentTasks.RunnerId` | 54 tasks created since 2026-10-11 ran on temp: 53 Succeeded, 1 Canceled, all terminal. No open task names it. | `GET /api/agent-tasks?since=2026-10-27T00:00:00Z` (954 rows; oldest createdAt 2026-10-11) and `?status=Queued,Dispatched,Working,Blocked` (14 rows: 0 on temp). |
| Attention feed (computed, in memory) | `RunnerUnavailable`, conditionKey `runner-unavailable:server2-temp` | Severity Error, "0 open task(s), 0 live session(s)", `downSince=2026-10-30T00:45:02.678Z`, `lastReason=transport_abort`. | `GET /api/attention`; producer `server/Application/Services/AttentionService.Alarms.cs:10-26`, episode raise `RunnerAlarmCoordinator.cs:103-127`. |
| Host stats (computed) | `GET /api/hosts/stats` | `server2-temp offline stale observedAt=2026-10-30T00:44:58Z`. | Live GET. |
| Repository | `docker-compose.server2-runner.temp.yml:6` (`PhoneHome__RunnerId: server2-temp`), `scripts/deploy-server2.ps1` (about 40 hard-coded uses, e.g. `:1282`, `:1288-1331`, `:1433-1435`), `scripts/c590-remote.sh`, `scripts/verify-card0849-caches.ps1:59`, `scripts/c590-real.ps1:669,732,735` | These are the same thing. The Compose project `antiphon-runner-temp` on server2 is the container that registers as runner id `server2-temp`. The scripts read its status from the server by that id. | — |

None of these name server2-temp:
- runner defaults: `GET /api/runner-defaults` gives revision 2, `globalRunnerId=server2`, `kindDefaults=[]`;
- routing pins: `GET /api/routing-pins` gives `{"pins":[]}`, and `RoutingPin` has no runner field;
- host budgets: `GET /api/hosts` gives server2-temp `configuredMaxInFlight=null`, `revision=0`;
- agents: `GET /api/agents` (2,926 rows) has 0 mentions, so no agent is runner-bound to temp;
- seats: `GET /api/session-runners/server2-temp/slots` returns 503 `phone_home_unavailable` (offline; no seat can be listed);
- scheduled card actions: no runner field was found on any entity.

## (2) Is anything still using it?

**No live use.** There are 0 open tasks, 0 sessions, 0 queued tasks, no defaults, no pins and
no budget. The only open task that mentions it is this one (d86bdf31, by its title).

**The design uses it.** The rollout scripts and docs depend on the entry staying configured:
- `docs/testing-and-build.md:978-979`: "The temp entry stays configured: offline it is
  unavailable and costs nothing."
- `docs/docker-stack.md:134-135`: gate 1 `deploy-temp` "accepts either a retired temp
  placeholder or a cleared offline one". `:160-163` reactivates it with `/drain/clear`, then
  `/drain`.
- `docs/docker-stack.md:248-249`: "Preserve the retired absent row; the next `deploy-temp`
  supports it."
- `docs/docker-stack.md:108-110`: a 404 status is a stop-and-report condition. With the entry
  removed, `GET …/server2-temp/status` is 404 (`docs/ops-http.md:133`, "An id that is not in
  the map is 404").
- Gate 2 dispatches a canary with `-Runner server2-temp` (`docs/docker-stack.md:123`). Runner
  admission accepts only configured ids (`docs/ops-http.md:145`).

## (3) Documented teardown and what it leaves

Gate 8 `drain-temp` and gate 9 `retire-temp` (`docs/docker-stack.md:129-130`;
`scripts/deploy-server2.ps1:1433-1477`) remove the temp **containers**, the four **private
volumes** (`antiphon-runner-temp_{work,dind-data,runner-tmp,runner-state}`) and the network.
They leave three things, on purpose:
- the server registration;
- its `SessionRunnerStates` row;
- the three shared cache volumes and all main-runner volumes (`docker-stack.md:388-400`).

`retire-temp` itself starts with `Get-RunnerStatus -RunnerId 'server2-temp'`
(`deploy-server2.ps1:1434`), so the phase needs the registration.

For the last rollout, the stored evidence is the attention feed's report body for task
`dcf9c6f3` ("Resume rc 8edda999 rollout: retire temp containers and Part C", 2026-10-30):
- Receipt `c727375426ea60f0` at 01:16Z: `retire-temp-containers`, `retire-temp-inventory` and
  `retire-temp-runner` all exited 0. The journal reached `volumesRemoved` with outcome
  `completed`, and about 21 GiB was reclaimed.
- The four temp volumes and `antiphon-runner-temp_default` were removed.
- The report verified "no `antiphon-runner-temp_*` containers, volumes or networks" and said
  "the runner row is stale, offline, draining … That is the expected leftover row."
- An earlier task, `a04846da` (2026-10-29), had also removed leftover temp volumes by exact name
  with proof.

**The stale registration is meant to persist until the next rollout reactivates it.**

## (4) Is there a way to remove or hide it?

- **There is no API, UI or CLI removal.** `SessionRunnerEndpoints.cs` maps GET list and status,
  capacity, drain, drain/clear, retire, slots, cold-reset, record-release, provider-auth and
  connect. There is no DELETE. `POST …/retire` (`:208-226`) only stamps retirement, which is
  already done. No "hide stale after N days" setting exists.
- **`Enabled=false` would not hide it.** Disabled entries still become slots and catalogue rows
  (`PhoneHomeRunnerSettings.cs:199-214`, `PhoneHomeRunnerDirectory.cs:60`).
- **The only way to remove it** is to delete the `PhoneHomeRunner:Runners:server2-temp`
  user-secrets block and restart the AppHost. After that:
  - the `SessionRunnerStates` row stays orphaned, logged and skipped (`RunnerStateLoader.cs:45-49`);
  - the 54 historical task rows keep `RunnerId=server2-temp`;
  - the next rollout needs the block re-entered by the operator, a human-only secret custody
    step (`docs/agent-credentials.md:326-328`), plus another AppHost restart before gate 1.

**What an AppHost restart disturbs** (from `docs/apphost-runbook.md:34-46` and `:54`):
- **Survives:** Postgres, the desktop session runner on 17204, and detached pty-host and herdr
  sessions. The orchestrator and the desktop delegates keep running.
- **Interrupted:** the API, client and dashboard go down for the restart window.
- **server2:** its phone-home socket drops and reconnects. Requests in flight fail with typed
  `phone_home_connection_closed_*` errors (`docs/session-runtime-invariants.md:823-828`). The
  dead-session grace map restarts empty (`:918-923`).
- **Code activation:** the restart builds whatever is on disk in `C:\src\Antiphon`. Live
  `/api/version` is `8edda999` (on `rc/2026-10-27`). `rc/2026-10-27` is now 390 commits ahead
  of it, and `rc/2026-11-01` (`10a826b9`) does not contain it (1,830 commits ahead). If the main
  checkout has moved, a config-only restart also activates new server code against a server2
  runner still on `8edda999`. Use `-ExpectedServerSha` and check the checkout HEAD first.
- **Windows rc qualification:** not expected to be affected. It runs from
  `C:\Antiphon\releases\checkout` with a private state root (`docs/release-gates.md:490-491`).
  Its test hosts never launch against the production runner (`AGENTS.md:71`). Build slots come
  from the 17204 runner, which survives. This is from the docs; the live run was not observed.
- **Restart-only option:** `restart-session-runner.ps1` is not needed. The registration lives
  in the server, not in the runner.

## (5) server2 side

The host is **not visible from this container.** `/var/run/docker.sock` here is the runner's
own nested dind (`docker info`: containerized Debian, 1 container, `c1609…` test-fixture volumes
only). No SSH was attempted.

The stored evidence is the 2026-10-30 report described above, which says nothing is left. If
the operator wants fresh proof:
- From the desktop, read-only: `ssh mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'`
  (the same check as `docs/docker-stack.md:232`). Also run
  `docker volume ls --filter name=antiphon-runner-temp_`.
- The scripted read-only census is `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Phase check-census`
  (`deploy-server2.ps1:1482-1502`: "no host jq, rollout lock, SSH, verify, POST or host case").
- If anything does remain, the only sanctioned removal is gate 9: run `-Phase retire-temp -DryRun`,
  then apply within 900 s with `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operation-id>` and
  `-RetirementProof <operation-id>` (`docker-stack.md:130`).
- Gate 9 never touches main volumes or the shared caches. Never use a blanket `down -v` or a
  name-pattern sweep (`docker-stack.md:385,395`).

## (6) Recommendation

1. **Do not delete the registration.** Answer the operator: it is the retired placeholder that
   the next staged rollout reactivates (`docs/testing-and-build.md:978-979`,
   `docs/docker-stack.md:248-249`), and server2 was already cleaned on 2026-10-30.
   Who: the orchestrator relays this. No action is needed.
2. **Optional fresh host proof:** run the `ssh … docker ps -aq` and `docker volume ls` commands
   above from the desktop. They are read-only. Who: the orchestrator, or a Deploy/Custom task on
   the desktop lane. Verify that both return nothing for `antiphon-runner-temp`.
3. **Fix the noise in the product.** Make retired and draining runners non-alarming and show
   them as retired. This needs a Plan, then Code; it is not an ops cleanup.
4. **If the operator still wants the row gone now** (human-only decision, operator-held secret):
   - delete the `PhoneHomeRunner:Runners:server2-temp` user-secrets block;
   - run `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <main-checkout HEAD>`
     from the main checkout (orchestrator autonomy), after checking
     `logs/apphost.restart.lock` and `logs/apphost.launch.lock`;
   - verify `GET /api/version`, that `GET /api/session-runners` no longer lists server2-temp, and
     that server2 shows `available` and `dispatchEligible` true with `acceptingNewWork=true` on
     `…/server2/status`;
   - re-add the block and restart again before the next rollout's gate 1.

   Risks: do not do this mid-rollout, because temp is a live participant from gate 1 to gate 9.
   A restart while the main checkout is ahead of `8edda999` activates new server code. Running
   delegates survive; server2 reconnects.

## Remaining uncertainties

- The server2 host was not observed directly. The absence of temp leftovers rests on the
  2026-10-30 stored report (receipt `c727375426ea60f0`).
- The user-secrets block was not read. Its exact contents are inferred from the docs and the
  live catalogue. The live `displayName` is `server2-temp`, not the documented `server2 (temp)`,
  so `DisplayName` is probably unset.
- The main-checkout HEAD on the desktop is unknown from here.
- The rc qualification's independence from an AppHost restart is taken from the docs, not from
  observing the live run.

## Not done, noted

- Fix idea: register a `SessionRunnerStates`-backed `IRunnerAlarmExclusion` (retired or
  draining means excluded), and add `retired` to the catalogue reason/DTO so the UI says
  "retired" instead of "stale".
