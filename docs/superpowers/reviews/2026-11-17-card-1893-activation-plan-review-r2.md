# CARD-1893 activation plan review, round 2

CLEAN. The amendment closes the round-1 blocker. The step-0 hold, the pump-paused cursor reset, the S1/S2 split, H-3, and the dispatch order match the code at `870a93d21f2b18c85a8b0905ab689b28e3099813`. Advisories below do not hold the land.

Reviewed source `5c1a0d5023b9aada4b62c1ff85d4be42e5b72bbe` on `feat/card-task-a1d414b6`. Diff only `f3ab42a7ca44a722b477e446b0e21285ecc620cd..5c1a0d5023b9aada4b62c1ff85d4be42e5b72bbe` (two docs commits, one file). Plan owner `a1d414b6-c240-4604-a494-7b9424c7166e` (Claude Opus). Reviewer is Grok. Previous review `ee9cdb64` at `11fa40e13b766d8255634339f385cef0d3789361`. `origin/master` and `origin/rc/2026-11-24` are both `870a93d21f2b18c85a8b0905ab689b28e3099813`. Mutation stays waived (operator 2026-11-12). Positive controls stay pending.

## 1. Step 0 hold

The rule forbids `deploy-temp`, `redeploy-old`, and `all` when the rollout SHA contains `9cf3e491705247bc03814e6afed77a6534805dfd` and the compose files at that SHA do not carry both `SessionRunner__HostStats__HistoryPath: /state/host-load` on the standing file and `SessionRunner__HostStats__HistoryEnabled: "false"` on the temp file. `drain-old`, `drain-temp`, `retire-temp`, and the check/provision phases do not start a runner from a new image (`scripts/deploy-server2.ps1:1379-1513`). `Phase=all` is only those five rollout phases (`:1547`).

The §7.0 script was run read-only against `origin/rc/2026-11-24` (`870a93d21`). `git merge-base --is-ancestor 9cf3e4917` exits 0. Neither grep matches. The script printed `HOLD`. `RELEASED` requires both greps. A missing or differently spelled line stays `HOLD`.

An AppHost restart does not open the server2 spool. `scripts/restart-apphost.ps1` preserves the desktop session-runner on port 17204 and never calls `deploy-server2.ps1`. The server2 runner is a compose container.

Older server2 runners do not create a `server2` cursor row. At `rc/2026-11-23` (`9b98828eb`) `HostLoadCaptureService.ExecuteAsync` returns when `HistoryPath` is null (`:60`), so no spool opens. `ReadHistory` then throws, and `HostStatsRoutes` answers 503 (`:88`). Phone-home does the same (`PhoneHomeCommandDispatcher.cs:1223`). `PhoneHomeRunnerClient.GetHostLoadHistoryAsync` throws on that error frame, and `HostLoadIngestPump.ProduceAsync` records `history_read_unavailable` without enqueueing (`:74`). `IngestAsync` never runs, so no cursor row is inserted. `rc/2026-11-24` adds only `9cf3e4917` under the runner, `docker/`, and the server2 runner compose files.

## 2. Pump-paused reset (replaces Q-6)

`HostLoadIngestService.IngestAsync` is the only production writer of `HostLoadIngestCursors` (`:60-69`). Its only server caller is `HostLoadIngestPump.ProcessOneAsync` (`:55`). No host-load API writes the table. `HostLoadDerivedService` does not touch it.

`HostLoadHistory:Enabled` defaults to true (`HostLoadHistorySettings.cs:6`) and is not part of `Validate`. `HostLoadIngestPump.ExecuteAsync` returns immediately when it is false (`:62`), before `ProduceAsync` or `ConsumeAsync`. `HostLoadDerivedWorker.ExecuteAsync` does the same (`:127`). Both read `IOptions` once at startup, so the plan's AppHost restart is required, and it is in step 5P. Binding resolution does not consult `Enabled`, so the step 5 `physicalHostKey` and `binding-unknown` stops still work during the pause (`HostLoadHistoryService.cs:62-72`).

The window is fail-closed against a late commit:

- After the restart the new process has no producer and no queued batch.
- A commit between the two 30-second `CommittedAt` reads fails step 5P.
- A frontier-matching page rewrites `CommittedAt` on every successful ingest (`HostLoadIngestService.cs:65-69`). A pump that is still running against a runner that serves pages cannot stay frozen for 30 seconds.
- If the current runner answers 503, `CommittedAt` can stay frozen while the pump is still looping. After `redeploy-old`, step 6P (d) requires the `server2` row to stay absent across two reads 30 seconds apart. A live pump reinserts on the next 5-second poll. That check stops. Step 6P (c) requires the delete to report one row, and (f) requires `RunnerStoreId` equal to the new spool id plus an advancing `CommittedAt`. A late insert of the old store id fails (f).

Deleting while any runner still serves the pinned store, or before the new spool exists, remains rejected. Sample and identity rows are kept.

## 3. D-9 broker split

`build-slots` is its own service (`docker-compose.server2-runner.yml:169-192`) with its own environment and no YAML anchor shared with `session-runner`. `ensure_build_slots_broker` runs `compose_host --profile broker up -d --no-build build-slots` with no `--no-recreate` (`scripts/c590-remote.sh:1012-1021`), from `deploy-parent` and `deploy-temp-runner`. Compose recreates that service when its own config changes. S1's file list leaves the `build-slots` service byte-identical to `870a93d21` and does not edit `c590-remote.sh` or `deploy-server2.ps1`. The temp compose file is not in the `compose_host` invocation. S1 therefore does not recreate the broker. S2 still owns the identity environment together with `--no-recreate` and `upgrade-broker` outside `Phase=all`.

## 4. H-3

True. `HostLoadHistoryService.TaskAsync` sets `durableLoss` when any cursor row has `GapsJson` other than `"[]"` or `""` (`:156-157`), then `complete` is false for that task and the gap list is `event-or-sample-gap` (`:166`). One gapped row makes every task-load answer incomplete, including tasks whose own windows would otherwise be complete.

E-4's `("GapsJson" NOT IN ('[]',''))` matches that predicate for the non-null column defined by `20261107034430_HostLoadTelemetryS1`. §9 item 4 treats any `gaps_present` row as a stop. That precondition is the right gate.

## 5. Dispatch order and checkpoints

`origin/feat/card-task-d336fcc3` is `12105bfbc2e0f472614e51a96adc4240b2384a32`, not on master, and its diff is `docs/docker-stack.md` +183/−7. `origin/feat/card-task-c6c85b5b` is `1b59dd109dd37c525d902b55ed3fbed1ef647637`, not on master, and touches `docs/docker-stack.md`, `scripts/c590-real.ps1`, `scripts/c1609-host-proof.ps1`, `scripts/deploy-server2.ps1`, and `VolumeRetirementHostProofTests.cs`. S1 shares only `docs/docker-stack.md` with those tasks. S2 shares that doc and `scripts/c590-real.ps1`, and also `scripts/deploy-server2.ps1`. Land order `d336fcc3`, then CARD-1957, then S1, then S2 serializes every shared edit. The rule-4 exception lets S1 precede a stalled CARD-1957 only for the doc, with the land rebasing it. S2 still waits.

`### Checkpoints` is the heading the importer reads. On master, `DockerStackContractTests` has 117 `[Test]` methods, `DockerStackDocumentationTests` 19, and `RemoteScriptContractTests` 230. The new-class floors (6 and 8) make CP-2 min 142 and CP-4 min 144. The OR filters match the importer's parenthesized shape. Derived row timeouts are `max(15, 3 × EstimatedMinutes)`: CP-1 and CP-3 are 42 minutes (no warning); CP-2 and CP-4 are 48; CP-5 is 90. The 45-minute line is a warning, not a refusal (`PlanTableImporter` `RowTimeoutAdvisoryMinutes`). CP-5's handling is acceptable: TestDesign measures one cold run and sets the timeout near 2× p95, capped at 30 minutes, or splits the row before use. CP-1..CP-4's 14–16 minutes include the collector plan's measured 607.7-second cold build. That is a provisional budget, not an unmeasured 48-minute run.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| §7.0 hold script on `origin/rc/2026-11-24` | `HOLD` |
| `git merge-tree --write-tree origin/master HEAD` | tree `5bda5b4c37510f6024b94b16500933b561935baa`, exit 0 |
| `scripts/check-evidence-diff.ps1` `f3ab42a7c..HEAD` | commits=2, entries=0, violations=0, exit 0 |
| `scripts/impacted-classes.ps1 -Range` | kinds and filter empty; downgrade selector count 0 |
| Leased `dotnet build tests/Antiphon.Tests` (`UseAppHost=false`, slot `5709cac3-8844-4155-b6ac-e2b838e63cee`) | 0 errors, 2145 warnings, 3m 47.15s, exit 0 |
| Leased `dotnet run --no-build` of `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*` (slot `ea491b6e-94bc-4c92-a5e7-ebddd4ce0af4`) | total 53, failed 0, skipped 0, 54s 474ms, exit 0 |

CP-1..CP-5 were not run. `C1893ActivationComposeTests` and `C1893BrokerUpgradeScriptTests` do not exist; those rows belong to TestDesign and Code. `reviewedSourceClean` stays false because these runs are build-slot leases, not checkpoint-tool receipts. No OS-selected production path changed. Producer/recipient delivery and input-wait release do not apply. Parking is not applicable.

## Advisories

- G-17 and §7.0 say an AppHost restart restarts the desktop runner and opens `%LOCALAPPDATA%\antiphon\host-load`. `restart-apphost.ps1` preserves the runner on port 17204. The server2 spool stays closed either way. A desktop spool opens when that runner process itself is replaced.
- §6.1 does not list `scripts/deploy-server2.ps1`. CARD-1957 and S2 both edit it. Rule 3 already keeps S2 behind CARD-1957.
- CP-2 and CP-4 warn at 48 minutes. TestDesign should set those estimates to at most 15, or record the one-minute overshoot, when it binds the rows. The plan's phrase "a row over the derived 30-minute deadline exits 5" is the wrong mechanism: exit 5 is `ExitCodes.Timeout` when a row or the total deadline is actually exceeded. Import does not refuse an estimate of 16 or 30.
- The plan says two classes are named `RemoteScriptContractTests`. Master has one, and its 230 `[Test]` methods match CP-5's floor.
