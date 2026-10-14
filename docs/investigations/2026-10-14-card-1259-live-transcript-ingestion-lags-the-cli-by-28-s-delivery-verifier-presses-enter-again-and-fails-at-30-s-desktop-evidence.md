# CARD-1259: live transcript ingestion lags the CLI by 28 s; delivery verifier presses Enter again and fails at 30 s — desktop evidence

Task `bd33cc44`, 2026-10-14 UTC. Read-only investigation on the desktop; no production source, configuration, provider input, session lifecycle, or database data changed. Generated diagnostic programs and metadata evidence are under `C:\src\Antiphon\.antiphon\c1259-desktop-evidence-bd33cc44`. Native provider transcript content was neither opened nor copied.

## Finding and confidence

**Confirmed mechanism: the serial server event pump can await queue delivery confirmation while that confirmation depends on transcript rows the same pump has not consumed.** The source dependency and the earlier controlled blocked-pump reproduction establish the mechanism. A new passive desktop observation caught the same false-negative shape: a UserPrompt was received from the runner in **0.469977 s**, but was stored **31.913968 s after that receipt**; its answer and TurnEnd waited **30.347580 s after runner receipt**. The delivery timed out, then the grace pull stored all three together and late-confirmed it. No input was sent by this investigator.

This establishes that a delay of the reported magnitude occurs downstream of native-file ingestion. It does not identify the exact awaited operation in the **07:02 Family incident**: no incident phase traces or runner-publish timestamps were retained. The live observation also lacks the server's handler-entry trace, so attribution of that particular wait to the queue handler is a reconstruction supported by its preceding TurnEnd and delivery timestamps, not a captured stack. SignalR, other inline side effects, database admission and scheduling are not individually timed.

The cold-history mechanism is real but does not by itself fit the collected Family timeline. Family bound at **07:00:50Z**, about **86.815 s before** its prompt. The file is currently **25,646,334 bytes (24.46 MiB)**; an equal-size generated history took **7.652744 s** to drain in the real Windows tailer. Historical file size and read frontier are unavailable, so this is not an unconditional exclusion of an earlier larger file or additional I/O/scan stalls. Delays near 30 s also recur many hours after the sole recorded Family bind.

Next: **Plan** for the confirmed server ingestion/confirmation dependency, retaining the original incident's exact-handler uncertainty. This is not a claim that the morning incident's individual blocking hop has been conclusively traced.

## Provenance

- Read the entire server2 report with `git show`, without switching branches: branch `feat/card-task-baa53a4a`, commit `389f1f1330ef539d3fe3e01dacb3a6d90d5093bd`, artifact `docs/investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s.md`.
- Shared checkout HEAD and current server `/api/version`: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`. File:line references below use that revision unless stated otherwise.
- Live runner `/capabilities`: `build.commitSha=893ddfceeebf4d13d9401848c535abada3983c27`, `processStartUtc=2026-10-13T21:12:44.3918931Z`, `assemblyWriteTimeUtc=2026-10-13T21:12:43.1080148Z`. This identifies the running desktop runner, rather than inferring it from checkout HEAD.
- The diagnostic loaded the on-disk runner DLL, informational version `1.0.0+893ddfceeebf4d13d9401848c535abada3983c27`, SHA-256 `176b1ac4c715e70260230db14c3e601990aebf3ba6fcd204b75a93d24005af9a`. Its copy in the probe output had that same hash. This was a binary characterization, not a fresh source-build qualification.
- `TranscriptTailer.cs`, `SessionRunnerRuntime.cs`, and `SessionRunnerEventPump.cs` have no diff between those two revisions. The queue/runtime changes are CARD-1247 late-confirmed channel reply handling, not the ordinary verification polling gate or serial event dispatch.
- Server log timestamps include `+01:00`; runner supervisor timestamps are local time. The UTC pty-host start and sidecar child start corroborate the conversion. Runner supervisor output lacks the date on each application line; its startup/run continuity and the matching sidecar anchor the cited morning section.

## Family file and binding metadata

Session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461`; standing agent `a7647365-4803-4541-a457-ba9e7fcfa8f0` (`Family`). The database session owns `C:\src\ClaudeBot\agents\family` and the local runner (`RunnerId` null).

Only `Get-Item` was used on the native JSONL:

```text
C:\Users\lndco\.claude\projects\C--src-ClaudeBot-agents-family\fb38e751-dfbd-4b8d-9ce5-ef84d1828461.jsonl
ObservedAtUtc:  2026-10-14T20:11:01.5550732Z
Length:         25646334 bytes
LastWriteTimeUtc: 2026-10-14T20:03:33.7575774Z
```

The runner sidecar `C:\logs\antiphon\session-runner\transcripts\fb38e751dfbd4b8d9ce5ef84d1828461.json` records `resumeLaunch=true`, `how=exact`, child start `07:00:47.6338084Z`, first input `07:01:20.6370259Z`, and update `07:01:20.6370268Z`. That update is not the bind timestamp: first-input recording also updates the sidecar. The log supplies the earlier bind time.

No UUID cursor, byte offset, or size-at-bind exists in that sidecar. `src/Antiphon.SessionRunner/TranscriptSidecar.cs:18` defines its metadata fields; `SessionRunnerRuntime.cs:3319` persists path/how, while `:3339` updates the sidecar timestamp. `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:909` defines the bind event with only session ID, path and method. A database-column inventory found no general persisted native offset/size field for AgentSessions or TranscriptEntries. No target UUID byte offset was read from the native file.

`src/Antiphon.SessionRunner/TranscriptTailer.cs:366` initializes `offset=0` after locating a binding. `:563` resumes a sidecar's **path**, not its byte position; `:400` also resets offset on a fork. `:49` caps a read at 1 MiB, `:416` applies that cap, `:423` advances an in-memory offset, and `:440` waits 300 ms even when more bytes remain. `:391` runs the periodic fork scan inline. Thus a fresh tailer/rebind replays history; there is no durable offset-resume mechanism in this path.

## Morning incident timeline

| UTC, 2026-10-14 | Evidence | Observation |
|---|---|---|
| 07:00:44.996457 | AgentSessions row `fb38e751…`, StartedAt | Server session generation starts. |
| 07:00:46.8369084 | `C:\logs\antiphon\session-runner\pty-hosts\logs\fb38e751dfbd4b8d9ce5ef84d1828461.log:83` | Pty-host starts, PID 12004. |
| 07:00:47.6338084 | Sidecar ChildStartUtc; runner session metadata | Provider child starts. |
| 07:00:50, second precision | `logs/session-runner.log:84734` | `Tailing transcript …fb38e751…jsonl for session fb38e751…`. Only matching tail-start line in this supervisor log; no matching fork/failure line. |
| 07:01:21.715 | `server/logs/antiphon-20261014.log:132686` | Remote-control setup starts. |
| 07:01:30.608784 | AgentSessions InteractiveLaunchCompletedAt | Interactive launch completed. |
| 07:01:34.807153 | SessionQueuedMessages `fc2286e9-2811-4537-b68e-0bfcbf872567` | Channel message queued, sequence 72. |
| 07:02:15.577028 | Same queue row | Delivery starts, baseline 1124, one delivery attempt. |
| 07:02:16.815 | TranscriptEntries `68829ca2-bff3-4b74-8ee1-b1c7af4c3fe2`, sequence 1125 | Native UserPrompt timestamp; UUID `f3e88a7d-51f4-4db1-b691-89827485b2be`. |
| 07:02:19.579 | TranscriptEntries sequences 1126/1127 | AssistantText and TurnEnd timestamp; UUID `0d928d08-9840-4b94-ac37-368c0010c690`. |
| 07:02:24.001 | Server log `:133107` | Enter attempt 2/3. |
| 07:02:31.247 | Server log `:133171` | Enter attempt 3/3. |
| 07:02:47.189 | Server log `:133277` | 30-second verification failure, baseline 1124, screen output advanced. |
| 07:02:47.356032 | CreatedAt for sequences 1125–1127 | Three rows stored as one batch: prompt lag **30.541032 s**, answer/end lag **27.777032 s**. |
| 07:02:47.371093 | Queue row verdict timestamp | LateConfirmed (enum value 8), Sent; log `:133279` reports success at 07:02:47.374. |

These are metadata-only SELECTs against the actual desktop database, independently confirming the server2 report's retained SQL. The capture contains one new UserPrompt and no duplicate submission for the incident; it does not prove universal safety of Enter retries.

The other active session `c83e3c62-c0ec-4a12-b40d-8f083dbd8885` also timed out during the window: prompt UUID `e8843be4-1dc5-4e62-a6b6-93eb109a12e0`, producer `07:02:26.968Z`, CreatedAt `07:02:59.322103Z`, lag **32.354103 s**; queue row `59a2e02f-efb5-49c2-ad72-d18932dc5c6c` late-confirmed at `07:02:59.435294Z`. This is corroboration of overlapping delayed deliveries, not proof that it blocked Family.

No `Runtime phase` line was found in the morning window, or in the inspected current-day server files/AppHost capture. `server/Application/Services/RuntimePhase.cs:32` emits these at Debug. The runner's daily Serilog file for October 14 is zero bytes; the nonempty supervisor log provided the bind. Phase durations, native append time, native read frontier and handler identity are therefore unavailable for the morning event. HostOccupancySamples at 07:00–07:03 report one local in-flight task and five server2 in-flight tasks; these are scheduling counts, not CPU, disk or thread-pool measurements.

## Passive live reproduction without sending input

From `20:12:05.376089Z` to `20:14:05.361191Z`, a local read-only subscriber watched runner `GET /events`. It retained only session, sequence, kind, UUID, producer timestamp and observer receipt time. It discarded raw output and transcript text. It observed 19 transcript events across four sessions, including four UserPrompts; all 19 later joined to stored rows. No live agent was spawned or messaged. The delayed Check was generated by normal existing automation for this task, not enqueued by the investigator.

The decisive sequence in Antiphon-Orchestrator session `c83e3c62…` is:

| Event | Producer UTC | Observer receipt UTC | DB CreatedAt UTC | Receipt to storage, s |
|---|---|---|---|---:|
| Prior TurnEnd, UUID `f6447b00-e5ae-4d16-a814-070282b94a56`, stored sequence 26028 | 20:13:01.251 | 20:13:01.527398 | 20:13:01.575049 | 0.047651 |
| New UserPrompt, UUID `45936423-62d1-49f5-b57c-cf8a02097fe0`, sequence 26031 | 20:13:02.932 | 20:13:03.401977 | 20:13:35.315945 | **31.913968** |
| AssistantText, UUID `66e91ea1-55a8-4713-9fc0-231e91594816`, sequence 26032 | 20:13:04.590 | 20:13:04.968365 | 20:13:35.315945 | **30.347580** |
| TurnEnd, same UUID, sequence 26033 | 20:13:04.590 | 20:13:04.968365 | 20:13:35.315945 | **30.347580** |

Queue row `633d758e-62a1-4759-845e-b95a3145f54a` was created at `20:12:39.767730Z` (Check, origin 4), started delivery at `20:13:01.678686Z` with baseline **26028**, and late-confirmed at `20:13:35.354776Z`. It had already been pending during the preceding turn. Delivery began **103.637 ms after** that TurnEnd's CreatedAt. `server/logs/antiphon-20261014_001.log:79419` and `:79468` show extra Enters at `20:13:10.201Z` and `20:13:17.460Z`; `:79553` shows timeout at `20:13:33.045Z`; `:79570` reports late confirmation at `20:13:35.358Z`. Producer-to-storage for the new prompt is **32.383945 s**, while producer-to-observer is **0.469977 s**.

The observer's socket is separate from the server's socket: it proves the runner had emitted the event, not when the server read it. Another session's row was stored during the interval; independent snapshot/persistence paths exist, so this is not a claim that the entire database or every ingestion path stopped.

A separate captured ToolCall, UUID `23054189-e986-47d7-a7e7-1ff0c4d94686`, illustrates why every lag must not be assigned to the same hop: producer-to-observer **15.042814 s**, observer-to-storage **0.004205 s**. Its append/flush, tailer and stream-buffer contributions cannot be separated without earlier timestamps. It does not explain the delayed UserPrompt above.

## Mechanism reconstructed from source and evidence

1. `server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:53` reads the stream sequentially and `:73` awaits each handler; `:420` awaits `ObserveTranscriptAsync`. Reconnect catch-up also awaits sessions sequentially (`:199`, `:204`).
2. `server/Application/Services/AgentSessionRuntime.cs:371` awaits SignalR before persistence. It persists at `:382`; a new TurnEnd then awaits the idle flush at `:402`. That flush awaits queue `OnTurnEndAsync` at `:649`.
3. `server/Application/Services/SessionMessageQueueService.cs:1561` awaits the session queue lock and `:1570` awaits delivery. Delivery awaits `DeliverAsync` at `:2575`; confirmation reads stored evidence at `:3950`. While awaiting that ordinary observable confirmation, the pump is not reading subsequent events.
4. The ordinary observable path does not periodically pull the runner: the pull gate is `fullInline || !observable` at `SessionMessageQueueService.cs:4083`. The failed-delivery grace does pull at `:4243`, through `AgentSessionRuntime.cs:724`, allowing the rows to be stored without the blocked live reader.

This is a wait dependency, not a file-watcher debounce. Delaying the verifier alone does not make an awaited pump consume the confirming event. Moving a native cursor cannot remove the new episode's measured post-runner interval.

The earlier server2 investigation ran `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs:29` in four controlled cases: held SignalR or queue-confirmation handlers, including reconnect catch-up. A second session was not read or persisted during the five-second holds and was persisted after release (`:110`, `:123`, `:139`). Those tests establish the inline dependency independently of the temporal inference in the live capture. All four cases passed, but that checkpoint's cleanup/receipt failed (`output-ledger-mismatch`, exit 2); this report does not relabel it a qualified green checkpoint or claim to have rerun it.

The morning Family event lacks a fresh retained TurnEnd immediately before delivery, so it must not be described as proven to have followed this exact same turn-end chain. A queue-lock wait, another inline handler, or other server-side contention remains possible there.

## Seven-day retained UserPrompt statistics

Fixed interval: **[2026-10-07 20:00:00Z, 2026-10-14 20:00:00Z)** by `TranscriptEntries.CreatedAt`. SELECTs ran with `PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=60000'` and selected no message/text/tool-input columns. Percentiles use PostgreSQL `percentile_cont`; counts above 10 s use strict `>10`.

The table below includes only nonnegative lag whose producer timestamp is within the interval. **Every row's size-at-bind group is `unknown`**: the binding metadata needed for the requested >10 MiB split was not recorded. Unknown is not a small-file group. Agent mapping uses the persisted `AgentSessions.StandingAgentId`; unowned/deleted mappings are retained as unmapped rather than guessed from cwd or names.

| Runner / agent | Prompts | Sessions | Median s | p95 s | Max s | >10 s |
|---|---:|---:|---:|---:|---:|---:|
| desktop / antiphon-check-interpreter | 1,619 | 1 | 0.338567 | 1.160644 | 7,560.666870 | 43 |
| desktop / Antiphon-Orchestrator | 3,454 | 1 | 0.463953 | 31.640419 | 27,478.821055 | 301 |
| desktop / AZ Care | 22 | 1 | 0.398449 | 32.334991 | 63.816041 | 4 |
| desktop / ClaudeBot-Antiphon | 187 | 2 | 0.359079 | 13.217610 | 259.290241 | 11 |
| desktop / Family | **55** | **1** | **0.548613** | **38.899015** | **61.906440** | **19** |
| desktop / Gym Stat Orchestrator | 168 | 1 | 0.565003 | 30.305928 | 142.664885 | 21 |
| desktop / markdown-package Orchestrator | 170 | 1 | 0.716088 | 30.708027 | 141.952775 | 21 |
| desktop / school-revision | 174 | 1 | 0.580100 | 30.184079 | 140.548168 | 18 |
| desktop / slides Orchestrator | 212 | 1 | 0.532302 | 30.253315 | 35.520022 | 24 |
| desktop / unmapped | 710 | 106 | 0.388658 | 22.664470 | 187.757700 | 58 |
| **desktop aggregate** | **6,771** | **116** | **0.423906** | **30.893568** | **27,478.821055** | **520** |
| server2 / unmapped | 1,026 | 706 | 0.411421 | 30.273907 | 50.332955 | 75 |
| server2-temp / unmapped | 338 | 245 | 0.470457 | 30.291435 | 57.158280 | 27 |

There are **8,165 retained rows** in the unfiltered interval: the table accounts for 8,135; another 29 server2 rows have negative lag (median -0.225013 s, range -0.456198 to -0.002481 s; clock/producer ordering not resolved), and one desktop check-interpreter row has a pre-window producer timestamp and lag 7,294.695425 s. No timestamp is null. The raw desktop population has 361 observations between 29 and 35 s. The aggregate percentile is computed from rows, not averaged from agent percentiles.

These are **producer timestamp to first-storage-stamp** statistics, not pure live-stream latency. CreatedAt is assigned before SaveChanges (`AgentSessionRuntime.cs:1186`, `:1230`, `:1267`); provider timestamp is not observed append time. History replay/backfill can exist even inside the time window. For example, maximum row `00de3a0e-3204-420d-9a49-f3f0a404c3bd`, orchestrator sequence 11234, has producer `2026-10-09T23:23:07.746Z` and CreatedAt `2026-10-10T07:01:06.567055Z`. The grace-window pull also shapes the distribution around 30 s. These percentiles therefore cannot independently prescribe a replacement verification timeout.

Family's later rows show recurrence without a recorded rebind: sequence 1151 at `17:40:45.826Z` lag 30.459679 s; 1164 at `17:41:33.835Z` lag 29.751375 s; 1240 at `19:30:18.198Z` lag 32.251388 s; 1263 at `19:59:21.877Z` lag 30.135361 s. Adjacent prompts often stored in under one second. All 55 metadata rows are retained in `family-prompts.csv`.

## Windows measurements without real sessions

Generated diagnostic `probe/Program.cs` uses the real runner's TranscriptTailer and event hub, with a process-local, uniquely created CLAUDE_CONFIG_DIR containing only synthetic files. It starts neither a provider nor a server. Known-file binding excludes discovery. It verifies matching prompt identity/kind and exact snapshot counts. All owned temporary histories were deleted on exit.

| Synthetic history bytes before prompt | Tailer start to event, ms |
|---:|---:|
| 0 | 123.361 |
| 25,646,334 (Family's current byte count) | 7,652.744 |
| 100,663,296 (96 MiB) | 30,635.828 |

| Concurrent warm tailers | Prompts | Median ms | p95 (nearest rank) ms | Max ms |
|---:|---:|---:|---:|---:|
| 1 | 32 | 233.090 | 291.299 | 292.706 |
| 16 | 512 | 231.720 | 282.072 | 307.308 |

Median here averages the middle two values. Low versus higher **synthetic tailer workload** was measured; a truly idle-host versus saturated-host comparison was not. The shared desktop kept its normal workload. A later point sample at `20:13:12.7593257Z` read CPU 53%, free physical memory 11,772,432 KiB out of 33,494,280 KiB; it neither characterizes the whole probe nor reconstructs morning load. Real file contents, directory scanning at desktop scale, provider flush, HTTP and server/database work are outside this synthetic measurement.

Final probe: **5 cases completed, 547 matching prompts, zero final case failures**. Three host-gated diagnostic builds ran: first failed with CS1705 (logging assembly 9/10 mismatch; 12 warnings, one error); second built with two dependency warnings, then its probe timed out at 90 s before completing the first case; after binding the diagnostic's System.* references to the runner's dependencies, the third build had zero warnings/errors and the probe completed. The timeout produced zero valid measurements and is retained as a harness failure, not application evidence. No production/test source was repaired. This is not a TUnit/checkpoint or mutation qualification claim.

Essential final run lines:

```text
BUILD SLOT granted lease=9486c263-fbcb-4ce2-95c3-321097e070ec waited=0s maxcpucount=4
PROBE started=2026-10-14T20:10:26.0809770Z os=Microsoft Windows NT 10.0.19045.0 cpus=8 runner=1.0.0.0
COLD historyBytes=0 startToHubMs=123.361 count=1
COLD historyBytes=25646334 startToHubMs=7652.744 count=1
COLD historyBytes=100663296 startToHubMs=30635.828 count=1
WARM lanes=1 samples=32 medianMs=233.090 p95NearestRankMs=291.299 maxMs=292.706
WARM lanes=16 samples=512 medianMs=231.720 p95NearestRankMs=282.072 maxMs=307.308
PROBE complete cases=5 failed=0
BUILD SLOT released lease=9486c263-fbcb-4ce2-95c3-321097e070ec held=59s
```

## Remaining uncertainty and proof obligation

- Exact 07:02 native file size, UUID offset, read frontier, append time and server phase owner were not recorded; present metadata cannot reconstruct them. A timestamped per-hop capture for a future matching event would resolve that attribution.
- The new live capture proves prompt availability outside the server stream reader well before storage, but its precise handler/lock wait is inferred. The prior controlled test proves the serial dependency. Do not label CPU, file scan or database latency as measured causes of the morning event.
- The >10 MiB-at-bind grouping cannot be recovered from current binding metadata. Future bind-size observations are needed; current file lengths would answer a different question.
- A validating regression must show a submitted prompt and a second session's transcript persisting while the first session's queue-confirmation handler remains pending, with one confirmed submission and no verification-timeout-driven catch-up requirement. Existing S0 supplies the blocked-handler baseline; the passive UUIDs supply the real failure shape. No implementation has been tested, so removal of the original incident's exact delay is not claimed as a verified fix.

## Evidence custody and rerun

Evidence root: `C:\src\Antiphon\.antiphon\c1259-desktop-evidence-bd33cc44` (gitignored). SQL files use SELECTs only; their invocations enforce database read-only mode. Files include `lag-by-agent.sql/.csv`, `family-prompts.sql/.csv`, `incident-rows.sql/.csv`, `queue-rows.sql/.csv`, `file-stat-and-bind.json`, `runner-build.json`, selected log lines, `passive-runner-metadata.jsonl`, `passive-join.sql/.csv`, diagnostic source and build/run logs. An initial passive-join formatter rejected timestamp/UUID formats before executing SQL; the corrected formatter preserves ISO timestamps and accepts the observed Codex metadata UUID. No incomplete query result is used.

| Evidence | SHA-256 |
|---|---|
| `lag-by-agent.csv` | `e0183d468beb4eeab4c8f6e1089592f26953cd079e26f3b8027d9525efa3f049` |
| `file-stat-and-bind.json` | `7ccc122ba7b84fcba97c6cc888d3fda84295a22123a1784d33cda00117909d1e` |
| `passive-runner-metadata.jsonl` | `32d1d072fbca37383d7c7708e1c9120bfe78e198063c9f56210ea32beba71620` |
| `passive-join.csv` | `61593b3c694af1c70a7f0681aa38602e079303c7b09da47af658dd0bdff12cc4` |
| `probe-run-2.log` | `297a876a945767bcda97c8813b4beac57d77182214de0388079539ca777425d8` |

Rerun the statistical SELECT with PowerShell:

```powershell
Get-Content -Raw -LiteralPath '.antiphon/c1259-desktop-evidence-bd33cc44/lag-by-agent.sql' |
  docker exec -i -e PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=60000' antiphon-postgres psql -X -U antiphon -d antiphon --csv -v ON_ERROR_STOP=1
```

The fixed-window population can change as retention prunes rows; the captured CSV is the observation. Revalidate the referenced runner DLL hash before rerunning the diagnostic:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1259-desktop-probe-build -SlotWaitMinutes 3 -- dotnet build .antiphon/c1259-desktop-evidence-bd33cc44/probe/Probe.csproj --configuration Release --property:OutputPath=bin-c1259-probe/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1259-desktop-probe-run -SlotWaitMinutes 3 -- dotnet .antiphon/c1259-desktop-evidence-bd33cc44/probe/bin-c1259-probe/Antiphon.SessionRunner.Tests.dll
```

Cleanup was attempted only after the diagnostic exited and provenance was recorded. Both paths were verified as ordinary directories inside the owned evidence root, with no descendant reparse points. Automatic approval review rejected the guarded cleanup and then the narrower literal `Remove-Item -LiteralPath 'C:\src\Antiphon\.antiphon\c1259-desktop-evidence-bd33cc44\probe\bin-c1259-probe' -Recurse -Force`, each with reason **"blocked by policy"**. No deletion ran. Task-owned `probe/bin-c1259-probe` and `probe/obj` remain gitignored for caller cleanup; no alternate deletion mechanism was attempted.

No commit/push: the caller explicitly commissioned READ-ONLY work in the shared checkout, which is on `master`; no permitted task branch was assigned. No branch was created or switched. The evidence Markdown is the only new nonignored deliverable; existing unrelated patch files were left alone.

## Not done, noted

Recommend planning the smallest change that lets transcript persistence continue while queue confirmation waits, proved by the pending-handler regression above; it removes the demonstrated wait dependency, whereas a native-tail cursor or timeout-only change cannot remove the measured post-runner delay; no fix was designed or implemented.

--- next stage ---
next: plan
handoff: Plan removal of the confirmed serial-pump/queue-confirmation dependency using the live 31.914 s post-runner delay and existing S0 reproduction; retain Family's exact morning handler and bind-size uncertainty, and prove persistence continues while confirmation is pending.
artifact: docs/investigations/2026-10-14-card-1259-live-transcript-ingestion-lags-the-cli-by-28-s-delivery-verifier-presses-enter-again-and-fails-at-30-s-desktop-evidence.md
