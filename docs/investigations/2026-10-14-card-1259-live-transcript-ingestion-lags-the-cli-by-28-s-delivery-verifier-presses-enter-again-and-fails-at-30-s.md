# CARD-1259: live transcript ingestion lags the CLI by 28 s; delivery verifier presses Enter again and fails at 30 s

Investigation by task `baa53a4a`, 2026-10-14 UTC. Source baseline and initial fetched
`origin/master`: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`.
Evidence only; no production implementation, configuration, deployment or chat input.

## Status

The original prompt's producer-to-database-stamp interval is **30.541032 seconds**;
the assistant/TurnEnd interval is **27.777032 seconds**. These are not measurements
of the file watcher alone. The exact blocking hop in this incident is unconfirmed.
Two mechanisms are now reproduced: draining 96 MiB of existing native history took
**30.363466 seconds** in the real tailer; a held inline server handler prevented a
second session's transcript from reaching PostgreSQL. Neither reproduction identifies
which hop delayed the incident. Next: **Investigate on the desktop**, with the native
file size/binding timeline, runner/server traces and a read-only seven-day database export.

The Debug task recorded incident server revision
`893ddfceeebf4d13d9401848c535abada3983c27`. The current `/api/version` returned
`5665ddb1a3ba730f3b858acc109e65b5d7304d6c`. The tailer and HTTP event-pump source
files have no diff between those revisions; the runtime's only diff is the
CARD-1247 late-confirmed reply-dispatch call. This does not establish the incident
runner's separately deployed build identity.

## Stored incident evidence

Retrieved `GET /api/agent-tasks/963621db` and the complete retained transcript for its
session `22e8899d-1c52-464e-a803-d312bc624dc8` (100 entries; continuation after 100
returned empty). Its ToolCall 51 and ToolResult 52 contain the original desktop SQL
and output, not merely the Debug task's final interpretation:

Session `fb38e751-dfbd-4b8d-9ce5-ef84d1828461`, `TranscriptEntries`:

| Sequence | Kind | Producer timestamp UTC | CreatedAt UTC | Difference, seconds |
|---|---|---|---|---:|
| 1124 | TurnEnd | 2026-10-12 21:50:16.347 | 2026-10-12 21:50:43.388964 | 27.041964 |
| 1125 | UserPrompt | 2026-10-14 07:02:16.815 | 2026-10-14 07:02:47.356032 | 30.541032 |
| 1126 | AssistantText | 2026-10-14 07:02:19.579 | 2026-10-14 07:02:47.356032 | 27.777032 |
| 1127 | TurnEnd | 2026-10-14 07:02:19.579 | 2026-10-14 07:02:47.356032 | 27.777032 |

ToolResult 46 additionally identifies prompt UUID `f3e88a7d-51f4-4db1-b691-89827485b2be`
and assistant/TurnEnd UUID `0d928d08-9840-4b94-ac37-368c0010c690`.
ToolResult 52 records queue message `fc2286e9-2811-4537-b68e-0bfcbf872567`:
baseline 1124, attempts 1, LastDeliveryStartedAt 07:02:15.577028Z,
SentAt/DeliveryVerdictAt 07:02:47.371093Z, verdict 8 (LateConfirmed).

ToolCall 40 / ToolResult 41 preserve lines from desktop
`C:\src\Antiphon\server\logs\antiphon-20261014.log` (original log zone +01:00):
line 133107: second Enter at 07:02:24.001Z; line 133171: third Enter at
07:02:31.247Z; line 133277: 30-second verification failure at 07:02:47.189Z;
line 133278: late confirmation at 07:02:47.371Z. Those excerpts are truncated
by the original capture and are not a complete pump trace.

The same CreatedAt for 1125-1127, immediately after the timeout and immediately
before late confirmation, is consistent with the grace-window catch-up batch.
CreatedAt is assigned before SaveChanges, not a database commit timestamp.
The native timestamp is not an observed filesystem append/flush time.

The failure-to-CreatedAt interval is 167.032 ms, followed by 15.061 ms to
late confirmation. This **does not** measure the duration of the pull alone:
there is no captured pull-start timestamp. The 30.541-second observation is
affected by the scheduled recovery pull, not a natural live-stream latency sample.

## Hop inventory and measured mechanisms

All file:line references below are in this repository at the recorded source.

| Hop | Code and configured numbers | Incident measurement / limit |
|---|---|---|
| CLI timestamp to native file append | `src/Antiphon.SessionRunner/TranscriptNormalizer.cs:375` parses the provider timestamp; it does not record append time. | Unknown; 07:02:16.815Z is the record's timestamp. |
| Locate/bind | `TranscriptTailer.cs:39`, `:127`, `:563`, `:641`: 250 ms locate poll, 10 s exact-ID grace, direct known-sidecar bind when available. | Bind method/start/end not captured. A grace value is not a bound on scanning or I/O. |
| Bound file read | `TranscriptTailer.cs:38`, `:366`, `:407`, `:416`, `:440`: start at byte zero, at most 1 MiB/read, then 300 ms wait even with unread bytes. | 96 MiB of prior bytes reproduced 30.363466 s to the target prompt; original native file size/offset unknown. |
| Complete-line normalization | `TranscriptTailer.cs:485`, `:504`, `:529`: newline gates parsing, then immediate hub publish; no FileSystemWatcher or debounce timer in this path. | Zero configured debounce, not zero processing time. Partial line and I/O delay are unmeasured for the incident. |
| Periodic fork scan | `TranscriptTailer.cs:44`, `:391`, `:882`: scan every 10 s, inline before the read; enumerate candidate files and check content. | No incident directory size or scan duration. The small synthetic directory does not qualify a large desktop provider store. |
| Hub to runner HTTP stream | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:4439`, `:4475`; `src/Antiphon.SessionRunner/Program.cs:384`: unbounded subscriber queue, await event, write available events and flush; default keepalive 15 s. | The 15 s keepalive is not an event polling interval. No captured runner emission / HTTP receipt timestamps. |
| HTTP stream to server handler | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:894`; `SessionRunnerEventPump.cs:53`, `:73`, `:199`, `:420`: read serially, await handler; reconnect catch-up also serial. | S0 blocked a second session for the five-second hold; exact incident blocker unknown. |
| Handler to durable evidence | `server/Application/Services/AgentSessionRuntime.cs:371`, `:382`, `:402`, `:649`: SignalR publish precedes persistence; boundary handling awaits queue confirmation. `:1186`, `:1230`, `:1267`: stamp CreatedAt before SaveChanges. | Producer-to-stamp 30.541032 s / 27.777032 s. Network, DB acquisition, save and scheduling contributions not separated. |
| Verification | `server/Application/Settings/SupervisionSettings.cs:317`, `:355`, `:361`, `:369`, `:398`: 500 ms checks, 30 s confirm window, 7 s re-Enter, 3 Enters total, 20 s post-failure grace. | Recorded extra Enters 7.186 s and 14.432 s after the prompt timestamp; timeout 30.374 s after it. |
| Host pressure | `docs/testing-and-build.md:780`: 5 s sampling, 360 samples / 30 minutes, memory only. | The morning desktop CPU, disk, memory and thread-pool pressure are not retained here; “loaded” is a card claim, not a measured contribution. |

`TranscriptTailer.cs` in this table means
`src/Antiphon.SessionRunner/TranscriptTailer.cs`; the other shortened server
filenames use the fully named directory in their same row.

### File-to-event characterization (CP-1)

| Concurrent bound tailers | Synthetic prompts | Median (upper-middle), ms | p95 (nearest rank), ms | Maximum, ms |
|---:|---:|---:|---:|---:|
| 1 | 32 | 228.602 | 284.601 | 398.212 |
| 16 | 512 | 228.466 | 281.469 | 313.579 |

Measured by `tests/Antiphon.SessionRunner.Tests/TranscriptIngestionLatencyProbeTests.cs`
(`Appended_prompts_reach_the_event_hub_under_concurrent_file_load`): stopwatch
before each append through the matching hub event, with exact UUID/kind/body and
snapshot-count checks. All 544 prompt checks completed. The probe reports the
upper-middle observation for even sample sizes, not the mean of the two middle values.
Its input cadence is controlled, not a random sample of real traffic.

A nearby host sample at 19:43:14.068Z recorded Linux, 24 logical CPUs, load averages
31.78 / 14.79 / 7.97 and MemAvailable 100674104 kB. Thus “one tailer” means low
test workload on a busy shared host. **No idle-host versus loaded-desktop comparison
was obtained.** This experiment also excludes discovery, native CLI flush, HTTP,
server work and database storage; it cannot certify those hops.

### Cold-history characterization (CP-3)

`TranscriptIngestionLatencyProbeTests.cs:20` builds a known-bound file from valid,
ignored `progress` records, then one literal target prompt. No provider is launched.

| Prior bytes | History | Tailer start to matching event |
|---:|---:|---:|
| 0 | 0 MiB | 176.253 ms |
| 100663296 | 96 MiB | 30363.466 ms |

Exactly one normalized UserPrompt was observed in each case. A native transcript can
contain bytes that emit no normalized rows (`TranscriptNormalizer.cs:76`), so the
incident's 1124 prior normalized entries do not establish its native byte size.
The coded 96 waits total 28.8 s before read/parse/scheduling overhead. This demonstrates
a mechanism with the observed order of delay; it is not evidence that the original
file contained 96 MiB or that the tailer was still reading history at 07:02:16Z.

### Serial server-pump characterization (CP-2)

The existing `tests/Antiphon.Tests/Application/SessionRunnerEventPumpTests.cs:29`
uses the hosted pump, a scripted in-memory runner stream and isolated PostgreSQL.
Its five-second observation deadline is in
`SpecialistTaskRunnerDeadlineTests.cs:179`. While A is held, B is neither pulled
from the live enumerator nor stored; after release B is stored and its marked task
settles (`SessionRunnerEventPumpTests.cs:110`, `:123`, `:139`, `:144`).

| Held operation | Reconnect catch-up | Throwing diagnostics | Logged held phase, ms | B persisted while held |
|---|---|---|---:|---|
| SignalR publication | no | no | 5012.012 | false |
| Queue delivery confirmation | no | no | 5756.9467 | false |
| Queue delivery confirmation | yes | no | 5569.9733 | false |
| SignalR publication | no | yes | 5002.0744 | false |

These deliberately held phases establish a dependency, not the production latency
distribution. Traces identify B by UUID; for example, queue-confirm/live used
`28cba591cbea4b1b813f65eefa1a3ec7` in session
`c6077610-f934-49e3-a019-efced5420ab1` and ended with
`persistedWhileBlocked=False`. This mechanism was already recorded in
`docs/investigations/2026-09-07-card-0432-s0-s2-evidence.md:9`.
CARD-0432's retrieved close reason explicitly defers S3; its Done status does not
mean that inline pump blocking was removed.

## Extra Enters and the 30-second window

For **this incident**, the stored SQL selected all entries from 1124 onward and
returned exactly one new UserPrompt, one AssistantText and one TurnEnd. No duplicate
submission is present in that retained capture. The re-press path sends only `"\r"`
(`SessionMessageQueueService.cs:4120`); it does not retype the body. The queue lock
serializes its delivery path (`:1559`, `:1560`, `:1569`). An accepted prompt followed
by an unchanged empty composer therefore has no second body for this path to submit.

The repository's real-provider check is
`tests/Antiphon.Agents.Pty.Tests/ClaudeSubmitConfirmCanaryTests.cs:57`, asserting
unchanged native user/assistant counts at `:106`. It was **not run here**. A fake
test or a source comment would not prove the incident CLI version's behavior.
Human/raw input, a nonempty composer or a picker is outside the empty-composer
assumption. Universal double-submit safety is not established by this incident.

For an ordinary observable baseline, `SessionMessageQueueService.cs:3950` queries
stored evidence; the main loop's pull is limited to full-inline Check or an
unobservable baseline (`:4083`). The observable path retains its Enter schedule
even after output advances (`:4094`). Only the post-failure grace path necessarily
pulls for this ordinary delivery (`:4243`), through
`AgentSessionRuntime.CatchUpTranscriptAsync` (`:715`, `:724`).

The recorded failure proves that the fixed window produced a false negative here.
It does **not** establish a replacement timeout or a latency-tracking rule. The
source's “runner event pump is sub-second” comment
(`SupervisionSettings.cs:350`) is not supported as a general bound by the blocked
pump reproduction. Waiting longer cannot consume a confirming event while the
same awaited handler prevents the pump from reading it. A distribution partly
determined by timeout-triggered pulls cannot independently justify that timeout.

## Verification scope

The new tailer probe writes only generated files under an owned temporary
CLAUDE_CONFIG_DIR. It measures monotonic time before append through receipt from
the real TranscriptTailer / SessionRunnerEventHub, with one and sixteen concurrent
tailers. It checks all prompt identities and counts. It is a characterization,
not a latency SLA, CPU saturation test, Windows qualification or complete server
ingestion measurement. Existing S0 uses the real hosted server pump with an
in-memory runner, isolated PostgreSQL and a deliberately blocked side effect.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | evidence | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-tailer/` | tailer-measurement | `/*/*/TranscriptIngestionLatencyProbeTests/Appended_prompts_reach_the_event_hub_under_concurrent_file_load` | file-to-hub characterization | 2 parameter rows, 0 failed/skipped | 2 | 5 |
| CP-2 | evidence | `tests/Antiphon.Tests -> bin-c1259-pump/` | blocked-pump | `/*/*/SessionRunnerEventPumpTests/S0_blocked_hosted_pump_probe` | existing serial-pump reproduction | 4 parameter rows, 0 failed/skipped | 4 | 8 |
| CP-3 | cold-tail | `tests/Antiphon.SessionRunner.Tests -> bin-c1259-cold/` | cold-history-measurement | `/*/*/TranscriptIngestionLatencyProbeTests/Cold_tail_records_latency_to_a_prompt_after_existing_history` | cold-tail backlog candidate | 2 parameter rows, 0 failed/skipped | 2 | 5 |

CP-3 was added after source inspection found an additional candidate: the tailer
starts at offset zero (`TranscriptTailer.cs:366`), reads at most 1 MiB (`:416`),
and waits 300 ms after each read (`:440`). With 96 MiB before a prompt, 96 waits
alone amount to 28.8 seconds. This is a new measurement, not a repeat of a passing
row. The incident's native file size and tail offset are not known.

CP-1/CP-2 run `20261014-194223-674c` at
`b33daf90bbfc028900b24d5f12350b3992fb35a4`: 2/2 and 4/4 test cases passed, zero
failed/skipped. Overall checkpoint **RED, exit 2**, because the S0 test created
`bin-c1259-pump/TestOutput/card-0432/s0` after the compiler output ledger was
captured; cleanup refused `output-ledger-mismatch`. Both the tool validator and
`scripts/validate-checkpoint-receipt.ps1` exited 2, `cleanup_refused`. The raw
receipt remains unchanged. Traces were copied to ignored
`.antiphon/c1259-evidence/pump-traces/`, and only the run-created
`tests/Antiphon.Tests/bin-c1259-pump` was manually removed by literal path.
These counts are diagnostic evidence, not a qualified green checkpoint.

CP-3 run `20261014-195453-5d88` at
`9df6e9cd9a896dcadc54692d7b6c7330bc1444bc`: 2/2 passed, zero failed/skipped;
checkpoint exit 0, output deleted. Both validators exited 0 and printed:
`CHECKPOINT SOURCE VALID source=9df6e9cd9a896dcadc54692d7b6c7330bc1444bc rows=1`.
Across both runs: **8 test executions passed, 0 failed, 0 skipped**; the first
run's cleanup/receipt failure remains a disclosure and has not been relabeled green.
Both runs used rowTimeoutMinutes 40, totalTimeoutMinutes 150, serial execution,
build-slot grants, Linux UseAppHost=false and keepOutputs=false. No other tests or
builds were added outside the checkpoint runs. No production source was modified.

To reproduce the two selections on a checkout of the corresponding recorded source,
use the artifact's `### Checkpoints` table with:

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-artifact> --after evidence --row-timeout 40m --total-timeout 150m --serial --expected-source-sha b33daf90bbfc028900b24d5f12350b3992fb35a4
dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-artifact> --after cold-tail --row-timeout 40m --total-timeout 150m --serial --expected-source-sha 9df6e9cd9a896dcadc54692d7b6c7330bc1444bc
```

Mutation was not run. Positive controls for the new probes remain **pending**:
normalized UserPrompt kind/body corruption and omission from the snapshot-count
oracle. Existing S0 controls were not requalified. No PC-clean claim is made.

## Disclosures and remaining evidence

Desktop log retrieval through `scripts/logs.ps1 -Source desktop-server -Tail 1`
failed because `/work/repos/antiphon/server/logs/antiphon-*.log` does not exist in
the Linux mirror. Original desktop runner logs and incident-time host samples
have not been retrieved. The public transcript DTO omits CreatedAt, so a seven-day
ingestion distribution cannot be computed from that endpoint. The four stored SQL
rows above are a selected incident sample, not a seven-day population.

The previous 27.041964-second TurnEnd sample shows that a comparable stored interval
occurred on October 12. Two selected turns do not establish frequency, median, p95,
maximum or whether the incident is an outlier. **Seven-day statistics are not measured.**
`TranscriptEntry.CreatedAt` exists (`server/Domain/Entities/TranscriptEntry.cs:87`)
but is absent from `server/Application/Dtos/SessionTranscriptDto.cs:9`.
`docs/logs.md:11`, `:76` specify five-day server-log retention and seven-day normalized
row retention; a complete seven-day log population must not be assumed available.

The next investigation needs, on the desktop:

1. Original runner binding/rebinding/read failures and server event/reconnect lines
   for 07:00:40-07:04:20Z on October 14, for session
   `fb38e751-dfbd-4b8d-9ce5-ef84d1828461`. Use `docs/logs.md`'s desktop-server and
   session-runner sources; retain their original zone and file/line provenance.
   The debug capture did not include these runner logs. Runtime phase diagnostics
   are Debug-level (`server/Application/Services/RuntimePhase.cs:32`); absence from
   an Information-level log cannot establish that no handler was blocked.
2. Only that known session's native transcript: byte length, target UUID byte offset,
   and binding/start/read frontier, plus the actual runner build. If that frontier
   had already passed the prompt before the timeout, cold replay is excluded for
   this event. If the target was behind tens of MiB during the observed period,
   quantify the chunk waits from its real offset instead of the synthetic 96 MiB.
3. Correlated runner-publish, HTTP-read, handler-start and save-complete times, or a
   new isolated Windows capture on an idle and a loaded host. A single shared trace
   must distinguish a prompt already present in the runner snapshot from a pending
   native read and from an event waiting behind a server handler. No real-chat input.
4. A read-only retained-row export for the seven-day window, with session/UUID/kind,
   provider timestamp and CreatedAt; separate resumed-history/backfill and clock
   anomalies. Do not label raw first-storage delay as live-stream-only lag.

The following **unexecuted** metadata-only SQL is a starting collection recipe for
the documented desktop Postgres connection. Run it in a read-only transaction; it
reports coverage and separates null/future/pre-window producer timestamps. It cannot
identify all replayed history within the window or split out runner/server hops.
No text, tool input or credentials are selected.

```sql
BEGIN READ ONLY;
SET LOCAL statement_timeout = '30s';
WITH bounds AS (
  SELECT now() AS hi, now() - interval '7 days' AS lo
), samples AS (
  SELECT t."AgentSessionId", s."AgentKind", t."Kind", t."CreatedAt",
         date_trunc('day', t."CreatedAt" AT TIME ZONE 'UTC') AS day_utc,
         extract(epoch FROM (t."CreatedAt" - t."Timestamp")) AS lag_seconds,
         CASE WHEN t."Timestamp" IS NULL THEN 'missing_timestamp'
              WHEN t."Timestamp" > t."CreatedAt" THEN 'negative_lag'
              WHEN t."Timestamp" < b.lo THEN 'prewindow_history'
              ELSE 'within_window' END AS cohort
  FROM "TranscriptEntries" t
  JOIN "AgentSessions" s ON s."Id" = t."AgentSessionId"
  CROSS JOIN bounds b
  WHERE (s."RunnerId" IS NULL OR s."RunnerId" = 'desktop')
    AND t."CreatedAt" >= b.lo AND t."CreatedAt" < b.hi
)
SELECT day_utc, "AgentKind", "Kind", cohort, count(*) AS rows,
       count(DISTINCT "AgentSessionId") AS sessions,
       min("CreatedAt") AS first_stored, max("CreatedAt") AS last_stored,
       percentile_cont(0.50) WITHIN GROUP (ORDER BY lag_seconds) AS median_s,
       percentile_cont(0.95) WITHIN GROUP (ORDER BY lag_seconds) AS p95_s,
       max(lag_seconds) AS max_s
FROM samples
GROUP BY day_utc, "AgentKind", "Kind", cohort
ORDER BY day_utc, "AgentKind", "Kind", cohort;
ROLLBACK;
```

Do not average daily percentiles to obtain the seven-day result; aggregate the raw
eligible rows for that result after the replay/clock classification is reviewed.
The unknown incident hop, missing seven-day distribution, absent incident-time host
metrics, missing idle/loaded Windows comparison and unrun current-provider canary
are disclosures, not additional defect findings.

## Evidence custody

Ignored captures are under `.antiphon/c1259-evidence/`; checkpoint JSON, logs and TRX
remain under `.antiphon/checkpoints/20261014-194223-674c/` and
`.antiphon/checkpoints/20261014-195453-5d88/`. They are not committed and may be lost
when the worktree retires. Essential values and the original checkpoint lines are
preserved here; the incident tool rows remain retrievable through the named Debug
session while transcript retention permits.

| Local capture | SHA-256 |
|---|---|
| `debug-task.json` | `b6313fd707322d695a43746251efd7b2595996fdee5bdb0cabccc8143d78b253` |
| `debug-transcript-all.json` | `d511d1681c91d1360d6d34052a57112aeb9a80e6a8b0482d094b445f66eabd2a` |
| `version.json` | `34f8a38dc69fdf4ef8fc7a514570acd6e6e20c60486b35339b7c1a99628ae1b3` |
| `card-0432.json` | `cc76ec328746e3fe35fb9b8a688c290d87f24beaf2e10fba00c6f753d9ae8373` |

## Not done, noted

No fix designed or implemented; distinguish cold-history draining from the existing CARD-0432 blocked-pump mechanism before choosing a verifier change.

## Original checkpoint lines

```text
CHECKPOINT CP-1 commit=b33daf90bbfc028900b24d5f12350b3992fb35a4 build=ok filter=/*/*/TranscriptIngestionLatencyProbeTests/Appended_prompts_reach_the_event_hub_under_concurrent_file_load executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-baa53a4a/.antiphon/checkpoints/20261014-194223-674c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b33daf90bbfc028900b24d5f12350b3992fb35a4 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=43.0574347s startup=1.7503832s testsWall=19.4693292s teardown=0.4786819s hostWall=21.6984s
CHECKPOINT CP-2 commit=b33daf90bbfc028900b24d5f12350b3992fb35a4 build=ok filter=/*/*/SessionRunnerEventPumpTests/S0_blocked_hosted_pump_probe executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-baa53a4a/.antiphon/checkpoints/20261014-194223-674c/rows/CP-2/run.trx slot=granted waited=105s dirty=0 source=b33daf90bbfc028900b24d5f12350b3992fb35a4 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=105s build=333.2899389s startup=61.0909439s testsWall=34.3026876s teardown=2.2354384s hostWall=97.62907s
CHECKPOINT CP-3 commit=9df6e9cd9a896dcadc54692d7b6c7330bc1444bc build=ok filter=/*/*/TranscriptIngestionLatencyProbeTests/Cold_tail_records_latency_to_a_prompt_after_existing_history executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-baa53a4a/.antiphon/checkpoints/20261014-195453-5d88/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9df6e9cd9a896dcadc54692d7b6c7330bc1444bc sourceState=clean buildSource=verified
PHASES CP-3 slotWait=15s build=31.8585437s startup=2.0270282s testsWall=31.1667962s teardown=0.4218947s hostWall=33.6157259s
```
